using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The published ballistic limits, fired at the model.
///
/// These fifteen numbers were written down before the model existed, with a note saying
/// nothing checked them against a v_bl "because there is no v_bl yet — that is the point
/// of writing them down now". The v_bl was built and nobody came back. So for two stages
/// the only thing constraining a material's strength was the class ladder, where the
/// thickness is the unknown being solved for and any error in the strength hides inside
/// it.
///
/// The materials here are the ladders' own, not the game's — see LadderMaterials for why
/// that distinction is the whole test.
/// </summary>
public class BallisticLadderTests
{
    public static TheoryData<string, double> Points()
    {
        var data = new TheoryData<string, double>();
        foreach (var l in ArmorStandardTests.Limits)
        {
            data.Add(l.Material, l.ThicknessMm);
        }

        return data;
    }

    private static (double Model, double Published, ArmorStandardTests.BallisticLimit Row)
        Run(string material, double thicknessMm)
    {
        var row = ArmorStandardTests.Limits.Single(
            l => l.Material == material && Math.Abs(l.ThicknessMm - thicknessMm) < 1e-6);
        var m = ArmorStandardTests.LadderMaterials[material];

        // built by the calibrator, so what the constants are derived from and what they
        // are tested against cannot drift apart
        var barrier = LadderCalibrator.LadderBarrier(row, m);

        return (ArmorFixture.V50(barrier, row.Threat), row.V50, row);
    }

    /// <summary>
    /// Every point, within the band its own source earns. A finite-element result
    /// validated against depth-of-penetration trials is not the same evidence as a
    /// depth-of-penetration figure read as a limit, and one band for all fifteen would
    /// have to be the loosest of them.
    ///
    /// A point the model is known to miss is held to its recorded reading in
    /// ArmorStandardTests.LadderMisses instead: no worse than measured, and the guard
    /// below makes the entry leave once the point is back inside its band.
    /// </summary>
    [Theory]
    [MemberData(nameof(Points))]
    public void The_model_lands_on_the_published_limit(string material, double thicknessMm)
    {
        var (model, published, row) = Run(material, thicknessMm);
        var ratio = model / published;

        if (ArmorStandardTests.LadderMisses.TryGetValue((material, thicknessMm), out var miss))
        {
            Assert.True(Math.Abs(ratio - 1) <= Math.Abs(miss.Reads - 1),
                $"{material} at {thicknessMm} mm reads {ratio:0.000} of the published " +
                $"limit, past its recorded miss of {miss.Reads:0.000} ({miss.Cause})");
            return;
        }

        Assert.InRange(ratio, 1 - row.Band, 1 + row.Band);
    }

    /// <summary>
    /// Every recorded ladder miss must still be a miss. An allowance that is no longer
    /// needed is a hole a regression walks through unseen.
    /// </summary>
    [Fact]
    public void Every_recorded_ladder_miss_is_still_needed()
    {
        foreach (var ((material, thicknessMm), (reads, cause)) in ArmorStandardTests.LadderMisses)
        {
            var (model, published, row) = Run(material, thicknessMm);
            var ratio = model / published;

            Assert.True(Math.Abs(reads - 1) > row.Band,
                $"{material} at {thicknessMm} mm is recorded at {reads:0.000}, inside its " +
                $"own band of ±{row.Band:0.00} — that is not a miss");
            Assert.True(Math.Abs(ratio - 1) > row.Band,
                $"{material} at {thicknessMm} mm now reads {ratio:0.000}, inside its band " +
                $"— delete its LadderMisses entry (\"{cause}\")");
        }
    }

