using PLATE.Server.Config;

namespace PLATE.Server.Services;

/// <summary>
/// Wound channel model ("maximum simulation" variant of the ammo normalizer).
/// Damage = permanent cavity (crush) + temporary pulsating cavity (stretch),
/// topped by an energy budget (you cannot destroy more tissue than the energy delivered).
///
/// Channel depth is a log model of quadratic drag: F = ½ρCdAv² gives
/// exponential deceleration, depth ∝ (m/A)·ln(v/vstop). A model linear in
/// velocity gave rifle rounds 2+ m of gelatin versus the real ~0.7 m.
///
/// Temporary cavity: tissue is elastic — it survives slow stretching.
/// Effectiveness grows as a sigmoid of impact velocity centered on the classic
/// "high-velocity wound" boundary (~600 m/s, Fackler). Fragmentation converts
/// stretching into tearing — and it is DERIVED, not read from the vanilla
/// FragmentationChance field: a bullet breaks up where it turns broadside,
/// because that is where the envelope takes the full load, and it only breaks if
/// it is still faster there than the jacket can bear. What breaks is the
/// deformable share; a hard core never fragments.
/// </summary>
public static class WoundModel
{
    public record Result(double Damage, double Pc, double Tc, double DepthMm, double DepositFrac)
    {
        public bool EnergyCapped { get; init; }

        /// <summary>Derived fragmentation degree, 0..1 — for the report.</summary>
        public double Frag { get; init; }
    }

    /// <param name="massG">Projectile mass, g.</param>
    /// <param name="diaMm">Diameter, mm.</param>
    /// <param name="v">Impact velocity (muzzle velocity on the server), m/s.</param>
    /// <param name="x">Expansiveness index 0..1.</param>
    /// <param name="coreMassFrac">Mass share of the hard core, which never breaks up.</param>
    public static Result Compute(double massG, double diaMm, double v, double x,
        double coreMassFrac, PlateServerConfig.AmmoNormalizerSection a)
    {
        var area = Math.PI * diaMm * diaMm / 4.0;          // mm²
        var e0 = 0.5 * (massG / 1000.0) * v * v;           // J
        var sd = massG / Math.Max(area, 1e-3);             // sectional density, g/mm²

        // Channel depth in gelatin, mm
        var vRatio = Math.Max(v / Math.Max(a.GelStopVelocity, 1), 1.01);
        var depth = Math.Max(
            a.GelDepthK * sd * Math.Log(vRatio) * (1 - a.ExpansionDepthFactor * x), 1);

        var inBody = Math.Min(depth, a.BodyDepthMm);       // portion of the channel inside the body

        // Fraction of energy left in the body. The same quadratic drag that gives the
        // log depth gives v(s) = v·exp(-s/lambda), so a projectile that exits leaves
        // 1-(v_out/v)² behind; one that stops inside leaves all of it. The share of
        // the PATH is not the share of the ENERGY — a rifle bullet loses most of its
        // energy in the first hand's width of tissue.
        var lambda = Math.Max(a.GelDepthK * sd * (1 - a.ExpansionDepthFactor * x), 1e-3);
        var phi = 1 - Math.Exp(-2 * inBody / lambda);

        // Permanent cavity: narrow while the projectile is still nose-first, wide once it
        // has turned. The card quotes the median neck — no dice on a display number.
        var yaw = YawTuning(a);
        var pc = YawModel.CavityVolumeMm3(
            YawModel.NoseAreaMm2(diaMm, x, a.ExpansionAreaFactor),
            YawModel.SideAreaMm2(massG, diaMm, x, yaw),
            YawModel.MedianNeckMm(diaMm, a.YawNeckCalibres),
            inBody) / a.WoundVolumePerHp;

        // Fragmentation: the envelope fails where the bullet turns, if it is still
        // fast enough there. The same drag law that gives the deposition gives the
        // velocity at the tumble point; a bullet that exits before turning never
        // fragments, and a hard core never does regardless.
        var neck = YawModel.MedianNeckMm(diaMm, a.YawNeckCalibres);
        var vNeck = v * Math.Exp(-neck / lambda);
        var frag = neck <= inBody && vNeck > a.FragVelocityThreshold
            ? x * (1 - Math.Clamp(coreMassFrac, 0, 1))
            : 0.0;

        // Temporary cavity: velocity sigmoid × deposited energy
        var eff = 1.0 / (1.0 + Math.Exp(-(v - a.TcVelocityCenter) / a.TcVelocityWidth));
        var tc = eff * e0 * phi * (1 + a.TcFragBonus * frag) / a.TcEnergyPerHp;

        var budget = e0 / a.EnergyCapPerHp;
        var damage = Math.Min(pc + tc, budget);
        return new Result(damage, pc, tc, depth, phi)
        {
            EnergyCapped = pc + tc > budget,
            Frag = frag,
        };
    }

    /// <summary>The broadside constants, as the config holds them.</summary>
    public static YawModel.Tuning YawTuning(PlateServerConfig.AmmoNormalizerSection a)
    {
        return new YawModel.Tuning(a.ExpansionAreaFactor, a.YawNeckCalibres,
            a.YawBroadsideFraction, a.BulletDensityGPerCm3, a.BulletFormFactor);
    }
}
