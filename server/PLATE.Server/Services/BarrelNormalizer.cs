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

    /// <summary>Length in millimetres as weapon packs and the base game both spell it.</summary>
    private static readonly Regex LengthInName = new(@"(\d{2,4})mm", RegexOptions.Compiled);

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

        var barrelCaliber = MapBarrelsToCalibers(items, out var hasRemovableBarrel);
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

            if (barrelCaliber.TryGetValue(item.Id, out var caliber))
            {
                Normalize(item, p, caliber, ParseLength(name), reference, b, changed, skipped);
                continue;
            }

            if (!string.IsNullOrEmpty(p.AmmoCaliber))
            {
                // a weapon whose barrel comes off carries nothing of its own: the barrel
                // item it is wearing is what got normalized above
                if (hasRemovableBarrel.Contains(item.Id))
                {
                    continue;
                }

                // the rest have a fixed barrel, and its length exists nowhere in the data
                // - only in the prototype the weapon is modelled on
                var length = reference.Weapons.TryGetValue(name, out var w) ? w.LengthMm : 0;
                var row = Normalize(item, p, p.AmmoCaliber, length, reference, b, changed, skipped);

                // a fixed-barrel weapon sitting at 0% and unknown to the reference book is
                // simply neutral; saying so every time would bury the entries worth acting on
                if (row != null && Math.Abs(old) < 0.01)
                {
                    skipped.Remove(row);
                }

                continue;
            }

            // muzzle devices, suppressors, handguards: a brake or a can moves muzzle
            // velocity by a hair either way, never by the double digits some of them claim
            if (Math.Abs(old) > b.DeviceClampPercent)
            {
                p.Velocity = Math.Clamp(old, -b.DeviceClampPercent, b.DeviceClampPercent);
                changed.Add(new Change
                {
                    Name = name, Caliber = "-", Old = old, New = p.Velocity.Value, Note = "device clamp",
                });
            }
        }

        WriteReport(modPath, changed, skipped);
        Summary = $"{changed.Count} barrels";
        logger.Debug($"[PLATE] BarrelNormalizer: {changed.Count} adjusted, {skipped.Count} left alone");
    }

    /// <summary>Returns the row when the item was left alone, null when it was adjusted.</summary>
    private Change? Normalize(TemplateItem item, TemplateItemProperties p, string caliber,
        double lengthMm, ReferenceBook.AmmoReference reference, PlateServerConfig.BarrelSection b,
        List<Change> changed, List<Change> skipped)
    {
        var name = item.Name ?? "";
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
        row.Note = spec.C > 0 ? "C measured" : "C from case";
        p.Velocity = row.New;
        changed.Add(row);
        return null;
    }

    private static double ParseLength(string name)
    {
        var m = LengthInName.Match(name);
        return m.Success ? double.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>
    /// Barrels carry no caliber of their own, so it comes from the weapons they fit:
    /// every weapon declares one, and its slot tree reaches its barrels. A barrel that
    /// somehow fits two calibers is left out rather than guessed at.
    /// </summary>
    private static Dictionary<MongoId, string> MapBarrelsToCalibers(
        Dictionary<MongoId, TemplateItem> items, out HashSet<MongoId> weaponsWithBarrels)
    {
        var found = new Dictionary<MongoId, HashSet<string>>();
        var withBarrels = new HashSet<MongoId>();

        foreach (var weapon in items.Values)
        {
            var caliber = weapon.Properties?.AmmoCaliber;
            if (string.IsNullOrEmpty(caliber))
            {
                continue;
            }

            foreach (var id in BarrelsUnder(weapon, items))
            {
                withBarrels.Add(weapon.Id);
                if (!found.TryGetValue(id, out var set))
                {
                    set = new HashSet<string>();
                    found[id] = set;
                }

                set.Add(caliber);
            }
        }

        weaponsWithBarrels = withBarrels;
        return found
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value.First());
    }

    /// <summary>Barrel items reachable through a weapon's slots, including nested ones.</summary>
    private static IEnumerable<MongoId> BarrelsUnder(TemplateItem weapon,
        Dictionary<MongoId, TemplateItem> items)
    {
        var seen = new HashSet<MongoId>();
        var queue = new Queue<TemplateItem>();
        queue.Enqueue(weapon);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var slots = current.Properties?.Slots;
            if (slots == null)
            {
                continue;
            }

            foreach (var slot in slots)
            {
                var filters = slot.Properties?.Filters;
                if (filters == null)
                {
                    continue;
                }

                foreach (var id in filters.SelectMany(f => f.Filter ?? []))
                {
                    if (!seen.Add(id) || !items.TryGetValue(id, out var child))
                    {
                        continue;
                    }

                    if ((child.Name ?? "").StartsWith("barrel_", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return id;
                    }

                    // handguards and gas blocks can hold a barrel further down
                    queue.Enqueue(child);
                }
            }
        }
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
        sb.AppendLine("| Item | Caliber | Length, mm | Was | Now | Source |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var c in changed.OrderBy(c => c.Caliber).ThenBy(c => c.LengthMm))
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
                          + "needs its prototype length, a caliber needs its reference barrel and case.");
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