    /// <summary>
    /// The shape, separately from the scale. If the model is uniformly off across a
    /// ladder then one constant is wrong; if the error grows with thickness then the law
    /// is wrong, and no constant will save it. This is what said T² beats T^0.75 in the
    /// first place, and it is worth keeping honest.
    ///
    /// Two ladders miss it, and both are recorded in ArmorStandardTests.ShapeMisses
    /// with the spread they read, rather than left red.
    ///
    /// MildSteel misses by a residual, not by the law any more. With failure mode a
    /// material property — mild steel flows, it does not plug — every point of its
    /// ladder sits inside its own band and the RHA/mild pair comparison lands at
    /// 0.90-0.99 of the published ratios on two independent constants. What remains
    /// is the spread: the per-row solutions hold at 5.6-6.4 from 4.7 to 16 mm and
    /// rise to 8.4-9.0 at 20-25 mm, i.e. past T/d ≈ 2.6 the flow is CONFINED — deep
    /// cavity expansion costs more than thin-plate flow — and one constant cannot
    /// carry both regimes. Deeper than any wearable plate; recorded, not fitted away.
    /// What would close it: a confinement term with data behind it.
    ///
    /// AramidUD misses for the same species of reason and a different mechanism. The
    /// woven pack holds to 1.08 over 3.6-10 mm, so the fibre law's linearity in
    /// thickness is not wrong everywhere; the laminate ladder drifts to 1.18 because its
    /// error climbs with thickness — 1.09 at 2.2 mm against 1.28 at 6.8 mm — which is
    /// the same direction the certificates disagree in, and no value of FibrousK moves a
    /// spread. The last laminate point is also the densest of its own ladder by 17%, so
    /// part of the drift may be packing rather than thickness; a laminate ladder at
    /// constant packing would separate the two.
    /// </summary>
    [Theory]
    [InlineData("ArmoredSteel")]
    [InlineData("MildSteel")]
    [InlineData("AramidWoven")]
    [InlineData("AramidUD")]
    public void The_error_does_not_grow_with_thickness(string material)
    {
        var (ratios, spread) = Shape(material);

        var allowed = ArmorStandardTests.ShapeMisses.TryGetValue(material, out var miss)
            ? miss.Spread
            : ShapeLimit;

        // ratios are ordered by thickness, so the ends are the ends — the message used to
        // call Min "thin" and Max "thick", which for the mild ladder names them backwards:
        // it runs +9% at the thin end and −14% at the thick one
        Assert.True(spread < allowed,
            $"{material}: the model is off by {ratios[0]:0.00}x at the thin end and " +
            $"{ratios[^1]:0.00}x at the thick end, a spread of {spread:0.000}x against " +
            $"{allowed:0.000} allowed — that is the law's shape, not a constant");
    }

    /// <summary>
    /// Every recorded shape miss must still be a miss, so a law that closes it has to
    /// take its entry out of the table.
    /// </summary>
    [Fact]
    public void Every_recorded_shape_miss_is_still_needed()
    {
        foreach (var (material, (recorded, cause)) in ArmorStandardTests.ShapeMisses)
        {
            var (_, spread) = Shape(material);

            Assert.True(recorded >= ShapeLimit,
                $"{material} is recorded at a spread of {recorded:0.000}, inside the " +
                "shape limit — that is not a miss");
            Assert.True(spread >= ShapeLimit,
                $"{material} now spreads {spread:0.000}x, inside the shape limit — " +
                $"delete its ShapeMisses entry (\"{cause}\")");
        }
    }

    /// <summary>How far one ladder's error may change across its thicknesses.</summary>
    private const double ShapeLimit = 1.15;

    private static (double[] Ratios, double Spread) Shape(string material)
    {
        var ratios = ArmorStandardTests.Limits
            .Where(l => l.Material == material)
            .OrderBy(l => l.ThicknessMm)
            .Select(l => Run(l.Material, l.ThicknessMm))
            .Select(x => x.Model / x.Published)
            .ToArray();

        return (ratios, ratios.Max() / ratios.Min());
    }

    /// <summary>
    /// The comparison that identifies the hardness term at all: two steels, the same
    /// projectile, the same thicknesses, differing only in how hard the plate is. The
    /// fixture has carried both ladders since the beginning and nothing has ever used
    /// them together.
    /// </summary>
    [Theory]
    [InlineData(6.0)]
    [InlineData(10.0)]
    [InlineData(12.0)]
    [InlineData(16.0)]
    public void Armour_steel_beats_mild_steel_by_what_the_papers_measured(double thicknessMm)
    {
        var (rhaModel, rhaPublished, _) = Run("ArmoredSteel", thicknessMm);
        var (mildModel, mildPublished, _) = Run("MildSteel", thicknessMm);

        var published = rhaPublished / mildPublished;
        var model = rhaModel / mildModel;

        Assert.InRange(model / published, 0.85, 1.15);
    }
}
