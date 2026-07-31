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
    }
}
