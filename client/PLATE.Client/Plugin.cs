using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PLATE.Client.Overlay;
using PLATE.Client.Patches;

namespace PLATE.Client
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.anamelash.plate";
        public const string Name = "P.L.A.T.E.";
        public const string Version = "0.9.0";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;

        private void Awake()
        {
            Log = Logger;
            try
            {
                Initialize();
            }
            catch (System.Exception ex)
            {
                // Unity swallows Awake exceptions into Player.log — duplicate them to the
                // BepInEx log, otherwise the mod silently stays half-initialized
                // (e.g. after a malformed config key)
                Log.LogError($"[PLATE] FATAL: plugin init failed, mod is INACTIVE: {ex}");
            }
        }

        private void Initialize()
        {
            PlateClientConfig.Bind(Config);
            HarmonyInstance = new Harmony(Guid);

            if (PlateClientConfig.SelfTestOnLoad.Value)
            {
                RunPatchTargetsSelfTest();
            }

            // Terminal ballistics.
            // Applied BEFORE the overlay so its postfixes log the already-corrected values.
            if (PlateClientConfig.BallisticsEnabled.Value)
            {
                BallisticsPatches.Apply(HarmonyInstance);
                GrenadePatches.Apply(HarmonyInstance); // grenade fragment range per config
                Log.LogInfo("[PLATE] Ballistics enabled");
            }

            // Blood system + the bar in the Health tab
            if (PlateClientConfig.BloodEnabled.Value)
            {
                BloodPatches.Apply(HarmonyInstance);
                HealthTabPatch.Apply(HarmonyInstance);
                CripplePatches.Apply(HarmonyInstance);
                gameObject.AddComponent<Blood.BloodSystemComponent>();
                Log.LogInfo("[PLATE] Blood system enabled");
            }
            else
            {
                Log.LogWarning(
                    "[PLATE] Blood module DISABLED! If PLATE BloodGlobals is enabled on the " +
                    "server, bleedings currently deal no damage at all. Enable Blood system " +
                    "in F12 or disable BloodGlobals in the server config.jsonc.");
            }

            // Hit overlay
            if (PlateClientConfig.OverlayEnabled.Value)
            {
                OverlayPatches.Apply(HarmonyInstance);
                gameObject.AddComponent<OverlayHud>();
                Log.LogInfo("[PLATE] Overlay enabled");
            }

            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void RunPatchTargetsSelfTest()
        {
            List<string> failed = PatchTargets.SelfTest();
            if (failed.Count == 0)
            {
                Log.LogInfo("[PLATE] Patch targets self-test: all targets resolved OK");
            }
            else
            {
                Log.LogError(
                    "[PLATE] Patch targets self-test FAILED for: " + string.Join(", ", failed) +
                    ". Likely remap-name drift after an SPT update — the patch targets need " +
                    "re-resolving against the new game assemblies.");
            }
        }
    }
}
