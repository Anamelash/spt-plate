using System;
using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The two properties the wound channel must not lose again.
    ///
    /// Both were wrong at once in 0.9.5 and cancelled each other out badly: a bullet
    /// crossing a torso was credited with the share of the PATH it travelled (~30%)
    /// instead of the share of its ENERGY it actually lost (~80%), while a bullet
    /// that stopped inside was allowed to carve its full gelatin depth — 800 mm of
    /// permanent cavity inside a 250 mm chest.
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

        [Fact]
        public void A_projectile_that_stops_leaves_all_of_its_energy()
        {
            var d = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, 250f, false, Params());

            Assert.Equal(1f, d.DepositFrac, 3);
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

            var d = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, chord, true, p);

            Assert.Equal(expected, d.DepositFrac, 3);

            // and it must be nowhere near the share of the path, which is what the
            // model used to credit: 250 of ~1000 mm
            Assert.True(d.DepositFrac > 0.6f,
                $"a rifle bullet crossing 250 mm of tissue left only {d.DepositFrac:P0} of its energy");
        }

        /// <summary>
        /// A projectile stopped by bone still only wounds the tissue it went through.
        /// </summary>
        [Fact]
        public void A_stopped_projectile_cannot_cut_a_channel_longer_than_the_part()
        {
            var p = Params();
            const float chord = 250f;

            var l = ClientWoundModel.ChannelMm(MassG, DiaMm, V, X, p);
            Assert.True(l > 3f * chord, "the test needs a channel far longer than the part");

            var stopped = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, chord, false, p);
            var through = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, chord, true, p);

            // same tissue crossed either way, so the same permanent cavity
            Assert.Equal(through.Pc, stopped.Pc, 3);

            // and it is the chord that sets it, not the gelatin depth
            var area = Math.PI * DiaMm * DiaMm / 4.0;
            var expected = area * (1 + p.ExpansionAreaFactor * X) * chord / p.WoundVolumePerHp;
            Assert.Equal(expected, stopped.Pc, 1);
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
                var d = ClientWoundModel.Compute(MassG, DiaMm, V, X, 0f, chord, true, p);
                Assert.True(d.DamageHp >= last,
                    $"chord {chord} mm dealt {d.DamageHp:0.#}, less than the shorter path before it");
                last = d.DamageHp;
            }
        }
    }
}
