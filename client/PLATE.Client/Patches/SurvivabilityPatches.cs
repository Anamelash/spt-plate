using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// The survivability overrides (config section 7). Everything here deliberately
    /// contradicts the model: it exists because a player may want to be harder to kill
    /// than the physics says, and that is a choice the mod should let them make in the
    /// open rather than by editing wound constants until the numbers feel nice.
    ///
    /// Both hooks sit on ActiveHealthController.ChangeHealth, which is where every HP
    /// change of a body part ends up. Inside vanilla ApplyDamage it is called twice for
    /// two different jobs: once with the damage for the part that was hit, and then once
    /// per surviving part when the hit destroyed that part and the excess spills over the
    /// rest of the body. Both are negative deltas, and telling them apart is just a
    /// matter of which part is being changed while we are inside whose ApplyDamage.
    ///
    /// Nothing here touches death from blood loss: that drains volume and calls Kill
    /// directly, never passing through HP, and it has its own per-category switches in
    /// section 3.
    /// </summary>
    internal static class SurvivabilityPatches
    {
        /// <summary>Whatever is left when the floor is in effect. One HP is a blacked-out
        /// part that has not blacked out — the smallest value the game still reads as alive.</summary>
        private const float AliveFloorHp = 1f;

        public static void Apply(Harmony harmony)
        {
            PatchSafe(harmony, PatchTargets.Health_ApplyDamage,
                prefix: nameof(ApplyDamagePrefix), finalizer: nameof(ApplyDamageFinalizer));
            PatchSafe(harmony, PatchTargets.Health_ChangeHealth,
                prefix: nameof(ChangeHealthPrefix), finalizer: null);
        }

        // --- The limb-spill window (option: "Limb hits can kill", everyone) ---

        /// <summary>
        /// The limb whose ApplyDamage we are currently inside, when the spill from it is
        /// to be dropped. Null the rest of the time. Saved and restored around every
        /// ApplyDamage rather than simply cleared, because ApplyDamage re-enters itself:
        /// the organ model's central wound applies its own Chest damage from a postfix,
        /// and that inner call must not be judged by the outer call's window.
        /// </summary>
        private static EBodyPart? _spillFrom;

        private static bool IsLimb(EBodyPart part)
        {
            return part == EBodyPart.LeftArm || part == EBodyPart.RightArm ||
                   part == EBodyPart.LeftLeg || part == EBodyPart.RightLeg;
        }

        private static void ApplyDamagePrefix(EBodyPart bodyPart, out EBodyPart? __state)
        {
            __state = _spillFrom;
            _spillFrom = !PlateClientConfig.LimbHitsCanKill.Value && IsLimb(bodyPart)
                ? bodyPart
                : (EBodyPart?)null;
        }

        // a finalizer rather than a postfix: the window has to close even if the original
        // throws, otherwise one exception silently makes the whole body immune
        private static void ApplyDamageFinalizer(EBodyPart? __state)
        {
            _spillFrom = __state;
        }

        // --- The one hook both options act through ---

        private static bool ChangeHealthPrefix(ActiveHealthController __instance,
            EBodyPart bodyPart, ref float value, DamageInfo damageInfo)
        {
            try
            {
                // A limb is being damaged with the spill switched off, and this change is
                // for some other part: that is the excess crossing over from the limb, and
                // it is exactly what "limbs are not a route to death" has to stop. The
                // limb's own damage lands normally — it still blacks out, still bleeds,
                // still cripples.
                if (_spillFrom.HasValue && bodyPart != _spillFrom.Value)
                {
                    return false;
                }

                return !FloorAtOneHp(__instance, bodyPart, ref value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PLATE] Survivability: ChangeHealthPrefix failed: {ex}");
                return true; // never let an override swallow the game's own damage
            }
        }

        /// <summary>
        /// Head and thorax keep at least one HP for the local player. Trimming the delta
        /// rather than refusing it keeps everything downstream honest: the part still
        /// takes what it can, the hit is still a hit, and the vanilla destroy/kill path
        /// simply never sees a part at minimum. Returns true when there was nothing left
        /// to take and the change should be skipped outright.
        /// </summary>
        private static bool FloorAtOneHp(ActiveHealthController ahc, EBodyPart bodyPart,
            ref float value)
        {
            if (value >= 0f || !PlateClientConfig.PreventPlayerDeath.Value ||
                (bodyPart != EBodyPart.Head && bodyPart != EBodyPart.Chest))
            {
                return false;
            }

            var player = ahc?.Player;
            if (player == null || !player.IsYourPlayer)
            {
                return false;
            }

            var current = ahc.GetBodyPartHealth(bodyPart).Current;
            var takeable = Mathf.Max(0f, current - AliveFloorHp);
            if (-value <= takeable)
            {
                return false; // the hit fits above the floor, nothing to do
            }

            var asked = -value;
            value = -takeable;
            Overlay.HitFeed.PushHit(player,
                $"  DEATH PREVENTED: {bodyPart} {asked:0.#} -> {takeable:0.#} " +
                $"({current:0.#} HP, floor {AliveFloorHp:0.#})");

            return takeable <= 0f;
        }

        private static void PatchSafe(Harmony harmony, MethodBase target,
            string prefix, string finalizer)
        {
            if (target == null)
            {
                PatchStats.MarkFailed(null, prefix, "target not resolved");
                Plugin.Log.LogError(
                    $"[PLATE] Survivability: target for {prefix} not resolved, skipped");
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(SurvivabilityPatches), prefix),
                    finalizer: finalizer == null
                        ? null
                        : new HarmonyMethod(typeof(SurvivabilityPatches), finalizer));
                PatchStats.Track(harmony, target, prefix);
            }
            catch (Exception ex)
            {
                PatchStats.MarkFailed(target, prefix, ex.Message);
                Plugin.Log.LogError(
                    $"[PLATE] Survivability: failed to patch {target.Name}: {ex}");
            }
        }
    }
}
