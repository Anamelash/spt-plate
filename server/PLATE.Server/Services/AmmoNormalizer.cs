using System.Text;
using System.Text.Json;
using PLATE.Server.Config;
using PLATE.Server.Routes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace PLATE.Server.Services;

/// <summary>
/// Ammo normalization (including rounds added by other mods).
/// Fills missing fields; derives the expansiveness index X; recomputes Damage
/// strictly from energy and PenetrationPower from E/A with a blend factor;
/// writes a report to plate-ammo-report.md plus machine data to plate-ammo-data.json.
/// Works in memory on top of the loaded DB — items.json on disk is untouched,
/// so the pass is idempotent from restart to restart.
/// </summary>
[Injectable]
public class AmmoNormalizer(
    DatabaseServer databaseServer,
    ReferenceBook referenceBook,
    ISptLogger<AmmoNormalizer> logger)
{
    private class Rec
    {
        public required TemplateItem Item;
        public required TemplateItemProperties P;
        public string Caliber = "";
        public bool IsBuckshot;
        public double MassG, DiaMm, E0, Area, Sd, Sp, FragChance, X;
        public double OldDamage, NewDamage;
        public int OldPen, NewPen;
        public double OldPdm, NewPdm;
        public bool EnergyOutlier;
        public double? RefX; // X from the spec reference book (buckshot/flechette)
        public readonly List<string> Notes = new();
    }

    public void Run(PlateServerConfig cfg, string modPath)
    {
        var a = cfg.AmmoNormalizer;
        var items = databaseServer.GetTables().Templates?.Items;
        if (items == null)
        {
            logger.Error("[PLATE] AmmoNormalizer: item DB unavailable");
            return;
        }

        // --- Collect candidates ---
        var recs = new List<Rec>();
        foreach (var item in items.Values)
        {
            var p = item.Properties;
            if (p?.Caliber == null || string.IsNullOrEmpty(p.AmmoType))
            {
                continue;
            }

            if (p.AmmoType != "bullet" && p.AmmoType != "buckshot")
            {
                continue; // launcher grenades (PG/VOG), flares, etc.
            }

            if ((p.InitialSpeed ?? 0) <= 0 || (p.Damage ?? 0) <= 0)
            {
                continue;
            }

            recs.Add(new Rec
            {
                Item = item,
                P = p,
                Caliber = p.Caliber,
                IsBuckshot = p.AmmoType == "buckshot",
                FragChance = p.FragmentationChance ?? 0,
                OldDamage = p.Damage ?? 0,
                OldPen = p.PenetrationPower ?? 0,
                OldPdm = p.PenetrationDamageMod ?? 0,
            });
        }

        // --- Prototype spec reference book: forced masses/velocities (anchors) ---
        var reference = referenceBook.Load(modPath);
        var refApplied = 0;
        foreach (var r in recs)
        {
            var key = r.Item.Name ?? "";
            if (!reference.Shotshells.TryGetValue(key, out var rf))
            {
                continue;
            }

            if (rf.PelletMassG > 0)
            {
                r.P.BulletMassGram = rf.PelletMassG;
            }

            if (rf.V0 > 0)
            {
                r.P.InitialSpeed = rf.V0;
            }

            if (rf.PelletCount > 0 && (r.P.ProjectileCount ?? 0) != rf.PelletCount)
            {
                r.Notes.Add($"pellets {r.P.ProjectileCount} -> {rf.PelletCount}");
                r.P.ProjectileCount = rf.PelletCount;
            }

            r.RefX = rf.X;
            r.Notes.Add($"ref specs: {rf.Prototype}");
            refApplied++;
        }

        // --- Fill missing mass/diameter/BC from caliber cohort medians ---
        var byCaliber = recs.GroupBy(r => r.Caliber).ToDictionary(g => g.Key, g => g.ToList());
        var fills = 0;
        foreach (var r in recs)
        {
            var cohort = byCaliber[r.Caliber];
            r.MassG = r.P.BulletMassGram ?? 0;
            r.DiaMm = r.P.BulletDiameterMilimeters ?? 0;

            if (r.MassG <= 0)
            {
                r.MassG = Median(cohort.Where(c => (c.P.BulletMassGram ?? 0) > 0)
                    .Select(c => c.P.BulletMassGram!.Value)) ?? EstimateMassFromCaliber(r.Caliber);
                r.P.BulletMassGram = r.MassG;
                r.Notes.Add($"mass filled {r.MassG:0.##}g");
                fills++;
            }

            if (r.DiaMm <= 0)
            {
                r.DiaMm = Median(cohort.Where(c => (c.P.BulletDiameterMilimeters ?? 0) > 0)
                    .Select(c => c.P.BulletDiameterMilimeters!.Value)) ?? ParseCaliberDiameter(r.Caliber);
                r.P.BulletDiameterMilimeters = r.DiaMm;
                r.Notes.Add($"diameter filled {r.DiaMm:0.##}mm");
                fills++;
            }

            if ((r.P.BallisticCoeficient ?? 0) <= 0)
            {
                var bc = Median(cohort.Where(c => (c.P.BallisticCoeficient ?? 0) > 0)
                    .Select(c => c.P.BallisticCoeficient!.Value)) ?? 0.2;
                r.P.BallisticCoeficient = bc;
                r.Notes.Add($"BC filled {bc:0.###}");
                fills++;
            }

            var v = r.P.InitialSpeed!.Value;
            r.E0 = 0.5 * (r.MassG / 1000.0) * v * v;
            r.Area = Math.PI * (r.DiaMm / 2.0) * (r.DiaMm / 2.0);
            r.Sd = r.OldDamage / Math.Max(r.E0, 1);
            r.Sp = r.OldPen * r.Area / Math.Max(r.E0, 1);

            if (r.E0 is < 30 or > 30000)
            {
                // suspicious data (usually broken modded fields) — leave untouched, only report
                r.EnergyOutlier = true;
                r.Notes.Add($"ENERGY OUTLIER {r.E0:0}J — rescale skipped, check mass/velocity");
            }
        }

        // --- Expansiveness index X ---
        var bullets = recs.Where(r => !r.IsBuckshot).ToList();
        var globalSdResiduals = LogResiduals(bullets, r => r.Sd, out var sdFit);
        var globalSpResiduals = LogResiduals(bullets, r => Math.Max(r.Sp, 1e-4), out var spFit);

        foreach (var r in bullets)
        {
            var cohort = byCaliber[r.Caliber].Where(c => !c.IsBuckshot).ToList();
            double pctSd, pctSp;
            if (cohort.Count >= a.MinCaliberCohort)
            {
                pctSd = Percentile(cohort.Select(c => c.Sd), r.Sd);
                pctSp = Percentile(cohort.Select(c => c.Sp), r.Sp);
            }
            else
            {
                pctSd = Percentile(globalSdResiduals, Residual(sdFit, r, x => x.Sd));
                pctSp = Percentile(globalSpResiduals, Residual(spFit, r, x => Math.Max(x.Sp, 1e-4)));
                r.Notes.Add("small cohort -> global regression");
            }

            var xRaw = a.WeightSpecificDamage * pctSd
                       - a.WeightSpecificPenetration * pctSp
                       + a.WeightFragmentation * Math.Clamp(r.FragChance / a.FragChanceNormalizer, 0, 1);
            r.X = Math.Clamp(0.5 + xRaw, 0, 1);

            ValidateSuffix(r);
        }

        // --- PenetrationDamageMod from X (only when not already set) ---
        var pdmFills = 0;
        foreach (var r in bullets.Where(r => r.OldPdm <= 0 && !r.EnergyOutlier))
        {
            r.NewPdm = Math.Round(a.PdmMax * (1 - r.X), 3);
            r.P.PenetrationDamageMod = r.NewPdm;
            pdmFills++;
        }

        // --- Damage from the wound channel model (PC+TC) + penetration from E/A ---
        var rescaled = 0;
        if (a.RescaleDamageFromEnergy)
        {
            foreach (var r in bullets.Where(r => !r.EnergyOutlier))
            {
                // multi-projectile "bullets" (Piranha, duplex) — damage floor as for buckshot
                var floor = (r.P.ProjectileCount ?? 1) > 1 ? a.MinPelletDamage : 1;
                r.NewDamage = Math.Clamp(ComputeDamage(r, a), floor, a.DamageCap);
                r.P.Damage = r.NewDamage;
                ApplyBleedDeltas(r, a, pellet: (r.P.ProjectileCount ?? 1) > 1);

                var penEnergy = a.PenPerEnergyDensity * (r.E0 / r.Area)
                                * (1 + a.PenConstructionFactor * (0.5 - r.X));
                r.NewPen = (int)Math.Clamp(
                    Math.Round(a.PenetrationBlend * penEnergy + (1 - a.PenetrationBlend) * r.OldPen),
                    1, 120);
                r.P.PenetrationPower = r.NewPen;
                rescaled++;
            }
        }

        // --- Buckshot: per-pellet Damage/Pen from energy using reference masses.
        // X does not come from statistics (pellets are not comparable to cohort
        // bullets) but from the reference book/default.
        var buckshotRescaled = 0;
        if (a.RescaleDamageFromEnergy && a.NormalizeBuckshot)
        {
            foreach (var r in recs.Where(r => r.IsBuckshot && !r.EnergyOutlier))
            {
                r.X = r.RefX ?? a.XBuckshotDefault;
                r.NewDamage = Math.Clamp(ComputeDamage(r, a), a.MinPelletDamage, a.DamageCap);
                r.P.Damage = r.NewDamage;

                var penEnergy = a.PenPerEnergyDensity * (r.E0 / r.Area)
                                * (1 + a.PenConstructionFactor * (0.5 - r.X));
                r.NewPen = (int)Math.Clamp(
                    Math.Round(a.PenetrationBlend * penEnergy + (1 - a.PenetrationBlend) * r.OldPen),
                    1, 120);
                r.P.PenetrationPower = r.NewPen;

                if (r.OldPdm <= 0)
                {
                    r.NewPdm = Math.Round(a.PdmMax * (1 - r.X), 3);
                    r.P.PenetrationDamageMod = r.NewPdm;
                }

                ApplyBleedDeltas(r, a, pellet: true);
                buckshotRescaled++;
            }
        }

        logger.Success($"[PLATE] AmmoNormalizer: {recs.Count} ammo ({bullets.Count} bullets, " +
                       $"{recs.Count - bullets.Count} buckshot), reference hits: {refApplied}, " +
                       $"field fills: {fills}, PDM computed: {pdmFills}, " +
                       $"rescaled: {rescaled} bullets + {buckshotRescaled} buckshot");

        // Machine data is always kept in memory — served to the client via /plate/ammo-data
        PlateAmmoData.Json = BuildMachineData(
            bullets.Concat(recs.Where(r => r.IsBuckshot && r.NewDamage > 0)).ToList(), cfg);

        // --- Report ---
        if (a.WriteReport)
        {
            WriteReport(modPath, recs, a);
            File.WriteAllText(System.IO.Path.Combine(modPath, "plate-ammo-data.json"), PlateAmmoData.Json);
        }
    }

    /// <summary>
    /// Damage for a record: the wound channel model (WoundModel, PC+TC) or, when it
    /// is disabled, the legacy linear energy formula E0/EnergyPerHp. The PC/TC
    /// breakdown goes into the report notes.
    /// </summary>
    private static double ComputeDamage(Rec r, PlateServerConfig.AmmoNormalizerSection a)
    {
        if (!a.WoundChannelModel)
        {
            return Math.Round(r.E0 / a.EnergyPerHp);
        }

        var w = WoundModel.Compute(r.MassG, r.DiaMm, r.P.InitialSpeed!.Value, r.X,
            r.FragChance, a);
        r.Notes.Add($"PC {w.Pc:0.#}+TC {w.Tc:0.#}" +
                    (w.EnergyCapped ? $", cap E0/{a.EnergyCapPerHp:0.#}" : "") +
                    $", channel {w.DepthMm:0} mm");
        return Math.Round(w.Damage);
    }

    /// <summary>
    /// Bleeding deltas from wound channel geometry: light — from diameter,
    /// heavy — from diameter and expansiveness (a large expanding channel tears
    /// vessels). A pellet gets the PelletHeavyFactor multiplier (small channels).
    /// </summary>
    private static void ApplyBleedDeltas(Rec r, PlateServerConfig.AmmoNormalizerSection a,
        bool pellet)
    {
        if (!a.NormalizeBleedDeltas || r.DiaMm <= 0)
        {
            return;
        }

        var xf = 0.5 + 0.5 * r.X;
        var heavy = a.BleedHeavyPerMm * r.DiaMm * xf * (pellet ? a.PelletHeavyFactor : 1.0);
        r.P.HeavyBleedingDelta = Math.Round(Math.Clamp(heavy, 0, a.HeavyDeltaMax), 3);
        r.P.LightBleedingDelta = Math.Round(
            Math.Clamp(a.BleedLightBase + a.BleedLightPerMm * r.DiaMm, 0, a.LightDeltaMax), 3);
    }

    private void ValidateSuffix(Rec r)
    {
        var name = (r.P.Name ?? r.Item.Name ?? "").ToLowerInvariant();
        var apLike = name.EndsWith("_ap") || name.Contains("_ap_") || name.Contains("_bs") ||
                     name.Contains("_bp") || name.Contains("m995") || name.Contains("m61") ||
                     name.Contains("igolnik");
        var hpLike = name.Contains("_hp") || name.Contains("_sp") || name.Contains("rip") ||
                     name.Contains("hollow");
        if (apLike && r.X > 0.5)
        {
            r.Notes.Add($"SUFFIX MISMATCH: name looks AP, but X={r.X:0.00}");
        }
        else if (hpLike && r.X < 0.5)
        {
            r.Notes.Add($"SUFFIX MISMATCH: name looks HP, but X={r.X:0.00}");
        }
    }

    private void WriteReport(string modPath, List<Rec> recs, PlateServerConfig.AmmoNormalizerSection a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# PLATE ammo normalization report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        if (a.WoundChannelModel)
        {
            sb.AppendLine("**Damage/Pen below are display values** (item card, fallback): with " +
                          "the client-side physics model enabled, the actual damage and penetration " +
                          "are computed at the moment of impact from the projectile state.");
            sb.AppendLine();
        }

        sb.AppendLine(a.WoundChannelModel
            ? $"Total: {recs.Count}. Wound channel model: depth K={a.GelDepthK}·(m/A)·ln(v/{a.GelStopVelocity})·(1−{a.ExpansionDepthFactor}X), " +
              $"body {a.BodyDepthMm} mm, PC: A·(1+{a.ExpansionAreaFactor}X)·Lb/{a.WoundVolumePerHp} mm³/HP; " +
              $"TC: sigmoid(v; {a.TcVelocityCenter}±{a.TcVelocityWidth})·E·φ·(1+{a.TcFragBonus}·frag)/{a.TcEnergyPerHp} J/HP; " +
              $"budget E0/{a.EnergyCapPerHp}. blend(pen)={a.PenetrationBlend}, PdmMax={a.PdmMax}"
            : $"Total: {recs.Count}. EnergyPerHp={a.EnergyPerHp}, blend(pen)={a.PenetrationBlend}, " +
              $"PdmMax={a.PdmMax}, X weights: dmg {a.WeightSpecificDamage} / pen {a.WeightSpecificPenetration} / " +
              $"frag {a.WeightFragmentation}");
        sb.AppendLine();
        sb.AppendLine("| Cartridge | Caliber | E0, J | X | Damage | Pen | PDM | Notes |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in recs.OrderBy(r => r.Caliber).ThenBy(r => r.P.Name))
        {
            var dmg = r.NewDamage > 0 && Math.Abs(r.NewDamage - r.OldDamage) > 0.5
                ? $"{r.OldDamage:0} -> **{r.NewDamage:0}**"
                : $"{r.OldDamage:0}";
            var pen = r.NewPen > 0 && r.NewPen != r.OldPen
                ? $"{r.OldPen} -> **{r.NewPen}**"
                : $"{r.OldPen}";
            var pdm = r.NewPdm > 0
                ? $"0 -> **{r.NewPdm:0.###}**"
                : $"{r.OldPdm:0.###}";
            var buck = r.IsBuckshot ? (r.NewDamage > 0 ? "buckshot " : "buckshot (skip) ") : "";
            sb.AppendLine($"| {r.P.Name ?? r.Item.Name} | {r.Caliber.Replace("Caliber", "")} | " +
                          $"{r.E0:0} | {r.X:0.00} | {dmg} | {pen} | {pdm} | {buck}{string.Join("; ", r.Notes)} |");
        }

        File.WriteAllText(System.IO.Path.Combine(modPath, "plate-ammo-report.md"), sb.ToString());
    }

    private static string BuildMachineData(List<Rec> bullets, PlateServerConfig cfg)
    {
        // Machine data for the client: X, energy and fragmentation chance for each
        // cartridge + the wound channel model constants ("__wound") and physical
        // armor constants ("__armor") — the client computes everything at impact time.
        var a = cfg.AmmoNormalizer;
        var data = bullets.ToDictionary(
            r => r.Item.Id.ToString(),
            r => (object)new
            {
                X = Math.Round(r.X, 4),
                E0 = Math.Round(r.E0),
                Pdm = r.P.PenetrationDamageMod,
                Frag = Math.Round(r.FragChance, 4),
            });
        data["__wound"] = new
        {
            Enabled = a.WoundChannelModel,
            a.GelDepthK,
            a.GelStopVelocity,
            a.ExpansionDepthFactor,
            a.ExpansionAreaFactor,
            a.BodyDepthMm,
            a.WoundVolumePerHp,
            a.TcVelocityCenter,
            a.TcVelocityWidth,
            a.TcEnergyPerHp,
            a.TcFragBonus,
            a.EnergyCapPerHp,
        };
        data["__armor"] = new
        {
            Enabled = cfg.Armor.PhysicalArmor,
            cfg.Armor.ThresholdBand,
            cfg.Armor.AngleMinCos,
            cfg.Armor.ClassULimitJmm2,
            cfg.Armor.DurabilityFloor,
            cfg.Armor.DegradeFloor,
            PenConstructionFactor = a.PenConstructionFactor,
            cfg.Armor.Materials,
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    // --- math ---

    private static double? Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        return list.Count % 2 == 1
            ? list[list.Count / 2]
            : (list[list.Count / 2 - 1] + list[list.Count / 2]) / 2.0;
    }

    /// <summary>Fraction of cohort values strictly below the given one (0..1).</summary>
    private static double Percentile(IEnumerable<double> cohort, double value)
    {
        var list = cohort.ToList();
        if (list.Count <= 1)
        {
            return 0.5;
        }

        var below = list.Count(v => v < value);
        var equal = list.Count(v => v == value);
        return Math.Clamp((below + 0.5 * equal) / list.Count, 0, 1);
    }

    /// <summary>Log-log regression of metric(E0); returns residuals of all records.</summary>
    private static List<double> LogResiduals(List<Rec> recs, Func<Rec, double> metric,
        out (double A, double B) fit)
    {
        var xs = recs.Select(r => Math.Log(Math.Max(r.E0, 1))).ToList();
        var ys = recs.Select(r => Math.Log(Math.Max(metric(r), 1e-6))).ToList();
        var mx = xs.Average();
        var my = ys.Average();
        var denom = xs.Sum(x => (x - mx) * (x - mx));
        var b = denom < 1e-9 ? 0 : xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum() / denom;
        var a = my - b * mx;
        fit = (a, b);
        var f = fit;
        return recs.Select(r => Residual(f, r, metric)).ToList();
    }

    private static double Residual((double A, double B) fit, Rec r, Func<Rec, double> metric)
    {
        return Math.Log(Math.Max(metric(r), 1e-6)) - (fit.A + fit.B * Math.Log(Math.Max(r.E0, 1)));
    }

    private static double ParseCaliberDiameter(string caliber)
    {
        // "Caliber556x45NATO" -> 5.56, "Caliber762x39" -> 7.62, "Caliber9x19PARA" -> 9
        var digits = new string(caliber.Replace("Caliber", "").TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return 7.62;
        }

        var raw = double.Parse(digits);
        return raw switch
        {
            >= 100 => raw / 100.0, // 556 -> 5.56, 762 -> 7.62, 127 -> 12.7
            >= 20 => raw / 10.0,   // 23 -> 2.3? none exist; 40mm grenades are filtered out
            _ => raw,              // 9 -> 9mm, 12 -> 12.7? (12 = 12ga, but buckshot is skipped)
        };
    }

    private static double EstimateMassFromCaliber(string caliber)
    {
        // scale from a 7.62mm/8g reference bullet by the cube of the diameter
        var d = ParseCaliberDiameter(caliber);
        return Math.Round(8.0 * Math.Pow(d / 7.62, 3), 2);
    }
}
