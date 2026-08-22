using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace PLATE.Server.Services;

/// <summary>
/// Barrel velocity normalization.
///
/// The muzzle velocity modifier of a barrel is the one input to the whole damage
/// pipeline that PLATE never derived from physics: mass, caliber and cartridge
/// velocity are normalized, and then an arbitrary percentage decides what actually
/// leaves the muzzle. Values in the wild are not physical — an 11-inch .308 barrel
/// ships at -31%, where measurement says about -16% — and whatever a weapon pack or
/// a live-values backport puts there propagates straight into wound energy.
///
/// So every barrel gets its modifier recomputed from its length by BarrelModel, and
/// muzzle devices, which have no business changing velocity, get clamped.
///
/// What a part is comes from <see cref="PartClassifier"/> — the item's class, its place
/// in the slot graph, the properties only barrels carry — and never from the naming
/// convention alone, which holds for vanilla items and for nothing a weapon pack adds.
///
/// Works in memory on the loaded DB, like the ammo pass — items.json on disk is
/// untouched and a restart re-derives everything from scratch.
/// </summary>
[Injectable]
public class BarrelNormalizer(
    DatabaseServer databaseServer,
    ReferenceBook referenceBook,
    ISptLogger<BarrelNormalizer> logger)
{
    /// <summary>One-line result for the startup summary; null if the module did not run.</summary>
    public string? Summary { get; private set; }

    /// <summary>
    /// Length in millimetres. The lookbehind is what keeps the caliber out of it: a
    /// pack that writes "AR-15 5.56x45mm 11.5 inch barrel" offers "45mm" to anything
    /// reading left to right, and a 45 mm AR-15 barrel is not a thing. The unit ends at
    /// a letter rather than at a word boundary, because the base game writes
    /// "barrel_ar15_260mm_556x45" and an underscore is a word character.
    /// </summary>
    private static readonly Regex Millimetres =
        new(@"(?<![\dxх×.,])(\d{2,4}(?:[.,]\d+)?)\s*(?:mm|мм)(?![a-zA-Zа-яА-Я])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Length in inches, as every American pack spells it.</summary>
    private static readonly Regex Inches =
        new(@"(?<![\dxх×.,])(\d{1,2}(?:[.,]\d+)?)\s*(?:inches|inch|in\b|""|″)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const double MmPerInch = 25.4;

    private sealed class Change
    {
        public required string Name;
        public required string Caliber;
        public double LengthMm;
        public double Old;
        public double New;
        public string Note = "";
    }

    public void Run(PlateServerConfig cfg, string modPath)
    {
        var b = cfg.BarrelNormalizer;
        var items = databaseServer.GetTables().Templates?.Items;
        if (items == null)
        {
            logger.Error("[PLATE] BarrelNormalizer: item DB unavailable");
            return;
        }

        var reference = referenceBook.Load(modPath);
        if (reference.Barrels.Count == 0)
        {
            logger.Error("[PLATE] BarrelNormalizer: no caliber reference data, skipped");
            return;
        }

        var parts = new PartClassifier(items, ReadLocale(databaseServer));
        var byModel = MapModelsToLengths(items, reference);
        var barrelCaliber = MapBarrelsToCalibers(items, parts, out var hasRemovableBarrel);
        var familyCaliber = MapFamiliesToCalibers(items, parts, barrelCaliber, reference);
        var changed = new List<Change>();
        var skipped = new List<Change>();

        foreach (var item in items.Values)
        {
            var p = item.Properties;
            if (p?.Velocity == null)
            {
                continue;
            }

            var name = item.Name ?? "";
            var old = p.Velocity.Value;
            var text = parts.TextOf(item).ToList();

            switch (parts.RoleOf(item))
            {
                case PartRole.Weapon:
                    NormalizeWeapon(item, p, parts, reference, b, hasRemovableBarrel, byModel, changed, skipped);
                    continue;

                case PartRole.Barrel:
                {
                    var caliber = CaliberFromText(text, reference) ?? "";
                    if (caliber.Length == 0 && !barrelCaliber.ContainsKey(item.Id) &&
                        familyCaliber.TryGetValue(Family(name), out var byFamily))
                    {
                        caliber = byFamily;
                    }

                    if (caliber.Length == 0 && !barrelCaliber.TryGetValue(item.Id, out caliber))
                    {
                        // a barrel whose caliber we could not work out is still a barrel:
                        // clamping it to a couple of percent would quietly hand a sawn-off
                        // Mosin full-length ballistics. Leave it and say so
                        skipped.Add(new Change
                        {
                            Name = name, Caliber = "?", LengthMm = ParseLength(text), Old = old,
                            New = old, Note = "no weapon links this barrel to a caliber",
                        });
                        continue;
                    }

                    var barrelLength = ParseLength(text);
                    var barrelEvidence = $"barrel {parts.BarrelEvidence(item)}";

                    if (barrelLength <= 0)
                    {
                        // a barrel sold as a weapon's rather than as a length — "MK-12
                        // Mod 0 Barrel" — is that weapon's barrel, and the book knows how
                        // long that is. Its name only, for the same reason a weapon's
                        // description is not read: this one's says which rifles it fits
                        barrelLength = LengthFromPrototype(parts.NameTextOf(item).ToList(),
                            reference, out var namedAfter);
                        if (barrelLength > 0)
                        {
                            barrelEvidence = $"barrel, prototype {namedAfter}";
                        }
                    }

                    Normalize(p, name, caliber, barrelLength, reference, b, changed, skipped,
                        barrelEvidence);
                    continue;
                }

                case PartRole.IntegratedBarrel:
                    NormalizeIntegrated(item, p, parts, reference, b, changed, skipped);
                    continue;

                case PartRole.MuzzleDevice:
                    // a brake or a can moves muzzle velocity by a hair either way, never
                    // by the double digits some of them claim
                    if (Math.Abs(old) > b.DeviceClampPercent)
                    {
                        p.Velocity = Math.Clamp(old, -b.DeviceClampPercent, b.DeviceClampPercent);
                        changed.Add(new Change
                        {
                            Name = name, Caliber = "-", Old = old, New = p.Velocity.Value,
                            Note = "device clamp",
                        });
                    }

                    continue;

                default:
                    // nothing in the data says what this is, so nothing here is entitled
                    // to overwrite it. Clamping on a guess is how a barrel ends up with
                    // the ballistics of a muzzle brake
                    if (Math.Abs(old) > b.DeviceClampPercent)
                    {
                        skipped.Add(new Change
                        {
                            Name = name, Caliber = "-", Old = old, New = old,
                            Note = "unclassified part carrying a velocity modifier",
                        });
                    }

                    continue;
            }
        }

        WriteReport(modPath, changed, skipped);
        Summary = $"{changed.Count} barrels";
        logger.Debug($"[PLATE] BarrelNormalizer: {changed.Count} adjusted, {skipped.Count} left alone");
    }

    /// <summary>
    /// A weapon carries a modifier of its own only when its barrel does not come off:
    /// otherwise the barrel item it is wearing is what got normalized.
    /// </summary>
    private void NormalizeWeapon(TemplateItem item, TemplateItemProperties p, PartClassifier parts,
        ReferenceBook.AmmoReference reference, PlateServerConfig.BarrelSection b,
        HashSet<MongoId> hasRemovableBarrel, Dictionary<string, double> byModel,
        List<Change> changed, List<Change> skipped)
    {
        if (hasRemovableBarrel.Contains(item.Id))
        {
            return;
        }

        var name = item.Name ?? "";
        var old = p.Velocity!.Value;

        // what a weapon is called, and deliberately not what is written about it: a
        // description is prose, and prose names other guns. The AS-1 is a bullpup on an
        // AK-74M whose card recounts the trials the AK-12 won, and reading that as "this
        // is an AK-12" was luck rather than reasoning - it would have handed the rifle
        // an AKS-74U's barrel just as readily. A barrel's description is about that
        // barrel and is still read; a weapon's is a history lesson
        var text = parts.NameTextOf(item).ToList();

        // a fixed barrel's length exists nowhere in the data — only in the prototype the
        // weapon is modelled on
        var length = reference.Weapons.TryGetValue(name, out var w) ? w.LengthMm : 0;
        var evidence = "fixed barrel";

        if (length <= 0 && byModel.TryGetValue(ModelOf(item), out var sameModel))
        {
            // a pack that renames a vanilla weapon without giving it a model of its own
            // has not built a different gun: the Century Arms Draco wears the AKS-74U and
            // is that carbine rebarreled, whatever the 12.25 inch original measures. What
            // the item is built as outranks what the pack wrote on it
            length = sameModel;
            evidence = "fixed barrel, same model as a known weapon";
        }

        if (length <= 0)
        {
            // the key is the template's _name, and a pack that rechambers a vanilla
            // weapon rewrites it: the AKS-74U reappears as "[Pack]_(Kalashnikov AKS-74U
            // .300 Blackout Assault Rifle)" and the book stops recognizing a weapon whose
            // barrel it knows. What survives the rename is the prototype's human name,
            // sitting in plain sight in the very entry that carries the length
            length = LengthFromPrototype(text, reference, out var prototype);
            if (length > 0)
            {
                evidence = $"fixed barrel, prototype {prototype}";
            }
        }

        // the declared caliber is not always the truth either: one pack ships the NSV, a
        // 12.7x108 heavy machine gun, declaring 5.45x39, and normalizing a 1100 mm barrel
        // against an AK's reference hands it a fat velocity bonus. Where the name says
        // otherwise, the name wins
        var declared = CaliberFromText(text, reference) ?? p.AmmoCaliber!;
        var row = Normalize(p, name, declared, length, reference, b, changed, skipped, evidence);

        // a fixed-barrel weapon sitting at 0% and unknown to the reference book is simply
        // neutral; saying so every time would bury the entries worth acting on
        if (row != null && Math.Abs(old) < 0.01)
        {
            skipped.Remove(row);
        }
    }

    /// <summary>
    /// A part with a barrel inside it. No length can be read off it and the length model
    /// does not apply — an MP5SD's ports, not its 146 mm, are what put the round below
    /// the speed of sound — so the figure comes from the reference book or not at all.
    ///
    /// The book states where the weapon should end up; the game adds the weapon's own
    /// modifier to the part's, so the part is handed the difference.
    /// </summary>
    private void NormalizeIntegrated(TemplateItem item, TemplateItemProperties p, PartClassifier parts,
        ReferenceBook.AmmoReference reference, PlateServerConfig.BarrelSection b,
        List<Change> changed, List<Change> skipped)
    {
        var name = item.Name ?? "";
        var old = p.Velocity!.Value;
        var row = new Change { Name = name, Caliber = "-", Old = old, New = old };

        if (!reference.IntegratedBarrels.TryGetValue(name, out var spec))
        {
            // never clamped: whatever this is, it is carrying a barrel, and a barrel is
            // entitled to move velocity by a lot. Silence here only costs a report line
            if (Math.Abs(old) > b.DeviceClampPercent)
            {
                row.Note = "integrated barrel, not in the reference book";
                skipped.Add(row);
            }

            return;
        }

        var host = parts.IntegratedHost(item.Id);
        var hostPercent = 0.0;
        var note = "integrated barrel";

        if (host?.Properties != null)
        {
            var hostName = host.Name ?? "";
            var caliber = CaliberFromText(parts.TextOf(host), reference) ?? host.Properties.AmmoCaliber ?? "";
            var length = reference.Weapons.TryGetValue(hostName, out var w) ? w.LengthMm : 0;
            hostPercent = ComputePercent(caliber, length, reference, b) ?? 0;
        }
        else
        {
            // several weapons take this part, or none does: nobody can be held
            // responsible for a modifier of their own, so the part carries the lot
            note = "integrated barrel, no single host";
        }

        row.New = Math.Round(spec.TotalPercent - hostPercent, 2);
        row.Note = note;
        p.Velocity = row.New;
        changed.Add(row);
    }

    /// <summary>Returns the row when the item was left alone, null when it was adjusted.</summary>
    private Change? Normalize(TemplateItemProperties p, string name, string caliber,
        double lengthMm, ReferenceBook.AmmoReference reference, PlateServerConfig.BarrelSection b,
        List<Change> changed, List<Change> skipped, string evidence)
    {
        var old = p.Velocity!.Value;
        var row = new Change { Name = name, Caliber = caliber, LengthMm = lengthMm, Old = old, New = old };

        if (!reference.Barrels.TryGetValue(caliber, out var spec))
        {
            row.Note = "caliber not in the reference book";
            skipped.Add(row);
            return row;
        }

        if (lengthMm < b.MinLengthMm || lengthMm > b.MaxLengthMm)
        {
            row.Note = lengthMm <= 0 ? "no barrel length known" : "length outside the sane band";
            skipped.Add(row);
            return row;
        }

        var c = spec.C > 0 ? spec.C : BarrelModel.EstimateC(spec.CaseMm3, spec.BoreMm);
        if (c <= 0)
        {
            row.Note = "no C and nothing to derive one from";
            skipped.Add(row);
            return row;
        }

        var percent = BarrelModel.VelocityPercent(lengthMm, spec.RefMm, c);
        if (percent < -b.MaxPercent || percent > b.MaxPercent)
        {
            // the model does not produce these; a name whose number is not a barrel
            // length does. Better to leave the item alone and say so
            row.Note = $"computed {percent:N1}% is outside the sane band";
            skipped.Add(row);
            return row;
        }

        row.New = Math.Round(percent, 2);
        row.Note = $"{(spec.C > 0 ? "C measured" : "C from case")}, {evidence}";
        p.Velocity = row.New;
        changed.Add(row);
        return null;
    }

    /// <summary>
    /// What the length model says, or null when it says nothing usable. The same
    /// arithmetic <see cref="Normalize"/> performs, without the bookkeeping: an
    /// integrated barrel needs to know what its host weapon was given.
    /// </summary>
    private static double? ComputePercent(string caliber, double lengthMm,
        ReferenceBook.AmmoReference reference, PlateServerConfig.BarrelSection b)
    {
        if (!reference.Barrels.TryGetValue(caliber, out var spec)
            || lengthMm < b.MinLengthMm || lengthMm > b.MaxLengthMm)
        {
            return null;
        }

        var c = spec.C > 0 ? spec.C : BarrelModel.EstimateC(spec.CaseMm3, spec.BoreMm);
        if (c <= 0)
        {
            return null;
        }

        var percent = BarrelModel.VelocityPercent(lengthMm, spec.RefMm, c);
        return percent < -b.MaxPercent || percent > b.MaxPercent ? null : Math.Round(percent, 2);
    }

    /// <summary>
    /// Caliber read off whatever the item is called. Vanilla names carry it as the bare
    /// dimensions — barrel_glock_glock_114mm_9x19_std — while the id spells it with a
    /// suffix, Caliber9x19PARA, so the id is matched by its dimensions with the trailing
    /// letters dropped. Packs write the dimensions with dots ("5.56x45") or not at all
    /// (".300 Blackout"), which is what the dot-stripped copy and the reference book's
    /// aliases are for. Only an unambiguous single hit counts: a name claimed by two
    /// calibers teaches nothing and the slot graph decides instead.
    /// </summary>
    public static string? CaliberFromText(IEnumerable<string> texts, ReferenceBook.AmmoReference reference)
    {
        var pool = new List<string>();
        foreach (var text in texts)
        {
            pool.Add(text);

            // dots because a pack writes "5.56x45" where the book's key says 556x45, and
            // the Cyrillic х because a Russian pack writes "5.56х45" in a name that is
            // otherwise Latin — the two letters are indistinguishable on screen and are
            // different characters
            var stripped = text.Replace(".", "").Replace(",", "").Replace('х', 'x').Replace('Х', 'X');
            if (stripped != text)
            {
                pool.Add(stripped);
            }
        }

        string? hit = null;

        foreach (var id in reference.Barrels.Keys)
        {
            if (!Claims(id, reference.Barrels[id], pool))
            {
                continue;
            }

            if (hit != null && hit != id)
            {
                return null;
            }

            hit = id;
        }

        return hit;
    }

    /// <summary>
    /// Shortest prototype name allowed to identify a weapon. "PM" would otherwise find
    /// itself inside every 9x18PM there is; three characters is where a name starts
    /// being a name.
    /// </summary>
    private const int MinPrototypeChars = 3;

    /// <summary>The model an item is drawn with, or "" when it has none.</summary>
    private static string ModelOf(TemplateItem item) => item.Properties?.Prefab?.Path ?? "";

    /// <summary>
    /// Barrel length per weapon model, for the weapons the book knows.
    ///
    /// A pack that rebrands a vanilla weapon without drawing a new one has not built a
    /// different gun: it ships the same model, the same handling and the same barrel
    /// under another name, and a rechambering does not move the muzzle. So where such an
    /// item and a known weapon are the same object on screen, they are the same length.
    ///
    /// Only models that belong to a single known weapon count. Two weapons sharing a
    /// model and disagreeing about the barrel teach nothing, and a model no weapon in
    /// the book uses answers nothing.
    /// </summary>
    private static Dictionary<string, double> MapModelsToLengths(
        Dictionary<MongoId, TemplateItem> items, ReferenceBook.AmmoReference reference)
    {
        var found = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.Values)
        {
            var model = ModelOf(item);
            if (model.Length == 0 || string.IsNullOrEmpty(item.Properties?.AmmoCaliber)
                || !reference.Weapons.TryGetValue(item.Name ?? "", out var spec) || spec.LengthMm <= 0)
            {
                continue;
            }

            if (found.TryGetValue(model, out var seen) && Math.Abs(seen - spec.LengthMm) > 0.01)
            {
                ambiguous.Add(model);
            }

            found[model] = spec.LengthMm;
        }

        foreach (var model in ambiguous)
        {
            found.Remove(model);
        }

        return found;
    }

    /// <summary>
    /// Barrel length of the prototype the item is named after, or 0.
    ///
    /// The reference book is keyed by the template's _name, which a weapon pack rewrites
    /// when it clones a weapon; the prototype's human name it writes instead is the same
    /// one the book already carries next to the length. Matching is on whole names — an
    /// AK-12K is not an AK-12 and its barrel is shorter, so a prefix must not count — and
    /// the longest name wins, which is what separates an AKS-74UB from an AKS-74U and a
    /// Uzi Pro from a Uzi. Two prototypes of the same length disagreeing about the barrel
    /// resolve to nothing, as everywhere else.
    /// </summary>
    public static double LengthFromPrototype(IEnumerable<string> texts,
        ReferenceBook.AmmoReference reference, out string prototype)
    {
        // hyphens go because a pack writes the internal name of what it backported —
        // "weapon_izhmash_ak308_762x51" — where the book says AK-308
        var pool = texts.SelectMany(t => new[] { t, t.Replace("-", "") }).Distinct().ToList();
        var best = "";
        var length = 0.0;
        var tied = false;

        foreach (var spec in reference.Weapons.Values)
        {
            var name = spec.Prototype;
            if (name.Length < MinPrototypeChars || name.Length < best.Length || spec.LengthMm <= 0
                || !pool.Any(t => ContainsWholeName(t, name) || ContainsWholeName(t, name.Replace("-", ""))))
            {
                continue;
            }

            if (name.Length > best.Length)
            {
                best = name;
                length = spec.LengthMm;
                tied = false;
            }
            else if (Math.Abs(spec.LengthMm - length) > 0.01)
            {
                tied = true;
            }
        }

        prototype = tied ? "" : best;
        return tied ? 0 : length;
    }

    /// <summary>
    /// Whether the text carries the name as a whole token. Letters, digits and hyphens
    /// count as part of a name, so "AK-12" is not found inside "AK-12K" and "PM" is not
    /// found inside "9x18PM". The underscore is a separator rather than a letter,
    /// because that is what it is in every name the game itself writes.
    /// </summary>
    private static bool ContainsWholeName(string text, string name)
    {
        var at = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);

        while (at >= 0)
        {
            var before = at == 0 || !IsNamePart(text[at - 1]);
            var end = at + name.Length;
            var after = end == text.Length || !IsNamePart(text[end]);
            if (before && after)
            {
                return true;
            }

            at = text.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '-';

    /// <summary>Whether a caliber's dimensions or one of its trade names is in the text.</summary>
    private static bool Claims(string id, ReferenceBook.BarrelRef spec, List<string> pool)
    {
        var token = id.StartsWith("Caliber", StringComparison.OrdinalIgnoreCase)
            ? id.Substring("Caliber".Length)
            : id;
        token = token.TrimEnd('A', 'B', 'C', 'P', 'R', 'M', 'N', 'O', 'T', 'S', 'E', 'D');

        if (token.Length >= 3 && pool.Any(t => t.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return spec.Aliases.Any(alias =>
            alias.Length > 0 && pool.Any(t => t.Contains(alias, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Weapon family a barrel belongs to: "barrel_mosin_izhmash_..." -> "barrel_mosin".</summary>
    private static string Family(string name)
    {
        var parts = name.Split('_');
        return parts.Length >= 2 ? $"{parts[0]}_{parts[1]}" : name;
    }

    /// <summary>
    /// Caliber per weapon family, from the barrels that did resolve. Some barrels hang
    /// off a receiver no weapon lists directly — the sawn-off Mosin's are like that, and
    /// leaving them alone means a 200 mm barrel keeps a -54% modifier nobody derived.
    /// Only families whose resolved barrels all agree are used; a family that mixes
    /// calibers teaches nothing.
    /// </summary>
    private static Dictionary<string, string> MapFamiliesToCalibers(
        Dictionary<MongoId, TemplateItem> items,
        PartClassifier parts,
        Dictionary<MongoId, string> barrelCaliber,
        ReferenceBook.AmmoReference reference)
    {
        var families = new Dictionary<string, HashSet<string>>();

        foreach (var (id, caliber) in barrelCaliber)
        {
            if (!items.TryGetValue(id, out var item) || !PartClassifier.LooksLikeBarrelByName(item))
            {
                continue;
            }

            // the name knows better where it says so, and it is what teaches the family
            var known = CaliberFromText(parts.TextOf(item), reference) ?? caliber;
            var family = Family(item.Name ?? "");
            if (!families.TryGetValue(family, out var set))
            {
                set = new HashSet<string>();
                families[family] = set;
            }

            set.Add(known);
        }

        return families
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value.First());
    }

    /// <summary>Most-voted caliber, or null on a dead heat — a tie teaches nothing.</summary>
    private static string? Majority(Dictionary<string, int> votes)
    {
        string? best = null;
        var bestCount = 0;
        var tied = false;

        foreach (var (caliber, count) in votes)
        {
            if (count > bestCount)
            {
                best = caliber;
                bestCount = count;
                tied = false;
            }
            else if (count == bestCount)
            {
                tied = true;
            }
        }

        return tied ? null : best;
    }

    /// <summary>
    /// Barrel length in millimetres, from the first string that carries one. Millimetres
    /// are tried before inches so that "6.5 inch (165mm)" answers with the exact figure
    /// rather than 165.1; a name with no unit at all answers with nothing, which leaves
    /// the item alone rather than guessing that "12.5 Carbine" means 12.5 of something.
    /// </summary>
    public static double ParseLength(IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            var mm = Millimetres.Match(text);
            if (mm.Success && TryNumber(mm.Groups[1].Value, out var millimetres))
            {
                return millimetres;
            }

            var inch = Inches.Match(text);
            if (inch.Success && TryNumber(inch.Groups[1].Value, out var inches))
            {
                return Math.Round(inches * MmPerInch, 1);
            }
        }

        return 0;
    }

    /// <summary>A number as either half of the world writes it.</summary>
    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// The item names as the player sees them. Read once: the locale table is lazy and
    /// re-deserializes the whole file on every access to Value.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ReadLocale(DatabaseServer databaseServer)
    {
        var global = databaseServer.GetTables().Locales?.Global;
        if (global == null || global.Count == 0)
        {
            return null;
        }

        if (!global.TryGetValue("en", out var lazy))
        {
            lazy = global.Values.FirstOrDefault();
        }

        return lazy?.Value;
    }

    /// <summary>
    /// Barrels carry no caliber of their own, so it comes from the weapons they fit:
    /// every weapon declares one, and its slot tree reaches its barrels.
    ///
    /// Fitting several calibers is the norm rather than an anomaly — an AR-15 upper
    /// takes 5.56 and .300 BLK barrels alike, and the same 260 mm barrel is listed by
    /// weapons of both. The item still carries a single velocity modifier whatever is
    /// loaded into it, so one caliber has to be picked, and the defensible pick is
    /// whichever most of the weapons that can mount it are chambered for.
    /// </summary>
    private static Dictionary<MongoId, string> MapBarrelsToCalibers(
        Dictionary<MongoId, TemplateItem> items, PartClassifier parts,
        out HashSet<MongoId> weaponsWithBarrels)
    {
        var found = new Dictionary<MongoId, Dictionary<string, int>>();
        var withBarrels = new HashSet<MongoId>();

        foreach (var weapon in items.Values)
        {
            var caliber = weapon.Properties?.AmmoCaliber;
            if (string.IsNullOrEmpty(caliber))
            {
                continue;
            }

            foreach (var id in parts.BarrelsUnder(weapon))
            {
                withBarrels.Add(weapon.Id);
                if (!found.TryGetValue(id, out var votes))
                {
                    votes = new Dictionary<string, int>();
                    found[id] = votes;
                }

                votes[caliber] = votes.TryGetValue(caliber, out var n) ? n + 1 : 1;
            }
        }

        weaponsWithBarrels = withBarrels;
        return found
            .Select(kv => new { kv.Key, Best = Majority(kv.Value) })
            .Where(x => x.Best != null)
            .ToDictionary(x => x.Key, x => x.Best!);
    }

    private void WriteReport(string modPath, List<Change> changed, List<Change> skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# PLATE barrel normalization report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("Muzzle velocity by barrel length follows Le Duc, `v = v∞·L/(L+C)`, calibrated "
                      + "against published barrel-length ladders. The modifier is relative to the "
                      + "reference barrel of the caliber — the one the cartridge's in-game muzzle "
                      + "velocity belongs to — so a barrel that length changes nothing.");
        sb.AppendLine();
        sb.AppendLine("`C measured` means the caliber has a fitted ladder behind it; `C from case` "
                      + "means it was derived from case capacity and is worth about ±35%.");
        sb.AppendLine();
        sb.AppendLine("What follows it says how the item was recognized as a barrel: `by class` and "
                      + "`by slot` come from the item database, `by props` from the two properties "
                      + "only barrels carry, `by name` from the vanilla naming convention alone.");
        sb.AppendLine();
        sb.AppendLine("Weapon packs clone items, so the same name can appear several times "
                      + "with different ids and different starting values.");
        sb.AppendLine();
        sb.AppendLine("| Item | Caliber | Length, mm | Was | Now | Source |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var c in changed.OrderBy(c => c.Caliber).ThenBy(c => c.LengthMm).ThenBy(c => c.Name))
        {
            var length = c.LengthMm > 0 ? $"{c.LengthMm:N0}" : "—";
            sb.AppendLine($"| {c.Name} | {c.Caliber} | {length} | {c.Old:N1}% | **{c.New:N1}%** | {c.Note} |");
        }

        if (skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Left alone");
            sb.AppendLine();
            sb.AppendLine("These keep whatever modifier they came with. An entry here is an invitation "
                          + "to add the item to `ammo-reference.jsonc` — a weapon with a fixed barrel "
                          + "needs its prototype length, a caliber needs its reference barrel and case, "
                          + "a part with a barrel built into it needs an `IntegratedBarrels` entry.");
            sb.AppendLine();
            sb.AppendLine("| Item | Caliber | Velocity | Why |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var c in skipped.OrderBy(c => c.Caliber).ThenBy(c => c.Name))
            {
                sb.AppendLine($"| {c.Name} | {c.Caliber} | {c.Old:N1}% | {c.Note} |");
            }
        }

        try
        {
            File.WriteAllText(System.IO.Path.Combine(modPath, "plate-barrel-report.md"), sb.ToString());
        }
        catch (Exception ex)
        {
            logger.Warning($"[PLATE] Could not write the barrel report: {ex.Message}");
        }
    }
}
