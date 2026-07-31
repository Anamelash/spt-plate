using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using PLATE.Client.Blood;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Blood system inputs.
    /// 1. Bleeding.RegularUpdate — a vanilla bleeding tick -> ml/s drain from the
    ///    flow-rate table (body part, type), self-limited by hypotension.
    /// 2. ActiveHealthController.ApplyDamage — guaranteed LightBleeding for any
    ///    penetrating wound with damage above the threshold.
    /// 3. DestroyBodyPart(Stomach) — massive internal bleeding.
    /// 4. Kill — removes the target from tracking.
    /// </summary>
    internal static class BloodPatches
    {
        /// <summary>Transfusion item tpl — kept in sync with PLATE.Server.TransfusionItem.</summary>
        public const string TransfusionTpl = "b100d0000000000000000001";

        public static void Apply(Harmony harmony)
        {
            PatchSafe(harmony, PatchTargets.Bleeding_RegularUpdate, nameof(BleedingTickPostfix));
            PatchSafe(harmony, PatchTargets.Health_ApplyDamage, nameof(GuaranteedBleedPostfix));
            PatchSafe(harmony, PatchTargets.Health_DestroyBodyPart, nameof(PartDestroyedPostfix));
            PatchSafe(harmony, PatchTargets.Health_Kill, nameof(KillPostfix));
            PatchSafe(harmony, PatchTargets.Health_DoMedEffect, nameof(MedEffectPostfix));
            PatchSafe(harmony, PatchTargets.Health_CanApplyItem, nameof(TransfusionCanApplyPostfix));

            // the local player applies items via ApplyItem (both overloads);
            // DoMedEffect is called only by observed controllers (bots)
            foreach (var target in PatchTargets.Health_ApplyItemOverloads)
            {
                try
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(typeof(BloodPatches),
                        nameof(TransfusionApplyItemPostfix)));
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[PLATE] Blood: ApplyItem patch failed: {ex.Message}");
                }
            }

            // push signals for cripple recalculation (the 5-7 s safety polling picks up anything missed)
            PatchSafe(harmony, PatchTargets.Health_DoFracture, nameof(FracturePushPostfix));
            PatchSafe(harmony, PatchTargets.EffectBase_Removed, nameof(EffectRemovedPushPostfix));
            PatchSafe(harmony, PatchTargets.Health_RestoreBodyPart, nameof(RestorePushPostfix));
            PatchSafe(harmony, PatchTargets.Health_FullRestoreBodyPart, nameof(RestorePushPostfix));

            // keep-alive for a LowEdgeHealth held by the blood system (tier 2+ heartbeat):
            // the vanilla tick self-removes the effect once total HP >= StartCommonHealth
            if (PatchTargets.LowEdge_RegularUpdate != null)
            {
                try
                {
                    harmony.Patch(PatchTargets.LowEdge_RegularUpdate,
                        prefix: new HarmonyMethod(typeof(BloodPatches), nameof(LowEdgeKeepAlivePrefix)));
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[PLATE] Blood: LowEdge keep-alive patch failed: {ex.Message}");
                }
            }
        }

        private static bool LowEdgeKeepAlivePrefix(object __instance)
        {
            var t = PerfTrace.Begin();
            // false = skip the vanilla tick (do not let the effect remove itself)
            // while the blood system is holding it
            var result = Off || !PlateBloodManager.IsHeldLowEdge(__instance);
            PerfTrace.End("lowedge.tick", t);
            return result;
        }

        private static void PatchSafe(Harmony harmony, MethodBase target, string postfixName)
        {
            if (target == null)
            {
                Plugin.Log.LogError($"[PLATE] Blood: target for {postfixName} not resolved, skipped");
                return;
            }

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(BloodPatches), postfixName));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PLATE] Blood: failed to patch {target.Name}: {ex.Message}");
            }
        }

        private static bool Off => !PlateClientConfig.BloodEnabled.Value;

        // --- 1. Drain from vanilla bleedings ---

        /// <summary>Flow rate for a specific effect: rolled once on the first tick.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object>
            EffectRateCache = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();

        private static void BleedingTickPostfix(ActiveHealthController.Bleeding __instance,
            float deltaTime)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var player = __instance.HealthController?.Player;
                if (player == null)
                {
                    return;
                }

                var state = PlateBloodManager.GetOrCreate(player);
                if (state == null || state.Dead)
                {
                    return;
                }

                if (!EffectRateCache.TryGetValue(__instance, out var boxed))
                {
                    boxed = ResolveRate(__instance, state, player);
                    EffectRateCache.Add(__instance, boxed);
                }

                PlateBloodManager.QueueExternalDrain(player,
                    (float)boxed * PlateBloodManager.SelfLimit(state) * deltaTime);
            }
            catch (Exception ex)
            {
                LogError(nameof(BleedingTickPostfix), ex);
            }
        }

        private static object ResolveRate(ActiveHealthController.Bleeding effect,
            Blood.BloodState state, EFT.Player player)
        {
            var isHeavy = effect.GetType().Name == "HeavyBleeding";
            if (!isHeavy)
            {
                return PlateClientConfig.BleedLight.Value;
            }

            var part = effect.BodyPart;
            switch (part)
            {
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg:
                    // femoral artery: only if the wound landed in the thigh/pelvis
                    // (Thigh/Pelvis hitboxes; the calf gets a regular arterial bleed),
                    // and only with a chance: the artery occupies a small share of the
                    // thigh's cross-section
                    if (state.LastHitCollider.TryGetValue(part, out var hit) &&
                        Time.time - hit.Time < 3f &&
                        IsFemoralZone(hit.Collider) &&
                        UnityEngine.Random.value < PlateClientConfig.FemoralChance.Value)
                    {
                        Overlay.HitFeed.PushPanel(
                            $"{Overlay.OverlayHud.NameOf(player)} FEMORAL ARTERY " +
                            $"({hit.Collider}) — {PlateClientConfig.FemoralBleedMlSec.Value:0} ml/s");
                        Overlay.HitFeed.PushFloat(player.ProfileId,
                            player.Position + UnityEngine.Vector3.up * 1.75f,
                            "FEMORAL ARTERY", new UnityEngine.Color(1f, 0.1f, 0.1f));
                        return PlateClientConfig.FemoralBleedMlSec.Value;
                    }

                    return PlateClientConfig.BleedHeavyLeg.Value;

                case EBodyPart.Stomach:
                    // groin: the Pelvis collider maps to Stomach — iliac vessels,
                    // same lethality as the femoral artery
                    if (state.LastHitCollider.TryGetValue(part, out var pelvisHit) &&
                        Time.time - pelvisHit.Time < 3f &&
                        IsFemoralZone(pelvisHit.Collider) &&
                        UnityEngine.Random.value < PlateClientConfig.FemoralChance.Value)
                    {
                        Overlay.HitFeed.PushPanel(
                            $"{Overlay.OverlayHud.NameOf(player)} ILIAC ARTERY " +
                            $"({pelvisHit.Collider}) — {PlateClientConfig.FemoralBleedMlSec.Value:0} ml/s");
                        return PlateClientConfig.FemoralBleedMlSec.Value;
                    }

                    return PlateClientConfig.BleedHeavyTorso.Value;

                case EBodyPart.LeftArm:
                case EBodyPart.RightArm:
                    return PlateClientConfig.BleedHeavyArm.Value;

                default:
                    return PlateClientConfig.BleedHeavyTorso.Value;
            }
        }

        private static bool IsFemoralZone(EBodyPartColliderType collider)
        {
            return collider == EBodyPartColliderType.LeftThigh ||
                   collider == EBodyPartColliderType.RightThigh ||
                   collider == EBodyPartColliderType.Pelvis ||
                   collider == EBodyPartColliderType.PelvisBack;
        }

        // --- 2. Guaranteed light bleeding on a penetrating wound ---

        private static void GuaranteedBleedPostfix(ActiveHealthController __instance,
            EBodyPart bodyPart, float damage, DamageInfoStruct damageInfo, float __result)
        {
            if (Off || damageInfo.BlockedBy.HasValue)
            {
                return;
            }

            var dt = damageInfo.DamageType;
            if (dt == EDamageType.Explosion)
            {
                TryBlastBarotrauma(__instance, __result > 0f ? __result : damage);
                return;
            }

            // penetrating wounds: bullets, grenade and landmine fragments
            if (dt != EDamageType.Bullet && dt != EDamageType.GrenadeFragment &&
                dt != EDamageType.Landmine)
            {
                return;
            }

            try
            {
                var player = __instance.Player;
                var state = PlateBloodManager.GetOrCreate(player);
                if (state == null || state.Dead)
                {
                    return;
                }

                // remember the wound collider — it decides femoral/iliac artery once
                // vanilla applies HeavyBleeding to this part
                state.LastHitCollider[bodyPart] = (damageInfo.BodyPartColliderType, Time.time);

                // push: the damage may have destroyed the part — recalculate cripples on the next tick
                state.NextCrippleCheck = 0f;

                var applied = __result > 0f ? __result : damage;

                // fracture: P = P(bone hit | collider) * ramp over the bullet's energy
                TryBoneFracture(__instance, bodyPart, damageInfo, applied);

                if (applied <= PlateClientConfig.GuaranteedBleedMinDamage.Value)
                {
                    return;
                }

                // at most once per 5 seconds per part — vanilla keeps one effect per
                // part, repeated DoBleed calls are pointless
                if (state.LastGuaranteedBleedAt.TryGetValue(bodyPart, out var last) &&
                    Time.time - last < 5f)
                {
                    return;
                }

                state.LastGuaranteedBleedAt[bodyPart] = Time.time;
                __instance.DoBleed(false, bodyPart);
            }
            catch (Exception ex)
            {
                LogError(nameof(GuaranteedBleedPostfix), ex);
            }
        }

        /// <summary>Timestamp of the last barotrauma roll per player (an explosion hits
        /// several body parts in one frame — one roll per blast).</summary>
        private static readonly Dictionary<string, float> LastBlastRoll =
            new Dictionary<string, float>();

        /// <summary>
        /// Blast-wave barotrauma: a close detonation (high Explosion damage) gives a
        /// chance of internal bleeding, ramping from MinDamage to FullDamage.
        /// </summary>
        private static void TryBlastBarotrauma(ActiveHealthController ahc, float applied)
        {
            if (!PlateClientConfig.BlastBarotrauma.Value ||
                applied < PlateClientConfig.BlastInternalMinDamage.Value ||
                PlateClientConfig.BlastInternalMlSec.Value <= 0f)
            {
                return;
            }

            try
            {
                var player = ahc.Player;
                var state = PlateBloodManager.GetOrCreate(player);
                if (state == null || state.Dead || player?.ProfileId == null)
                {
                    return;
                }

                if (LastBlastRoll.TryGetValue(player.ProfileId, out var last) &&
                    Time.time - last < 1f)
                {
                    return; // this blast has already been rolled (another body part)
                }

                LastBlastRoll[player.ProfileId] = Time.time;

                var min = PlateClientConfig.BlastInternalMinDamage.Value;
                var full = Mathf.Max(PlateClientConfig.BlastInternalFullDamage.Value, min + 1f);
                var p = Mathf.Clamp01((applied - min) / (full - min));
                if (UnityEngine.Random.value < p)
                {
                    PlateBloodManager.AddInternal(player, PlateClientConfig.BlastInternalMlSec.Value);
                    Overlay.HitFeed.PushPanel(
                        $"{Overlay.OverlayHud.NameOf(player)} BLAST BAROTRAUMA " +
                        $"(dmg {applied:0}, p={p:0.00}) -> internal bleed");
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(TryBlastBarotrauma), ex);
            }
        }

        /// <summary>
        /// Our own fracture roll instead of the vanilla damage curve:
        /// P = P(bone | collider) * clamp01((E - Emin)/(Efull - Emin)).
        /// If the ballistics code already rolled bone for this hit
        /// (IsPenetratedPrefix: stopped-by-bone), consume its result — the bone hit
        /// and the bullet stop stay consistent; only the energy ramp remains here.
        /// The vanilla bullet fracture roll is zeroed on the server (BloodGlobals).
        /// </summary>
        private static void TryBoneFracture(ActiveHealthController ahc, EBodyPart bodyPart,
            DamageInfoStruct damageInfo, float applied)
        {
            var bone = BoneChance(damageInfo.BodyPartColliderType);
            if (bone <= 0f)
            {
                return;
            }

            if (Blood.CrippleSystem.HasActiveFracture(ahc, bodyPart))
            {
                return; // already fractured
            }

            var e = BallisticsPatches.ShotEnergyThisFrame;
            if (e <= 0f)
            {
                // fallback if the ballistics module is disabled: energy from damage
                e = applied * 35f;
            }

            var eMin = PlateClientConfig.FractureEnergyMin.Value;
            var ramp = Mathf.Clamp01((e - eMin) /
                Mathf.Max(PlateClientConfig.FractureEnergyFull.Value - eMin, 1f));

            float p;
            if (BallisticsPatches.TryGetBoneHit(damageInfo.BodyPartColliderType, out var boneHit))
            {
                if (!boneHit)
                {
                    return; // bone not hit (the shared ballistics roll)
                }

                p = ramp; // bone is hit — only energy decides the fracture
            }
            else
            {
                p = bone * ramp; // no prior ballistics roll — joint roll as before
            }

            if (p > 0f && UnityEngine.Random.value < p)
            {
                ahc.DoFracture(bodyPart);
                Overlay.HitFeed.PushPanel(
                    $"  bone fracture {bodyPart} ({damageInfo.BodyPartColliderType}, " +
                    $"{e:0}J, p={p:0.00})");
            }
        }

        internal static float BoneChance(EBodyPartColliderType collider)
        {
            switch (collider)
            {
                case EBodyPartColliderType.LeftThigh:
                case EBodyPartColliderType.RightThigh:
                    return PlateClientConfig.BoneChanceThigh.Value;
                case EBodyPartColliderType.LeftCalf:
                case EBodyPartColliderType.RightCalf:
                    return PlateClientConfig.BoneChanceCalf.Value;
                case EBodyPartColliderType.LeftUpperArm:
                case EBodyPartColliderType.RightUpperArm:
                    return PlateClientConfig.BoneChanceUpperArm.Value;
                case EBodyPartColliderType.LeftForearm:
                case EBodyPartColliderType.RightForearm:
                    return PlateClientConfig.BoneChanceForearm.Value;
                default:
                    return 0f; // torso/head — bullet fractures only on limbs (like vanilla)
            }
        }

        // --- 3. Destroyed body part = massive internal bleeding ---

        private static void PartDestroyedPostfix(ActiveHealthController __instance,
            EBodyPart bodyPart, EDamageType damageType)
        {
            if (Off)
            {
                return;
            }

            try
            {
                var rate = PlateBloodManager.DestroyedPartBleed(bodyPart);
                var player = __instance.Player;
                if (rate > 0f && player != null)
                {
                    PlateBloodManager.AddInternal(player, rate);
                }

                PlateBloodManager.RequestRefresh(player); // push: instant CRIPPLED / jump ban
            }
            catch (Exception ex)
            {
                LogError(nameof(PartDestroyedPostfix), ex);
            }
        }

        // --- Push signals for cripple recalculation ---

        private static void FracturePushPostfix(ActiveHealthController __instance)
        {
            if (!Off)
            {
                PlateBloodManager.RequestRefresh(__instance.Player);
            }
        }

        private static PropertyInfo _effectControllerProp;

        /// <summary>Fracture removal (splint/surgery/anything) — instant recalculation.</summary>
        private static void EffectRemovedPushPostfix(object __instance)
        {
            if (Off || __instance.GetType().Name != "Fracture")
            {
                return;
            }

            try
            {
                _effectControllerProp ??= PatchTargets.EffectBase?.GetProperty("HealthController");
                var ahc = _effectControllerProp?.GetValue(__instance) as ActiveHealthController;
                if (ahc?.Player != null)
                {
                    PlateBloodManager.RequestRefresh(ahc.Player);
                }
            }
            catch
            {
                // the safety polling will pick it up
            }
        }

        private static void RestorePushPostfix(ActiveHealthController __instance)
        {
            if (!Off)
            {
                PlateBloodManager.RequestRefresh(__instance.Player);
            }
        }

        // --- 4. Transfusion item: volume restoration ---

        /// <summary>
        /// Applicability gate: the MedKit class requires lost HP or healable effects —
        /// the blood bag does not need that (it restores blood volume, invisible to
        /// vanilla). Always allowed except: dead, a med is already being applied, the
        /// bag is empty.
        /// </summary>
        // __instance is typed as object: the method is declared on the generic base
        // GClass3009 and shared between controllers (ActiveHealthController in raid,
        // HealthControllerClass in the stash) — a hard type would throw
        // InvalidCastException out of raid.
        private static void TransfusionCanApplyPostfix(object __instance,
            EFT.InventoryLogic.Item item, ref bool __result)
        {
            if (Off || __result || item == null || item.TemplateId.ToString() != TransfusionTpl)
            {
                return;
            }

            try
            {
                if (!(__instance is ActiveHealthController ahc))
                {
                    return; // out of raid food/drink is applicable in vanilla without our help
                }

                if (!ahc.IsAlive)
                {
                    return;
                }

                if (ahc.Player?.HandsController is Player.MedsController)
                {
                    return; // another med is in progress — do not allow a second Proceed
                }

                var comp = item.GetItemComponent<EFT.InventoryLogic.MedKitComponent>();
                if (comp != null && comp.HpResource <= 0f)
                {
                    return; // empty bag
                }

                __result = true;
            }
            catch (Exception ex)
            {
                LogError(nameof(TransfusionCanApplyPostfix), ex);
            }
        }

        private static void MedEffectPostfix(ActiveHealthController __instance,
            EFT.InventoryLogic.Item item)
        {
            TryTransfusionRestore(__instance, item);
        }

        private static void TransfusionApplyItemPostfix(object __instance,
            EFT.InventoryLogic.Item item)
        {
            TryTransfusionRestore(__instance, item);
        }

        private static string _lastTransfusionItemId;
        private static float _lastTransfusionAt;

        /// <summary>
        /// Shared blood restoration point: ApplyItem (local player, 2 overloads) and
        /// DoMedEffect (observed players) can fire several times for a single use —
        /// dedup per item within a 2 s window. In raid the blood lives in
        /// PlateBloodManager, out of raid (stash/character menu) — in the server profile.
        /// </summary>
        private static void TryTransfusionRestore(object controller, EFT.InventoryLogic.Item item)
        {
            if (Off || item == null || item.TemplateId.ToString() != TransfusionTpl)
            {
                return;
            }

            try
            {
                var id = item.Id.ToString();
                if (id == _lastTransfusionItemId && Time.time - _lastTransfusionAt < 2f)
                {
                    return; // duplicate of the same use (overloads/DoMedEffect)
                }

                var restore = PlateClientConfig.TransfusionMlPerUse.Value;

                if (controller is ActiveHealthController ahc)
                {
                    // in raid: the volume lives in PlateBloodManager, its tick pushes to the server
                    var player = ahc.Player;
                    var state = PlateBloodManager.GetOrCreate(player);
                    if (state == null || state.Dead)
                    {
                        return;
                    }

                    _lastTransfusionItemId = id;
                    _lastTransfusionAt = Time.time;

                    state.Cur = Mathf.Min(state.Max, state.Cur + restore);

                    // resource consumption: vanilla deducts food/drink itself (the Eat
                    // event). Manual deduction is only needed for the MedKit class:
                    var comp = item.GetItemComponent<EFT.InventoryLogic.MedKitComponent>();
                    if (comp != null)
                    {
                        comp.HpResource = Mathf.Max(0f, comp.HpResource - 1f);
                        BloodSync.PushItemUse(id);
                    }

                    Overlay.HitFeed.PushPanel($"{Overlay.OverlayHud.NameOf(player)} TRANSFUSION " +
                                              $"+{restore:0} ml -> {state.Cur:0}/{state.Max:0}");
                    HealthTabPatch.Refresh(); // the Health tab may be open
                }
                else
                {
                    // out of raid: edit the profile record directly via blood-set
                    _lastTransfusionItemId = id;
                    _lastTransfusionAt = Time.time;

                    var rec = BloodSync.GetCached();
                    var max = rec?.Max ?? PlateClientConfig.BloodMaxMl.Value;
                    var cur = rec?.Cur ?? max;
                    var newCur = Math.Min(max, cur + restore);
                    BloodSync.Push(newCur, max, false);
                    HealthTabPatch.Refresh(); // BP bar updates without reopening the tab
                    Plugin.Log.LogInfo($"[PLATE] Transfusion out-of-raid: +{restore:0} ml " +
                                       $"-> {newCur:0}/{max:0}");
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(TryTransfusionRestore), ex);
            }
        }

        // --- 5. Death — remove from tracking ---

        private static void KillPostfix(ActiveHealthController __instance)
        {
            try
            {
                PlateBloodManager.MarkDead(__instance.Player?.ProfileId);
            }
            catch
            {
                // not critical
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
            Plugin.Log.LogError($"[PLATE] Blood {where}: {ex}");
        }
    }
}
