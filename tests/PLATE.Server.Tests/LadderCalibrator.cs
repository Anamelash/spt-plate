using PLATE.Server.Services;

namespace PLATE.Server.Tests;

/// <summary>
/// Derives a failure-mode constant from published ladder rows — the arithmetic that
/// used to be done by hand every recalibration, with the one check the hand version
/// could not enforce: rows resting on different core assumptions never mix silently.
///
/// The constants themselves ship in Tuning.Default; this class is how they are
/// re-derived when the physics changes, and the tests hold its output against what
/// ships.
/// </summary>
public static class LadderCalibrator
{
    /// <summary>
    /// The mode constant that puts the model exactly on one published row. WorkJ is
    /// linear in its mode constant, so the constant is the work the row demands over
    /// the work the model computes at k = 1.
    /// </summary>
    public static double SolveK(ArmorStandardTests.BallisticLimit row)
    {
        return SolveK(row, BallisticLimit.Tuning.Default);
    }

    /// <summary>The same, against a tuning other than the shipped one — for deriving
    /// the terms the mode constant sits on top of.</summary>
    public static double SolveK(ArmorStandardTests.BallisticLimit row,
        BallisticLimit.Tuning tuning)
    {
        var m = ArmorStandardTests.LadderMaterials[row.Material];
        var barrier = LadderBarrier(row, m);
        var core = ArmorFixture.CoreOf(row.Threat);

        var unit = tuning;
        unit.DuctileK = unit.HoleGrowthK = unit.BrittleK = unit.FibrousK = 1;

        var perK = BallisticLimit.WorkJ(barrier, core, 1.0, row.V50, unit);
        if (perK <= 0)
        {
            throw new InvalidOperationException(
                $"{row.Material} at {row.ThicknessMm} mm computes no work at all — " +
                "nothing to calibrate against");
        }

        var demanded = 0.5 * (BallisticLimit.MassAgainst(barrier, core) / 1000.0)
                       * row.V50 * row.V50;
        return demanded / perK;
    }

    /// <summary>
    /// One constant from a set of rows sharing a single core assumption — the geometric
    /// mean of the per-row solutions, since the constant acts multiplicatively.
    ///
    /// Throws when the rows rest on different assumptions. A hardened core is a
    /// diameter and a mass, both of which sit inside the work integral; solving one
    /// constant across two different assumed cores folds the difference between the
    /// assumptions into the constant, where nobody will ever find it. The overload
    /// below is the only way past, and it demands the compensation in writing.
    /// </summary>
    public static double DeriveK(IReadOnlyList<ArmorStandardTests.BallisticLimit> rows)
    {
        return DeriveK(rows, BallisticLimit.Tuning.Default);
    }

    public static double DeriveK(IReadOnlyList<ArmorStandardTests.BallisticLimit> rows,
        BallisticLimit.Tuning tuning)
    {
        if (rows.Count == 0)
        {
            throw new ArgumentException("no rows to derive from", nameof(rows));
        }

        var sources = rows.Select(r => r.Core.SourceName).Distinct().ToArray();
        if (sources.Length > 1)
        {
            throw new InvalidOperationException(
                "calibration across conflicting core assumptions: " +
                string.Join(" vs ", sources.Select(s => $"'{s}'")) +
                ". Deriving one constant over both folds the difference between the " +
                "assumed cores into the constant. State the area compensation " +
                "explicitly, or derive per assumption");
        }

        return GeometricMeanK(rows, tuning);
    }

    /// <summary>
    /// The explicit way past the guard: rows with different assumptions, plus a written
    /// statement of how the area difference was compensated. The statement is recorded
    /// in the exception-free path on purpose — a caller who cannot say what the
    /// compensation is has no business being here.
    /// </summary>
    public static double DeriveKAcrossAssumptions(
        IReadOnlyList<ArmorStandardTests.BallisticLimit> rows, string areaCompensation)
    {
        if (string.IsNullOrWhiteSpace(areaCompensation))
        {
            throw new ArgumentException(
                "crossing assumptions demands the compensation in writing — an empty " +
                "note is the silence the contract forbids", nameof(areaCompensation));
        }

        if (rows.Count == 0)
        {
            throw new ArgumentException("no rows to derive from", nameof(rows));
        }

        return GeometricMeanK(rows, BallisticLimit.Tuning.Default);
    }

    private static double GeometricMeanK(
        IReadOnlyList<ArmorStandardTests.BallisticLimit> rows,
        BallisticLimit.Tuning tuning)
    {
        var logSum = rows.Sum(r => Math.Log(SolveK(r, tuning)));
        return Math.Exp(logSum / rows.Count);
    }

    /// <summary>The plate a ladder was shot at, exactly as BallisticLadderTests builds it.</summary>
    public static BallisticLimit.Barrier LadderBarrier(
        ArmorStandardTests.BallisticLimit row, ArmorStandardTests.LadderMaterial m)
    {
        var fibrous = m.Class == BallisticLimit.Fibrous;

        // A fibre row publishes two numbers because one of them would not be a
        // measurement: kg/m² over mm is g/cm³, and that over the fibre's own density is
        // how much of the pack is fibre. Nothing here is chosen — the sewn packs come
        // out at 0.48 and the pressed laminate at 0.61, which is the difference between
        // the two ladders in a single number.
        var packed = fibrous && row.ArealDensityKgM2 > 0 && m.DensityGCm3 > 0
            ? row.ArealDensityKgM2 / row.ThicknessMm / m.DensityGCm3
            : 1;

        return new BallisticLimit.Barrier
        {
            Class = m.Class,
            FailureMode = m.FailureMode,
            ThicknessMm = row.ThicknessMm,
            ShearMPa = m.Class == "Ductile" ? m.StrengthMPa : 0,
            YieldMPa = m.Class == "Ductile" ? m.YieldMPa : 0,
            CompressiveMPa = m.Class == "Brittle" ? m.StrengthMPa : 0,
            FibreTensileMPa = fibrous ? m.StrengthMPa : 0,
            FailureStrain = fibrous ? m.FailureStrain : 0,
            HardnessHv = m.HardnessHv,
            DensityGCm3 = fibrous && row.ArealDensityKgM2 > 0
                ? row.ArealDensityKgM2 / row.ThicknessMm
                : m.DensityGCm3,
            PackedFraction = packed,
        };
    }

    /// <summary>The plate an obliquity row was shot at.</summary>
    public static BallisticLimit.Barrier ObliqueBarrier(ArmorStandardTests.ObliqueLimit row)
    {
        var m = ArmorStandardTests.LadderMaterials[row.Material];
        return new BallisticLimit.Barrier
        {
            Class = m.Class,
            FailureMode = m.FailureMode,
            ThicknessMm = row.ThicknessMm,
            ShearMPa = m.Class == "Ductile" ? m.StrengthMPa : 0,
            YieldMPa = m.Class == "Ductile" ? m.YieldMPa : 0,
            CompressiveMPa = m.Class == "Brittle" ? m.StrengthMPa : 0,
            FibreTensileMPa = m.Class == BallisticLimit.Fibrous ? m.StrengthMPa : 0,
            FailureStrain = m.Class == BallisticLimit.Fibrous ? m.FailureStrain : 0,
            HardnessHv = m.HardnessHv,
            DensityGCm3 = m.DensityGCm3,
            PackedFraction = 1,
        };
    }
}
