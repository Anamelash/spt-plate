using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The bounce. Vanilla has one window for every surface in the game — between 42.5°
    /// and 80° from the normal, then a coin flip weighted by two per-material chances —
    /// which says the same thing about concrete, water and a sheet of tin. Here it is a
    /// critical grazing angle per surface, falling with speed, with a band around it for
    /// the roughness that is honestly a die roll.
    /// </summary>
    public class ObstacleRicochetTests
    {
        private static readonly ObstacleReference.Book Shipped =
            ObstacleReference.Parse(ObstacleReference.DefaultJsonc);

        private static ObstacleModel.Tuning Tuning => ObstacleReference.TuningOf(Shipped);

        private static double Alpha0(string material)
        {
            Assert.True(ObstacleReference.TryRicochet(Shipped, material, out var a, out _));
            return a;
        }

        [Theory]
        [InlineData(1.0, 90)]    // straight into the face
        [InlineData(0.0, 0)]     // along the surface
        [InlineData(-1.0, 90)]   // the sign of the dot product is not the question
        [InlineData(0.5, 30)]
        public void Graze_angle_is_measured_from_the_surface(double cos, double expected)
        {
            Assert.Equal(expected, ObstacleModel.GrazeAngleDeg(cos), 3);
        }

        [Fact]
        public void The_critical_angle_is_the_tabulated_one_at_the_reference_speed()
        {
            var t = Tuning;
            Assert.Equal(17, ObstacleModel.CriticalAngleDeg(17, t.RicochetVelocityRef, t), 3);
        }

        [Fact]
        public void The_critical_angle_falls_as_the_projectile_speeds_up()
        {
            var t = Tuning;
            var last = double.MaxValue;

            for (var v = 100; v <= 1200; v += 100)
            {
                var a = ObstacleModel.CriticalAngleDeg(17, v, t);
                Assert.True(a < last, $"critical angle rose at {v} m/s");
                Assert.InRange(a, 0, 89);
                last = a;
            }
        }

        /// <summary>
        /// Concrete at handgun speed sits in the mid-teens of degrees, and a rifle round
        /// arriving twice as fast has to come in flatter still before it bounces.
        /// </summary>
        [Fact]
        public void Concrete_sits_where_the_forensic_literature_puts_it()
        {
            var t = Tuning;
            var alpha0 = Alpha0("Concrete");

            Assert.InRange(ObstacleModel.CriticalAngleDeg(alpha0, 380, t), 14, 20);
            Assert.InRange(ObstacleModel.CriticalAngleDeg(alpha0, 800, t), 10, 15);
        }

        [Fact]
        public void Water_bounces_almost_anything_that_is_not_nearly_flat()
        {
            var t = Tuning;
            Assert.InRange(ObstacleModel.CriticalAngleDeg(Alpha0("Water"), 400, t), 6, 8);
        }

        // --- The band ---

        [Fact]
        public void Below_the_band_it_always_bounces_and_above_it_never_does()
        {
            var t = Tuning;
            Assert.Equal(1, ObstacleModel.RicochetChance(5, 17, t), 6);
            Assert.Equal(0, ObstacleModel.RicochetChance(30, 17, t), 6);
        }

        [Fact]
        public void The_critical_angle_itself_is_a_coin_flip()
        {
            Assert.Equal(0.5, ObstacleModel.RicochetChance(17, 17, Tuning), 6);
        }

        [Fact]
        public void Inside_the_band_the_chance_falls_with_the_angle()
        {
            var t = Tuning;
            var last = 2.0;

            for (var a = 12.0; a <= 22.0; a += 0.5)
            {
                var p = ObstacleModel.RicochetChance(a, 17, t);
                Assert.InRange(p, 0, 1);
                Assert.True(p <= last, $"chance rose at {a}°");
                last = p;
            }
        }

        [Fact]
        public void A_surface_with_no_critical_angle_never_bounces_anything()
        {
            Assert.Equal(0, ObstacleModel.RicochetChance(0.1, 0, Tuning), 6);
        }

        // --- What comes off ---

        [Fact]
        public void A_grazing_bounce_keeps_most_of_its_speed_and_a_steep_one_much_less()
        {
            var t = Tuning;
            Assert.Equal(0.80, ObstacleModel.RicochetRetention(0, 17, 0.80, t), 3);
            Assert.Equal(0.40, ObstacleModel.RicochetRetention(17, 17, 0.80, t), 3);
        }

        [Fact]
        public void Retention_is_never_a_gain()
        {
            var t = Tuning;
            for (var a = 0.0; a <= 40; a += 1)
            {
                Assert.InRange(ObstacleModel.RicochetRetention(a, 17, 0.80, t), 0, 1);
            }
        }

        [Fact]
        public void Soil_takes_more_out_of_a_bounce_than_concrete_does()
        {
            Assert.True(ObstacleReference.TryRicochet(Shipped, "Concrete", out _, out var hard));
            Assert.True(ObstacleReference.TryRicochet(Shipped, "Soil", out _, out var soft));
            Assert.True(soft < hard);
        }

        [Fact]
        public void A_ricochet_leaves_flatter_than_it_arrived()
        {
            var t = Tuning;
            for (var a = 1.0; a <= 45; a += 1)
            {
                var outAngle = ObstacleModel.ExitGrazeDeg(a, t);
                Assert.True(outAngle < a, $"a {a}° hit left at {outAngle}°");
                Assert.True(outAngle > 0);
            }
        }

        [Fact]
        public void Flattening_a_flat_hit_changes_almost_nothing()
        {
            // the flattening is a scaling of the normal component, so it vanishes as
            // the trajectory approaches the surface
            Assert.Equal(0, ObstacleModel.ExitGrazeDeg(0, Tuning), 6);
        }

        // --- The sheet gate ---

        private static ObstacleModel.Projectile Bullet(double massG, double diaMm, double v)
        {
            return new ObstacleModel.Projectile
            {
                MassG = massG,
                DiaMm = diaMm,
                V = v,
                X = 0,
                CoreAreaFrac = 1,
                CoreMassFrac = 1,
                HardnessHv = 60,
            };
        }

        /// <summary>
        /// A sheet can only throw off what it could refuse. The same corrugated sheet
        /// punches (and so cannot bounce) a 9 mm at muzzle speed and refuses (and so may
        /// bounce) the same bullet arrived spent — the folk rule "thinner than the
        /// calibre never ricochets" falls out as a special case, but with the speed and
        /// the mass in it. The spent speed sits below the V50 window the model tests pin
        /// for this sheet (120-155 m/s).
        /// </summary>
        [Fact]
        public void A_sheet_only_bounces_what_it_could_refuse()
        {
            var t = Tuning;
            Assert.True(ObstacleReference.TryBarrier(Shipped, "MetalThin", 4, out var tin));

            var fastBullet = Bullet(8.0, 9.0, 380);
            var fast = ObstacleModel.Resolve(fastBullet, tin, t, 1);
            Assert.True(fast.Penetrates);
            Assert.False(ObstacleModel.SheetCanRefuse(fastBullet, tin, fast));

            var spentBullet = Bullet(8.0, 9.0, 100);
            var spent = ObstacleModel.Resolve(spentBullet, tin, t, 1);
            Assert.False(spent.Penetrates);
            Assert.True(ObstacleModel.SheetCanRefuse(spentBullet, tin, spent));
        }

        /// <summary>
        /// The refusal limit is computed along the true line of arrival, with no
        /// obliquity floor: the floor answers an exit question, and refusal is not one.
        /// The same 9 mm the sheet cannot refuse square-on is refused — and so may skip
        /// — at a few degrees of graze, where the slant it would have to displace is
        /// twenty calibres of steel. That is where Haag puts sheet-metal ricochets.
        /// </summary>
        [Fact]
        public void A_tin_roof_refuses_at_a_graze_what_it_cannot_refuse_square_on()
        {
            var t = Tuning;
            Assert.True(ObstacleReference.TryBarrier(Shipped, "MetalThin", 4, out var tin));
            var bullet = Bullet(8.0, 9.0, 380);

            var square = ObstacleModel.Resolve(bullet, tin, t, 1);
            Assert.False(ObstacleModel.SheetCanRefuse(bullet, tin, square));

            // ~3 degrees off the surface: refused, the skip is on the table
            var graze = ObstacleModel.Resolve(bullet, tin, t, 0.05);
            Assert.True(ObstacleModel.SheetCanRefuse(bullet, tin, graze));

            // 30 degrees off the surface: the slant is under two calibres — punched
            var mid = ObstacleModel.Resolve(bullet, tin, t, 0.5);
            Assert.False(ObstacleModel.SheetCanRefuse(bullet, tin, mid));
        }

        /// <summary>
        /// The refusal gate now covers bulk media too, and this is the case that paid
        /// for it: a table top is 20 mm of pine, a standing shooter meets it at 10-16
        /// degrees of graze, and a P90 crosses that whole slant with speed to spare —
        /// so the table must not mirror it. A log stops the same round and keeps its
        /// bounce; so does the table against a round that arrives spent.
        /// </summary>
        [Fact]
        public void A_table_cannot_mirror_what_it_cannot_stop()
        {
            var t = Tuning;
            Assert.True(ObstacleReference.TryBarrier(Shipped, "WoodThin", 3, out var board));

            // 15 degrees of graze = cos 0.26 from the normal
            var p90 = Bullet(2.0, 5.7, 716);
            var across = ObstacleModel.Resolve(p90, board, t, 0.26);
            Assert.True(across.Penetrates);
            Assert.False(ObstacleModel.SheetCanRefuse(p90, board, across));

            // the same board still skips what it can stop
            var spent = Bullet(2.0, 5.7, 120);
            var slow = ObstacleModel.Resolve(spent, board, t, 0.26);
            Assert.False(slow.Penetrates);
            Assert.True(ObstacleModel.SheetCanRefuse(spent, board, slow));

            // and the timber the round dies in keeps its bounce
            Assert.True(ObstacleReference.TryBarrier(Shipped, "WoodThick", 25, out var log));
            var intoLog = ObstacleModel.Resolve(p90, log, t, 0.26);
            Assert.False(intoLog.Penetrates);
            Assert.True(ObstacleModel.SheetCanRefuse(p90, log, intoLog));
        }

        /// <summary>
        /// Wood is not soil: fibres cut where grains yield, and the forensic tables
        /// separate them by ten degrees. One shared class was why tables mirrored P90
        /// fire — a standing shooter's graze sits under soil's threshold and over
        /// wood's.
        /// </summary>
        [Fact]
        public void Wood_bounces_at_shallower_angles_than_soil()
        {
            var wood = Alpha0("WoodThin");
            var soil = Alpha0("Soil");
            Assert.True(wood < soil,
                $"wood alpha0 {wood} must sit below soil's {soil}");
            Assert.Equal(Alpha0("WoodThick"), wood, 3);

            // at P90 speed the wood threshold drops below a standing shooter's graze
            var critAtP90 = ObstacleModel.CriticalAngleDeg(wood, 716, Tuning);
            Assert.True(critAtP90 < 13, $"crit {critAtP90:0.0} deg");
        }

        /// <summary>The refusal limit and the verdict describe the same sheet: the
        /// former is the latter with the floor removed, so it can never sit below.</summary>
        [Fact]
        public void The_refusal_limit_never_undercuts_the_verdict()
        {
            var t = Tuning;
            Assert.True(ObstacleReference.TryBarrier(Shipped, "MetalThick", 18, out var plate));
            var bullet = Bullet(8.0, 9.0, 380);

            foreach (var cos in new[] { 1.0, 0.7, 0.5, 0.34, 0.2, 0.05 })
            {
                var o = ObstacleModel.Resolve(bullet, plate, t, cos);
                Assert.True(o.RefusalV50 >= o.V50 * 0.999,
                    $"refusal {o.RefusalV50:0} under verdict {o.V50:0} at cos {cos}");
            }
        }

        /// <summary>
        /// The gate's other half: media that stop the round keep their bounces, and
        /// zero-cost surfaces are not gated at all — the refusal concept does not
        /// apply to something that never resists, and mud's (and water's) bounce
        /// lives on its own ricochet class and must not die here.
        /// </summary>
        [Fact]
        public void What_stops_the_round_keeps_its_bounce_and_freebies_are_ungated()
        {
            var t = Tuning;

            Assert.True(ObstacleReference.TryBarrier(Shipped, "Concrete", 100, out var wall));
            var pistol = Bullet(8.0, 9.0, 380);
            var held = ObstacleModel.Resolve(pistol, wall, t, 1);
            Assert.False(held.Penetrates);
            Assert.True(ObstacleModel.SheetCanRefuse(pistol, wall, held));

            Assert.True(ObstacleReference.TryBarrier(Shipped, "Mud", 0, out var mud));
            var rifle = Bullet(7.9, 7.92, 715);
            var splash = ObstacleModel.Resolve(rifle, mud, t, 1);
            Assert.True(ObstacleModel.SheetCanRefuse(rifle, mud, splash));
        }
    }
}
