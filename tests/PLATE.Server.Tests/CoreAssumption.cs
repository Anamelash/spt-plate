namespace PLATE.Server.Tests;

/// <summary>
/// What a ladder's projectile actually is, below the jacket — a strict contract rather
/// than four loose fields.
///
/// The papers give a mass, a calibre and the words "7.62 AP", and that is not one
/// projectile: M2 AP is 10.7 g, the 7.62x51 AP8 is 9.7 g, B-32 is 10.4 g. Every ladder
/// therefore rests on a statement about what was fired, and that statement used to live
/// in a private helper any row could quietly take or quietly not. This type removes the
/// quiet option: a core cannot exist without a named source, a row cannot exist without
/// a core (no default — there is deliberately no parameterless construction and no
/// static called "Default"), and the calibrator refuses to derive one constant across
/// rows whose cores differ unless the caller states the compensation out loud.
///
/// **The core is held in absolute figures, not fractions, and that is the whole
/// improvement.** A hardened core is a mass and a diameter — 5.3 g of steel 6.2484 mm
/// across — and it stays that whether the paper quotes the bullet at 10.0 g or 10.7 g,
/// or fires the core on its own with no bullet around it at all. Fractions are what a
/// row computes from it, not what it is. The version of this type that stored fractions
/// needed three named statics for one physical core and would have needed a fourth for
/// the next paper.
/// </summary>
public sealed record CoreAssumption
{
    /// <summary>Where the construction came from — never blank, by construction.</summary>
    public string SourceName { get; }

    /// <summary>Mass of the hard core, g.</summary>
    public double MassG { get; }

    /// <summary>Diameter of the hard core, mm.</summary>
    public double DiaMm { get; }

    /// <summary>Vickers hardness of the core.</summary>
    public double HardnessHv { get; }

    public CoreAssumption(string sourceName, double massG, double diaMm, double hardnessHv)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException(
                "a core without a named source is the silent default this type " +
                "exists to forbid", nameof(sourceName));
        }

        if (massG <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(massG), massG,
                "a core has a mass");
        }

        if (diaMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diaMm), diaMm,
                "a core has a diameter");
        }

        if (hardnessHv <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hardnessHv), hardnessHv,
                "a core with no hardness is not a core");
        }

        SourceName = sourceName;
        MassG = massG;
        DiaMm = diaMm;
        HardnessHv = hardnessHv;
    }

    /// <summary>
    /// Frontal area of the core over the bullet's own. 1 when the core IS the
    /// projectile — a bare core, or a fragment that is one piece of steel.
    /// </summary>
    public double AreaFracOf(double bulletDiaMm)
    {
        if (bulletDiaMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bulletDiaMm), bulletDiaMm,
                "a bullet has a diameter");
        }

        var frac = DiaMm * DiaMm / (bulletDiaMm * bulletDiaMm);
        if (frac > 1.0001)
        {
            throw new InvalidOperationException(
                $"{SourceName}: a core of {DiaMm} mm does not fit in a bullet of " +
                $"{bulletDiaMm} mm — one of the two figures belongs to another round");
        }

        return Math.Min(frac, 1.0);
    }

    /// <summary>Mass of the core over the bullet's own; 1 for a bare core.</summary>
    public double MassFracOf(double bulletMassG)
    {
        if (bulletMassG <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bulletMassG), bulletMassG,
                "a bullet has a mass");
        }

        var frac = MassG / bulletMassG;
        if (frac > 1.0001)
        {
            throw new InvalidOperationException(
                $"{SourceName}: a core of {MassG} g does not fit in a bullet of " +
                $"{bulletMassG} g");
        }

        return Math.Min(frac, 1.0);
    }

    /// <summary>
    /// The .30 calibre M2 AP core, measured rather than assumed: 5.3 g of hardened
    /// steel, 6.2484 mm across, 570 HV. Every CAL30APM2 row of the REL ballistic-limit
    /// database carries these figures, and they come from the trials themselves.
    ///
    /// This used to say 730 HV "because no paper gives it", which was not a small error:
    /// 730 HV is what the same database measures for a TUNGSTEN CARBIDE core (14.5 mm
    /// BS-41), and hardened steel AP cores measure 570 at .30 calibre, 595 for the
    /// 14.5 mm B-32 and 630 for the .50 M2 AP. The ladders were being read against a
    /// core half again harder than the one that was fired, and the hardness term is
    /// where that landed — which is why replacing it forced DuctileK and the hardness
    /// exponent to be derived again.
    ///
    /// The same core serves the rows that fired it bare: against a "bullet" of 5.3 g at
    /// 6.2484 mm both fractions come out 1, which is what a stripped core is.
    /// </summary>
    public static readonly CoreAssumption M2Ap = new(
        "M2 AP core as measured: 5.3 g at 6.2484 mm, 570 HV (REL V50 database)",
        5.3, 6.2484, 570);

    /// <summary>
    /// The .22 fragment simulating projectile of STANAG 2920, which is not an assumption
    /// at all — the standard specifies the whole projectile and the paper repeats it:
    /// "a steel fragment with a hardness of 27 ± 3 HRC, a mass of 1.10 ± 0.03 g, a
    /// diameter of 5.46 ± 0.05 mm". One piece of steel, so the core IS the projectile.
    /// It carries a SourceName like every other core because the calibrator's guard is
    /// about what a ladder rests on, and "nothing was assumed" is itself a statement
    /// worth being unable to omit.
    /// </summary>
    public static readonly CoreAssumption Fsp22 = new(
        "STANAG 2920 .22 FSP: 1.10 g of steel at 5.46 mm, 27 HRC — specified, not assumed",
        1.10, 5.46, 270);

    /// <summary>
    /// The titanium trial's own projectile, and the source says so: 9.7 g at 7.85 mm
    /// with a FLAT-NOSED hardened core, which is the 7.62x51 AP8 rather than the M2 AP.
    /// A flat nose presents its whole core face instead of working up to it, so the
    /// diameter is the core's own with nothing taken off for the ogive.
    ///
    /// Hardness read at the .30 M2 AP's measured 570 rather than the 730 it used to
    /// assume: the same database measures 570-630 HV across three calibres of hardened
    /// steel AP core and reserves 730 for tungsten carbide, and the AP8 is a steel core
    /// of the same calibre class as the .30.
    /// </summary>
    public static readonly CoreAssumption Ap8FlatNose = new(
        "AP8: 5.0 g hardened steel core of 6.1 mm in a 9.7 g bullet, flat-nosed; " +
        "hardness read at the measured .30 AP core's 570 HV",
        5.0, 6.1, 570);
}
