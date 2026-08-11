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
        public const string Version = "1.2.0";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;

        /// <summary>Patches that could not be applied; reported once after startup.</summary>
        internal static int PatchFailures => PatchStats.Failures;

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

            // Survivability overrides (section 7). Applied whatever the modules are set
            // to — they override the game as much as they override PLATE — and always,
            // rather than only when something is off-default, so that flipping a switch
            // in F12 takes effect the way every other setting in the mod does.
            SurvivabilityPatches.Apply(HarmonyInstance);

            // Hit overlay
            if (PlateClientConfig.OverlayEnabled.Value)
            {
                OverlayPatches.Apply(HarmonyInstance);
                gameObject.AddComponent<OverlayHud>();
                Log.LogInfo("[PLATE] Overlay enabled");
            }

            // a patch that fails to apply leaves a feature silently dead, which reads as
            // a broken mod rather than a broken hook — say it once, loudly, at the end
            if (PatchFailures > 0)
            {
                Log.LogError(
                    $"[PLATE] {PatchFailures} patch(es) FAILED to apply — parts of the mod are " +
                    "inactive. Search this log for '[PLATE]' errors above and report them with " +
                    "the mod version and your SPT/EFT versions.");
            }

            Log.LogInfo($"{Name} {Version} loaded" +
                        (PatchFailures > 0 ? $" WITH {PatchFailures} FAILED PATCH(ES)" : ""));
        }

        /// <summary>
        /// Journal upkeep. Lives on the plugin itself so it runs whatever modules are
        /// enabled — the event journal used to be flushed by the overlay component,
        /// which is off by default, so no journal was ever written unless someone
        /// switched on a debug visualisation they had no reason to switch on.
        /// </summary>
        private void Update()
        {
            HitFeed.FlushTick(UnityEngine.Time.time);

            // raid end: dump the hook telemetry while the session is still fresh
            var world = Comfort.Common.Singleton<EFT.GameWorld>.Instance;
            var inRaid = world != null;
            if (_wasInRaid && !inRaid)
            {
                HitFeed.WriteHookReport();
                _warmed.Clear();
            }

            _wasInRaid = inRaid;

            if (inRaid)
            {
                WarmOneVictim(world);
            }
        }

        private bool _wasInRaid;

        private readonly HashSet<string> _warmed = new HashSet<string>();

        /// <summary>
        /// A victim's hitbox list is built by walking their whole rig — the one piece of
        /// per-target work heavy enough to be felt, and it used to land on the frame of
        /// the first hit on them. Done here instead: one player per frame, so the scans
        /// never bunch up and are finished long before anyone gets shot.
        /// </summary>
        private void WarmOneVictim(EFT.GameWorld world)
        {
            var players = world.AllAlivePlayersList;
            if (players == null)
            {
                return;
            }

            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.ProfileId == null || !_warmed.Add(p.ProfileId))
                {
                    continue;
                }

                BallisticsPatches.WarmVictimColliders(p);
                return;
            }
        }

        private void OnDestroy()
        {
            HitFeed.WriteHookReport();
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
