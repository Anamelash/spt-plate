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
    }

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
            var target = reference.ArmorPlates.TryGetValue(product, out var spec) ? known : unknown;

            if (!target.TryGetValue(product, out var row))
            {
                row = new Row
                {
                    Product = product,
                    Material = material,
                    Class = (int)(p.ArmorClass ?? 0),
                };
                target[product] = row;
            }

            row.Zones++;

            if (spec == null)
            {
                continue;
            }

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

        sb.AppendLine();
        sb.AppendLine($"## Products with construction data ({known.Count})");
        sb.AppendLine();
        sb.AppendLine("| Product | Prototype | Material | Thickness | Zones | Source |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in known.Values.OrderBy(r => r.Product))
        {
            var material = r.MaterialWas.Length > 0
                ? $"{r.MaterialWas} → **{r.Material}**"
                : r.Material;
            sb.AppendLine($"| {r.Product} | {r.Prototype} | {material} | {r.ThicknessMm:N1} mm | " +
                          $"{r.Zones} | {r.Source} |");
        }

        sb.AppendLine();
        sb.AppendLine($"## Still on their class number ({unknown.Count})");
        sb.AppendLine();
        sb.AppendLine("These behave exactly as before. Each is an invitation to look up the real "
                      + "product and add its material and thickness to `ammo-reference.jsonc`; the "
                      + "high classes are worth the most, since that is where the class number is "
                      + "doing the most guessing.");
        sb.AppendLine();
        sb.AppendLine("| Product | Class | Material | Zones |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in unknown.Values.OrderByDescending(r => r.Class).ThenBy(r => r.Product))
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
}
