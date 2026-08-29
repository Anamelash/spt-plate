using PLATE.Client.Ballistics;
using PLATE.Client.Patches;
using PLATE.Server.Services;
using UnityEngine;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// What a resolved collision hands the projectile the engine is about to build.
    ///
    /// It has to be handed over as ARGUMENTS to the spawn, because the engine forms the
    /// projectile's whole trajectory table there and overwrites its velocity out of that
    /// table on every tick afterwards. Everything the mod used to write into a freshly
    /// spawned child was therefore discarded on the child's first tick — invisible at
    /// contact range, where an impact inside that first tick interpolates most of it
    /// back, and worth a bullet arriving through a door at nearly its muzzle speed at
    /// thirty metres.
    ///
    /// The decision is pure and is tested as such, guard by guard.
    /// </summary>
    public class ObstacleExitStateTests
    {
        private static ObstacleModel.Outcome Exit(double exitV, double deviation,
            BallisticLimit.CoreFate fate = BallisticLimit.CoreFate.Rigid)
        {
            return new ObstacleModel.Outcome
            {
                Penetrates = true,
                ExitV = exitV,
                Deviation = deviation,
                Fate = fate,
            };
        }

        [Fact]
        public void A_collision_nothing_claimed_launches_nothing()
        {
            var launch = ObstacleModel.Launch(pierced: false, Exit(300, 0.1), bounced: false,
                retention: 0.7, parentSpeedMs: 400);

            Assert.Equal(ObstacleModel.LaunchSource.None, launch.Source);
        }

        /// <summary>What went through leaves at the speed the barrier left it, and that
        /// is the whole content of the fix.</summary>
        [Fact]
        public void A_pass_through_launches_at_the_exit_speed()
        {
            var launch = ObstacleModel.Launch(pierced: true, Exit(312, 0.15), bounced: false,
                retention: 0.7, parentSpeedMs: 940);

            Assert.Equal(ObstacleModel.LaunchSource.Penetration, launch.Source);
            Assert.Equal(312, launch.SpeedMs, 6);
            Assert.True(launch.RebuildDirection);
        }

        /// <summary>
        /// A barrier with no deflection of its own — wire mesh, grass — keeps vanilla's
        /// direction. "No model of ours" must not silently become "no deflection at all".
        /// </summary>
        [Fact]
        public void No_deflection_of_ours_leaves_the_direction_to_vanilla()
        {
            var launch = ObstacleModel.Launch(pierced: true, Exit(930, 0), bounced: false,
                retention: 0.7, parentSpeedMs: 940);

            Assert.Equal(ObstacleModel.LaunchSource.Penetration, launch.Source);
            Assert.False(launch.RebuildDirection);
            Assert.Equal(930, launch.SpeedMs, 6);
        }

        /// <summary>
        /// Guard one. A shattered core is built by calling the same spawn N times with
        /// the same parent, and handing every fragment the full exit speed would create
        /// energy out of nothing — the model's own arithmetic already split that energy
        /// between them.
        /// </summary>
        [Fact]
        public void A_shattered_core_launches_nothing()
        {
            var launch = ObstacleModel.Launch(pierced: true,
                Exit(400, 0.2, BallisticLimit.CoreFate.Shattered), bounced: false,
                retention: 0.7, parentSpeedMs: 900);

            Assert.Equal(ObstacleModel.LaunchSource.None, launch.Source);
        }

        /// <summary>
        /// And a shattered verdict does not fall through to the bounce slot either: the
        /// collision went through, so a "this bounced" stamp left on it is stale by
        /// definition.
        /// </summary>
        [Fact]
        public void A_shattered_core_does_not_fall_through_to_a_stale_bounce()
        {
            var launch = ObstacleModel.Launch(pierced: true,
                Exit(400, 0.2, BallisticLimit.CoreFate.Shattered), bounced: true,
                retention: 0.7, parentSpeedMs: 900);

            Assert.Equal(ObstacleModel.LaunchSource.None, launch.Source);
        }

        /// <summary>
        /// Guard two. Vanilla asks the ricochet gate FIRST and may then overrule it — a
        /// projectile that has already bounced twice is not allowed a third — so a
        /// collision can carry a bounce stamp and then go through. The penetration
        /// verdict is the later and truer one and wins.
        /// </summary>
        [Fact]
        public void Penetration_outranks_a_bounce_vanilla_declined()
        {
            var launch = ObstacleModel.Launch(pierced: true, Exit(280, 0.1), bounced: true,
                retention: 0.7, parentSpeedMs: 400);

            Assert.Equal(ObstacleModel.LaunchSource.Penetration, launch.Source);
            Assert.Equal(280, launch.SpeedMs, 6);
        }

        /// <summary>A bounce that nothing overruled keeps the surface's own retention,
        /// and always rebuilds the direction — where it went is the whole content of a
        /// ricochet.</summary>
        [Fact]
        public void A_bounce_launches_at_the_surfaces_retention()
        {
            var launch = ObstacleModel.Launch(pierced: false, Exit(0, 0), bounced: true,
                retention: 0.55, parentSpeedMs: 400);

            Assert.Equal(ObstacleModel.LaunchSource.Ricochet, launch.Source);
            Assert.Equal(220, launch.SpeedMs, 6);
            Assert.True(launch.RebuildDirection);
        }

        /// <summary>A retention nobody set cannot turn into a negative speed.</summary>
        [Fact]
        public void A_bounce_never_launches_backwards()
        {
            var launch = ObstacleModel.Launch(pierced: false, Exit(0, 0), bounced: true,
                retention: -1, parentSpeedMs: 400);

            Assert.Equal(0, launch.SpeedMs, 6);
        }

        // --- Guard three: the origin moves with the direction ---

        /// <summary>
        /// Vanilla places an overpenetration child two millimetres past the hit point
        /// along ITS OWN direction. Rewriting the direction and leaving the origin would
        /// start the child sideways of the hole it came out of; the offset is re-laid
        /// along the new direction instead, and neither the offset nor its owner appears
        /// in the code that does it.
        /// </summary>
        [Fact]
        public void The_spawn_offset_is_re_laid_along_the_new_direction()
        {
            var hit = new Vector3(1, 2, 3);
            var vanillaOrigin = hit + Vector3.right * 0.002f;

            var moved = ShotLifecyclePatches.OriginFor(hit, vanillaOrigin, Vector3.up);

            Assert.Equal(0.002f, (moved - hit).magnitude, 5);
            Assert.Equal(hit.y + 0.002f, moved.y, 5);
            Assert.Equal(hit.x, moved.x, 5);
        }

        /// <summary>
        /// A ricochet child begins exactly ON the hit point, so there is no offset to
        /// re-lay and turning it must not move it off the surface.
        /// </summary>
        [Fact]
        public void A_spawn_on_the_hit_point_stays_on_the_hit_point()
        {
            var hit = new Vector3(-4, 0.5f, 12);

            var moved = ShotLifecyclePatches.OriginFor(hit, hit, new Vector3(0.3f, 0.9f, 0f));

            Assert.Equal(0f, (moved - hit).magnitude, 5);
        }

        /// <summary>The direction handed in need not be unit length — it is a trajectory,
        /// and the offset is a distance.</summary>
        [Fact]
        public void The_offset_is_a_distance_and_not_a_scaling()
        {
            var hit = Vector3.zero;
            var vanillaOrigin = new Vector3(0.002f, 0, 0);

            var moved = ShotLifecyclePatches.OriginFor(hit, vanillaOrigin,
                new Vector3(0, 17f, 0));

            Assert.Equal(0.002f, moved.magnitude, 5);
        }
    }
}
