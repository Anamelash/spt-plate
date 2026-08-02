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

    private sealed class Row
    {
        public required string Product;
        public required string Material;
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

            var product = Product(item.Name ?? "");
            reference.ArmorPlates.TryGetValue(product, out var spec);

            // no documented product, but a plate carries its own mass, and mass over
            // density over face area is a thickness. Most of the armour here is invented
            // for the game and has no specification to look up — this is the only honest
            // number available for it, and it is still physics rather than a guess
            var itemName = item.Name ?? "";
            var cls = (int)(p.ArmorClass ?? 0);

            // a plate the game invented has no product to look up, but there is a real
            // plate of the same rating doing the same job, and that is what it stands in
            // for. Mass only decides it when even that is missing
            var byClass = spec == null ? ClassReference(reference, itemName, material, cls) : null;
            var derived = spec == null && byClass == null
                ? DeriveThickness(itemName, p, material, reference)
                : 0;

            var target = spec != null || byClass != null || derived > 0 ? known : unknown;

            // a documented product is one plate however many zones wear it, but two
            // plates that merely share a product name are two plates: "granit4" covers a
            // front and a side of different mass, and collapsing them keeps whichever
            // was read last
            var perItem = derived > 0 || byClass != null;
            var rowKey = perItem ? itemName : product;

            if (!target.TryGetValue(rowKey, out var row))
            {
                row = new Row
                {
                    Product = perItem ? Shorten(itemName) : product,
                    Material = material,
                    Class = cls,
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
                    row.Prototype = byClass.Ref.Prototype;
                    row.ThicknessMm = byClass.Ref.ThicknessMm;
                    row.Source = byClass.Ref.Source;
                    _thickness[item.Id] = byClass.Ref.ThicknessMm;
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
    /// The real armour of this rating, or null when we have not named one. A plate and
    /// a soft package of the same rating are different objects — 20 mm of monolithic
    /// polyethylene against 9 mm of helmet shell — so which table answers depends on
    /// which the item is.
    /// </summary>
    private static Resolved? ClassReference(
        ReferenceBook.AmmoReference reference, string itemName, string material, int cls)
    {
        if (itemName.StartsWith("item_equipment_plate_", StringComparison.OrdinalIgnoreCase))
        {
            var plateKey = $"{material}/{cls}";
            return reference.ArmorByClass.TryGetValue(plateKey, out var plate)
                ? new Resolved(plate, plateKey, 0)
                : null;
        }

        var rating = SoftRating(material, cls);
        var softKey = $"{material}/{rating}";
        return reference.SoftArmor.TryGetValue(softKey, out var soft)
            ? new Resolved(soft, softKey, rating < cls ? cls : 0)
            : null;
    }

    /// <summary>
    /// Materials with a ceiling their rating cannot lift. A woven package and a
    /// polycarbonate visor stop pistol rounds and fragments; getting to Br3 with aramid
    /// alone would take around 200 mm of it, which is why carriers are sold as Br1 or
    /// Br2 and everything above that in a vest is a plate. Whatever the game prints on
    /// one, it is read at 2.
    /// </summary>
    private static readonly string[] CappedAtTwo = ["Aramid", "UHMWPE", "Glass"];

    private static int SoftRating(string material, int cls)
    {
        return CappedAtTwo.Contains(material, StringComparer.OrdinalIgnoreCase)
            ? Math.Min(cls, 2)
            : cls;
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
        sb.AppendLine("| Source | Items | Trust |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| The real product | {all.Count(r => r.From == Origin.Product)} | "
                      + "published specifications for the thing it is modelled on |");
        sb.AppendLine($"| A reference construction | {all.Count(r => r.From == Origin.Reference)} | "
                      + "the real armour of that material and rating |");
        sb.AppendLine($"| Its own mass | {all.Count(r => r.From == Origin.Mass)} | "
                      + "physics, but from a weight other mods can rewrite |");
        sb.AppendLine($"| Nothing | {all.Count(r => r.From == Origin.None)} | "
                      + "behaves exactly as the game shipped it |");

        Section(sb, "From the real product", all.Where(r => r.From == Origin.Product),
            "Published specifications for the product the item is modelled on. These are the "
            + "figures to trust, and the ones worth growing.",
            withReference: false);

        Section(sb, "From a reference construction", all.Where(r => r.From == Origin.Reference),
            "No documented product, so the item takes the real armour of its material and "
            + "rating. Most of the armour in the game is invented for it — there is no "
            + "specification for a \"Cult Termite\" — but there is always a real one doing the "
            + "same job. `Reference` names exactly which entry answered; where the material has "
            + "a ceiling its rating cannot lift, the rating it was read at is shown too.",
            withReference: true);

        Section(sb, "From its own mass", all.Where(r => r.From == Origin.Mass),
            "Neither a documented product nor a reference, but the item carries a mass, and "
            + "mass over density over face area is a thickness. Weakest of the three: any mod "
            + "that scales item weight moves these figures with it.",
            withReference: false);

        sb.AppendLine();
        sb.AppendLine($"## Not normalized ({all.Count(r => r.From == Origin.None)})");
        sb.AppendLine();
        sb.AppendLine("Nothing was applied; these behave exactly as the game shipped them. Each is "
                      + "an invitation to add its material and thickness to `ammo-reference.jsonc`.");
        sb.AppendLine();
        sb.AppendLine("| Item | Class | Material | Zones |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in all.Where(r => r.From == Origin.None)
                     .OrderByDescending(r => r.Class).ThenBy(r => r.Product))
        {
            sb.AppendLine($"| {r.Product} | {r.Class} | {r.Material} | {r.Zones} |");
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

    private static void Section(StringBuilder sb, string title, IEnumerable<Row> rows,
        string blurb, bool withReference)
    {
        var list = rows.OrderBy(r => r.Material).ThenByDescending(r => r.Class)
            .ThenBy(r => r.Product).ToList();

        sb.AppendLine();
        sb.AppendLine($"## {title} ({list.Count})");
        sb.AppendLine();
        sb.AppendLine(blurb);
        sb.AppendLine();

        if (list.Count == 0)
        {
            sb.AppendLine("*(none)*");
            return;
        }

        sb.AppendLine(withReference
            ? "| Item | Material | Class | Reference | Construction | Thickness | Zones |"
            : "| Item | Material | Class | Construction | Thickness | Zones | Source |");
        sb.AppendLine(withReference ? "|---|---|---|---|---|---|---|" : "|---|---|---|---|---|---|---|");

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
                              $"{r.ThicknessMm:N1} mm | {r.Zones} |");
            }
            else
            {
                sb.AppendLine($"| {r.Product} | {material} | {r.Class} | {r.Prototype} | " +
                              $"{r.ThicknessMm:N1} mm | {r.Zones} | {r.Source} |");
            }
        }
    }
}
