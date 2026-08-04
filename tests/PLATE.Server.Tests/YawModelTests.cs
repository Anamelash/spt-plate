using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The shape of the channel.
///
/// One number used to cover two different things: a hollow point opening up, and a full
/// metal jacket not opening at all but eventually lying sideways. Splitting them means
/// the model has to say how wide a bullet is when it turns, and the only thing always
/// known about a bullet is how much mass sits behind its calibre. That the resulting
/// lengths land on the measured ones to within a tenth of a millimetre is the check that
/// the derivation is not a fudge.
/// </summary>
public class YawModelTests
{
    private static YawModel.Tuning Tuning() =>
        new(expansionAreaFactor: 1.35, neckCalibres: 20, broadsideFraction: 0.75,
            densityGPerCm3: 10.5, formFactor: 0.65);

    /// <summary>
    /// Mass over density over frontal area is the equivalent cylinder; a real bullet
    /// fills about two thirds of it, the rest being ogive and boat tail. Both of these
    /// are published lengths, and neither was used to fit the constants.
    /// </summary>
    [Theory]
    [InlineData(9.5, 7.85, 28.9)]  // 7.62x51 M80
    [InlineData(4.0, 5.70, 23.0)]  // 5.56x45 M855
    public void A_bullets_length_falls_out_of_its_mass_and_calibre(double massG, double diaMm,
        double measuredMm)
    {
        var length = YawModel.LengthMm(massG, diaMm, Tuning());

        Assert.InRange(length, measuredMm * 0.92, measuredMm * 1.08);
    }

    /// <summary>
    /// What the split is worth. A 7.62 that has turned presents 28 mm by 7.85 instead of
    /// a 7.85 circle, and it does not hold that pose — it turns through the whole circle,
    /// so the average is a fraction of full broadside.
    /// </summary>
    [Fact]
    public void A_tumbled_rifle_bullet_cuts_with_three_and_a_half_times_its_calibre()
    {
        var t = Tuning();
        var side = YawModel.SideAreaMm2(9.5, 7.85, 0.25, t);

        Assert.Equal(3.5, side / YawModel.CalibreAreaMm2(7.85), 1);
    }

    /// <summary>
    /// A ball has no broadside, and nothing in the code says so — the geometry does. The
    /// square around a circle is 1.27 times its area and a rotating projectile shows
    /// three quarters of its widest face, and those two very nearly cancel.
    /// </summary>
    [Fact]
    public void A_round_ball_presents_the_same_area_whichever_way_it_faces()
    {
        var t = Tuning();
        const double dia = 8.4;                        // 00 buckshot
        var mass = System.Math.PI / 6.0 * dia * dia * dia * t.DensityGPerCm3 / 1000.0;

        Assert.Equal(YawModel.NoseAreaMm2(dia, 0, t.ExpansionAreaFactor),
            YawModel.SideAreaMm2(mass, dia, 0, t), 3);
    }

    /// <summary>
    /// A fully expanded bullet is short and blunt and has nothing wider to turn into.
    /// Without the floor at the nose area, opening up would have made a projectile
    /// narrower once it tumbled.
    /// </summary>
    [Fact]
    public void An_expanded_bullet_has_nothing_wider_to_turn_into()
    {
        var t = Tuning();
        var nose = YawModel.NoseAreaMm2(9.0, 1.0, t.ExpansionAreaFactor);

        Assert.Equal(nose, YawModel.SideAreaMm2(8.0, 9.0, 1.0, t), 3);
    }

    [Fact]
    public void The_channel_is_narrow_up_to_the_turn_and_wide_after_it()
    {
        const double nose = 64.7;
        const double side = 169.3;
        const double neck = 157;

        Assert.Equal(nose * 100, YawModel.CavityVolumeMm3(nose, side, neck, 100), 1);
        Assert.Equal(nose * neck + side * 43,
            YawModel.CavityVolumeMm3(nose, side, neck, 200), 1);
    }

    /// <summary>
    /// The behaviour the split exists for. Where a projectile turns matters twice as
    /// much in a shallow wound as in a deep one, because in a deep one most of the
    /// channel is past the turn either way. One cartridge through an arm and through a
    /// chest behaves differently, and not because a die was rolled.
    /// </summary>
    [Fact]
    public void Turning_early_matters_far_more_in_a_shallow_wound_than_in_a_deep_one()
    {
        const double nose = 64.7;
        const double side = 169.3;

        var shallow = YawModel.CavityVolumeMm3(nose, side, 50, 120) /
                      YawModel.CavityVolumeMm3(nose, side, 150, 120);
        var deep = YawModel.CavityVolumeMm3(nose, side, 50, 400) /
                   YawModel.CavityVolumeMm3(nose, side, 150, 400);

        Assert.InRange(shallow, 1.8, 2.1);
        Assert.InRange(deep, 1.1, 1.3);
    }

    /// <summary>A projectile that never reaches the turn cuts with its nose the whole way.</summary>
    [Fact]
    public void A_wound_shallower_than_the_neck_never_sees_the_turn()
    {
        var t = Tuning();
        var neck = YawModel.MedianNeckMm(7.85, t.NeckCalibres);
        var nose = YawModel.NoseAreaMm2(7.85, 0.25, t.ExpansionAreaFactor);

        Assert.True(neck > 90, "an arm has to be shallower than the neck for this to mean anything");
        Assert.Equal(nose * 90, YawModel.CavityVolumeMm3(nose, 169.3, neck, 90), 3);
    }

    [Fact]
    public void Nothing_is_cut_by_a_path_of_no_length()
    {
        Assert.Equal(0, YawModel.CavityVolumeMm3(64.7, 169.3, 157, 0));
        Assert.Equal(0, YawModel.CavityVolumeMm3(64.7, 169.3, 157, -10));
    }
}
