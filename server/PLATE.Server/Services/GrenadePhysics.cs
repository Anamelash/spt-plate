using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Tables;
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
    TemplateTable templateTable,
    LocaleTable localeTable,
    ReferenceBook referenceBook,
    JsonUtil jsonUtil,
    ISptLogger<GrenadePhysics> logger)
{
    /// <summary>Fragment steel density, g/cm³ (for the equivalent sphere diameter).</summary>
    private const double SteelDensity = 7.85;

    /// <summary>
    /// Id space reserved for PLATE's fragment templates: b100d0000000000000001NNN, one
    /// per grenade in the reference book. Public because the ammo normalizer has to be
    /// able to tell them apart from ammunition (see <see cref="IsFragmentTemplate"/>).
    /// </summary>
    public const string FragmentIdPrefix = "b100d0000000000000001";

    /// <summary>
    /// Marker in the template <c>_name</c> of a fragment clone. The id alone says the
    /// slot is taken, not by whom: if another mod ever occupies one of ours, repointing
    /// a grenade at its item would be worse than leaving the grenade vanilla.
    /// </summary>
    private const string CloneNameMarker = "_plate_";

    /// <summary>One-line result for the startup summary; null if the module did not run.</summary>
    public string? Summary { get; private set; }

    /// <summary>Template id of the fragment of the <paramref name="index"/>-th grenade (1-based).</summary>
    public static string FragmentTemplateId(int index) => $"{FragmentIdPrefix}{index:000}";

    /// <summary>True if the template is one of PLATE's grenade fragments.</summary>
    public static bool IsFragmentTemplate(MongoId id) =>
        id.ToString().StartsWith(FragmentIdPrefix, StringComparison.Ordinal);

    /// <param name="canAddItems">
    /// True only for the early pass in <see cref="PlateItemRegistration"/>, which runs
    /// before the server closes the item database (see <see cref="ItemRegistrationWindow"/>).
    /// The late pass runs with false and cannot add anything: since 4.1.3 an item that
    /// appears after the cutoff kills the server, so "create it if it is missing" is not
    /// a recoverable path there. Everything else the method does is idempotent and runs
    /// in both passes — the late one is what keeps PLATE's numbers on the fragments after
    /// every other mod has had its turn at the database.
    /// </param>
    public void Apply(PlateServerConfig cfg, string modPath, bool canAddItems)
    {
        var items = templateTable.Items;
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

            var cloneId = FragmentTemplateId(idx);
            if (!items.TryGetValue(new MongoId(cloneId), out var clone))
            {
                if (!canAddItems)
                {
                    // The grenade exists but its fragment does not: it was added to the
                    // database after our registration pass, and the database is closed by
                    // now. Vanilla shrapnel for it, and a line saying why.
                    logger.Warning($"[PLATE] GrenadePhysics: '{name}' appeared after PLATE registered " +
                                   "its fragments; it keeps the vanilla ones");
                    continue;
                }

                clone = jsonUtil.Deserialize<SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem>(
                    jsonUtil.Serialize(srcTpl));
                if (clone?.Properties == null)
                {
                    logger.Error($"[PLATE] GrenadePhysics: shrapnel clone for '{name}' failed");
                    continue;
                }

                clone.Id = new MongoId(cloneId);
                clone.Name = $"{srcTpl.Name}{CloneNameMarker}{gr.Prototype}";

                items[clone.Id] = clone;
                AddLocales(cloneId, gr.Prototype);
            }
            else if (clone.Properties == null || clone.Name?.Contains(CloneNameMarker) != true)
            {
                logger.Error($"[PLATE] GrenadePhysics: item id {cloneId} is taken by '{clone.Name}', " +
                             $"'{name}' keeps its vanilla fragments");
                continue;
            }

            ConfigureFragment(clone, gr, cfg);
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
                    logger.Debug($"[PLATE] {gr.Prototype}: Strength {oldStrength:0} -> " +
                                 $"{grenade.Properties.Strength:0} (explosive {gr.TntG} g)");
                }
            }

            done++;
        }

        PublishAmmoData(largeShares, cfg);

        Summary = $"{done}/{reference.Grenades.Count} grenades";
        logger.Debug($"[PLATE] GrenadePhysics: {done}/{reference.Grenades.Count} grenades brought to prototype specs " +
                     $"({(canAddItems ? "registration pass; " : "")}" +
                     $"fragments: mass/velocity/damage from energy; blast: " +
                     $"{(cfg.Grenades.BlastFromTnt ? "from explosive mass" : "vanilla")})");
    }

    /// <summary>
    /// Writes the prototype's fragment onto the clone: mass, velocity, the diameter of
    /// the equivalent steel sphere, and damage/penetration derived from those. Called in
    /// both passes and depends on nothing but the reference book and the config, so the
    /// late one simply restores the numbers if another mod has been at the template in
    /// between.
    /// </summary>
    private static void ConfigureFragment(
        SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem clone,
        ReferenceBook.GrenadeRef gr,
        PlateServerConfig cfg)
    {
        var a = cfg.AmmoNormalizer;
        var p = clone.Properties!;
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
        // a grenade fragment IS its core: solid steel, nothing to break off
        var wound = WoundModel.Compute(massG, diaMm, v0, cfg.Grenades.FragmentX, 1, a);
        p.Damage = Math.Clamp(Math.Round(
            a.WoundChannelModel ? wound.Damage : e / a.EnergyPerHp),
            a.MinPelletDamage, a.DamageCap);
        // a fragment is a lump of casing: no core to concentrate anything, and
        // steel that ragged does not flatten out either
        p.PenetrationPower = (int)Math.Clamp(
            Math.Round(a.PenPerEnergyDensity * (e / AmmoNormalizer.ImpactArea(
                area, 1, cfg.Grenades.FragmentX, cfg.Armor.ExpansionOnArmor))),
            1, 60);
        // ragged fragment wounds bleed almost always (on penetration the
        // client additionally guarantees a light bleed)
        p.LightBleedingDelta = cfg.Grenades.FragLightDelta;
        p.HeavyBleedingDelta = cfg.Grenades.FragHeavyDelta;
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
    private void AddLocales(string tpl, string prototype)
    {
        var locales = localeTable.Global;
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
