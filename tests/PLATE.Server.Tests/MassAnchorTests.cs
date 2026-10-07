using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// Card weight against mass. Another mod may rescale every card in the database (a carry
/// weight multiplier); the two anchors say by how much, and everything that reads a mass
/// off a card divides by it.
/// </summary>
public class MassAnchorTests
{
    [Fact]
    public void An_untouched_database_scales_nothing()
    {
        Assert.Equal(1, MassAnchor.Scale(MassAnchor.GearKg, MassAnchor.GearKg), 12);
        Assert.Equal(1, MassAnchor.Scale(MassAnchor.AmmoKg, MassAnchor.AmmoKg), 12);
    }

    [Fact]
    public void A_halving_multiplier_is_read_off_the_anchor_and_undone()
    {
        var scale = MassAnchor.Scale(MassAnchor.GearKg / 2, MassAnchor.GearKg);
        Assert.Equal(0.5, scale, 12);

        // a 3.6 kg shotgun carried as 1.8 is still 3.6 kg when it recoils
        Assert.Equal(3.6, MassAnchor.Real(1.8, scale), 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_anchor_that_says_nothing_leaves_the_cards_alone(double current)
    {
        // a weightless-items mod zeroes the anchor: that is no information, not a
        // scale of zero — every mass would otherwise come out infinite
        Assert.Equal(1, MassAnchor.Scale(current, MassAnchor.GearKg));
        Assert.Equal(2.5, MassAnchor.Real(2.5, 0));
    }
}
