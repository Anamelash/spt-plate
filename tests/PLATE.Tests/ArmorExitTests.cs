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
        /// <summary>
        /// What a raid found. A 5.45 PP defeating a 7.5 mm aramid vest was coming out at
        /// half its mass and three quarters of its calibre, because the core split was
        /// applied to every barrier alike. The energy price of the hole was 234 J; the
        /// mass that quietly disappeared was worth another 600.
        ///
        /// A rigid plate does strip a jacket — the hole's rim shears it off, which is why
        /// bullets recovered from steel come back as bare cores. Woven fibre has no rim.
        /// The material profile has said so since it was written ("a penetrating bullet
        /// stays intact", KFrag 0); the core split overrode it without saying anything.
        /// </summary>
        [Fact]
        public void A_fibre_pack_has_no_rim_to_shear_a_jacket_against()
        {
            // 5.45x39 PP as the book has it: a hardened core at about half the mass
            const float mass = 3.7f;
            const float dia = 5.6f;
            const float energy = 1181f;

            var throughPlate = ArmorExit.Compute(mass, dia, 0.15f, energy, 0.54f, 0.49f,
                kFrag: 0f, kDef: 0.05f, stripsJacket: true);
            var throughPack = ArmorExit.Compute(mass, dia, 0.15f, energy, 0.54f, 0.49f,
                kFrag: 0f, kDef: 0.05f, stripsJacket: false);

            Assert.Equal(mass, throughPack.MassG, 3);
            Assert.Equal(dia, throughPack.DiaMm, 3);
            Assert.Equal(0f, throughPack.JacketEnergyJ, 3);

            // and it still keeps the jacket it did not lose, so it stays as deformable as
            // it went in rather than coming out the far side as a solid
            Assert.True(throughPack.X > 0.1f, $"the pack hardened the bullet to {throughPack.X:0.00}");
            Assert.Equal(0f, throughPlate.X, 3);

            // the whole of the finding in one number: the same vest, twice the energy
            var pack = 0.5f * (throughPack.MassG / 1000f) * throughPack.V * throughPack.V;
            var plate = 0.5f * (throughPlate.MassG / 1000f) * throughPlate.V * throughPlate.V;
            Assert.True(pack > 1.9f * plate,
                $"a soft pack passed {pack:0} J where a plate passed {plate:0}");
        }

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

            // M80 ball and M993 at the same energy: X 0.25 against 0.05
            var ball = ArmorExit.ImpactDensity(e, Dia, 1f, 0.25f, 0.6f);
            var ap = ArmorExit.ImpactDensity(e, Dia, 0.491f, 0.05f, 0.6f);

            Assert.True(ap / ball > 2f && ap / ball < 2.4f,
                $"the core should better than double the density, it multiplied it by {ap / ball:0.00}");
        }

        /// <summary>
        /// The other half of the same sentence, and the one this pass nearly dropped.
        /// A hollow point flattens on the face of the panel before it has finished
        /// loading it, so the same energy lands on more of the plate. Removing the old
        /// construction multiplier without putting this back handed every expanding
        /// round in the game a penetration buff.
        /// </summary>
        [Fact]
        public void A_bullet_that_flattens_loads_more_of_the_plate_not_less()
        {
            const float e = 2000f;

            var fmj = ArmorExit.ImpactDensity(e, Dia, 1f, 0.25f, 0.6f);
            var hollow = ArmorExit.ImpactDensity(e, Dia, 1f, 0.9f, 0.6f);

            Assert.True(hollow < fmj,
                "a hollow point must arrive at a lower density than an FMJ of the same energy");
            Assert.Equal(1.15f / 1.54f, hollow / fmj, 3);
        }

        [Fact]
        public void Turning_the_spread_off_leaves_the_bare_cross_section()
        {
            var area = ArmorExit.ImpactArea(Dia, 1f, 0.9f, 0f);

            Assert.Equal(Mathf.PI * Dia * Dia / 4f, area, 3);
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
