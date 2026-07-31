using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Grenade fragment spread. In vanilla MaxExplosionDistance (5-8 m) is a hard cap:
    /// Explosion gathers targets with a sphere and creates fragments only for players
    /// closer than it. The transpiler inflates every read of MaxExplosionDistance
    /// INSIDE Explosion up to the configured value (25 m). The blast and the concussion
    /// are NOT stretched: they are applied by a separate method (smethod_0) that reads
    /// the radii itself and normalizes the falloff as InverseLerp(Max, Min, dist) —
    /// strictly 0 beyond the vanilla radius.
    /// The game itself computes the fragment count per target from the solid angle
    /// (4πr²) — at 25 m only a handful arrive, as in real life.
    /// </summary>
    internal static class GrenadePatches
    {
        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.Grenade_Explosion;
            if (target == null)
            {
                Plugin.Log.LogError("[PLATE] GrenadePatches: Explosion not resolved — fragment spread stays vanilla");
                return;
            }

            harmony.Patch(target,
                transpiler: new HarmonyMethod(typeof(GrenadePatches), nameof(ExplosionTranspiler)));
            Plugin.Log.LogInfo("[PLATE] Grenade fragment range patch applied");
        }

        private static IEnumerable<CodeInstruction> ExplosionTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var inflate = AccessTools.Method(typeof(GrenadePatches), nameof(InflateFragmentRange));
            foreach (var ins in instructions)
            {
                yield return ins;
                if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) &&
                    ins.operand is MethodInfo mi && mi.Name == "get_MaxExplosionDistance")
                {
                    yield return new CodeInstruction(OpCodes.Call, inflate);
                }
            }
        }

        /// <summary>Called from the transpiler after every read of MaxExplosionDistance.</summary>
        public static float InflateFragmentRange(float vanilla)
        {
            if (vanilla <= 0f || !PlateClientConfig.BallisticsEnabled.Value)
            {
                return vanilla; // smokes/flashbangs (0) and a disabled module are left alone
            }

            var configured = PlateClientConfig.GrenadeFragmentRange?.Value ?? 0f;
            return Mathf.Max(vanilla, configured);
        }
    }
}
