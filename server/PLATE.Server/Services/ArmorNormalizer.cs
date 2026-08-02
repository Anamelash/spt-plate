using System.Text;
using System.Text.RegularExpressions;
using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace PLATE.Server.Services;

/// <summary>
/// Armour construction: matches the armour in the database against the reference book
/// of real products and reports what is covered.
///
/// The game describes armour by a class number and a material. The class is a
/// consequence of the construction rather than its cause, and the one physical
/// quantity that decides everything — how thick the hard element actually is — exists
/// nowhere in the data. This pass is where that gets attached, keyed by the product
/// the item is modelled on rather than by its class.
///
/// For now it only corrects materials that are wrong and writes the coverage report;
/// the thickness it resolves is what the ballistic-limit model will consume.
/// </summary>
[Injectable]
public class ArmorNormalizer(
    DatabaseServer databaseServer,
    ReferenceBook referenceBook,
    ISptLogger<ArmorNormalizer> logger)
{
    /// <summary>One-line result for the startup summary; null if the module did not run.</summary>
    public string? Summary { get; private set; }

    /// <summary>Resolved thickness per armour template id — what stage three will need.</summary>
    public IReadOnlyDictionary<string, double> ThicknessByTemplate => _thickness;

    private readonly Dictionary<string, double> _thickness = new();

    /// <summary>
    /// "6b5-16_level3_soft_armor_front" and "granit4_5class_back" both name a product.
    /// No word boundary after the marker: what follows it is an underscore, which is a
    /// word character, so \b never matches there and the cut silently never happens.
    /// </summary>
    private static readonly Regex ProductCut =
        new(@"_(level\d+|\d+\s*class).*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Plate face area by inventory footprint, mm². The grid is the only size the game
    /// carries, so it maps onto the standard plate sizes it is drawn from: a side plate,
    /// a square plate, a SAPI-sized one. Only used to turn mass into thickness, and a
    /// plate that lands in the wrong bucket is off by the ratio of two real plate sizes,
    /// not by an order of magnitude.
    /// </summary>
    private static readonly Dictionary<string, double> PlateAreaMm2 = new()
    {
        ["1x1"] = 152 * 152.0,  // small square insert
        ["2x1"] = 203 * 152.0,  // side plate, 8x6 in
        ["2x2"] = 254 * 254.0,  // 10x10 in
        ["2x3"] = 254 * 318.0,  // 10x12.5 in, SAPI cut
    };

    /// <summary>Where an item's construction came from, in descending order of trust.</summary>
    private enum Origin
    {
        /// <summary>The real product, from published specifications.</summary>
        Product,

        /// <summary>The reference construction for its material and rating.</summary>
        Reference,

        /// <summary>Worked out from the item's own mass.</summary>
        Mass,

        /// <summary>Nothing applied; it behaves exactly as the game shipped it.</summary>
        None,
    }

    /// <summary>
    /// What kind of armour an item is. The four are made by different people out of
    /// different things — a helmet shell, a rifle plate, the package sewn into a
    /// carrier, and a mask or a pair of glasses — and reading them apart is the only
    /// way to see which of them the reference book still has nothing real to say about.
    /// </summary>
    public enum Kind
    {
        Helmet,
        Plate,
        VestComponent,
        Other,
    }

    private sealed class Row
    {
        public required string Product;
        public required string Material;
        public Kind Kind;
        public int Class;
        public int Zones;
        public double ThicknessMm;
        public string Prototype = "";
        public string Source = "";
        public string MaterialWas = "";
        public Origin From = Origin.None;

        /// <summary>Which reference answered, e.g. "Aramid/2". Empty unless From is Reference.</summary>
        public string ReferenceKey = "";

        /// <summary>Rating the item declares, when the material's ceiling overrode it.</summary>
        public int CappedFrom;

        /// <summary>What the search for this one turned up, when it was not a figure.</summary>
        public string Note = "";
    }

    /// <summary>A reference entry together with the key that found it, for the report.</summary>
    private sealed record Resolved(ReferenceBook.ArmorPlateRef Ref, string Key, int CappedFrom);

    public void Run(PlateServerConfig cfg, string modPath)
    {
        var items = databaseServer.GetTables().Templates?.Items;
        if (items == null)
        {
            logger.Error("[PLATE] ArmorNormalizer: item DB unavailable");
            return;
        }

        var reference = referenceBook.Load(modPath);
        if (reference.ArmorMaterials.Count == 0)
        {
            logger.Error("[PLATE] ArmorNormalizer: no material reference data, skipped");
            return;
        }

        var known = new Dictionary<string, Row>();
        var unknown = new Dictionary<string, Row>();

        foreach (var item in items.Values)
        {
            var p = item.Properties;
            var material = p?.ArmorMaterial?.ToString();
            if (p == null || string.IsNullOrEmpty(material) || (p.ArmorClass ?? 0) <= 0)
            {
                continue;
            }

            // no documented product, but a plate carries its own mass, and mass over
            // density over face area is a thickness. Most of the armour here is invented
            // for the game and has no specification to look up — this is the only honest
            // number available for it, and it is still physics rather than a guess
            var itemName = item.Name ?? "";
            var product = Product(itemName);
            var spec = ProductSpec(reference, itemName, product, out var specKey);
            var cls = (int)(p.ArmorClass ?? 0);

            // an entry with no thickness is not a documented product — the maker says
            // what it stops, or what it is made of, and nothing about how much of it
            // there is. Both are still worth more than the game's own answer: the
            // reference is read at their rating, and out of their material
            var stated = spec is { ThicknessMm: <= 0 } ? spec : null;
            spec = stated == null ? spec : null;
            var rating = stated is { Rating: > 0 } ? stated.Rating : cls;

            var was = "";
            if (stated != null && stated.Material.Length > 0 && stated.Material != material)
            {
                was = material;
                material = stated.Material;
                p.ArmorMaterial = Enum.TryParse<SPTarkov.Server.Core.Models.Enums.ArmorMaterial>(
                    material, out var known2) ? known2 : p.ArmorMaterial;
            }

            // a plate the game invented has no product to look up, but there is a real
            // plate of the same rating doing the same job, and that is what it stands in
            // for. Mass only decides it when even that is missing
            var byClass = spec == null
                ? ClassReference(reference, itemName, material, rating, cls)
                : null;
            var derived = spec == null && byClass == null
                ? DeriveThickness(itemName, p, material, reference)
                : 0;

            var target = spec != null || byClass != null || derived > 0 ? known : unknown;

            // a documented product is one plate however many zones wear it, but two
            // plates that merely share a product name are two plates: "granit4" covers a
            // front and a side of different mass, and collapsing them keeps whichever
            // was read last
            var perItem = derived > 0 || byClass != null;
            var rowKey = perItem ? itemName : spec != null ? specKey : product;

            if (!target.TryGetValue(rowKey, out var row))
            {
                row = new Row
                {
                    Product = perItem ? Shorten(itemName) : rowKey,
                    Material = material,
                    MaterialWas = was,
                    Kind = Classify(itemName),
                    Class = cls,
                    Note = Note(reference, itemName, product),
                };
                target[rowKey] = row;
            }

            row.Zones++;

            if (spec == null)
            {
                if (byClass != null)
                {
                    row.From = Origin.Reference;
                    row.ReferenceKey = byClass.Key;
                    row.CappedFrom = byClass.CappedFrom;
                    row.Prototype = stated?.Prototype.Length > 0 ? stated.Prototype : byClass.Ref.Prototype;
                    row.ThicknessMm = byClass.Ref.ThicknessMm;
                    row.Source = byClass.Ref.Source;
                    _thickness[item.Id] = byClass.Ref.ThicknessMm;

                    if (stated != null)
                    {
                        row.Note = stated.Source;
                    }
                }
                else if (derived > 0)
                {
                    row.From = Origin.Mass;
                    row.ThicknessMm = derived;
                    row.Source = $"{p.Weight ?? 0:N2} kg over a {p.Width}x{p.Height} face";
                    _thickness[item.Id] = derived;
                }

                continue;
            }

            row.From = Origin.Product;
            row.Prototype = spec.Prototype;
            row.Source = spec.Source;
            row.ThicknessMm = spec.ThicknessMm;
            _thickness[item.Id] = spec.ThicknessMm;

            // the game is right about the material far more often than not, so this
            // corrects the exceptions rather than overriding everything
            if (spec.Material.Length > 0 && spec.Material != material)
            {
                row.MaterialWas = material;
                row.Material = spec.Material;
                p.ArmorMaterial = Enum.TryParse<SPTarkov.Server.Core.Models.Enums.ArmorMaterial>(
                    spec.Material, out var parsed) ? parsed : p.ArmorMaterial;
            }
        }

        WriteReport(modPath, reference, known, unknown);
        Summary = $"{known.Count}/{known.Count + unknown.Count} armour products";
        logger.Debug($"[PLATE] ArmorNormalizer: {known.Count} products with construction data, " +
                     $"{unknown.Count} on their class");
    }

    /// <summary>
    /// Thickness of the hard element from the plate's own mass: t = m·hardFraction /
    /// (ρ·A). Returns 0 when the item has no mass of its own — soft armour built into a
    /// vest weighs nothing here, its mass lives on the vest.
    /// </summary>
    private static double DeriveThickness(string itemName, TemplateItemProperties p,
        string material, ReferenceBook.AmmoReference reference)
    {
        // plates only. The face-area convention is about plates, and a balaclava run
        // through it came out as 17 mm of polyethylene: its mass is fabric spread over
        // a head, not a hard element spread over a plate
        if (!itemName.StartsWith("item_equipment_plate_", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var kg = p.Weight ?? 0;
        if (kg <= 0 || !reference.ArmorMaterials.TryGetValue(material, out var m) ||
            m.DensityGCm3 <= 0)
        {
            return 0;
        }

        var footprint = $"{p.Width}x{p.Height}";
        if (!PlateAreaMm2.TryGetValue(footprint, out var areaMm2))
        {
            return 0;
        }

        // kg -> g, g/cm³ -> g/mm³
        var hardGrams = kg * 1000.0 * m.HardMassFraction;
        var densityGMm3 = m.DensityGCm3 / 1000.0;
        return hardGrams / (densityGMm3 * areaMm2);
    }

    /// <summary>
    /// The documented product for an item, and the key that found it. The item's own
    /// name answers first so that a product whose zones really do differ can name them
    /// one at a time — "granit4" is a class 5 ceramic front, a class 4 steel one and a
    /// class 3 polyethylene Zhuk insert, all under the one name — while the product key
    /// still covers everything that is genuinely one plate worn in four places.
    /// </summary>
    public static ReferenceBook.ArmorPlateRef? ProductSpec(ReferenceBook.AmmoReference reference,
        string itemName, string product, out string key)
    {
        key = Shorten(itemName);
        if (reference.ArmorPlates.TryGetValue(key, out var byName))
        {
            return byName;
        }

        key = product;
        return reference.ArmorPlates.TryGetValue(product, out var byProduct) ? byProduct : null;
    }

    /// <summary>
    /// Which of the four things an item is. Order matters: a vest and a helmet can
    /// share a product name — UNTAR is both — and the zone suffix is what tells them
    /// apart.
    /// </summary>
    public static Kind Classify(string itemName)
    {
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;

        if (itemName.StartsWith("item_equipment_plate_", ic))
        {
            return Kind.Plate;
        }

        if (itemName.Contains("helmet", ic) || itemName.Contains("visor", ic))
        {
            return Kind.Helmet;
        }

        return itemName.Contains("soft_armor", ic) ? Kind.VestComponent : Kind.Other;
    }

    /// <summary>
    /// The real armour of this rating, or null when we have not named one. Three
    /// objects of the same rating are three different things — 20 mm of monolithic
    /// polyethylene, an 11 mm pressed shell, a 7 mm sewn package — so which table
    /// answers matters more than the rating does.
    /// </summary>
    /// <param name="cls">The rating to read at — the maker's where they state one.</param>
    /// <param name="declared">What the game prints on it, for the report.</param>
    private static Resolved? ClassReference(ReferenceBook.AmmoReference reference,
        string itemName, string material, int cls, int declared)
    {
        if (itemName.StartsWith("item_equipment_plate_", StringComparison.OrdinalIgnoreCase))
        {
            var plateKey = $"{material}/{cls}";
            return reference.ArmorByClass.TryGetValue(plateKey, out var plate)
                ? new Resolved(plate, plateKey, cls < declared ? declared : 0)
                : null;
        }

        var shell = IsRigid(itemName, material);
        var table = shell ? reference.HelmetShells : reference.SoftArmor;
        var rating = Math.Min(cls, Ceiling(material, shell));
        var key = $"{material}/{rating}";

        return table.TryGetValue(key, out var entry)
            ? new Resolved(entry, key, rating < declared ? declared : 0)
            : null;
    }

    /// <summary>
    /// Why the search for this one came back empty, or nothing if nobody has looked.
    /// Keyed the same way the product table is — the item's own name, then the product.
    /// </summary>
    private static string Note(ReferenceBook.AmmoReference reference, string itemName, string product)
    {
        if (reference.NoRealSpecs.TryGetValue(Shorten(itemName), out var byName))
        {
            return byName;
        }

        return reference.NoRealSpecs.TryGetValue(product, out var byProduct) ? byProduct : "";
    }

    /// <summary>The materials that come both as a pressed laminate and as loose fabric.</summary>
    private static readonly string[] Fibrous = ["Aramid", "UHMWPE"];

    /// <summary>
    /// Headwear the game arms that is genuinely cloth. Everything else worn on the head
    /// — a helmet, a visor, a face mask — is pressed into a rigid shell, so the list
    /// runs this way round: fabric is the exception and has to be named.
    /// </summary>
    private static readonly string[] Woven =
        ["balaclava", "bomber", "hood", "shemagh", "bandana", "beanie"];

    /// <summary>
    /// Whether the armour is a rigid element rather than a sewn package. A rigid
    /// element is rigid wherever it is worn — nobody sews a package out of steel — so
    /// anything that is not fibre is one. Fibre is the material that comes both ways: a
    /// vest insert is stitched, and a helmet or a mask is prepreg pressed under heat
    /// into a laminate that fails as a solid.
    /// </summary>
    private static bool IsRigid(string itemName, string material)
    {
        if (!Fibrous.Contains(material, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return Classify(itemName) switch
        {
            Kind.Helmet => true,
            Kind.VestComponent or Kind.Plate => false,
            _ => !Woven.Any(w => itemName.Contains(w, StringComparison.OrdinalIgnoreCase)),
        };
    }

    /// <summary>
    /// The rating a material can actually reach in that form. A woven package stops at
    /// 2: getting to Br3 with aramid alone would take around 200 mm of it, which is why
    /// carriers are sold as Br1 or Br2 and everything above that in a vest is a plate.
    /// Pressing the same fibre into a resin-bonded shell buys one rung and no more —
    /// past that a helmet stops getting thicker and starts getting a metal or ceramic
    /// element, and that element belongs in the product table by name. A visor is
    /// polycarbonate and laminate whatever it is bolted to. Metal and ceramic are not
    /// capped at all: a shell really is thicker on a heavier helmet.
    /// </summary>
    private static int Ceiling(string material, bool shell)
    {
        if (material.Equals("Glass", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (!Fibrous.Contains(material, StringComparer.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        return shell ? 3 : 2;
    }

    /// <summary>Drops the shared prefix so the report reads as plate names.</summary>
    private static string Shorten(string itemName)
    {
        const string platePrefix = "item_equipment_plate_";
        return itemName.StartsWith(platePrefix, StringComparison.OrdinalIgnoreCase)
            ? itemName.Substring(platePrefix.Length)
            : itemName;
    }

    /// <summary>
    /// The product an item belongs to: everything before the class or level marker. One
    /// vest's front, back, side and groin are the same plate, and naming them
    /// separately in the reference would be four ways to get the same figure wrong.
    /// </summary>
    public static string Product(string name)
    {
        const string platePrefix = "item_equipment_plate_";
        if (name.StartsWith(platePrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(platePrefix.Length);
        }

        return ProductCut.Replace(name, "").TrimEnd('_');
    }

    private void WriteReport(string modPath, ReferenceBook.AmmoReference reference,
        Dictionary<string, Row> known, Dictionary<string, Row> unknown)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# PLATE armour construction report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("The game describes armour by a class and a material. The class is a "
                      + "consequence of the construction, not its cause, and the thickness of the "
                      + "hard element — the quantity that actually decides what stops what — is "
                      + "not in the data at all. This is where it comes from.");
        sb.AppendLine();

        sb.AppendLine("## Material properties in use");
        sb.AppendLine();
        sb.AppendLine("| Material | Fails by | Density | Strength | Source |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var (name, m) in reference.ArmorMaterials.OrderBy(kv => kv.Key))
        {
            var strength = m.Class switch
            {
                "Ductile" => $"yield {m.YieldMPa:N0} / shear {m.ShearMPa:N0} MPa",
                "Brittle" => $"compressive {m.CompressiveMPa:N0} MPa",
                "Fibrous" => $"tensile {m.FibreTensileMPa:N0} MPa, ε {m.FailureStrain:P1}",
                _ => "—",
            };
            var mechanism = m.Class switch
            {
                "Ductile" => "plugging and hole growth",
                "Brittle" => "fracture conoid, erodes the core",
                "Fibrous" => "a stretching cone of fibre",
                _ => m.Class,
            };
            sb.AppendLine($"| {name} | {mechanism} | {m.DensityGCm3:N2} g/cm³ | {strength} | {m.Source} |");
        }

        var all = known.Values.Concat(unknown.Values).ToList();

        sb.AppendLine();
        sb.AppendLine("## Where each figure came from");
        sb.AppendLine();
        sb.AppendLine("| | The real product | A reference construction | Its own mass | Nothing |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var kind in Kinds)
        {
            var of = all.Where(r => r.Kind == kind).ToList();
            sb.AppendLine($"| {Title(kind)} | {of.Count(r => r.From == Origin.Product)} | "
                          + $"{of.Count(r => r.From == Origin.Reference)} | "
                          + $"{of.Count(r => r.From == Origin.Mass)} | "
                          + $"{of.Count(r => r.From == Origin.None)} |");
        }

        sb.AppendLine($"| **All {all.Count}** | **{all.Count(r => r.From == Origin.Product)}** | "
                      + $"**{all.Count(r => r.From == Origin.Reference)}** | "
                      + $"**{all.Count(r => r.From == Origin.Mass)}** | "
                      + $"**{all.Count(r => r.From == Origin.None)}** |");
        sb.AppendLine();
        sb.AppendLine("Left to right is descending trust: published specifications for the thing "
                      + "the item is modelled on, then the real armour of its material and rating, "
                      + "then physics off a weight other mods can rewrite, then nothing at all.");

        var onReference = all.Where(r => r.From == Origin.Reference).ToList();
        var searched = onReference.Count(r => r.Note.Length > 0);
        var untouched = onReference.Where(r => r.Note.Length == 0)
            .GroupBy(r => r.Kind)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {Title(g.Key).ToLowerInvariant()}")
            .ToList();

        sb.AppendLine();
        sb.AppendLine($"Of the {onReference.Count} on a reference, {searched} have been looked into "
                      + "and had nothing to give — `Looked up` says what was found instead. Nobody "
                      + "has gone looking for: "
                      + (untouched.Count > 0 ? string.Join(", ", untouched) + "." : "nothing, "
                          + "every one of them has been searched for."));

        foreach (var kind in Kinds)
        {
            var of = all.Where(r => r.Kind == kind).ToList();
            sb.AppendLine();
            sb.AppendLine($"## {Title(kind)} ({of.Count})");
            sb.AppendLine();
            sb.AppendLine(Blurb(kind));

            foreach (var origin in Origins)
            {
                Section(sb, origin, of.Where(r => r.From == origin));
            }
        }

        try
        {
            File.WriteAllText(System.IO.Path.Combine(modPath, "plate-armor-report.md"), sb.ToString());
        }
        catch (Exception ex)
        {
            logger.Warning($"[PLATE] Could not write the armour report: {ex.Message}");
        }
    }

    private static readonly Kind[] Kinds =
        [Kind.Helmet, Kind.Plate, Kind.VestComponent, Kind.Other];

    private static readonly Origin[] Origins =
        [Origin.Product, Origin.Reference, Origin.Mass, Origin.None];

    private static string Title(Kind kind) => kind switch
    {
        Kind.Helmet => "Helmets",
        Kind.Plate => "Plates",
        Kind.VestComponent => "Vest components",
        _ => "Other",
    };

    private static string Blurb(Kind kind) => kind switch
    {
        Kind.Helmet =>
            "Shells, visors, mandibles and appliques. Manufacturers publish a mass and a "
            + "rating rather than a thickness, so a documented helmet is its shell mass over "
            + "the area it covers — which reproduces the published areal density, the "
            + "quantity that actually decides what gets through.",
        Kind.Plate =>
            "The hard inserts. The only armour in the game that carries a mass of its own, "
            + "so the only kind that can fall back on physics when nothing else is known.",
        Kind.VestComponent =>
            "The woven package sewn into a carrier. A vest without plates is certified as "
            + "class 2 whatever the game prints on it, and the package is the same fabric at "
            + "every rating above that.",
        _ =>
            "Masks, glasses, balaclavas and headgear that is armour only by the game's "
            + "reckoning.",
    };

    private static string Heading(Origin origin) => origin switch
    {
        Origin.Product => "From the real product",
        Origin.Reference => "From a reference construction",
        Origin.Mass => "From its own mass",
        _ => "Not normalized",
    };

    private static string Blurb(Origin origin) => origin switch
    {
        Origin.Product =>
            "Published specifications for the product the item is modelled on. These are the "
            + "figures to trust, and the ones worth growing.",
        Origin.Reference =>
            "No documented product, so the item takes the real armour of its material and "
            + "rating. `Reference` names exactly which entry answered; where the material has "
            + "a ceiling its rating cannot lift, the rating it was read at is shown too.",
        Origin.Mass =>
            "Neither a documented product nor a reference, but the item carries a mass, and "
            + "mass over density over face area is a thickness. Weakest of the three: any mod "
            + "that scales item weight moves these figures with it.",
        _ =>
            "Nothing was applied; these behave exactly as the game shipped them. Each is an "
            + "invitation to add its material and thickness to `ammo-reference.jsonc`.",
    };

    private static void Section(StringBuilder sb, Origin origin, IEnumerable<Row> rows)
    {
        var list = rows.OrderBy(r => r.Material).ThenByDescending(r => r.Class)
            .ThenBy(r => r.Product).ToList();

        if (list.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"### {Heading(origin)} ({list.Count})");
        sb.AppendLine();
        sb.AppendLine(Blurb(origin));
        sb.AppendLine();

        if (origin == Origin.None)
        {
            sb.AppendLine("| Item | Material | Class | Zones |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var r in list)
            {
                sb.AppendLine($"| {r.Product} | {r.Material} | {r.Class} | {r.Zones} |");
            }

            return;
        }

        var withReference = origin == Origin.Reference;
        sb.AppendLine(withReference
            ? "| Item | Material | Class | Reference | Construction | Thickness | Looked up |"
            : "| Item | Material | Class | Construction | Thickness | Zones | Source |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var r in list)
        {
            var material = r.MaterialWas.Length > 0
                ? $"{r.MaterialWas} → **{r.Material}**"
                : r.Material;

            if (withReference)
            {
                var key = r.CappedFrom > 0
                    ? $"`{r.ReferenceKey}` (declares {r.CappedFrom})"
                    : $"`{r.ReferenceKey}`";
                sb.AppendLine($"| {r.Product} | {material} | {r.Class} | {key} | {r.Prototype} | " +
                              $"{r.ThicknessMm:N1} mm | {r.Note} |");
            }
            else
            {
                sb.AppendLine($"| {r.Product} | {material} | {r.Class} | {r.Prototype} | " +
                              $"{r.ThicknessMm:N1} mm | {r.Zones} | {r.Source} |");
            }
        }
    }
}
