using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The core-assumption contract, exercised. The point of CoreAssumption is that the
/// system can no longer take a silent default: an assumption cannot be created without
/// a name, a ladder row cannot be created without an assumption, and a constant cannot
/// be derived across two assumptions without saying how the difference was compensated.
/// Each of those "cannot"s is only real if a test proves it throws.
/// </summary>
public class CoreAssumptionContractTests
{
    [Fact]
    public void An_assumption_cannot_be_nameless()
    {
        Assert.Throws<ArgumentException>(() => new CoreAssumption("", 5.3, 6.25, 570));
        Assert.Throws<ArgumentException>(() => new CoreAssumption("   ", 5.3, 6.25, 570));
    }

    [Fact]
    public void An_assumption_cannot_carry_impossible_physics()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CoreAssumption("a core with no mass", 0, 6.25, 570));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CoreAssumption("a core with no diameter", 5.3, 0, 570));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CoreAssumption("a core with no hardness", 5.3, 6.25, 0));
    }

    /// <summary>
    /// The check that moved when the contract started holding absolute figures: a core
    /// cannot be wider or heavier than the bullet it is quoted inside, and since the
    /// bullet is the row's rather than the core's, the complaint has to happen when the
    /// two meet.
    /// </summary>
    [Fact]
    public void A_core_that_does_not_fit_its_bullet_is_refused_at_the_row()
    {
        var core = new CoreAssumption("a .50 AP core", 25.9, 10.88, 630);

        Assert.Throws<InvalidOperationException>(() => core.AreaFracOf(7.62));
        Assert.Throws<InvalidOperationException>(() => core.MassFracOf(10.7));

        // and the same core inside the bullet it belongs to is fine
        Assert.InRange(core.AreaFracOf(12.7), 0.7, 0.8);
        Assert.InRange(core.MassFracOf(46.0), 0.5, 0.6);
    }

    /// <summary>
    /// The criterion of the whole phase: deriving one constant over two ladders that
    /// assumed different cores fails as a test, instead of yielding a number. The Titan
    /// row is the live case — it carries the AP8 flat-nose assumption where the steel
    /// ladders carry the assumed M2 AP — so the guard is exercised by real fixture
    /// data, not by a synthetic pair.
    /// </summary>
    [Fact]
    public void Deriving_a_constant_across_conflicting_assumptions_throws()
    {
        var mixed = ArmorStandardTests.Limits
            .Where(l => l.Material is "ArmoredSteel" or "Titan")
            .ToArray();

        Assert.Contains(mixed, l => l.Core == CoreAssumption.M2Ap);
        Assert.Contains(mixed, l => l.Core == CoreAssumption.Ap8FlatNose);

        var ex = Assert.Throws<InvalidOperationException>(
            () => LadderCalibrator.DeriveK(mixed));

        // the exception has to say which assumptions collided, or the reader is left
        // exactly as blind as the silent default used to leave them
        Assert.Contains(CoreAssumption.M2Ap.SourceName, ex.Message);
        Assert.Contains(CoreAssumption.Ap8FlatNose.SourceName, ex.Message);
    }

    [Fact]
    public void Crossing_assumptions_demands_the_compensation_in_writing()
    {
        var mixed = ArmorStandardTests.Limits
            .Where(l => l.Material is "ArmoredSteel" or "Titan")
            .ToArray();

        Assert.Throws<ArgumentException>(
            () => LadderCalibrator.DeriveKAcrossAssumptions(mixed, " "));

        // with the compensation stated, the same rows do produce a number
        var k = LadderCalibrator.DeriveKAcrossAssumptions(mixed,
            "test-only: both assumptions read the core at 6.1 mm, so the area term " +
            "cancels and no adjustment is required");
        Assert.True(double.IsFinite(k) && k > 0);
    }

    [Fact]
    public void A_single_assumption_ladder_still_yields_a_constant()
    {
        var rha = ArmorStandardTests.Limits
            .Where(l => l.Material == "ArmoredSteel")
            .ToArray();

        var k = LadderCalibrator.DeriveK(rha);
        Assert.True(double.IsFinite(k) && k > 0,
            $"the RHA ladder alone must always calibrate, and gave {k}");
    }
}
