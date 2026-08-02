using PLATE.Client.Ballistics;
using UnityEngine;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// What comes out the back of a plate.
    ///
    /// Before this, one scalar decided everything a bullet did: how deep the channel
    /// went, how wide the cavity was, how much energy landed on armour and how far the
    /// bullet deformed in it. A jacketed lead ball and a tungsten dart are not the same
    /// mechanism, and the model said they were, with a multiplier keyed off how soft the
    /// bullet was standing in for a penetrator.
    ///
    /// Pure arithmetic: no game assemblies, no Unity runtime beyond Mathf.
    /// </summary>
    public class ArmorExitTests
    {
        // 7.62x51 M993 as the reference book has it: a WC-Co core at 0.49 of the
        // bullet's face and 0.71 of its mass, against the M80 in the same calibre
        private const float Mass = 8.1f;
        private const float Dia = 7.85f;

        /// <summary>
        /// The point of the whole change. Same energy, same calibre, same plate — the
        /// round with a core arrives at twice the density because it is hitting with a
        /// core, not with a jacket.
        /// </summary>
        [Fact]
        public void A_core_concentrates_the_same_energy_onto_less_of_the_plate()
        {
            const float e = 3500f;

            var ball = ArmorExit.ImpactDensity(e, Dia, 1f);
            var ap = ArmorExit.ImpactDensity(e, Dia, 0.491f);

            Assert.Equal(e / (Mathf.PI * Dia * Dia / 4f), ball, 2);
            Assert.True(ap / ball > 1.9f && ap / ball < 2.1f,
                $"the core should roughly double the density, it multiplied it by {ap / ball:0.00}");
        }

        /// <summary>
        /// A monolithic bullet is the case the model used to have: nothing is shed, the
        /// calibre does not change, and the only thing the barrier does is blunt it.
        /// </summary>
        [Fact]
        public void A_bullet_with_no_core_comes_out_whole()
        {
            var exit = ArmorExit.Compute(Mass, Dia, 0.25f, 2000f,
                coreAreaFrac: 1f, coreMassFrac: 1f, kFrag: 0f, kDef: 0.5f);

            Assert.Equal(Mass, exit.MassG, 3);
            Assert.Equal(Dia, exit.DiaMm, 3);
            Assert.Equal(0f, exit.JacketEnergyJ, 3);
            Assert.Equal(0.375f, exit.X, 3); // 0.25 blunted by half again
        }

        /// <summary>
        /// The jacket stays in the hole. What carries on is lighter and narrower, and the
        /// energy it was carrying does not vanish — it stays in the plate.
        /// </summary>
        [Fact]
        public void The_jacket_stays_in_the_plate_and_so_does_its_energy()
        {
            const float eOut = 2000f;
            var exit = ArmorExit.Compute(Mass, Dia, 0.05f, eOut,
                coreAreaFrac: 0.491f, coreMassFrac: 0.712f, kFrag: 0f, kDef: 0.5f);

            Assert.Equal(Mass * 0.712f, exit.MassG, 3);
            Assert.Equal(Dia * Mathf.Sqrt(0.491f), exit.DiaMm, 3);
            Assert.Equal(eOut * (1f - 0.712f), exit.JacketEnergyJ, 1);

            // the core is travelling at the velocity the whole projectile reached:
            // shedding a jacket is not an accelerator
            Assert.Equal(Mathf.Sqrt(2f * eOut / (Mass / 1000f)), exit.V, 1);
        }

        /// <summary>
        /// A bullet that loses its jacket comes out HARDER than it went in. The old model
        /// had this backwards twice over: mass loss scaled as (1 − 0.5X) under a comment
        /// reading "a hard core crumbles more", and X could only ever rise.
        /// </summary>
        [Fact]
        public void Losing_a_jacket_leaves_something_harder_than_the_bullet_was()
        {
            // M855: a soft-ish bullet, but only the steel tip carries on
            var exit = ArmorExit.Compute(4f, 5.7f, 0.25f, 900f,
                coreAreaFrac: 1f, coreMassFrac: 0.162f, kFrag: 0f, kDef: 0.5f);

            Assert.Equal(0f, exit.X, 3); // all 25% of deformable material went with the jacket
            Assert.True(exit.MassG < 0.7f,
                $"the M855 should arrive as its penetrator, it arrived as {exit.MassG:0.00} g");
        }

        /// <summary>
        /// Erosion belongs to the barrier, not to the bullet: ceramic grinds a core down,
        /// aramid does not. It multiplies whatever the core stripping already left.
        /// </summary>
        [Fact]
        public void Ceramic_erodes_what_gets_through_and_aramid_does_not()
        {
            var throughCeramic = ArmorExit.Compute(Mass, Dia, 0.05f, 2000f,
                0.491f, 0.712f, kFrag: 0.35f, kDef: 0.6f);
            var throughAramid = ArmorExit.Compute(Mass, Dia, 0.05f, 2000f,
                0.491f, 0.712f, kFrag: 0f, kDef: 0.05f);

            Assert.Equal(throughAramid.MassG * 0.65f, throughCeramic.MassG, 3);
        }

        /// <summary>
        /// Fractions outside 0..1 are a broken reference entry or a broken mod, not a
        /// bullet that grew on the way through.
        /// </summary>
        [Fact]
        public void A_nonsense_core_fraction_cannot_make_a_bigger_bullet()
        {
            var exit = ArmorExit.Compute(Mass, Dia, 0.5f, 2000f,
                coreAreaFrac: 4f, coreMassFrac: -1f, kFrag: 0f, kDef: 0f);

            Assert.Equal(Dia, exit.DiaMm, 3);
            Assert.True(exit.MassG > 0f && exit.MassG < Mass);
        }
    }
}
