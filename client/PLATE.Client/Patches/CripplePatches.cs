using System;
using EFT;
using HarmonyLib;
using PLATE.Client.Blood;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Sprint ban while a body part is destroyed. Two hooks: the CanSprint getter
    /// (the gate for the player and AI) and EnableSprint (a safeguard against direct enabling).
    /// </summary>
    internal static class CripplePatches
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    AccessTools.PropertyGetter(typeof(MovementContext), nameof(MovementContext.CanSprint)),
                    postfix: new HarmonyMethod(typeof(CripplePatches), nameof(CanSprintPostfix)));
                harmony.Patch(
                    AccessTools.Method(typeof(MovementContext), nameof(MovementContext.EnableSprint)),
                    prefix: new HarmonyMethod(typeof(CripplePatches), nameof(EnableSprintPrefix)));
                harmony.Patch(
                    AccessTools.PropertyGetter(typeof(MovementContext), nameof(MovementContext.CanJump)),
                    postfix: new HarmonyMethod(typeof(CripplePatches), nameof(CanJumpPostfix)));
                harmony.Patch(
                    AccessTools.Method(typeof(MovementContext), nameof(MovementContext.TryJump)),
                    prefix: new HarmonyMethod(typeof(CripplePatches), nameof(TryJumpPrefix)));

                // a bot on a broken leg stays down; the throw is the one thing it is
                // allowed to stand for (see GetUpPrefix)
                harmony.Patch(
                    AccessTools.Method(typeof(BotLay), nameof(BotLay.GetUp)),
                    prefix: new HarmonyMethod(typeof(CripplePatches), nameof(GetUpPrefix)));
                harmony.Patch(
                    AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.DoThrow)),
                    prefix: new HarmonyMethod(typeof(CripplePatches), nameof(ThrowPrefix)),
                    finalizer: new HarmonyMethod(typeof(CripplePatches), nameof(ThrowFinalizer)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PLATE] Cripple: patch failed: {ex.Message}");
            }
        }

        private static void CanSprintPostfix(MovementContext __instance, ref bool __result)
        {
            if (__result && CrippleSystem.SprintBanned.Contains(__instance))
            {
                __result = false;
            }
        }

        private static void EnableSprintPrefix(MovementContext __instance, ref bool enable)
        {
            if (enable && CrippleSystem.SprintBanned.Contains(__instance))
            {
                enable = false;
            }
        }

        private static void CanJumpPostfix(MovementContext __instance, ref bool __result)
        {
            if (__result && CrippleSystem.JumpBanned.Contains(__instance))
            {
                __result = false;
            }
        }

        private static bool TryJumpPrefix(MovementContext __instance)
        {
            return !CrippleSystem.JumpBanned.Contains(__instance);
        }

        // --- Keeping a bot on a broken leg down ---

        /// <summary>
        /// The bot this hook is currently inside the throw of, when standing up for it is
        /// to be allowed. Saved and restored rather than cleared, on the same reasoning as
        /// the spill window in SurvivabilityPatches: nothing guarantees these calls do not
        /// nest, and an inner one must not close the outer one's permission.
        /// </summary>
        private static BotOwner _mayStandFor;

        /// <summary>
        /// Standing up is not a single decision in the AI: pathing, patrolling, running,
        /// steering, taking a stationary weapon and simply being shot at all call GetUp
        /// straight out. Gating any one of them leaves the rest, so the ban sits on GetUp
        /// itself — and then the cases worth standing for have to be named back.
        ///
        /// Throwing a grenade is one: prone, a man cannot get the arc, and a broken leg is
        /// no reason to give up the one thing he can still do at range. Being shot at is
        /// deliberately not one — that is the reflex that produced the stand-fall-stand
        /// loop in the first place, and a man with a broken femur does not answer fire by
        /// standing up.
        /// </summary>
        private static bool GetUpPrefix(BotLay __instance)
        {
            var owner = __instance?.BotOwner_0;
            if (!CrippleSystem.IsGrounded(owner))
            {
                return true;
            }

            if (owner == _mayStandFor)
            {
                CrippleSystem.GetUpsAllowed++;
                return true;
            }

            // Counted only when it changed something. GetUp is an idempotent "make sure I
            // am standing" that pathing and steering call every frame, so blocking it on a
            // bot who is already on his feet is a no-op — and counting those put 8281 in
            // the report for a raid where four bots ever lay down.
            if (__instance.IsLay)
            {
                CrippleSystem.GetUpsBlocked++;
            }

            return false;
        }

        private static void ThrowPrefix(BotGrenadeController __instance, out BotOwner __state)
        {
            __state = _mayStandFor;
            _mayStandFor = __instance?.BotOwner_0;
        }

        // a finalizer, not a postfix: the permission has to close even if the throw throws
        private static void ThrowFinalizer(BotOwner __state)
        {
            _mayStandFor = __state;
        }
    }
}
