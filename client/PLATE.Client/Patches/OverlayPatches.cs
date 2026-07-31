using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using PLATE.Client.Overlay;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Hit data collection. Read-only — game formulas are not changed.
    /// Every handler starts with a check of the live OverlayEnabled toggle: the module
    /// can be turned off via F12 right in raid (to A/B-test frame hitches).
    /// </summary>
    internal static class OverlayPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchSafe(harmony, PatchTargets.Health_ApplyDamage, nameof(HealthApplyDamagePostfix));
            PatchSafe(harmony, PatchTargets.Armor_ApplyDamage, nameof(ArmorApplyDamagePostfix),
                prefixName: nameof(ArmorApplyDamagePrefix));
            PatchSafe(harmony, PatchTargets.Bullet_DegradeOnHit, nameof(BulletDegradePostfix));
            PatchSafe(harmony, PatchTargets.Bullet_Overpenetrate, nameof(BulletOverpenPostfix));
            PatchSafe(harmony, PatchTargets.Bullet_Fragment, nameof(BulletFragmentPostfix));

            // Health events via direct postfixes: the vanilla EffectAddedEvent in 0.16.9
            // is dead (nobody invokes it), and subscribing to Died/PartDestroyed did not fire
            PatchSafe(harmony, PatchTargets.Health_Kill, nameof(KillPostfix));
            PatchSafe(harmony, PatchTargets.Health_DestroyBodyPart, nameof(DestroyBodyPartPostfix));
            PatchSafe(harmony, PatchTargets.Health_DoBleed, nameof(DoBleedPostfix));
            PatchSafe(harmony, PatchTargets.Health_DoFracture, nameof(DoFracturePostfix));
        }

        // --- Health events (death, part destruction, bleedings, fractures) ---

        private static readonly System.Collections.Generic.HashSet<string> DeathLogged =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>Per-raid state reset (called by OverlayHud at raid end).</summary>
        public static void ResetRaidState()
        {
            DeathLogged.Clear();
        }

        private static void KillPostfix(ActiveHealthController __instance, EDamageType damageType)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var victim = __instance.Player;
                if (victim == null || !OverlayHud.PassesFightFilter(victim.ProfileId, null))
                {
                    return;
                }

                // the game calls Kill twice — log the death once
                if (!DeathLogged.Add(victim.ProfileId))
                {
                    return;
                }

                HitFeed.PushFloat(victim.ProfileId, victim.Position + Vector3.up * 1.6f,
                    $"DEAD ({damageType})", new Color(0.9f, 0.15f, 0.15f));
                HitFeed.PushPanel($"{OverlayHud.NameOf(victim)} DIED ({damageType})");
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(KillPostfix), ex);
            }
        }

        private static void DestroyBodyPartPostfix(ActiveHealthController __instance,
            EBodyPart bodyPart, EDamageType damageType)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var victim = __instance.Player;
                if (victim == null || !OverlayHud.PassesFightFilter(victim.ProfileId, null))
                {
                    return;
                }

                HitFeed.PushFloat(victim.ProfileId, victim.Position + Vector3.up * 1.6f,
                    $"DESTROYED {bodyPart}", new Color(1f, 0.6f, 0.1f));
                HitFeed.PushPanel($"{OverlayHud.NameOf(victim)} part destroyed: {bodyPart} ({damageType})");
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(DestroyBodyPartPostfix), ex);
            }
        }

        private static void DoBleedPostfix(ActiveHealthController __instance,
            bool isHeavy, EBodyPart bodyPart)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var victim = __instance.Player;
                if (victim == null || !OverlayHud.PassesFightFilter(victim.ProfileId, null))
                {
                    return;
                }

                var name = isHeavy ? "HeavyBleeding" : "LightBleeding";
                HitFeed.PushFloat(victim.ProfileId, victim.Position + Vector3.up * 1.75f,
                    $"+{name} {bodyPart}", new Color(1f, 0.45f, 0.35f));
                HitFeed.PushPanel($"{OverlayHud.NameOf(victim)} +{name} {bodyPart}");
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(DoBleedPostfix), ex);
            }
        }

        private static void DoFracturePostfix(ActiveHealthController __instance, EBodyPart bodyPart)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var victim = __instance.Player;
                if (victim == null || !OverlayHud.PassesFightFilter(victim.ProfileId, null))
                {
                    return;
                }

                HitFeed.PushFloat(victim.ProfileId, victim.Position + Vector3.up * 1.75f,
                    $"+Fracture {bodyPart}", new Color(1f, 0.45f, 0.35f));
                HitFeed.PushPanel($"{OverlayHud.NameOf(victim)} +Fracture {bodyPart}");
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(DoFracturePostfix), ex);
            }
        }

        private static void PatchSafe(Harmony harmony, MethodBase target, string postfixName,
            string prefixName = null)
        {
            if (target == null)
            {
                Plugin.Log.LogError($"[PLATE] Overlay: target for {postfixName} not resolved, skipped");
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix: prefixName == null
                        ? null
                        : new HarmonyMethod(typeof(OverlayPatches), prefixName),
                    postfix: new HarmonyMethod(typeof(OverlayPatches), postfixName));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PLATE] Overlay: failed to patch {target.Name}: {ex.Message}");
            }
        }

        private static bool Off => !PlateClientConfig.OverlayEnabled.Value;

        private static string ChainOf(EftBulletClass shot)
        {
            return $"b{shot.RandomSeed & 0xfff:x3}/{shot.FragmentIndex}";
        }

        private static string FlagsOf(EftBulletClass shot)
        {
            var f = "";
            if (shot.AvoidAdditionalDamage)
            {
                f += "AVOID ";
            }

            if (shot.DelayedDamage)
            {
                f += "DELAY ";
            }

            return f;
        }

        // --- Final body-part damage (player and bots) ---

        private static void HealthApplyDamagePostfix(ActiveHealthController __instance,
            EBodyPart bodyPart, float damage, DamageInfoStruct damageInfo, float __result)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var victim = __instance.Player;
                if (victim == null)
                {
                    return;
                }

                var aggressorId = damageInfo.Player?.iPlayer?.ProfileId;
                if (!OverlayHud.PassesFightFilter(victim.ProfileId, aggressorId))
                {
                    return;
                }

                // chronic ticks (bleedings, dehydration etc.) are spam:
                // 7 lines per tick with a destroyed part. Log only in verbose.
                var dtName = damageInfo.DamageType.ToString();
                if (!PlateClientConfig.VerboseLog.Value &&
                    (dtName.Contains("Bleeding") || dtName == "Dehydration" ||
                     dtName == "Exhaustion" || dtName == "Intoxication" ||
                     dtName == "Poison" || dtName == "Radiation" || dtName == "LethalToxin"))
                {
                    return;
                }

                var applied = __result > 0f ? __result : damage;
                var blocked = damageInfo.BlockedBy.HasValue;
                var tag = blocked ? "BLUNT" : dtName;

                var extra = "";
                if (HitFeed.TryConsumeImpact(victim.ProfileId, out var imp))
                {
                    extra = $" {imp.ChainId} {imp.Flags}{imp.EnergyJ:0}J {imp.SpeedMs:0}m/s pen{imp.PenPower:0.#}";
                    if (!string.IsNullOrEmpty(imp.Tag))
                    {
                        tag += " " + imp.Tag;
                    }
                }

                var hpAfter = "";
                try
                {
                    var hp = __instance.GetBodyPartHealth(bodyPart, false);
                    hpAfter = $" hp {hp.Current:0.#}/{hp.Maximum:0}";
                }
                catch
                {
                    // not critical
                }

                if (!victim.IsYourPlayer)
                {
                    var color = blocked ? new Color(0.75f, 0.75f, 0.75f) : Color.white;
                    HitFeed.PushFloat(victim.ProfileId, victim.Position + Vector3.up * 1.9f,
                        $"-{applied:0.#} {bodyPart} [{tag}]", color);
                }

                HitFeed.PushPanel(
                    $"{OverlayHud.NameOf(victim)} {bodyPart} -{applied:0.#} (raw {damageInfo.Damage:0.#})" +
                    $"{hpAfter} [{tag}]{extra}");
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(HealthApplyDamagePostfix), ex);
            }
        }

        // --- Armor: how much it shaved off, penetrated or not ---

        private static void ArmorApplyDamagePrefix(ref DamageInfoStruct damageInfo, out float __state)
        {
            __state = damageInfo.Damage;
        }

        private static void ArmorApplyDamagePostfix(ArmorComponent __instance,
            ref DamageInfoStruct damageInfo, float __state, float __result)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var aggressorId = damageInfo.Player?.iPlayer?.ProfileId;
                if (!OverlayHud.PassesFightFilter(null, aggressorId))
                {
                    return;
                }

                var status = damageInfo.BlockedBy.HasValue ? "BLOCK" : "PEN";
                HitFeed.PushPanel(
                    $"  armor c{__instance.ArmorClass} [{status}] dmg {__state:0.#} -> {damageInfo.Damage:0.#} " +
                    $"(ret {__result:0.#}) dura {__instance.Repairable.Durability:0.#}/" +
                    $"{__instance.Repairable.MaxDurability:0.#}");
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(ArmorApplyDamagePostfix), ex);
            }
        }

        // --- Bullet level: energy at impact, overpenetration, fragmentation ---

        private static void BulletDegradePostfix(EftBulletClass __instance)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var bpc = __instance.HittedBallisticCollider as BodyPartCollider;
                var victimId = bpc?.Player?.ProfileId;
                if (victimId == null ||
                    !OverlayHud.PassesFightFilter(victimId, __instance.PlayerProfileID))
                {
                    return;
                }

                var v = __instance.Vector3_1.magnitude;
                var e = 0.5f * (__instance.BulletMassGram / 1000f) * v * v;
                HitFeed.RememberImpact(victimId, new HitFeed.BulletImpact
                {
                    EnergyJ = e,
                    SpeedMs = v,
                    PenPower = __instance.PenetrationPower,
                    ChainId = ChainOf(__instance),
                    Flags = FlagsOf(__instance),
                    Tag = "",
                });
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(BulletDegradePostfix), ex);
            }
        }

        private static void BulletOverpenPostfix(EftBulletClass __instance)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var bpc = __instance.HittedBallisticCollider as BodyPartCollider;
                var victimId = bpc?.Player?.ProfileId;
                if (victimId == null ||
                    !OverlayHud.PassesFightFilter(victimId, __instance.PlayerProfileID))
                {
                    return;
                }

                var child = __instance.Fragments.Count > 0
                    ? __instance.Fragments[__instance.Fragments.Count - 1]
                    : null;
                var k = child != null && __instance.Damage > 0.01f
                    ? child.Damage / __instance.Damage
                    : 0f;
                HitFeed.AmendImpactTag(victimId, $"OVERPEN k={k:0.00}");
                HitFeed.PushPanel(
                    $"  overpen {ChainOf(__instance)} {bpc.BodyPartType} " +
                    $"{(__instance.IsForwardHit ? "fwd" : "back")} k={k:0.00} " +
                    $"(dmg {__instance.Damage:0.#} -> {child?.Damage ?? 0f:0.#}) {FlagsOf(__instance)}");
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(BulletOverpenPostfix), ex);
            }
        }

        private static void BulletFragmentPostfix(EftBulletClass __instance)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var bpc = __instance.HittedBallisticCollider as BodyPartCollider;
                var victimId = bpc?.Player?.ProfileId;
                if (victimId == null ||
                    !OverlayHud.PassesFightFilter(victimId, __instance.PlayerProfileID))
                {
                    return;
                }

                var n = __instance.Fragments.Count;
                if (n > 0)
                {
                    HitFeed.AmendImpactTag(victimId, $"FRAG x{n}");
                    HitFeed.PushPanel(
                        $"  fragmentation {ChainOf(__instance)} {bpc.BodyPartType} x{n}");
                }
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(BulletFragmentPostfix), ex);
            }
        }

        private static float _lastErrorLogged;

        private static void LogPatchError(string where, Exception ex)
        {
            // avoid spamming the log if something is systemically broken
            if (Time.unscaledTime - _lastErrorLogged < 5f)
            {
                return;
            }

            _lastErrorLogged = Time.unscaledTime;
            Plugin.Log.LogError($"[PLATE] Overlay {where}: {ex}");
        }
    }
}
