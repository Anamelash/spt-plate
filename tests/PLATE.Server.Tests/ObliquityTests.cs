using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The angle, measured at last.
///
/// Every constant in the model has had a published dataset behind it for two stages
/// except one: the obliquity term. The model lengthens the path by 1/cos and derives
/// everything else from that, which is not a small assumption — it says a plate that
/// fails by plugging turns back exactly sec θ more velocity at angle, that the gain is
/// the same for steel, aluminium and titanium, and that a fibre pack gains only the
/// square root of it. A raid log had one vest reading V50 from 767 to 1528 m/s across
/// neighbouring hits, all of it angle. Nothing tested any of it.
///
/// Two datasets do now, both out of the REL ballistic-limit database (Ryan et al.,
/// Defence Technology 2023; Mendeley doi 10.17632/4f92y6jzzh.2, CC BY 4.0): one plate
/// shot at four angles, and twelve other plates shot at two.
/// </summary>
public class ObliquityTests
{
    private static double Model(ArmorStandardTests.ObliqueLimit row)
    {
        return BallisticLimit.V50(LadderCalibrator.ObliqueBarrier(row),
            ArmorFixture.CoreOf(row.Threat), row.Cos, BallisticLimit.Tuning.Default);
    }

    public static TheoryData<string, int> Series()
    {
        var data = new TheoryData<string, int>();
        foreach (var r in ArmorStandardTests.Obliquities.Where(r => r.AngleDeg > 0))
        {
            data.Add(r.Projectile, r.AngleDeg);
        }

        return data;
    }

    /// <summary>
    /// What obliquity is worth, as a ratio against the same plate shot square on.
    ///
    /// The ratio is the honest comparison and the absolute value is not: the plate is
    /// 6082-T651, an aerospace alloy the model has never been calibrated against and
    /// whose game namesake is a different alloy entirely, so a miss on the absolute
    /// limit would be about DuctileK meeting aluminium and would say nothing about the
    /// angle. Divide the trial by its own zero-degree row and all of that cancels.
    ///
    /// The band is 5%, which is tight on purpose. Reproducing sec θ to a few percent is
    /// the only reason to keep a law this simple; at 10% the test would pass for laws
    /// that disagree with each other by more than the angle is worth.
    /// </summary>
    [Theory]
    [MemberData(nameof(Series))]
    public void The_angle_buys_what_the_trial_says_it_buys(string projectile, int angleDeg)
    {
        var rows = ArmorStandardTests.Obliquities.Where(r => r.Projectile == projectile);
        var at0 = rows.Single(r => r.AngleDeg == 0);
        var row = rows.Single(r => r.AngleDeg == angleDeg);

        var published = row.V50 / at0.V50;
        var model = Model(row) / Model(at0);

        Assert.InRange(model / published, 0.95, 1.05);
    }

    /// <summary>
    /// The claim the four-angle series cannot test on its own: in the plugging regime
    /// the obliquity gain has no material in it. Work goes as the square of the path
    /// and the path goes as sec θ, so the ratio is sec θ for a 20 mm aluminium plate and
    /// for a 9.8 mm ultra-high-hardness steel one alike.
    ///
    /// Twelve pairs from four studies say that is nearly right and slightly
    /// generous: they average about 1.11 against the model's 1.155, and the spread runs
    /// 1.06 to 1.16. The model sits at the top of the measured range rather than in the
    /// middle of it, so an angled plate in the game is a little stronger than the
    /// average trial found — recorded rather than tuned away, because one number that
    /// falls out of the geometry is worth more than a fitted exponent that fits the mean
    /// of twelve alloys nobody wears.
    /// </summary>
    [Fact]
    public void The_thirty_degree_gain_is_the_same_for_every_plate_and_the_trials_agree()
    {
        var ratios = ArmorStandardTests.ObliquityPairs
            .Select(p => p.V50At30 / p.V50At0)
            .ToArray();

        Assert.Equal(12, ratios.Length);
        Assert.InRange(ratios.Min(), 1.05, 1.10);
        Assert.InRange(ratios.Max(), 1.15, 1.20);

        var model = 1.0 / Math.Cos(30 * Math.PI / 180.0);
        Assert.InRange(model, ratios.Min(), ratios.Max());

        var mean = Math.Exp(ratios.Sum(Math.Log) / ratios.Length);
        Assert.InRange(model / mean, 1.0, 1.05);
    }

    /// <summary>
    /// Where the measurement stops and the model keeps going.
    ///
    /// The published series ends at 45°. The model does not: it holds the cosine down to
    /// MinCos before handing over to ricochet, which is past 70°, and everything between
    /// is extrapolation on the strength of a law verified over half that range. That is
    /// not a bug — a game has to answer for a 60° hit — but it is the kind of thing that
    /// stops being remembered unless a test says it every run.
    /// </summary>
    [Fact]
    public void Beyond_forty_five_degrees_the_model_is_extrapolating()
    {
        var measured = ArmorStandardTests.Obliquities.Max(r => r.AngleDeg);
        Assert.Equal(45, measured);

        var floorAngle = Math.Acos(BallisticLimit.Tuning.Default.MinCos) * 180 / Math.PI;
        Assert.InRange(floorAngle, 65, 75);
        Assert.True(floorAngle > measured,
            "the cosine floor has come down inside the measured range — either the " +
            "trials now reach further or the floor has moved, and both change what " +
            "this fixture is entitled to claim");
    }

    /// <summary>
    /// A bullet and its own bare core, shot into the same plate at the same four angles —
    /// and the one test in this file that is red.
    ///
    /// Forrestal fired complete APM2 bullets and, separately, the stripped 5.3 g cores.
    /// The plate turned them back at almost the same VELOCITY: 501 against 514 at normal
    /// incidence, 718 against 723 at 45°, the core a couple of percent the harder of the
    /// two everywhere. Half the bullet's mass makes no difference to the limit, which is
    /// a statement about what the jacket does — nothing. It strips at the face and its
    /// energy goes elsewhere.
    ///
    /// The model says otherwise by 38%. Driving reads a bullet as the core's diameter
    /// carrying the WHOLE bullet's mass, so the bare core comes out sqrt(10.7/5.3) =
    /// 1.42x easier to stop than the bullet that contained it. The choice is deliberate
    /// and its reasoning is written in Driving: reading a 7N10 as its 1.7 g core alone
    /// made the round HARDER to stop than the same round with no core described, and
    /// "writing down what a bullet is made of must never cost it penetration". That
    /// reasoning is about the model's own consistency; this is a measurement, and it
    /// says the mass behind a core is not what carries it through.
    ///
    /// What closes it is not a constant: it is deciding what mass the limit is computed
    /// against — core, bullet, or something between — and re-deriving every mode
    /// constant afterwards, since all of them were fitted with whole-bullet masses. That
    /// is a stage of its own, and the fixture's job here is to make sure it cannot be
    /// forgotten.
    /// </summary>
    [Fact]
    public void The_bullet_and_its_own_core_are_read_as_the_same_penetrator()
    {
        foreach (var angle in new[] { 0, 15, 30, 45 })
        {
            var bullet = ArmorStandardTests.Obliquities
                .Single(r => r.AngleDeg == angle && r.Projectile.EndsWith("bullet"));
            var core = ArmorStandardTests.Obliquities
                .Single(r => r.AngleDeg == angle && r.Projectile.EndsWith("core"));

            var published = core.V50 / bullet.V50;
            var model = Model(core) / Model(bullet);

            Assert.InRange(model / published, 0.85, 1.15);
        }
    }
}
