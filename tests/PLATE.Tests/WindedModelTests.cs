using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The winded ramp and its volley algebra. The anchors mirror the shipped
    /// defaults deliberately: a blocked 12ga slug on a steel plate must saturate,
    /// a spent bullet dying in the far side of a vest must do nothing, and eight
    /// pellets in one frame must come out exactly as one blow of their sum.
    /// </summary>
    public class WindedModelTests
    {
        private static WindedModel.Tuning Defaults => new WindedModel.Tuning
        {
            OnsetJ = 60f,
            FullJ = 300f,
            MaxLockSec = 10f,
        };

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(60f, 0f)]     // the onset itself is still silent
        [InlineData(180f, 0.5f)]  // midpoint of the ramp
        [InlineData(300f, 1f)]
        [InlineData(430f, 1f)]    // blocked 12ga slug behind a steel plate: saturated
        public void Ramp_is_linear_between_onset_and_full(float joules, float expected)
        {
            Assert.Equal(expected, WindedModel.Severity(joules, Defaults), 3);
        }

        [Fact]
        public void Spent_bullet_in_the_far_panel_does_not_wind()
        {
            // the .50 AE that crossed the torso and died in the back of the vest:
            // 36 J behind armour, well under the onset
            Assert.Equal(0f, WindedModel.Severity(36f, Defaults), 3);
        }

        [Fact]
        public void Blocked_9x19_on_aramid_winds_partially()
        {
            // ~220 J behind a soft vest: winded, but with breath left
            var t = WindedModel.Severity(220f, Defaults);
            Assert.InRange(t, 0.6f, 0.72f);
            Assert.InRange(WindedModel.LockSec(t, Defaults), 6f, 7.2f);
        }

        [Fact]
        public void Degenerate_window_reads_as_a_hard_threshold()
        {
            var hard = new WindedModel.Tuning { OnsetJ = 100f, FullJ = 100f, MaxLockSec = 10f };
            Assert.Equal(0f, WindedModel.Severity(100f, hard), 3);
            Assert.Equal(1f, WindedModel.Severity(100.1f, hard), 3);
        }

        /// <summary>
        /// The volley identity: draining for t1 and then upgrading to t12 must land the
        /// pool exactly where one blow of t12 would have.
        /// </summary>
        [Theory]
        [InlineData(0.0f, 0.4f)]
        [InlineData(0.3f, 0.7f)]
        [InlineData(0.5f, 1.0f)]
        public void Upgrading_a_volley_equals_one_combined_blow(float t1, float t12)
        {
            var afterFirst = WindedModel.StaminaFactor(t1);
            var afterUpgrade = afterFirst * WindedModel.UpgradeFactor(t1, t12);

            Assert.Equal(WindedModel.StaminaFactor(t12), afterUpgrade, 4);
        }

        [Fact]
        public void Upgrade_never_restores_stamina()
        {
            // a smaller "total" than what is already applied must change nothing
            Assert.Equal(1f, WindedModel.UpgradeFactor(0.6f, 0.4f), 4);
            // a pool already emptied has nothing to upgrade: the factor is a no-op,
            // and whatever it multiplies is already zero
            Assert.Equal(1f, WindedModel.UpgradeFactor(1f, 1f), 4);
        }

        [Fact]
        public void Durations_scale_with_severity()
        {
            Assert.Equal(5f, WindedModel.LockSec(0.5f, Defaults), 3);
            Assert.Equal(10f, WindedModel.LockSec(2f, Defaults), 3); // clamped
        }
    }
}
