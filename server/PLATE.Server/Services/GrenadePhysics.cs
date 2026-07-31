using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace PLATE.Server.Services;

/// <summary>
/// Grenade fragment physics from prototype specs (ammo-reference.jsonc).
/// Vanilla shrapnel templates are a fiction (0.09 g @ 90 m/s = 0.4 J): all of
/// PLATE's client-side energy mechanics (BABT, fractures, retention) are dead
/// for them. For each grenade in the reference book a clone of its fragment is
/// created with real mass/velocity; damage comes from energy (the ammo
/// normalizer formulas), penetration from E/A. Optionally the blast (Strength)
/// is matched to the explosive mass by cube root from the anchor.
/// Fragment count (FragmentsCount) is left untouched — performance.
/// </summary>
[Injectable]
public class GrenadePhysics(
    DatabaseServer databaseServer,
    ReferenceBook referenceBook,
    JsonUtil jsonUtil,
    ISptLogger<GrenadePhysics> logger)
{
    /// <summary>Fragment steel density, g/cm³ (for the equivalent sphere diameter).</summary>
    private const double SteelDensity = 7.85;

    public void Apply(PlateServerConfig cfg, string modPath)
    {
        var tables = databaseServer.GetTables();
        var items = tables.Templates?.Items;
        if (items == null)
        {
            return;
        }

        var reference = referenceBook.Load(modPath);
        if (reference.Grenades.Count == 0)
        {
            logger.Warning("[PLATE] GrenadePhysics: reference book is empty, skipping");
            return;
        }

        var a = cfg.AmmoNormalizer;
        var byName = items.Values
            .Where(i => i.Name != null)
            .GroupBy(i => i.Name!)
            .ToDictionary(g => g.Key, g => g.First());

        var idx = 0;
        var done = 0;
        // share of large fragments (base plate/fuze): 1 per grenade out of FragmentsCount fragments
        var largeShares = new Dictionary<string, (double E0, double Share)>();
        foreach (var (name, gr) in reference.Grenades)
        {
            idx++;
            if (!byName.TryGetValue(name, out var grenade) || grenade.Properties == null)
            {
                logger.Warning($"[PLATE] GrenadePhysics: grenade '{name}' not found in the DB");
                continue;
            }

            var fragSrc = grenade.Properties.FragmentType;
            if (string.IsNullOrEmpty(fragSrc) || !items.TryGetValue(new MongoId(fragSrc), out var srcTpl))
            {
                logger.Warning($"[PLATE] GrenadePhysics: '{name}' has no FragmentType");
                continue;
            }

            // stable clone id: b100d0000000000000001NNN (hex-compatible)
            var cloneId = $"b100d0000000000000001{idx:000}";
            if (!items.ContainsKey(new MongoId(cloneId)))
            {
                var clone = jsonUtil.Deserialize<SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem>(
                    jsonUtil.Serialize(srcTpl));
                if (clone?.Properties == null)
                {
                    logger.Error($"[PLATE] GrenadePhysics: shrapnel clone for '{name}' failed");
                    continue;
                }

                clone.Id = new MongoId(cloneId);
                clone.Name = $"{srcTpl.Name}_plate_{gr.Prototype}";

                var p = clone.Properties;
                var massG = Math.Max(gr.FragMassG, 0.05);
                var v0 = Math.Max(gr.FragV0, 50);
                var e = 0.5 * (massG / 1000.0) * v0 * v0;
                // diameter of the equivalent steel sphere, mm
                var diaMm = Math.Pow(6.0 * (massG / SteelDensity) / Math.PI, 1.0 / 3.0) * 10.0;
                var area = Math.PI * diaMm * diaMm / 4.0;

                p.BulletMassGram = massG;
                p.InitialSpeed = v0;
                p.BulletDiameterMilimeters = Math.Round(diaMm, 2);
                // damage — wound channel model (PC+TC), same as for bullets/buckshot;
                // a fragment lodges in the body and deposits everything
                var wound = WoundModel.Compute(massG, diaMm, v0, cfg.Grenades.FragmentX, 0, a);
                p.Damage = Math.Clamp(Math.Round(
                    a.WoundChannelModel ? wound.Damage : e / a.EnergyPerHp),
                    a.MinPelletDamage, a.DamageCap);
                p.PenetrationPower = (int)Math.Clamp(
                    Math.Round(a.PenPerEnergyDensity * (e / area)
                               * (1 + a.PenConstructionFactor * (0.5 - cfg.Grenades.FragmentX))),
                    1, 60);
                // ragged fragment wounds bleed almost always (on penetration the
                // client additionally guarantees a light bleed)
                p.LightBleedingDelta = cfg.Grenades.FragLightDelta;
                p.HeavyBleedingDelta = cfg.Grenades.FragHeavyDelta;

                items[clone.Id] = clone;
                AddLocales(tables, cloneId, gr.Prototype);
            }

            grenade.Properties.FragmentType = cloneId;

            var fragCount = Math.Max(grenade.Properties.FragmentsCount ?? 1, 1);
            var e0 = 0.5 * (Math.Max(gr.FragMassG, 0.05) / 1000.0) *
                     Math.Max(gr.FragV0, 50) * Math.Max(gr.FragV0, 50);
            largeShares[cloneId] = (Math.Round(e0), Math.Round(1.0 / fragCount, 4));

            if (cfg.Grenades.BlastFromTnt && (grenade.Properties.Strength ?? 0) > 0 && gr.TntG > 0 &&
                reference.BlastAnchor.TntG > 0)
            {
                var oldStrength = grenade.Properties.Strength;
                grenade.Properties.Strength = Math.Round(reference.BlastAnchor.Strength *
                    Math.Cbrt(gr.TntG / reference.BlastAnchor.TntG));
                if (Math.Abs((oldStrength ?? 0) - grenade.Properties.Strength.Value) > 0.5)
                {
                    logger.Info($"[PLATE] {gr.Prototype}: Strength {oldStrength:0} -> " +
                                $"{grenade.Properties.Strength:0} (explosive {gr.TntG} g)");
                }
            }

            done++;
        }

        PublishAmmoData(largeShares, cfg);

        logger.Success($"[PLATE] GrenadePhysics: {done}/{reference.Grenades.Count} grenades brought to prototype specs " +
                       $"(fragments: mass/velocity/damage from energy; blast: " +
                       $"{(cfg.Grenades.BlastFromTnt ? "from explosive mass" : "vanilla")})");
    }

    /// <summary>
    /// Appends the shrapnel clones to /plate/ammo-data: X, E0 and LargeShare
    /// (=1/FragmentsCount — the probability that the hitting fragment turned out
    /// to be the base plate/fuze). The client uses LargeShare to decide whether
    /// the fragment gets an honest penetration roll.
    /// </summary>
    private static void PublishAmmoData(
        Dictionary<string, (double E0, double Share)> largeShares, PlateServerConfig cfg)
    {
        if (largeShares.Count == 0)
        {
            return;
        }

        var root = System.Text.Json.Nodes.JsonNode.Parse(
                       string.IsNullOrEmpty(Routes.PlateAmmoData.Json) ? "{}" : Routes.PlateAmmoData.Json)
                   as System.Text.Json.Nodes.JsonObject
                   ?? new System.Text.Json.Nodes.JsonObject();

        foreach (var (tpl, v) in largeShares)
        {
            root[tpl] = new System.Text.Json.Nodes.JsonObject
            {
                ["X"] = cfg.Grenades.FragmentX,
                ["E0"] = v.E0,
                ["LargeShare"] = v.Share,
            };
        }

        Routes.PlateAmmoData.Json = root.ToJsonString(
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Shrapnel display strings, keyed by SPT locale code; "en" is the fallback.</summary>
    private static readonly Dictionary<string, (string Name, string Short, string Desc)> FragmentLocales =
        new()
        {
            ["en"] = ("{0} fragment", "fragment", "Fragment of the {0} grenade (PLATE)."),
            ["ru"] = ("Осколок {0}", "осколок", "Осколок гранаты {0} (PLATE)."),
            ["ge"] = ("{0}-Splitter", "Splitter", "Splitter der Granate {0} (PLATE)."),
            ["fr"] = ("Éclat de {0}", "éclat", "Éclat de la grenade {0} (PLATE)."),
            ["es"] = ("Fragmento de {0}", "fragmento", "Fragmento de la granada {0} (PLATE)."),
            ["pl"] = ("Odłamek {0}", "odłamek", "Odłamek granatu {0} (PLATE)."),
            ["cz"] = ("Střepina {0}", "střepina", "Střepina granátu {0} (PLATE)."),
            ["tu"] = ("{0} şarapneli", "şarapnel", "{0} el bombasının şarapneli (PLATE)."),
            ["ch"] = ("{0} 破片", "破片", "{0} 手雷破片（PLATE）。"),
            ["jp"] = ("{0} 破片", "破片", "{0} 手榴弾の破片（PLATE）。"),
            ["kr"] = ("{0} 파편", "파편", "{0} 수류탄 파편 (PLATE)."),
        };

    /// <summary>Locale entries for the shrapnel clone (kill feed/hit log).</summary>
    private static void AddLocales(
        SPTarkov.Server.Core.Models.Spt.Server.DatabaseTables tables, string tpl, string prototype)
    {
        var locales = tables.Locales?.Global;
        if (locales == null)
        {
            return;
        }

        foreach (var (lang, lazy) in locales)
        {
            if (!FragmentLocales.TryGetValue(lang, out var t))
            {
                // es-mx falls back to es, everything else to en
                t = lang.StartsWith("es") ? FragmentLocales["es"] : FragmentLocales["en"];
            }

            var loc = t;
            lazy.AddTransformer(d =>
            {
                if (d != null)
                {
                    d[$"{tpl} Name"] = string.Format(loc.Name, prototype);
                    d[$"{tpl} ShortName"] = loc.Short;
                    d[$"{tpl} Description"] = string.Format(loc.Desc, prototype);
                }

                return d;
            });
        }
    }
}
