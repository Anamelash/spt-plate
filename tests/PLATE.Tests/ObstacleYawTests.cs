using PLATE.Client.Ballistics;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Barrier-induced yaw: what a crossing leaves the projectile turning by, and what
    /// the NEXT barrier pays for it.
    ///
    /// The module used to let a rigid core cross a row of barrels in a straight line,
    /// every wall costing exactly what the first one did — because nothing in it
    /// destabilised the projectile and deflection is pinned on an anchor far too small
    /// to stand in for that. What actually stops a row of barrels is that the bullet
    /// arrives at the second one sideways. These tests pin the anchor the gain constant
    /// is set by, the ordering slenderness has to produce (a ball cannot yaw, a dart
    /// yaws at once), and the guarantee that a caller who does not fill the geometry in
    /// gets the old numbers back untouched.
    /// </summary>
    public class ObstacleYawTests
    {
        private static readonly ObstacleReference.Book Shipped =
            ObstacleReference.Parse(ObstacleReference.DefaultJsonc);

        private static ObstacleModel.Tuning Tuning => ObstacleReference.TuningOf(Shipped);

        /// <summary>
        /// The broadside geometry as the client reads it with no server on the line —
        /// the same numbers the wound channel is built on, which is the point: a bullet
        /// has one length and the two models must not each have an opinion about it.
        /// </summary>
        private static YawModel.Tuning Geometry =>
            ClientWoundModel.Yaw(new AmmoDataCache.WoundParams());

        private static ObstacleModel.Barrier Barrier(string material, float level)
        {
            Assert.True(ObstacleReference.TryBarrier(Shipped, material, level, out var b),
                $"{material} at level {level} did not resolve to a barrier");
            return b;
        }

        /// <summary>
        /// A projectile with its broadside geometry filled in, exactly as
        /// ObstaclePatches.Read fills it: length and side area from YawModel, yaw from
        /// whatever the last barrier recorded.
        /// </summary>
        /// <param name="lengthMm">The cartridge's measured length as the server publishes
        /// it, mm; 0 = the book is silent and the geometry works it out from the mass.</param>
        private static ObstacleModel.Projectile Bullet(double massG, double diaMm, double v,
            double x = 0.2, double hv = 60, double coreArea = 1, double coreMass = 1,
            double yaw = 0, double lengthMm = 0)
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
                YawFrac = yaw,
                LengthMm = YawModel.LengthMm(massG, diaMm, Geometry, lengthMm),
                SideAreaMm2 = YawModel.SideAreaMm2(massG, diaMm, x, Geometry, lengthMm),
            };
        }

        /// <summary>The same projectile with nothing said about its shape — the state
        /// every caller that does not model yaw hands in.</summary>
        private static ObstacleModel.Projectile Shapeless(ObstacleModel.Projectile p)
        {
            p.LengthMm = 0;
            p.SideAreaMm2 = 0;
            return p;
        }

        /// <summary>
        /// Both constants come out of the BOOK, not out of the C# defaults behind it.
        /// A misspelt key in the jsonc parses cleanly and falls back to the initializer,
        /// so the player's edit would be silently ignored — the same failure mode the
        /// rest of this module's book tests exist to catch.
        /// </summary>
        [Fact]
        public void The_yaw_constants_live_in_the_book()
        {
            Assert.Contains("\"YawGainK\":", ObstacleReference.DefaultJsonc);
            Assert.Contains("\"YawObliquityK\":", ObstacleReference.DefaultJsonc);

            var t = Tuning;
            Assert.True(t.YawGainK > 0);
            Assert.True(t.YawObliquityK > 0);
        }

        // --- The anchor ---

        /// <summary>
        /// What the gain constant is pinned on: a 9x19 ball through a car flank comes out
        /// about half sideways, which is the keyholing on the target that forensic
        /// reconstructions of shots through vehicle doors are recognised by. Three
        /// millimetres of steel takes 43% of the speed and the bullet is barely twice as
        /// long as it is wide, so this is a whole barrier's worth of destabilisation
        /// rather than a nudge.
        /// </summary>
        [Fact]
        public void A_pistol_bullet_through_a_car_flank_comes_out_half_sideways()
        {
            var o = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380, x: 0.3),
                Barrier("VehicleChassis", 4), Tuning, 1);

            Assert.True(o.Penetrates);
            Assert.InRange(o.ExitYaw, 0.4, 0.6);
        }

        // --- What slenderness has to produce ---

        /// <summary>
        /// A sphere has no orientation to lose. Nothing says so in the code — it comes out
        /// of the geometry, because a ball's length is its diameter and the lever arm
        /// L/d − 1 is therefore nearly nothing. The same crossing that half-turns a pistol
        /// bullet leaves a buckshot pellet facing the way it went in.
        /// </summary>
        [Fact]
        public void A_round_ball_does_not_yaw()
        {
            var pellet = Bullet(0.86, 5.25, 400, x: 0.3, hv: 40);
            var o = ObstacleModel.Resolve(pellet, Barrier("MetalThin", 4), Tuning, 1);

            Assert.True(o.Penetrates);
            Assert.InRange(o.ExitYaw, 0, 0.1);

            // and the little it has changes nothing worth measuring on the next barrier
            var next = pellet;
            next.YawFrac = o.ExitYaw;
            Assert.InRange(ObstacleModel.EffectiveAreaMm2(next) /
                           ObstacleModel.EffectiveAreaMm2(pellet), 1.0, 1.05);
        }

        /// <summary>
        /// A dart is the other end of the same lever. A flechette is fifteen calibres
        /// long, and a sheet of tin that costs it five percent of its speed still turns it
        /// most of the way over — which is why flechette rounds are notorious for losing
        /// the plot on the first thing they touch.
        /// </summary>
        [Fact]
        public void A_flechette_loses_the_plot_on_the_first_barrier()
        {
            var o = ObstacleModel.Resolve(Bullet(0.65, 2.0, 700, x: 0.05, hv: 500),
                Barrier("MetalThin", 4), Tuning, 1);

            Assert.True(o.Penetrates);
            Assert.True(o.ExitYaw > 0.5, $"a dart should be well over after one sheet, got {o.ExitYaw:0.00}");
        }

        /// <summary>
        /// Where a length has to come from a measurement rather than from the mass. The
        /// mass-over-calibre inference assumes lead, and 9x19 7N31 is a steel core under
        /// an aluminium jacket: it reads 9.4 mm at a 9 mm calibre, which this model can
        /// only call a ball, so no barrier would ever tip it — and the raid journal shows
        /// it keyholing. Its published 13 mm gives it the lever arm the round has.
        /// </summary>
        [Fact]
        public void A_measured_length_is_what_lets_a_steel_cored_pistol_round_yaw()
        {
            var sheet = Barrier("MetalThin", 4);
            var inferred = ObstacleModel.Resolve(
                Bullet(4.1, 9.0, 600, x: 0.08, hv: 700), sheet, Tuning, 1);
            var published = ObstacleModel.Resolve(
                Bullet(4.1, 9.0, 600, x: 0.08, hv: 700, lengthMm: 13.0), sheet, Tuning, 1);

            Assert.True(inferred.Penetrates && published.Penetrates);
            Assert.InRange(inferred.ExitYaw, 0, 0.02);   // "this is a ball"
            Assert.True(published.ExitYaw > 5 * inferred.ExitYaw,
                $"measured {published.ExitYaw:0.000} should dwarf inferred {inferred.ExitYaw:0.000}");
        }

        /// <summary>Ordered by slenderness, at the same barrier and the same speed.</summary>
        [Fact]
        public void Yaw_is_ordered_by_slenderness()
        {
            var sheet = Barrier("MetalThin", 4);
            var ball = ObstacleModel.Resolve(Bullet(0.86, 5.25, 400, hv: 40), sheet, Tuning, 1);
            var pistol = ObstacleModel.Resolve(Bullet(8.0, 9.0, 400, x: 0.3), sheet, Tuning, 1);

            Assert.True(ball.ExitYaw < pistol.ExitYaw);
        }

        /// <summary>
        /// An angled face loads one side of the nose before the other, so a projectile
        /// leaves it turning harder than it leaves a square one. This is the one term in
        /// the gain that is a judgement rather than a measurement, and the book says so.
        /// </summary>
        [Fact]
        public void An_oblique_crossing_turns_it_further_than_a_square_one()
        {
            var p = Bullet(8.0, 9.0, 500, x: 0.3);
            var flank = Barrier("VehicleChassis", 4);

            var square = ObstacleModel.Resolve(p, flank, Tuning, 1);
            var angled = ObstacleModel.Resolve(p, flank, Tuning, 0.7071);

            Assert.True(square.Penetrates && angled.Penetrates);
            Assert.True(angled.ExitYaw > square.ExitYaw,
                $"45 deg ({angled.ExitYaw:0.000}) should beat normal ({square.ExitYaw:0.000})");
        }

        // --- What the next barrier pays ---

        /// <summary>
        /// The whole reason this exists. A 5.45 steel core crossed a row of thin sheet in
        /// a straight line and lost the same six percent at the twelfth wall as at the
        /// first; now every wall hands it more yaw, the next one meets more of it, and the
        /// row stops it. The comparison is against the same projectile with its geometry
        /// left blank, so nothing but yaw separates the two rows.
        /// </summary>
        [Fact]
        public void A_row_of_thin_walls_stops_what_one_wall_barely_touches()
        {
            var yawing = CrossRow(withGeometry: true, walls: 12, out var yawWalls);
            var rigid = CrossRow(withGeometry: false, walls: 12, out var rigidWalls);

            Assert.Equal(12, rigidWalls); // the old behaviour: straight through the lot
            Assert.True(yawWalls < 12,
                $"a yawing core should die in the row, it crossed {yawWalls} walls");
            Assert.True(yawing < rigid,
                $"yawing exit {yawing:0} m/s should be under rigid {rigid:0} m/s");
        }

        /// <summary>
        /// And each wall in that row is dearer than the one before it — not because the
        /// projectile is slower, but because more of it is presented. Measured against
        /// the rigid row at the same wall index, which strips the velocity dependence out.
        /// </summary>
        [Fact]
        public void Each_wall_in_the_row_costs_more_than_the_last()
        {
            var sheet = Barrier("MetalThin", 4);
            var t = Tuning;
            var p = Bullet(3.68, 5.6, 850, x: 0.05, hv: 1300, coreArea: 0.507, coreMass: 0.512);

            var previous = 0.0;
            for (var wall = 1; wall <= 3; wall++)
            {
                var o = ObstacleModel.Resolve(p, sheet, t, 1);
                Assert.True(o.Penetrates);

                Assert.True(o.Work > previous,
                    $"wall {wall} took {o.Work:0.000} of the speed, wall {wall - 1} took {previous:0.000}");
                previous = o.Work;

                p = Bullet(o.ExitMassG, o.ExitDiaMm, o.ExitV, o.ExitX, 1300, 0.507, 0.512,
                    o.ExitYaw);
            }
        }

        /// <summary>
        /// Yaw reaches the next barrier through one quantity — the area presented — and
        /// every consumer reads it off the same place. The deflection is one of them, and
        /// it needs no multiplier of its own: sectional density is in its denominator
        /// already.
        /// </summary>
        [Fact]
        public void A_yawing_projectile_is_thrown_further_off_line()
        {
            var door = Barrier("WoodThick", 10);
            var straight = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), door, Tuning, 1);
            var sideways = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380, yaw: 0.8), door,
                Tuning, 1);

            Assert.True(sideways.Deviation > straight.Deviation);
        }

        /// <summary>A projectile lying over digs less deeply into a bulk medium.</summary>
        [Fact]
        public void A_yawing_projectile_does_not_reach_as_deep()
        {
            var log = Barrier("WoodThick", 25);
            var straight = ObstacleModel.DepthMm(Bullet(8.0, 9.0, 380), log, Tuning);
            var sideways = ObstacleModel.DepthMm(Bullet(8.0, 9.0, 380, yaw: 0.8), log, Tuning);

            Assert.True(sideways < straight);
        }

        /// <summary>
        /// Yaw is not a calibre. A projectile that arrives sideways presents more area to
        /// the barrier, and that is all: what comes out the far side is the same width it
        /// went in, and the wound model downstream must never read it as a fatter bullet.
        /// </summary>
        [Fact]
        public void Yaw_does_not_widen_what_comes_out()
        {
            var door = Barrier("WoodThick", 10);
            var straight = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), door, Tuning, 1);
            var sideways = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380, yaw: 1.0), door,
                Tuning, 1);

            Assert.Equal(9.0, straight.ExitDiaMm, 6);
            Assert.Equal(9.0, sideways.ExitDiaMm, 6);
        }

        // --- The safe default ---

        /// <summary>
        /// A caller that says nothing about the projectile's shape gets exactly what it
        /// always got. Both halves of the geometry are load-bearing on their own, and a
        /// yaw recorded against a shapeless projectile does nothing at all — which is what
        /// keeps every other test in this suite honest about what changed.
        /// </summary>
        [Theory]
        [InlineData("MetalThin", 4)]
        [InlineData("MetalThick", 18)]
        [InlineData("WoodThick", 10)]
        [InlineData("Concrete", 10)]
        [InlineData("Glass", 0)]
        public void Without_the_geometry_nothing_changes(string material, float level)
        {
            var t = Tuning;
            var b = Barrier(material, level);
            var shaped = Bullet(8.0, 9.0, 500, x: 0.3, yaw: 0.9);

            var blank = ObstacleModel.Resolve(Shapeless(shaped), b, t, 0.8);

            var noLength = shaped;
            noLength.LengthMm = 0;
            var noSide = shaped;
            noSide.SideAreaMm2 = 0;

            foreach (var half in new[] { noLength, noSide })
            {
                var o = ObstacleModel.Resolve(half, b, t, 0.8);
                Assert.Equal(blank.Penetrates, o.Penetrates);
                Assert.Equal(blank.ExitV, o.ExitV, 10);
                Assert.Equal(blank.PathMm, o.PathMm, 10);
                Assert.Equal(blank.DepthMm, o.DepthMm, 10);
                Assert.Equal(blank.V50, o.V50, 10);
                Assert.Equal(blank.Deviation, o.Deviation, 10);
                Assert.Equal(blank.ExitDiaMm, o.ExitDiaMm, 10);
                Assert.Equal(blank.ExitMassG, o.ExitMassG, 10);
                Assert.Equal(0, o.ExitYaw - blank.ExitYaw, 10);
            }
        }

        /// <summary>
        /// And a projectile whose geometry IS filled in but which is still flying nose-on
        /// is the first barrier's case — it too has to reproduce the old numbers, or every
        /// anchor in the module would have moved the day yaw was added.
        /// </summary>
        [Theory]
        [InlineData("MetalThin", 4)]
        [InlineData("MetalThick", 18)]
        [InlineData("WoodThick", 10)]
        [InlineData("Concrete", 10)]
        public void The_first_barrier_is_unchanged(string material, float level)
        {
            var t = Tuning;
            var b = Barrier(material, level);
            var shaped = Bullet(8.0, 9.0, 500, x: 0.3);

            var blank = ObstacleModel.Resolve(Shapeless(shaped), b, t, 0.8);
            var o = ObstacleModel.Resolve(shaped, b, t, 0.8);

            Assert.Equal(blank.ExitV, o.ExitV, 10);
            Assert.Equal(blank.V50, o.V50, 10);
            Assert.Equal(blank.DepthMm, o.DepthMm, 10);
            Assert.Equal(blank.Deviation, o.Deviation, 10);
            Assert.Equal(blank.ExitMassG, o.ExitMassG, 10);
        }

        /// <summary>A barrier that held throws nothing on: no crossing, no yaw.</summary>
        [Fact]
        public void A_barrier_that_holds_adds_no_yaw()
        {
            var o = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380, yaw: 0.3),
                Barrier("WoodThick", 25), Tuning, 1);

            Assert.False(o.Penetrates);
            Assert.Equal(0.3, o.ExitYaw, 6);
        }

        /// <summary>Yaw saturates: fully broadside is as sideways as anything gets.</summary>
        [Fact]
        public void Yaw_never_passes_fully_broadside()
        {
            var o = ObstacleModel.Resolve(Bullet(0.65, 2.0, 700, x: 0.05, hv: 500, yaw: 0.9),
                Barrier("MetalThin", 4), Tuning, 1);

            Assert.Equal(1.0, o.ExitYaw, 6);
        }

        /// <summary>
        /// One row of 1 mm sheet, carrying the whole exit state forward the way
        /// ObstaclePatches does. Returns the speed it came out with (0 if the row held it)
        /// and how many walls it crossed.
        /// </summary>
        private static double CrossRow(bool withGeometry, int walls, out int crossed)
        {
            var sheet = Barrier("MetalThin", 4);
            var t = Tuning;

            // 5.45 BS: the reference book's own core geometry for it
            var p = Bullet(3.68, 5.6, 850, x: 0.05, hv: 1300, coreArea: 0.507, coreMass: 0.512);
            if (!withGeometry)
            {
                p = Shapeless(p);
            }

            crossed = 0;
            var v = 0.0;
            for (var i = 0; i < walls; i++)
            {
                var o = ObstacleModel.Resolve(p, sheet, t, 1);
                if (!o.Penetrates)
                {
                    return 0;
                }

                crossed++;
                v = o.ExitV;

                p = Bullet(o.ExitMassG, o.ExitDiaMm, o.ExitV, o.ExitX, 1300, 0.507, 0.512,
                    o.ExitYaw);
                if (!withGeometry)
                {
                    p = Shapeless(p);
                }
            }

            return v;
        }
    }
}
