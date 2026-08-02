using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The fallback for a cartridge nobody has written down.
///
/// The reference book carries the real construction, and it will never carry every mod's
/// ammunition. The danger in a fallback is not that it guesses wrong on one round — it
/// is that it quietly changes every round. A blanket "assume a core" would hand a
/// penetrator to every FMJ in the game and shift the whole armour calibration sideways.
/// </summary>
public class BulletCoreTests
{
    private const double Depth = 0.5; // the shipped CoreFallbackDepth

    [Fact]
    public void An_ordinary_round_is_one_piece_of_metal()
    {
        Assert.Equal(1.0, AmmoNormalizer.InferredCore(0.5, Depth), 4);
        Assert.Equal(1.0, AmmoNormalizer.InferredCore(0.1, Depth), 4);
        Assert.Equal(1.0, AmmoNormalizer.InferredCore(0.0, Depth), 4);
    }

    /// <summary>
    /// The top of a calibre's cohort is where the AP round lives, and the core it gets
    /// should land near the ones that are actually published: the M993's 0.49 and the
    /// 7N37's 0.53.
    /// </summary>
    [Fact]
    public void The_hardest_hitter_in_a_cohort_lands_where_the_published_cores_are()
    {
        var top = AmmoNormalizer.InferredCore(1.0, Depth);

        Assert.True(top is > 0.45 and < 0.55,
            $"the inferred core for a cohort-topping round is {top:0.00}, " +
            "which is not where the published carbide cores sit");
    }

    [Fact]
    public void The_core_shrinks_smoothly_with_the_penetration_excess()
    {
        var mid = AmmoNormalizer.InferredCore(0.75, Depth);

        Assert.True(mid < AmmoNormalizer.InferredCore(0.6, Depth));
        Assert.True(mid > AmmoNormalizer.InferredCore(0.9, Depth));
        Assert.Equal(0.75, mid, 4);
    }

    /// <summary>
    /// Turning the knob to zero has to mean "leave the ammunition alone", not "leave it
    /// alone unless it happens to be a good round".
    /// </summary>
    [Fact]
    public void A_depth_of_zero_turns_the_inference_off_entirely()
    {
        Assert.Equal(1.0, AmmoNormalizer.InferredCore(1.0, 0), 4);
    }

    [Fact]
    public void No_bullet_can_be_inferred_out_of_existence()
    {
        Assert.True(AmmoNormalizer.InferredCore(1.0, 10) >= 0.05);
    }
}
