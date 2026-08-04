using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The probabilistic wear rule (3.4): one curve, two inputs. The reference points
    /// come straight from the resolution — after ONE recorded hit in the same spot a
    /// ceramic tile is rubble at 15% thickness, hard steel keeps 75%, soft
    /// ductile/UHMWPE 94%, aramid 99% — and the max() rule is what keeps the seen and
    /// unseen paths from telling two stories about the same plate.
    ///
    /// Pure arithmetic: no game assemblies, no Unity runtime.
    /// </summary>
    public class ArmorWearTests
    {
        // the per-material (q, k) the server ships
        private const float CeramicQ = 0.90f, CeramicK = 1.5f;
        private const float HardQ = 0.50f, HardK = 2f;
        private const float SoftQ = 0.40f, SoftK = 3f;
        private const float AramidQ = 0.30f, AramidK = 4f;

        [Theory]
        [InlineData(CeramicQ, CeramicK, 0.15f)] // tile struck once: rubble
        [InlineData(HardQ, HardK, 0.75f)]       // metal holds, with a stress raiser
        [InlineData(SoftQ, SoftK, 0.94f)]
        [InlineData(AramidQ, AramidK, 0.99f)]   // cut fibres in the spot, neighbours intact
        public void The_first_repeat_hit_meets_what_the_resolution_says(
            float q, float k, float expected)
        {
            var frac = ArmorWear.WornFraction(1, 0f, q, k, roll: 1f);
            Assert.Equal(expected, frac, 2);
        }

        [Fact]
        public void An_undamaged_spot_on_a_fresh_plate_is_the_whole_plate()
        {
            Assert.Equal(1f, ArmorWear.WornFraction(0, 0f, CeramicQ, CeramicK, 0.99f));
        }

        /// <summary>
        /// The near-mint trap the resolution called out: a plate at 95% durability
        /// must not hand a repeat hit 99% of its thickness. Seen damage is the
        /// point's, not the plate's — the global number never dilutes it.
        /// </summary>
        [Fact]
        public void Seen_damage_is_never_diluted_by_a_healthy_plate()
        {
            var seen = ArmorWear.WornFraction(1, 0.05f, CeramicQ, CeramicK, 1f);
            Assert.True(seen < 0.16f,
                $"a hit into the same tile of a nearly mint plate met {seen:P0} of it");
        }

        /// <summary>
        /// The max() rule: where the roll says the unseen spot is damaged, it cannot
        /// be less damaged than one hit leaves a spot — otherwise the two paths
        /// disagree about what may be one and the same situation.
        /// </summary>
        [Fact]
        public void The_unseen_path_meets_the_seen_path_at_the_boundary()
        {
            // rolled into damage on a half-worn ceramic: x = max(0.5, 0.9) = 0.9 —
            // exactly what one seen hit gives
            var unseen = ArmorWear.WornFraction(0, 0.5f, CeramicQ, CeramicK, roll: 0.1f);
            var seen = ArmorWear.WornFraction(1, 0.5f, CeramicQ, CeramicK, roll: 1f);
            Assert.Equal(seen, unseen, 3);
        }

        [Fact]
        public void Missing_durability_is_the_chance_of_finding_damage()
        {
            // roll below the missing fraction — damage found; above — clean spot
            Assert.True(ArmorWear.WornFraction(0, 0.5f, HardQ, HardK, 0.49f) < 1f);
            Assert.Equal(1f, ArmorWear.WornFraction(0, 0.5f, HardQ, HardK, 0.51f));
        }

        /// <summary>
        /// Deep unseen wear outgrows q: a plate at 30% durability that rolls into a
        /// damaged spot reads the missing 70%, not the single-hit 50%.
        /// </summary>
        [Fact]
        public void Deep_wear_reads_deeper_than_one_hit()
        {
            var deep = ArmorWear.WornFraction(0, 0.7f, HardQ, HardK, 0f);
            var one = ArmorWear.WornFraction(1, 0f, HardQ, HardK, 0f);
            Assert.True(deep < one,
                $"70% missing gave {deep:P0} against one hit's {one:P0}");
        }

        [Fact]
        public void Repeat_hits_grind_the_spot_down_monotonically()
        {
            var last = 1f;
            for (var n = 1; n <= 6; n++)
            {
                var frac = ArmorWear.WornFraction(n, 0f, HardQ, HardK, 1f);
                Assert.True(frac < last, $"hit {n} left more plate than hit {n - 1}");
                last = frac;
            }
        }
    }
}
