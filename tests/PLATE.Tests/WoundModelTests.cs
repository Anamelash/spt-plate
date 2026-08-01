using System;
using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// What the wound channel must not lose again.
    ///
    /// Up to 0.9.5 a bullet crossing a torso was credited with the share of the PATH it
    /// travelled (~30%) instead of the share of its ENERGY it actually lost (~80%); a
    /// bullet the game called "stopped" was allowed to carve its full gelatin depth,
    /// 800 mm of cavity inside a 250 mm chest, and to hand over its full muzzle energy
    /// to a part it had merely clipped.
    ///
    /// Pure arithmetic: no game assemblies, no Unity runtime.
    /// </summary>
    public class WoundModelTests
    {
        // the shipped defaults, so the numbers below mean what they mean in a raid
        private static AmmoDataCache.WoundParams Params() => new AmmoDataCache.WoundParams
        {
            Enabled = true,
            GelDepthK = 2700,
            GelStopVelocity = 50,
            ExpansionDepthFactor = 0.4,
            ExpansionAreaFactor = 1.35,
            BodyDepthMm = 250,
            WoundVolumePerHp = 710,
            TcVelocityCenter = 600,
            TcVelocityWidth = 80,
            TcEnergyPerHp = 28,
            TcFragBonus = 0.5,
            EnergyCapPerHp = 7,
        };

        // 7.62x51 M80 as the game has it, at the impact velocity seen in raid logs
        private const float MassG = 9.5f;
        private const float DiaMm = 7.85f;
        private const float V = 803f;
        private const float X = 0.79f;

        /// <summary>
        /// When the channel ends inside the part, everything is left behind except what
        /// the projectile still carries at the velocity where it stops cutting tissue —
        /// a residue of (v_stop/v)², which is 0.4% for a rifle round and only becomes
        /// visible down in pistol-subsonic territory. No flag decides this; the same
        /// drag law that ends the channel also says how much energy went into it.
        /// </summary>
        [Fact]
        public void A_channel_that_ends_inside_the_part_leaves_everything_it_could()
        {
            var p = Params();
            const float chord = 600f;
            const float slow = 120f;

            var l = ClientWoundModel.ChannelMm(MassG, DiaMm, slow, X, p);
            Assert.True(l < chord, "the test needs a channel that ends inside the part");

            var residue = (p.GelStopVelocity / slow) * (p.GelStopVelocity / slow);
            var d = ClientWoundModel.Compute(MassG, DiaMm, slow, X, 0f, chord, p);
            Assert.Equal(1.0 - residue, d.DepositFrac, 3);

            var fast = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, 4000f, p);
            Assert.True(fast.DepositFrac > 0.99f,
                $"a rifle round that went nowhere else left only {fast.DepositFrac:P1} behind");
        }

        /// <summary>
        /// A part the projectile merely clipped must not be charged for the whole
        /// cartridge. Before the drag law decided this, a bullet with a metre of
        /// penetration left "in" a 96 mm calf was credited with 100% of its energy.
        /// </summary>
        [Fact]
        public void A_clipped_part_is_not_charged_for_the_whole_cartridge()
        {
            var p = Params();
            var d = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, 96f, p);

            Assert.True(d.DepositFrac < 0.5f,
                $"96 mm of tissue absorbed {d.DepositFrac:P0} of a rifle round");
        }

        /// <summary>
        /// The share left behind follows the same quadratic drag as the channel depth:
        /// v(s) = v·exp(-s/lambda), so the energy lost crossing the part is
        /// 1-(v_out/v)². Lambda is derived here from the model's own channel depth
        /// rather than from the deposition code, so the two have to agree.
        /// </summary>
        [Fact]
        public void A_through_shot_leaves_the_energy_the_drag_law_takes_from_it()
        {
            var p = Params();
            const float chord = 250f;

            var l = ClientWoundModel.ChannelMm(MassG, DiaMm, V, X, p);
            var lambda = l / Math.Log(V / p.GelStopVelocity);
            var vOut = V * Math.Exp(-chord / lambda);
            var expected = 1.0 - (vOut / V) * (vOut / V);

            var d = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, chord, p);

            Assert.Equal(expected, d.DepositFrac, 3);

            // and it must be nowhere near the share of the path, which is what the
            // model used to credit: 250 of ~1000 mm
            Assert.True(d.DepositFrac > 0.6f,
                $"a rifle bullet crossing 250 mm of tissue left only {d.DepositFrac:P0} of its energy");
        }

        /// <summary>
        /// The cavity is set by the tissue actually crossed, not by how deep the round
        /// would have gone in a block of gelatin.
        /// </summary>
        [Fact]
        public void The_cavity_is_no_longer_than_the_part()
        {
            var p = Params();
            const float chord = 250f;

            var l = ClientWoundModel.ChannelMm(MassG, DiaMm, V, X, p);
            Assert.True(l > 3f * chord, "the test needs a channel far longer than the part");

            var d = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, chord, p);

            var area = Math.PI * DiaMm * DiaMm / 4.0;
            var expected = area * (1 + p.ExpansionAreaFactor * X) * chord / p.WoundVolumePerHp;
            Assert.Equal(expected, d.Pc, 1);
        }

        /// <summary>
        /// More tissue in front of the projectile is never less damage.
        /// </summary>
        [Fact]
        public void Damage_grows_with_the_path_through_the_part()
        {
            var p = Params();
            var last = 0f;
            foreach (var chord in new[] { 50f, 100f, 150f, 250f, 350f, 500f })
            {
                var d = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, chord, p);
                Assert.True(d.DamageHp >= last,
                    $"chord {chord} mm dealt {d.DamageHp:0.#}, less than the shorter path before it");
                last = d.DamageHp;
            }
        }
    }
}
