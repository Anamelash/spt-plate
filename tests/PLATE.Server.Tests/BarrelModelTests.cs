using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The published barrel-length ladders the velocity model is calibrated against,
/// kept as a fixture so the model can never be "improved" into disagreeing with
/// measurement. Seven cartridges, 58 chronographed points, 51 to 711 mm — the range
/// of every barrel in the game.
///
/// Sources: rifleshooter.com (.308 28-16.5" and 15-6", 7.62x39, 6.5 Creedmoor),
/// firearmwiki.com / Ballistics By The Inch (5.56, 9x19, .300 BLK, .357 Magnum).
/// </summary>
public class BarrelModelTests
{
    /// <summary>Barrel length in mm and the measured muzzle velocity in m/s.</summary>
    public record Ladder(string Name, double CMm, double Tolerance, (double Mm, double Ms)[] Points);

    public static readonly Ladder[] Measured =
    [
        new(".308 Win 147gr FMJ", 129, 0.01,
        [
            (711.2, 903.7), (609.6, 886.7), (508.0, 854.7), (419.1, 817.5),
        ]),
        new("5.56 NATO XM193 55gr", 134, 0.01,
        [
            (508.0, 1007.7), (406.4, 971.4), (254.0, 843.4), (203.2, 768.1),
        ]),
        new("7.62x39 123gr", 68, 0.01,
        [
            (584.2, 748.0), (558.8, 744.3), (533.4, 740.4), (508.0, 736.1),
            (482.6, 730.6), (457.2, 725.1), (431.8, 721.8), (419.1, 719.3),
        ]),
        new("6.5 Creedmoor 120gr", 98, 0.01,
        [
            (685.8, 902.5), (609.6, 889.4), (508.0, 863.8), (406.4, 831.5),
        ]),
        // the flat shelf from 190 to 230 mm is real: this cartridge finishes burning
        // its powder inside nine inches, which no two-parameter curve reproduces
        new(".300 BLK 125gr", 58, 0.04,
        [
            (406.4, 677.0), (317.5, 645.6), (261.6, 604.7), (228.6, 602.0), (190.5, 596.5),
        ]),
        new("9x19 115gr", 24, 0.02,
        [
            (457.2, 395.3), (431.8, 402.3), (406.4, 394.7), (381.0, 397.5), (355.6, 394.7),
            (330.2, 390.4), (304.8, 390.8), (279.4, 386.5), (254.0, 381.9), (228.6, 377.3),
            (203.2, 376.1), (177.8, 372.8), (152.4, 362.1), (127.0, 355.4), (101.6, 333.5),
            (76.2, 313.6), (50.8, 289.0),
        ]),
        // the source has 16" faster than 18", which cannot happen: part of the residual
        // here is chronograph scatter, not the model
        new(".357 Magnum 158gr", 56, 0.045,
        [
            (457.2, 524.6), (431.8, 521.8), (406.4, 530.7), (381.0, 523.6), (355.6, 522.7),
            (330.2, 514.2), (304.8, 511.8), (279.4, 509.3), (254.0, 499.3), (228.6, 483.4),
            (177.8, 468.2), (152.4, 452.6), (127.0, 427.3), (101.6, 406.0), (76.2, 342.0),
            (50.8, 278.6),
        ]),
        // two points rather than a ladder, and the maker's own: FN quotes SS190 at
        // 716 m/s from the P90 and 650 m/s from the Five-seveN. They are here because
        // the case rule was four times out on this cartridge — it derived 94 and put
        // the pistol 24% below the P90 where FN puts it 9% below — and a constant that
        // wrong is worth pinning down even on two points
        new("5.7x28 SS190", 24, 0.02,
        [
            (263.0, 716.0), (122.0, 650.0),
        ]),
    ];

    public static TheoryData<string> LadderNames()
    {
        var data = new TheoryData<string>();
        foreach (var l in Measured)
        {
            data.Add(l.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LadderNames))]
    public void The_curve_reproduces_the_measured_ladder(string name)
    {
        var ladder = Measured.Single(l => l.Name == name);

        // v∞ is not a free knob per test: it is whatever best scales the shape, the
        // same way the normalizer never needs it because it works in ratios
        var num = 0.0;
        var den = 0.0;
        foreach (var (mm, ms) in ladder.Points)
        {
            var share = BarrelModel.VelocityShare(mm, ladder.CMm);
            num += ms * share;
            den += share * share;
        }

        var vInf = num / den;

        foreach (var (mm, ms) in ladder.Points)
        {
            var predicted = vInf * BarrelModel.VelocityShare(mm, ladder.CMm);
            var error = Math.Abs(predicted - ms) / ms;
            Assert.True(error <= ladder.Tolerance,
                $"{name} at {mm:N0} mm: measured {ms:N0} m/s, model {predicted:N0} m/s, " +
                $"off by {error:P1} (allowed {ladder.Tolerance:P1})");
        }
    }

    /// <summary>Rifle calibers are the ones that drive damage, and they fit far better.</summary>
    [Fact]
    public void Rifle_calibers_stay_within_one_percent()
    {
        foreach (var ladder in Measured.Where(l => l.Tolerance <= 0.01))
        {
            Assert.True(ladder.Points.Length >= 4, $"{ladder.Name} has too few points to mean anything");
        }

        Assert.Equal(4, Measured.Count(l => l.Tolerance <= 0.01));
        Assert.Equal(60, Measured.Sum(l => l.Points.Length));
    }

    [Fact]
    public void The_reference_barrel_is_the_one_that_changes_nothing()
    {
        Assert.Equal(0.0, BarrelModel.VelocityPercent(559, 559, 129), 6);
    }

    [Fact]
    public void A_shorter_barrel_loses_and_a_longer_one_gains()
    {
        Assert.True(BarrelModel.VelocityPercent(280, 559, 129) < 0);
        Assert.True(BarrelModel.VelocityPercent(711, 559, 129) > 0);
    }

    /// <summary>
    /// The case that started this: an 11-inch .308 barrel. The game shipped -12%, a
    /// live-values backport mod raised it to -31%, and measurement says about -16%.
    /// </summary>
    [Fact]
    public void The_short_308_barrel_lands_where_measurement_says()
    {
        var percent = BarrelModel.VelocityPercent(280, 559, 129);

        Assert.InRange(percent, -20, -12);
    }

    [Fact]
    public void The_geometric_fallback_lands_near_the_measured_constants()
    {
        // .308: case 3.64 cm³ over a 7.82 mm bore; measured c = 129
        var estimated = BarrelModel.EstimateC(3640, 7.82);

        Assert.InRange(estimated, 129 * 0.65, 129 * 1.35);
    }
}
