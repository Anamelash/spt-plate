using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Fragmentation without the vanilla field.
    ///
    /// The vanilla FragmentationChance is the game's opinion of a cartridge; the model
    /// derives the fact instead: a bullet breaks up where it turns broadside — that is
    /// where the envelope takes the full load — if it is still faster there than the
    /// jacket can bear, and only its deformable share breaks, never the hard core.
    ///
    /// The criterion of the whole change, straight from the gelatin literature: M193
    /// and M855 fragment at their speeds, the 7.62x39 PS and the monoliths do not, and
    /// no pistol round does — without a single field from vanilla.
    ///
    /// Pure arithmetic: no game assemblies, no Unity runtime.
    /// </summary>
    public class FragmentationTests
    {
        private static AmmoDataCache.WoundParams Params() => new AmmoDataCache.WoundParams
        {
            Enabled = true,
            GelDepthK = 2700,
            GelStopVelocity = 50,
            ExpansionDepthFactor = 0.4,
            ExpansionAreaFactor = 1.35,
            BodyDepthMm = 250,
            WoundVolumePerHp = 381,
            TcVelocityCenter = 600,
            TcVelocityWidth = 80,
            TcEnergyPerHp = 74,
            TcFragBonus = 0.5,
            FragVelocityThreshold = 600,
            EnergyCapPerHp = 7,
        };

        private const float Chord = 250f;

        /// <summary>The cartridge's median turn, the way the server bakes the card.</summary>
        private static float Neck(float diaMm) => diaMm * 20f;

        private static ClientWoundModel.Deposit Hit(float massG, float diaMm, float v,
            float x, float coreMassFrac)
        {
            return ClientWoundModel.Compute(massG, diaMm, v, x, coreMassFrac, Chord,
                Params(), Neck(diaMm));
        }

        [Fact]
        public void M193_fragments_at_its_speed()
        {
            // 56 gr of jacketed lead at 990 m/s: the textbook fragmenting round
            var d = Hit(3.6f, 5.70f, 990f, 0.30f, 0.05f);
            Assert.True(d.Frag > 0.2f,
                $"M193 came through whole (frag {d.Frag:0.00}) — the round the term exists for");
        }

        [Fact]
        public void M855_fragments_at_its_speed()
        {
            // the steel tip is 16% of the mass and does not break; the lead behind it does
            var d = Hit(4.0f, 5.70f, 950f, 0.25f, 0.162f);
            Assert.True(d.Frag > 0.15f,
                $"M855 came through whole (frag {d.Frag:0.00})");
        }

        [Fact]
        public void The_soviet_ball_does_not()
        {
            // 7.62x39 PS at 720 m/s has already slowed below the jacket's threshold by
            // the time it turns — which is exactly what the gelatin work shows
            var d = Hit(7.9f, 7.92f, 720f, 0.25f, 0.468f);
            Assert.Equal(0f, d.Frag);
        }

        [Fact]
        public void A_monolith_barely_notices_its_own_turn()
        {
            // M995: fast enough at the neck, but 71% of it is a core that cannot break
            // and X says almost none of the rest deforms
            var d = Hit(4.2f, 5.70f, 1013f, 0.05f, 0.71f);
            Assert.True(d.Frag < 0.05f,
                $"a near-solid penetrator fragmented at {d.Frag:0.00}");
        }

        [Fact]
        public void No_pistol_round_fragments()
        {
            // 9x19 ball: the whole flight is below the threshold, never mind the neck
            var d = Hit(8.0f, 9.00f, 375f, 0.30f, 0.05f);
            Assert.Equal(0f, d.Frag);
        }

        /// <summary>
        /// A bullet that exits before it turns never fragments, whatever its speed:
        /// the envelope only fails where the broadside load lands on it.
        /// </summary>
        [Fact]
        public void No_turn_inside_the_body_means_no_fragmentation()
        {
            var d = ClientWoundModel.Compute(3.6f, 5.70f, 990f, 0.30f, 0.05f, 80f,
                Params(), neckMm: 114f); // the chord ends before the turn
            Assert.Equal(0f, d.Frag);
        }
    }
}
