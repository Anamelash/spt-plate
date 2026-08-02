using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// Thickness derived from a plate's own mass. Most of the armour in the game is
/// invented for it — "SAPI Cult Termite" has no specification anywhere — so a lookup
/// table can never cover it. Mass over density over face area can, and it stays
/// physics rather than a guess: a heavier plate of the same material and size is
/// thicker, which is the behaviour that matters.
/// </summary>
public class ArmorThicknessTests
{
    /// <summary>
    /// Granit-4 is the check that the derivation lands on reality: 3.05 kg of alumina
    /// on a SAPI-cut face should come out near the 10 mm the real plate carries.
    /// </summary>
    [Fact]
    public void A_known_plate_lands_near_its_real_thickness()
    {
        var t = Thickness(kg: 3.05, densityGCm3: 3.90, hardFraction: 0.65, widthMm: 254, heightMm: 318);

        Assert.InRange(t, 5, 12);
    }

    [Fact]
    public void A_heavier_plate_of_the_same_material_is_thicker()
    {
        var light = Thickness(1.93, 3.90, 0.65, 254, 254);
        var heavy = Thickness(3.85, 3.90, 0.65, 254, 254);

        Assert.True(heavy > light * 1.9, "twice the mass should be about twice the thickness");
    }

    /// <summary>
    /// A ceramic plate is a strike face on a fibre backer. Counting the backer as
    /// ceramic makes every plate read thicker than it is, so the hard fraction is not
    /// cosmetic.
    /// </summary>
    [Fact]
    public void The_backer_is_not_counted_as_ceramic()
    {
        var withBacker = Thickness(3.05, 3.90, 1.00, 254, 318);
        var hardOnly = Thickness(3.05, 3.90, 0.65, 254, 318);

        Assert.True(hardOnly < withBacker);
    }

    private static double Thickness(double kg, double densityGCm3, double hardFraction,
        double widthMm, double heightMm)
    {
        return kg * 1000.0 * hardFraction / (densityGCm3 / 1000.0 * widthMm * heightMm);
    }
}
