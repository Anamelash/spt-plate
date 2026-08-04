using System;
using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The part of a shot that is not the same twice.
    ///
    /// A multiplier of N(1, sigma) on the final damage would be noise wearing a model's
    /// clothes. What is tested here is that the spread comes from where it comes from in
    /// reality and then correlates by itself: one draw per projectile, carried through
    /// overpenetration children, and a neck length that varies multiplicatively because
    /// that is how it varies in gelatin.
    /// </summary>
    public class ShotSpreadTests : IClassFixture<GameFixture>
    {
        public ShotSpreadTests(GameFixture fixture)
        {
            _ = fixture; // binds the config the draws read their sigmas from
        }

        private static AmmoDataCache.WoundParams Wound() => new AmmoDataCache.WoundParams
        {
            Enabled = true,
            YawNeckCalibres = 20,
            YawBroadsideFraction = 0.75,
            BulletDensityGPerCm3 = 10.5,
            BulletFormFactor = 0.65,
        };

        /// <summary>
        /// Box-Muller with the cosine at zero: whatever the first uniform was, the draw
        /// is the mean. A stub rather than a statistic, so the wiring is pinned exactly.
        /// </summary>
        private class Scripted : Random
        {
            private readonly double[] _values;
            private int _i;

            public Scripted(params double[] values) => _values = values;

            public override double NextDouble() => _values[_i++ % _values.Length];
        }

        [Fact]
        public void The_median_draw_is_the_median()
        {
            Assert.Equal(0f, ShotSpread.Normal(0.35f, new Scripted(0.5, 0.25)), 5);
            Assert.Equal(157f, ShotSpread.LogNormal(157f, 0.35f, new Scripted(0.5, 0.25)), 3);
        }

        /// <summary>One sigma out: u1 = e^-0.5 makes the radius exactly one, u2 = 0 the cosine one.</summary>
        [Fact]
        public void One_sigma_comes_out_one_sigma()
        {
            var u1 = Math.Exp(-0.5);

            Assert.Equal(0.35f, ShotSpread.Normal(0.35f, new Scripted(u1, 0.0)), 4);
            Assert.Equal(157f * Math.Exp(0.35), ShotSpread.LogNormal(157f, 0.35f,
                new Scripted(u1, 0.0)), 2);
        }

        [Fact]
        public void The_draws_have_the_mean_and_the_spread_they_claim()
        {
            var rng = new Random(20260803);
            const int n = 40000;
            var sum = 0.0;
            var sumSq = 0.0;

            for (var i = 0; i < n; i++)
            {
                var v = ShotSpread.Normal(0.35f, rng);
                sum += v;
                sumSq += v * v;
            }

            var mean = sum / n;
            var sd = Math.Sqrt(sumSq / n - mean * mean);

            Assert.InRange(mean, -0.01, 0.01);
            Assert.InRange(sd, 0.34, 0.36);
        }

        /// <summary>
        /// A neck cannot be negative and its spread is multiplicative — one cartridge's
        /// neck length varies twofold in gelatin, which is a ratio and not a distance.
        /// That is the whole reason it is log-normal.
        /// </summary>
        [Fact]
        public void A_neck_varies_multiplicatively_and_never_goes_negative()
        {
            var rng = new Random(19951231);
            const int n = 40000;
            var draws = new double[n];

            for (var i = 0; i < n; i++)
            {
                draws[i] = ShotSpread.LogNormal(157f, 0.35f, rng);
            }

            Assert.All(draws, d => Assert.True(d > 0, $"a neck came out at {d:0.0} mm"));

            Array.Sort(draws);
            var median = draws[n / 2];
            var upper = draws[(int)(n * 0.8413)];

            Assert.InRange(median, 152, 162);
            Assert.InRange(upper / median, Math.Exp(0.35) * 0.97, Math.Exp(0.35) * 1.03);
        }

        [Fact]
        public void A_shot_draws_once_per_organ_and_keeps_the_number()
        {
            var spread = new ShotSpread();

            var first = spread.RollFor(1, out var fresh);
            Assert.True(fresh);

            var again = spread.RollFor(1, out var reused);
            Assert.False(reused);
            Assert.Equal(first, again);

            spread.RollFor(2, out var other);
            Assert.True(other, "a different organ is a different question");
        }

        /// <summary>
        /// The behaviour the single draw exists for, and the trap in it.
        ///
        /// One organ is several collider boxes, so a projectile meets the same heart
        /// twice and each meeting has its own chance. Rolling twice would nearly double
        /// it; remembering the verdict of the first would let a glancing pass at 1% use
        /// the roll up and silence the crossing behind it at 30%. Keeping the NUMBER and
        /// re-testing it comes out at the best chance the shot ever had, which is what
        /// both meetings together are worth.
        /// </summary>
        [Fact]
        public void The_strongest_meeting_with_an_organ_is_the_one_that_decides()
        {
            const int n = 40000;
            var afterWeakFirst = 0;
            var afterTwoEqual = 0;

            for (var i = 0; i < n; i++)
            {
                var a = new ShotSpread();
                if (a.RollFor(1, out _) < 0.01f || a.RollFor(1, out _) < 0.30f)
                {
                    afterWeakFirst++;
                }

                var b = new ShotSpread();
                if (b.RollFor(1, out _) < 0.30f || b.RollFor(1, out _) < 0.30f)
                {
                    afterTwoEqual++;
                }
            }

            // the glancing 1% must not be what decided it
            Assert.InRange(afterWeakFirst / (double)n, 0.29, 0.31);

            // and two meetings at 30% are still 30%, not the 51% of two independent rolls
            Assert.InRange(afterTwoEqual / (double)n, 0.29, 0.31);
        }

        [Fact]
        public void An_organ_is_counted_once_however_many_boxes_it_is_cut_into()
        {
            var spread = new ShotSpread();

            Assert.True(spread.FirstTouch(1));
            Assert.False(spread.FirstTouch(1));

            Assert.True(spread.FirstThrough(1));
            Assert.False(spread.FirstThrough(1));

            Assert.True(spread.FirstLethal(1));
            Assert.False(spread.FirstLethal(1));

            Assert.True(spread.FirstTouch(2), "a different organ counts on its own");
        }

        /// <summary>
        /// One liver bleeds at one rate however many boxes the game cuts it into. A graze
        /// that opened 30 ml/s followed by a run-through worth 80 has to end at 80, not
        /// at 110.
        /// </summary>
        [Fact]
        public void Bleeding_from_one_organ_tops_up_instead_of_stacking()
        {
            var spread = new ShotSpread();

            Assert.Equal(30f, spread.BleedTopUp(2, 30f), 3);
            Assert.Equal(50f, spread.BleedTopUp(2, 80f), 3);
            Assert.Equal(0f, spread.BleedTopUp(2, 80f), 3);
            Assert.Equal(0f, spread.BleedTopUp(2, 40f), 3);
        }

        /// <summary>
        /// One draw per projectile, not per frame and not per body part. A shot crossing
        /// two body parts that got two independent draws would have stopped being one
        /// shot.
        /// </summary>
        [Fact]
        public void The_same_projectile_is_only_drawn_for_once()
        {
            var bullet = new object();
            var first = ShotSpread.For(bullet, 7.85f, Wound());
            var again = ShotSpread.For(bullet, 7.85f, Wound());

            Assert.Same(first, again);
        }

        /// <summary>
        /// The child of an overpenetration is the same shot in the same body, and a
        /// projectile does not un-turn: what it has already crossed comes off its neck,
        /// so a bullet that turned in an arm arrives at the chest already sideways.
        /// </summary>
        [Fact]
        public void An_overpenetration_child_carries_the_shot_on_minus_what_it_crossed()
        {
            var parent = new object();
            var child = new object();
            var from = ShotSpread.For(parent, 7.85f, Wound());
            from.NeckMm = 200f;
            from.TissueScale = 1.12f;
            from.ZoneShiftMm = -7f;

            ShotSpread.Inherit(parent, child, consumedMm: 90f);
            var carried = ShotSpread.For(child, 7.85f, Wound());

            Assert.Equal(110f, carried.NeckMm, 3);
            Assert.Equal(1.12f, carried.TissueScale, 3);
            Assert.Equal(-7f, carried.ZoneShiftMm, 3);
        }

        /// <summary>
        /// The child of an overpenetration is the same projectile, so what the shot has
        /// already done to an organ travels with it. A child with its own fresh slots
        /// would roll the same heart a second time — the one thing a single draw per
        /// shot exists to prevent.
        /// </summary>
        [Fact]
        public void A_child_shares_what_the_shot_has_already_done_to_an_organ()
        {
            var parent = new object();
            var child = new object();
            var from = ShotSpread.For(parent, 7.85f, Wound());
            var drawn = from.RollFor(1, out _);
            Assert.True(from.FirstTouch(1));

            ShotSpread.Inherit(parent, child, consumedMm: 50f);
            var carried = ShotSpread.For(child, 7.85f, Wound());

            Assert.Equal(drawn, carried.RollFor(1, out var fresh));
            Assert.False(fresh, "the child drew again for an organ the parent already met");
            Assert.False(carried.FirstTouch(1), "the same organ was counted twice");
        }

        [Fact]
        public void A_projectile_that_already_turned_stays_turned()
        {
            var parent = new object();
            var child = new object();
            var from = ShotSpread.For(parent, 7.85f, Wound());
            from.NeckMm = 60f;

            ShotSpread.Inherit(parent, child, consumedMm: 250f);

            Assert.Equal(0f, ShotSpread.For(child, 7.85f, Wound()).NeckMm);
        }

        /// <summary>
        /// Two shots are two shots. The trap this guards is subtle: seeding a stream
        /// from a counter gives every consecutive shot a nearly identical opening draw,
        /// so a burst would walk its neck lengths steadily in one direction and look
        /// random in any single log line. Half the pairs going up is what actual
        /// independence looks like.
        /// </summary>
        [Fact]
        public void Consecutive_shots_are_independent_and_do_not_drift()
        {
            const int n = 200;
            var necks = new float[n];
            for (var i = 0; i < n; i++)
            {
                necks[i] = ShotSpread.For(new object(), 7.85f, Wound()).NeckMm;
            }

            Assert.True(new System.Collections.Generic.HashSet<float>(necks).Count > n * 0.95,
                "shots are repeating each other's draws");

            var rising = 0;
            for (var i = 1; i < n; i++)
            {
                if (necks[i] > necks[i - 1])
                {
                    rising++;
                }
            }

            Assert.InRange(rising / (double)(n - 1), 0.35, 0.65);
        }

        /// <summary>A projectile with no identity to hang a draw on gets the median shot.</summary>
        [Fact]
        public void Nothing_to_hang_a_draw_on_is_the_median_shot()
        {
            var fallback = ShotSpread.For(null, 7.85f, Wound());

            Assert.Equal(1f, fallback.TissueScale);
            Assert.Equal(0f, fallback.ZoneShiftMm);
            Assert.True(fallback.NeckMm > 1e6f, "a shot with no draw must never be turning");
        }
    }
}
