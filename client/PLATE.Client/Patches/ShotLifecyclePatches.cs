using System;
using EFT.Ballistics;
using HarmonyLib;
using PLATE.Client.Ballistics;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// The two things that have to happen at the moment a projectile is born, and can
    /// happen nowhere else.
    ///
    /// <b>The trajectory table is built at birth.</b> `Shot.Create` forms the whole
    /// predicted trajectory from the `direction` and `speed` it is handed, and every
    /// tick afterwards overwrites the projectile's position and velocity out of that
    /// table. So everything the mod used to write into a freshly spawned child — the
    /// speed a wall left it with, the direction the wall bent it to — was discarded on
    /// the child's first tick. Indoors this went unnoticed, because a second impact
    /// inside the first tick is interpolated back towards the written value; at range it
    /// meant a bullet arrived at a body through a door with very nearly its muzzle
    /// speed. Vanilla's own deviation and ricochet work for exactly the reason ours did
    /// not: they are ARGUMENTS to Create, computed before the call. So the exit state
    /// goes in here, as an argument, and the postfixes that used to write velocity no
    /// longer do. That is true of both halves of the mod that spawn projectiles: what a
    /// wall leaves a bullet with, and what a BODY leaves it with — the child of an
    /// overpenetration and the fragments a bullet breaks into inside a person had the
    /// same defect for the same reason.
    ///
    /// <b>Shots come out of a pool.</b> Two hundred objects are recycled for the whole
    /// raid, and `PrepareToPool` clears the engine's own fields and knows nothing of
    /// ours. Anything the mod keys on the shot OBJECT — the deformable fraction and yaw
    /// in <see cref="ProjectileState"/>, the per-shot draw and the organ memory in
    /// <see cref="ShotSpread"/> — is therefore inherited by an unrelated bullet fired
    /// later. That is a wound-model defect as much as a barrier one, so this hook is
    /// applied whatever the modules are set to, the way SurvivabilityPatches is.
    ///
    /// Deliberately its own file rather than a third hook inside ObstaclePatches: the
    /// clearing belongs to no module, and the target is the one place both needs meet.
    /// </summary>
    internal static class ShotLifecyclePatches
    {
        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.Bullet_Create;
            if (target == null)
            {
                PatchStats.MarkFailed(null, Label(nameof(ExitStatePrefix)), "target not resolved");
                PatchStats.MarkFailed(null, Label(nameof(ForgetPooledStatePostfix)),
                    "target not resolved");
                Plugin.Log.LogError("[PLATE] Shot lifecycle: Shot.Create not resolved, skipped");
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(ShotLifecyclePatches),
                        nameof(ExitStatePrefix)),
                    postfix: new HarmonyMethod(typeof(ShotLifecyclePatches),
                        nameof(ForgetPooledStatePostfix)));
                PatchStats.Track(harmony, target, Label(nameof(ExitStatePrefix)));
                PatchStats.Track(harmony, target, Label(nameof(ForgetPooledStatePostfix)));
            }
            catch (Exception ex)
            {
                PatchStats.MarkFailed(target, Label(nameof(ExitStatePrefix)), ex.Message);
                PatchStats.MarkFailed(target, Label(nameof(ForgetPooledStatePostfix)), ex.Message);
                Plugin.Log.LogError($"[PLATE] Shot lifecycle: failed to patch Create: {ex.Message}");
            }
        }

        /// <summary>Telemetry label, prefixed for the same reason ObstaclePatches
        /// prefixes its own: PatchStats keys its rows by label and two hooks on one
        /// target must stay two rows.</summary>
        private static string Label(string name)
        {
            return "shot:" + name;
        }

        /// <summary>
        /// The exit state of the collision that is spawning this projectile, written
        /// into the arguments the trajectory table is about to be built from.
        ///
        /// `parent` is what identifies the collision: whoever resolved it did so a moment
        /// ago inside the same HandleCollision and left its verdict behind, so asking the
        /// two sources whether they claim this parent is the whole of the matching. A
        /// muzzle shot and a grenade's fictional projectile have no parent and are left
        /// alone.
        ///
        /// <b>Two sources, asked in a fixed order.</b> The environment first, the body
        /// second. They cannot both answer — a collider either is a BodyPartCollider or
        /// it is not, and the two modules gate on exactly that — but the order is written
        /// down rather than left to chance because the environment source CONSUMES its
        /// verdict when it answers (one stamp, one child), and a body collision must
        /// never be the thing that eats a wall's.
        ///
        /// The origin moves with the direction. Vanilla places an overpenetration child
        /// two millimetres beyond the hit point ALONG ITS OWN direction and a ricochet
        /// child exactly on it; rebuilding the direction without the origin would launch
        /// the child from a point sideways of the hole it came out of. The same offset is
        /// re-laid along the new direction, which reproduces both cases without either
        /// number appearing here. Only the environment rebuilds a direction: a body bends
        /// nothing that vanilla's own scatter does not already bend.
        ///
        /// Mass and calibre are arguments too, and only the body side has anything to say
        /// about them — a fragment is a smaller projectile than the bullet it broke off,
        /// and handing that over here rather than writing it afterwards also gives
        /// vanilla's own drag the fragment's sectional density instead of the parent's.
        /// </summary>
        private static void ExitStatePrefix(ref Vector3 origin, ref Vector3 direction,
            ref float speed, ref float bulletMassGram, ref float bulletDiameterMilimeters,
            Shot parent)
        {
            PatchStats.Hit(Label(nameof(ExitStatePrefix)));
            if (parent == null)
            {
                return; // the first shot of a chain: nothing resolved anything for it
            }

            try
            {
                if (ObstaclePatches.TryChildLaunch(parent, direction, out var exitSpeed,
                        out var exitDirection, out var rebuildDirection))
                {
                    speed = exitSpeed;

                    if (rebuildDirection && exitDirection.sqrMagnitude > 1e-8f)
                    {
                        origin = OriginFor(parent.HitPoint, origin, exitDirection);
                        direction = exitDirection;
                    }

                    return;
                }

                if (BallisticsPatches.TryChildLaunch(parent, out var bodySpeed,
                        out var massG, out var diaMm))
                {
                    speed = bodySpeed;

                    // zero means "the same projectile carries on" — an overpenetrating
                    // bullet is still that bullet, and vanilla already passes its figures
                    if (massG > 0f && diaMm > 0f)
                    {
                        bulletMassGram = massG;
                        bulletDiameterMilimeters = diaMm;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(ExitStatePrefix), ex);
            }
        }

        /// <summary>
        /// Where a child starts once its direction has been rewritten.
        ///
        /// Vanilla lays the spawn point out along ITS OWN direction: an overpenetration
        /// child begins two millimetres past the hit point along the deviated trajectory,
        /// a ricochet child begins exactly on the hit point. Neither number is quoted
        /// here — the offset is read back out of the origin the caller was going to use
        /// and re-laid along the new direction — so both cases come out right and a
        /// future change to either stays vanilla's business.
        ///
        /// Rewriting the direction without this would launch the child from a point
        /// sideways of the hole it came out of, which for a shallow deflection is
        /// millimetres and for a bounce off a wall is the wrong side of the surface.
        /// </summary>
        internal static Vector3 OriginFor(Vector3 hitPoint, Vector3 vanillaOrigin,
            Vector3 newDirection)
        {
            var offset = (vanillaOrigin - hitPoint).magnitude;
            return hitPoint + newDirection.normalized * offset;
        }

        /// <summary>
        /// Everything this mod recorded against the object the pool just handed out
        /// belonged to a different bullet. Postfix rather than prefix because the object
        /// does not exist until Create has taken it off the stack — and it runs before
        /// anything writes to the new projectile, because the writers
        /// (<see cref="ShotSpread.Inherit"/>, the barrier's own X and yaw) all sit in
        /// postfixes of the spawners that CALL Create.
        /// </summary>
        private static void ForgetPooledStatePostfix(Shot __result)
        {
            PatchStats.Hit(Label(nameof(ForgetPooledStatePostfix)));
            try
            {
                ProjectileState.Forget(__result);
                ShotSpread.Forget(__result);
            }
            catch (Exception ex)
            {
                LogError(nameof(ForgetPooledStatePostfix), ex);
            }
        }

        private static float _lastErrorLogged;

        private static void LogError(string where, Exception ex)
        {
            if (Time.unscaledTime - _lastErrorLogged < 5f)
            {
                return;
            }

            _lastErrorLogged = Time.unscaledTime;
            Plugin.Log.LogError($"[PLATE] Shot lifecycle {where}: {ex}");
        }
    }
}
