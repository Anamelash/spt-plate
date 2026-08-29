using PLATE.Client.Ballistics;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// What a barrier does to a projectile besides slowing it: how far off line it
    /// throws it, and what is left of the projectile itself.
    ///
    /// The two are one mechanism. A rigid projectile's deflection has no velocity in it
    /// — every route to one cancels — so what makes a light fast bullet worse through a
    /// barrier than a heavy slow one is sectional density on one side and, above the
    /// speed where the barrier kills the core, deformation on the other. These tests pin
    /// that ordering and the exit state it comes from.
    /// </summary>
    public class ObstacleDeviationTests
    {
        private static readonly ObstacleReference.Book Shipped =
            ObstacleReference.Parse(ObstacleReference.DefaultJsonc);

        private static ObstacleModel.Tuning Tuning => ObstacleReference.TuningOf(Shipped);

        private static ObstacleModel.Barrier Barrier(string material, float level)
        {
            Assert.True(ObstacleReference.TryBarrier(Shipped, material, level, out var b));
            return b;
        }

        /// <summary>Lead-cored ball unless a hardness is named: 60 HV is lead and copper.</summary>
        private static ObstacleModel.Projectile Bullet(double massG, double diaMm, double v,
            double hv = 60, double coreArea = 1, double coreMass = 1, double x = 0.2)
        {
            return new ObstacleModel.Projectile
            {
                MassG = massG,
                DiaMm = diaMm,
                V = v,
                X = x,
                CoreAreaFrac = coreArea,
                CoreMassFrac = coreMass,
                HardnessHv = hv,
            };
        }

        private static ObstacleModel.Outcome Through(string material, float level,
            ObstacleModel.Projectile p, double cos = 1)
        {
            return ObstacleModel.Resolve(p, Barrier(material, level), Tuning, cos);
        }

        // --- The ordering the deflection has to produce ---

        /// <summary>
        /// The headline case: through one wooden door, the heavy slow pistol bullet comes
        /// out straighter than the light fast one, and the rifle round straighter still.
        /// Nothing about this is a velocity term — it is sectional density.
        /// </summary>
        [Fact]
        public void Heavy_and_slow_comes_out_straighter_than_light_and_fast()
        {
            var acp45 = Through("WoodThick", 10, Bullet(14.9, 11.5, 250)).Deviation;
            var nine = Through("WoodThick", 10, Bullet(8.0, 9.0, 380)).Deviation;
            var fiveSeven = Through("WoodThick", 10, Bullet(2.0, 5.7, 716)).Deviation;

            Assert.True(acp45 < nine, $".45 ({acp45:0.0000}) should beat 9x19 ({nine:0.0000})");
            Assert.True(nine < fiveSeven,
                $"9x19 ({nine:0.0000}) should beat 5.7x28 ({fiveSeven:0.0000})");
        }

        /// <summary>
        /// The anchor the constant is pinned on: a 9x19 through a 45 mm pine door comes
        /// off line by about two degrees. Everything else in the table is that number
        /// times a ratio of areal densities.
        /// </summary>
        [Fact]
        public void A_pistol_bullet_through_a_door_leaves_about_two_degrees_off()
        {
            var s = Through("WoodThick", 10, Bullet(8.0, 9.0, 380)).Deviation;
            var deg = System.Math.Atan(s) * 180.0 / System.Math.PI;
            Assert.InRange(deg, 1.2, 3.0);
        }

        /// <summary>A sheet of tin barely turns anything; a log turns everything.</summary>
        [Fact]
        public void A_heavier_barrier_throws_it_further_off()
        {
            var p = Bullet(9.5, 7.85, 800);
            var tin = Through("MetalThin", 4, p).Deviation;
            var door = Through("WoodThick", 10, p).Deviation;
            var log = Through("WoodThick", 25, p).Deviation;

            Assert.True(tin < door);
            Assert.True(door < log);
        }

        [Fact]
        public void An_oblique_hit_throws_it_further_off_than_a_square_one()
        {
            var p = Bullet(8.0, 9.0, 380);
            Assert.True(Through("WoodThick", 10, p, cos: 0.5).Deviation >
                        Through("WoodThick", 10, p, cos: 1).Deviation);
        }

        /// <summary>
        /// A rigid projectile's deflection does not depend on how fast it was going, and
        /// that is a result rather than an omission — see the derivation in
        /// DeviationScale. Pine cannot deform a lead core at any speed a gun reaches, so
        /// this is the case where it shows in the open.
        /// </summary>
        [Fact]
        public void A_rigid_projectile_is_deflected_the_same_at_any_speed()
        {
            var slow = Through("WoodThick", 10, Bullet(8.0, 9.0, 250));
            var fast = Through("WoodThick", 10, Bullet(8.0, 9.0, 420));

            Assert.Equal(BallisticLimit.CoreFate.Rigid, slow.Fate);
            Assert.Equal(BallisticLimit.CoreFate.Rigid, fast.Fate);
            Assert.Equal(slow.Deviation, fast.Deviation, 6);
        }

        /// <summary>
        /// And where the barrier CAN kill the core, speed is exactly what decides it, so
        /// the faster of the same two bullets comes out worse. This is the whole velocity
        /// dependence of the deflection.
        /// </summary>
        [Fact]
        public void Speed_matters_through_the_deformation_it_causes()
        {
            // a mild steel core against sheet steel: rigid at pistol speed, dead at rifle
            var slow = Through("MetalThin", 4, Bullet(8.0, 9.0, 200, hv: 390));
            var fast = Through("MetalThin", 4, Bullet(8.0, 9.0, 800, hv: 390));

            Assert.Equal(BallisticLimit.CoreFate.Rigid, slow.Fate);
            Assert.Equal(BallisticLimit.CoreFate.Deformed, fast.Fate);
            Assert.True(fast.Deviation > slow.Deviation);
        }

        [Fact]
        public void A_barrier_that_holds_deflects_nothing()
        {
            var stopped = Through("WoodThick", 25, Bullet(8.0, 9.0, 380));
            Assert.False(stopped.Penetrates);
            Assert.Equal(0, stopped.Deviation, 6);
        }

        // --- What is left of the projectile ---

        /// <summary>
        /// Wood is the classic bullet-recovery medium precisely because it does not
        /// mushroom what it catches: the stagnation pressure of a 0.5 g/cm³ medium never
        /// reaches lead's dynamic yield at any speed a gun produces.
        /// </summary>
        [Fact]
        public void Wood_does_not_deform_a_bullet()
        {
            var p = Bullet(8.0, 9.0, 380);
            var door = Through("WoodThick", 10, p);

            Assert.Equal(BallisticLimit.CoreFate.Rigid, door.Fate);
            Assert.Equal(p.MassG, door.ExitMassG, 4);
            Assert.Equal(p.DiaMm, door.ExitDiaMm, 4);
            Assert.Equal(p.X, door.ExitX, 4);
        }

        /// <summary>
        /// Steel does. A pistol bullet mushrooms on a plate that nearly stopped it, comes
        /// out blunter and lighter, and the mass it left behind is in the hole.
        /// </summary>
        [Fact]
        public void Steel_mushrooms_a_lead_bullet_and_shaves_mass_off_it()
        {
            var p = Bullet(8.0, 9.0, 380);
            var plate = Through("MetalThick", 18, p); // 4 mm

            Assert.True(plate.Penetrates);
            Assert.Equal(BallisticLimit.CoreFate.Deformed, plate.Fate);
            Assert.True(plate.ExitX > p.X, "a dead core comes out blunter");
            Assert.True(plate.ExitMassG < p.MassG, "and lighter");
            Assert.True(plate.ExitMassG > p.MassG * 0.8, "but it is not shredded");
        }

        /// <summary>
        /// The scaling that makes one set of constants work from a sheet of paper to a
        /// log: what a barrier is allowed to do to a projectile is proportional to how
        /// much of its speed it actually took. Tin and plate both deform a lead bullet;
        /// only one of them does it appreciably.
        /// </summary>
        [Fact]
        public void A_barrier_that_barely_slowed_it_barely_deforms_it()
        {
            var p = Bullet(8.0, 9.0, 380);
            var tin = Through("MetalThin", 4, p);    // 0.7 mm
            var plate = Through("MetalThick", 18, p); // 4 mm

            Assert.Equal(BallisticLimit.CoreFate.Deformed, tin.Fate);
            Assert.True(tin.Work < 0.2, $"tin takes little of the speed (work={tin.Work:0.000})");
            Assert.True(plate.Work > 0.4, "4 mm takes a lot of it");

            Assert.True(tin.ExitX - p.X < (plate.ExitX - p.X) * 0.4,
                $"sheet dX={tin.ExitX - p.X:0.000} plate dX={plate.ExitX - p.X:0.000}");
            Assert.True(tin.ExitMassG > p.MassG * 0.97); // 1.0 mm sheet: a couple of percent
        }

        /// <summary>
        /// A pane of glass nicks the nose and nothing more — the fate says "deformed",
        /// and the work scaling is what keeps that from meaning anything much.
        /// </summary>
        [Fact]
        public void A_window_costs_a_bullet_its_nose_and_no_more()
        {
            var p = Bullet(8.0, 9.0, 380);
            var pane = Through("Glass", 0, p);

            Assert.True(pane.Penetrates);
            Assert.True(pane.ExitX - p.X < 0.02);
            Assert.True(pane.ExitMassG > p.MassG * 0.995);
        }

        /// <summary>
        /// A hard core carries its jacket through thin sheet and leaves it in a plate.
        /// The threshold is how much of the speed the barrier took, because that is what
        /// says whether there was a rim there to shear against.
        /// </summary>
        [Fact]
        public void A_jacket_is_stripped_by_a_plate_and_not_by_a_sheet()
        {
            // 5.45 7N10-like: hard steel core, roughly half the frontal area
            var ap = Bullet(3.43, 5.6, 880, hv: 390, coreArea: 0.55, coreMass: 0.5, x: 0.05);

            var tin = Through("MetalThin", 4, ap);
            var plate = Through("MetalThick", 32, ap); // 6 mm

            Assert.True(tin.Penetrates && plate.Penetrates);

            // the sheet nicks the nose — at 880 m/s the stagnation pressure on steel is
            // over twice the core's dynamic yield, so the fate is honestly "deformed" —
            // but there is no rim to shear a jacket against, so the calibre is untouched
            // and the mass loss stays around a percent
            Assert.Equal(ap.DiaMm, tin.ExitDiaMm, 3);
            Assert.True(tin.ExitMassG > ap.MassG * 0.985, // ~1% at the 1.0 mm sheet
                $"tin took {ap.MassG - tin.ExitMassG:0.000} g off a hard core (exit {tin.ExitMassG:0.000})");

            Assert.True(plate.ExitMassG < ap.MassG * 0.7, "the plate keeps the jacket");
            Assert.True(plate.ExitDiaMm < ap.DiaMm, "what carries on is the core");
        }

        /// <summary>
        /// A hardened core is not deformed by what deforms lead — the same separation the
        /// armour model is calibrated on, applied to a wall.
        /// </summary>
        [Fact]
        public void A_hardened_core_survives_what_kills_a_lead_one()
        {
            var lead = Through("MetalThick", 7, Bullet(8.0, 9.0, 380));
            var hard = Through("MetalThick", 7, Bullet(8.0, 9.0, 380, hv: 700));

            Assert.Equal(BallisticLimit.CoreFate.Deformed, lead.Fate);
            Assert.Equal(BallisticLimit.CoreFate.Rigid, hard.Fate);
        }

        [Fact]
        public void Nothing_that_did_not_get_through_reports_an_exit_state()
        {
            var p = Bullet(0.2, 3.7, 300);
            var stopped = Through("MetalThin", 4, p);

            Assert.False(stopped.Penetrates);
            Assert.Equal(0, stopped.ExitV, 6);
            Assert.Equal(p.MassG, stopped.ExitMassG, 4);
            Assert.Equal(p.X, stopped.ExitX, 4);
        }

        [Fact]
        public void Mass_and_calibre_never_grow()
        {
            var p = Bullet(9.5, 7.85, 800);
            foreach (var level in new[] { 4f, 7f, 18f, 32f })
            {
                var o = Through(level > 4 ? "MetalThick" : "MetalThin", level, p);
                if (!o.Penetrates)
                {
                    continue;
                }

                Assert.True(o.ExitMassG <= p.MassG + 1e-6, $"mass grew at level {level}");
                Assert.True(o.ExitDiaMm <= p.DiaMm + 1e-6, $"calibre grew at level {level}");
                Assert.InRange(o.ExitX, 0, 1);
            }
        }
    }
}
