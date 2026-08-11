using PLATE.Client.Ballistics;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// What a hit costs the plate, by material. The reference points come from the
    /// measurements behind the rule: Armox 600T losing nothing to same-spot AP hits
    /// below the limit, Dyneema HB26 gaining V50 during its own multi-hit test, and
    /// the ceramic certification budgets landing where the plain energy price already
    /// puts them (ESAPI's three shots per threat; the observed 2-4 rifle, 5-10
    /// intermediate, 10-20 pistol stops).
    ///
    /// Pure arithmetic: no game assemblies, no Unity runtime.
    /// </summary>
    public class ArmorDamageCalculatorTests
    {
        private const float F = 0.5f;   // WearDepthFraction the server ships
        private const float FibreBlock = 0f;

        private static ArmorDamageCalculator.Verdict Assess(string cls, string mode,
            bool pen, float v, float v50, float joules, float jPerDura)
        {
            return ArmorDamageCalculator.Assess(cls, mode, pen, v, v50, joules,
                jPerDura, F, FibreBlock);
        }

        // --- metal: free below half depth, ramped above it ---

        [Fact]
        public void A_blocked_hit_below_half_depth_costs_a_metal_plate_nothing()
        {
            var verdict = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                pen: false, v: 489f, v50: 1000f, joules: 2000f, jPerDura: 700f);

            Assert.Equal(0f, verdict.DurabilityLoss);
            Assert.False(verdict.RecordSpot);
        }

        [Fact]
        public void The_ramp_is_continuous_at_the_threshold()
        {
            var atFloor = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                false, 500f, 1000f, 2000f, 700f);
            var justOver = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                false, 501f, 1000f, 2000f, 700f);

            Assert.Equal(0f, atFloor.DurabilityLoss, 3);
            Assert.True(atFloor.RecordSpot, "at the floor the crater is half the plate");
            Assert.InRange(justOver.DurabilityLoss, 0f, 0.02f);
        }

        [Fact]
        public void At_the_limit_a_metal_plate_pays_the_full_energy_price()
        {
            var verdict = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                false, 1000f, 1000f, 2100f, 700f);

            Assert.Equal(3f, verdict.DurabilityLoss, 2);
            Assert.True(verdict.RecordSpot);
        }

        [Fact]
        public void The_price_grows_monotonically_between_threshold_and_limit()
        {
            var previous = -1f;
            for (var v = 500f; v <= 1000f; v += 50f)
            {
                var loss = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                    false, v, 1000f, 2000f, 700f).DurabilityLoss;
                Assert.True(loss >= previous, $"price fell from {previous} at v {v}");
                previous = loss;
            }
        }

        /// <summary>
        /// The depth law is the failure mode's own: plugging work grows as T² so depth
        /// is v/v50, flow grows as T so depth is (v/v50)². The same 0.7·v50 hit bites
        /// 0.7 of a plugging plate and only 0.49 of a flowing one — one pays, the
        /// other does not.
        /// </summary>
        [Fact]
        public void The_same_hit_bites_deeper_into_a_plugging_plate_than_a_flowing_one()
        {
            var plugging = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                false, 700f, 1000f, 2000f, 700f);
            var flowing = Assess(BallisticLimit.Ductile, BallisticLimit.HoleExpansion,
                false, 700f, 1000f, 2000f, 700f);

            Assert.True(plugging.DurabilityLoss > 0f);
            Assert.True(plugging.RecordSpot);
            Assert.Equal(0f, flowing.DurabilityLoss);
            Assert.False(flowing.RecordSpot);
        }

        /// <summary>
        /// A projectile that died on the face is spread mass and digs by the flow
        /// reading whatever the plate's own law: the linear depth belongs to a rigid
        /// punch boring its own calibre. The reference case is the raid that surfaced
        /// it — soft-point 7.62x39 point-blank into a Бр3 steel panel at 0.7 of the
        /// limit: a rigid core there bites 0.70 and pays, the mushroomed slug bites
        /// 0.49 and leaves a dent the plate does not count.
        /// </summary>
        [Fact]
        public void A_dead_projectile_digs_by_the_flow_reading_whatever_the_plate()
        {
            var rigid = ArmorDamageCalculator.Assess(BallisticLimit.Ductile,
                BallisticLimit.ShearPlugging, false, 700f, 1000f, 2000f, 700f,
                F, FibreBlock, coreDead: false);
            var dead = ArmorDamageCalculator.Assess(BallisticLimit.Ductile,
                BallisticLimit.ShearPlugging, false, 700f, 1000f, 2000f, 700f,
                F, FibreBlock, coreDead: true);

            Assert.True(rigid.DurabilityLoss > 0f);
            Assert.Equal(0f, dead.DurabilityLoss);
            Assert.False(dead.RecordSpot);
        }

        /// <summary>
        /// And near the limit the dead slug still pays — the AR500 Level III
        /// certificate is six M80 ball at ~0.96 of the limit, and a certificate whose
        /// shots are free would be no test at all. At 0.96 the flow reading is 0.92
        /// deep: well past the floor, most of the full price.
        /// </summary>
        [Fact]
        public void Near_the_limit_a_dead_slug_still_pays()
        {
            var nearLimit = ArmorDamageCalculator.Assess(BallisticLimit.Ductile,
                BallisticLimit.ShearPlugging, false, 960f, 1000f, 3300f, 700f,
                F, FibreBlock, coreDead: true);

            Assert.True(nearLimit.DurabilityLoss > 0.5f * 3300f / 700f,
                "a near-limit ball hit should cost most of the full price");
            Assert.True(nearLimit.RecordSpot);
        }

        [Fact]
        public void A_penetration_costs_a_metal_plate_the_full_price_as_before()
        {
            var verdict = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                pen: true, v: 900f, v50: 700f, joules: 1400f, jPerDura: 700f);

            Assert.Equal(2f, verdict.DurabilityLoss, 2);
            Assert.True(verdict.RecordSpot);
        }

        // --- fibre: durability is paid by penetration only ---

        [Fact]
        public void A_fibre_pack_pays_nothing_for_a_hit_it_stopped_but_remembers_it()
        {
            var verdict = Assess(BallisticLimit.Fibrous, "", false, 400f, 500f,
                800f, 400f);

            Assert.Equal(0f, verdict.DurabilityLoss);
            Assert.True(verdict.RecordSpot, "the caught bullet cut its channel's fibres");
        }

        [Fact]
        public void A_fibre_pack_pays_in_full_for_a_penetration()
        {
            var verdict = Assess(BallisticLimit.Fibrous, "", true, 600f, 500f,
                800f, 400f);

            Assert.Equal(2f, verdict.DurabilityLoss, 2);
            Assert.True(verdict.RecordSpot);
        }

        // --- ceramic: the old price, bit for bit ---

        [Theory]
        [InlineData(false, 2000f)]
        [InlineData(true, 1400f)]
        public void A_ceramic_pays_exactly_what_it_always_paid(bool pen, float joules)
        {
            var verdict = Assess(BallisticLimit.Brittle, "", pen, 700f, 800f,
                joules, 150f);

            Assert.Equal(joules / 150f, verdict.DurabilityLoss, 3);
            Assert.True(verdict.RecordSpot);
        }

        // --- the paths with nothing to compute with ---

        [Fact]
        public void No_computed_limit_means_the_old_full_price()
        {
            // an item the server has no geometry for: there is no thickness to halve
            var verdict = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                false, 400f, 0f, 2000f, 700f);

            Assert.Equal(2000f / 700f, verdict.DurabilityLoss, 3);
            Assert.True(verdict.RecordSpot);
        }

        [Fact]
        public void Zero_energy_price_still_records_the_spot_where_the_rules_say_so()
        {
            var verdict = Assess(BallisticLimit.Brittle, "", false, 700f, 800f,
                2000f, 0f);

            Assert.Equal(0f, verdict.DurabilityLoss);
            Assert.True(verdict.RecordSpot);
        }

        // --- the budget windows the research pinned ---

        /// <summary>
        /// A representative plate carries about 45 durability. The energy price alone
        /// puts a ceramic at ~13 pistol / ~3.4 intermediate / ~2 rifle stops — inside
        /// the observed 10-20 / 5-10 / 2-4 and the ESAPI three-shot budget — which is
        /// why the brittle rule did not change. A steel plate below the depth floor
        /// holds those same rounds forever.
        /// </summary>
        [Theory]
        [InlineData(500f, 10, 20)]     // pistol ball
        [InlineData(2000f, 2, 10)]     // intermediate rifle
        [InlineData(3300f, 2, 4)]      // full-power rifle
        public void The_ceramic_budgets_land_in_the_observed_windows(float joules,
            int atLeast, int atMost)
        {
            const float durability = 45f;
            var perHit = Assess(BallisticLimit.Brittle, "", false, 700f, 900f,
                joules, 150f).DurabilityLoss;
            var stops = (int)(durability / perHit);

            Assert.InRange(stops, atLeast, atMost);
        }

        [Fact]
        public void A_steel_plate_stops_subcritical_rounds_forever()
        {
            var perHit = Assess(BallisticLimit.Ductile, BallisticLimit.ShearPlugging,
                false, 400f, 900f, 2000f, 700f).DurabilityLoss;

            Assert.Equal(0f, perHit);
        }
    }
}
