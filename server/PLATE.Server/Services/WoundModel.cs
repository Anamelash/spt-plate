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
/// stretching into tearing — a bonus multiplier from FragmentationChance.
/// </summary>
public static class WoundModel
{
    public record Result(double Damage, double Pc, double Tc, double DepthMm, double DepositFrac)
    {
        public bool EnergyCapped { get; init; }
    }

    /// <param name="massG">Projectile mass, g.</param>
    /// <param name="diaMm">Diameter, mm.</param>
    /// <param name="v">Impact velocity (muzzle velocity on the server), m/s.</param>
    /// <param name="x">Expansiveness index 0..1.</param>
    /// <param name="fragChance">Fragmentation chance (vanilla field) for the TC bonus.</param>
    public static Result Compute(double massG, double diaMm, double v, double x,
        double fragChance, PlateServerConfig.AmmoNormalizerSection a)
    {
        var area = Math.PI * diaMm * diaMm / 4.0;          // mm²
        var e0 = 0.5 * (massG / 1000.0) * v * v;           // J
        var sd = massG / Math.Max(area, 1e-3);             // sectional density, g/mm²

        // Channel depth in gelatin, mm
        var vRatio = Math.Max(v / Math.Max(a.GelStopVelocity, 1), 1.01);
        var depth = Math.Max(
            a.GelDepthK * sd * Math.Log(vRatio) * (1 - a.ExpansionDepthFactor * x), 1);

        var inBody = Math.Min(depth, a.BodyDepthMm);       // portion of the channel inside the body
        var phi = inBody / depth;                          // ≈ fraction of energy left in the body

        // Permanent cavity: channel volume including expansion/tumbling
        var areaEff = area * (1 + a.ExpansionAreaFactor * x);
        var pc = areaEff * inBody / a.WoundVolumePerHp;

        // Temporary cavity: velocity sigmoid × deposited energy
        var eff = 1.0 / (1.0 + Math.Exp(-(v - a.TcVelocityCenter) / a.TcVelocityWidth));
        var tc = eff * e0 * phi * (1 + a.TcFragBonus * fragChance) / a.TcEnergyPerHp;

        var budget = e0 / a.EnergyCapPerHp;
        var damage = Math.Min(pc + tc, budget);
        return new Result(damage, pc, tc, depth, phi) { EnergyCapped = pc + tc > budget };
    }
}
