using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.UI.Health;
using HarmonyLib;
using PLATE.Client.Blood;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Blood bar on the Health tab.
    /// Reuses the vanilla _bloodPressure slot ("blood pressure" from old EFT builds —
    /// unused in 0.16.9): activates it and fills it with our blood volume.
    /// In raid the value comes from PlateBloodManager, out of raid — from the profile
    /// (/plate/blood-get).
    /// </summary>
    internal static class HealthTabPatch
    {
        private static readonly AccessTools.FieldRef<HealthParametersPanel, HealthParameterPanel>
            BloodPressureRef =
                AccessTools.FieldRefAccess<HealthParametersPanel, HealthParameterPanel>("_bloodPressure");

        public static void Apply(Harmony harmony)
        {
            if (PatchTargets.HealthPanel_Show == null)
            {
                Plugin.Log.LogError("[PLATE] HealthTab: Show target not resolved, blood bar skipped");
                return;
            }

            try
            {
                harmony.Patch(PatchTargets.HealthPanel_Show,
                    postfix: new HarmonyMethod(typeof(HealthTabPatch), nameof(ShowPostfix)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PLATE] HealthTab: patch failed: {ex.Message}");
            }
        }

        private static HealthParametersPanel _lastPanel;

        /// <summary>Live bar update (e.g. a blood bag was used in the stash without
        /// reopening the tab). Safe if the panel has already been destroyed.</summary>
        public static void Refresh()
        {
            if (_lastPanel != null) // Unity null check: a destroyed panel is filtered out
            {
                UpdateValue(_lastPanel);
            }
        }

        private static void ShowPostfix(HealthParametersPanel __instance)
        {
            _lastPanel = __instance;
            UpdateValue(__instance);
        }

        private static void UpdateValue(HealthParametersPanel __instance)
        {
            if (!PlateClientConfig.BloodEnabled.Value)
            {
                return;
            }

            try
            {
                var panel = BloodPressureRef(__instance);
                if (panel == null)
                {
                    return;
                }

                float cur;
                float max;
                var gw = Singleton<GameWorld>.Instance;
                if (gw?.MainPlayer != null)
                {
                    // in raid — live value
                    var s = PlateBloodManager.Get(gw.MainPlayer.ProfileId);
                    cur = s?.Cur ?? PlateClientConfig.BloodMaxMl.Value;
                    max = s?.Max ?? PlateClientConfig.BloodMaxMl.Value;
                }
                else
                {
                    // out of raid — from the profile (with out-of-raid regeneration on the server)
                    var saved = BloodSync.GetCached();
                    cur = (float)(saved?.Cur ?? PlateClientConfig.BloodMaxMl.Value);
                    max = (float)(saved?.Max ?? PlateClientConfig.BloodMaxMl.Value);
                }

                // "pressure" scale: 100% = full volume, 0% = the ATLS death threshold
                var death = PlateClientConfig.DeathThreshold.Value;
                var frac = max > 0f ? UnityEngine.Mathf.Clamp01(cur / max) : 1f;
                var bp = UnityEngine.Mathf.Clamp01((frac - death) / (1f - death)) * 100f;

                panel.gameObject.SetActive(true);
                panel.SetParameterValue(
                    new ValueStruct { Current = bp, Maximum = 100f },
                    40f, // warning from tier 2 onward (70% volume = 40% pressure)
                    0, false, true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PLATE] HealthTab blood bar: {ex}");
            }
        }
    }
}
