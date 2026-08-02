namespace PLATE.Server.Services;

/// <summary>
/// Muzzle velocity as a function of barrel length.
///
/// Le Duc's interior-ballistics approximation, v(L) = v∞·L/(L + c): the numerator is
/// the work the expanding gas has done by the time the projectile has travelled L,
/// the denominator saturates it as pressure falls off behind the bullet. A century
/// old and still the standard closed form for this curve.
///
/// Validated against published barrel-length ladders — 58 measurements, seven
/// cartridges, 51 to 711 mm (see BarrelModelTests): under 1% for rifle rounds with
/// clean data, 4% worst case including the chronograph scatter in the sources. The
/// obvious alternative, a saturating exponential, fits rifles just as well but
/// misses badly on pistol calibers (7% on the full 9x19 ladder) because it cannot
/// produce the long flat shelf a pistol round reaches by seven inches.
/// </summary>
public static class BarrelModel
{
    /// <summary>Fraction of the cartridge's terminal velocity reached in L mm of bore.</summary>
    public static double VelocityShare(double lengthMm, double cMm)
    {
        return lengthMm <= 0 ? 0 : lengthMm / (lengthMm + Math.Max(cMm, 1e-6));
    }

    /// <summary>
    /// The game's Velocity modifier, in percent, for a barrel of this length. It is
    /// relative to the reference barrel the cartridge's InitialSpeed is quoted for, so
    /// the terminal velocity cancels and only the two per-caliber numbers are needed.
    /// </summary>
    public static double VelocityPercent(double lengthMm, double refLengthMm, double cMm)
    {
        var reference = VelocityShare(refLengthMm, cMm);
        if (reference <= 0)
        {
            return 0;
        }

        return 100.0 * (VelocityShare(lengthMm, cMm) / reference - 1.0);
    }

    /// <summary>
    /// Fallback c for a caliber nobody has published a barrel-length ladder for.
    /// Across the seven measured cartridges c/(V₀/A) landed between 1.0 and 2.2 with a
    /// mean of 1.67, so this is good to about ±35% — which sounds worse than it is:
    /// near the reference length the curve is flat, and a 35% error in c moves the
    /// modifier of a heavily shortened barrel by about three percentage points.
    /// </summary>
    public static double EstimateC(double caseVolumeMm3, double boreDiaMm)
    {
        var area = Math.PI * boreDiaMm * boreDiaMm / 4.0;
        return area <= 0 ? 0 : 1.67 * caseVolumeMm3 / area;
    }
}
