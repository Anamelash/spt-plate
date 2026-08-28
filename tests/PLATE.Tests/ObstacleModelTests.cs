using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The environment barrier model, against the shipped book.
    ///
    /// The anchors that matter are the published pine-penetration tables: the depth law
    /// has two free constants, both taken from theory rather than fitted, and what
    /// justifies them is that four cartridges spanning a factor of six in energy land
    /// where Hatcher's white-pine figures put them. Those four are the first block
    /// below and they are the ones to argue with.
    /// </summary>
    public class ObstacleModelTests
    {
        private static readonly ObstacleReference.Book Shipped =
            ObstacleReference.Parse(ObstacleReference.DefaultJsonc);

        private static ObstacleModel.Tuning Tuning => ObstacleReference.TuningOf(Shipped);

        private static ObstacleModel.Barrier Barrier(string material, float level)
        {
            Assert.True(ObstacleReference.TryBarrier(Shipped, material, level, out var b),
                $"{material} at level {level} did not resolve to a barrier");
            return b;
        }

        /// <summary>A solid, non-deforming bullet: X = 0, monolithic core.</summary>
        private static ObstacleModel.Projectile Bullet(double massG, double diaMm, double v,
            double x = 0)
        {
            return new ObstacleModel.Projectile
            {
                MassG = massG,
                DiaMm = diaMm,
                V = v,
                X = x,
                CoreAreaFrac = 1,
                CoreMassFrac = 1,
                HardnessHv = 60,
            };
        }

        // --- The pine calibration ---

        /// <summary>
        /// Depth in seasoned pine against Hatcher's tables. The bands are wide on
        /// purpose: the published figures are ranges themselves, and what is being
        /// checked is that one law with two theory-derived constants spans .22 rimfire
        /// to .30-06 without a per-cartridge fudge.
        /// </summary>
        [Theory]
        // .22 LR, 2.6 g at 330 m/s — published pine penetration about 4-6 in
        [InlineData(2.6, 5.7, 330, 100, 170)]
        // .45 ACP ball, 14.9 g at 250 m/s — about 5-6 in
        [InlineData(14.9, 11.5, 250, 100, 170)]
        // 9x19 ball, 8.0 g at 380 m/s — about 6-8 in
        [InlineData(8.0, 9.0, 380, 170, 230)]
        // .30-06 M2 ball, 9.7 g at 838 m/s — about 27-30 in
        [InlineData(9.7, 7.82, 838, 690, 850)]
        public void Depth_in_pine_matches_the_published_tables(double massG, double diaMm,
            double v, double lowMm, double highMm)
        {
            var pine = Barrier("WoodThick", 25);
            var d = ObstacleModel.DepthMm(Bullet(massG, diaMm, v), pine, Tuning);
            Assert.InRange(d, lowMm, highMm);
        }

        /// <summary>
        /// The medium's stop velocity is computed from its strength and density, not
        /// tuned: pine comes out at about 350 m/s, which is where a bullet stops
        /// throwing wood aside and starts pushing through it.
        /// </summary>
        [Fact]
        public void Pine_stop_velocity_comes_out_of_the_material()
        {
            Assert.InRange(ObstacleModel.StopVelocity(Barrier("WoodThin", 3), Tuning), 330, 360);
        }

        // --- Wood in the game's own thicknesses ---

        [Fact]
        public void Nine_millimetre_goes_through_a_plank_and_a_door_and_stops_in_a_log()
        {
            var t = Tuning;
            var bullet = Bullet(8.0, 9.0, 380);

            var plank = ObstacleModel.Resolve(bullet, Barrier("WoodThin", 3), t, 1);
            Assert.True(plank.Penetrates);
            Assert.InRange(plank.ExitV, 340, 375); // 20 mm of board is nearly free

            var door = ObstacleModel.Resolve(bullet, Barrier("WoodThick", 10), t, 1);
            Assert.True(door.Penetrates);
            Assert.InRange(door.ExitV, 290, 340); // 45 mm of door costs about 60 m/s

            var log = ObstacleModel.Resolve(bullet, Barrier("WoodThick", 25), t, 1);
            Assert.False(log.Penetrates);
            Assert.Equal(0, log.ExitV, 6);
        }

        /// <summary>The same timber a pistol dies in is not a wall to a rifle.</summary>
        [Fact]
        public void A_rifle_round_crosses_the_timber_a_pistol_dies_in()
        {
            var log = Barrier("WoodThick", 25);
            var ps = ObstacleModel.Resolve(Bullet(7.9, 7.92, 715), log, Tuning, 1);

            Assert.True(ps.Penetrates);
            Assert.InRange(ps.ExitV, 380, 500);
        }

        /// <summary>
        /// The depth is where the projectile comes to rest, so a barrier exactly that
        /// thick lets nothing out. The wound channel's depth is defined at a cutting
        /// threshold instead — tissue stops being cut long before the bullet stops —
        /// and the two definitions must not be confused when reading the numbers.
        /// </summary>
        [Fact]
        public void A_barrier_exactly_as_thick_as_the_depth_lets_nothing_out()
        {
            var t = Tuning;
            var pine = Barrier("WoodThick", 25);
            var bullet = Bullet(8.0, 9.0, 380);

            var depth = ObstacleModel.DepthMm(bullet, pine, t);
            Assert.Equal(0, ObstacleModel.PonceletResidual(bullet, pine, t, depth), 4);
            Assert.True(ObstacleModel.PonceletResidual(bullet, pine, t, depth * 0.99) > 0);
        }

        [Fact]
        public void An_expanding_bullet_digs_less_deeply()
        {
            var t = Tuning;
            var pine = Barrier("WoodThick", 25);

            var solid = ObstacleModel.DepthMm(Bullet(8.0, 9.0, 380), pine, t);
            var hollow = ObstacleModel.DepthMm(Bullet(8.0, 9.0, 380, x: 1), pine, t);

            Assert.True(hollow < solid);
            Assert.Equal(solid * 0.6, hollow, 1); // 1 − ExpansionDepthFactor·X
        }

        // --- Steel ---

        [Fact]
        public void Nine_millimetre_shoots_through_environment_sheet_with_a_real_loss()
        {
            // the anchor moved from 0.7 (fence profile) to 1.0 mm with the campaign:
            // the census put 95% of MetalThin on one level, and the typical carrier is
            // a car body or a cabinet, not a fence
            var tin = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), Barrier("MetalThin", 4),
                Tuning, 1);

            Assert.True(tin.Penetrates);
            Assert.InRange(tin.V50, 140, 195);
            Assert.InRange(tin.ExitV, 300, 345);
        }

        /// <summary>
        /// The steel ladder in the book has to stay a ladder for a pistol round: through
        /// the thin plate, marginal in the middle one, stopped by the thick one. That is
        /// the vanilla hierarchy reproduced by thickness rather than by a threshold.
        /// </summary>
        [Theory]
        [InlineData(7, true)]    // 2 mm
        [InlineData(18, true)]   // 4 mm, barely
        [InlineData(32, false)]  // 6 mm
        [InlineData(69, false)]  // 10 mm
        public void The_steel_ladder_holds_a_pistol_round_where_it_should(float level,
            bool expected)
        {
            var outcome = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380),
                Barrier("MetalThick", level), Tuning, 1);
            Assert.Equal(expected, outcome.Penetrates);
        }

        [Fact]
        public void A_rifle_round_crosses_four_millimetres_of_steel()
        {
            var outcome = ObstacleModel.Resolve(Bullet(3.43, 5.6, 880),
                Barrier("MetalThick", 18), Tuning, 1);

            Assert.True(outcome.Penetrates);
            Assert.InRange(outcome.ExitV, 550, 750);
        }

        /// <summary>
        /// The ballistic limit is a distribution, not a number: each encounter draws its
        /// own sheet from within the certification scatter (CV 0.04, ±2σ), so near the
        /// limit some rounds dribble through and some stop — a zone of mixed results
        /// instead of a cliff. The centre of the draw is the old deterministic answer,
        /// which is what every other test in this file implicitly uses.
        /// </summary>
        [Fact]
        public void The_limit_is_a_band_and_not_a_cliff()
        {
            var t = Tuning;
            var tin = Barrier("MetalThin", 4);
            var bullet = Bullet(8.0, 9.0, 380);

            var centre = ObstacleModel.Resolve(bullet, tin, t, 1, 0.5);
            var weak = ObstacleModel.Resolve(bullet, tin, t, 1, 0.0);
            var tough = ObstacleModel.Resolve(bullet, tin, t, 1, 1.0);

            Assert.True(weak.V50 < centre.V50);
            Assert.True(centre.V50 < tough.V50);
            Assert.Equal(centre.V50 * (1 + t.SteelLimitScatter), tough.V50, 3);
            Assert.Equal(centre.V50 * (1 - t.SteelLimitScatter), weak.V50, 3);

            // a velocity inside the band goes through the weak draw and stops in the
            // tough one — the mixed zone in one pair of shots
            var onLimit = Bullet(8.0, 9.0, centre.V50);
            Assert.True(ObstacleModel.Resolve(onLimit, tin, t, 1, 0.0).Penetrates);
            Assert.False(ObstacleModel.Resolve(onLimit, tin, t, 1, 1.0).Penetrates);
        }

        /// <summary>
        /// A fragment is not a bullet: it starts fast but it has almost no sectional
        /// density, so once it has slowed it cannot pay for a hole in even the thinnest
        /// sheet. This is the case vanilla got backwards — a fragment carries the
        /// grenade's template penetration whatever is left of its speed.
        /// </summary>
        [Fact]
        public void A_slowed_fragment_stops_in_sheet_metal()
        {
            var t = Tuning;
            var tin = Barrier("MetalThin", 4);

            Assert.True(ObstacleModel.Resolve(Bullet(0.2, 3.7, 1200), tin, t, 1).Penetrates);
            Assert.False(ObstacleModel.Resolve(Bullet(0.2, 3.7, 300), tin, t, 1).Penetrates);
        }

        // --- Glass and walls ---

        [Fact]
        public void A_pane_costs_a_bullet_almost_nothing_and_a_pellet_something()
        {
            var t = Tuning;
            var glass = Barrier("Glass", 0);

            var bullet = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), glass, t, 1);
            Assert.True(bullet.Penetrates);
            Assert.InRange(bullet.ExitV, 370, 379);

            // a birdshot pellet does not cross a window free: most of its 18 J goes into
            // the pane and what comes out the far side is barely dangerous
            var pellet = ObstacleModel.Resolve(Bullet(0.4, 4.0, 300), glass, t, 1);
            Assert.True(pellet.Penetrates);
            Assert.InRange(pellet.ExitV, 90, 160);

            // and once it has slowed further the pane simply stops it
            Assert.False(ObstacleModel.Resolve(Bullet(0.4, 4.0, 200), glass, t, 1).Penetrates);
        }

        [Fact]
        public void A_wall_is_a_wall_whatever_hits_it()
        {
            var t = Tuning;

            // ground and road surfaces have no far face for the probe to find, so there
            // is nothing to compare a depth against and they stay impassable by decree
            foreach (var name in new[] { "Stone", "Asphalt", "Soil", "Gravel" })
            {
                Assert.False(
                    ObstacleModel.Resolve(Bullet(48.2, 12.98, 850), Barrier(name, 100), t, 1)
                        .Penetrates,
                    $"{name} let a .50 through");
            }
        }

        // --- Concrete and brick ---

        /// <summary>
        /// A cartridge against a wall of the given thickness, straight on.
        /// </summary>
        private static ObstacleModel.Outcome Into(string material, double massG,
            double diaMm, double v, double wallMm)
        {
            var b = Barrier(material, 100);
            b.ThicknessMm = wallMm;
            return ObstacleModel.Resolve(Bullet(massG, diaMm, v), b, Tuning, 1);
        }

        private static ObstacleModel.Outcome IntoConcrete(double massG, double diaMm,
            double v, double wallMm)
        {
            return Into("Concrete", massG, diaMm, v, wallMm);
        }

        /// <summary>
        /// The depth law in concrete, checked where somebody has published a number.
        /// A 7.62 ball leaves 55 mm in ultra-high-performance concrete; the same law at
        /// ordinary structural strength puts a rifle round around 90 mm and a pistol
        /// round around 20, which is the difference between a dent and a hole.
        /// </summary>
        [Theory]
        // 7.62x51 M80, 9.5 g at 830 m/s
        [InlineData(9.5, 7.85, 830, 80, 110)]
        // 9x19 ball: a scar in the surface and nothing more
        [InlineData(8.0, 9.0, 380, 12, 28)]
        // .50 BMG ball, 46 g at 890 m/s
        [InlineData(46.0, 12.95, 890, 160, 210)]
        public void Depth_in_concrete_is_what_the_cavity_expansion_fit_says(double massG,
            double diaMm, double v, double lowMm, double highMm)
        {
            var d = ObstacleModel.DepthMm(Bullet(massG, diaMm, v),
                Barrier("Concrete", 100), Tuning);
            Assert.InRange(d, lowMm, highMm);
        }

        /// <summary>
        /// The wall the whole change is about: one course of brick, 115 mm. A rifle round
        /// goes through it and a pistol round does not, which is what the material was
        /// doing wrong when it was impassable to everything.
        /// </summary>
        [Fact]
        public void A_single_course_of_brick_stops_a_pistol_and_not_a_rifle()
        {
            var rifle = Into("Brick", 9.5, 7.85, 830, 115);
            Assert.True(rifle.Penetrates);
            Assert.InRange(rifle.ExitV, 150, 450); // through, and slowed — not untouched

            Assert.False(Into("Brick", 8.0, 9.0, 380, 115).Penetrates);
        }

        /// <summary>
        /// Brick is the weaker material, and the case that pays for saying so is the
        /// intermediate cartridge: a hard-cored 5.45 crosses one course of brick and does
        /// not cross the same thickness of concrete. Under one shared preset that round
        /// was stopped by both.
        /// </summary>
        [Fact]
        public void Brick_is_weaker_than_concrete_where_it_matters()
        {
            Assert.True(Into("Brick", 4.15, 5.6, 830, 115).Penetrates);
            Assert.False(IntoConcrete(4.15, 5.6, 830, 115).Penetrates);

            // and it is weaker as a medium, not only at that one thickness: the same
            // round reaches further into it, and leaves faster wherever it gets out at
            // all. Past the point where both stop there is nothing to compare — a wall
            // that holds holds, and 0 is not less than 0.
            var bullet = Bullet(9.5, 7.85, 830);
            Assert.True(ObstacleModel.DepthMm(bullet, Barrier("Brick", 100), Tuning) >
                        ObstacleModel.DepthMm(bullet, Barrier("Concrete", 100), Tuning));

            foreach (var mm in new[] { 60.0, 90.0, 115.0 })
            {
                Assert.True(
                    Into("Brick", 9.5, 7.85, 830, mm).ExitV >
                    IntoConcrete(9.5, 7.85, 830, mm).ExitV,
                    $"brick was not easier than concrete at {mm} mm");
            }
        }

        /// <summary>
        /// And the .50 is in a different class again: through two courses of brick,
        /// stopped by a structural wall.
        /// </summary>
        [Fact]
        public void Fifty_calibre_crosses_masonry_a_rifle_cannot()
        {
            Assert.True(IntoConcrete(46.0, 12.95, 890, 115).Penetrates);
            Assert.True(IntoConcrete(46.0, 12.95, 890, 230).Penetrates);

            Assert.False(IntoConcrete(9.5, 7.85, 830, 230).Penetrates);
            Assert.False(IntoConcrete(46.0, 12.95, 890, 300).Penetrates);
        }

        /// <summary>
        /// A brittle slab is easier than a brittle block: its rear face scabs off ahead
        /// of the projectile, so the perforation limit sits above the penetration depth
        /// rather than at it. Ductile media get no such credit.
        /// </summary>
        [Fact]
        public void A_brittle_slab_perforates_beyond_the_depth_it_stops_at()
        {
            var concrete = Barrier("Concrete", 100);
            Assert.True(concrete.SpallFactor > 1);
            Assert.Equal(100.0, ObstacleModel.ResistingPathMm(130.0, concrete), 1);

            var pine = Barrier("WoodThick", 25);
            Assert.Equal(130.0, ObstacleModel.ResistingPathMm(130.0, pine), 1);

            // the credit is real: a wall between the depth and the depth times the
            // factor is perforated, and one past the factor is not
            var depth = ObstacleModel.DepthMm(Bullet(9.5, 7.85, 830), concrete, Tuning);
            Assert.True(IntoConcrete(9.5, 7.85, 830, depth * 1.15).Penetrates);
            Assert.False(IntoConcrete(9.5, 7.85, 830, depth * 1.45).Penetrates);
        }

        // --- The campaign's new media ---

        /// <summary>
        /// Sand, calibrated on the published behaviour of dry sand fill: a rifle ball
        /// dies at 250-350 mm, a pistol round within about 100 — one sandbag on the
        /// edge for a rifle, two bags proof against everything man-portable.
        /// </summary>
        [Theory]
        // 7.62x51 M80
        [InlineData(9.5, 7.85, 830, 250, 360)]
        // 9x19 ball
        [InlineData(8.0, 9.0, 380, 60, 130)]
        public void Depth_in_sand_matches_the_published_fill_behaviour(double massG,
            double diaMm, double v, double lowMm, double highMm)
        {
            var sand = Barrier("Sand", 0);
            Assert.InRange(ObstacleModel.DepthMm(Bullet(massG, diaMm, v), sand, Tuning),
                lowMm, highMm);
        }

        [Fact]
        public void Two_sandbags_stop_a_rifle_and_one_is_marginal()
        {
            var sand = Barrier("Sand", 0);
            sand.ThicknessMm = 500; // two bags, as the survey measured them
            Assert.False(ObstacleModel.Resolve(Bullet(9.5, 7.85, 830), sand, Tuning, 1)
                .Penetrates);
        }

        /// <summary>
        /// Upholstery: a pistol round dies somewhere inside a couch, a rifle round
        /// crosses the whole 1.4 m of it slowed — which is what couches do on camera.
        /// </summary>
        [Fact]
        public void A_couch_swallows_a_pistol_round_and_not_a_rifle_round()
        {
            var pad = Barrier("Upholstery", 0);
            pad.ThicknessMm = 1400; // the deepest sofa blob the survey measured

            Assert.False(ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), pad, Tuning, 1)
                .Penetrates);

            var rifle = ObstacleModel.Resolve(Bullet(9.5, 7.85, 830), pad, Tuning, 1);
            Assert.True(rifle.Penetrates);
            Assert.True(rifle.ExitV < 700, "a metre and a half of padding is not free");
        }

        /// <summary>
        /// The glass-block wall: a slow, fat pistol round dies in the block, 9x19 and
        /// rifle rounds go through — which is what filmed tests of block walls show.
        /// The block is a shell — ~10 mm of glass per face — so the book wall is
        /// 20 mm, not the 120 the collider measures.
        /// </summary>
        [Fact]
        public void A_glass_block_wall_stops_a_slow_pistol_and_not_a_nine()
        {
            var block = Barrier("GlassBlock", 0);
            Assert.False(ObstacleModel.Resolve(Bullet(14.9, 11.5, 250), block, Tuning, 1)
                .Penetrates);

            var nine = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), block, Tuning, 1);
            Assert.True(nine.Penetrates);
            Assert.True(nine.ExitV < 300, "the block is not a window pane");

            Assert.True(ObstacleModel.Resolve(Bullet(9.5, 7.85, 830), block, Tuning, 1)
                .Penetrates);
        }

        /// <summary>
        /// A log wall does what log walls do on camera: pistol fire dies in it, rifle
        /// fire comes through slowed.
        /// </summary>
        [Fact]
        public void A_timber_wall_stops_a_pistol_and_slows_a_rifle()
        {
            var wall = Barrier("TimberWall", 0);
            Assert.False(ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), wall, Tuning, 1)
                .Penetrates);

            var rifle = ObstacleModel.Resolve(Bullet(9.5, 7.85, 830), wall, Tuning, 1);
            Assert.True(rifle.Penetrates);
            Assert.True(rifle.ExitV < 700, "a quarter metre of pine is not free");
        }

        /// <summary>
        /// A full cable drum is wound copper: a rifle ball dies inside it, and only the
        /// rim of the spool — where the chord is short — lets anything out.
        /// </summary>
        [Fact]
        public void A_cable_drum_swallows_a_rifle_round()
        {
            var cable = Barrier("Cable", 0);
            var depth = ObstacleModel.DepthMm(Bullet(9.5, 7.85, 830), cable, Tuning);
            Assert.InRange(depth, 150, 400);

            cable.ThicknessMm = 1000; // the drum the survey measured
            Assert.False(ObstacleModel.Resolve(Bullet(9.5, 7.85, 830), cable, Tuning, 1)
                .Penetrates);
        }

        /// <summary>
        /// The bank screen sits exactly on the boundary every published BR rating puts
        /// there: handguns are stopped, rifle rounds defeat it.
        /// </summary>
        [Fact]
        public void Armored_glass_stops_handguns_and_not_rifles()
        {
            var screen = Barrier("ArmoredGlass", 0);
            Assert.False(ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), screen, Tuning, 1)
                .Penetrates);
            Assert.False(ObstacleModel.Resolve(Bullet(14.9, 11.5, 250), screen, Tuning, 1)
                .Penetrates);
            Assert.True(ObstacleModel.Resolve(Bullet(4.15, 5.6, 890), screen, Tuning, 1)
                .Penetrates);
        }

        // --- Vehicles and doors ---

        /// <summary>
        /// A car's flank is not a road sign: outer panel, inner panel and the window
        /// mechanism between them, an effective 3 mm of steel. What that has to
        /// reproduce is the published behaviour of shooting cars — a 9x19 gets through
        /// the near door and arrives on the other side of it nearly spent, a slow heavy
        /// pistol round does not get through at all, and rifle ball barely notices.
        /// </summary>
        [Fact]
        public void A_car_flank_is_three_millimetres_of_steel()
        {
            var t = Tuning;
            var flank = Barrier("VehicleChassis", 4);
            Assert.Equal(3.0, flank.ThicknessMm, 3);

            var nine = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), flank, t, 1);
            Assert.True(nine.Penetrates);
            Assert.InRange(nine.V50, 250, 310);
            Assert.InRange(nine.ExitV, 150, 270);

            Assert.False(ObstacleModel.Resolve(Bullet(14.9, 11.5, 250), flank, t, 1)
                .Penetrates);

            var ball = ObstacleModel.Resolve(Bullet(4.15, 5.6, 880), flank, t, 1);
            Assert.True(ball.Penetrates);
            Assert.InRange(ball.ExitV, 650, 800);
        }

        /// <summary>
        /// And a car is two of them. The second flank is not a separate rule — a
        /// vehicle body is a shell whose collider spans the whole car, so the far face
        /// is charged its own wall by the shell rule (`ShellCavityMm`) exactly as a
        /// barrel's is. Here that is the crossing spelled out: the round that came
        /// through the near door meets the far one at what the near one left it.
        /// </summary>
        [Fact]
        public void A_pistol_round_dies_crossing_a_car_and_a_rifle_round_does_not()
        {
            var t = Tuning;
            var flank = Barrier("VehicleChassis", 4);

            var nine = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), flank, t, 1);
            Assert.True(nine.Penetrates);
            Assert.False(ObstacleModel.Resolve(Continuing(Bullet(8.0, 9.0, 380), nine),
                flank, t, 1).Penetrates);

            var ball = ObstacleModel.Resolve(Bullet(4.15, 5.6, 880), flank, t, 1);
            var far = ObstacleModel.Resolve(Continuing(Bullet(4.15, 5.6, 880), ball),
                flank, t, 1);
            Assert.True(far.Penetrates);
            Assert.InRange(far.ExitV, 450, 700);
        }

        /// <summary>What came out of one barrier, as it arrives at the next.</summary>
        private static ObstacleModel.Projectile Continuing(ObstacleModel.Projectile p,
            ObstacleModel.Outcome o)
        {
            p.MassG = o.ExitMassG;
            p.DiaMm = o.ExitDiaMm;
            p.V = o.ExitV;
            p.X = o.ExitX;
            return p;
        }

        /// <summary>
        /// A HOLLOW door leaf is two skins over a frame, and its collider is far too
        /// shallow for the shell rule to notice the cavity — so the scene's `DOORS`
        /// node plus the material's own word (DoorLeaf: skins — only sheet that cannot
        /// carry itself laminates) charge the entry face both sheets. The same sheet
        /// with nothing overhead is charged one, which is the case that must not move.
        /// </summary>
        [Fact]
        public void A_door_leaf_pays_for_two_sheets_and_a_bare_sheet_for_one()
        {
            var t = Tuning;
            var sheet = Barrier("MetalThin", 4);
            var leaf = sheet;
            leaf.Walls = 2;

            var bullet = Bullet(8.0, 9.0, 380);
            var through = ObstacleModel.Resolve(bullet, sheet, t, 1);
            var door = ObstacleModel.Resolve(bullet, leaf, t, 1);

            Assert.True(through.Penetrates);
            Assert.True(door.Penetrates);
            Assert.True(door.V50 > through.V50);
            Assert.True(door.ExitV < through.ExitV);

            // it is exactly a sheet of twice the thickness, and nothing else
            var twice = sheet;
            twice.ThicknessMm = sheet.ThicknessMm * 2;
            Assert.Equal(ObstacleModel.Resolve(bullet, twice, t, 1).ExitV, door.ExitV, 6);

            // and it changes verdicts where it should: a round that dribbles through
            // one skin does not cross two
            var slow = Bullet(8.0, 9.0, 200);
            Assert.True(ObstacleModel.Resolve(slow, sheet, t, 1).Penetrates);
            Assert.False(ObstacleModel.Resolve(slow, leaf, t, 1).Penetrates);
        }

        /// <summary>
        /// A barrier built without a wall count behaves exactly as it always did —
        /// the field reads 0 for everything the resolution does not touch.
        /// </summary>
        [Fact]
        public void No_wall_count_means_one_wall()
        {
            var sheet = Barrier("MetalThin", 4);
            Assert.Equal(sheet.ThicknessMm, ObstacleModel.WallMm(sheet), 6);

            sheet.Walls = 1;
            Assert.Equal(sheet.ThicknessMm, ObstacleModel.WallMm(sheet), 6);
        }

        // --- Geometry and sanity ---

        [Fact]
        public void An_oblique_hit_presents_more_material()
        {
            var t = Tuning;
            Assert.Equal(10, ObstacleModel.PathMm(10, 1, t), 4);
            Assert.Equal(20, ObstacleModel.PathMm(10, 0.5, t), 4);       // 60° from normal
            Assert.Equal(50, ObstacleModel.PathMm(10, 0.05, t), 4);      // clamped at AngleMinCos
        }

        [Fact]
        public void An_oblique_hit_is_harder_to_get_through()
        {
            var t = Tuning;
            var door = Barrier("WoodThick", 10);
            var bullet = Bullet(8.0, 9.0, 380);

            var square = ObstacleModel.Resolve(bullet, door, t, 1);
            var slanted = ObstacleModel.Resolve(bullet, door, t, 0.5);

            Assert.True(slanted.ExitV < square.ExitV);
        }

        [Theory]
        [InlineData("WoodThick", 10f)]
        [InlineData("MetalThick", 18f)]
        [InlineData("Glass", 0f)]
        public void Exit_velocity_rises_with_impact_velocity_and_never_exceeds_it(
            string material, float level)
        {
            var t = Tuning;
            var b = Barrier(material, level);
            var last = -1.0;

            for (var v = 100; v <= 1000; v += 50)
            {
                var outcome = ObstacleModel.Resolve(Bullet(8.0, 9.0, v), b, t, 1);
                Assert.True(outcome.ExitV <= v, $"energy created at {v} m/s");
                Assert.True(outcome.ExitV >= last, $"exit velocity fell at {v} m/s");
                last = outcome.ExitV;
            }
        }

        [Theory]
        [InlineData("WoodThick")]
        [InlineData("MetalThick")]
        public void Exit_velocity_falls_as_the_barrier_thickens(string material)
        {
            var t = Tuning;
            var last = double.MaxValue;

            for (var level = 1; level <= 69; level += 2)
            {
                var outcome = ObstacleModel.Resolve(Bullet(9.5, 7.85, 800),
                    Barrier(material, level), t, 1);
                Assert.True(outcome.ExitV <= last, $"exit velocity rose at level {level}");
                last = outcome.ExitV;
            }
        }

        [Fact]
        public void A_projectile_with_no_state_does_not_get_through_anything()
        {
            var t = Tuning;
            var door = Barrier("WoodThick", 10);

            Assert.False(ObstacleModel.Resolve(Bullet(0, 9, 380), door, t, 1).Penetrates);
            Assert.False(ObstacleModel.Resolve(Bullet(8, 0, 380), door, t, 1).Penetrates);
            Assert.False(ObstacleModel.Resolve(Bullet(8, 9, 0), door, t, 1).Penetrates);
        }
    }
}
