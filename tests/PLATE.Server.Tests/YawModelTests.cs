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
///
/// It is a fudge for one family, though: the inference assumes lead, and a steel-cored
/// bullet is longer than its mass suggests. So the reference book may publish a length
/// per cartridge, and a published length outranks the inference — the tests below say
/// what that means where a book has an entry and where it does not.
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
    /// A measurement outranks a model. The inference exists to answer the question where
    /// nobody has answered it already; where the reference book publishes a length, that
    /// is the length, and nothing about mass or calibre gets a vote.
    /// </summary>
    [Fact]
    public void A_published_length_outranks_what_the_mass_would_have_said()
    {
        var t = Tuning();

        Assert.Equal(24.8, YawModel.LengthMm(9.5, 7.85, t, 24.8), 6);
        Assert.Equal(24.8 * 7.85 * t.BroadsideFraction,
            YawModel.SideAreaMm2(9.5, 7.85, 0.25, t, 24.8), 6);
    }

    /// <summary>
    /// Coverage is deliberately partial: most cartridges have no published length, and a
    /// made-up one would be worse than an openly approximate one. Nothing published — a
    /// book that is silent, a server that is old, no server at all — has to land back on
    /// the inference, never on zero, which would be a bullet with no broadside.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void An_absent_length_falls_back_to_the_inference(double published)
    {
        var t = Tuning();

        Assert.Equal(YawModel.LengthMm(9.5, 7.85, t), YawModel.LengthMm(9.5, 7.85, t, published), 6);
        Assert.Equal(YawModel.SideAreaMm2(9.5, 7.85, 0.25, t),
            YawModel.SideAreaMm2(9.5, 7.85, 0.25, t, published), 6);
    }

    /// <summary>
    /// The failure the override exists for. One density for every bullet is a lead
    /// density, and a mild-steel core is lighter than lead for the same volume, so the
    /// inference reads 5.45x39 7N6 at 20.4 mm against a measured 24.8 — a fifth narrower
    /// once it turns, and a fifth less lever arm against a wall.
    /// </summary>
    [Fact]
    public void The_545_reads_its_measured_length_instead_of_the_lead_assumption()
    {
        var t = Tuning();

        Assert.InRange(YawModel.LengthMm(3.4, 5.60, t), 20.0, 20.8);
        Assert.Equal(24.8, YawModel.LengthMm(3.4, 5.60, t, 24.8), 6);
    }

    /// <summary>
    /// The same failure at its worst, and the one that is not only a channel width. A
    /// steel core under an aluminium jacket is the lightest construction there is: the
    /// inference gives 9x19 7N31 a length barely over its own calibre, which the obstacle
    /// module reads as a sphere — slenderness L/d − 1 near zero, so no barrier can ever
    /// tip it over. The raid says otherwise. Its published 13 mm gives it a lever arm.
    /// </summary>
    [Fact]
    public void A_light_steel_cored_pistol_bullet_stops_reading_as_a_sphere()
    {
        var t = Tuning();
        const double massG = 4.1;
        const double diaMm = 9.0;

        var inferred = YawModel.LengthMm(massG, diaMm, t) / diaMm - 1.0;
        var published = YawModel.LengthMm(massG, diaMm, t, 13.0) / diaMm - 1.0;

        Assert.InRange(inferred, 0.0, 0.08);          // "this is a ball"
        Assert.InRange(published, 0.40, 0.50);        // a lever arm a wall can work on
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

    /// <summary>
    /// The length has to reach the number the server bakes onto the card, not only the
    /// geometry underneath it. The client computes the damage of a real hit through the
    /// same YawModel with the same figure off the same /plate/ammo-data entry, so if the
    /// bake ignored the book the card and the raid would disagree — which is the one
    /// invariant this project will not trade.
    /// </summary>
    [Fact]
    public void The_baked_damage_moves_with_the_published_length()
    {
        var a = new PLATE.Server.Config.PlateServerConfig.AmmoNormalizerSection();

        // 5.45x39 PS: measured 24.8 mm, inferred 20.4
        var inferred = WoundModel.Compute(3.4, 5.60, 880, 0.25, 0.42, a);
        var measured = WoundModel.Compute(3.4, 5.60, 880, 0.25, 0.42, a, 24.8);

        Assert.True(measured.Pc > inferred.Pc,
            $"measured PC {measured.Pc:0.##} did not beat inferred {inferred.Pc:0.##}");
        Assert.Equal(inferred.Pc, WoundModel.Compute(3.4, 5.60, 880, 0.25, 0.42, a, 0).Pc, 6);
    }
}
