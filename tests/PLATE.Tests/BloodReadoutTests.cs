using System.Globalization;
using System.Threading;
using PLATE.Client.Blood;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// What the blood panel says, checked without a game running.
    ///
    /// The first version of the panel had this arithmetic welded into a MonoBehaviour,
    /// so the only way to find out whether a countdown was right was to bleed in a raid
    /// and read it off the screen. Pulled out into <see cref="BloodReadout"/> it is
    /// ordinary arithmetic over floats and strings, and wrong answers are caught here.
    /// </summary>
    public class BloodReadoutTests
    {
        /// <summary>
        /// The shipped anchors: warning at tier 2 (70%), death at 50%, counting down to
        /// tier 3 at 60%.
        /// </summary>
        private static BloodReadout.Thresholds Tier2() => new BloodReadout.Thresholds
        {
            Warning = 0.70f,
            Death = 0.50f,
            Next = 0.60f,
            NextLabel = "T3",
        };

        private static BloodReadout.Format Ml(BloodRange range = BloodRange.FullVolume) =>
            new BloodReadout.Format { Units = BloodUnits.Milliliters, Range = range };

        private static BloodReadout.Format Pct(BloodRange range = BloodRange.FullVolume) =>
            new BloodReadout.Format { Units = BloodUnits.Percent, Range = range };

        [Fact]
        public void Millilitres_read_as_current_over_capacity_beside_the_tier()
        {
            var lines = BloodReadout.Build(4500f, 5000f, 2, 0f, Tier2(), Ml());

            Assert.Equal("4500", lines.Volume);
            Assert.Equal("5000 ml", lines.Capacity);
            Assert.Equal("T2", lines.Tag);
        }

        /// <summary>A share is already out of something, so there is no capacity to print.</summary>
        [Fact]
        public void A_percentage_carries_no_capacity()
        {
            var lines = BloodReadout.Build(4500f, 5000f, 2, 0f, Tier2(), Pct());

            Assert.Equal("90%", lines.Volume);
            Assert.Equal(string.Empty, lines.Capacity);
        }

        /// <summary>
        /// The usable range starts at the death point. With death at 50% of 5000, a full
        /// body has 2500 ml to lose and 4000 ml left means 1500 of them are still there.
        /// </summary>
        [Fact]
        public void The_usable_range_counts_only_the_blood_that_can_be_lost()
        {
            var lines = BloodReadout.Build(4000f, 5000f, 2, 0f, Tier2(), Ml(BloodRange.UsableVolume));

            Assert.Equal("1500", lines.Volume);
            Assert.Equal("2500 ml", lines.Capacity);
        }

        /// <summary>Full on the usable scale is 2500 of 2500 — and 100%, not 50%.</summary>
        [Fact]
        public void A_full_body_reads_full_on_the_usable_scale()
        {
            var lines = BloodReadout.Build(5000f, 5000f, 0, 0f, Tier2(), Pct(BloodRange.UsableVolume));

            Assert.Equal("100%", lines.Volume);
        }

        /// <summary>
        /// The death point is zero on the usable scale, which is the whole reason for
        /// having it: the same volume reads 50% on the full scale and reads as gone here.
        /// </summary>
        [Fact]
        public void The_death_point_is_zero_on_the_usable_scale()
        {
            var full = BloodReadout.Build(2500f, 5000f, 3, 0f, Tier2(), Pct());
            var usable = BloodReadout.Build(2500f, 5000f, 3, 0f, Tier2(), Pct(BloodRange.UsableVolume));

            Assert.Equal("50%", full.Volume);
            Assert.Equal("0%", usable.Volume);
        }

        /// <summary>Below the death point the usable scale floors rather than going negative.</summary>
        [Fact]
        public void The_usable_scale_does_not_go_negative()
        {
            var lines = BloodReadout.Build(2000f, 5000f, 3, 0f, Tier2(), Ml(BloodRange.UsableVolume));

            Assert.Equal("0", lines.Volume);
        }

        /// <summary>
        /// A drain under the print resolution is not a bleed: showing "0 ml/s" next to a
        /// countdown would be a panel arguing with itself.
        /// </summary>
        [Theory]
        [InlineData(0f, false)]
        [InlineData(0.04f, false)]
        [InlineData(0.05f, true)]
        [InlineData(3.2f, true)]
        public void Only_a_printable_rate_counts_as_bleeding(float rate, bool bleeding)
        {
            var lines = BloodReadout.Build(4000f, 5000f, 1, rate, Tier2(), Ml());

            Assert.Equal(bleeding, lines.Bleeding);
            Assert.Equal(bleeding, lines.Rate.Length > 0);
        }

        /// <summary>
        /// 4000 ml with tier 3 at 60% of 5000 leaves 1000 ml of headroom; at 25 ml/s
        /// that is 40 seconds. The countdown is in real volume whatever the display
        /// scale — it is a time, not a reading.
        /// </summary>
        [Theory]
        [InlineData(BloodRange.FullVolume)]
        [InlineData(BloodRange.UsableVolume)]
        public void The_countdown_runs_to_the_next_threshold_at_the_current_rate(BloodRange range)
        {
            var lines = BloodReadout.Build(4000f, 5000f, 2, 25f, Tier2(), Ml(range));

            Assert.Equal("T3 in 40s", lines.Estimate);
        }

        /// <summary>Under a minute counts in seconds, over it in minutes and seconds.</summary>
        [Theory]
        [InlineData(41f, "41s")]
        [InlineData(59f, "59s")]
        [InlineData(60f, "1:00")]
        [InlineData(135f, "2:15")]
        public void The_clock_switches_to_minutes_at_a_minute(float seconds, string expected)
        {
            Assert.Equal(expected, BloodReadout.Clock(seconds));
        }

        /// <summary>
        /// Volume can sit below the boundary for a frame or two before the tier catches
        /// up. A countdown to something already behind you is worse than none.
        /// </summary>
        [Fact]
        public void No_countdown_to_a_threshold_already_passed()
        {
            var lines = BloodReadout.Build(2900f, 5000f, 2, 25f, Tier2(), Ml());

            Assert.True(lines.Bleeding);
            Assert.Equal(string.Empty, lines.Estimate);
        }

        [Theory]
        [InlineData(3600f, true)]  // 72% — above the 70% warning
        [InlineData(3500f, false)] // exactly at it
        [InlineData(3000f, false)]
        public void The_warning_trips_at_the_threshold(float cur, bool comfortable)
        {
            var lines = BloodReadout.Build(cur, 5000f, 1, 0f, Tier2(), Ml());

            Assert.Equal(!comfortable, lines.Warning);
        }

        /// <summary>The warning is about the body, not about how it is being displayed.</summary>
        [Fact]
        public void The_warning_does_not_move_with_the_display_scale()
        {
            var full = BloodReadout.Build(3400f, 5000f, 2, 0f, Tier2(), Ml());
            var usable = BloodReadout.Build(3400f, 5000f, 2, 0f, Tier2(), Ml(BloodRange.UsableVolume));

            Assert.True(full.Warning);
            Assert.True(usable.Warning);
        }

        /// <summary>
        /// A capacity of zero has no fraction of it that means anything. The panel is
        /// still drawn every frame, so this has to produce strings rather than divide by
        /// it — the state exists for a frame if a profile arrives empty.
        /// </summary>
        [Fact]
        public void A_zero_capacity_produces_strings_rather_than_infinities()
        {
            var lines = BloodReadout.Build(0f, 0f, 0, 5f, Tier2(), Ml());

            Assert.Equal("0", lines.Volume);
            Assert.Equal(string.Empty, lines.Estimate);
            Assert.False(lines.Warning);
        }

        /// <summary>
        /// The game runs on whatever locale the machine has. Formatted through the
        /// current culture, a Russian or German install would print "3,2 ml/s" and a
        /// decimal comma in the HUD — so the readout pins the culture itself.
        /// </summary>
        [Fact]
        public void The_rate_prints_the_same_on_a_comma_decimal_locale()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                var lines = BloodReadout.Build(4000f, 5000f, 1, 3.2f, Tier2(), Ml());

                Assert.Equal("3.2 ml/s", lines.Rate);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
