using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
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

            // Obstacles, on the same method and deliberately as its own hook: it runs
            // after CreateFragments, so it sees what the engine actually decided —
            // the bullet state, the child and its speed — which the gate prefixes
            // upstream cannot, because the child does not exist yet when they run.
            PatchSafe(harmony, PatchTargets.Bullet_DegradeOnHit, nameof(ObstacleHitPostfix));
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
            PatchStats.Hit($"overlay:{nameof(KillPostfix)}");
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
            PatchStats.Hit($"overlay:{nameof(DestroyBodyPartPostfix)}");
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
            PatchStats.Hit($"overlay:{nameof(DoBleedPostfix)}");
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
            PatchStats.Hit($"overlay:{nameof(DoFracturePostfix)}");
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
                PatchStats.MarkFailed(null, $"overlay:{postfixName}", "target not resolved");
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
                PatchStats.Track(harmony, target, $"overlay:{postfixName}");
            }
            catch (Exception ex)
            {
                PatchStats.MarkFailed(target, $"overlay:{postfixName}", ex.Message);
                Plugin.Log.LogError($"[PLATE] Overlay: failed to patch {target.Name}: {ex.Message}");
            }
        }

        private static bool Off => !PlateClientConfig.OverlayEnabled.Value;

        /// <summary>
        /// Which projectile this is, for the journal and for stitching an impact to the
        /// damage it caused.
        ///
        /// The same serial the obstacle journal uses, and for the same reason: this used
        /// to be built from `RandomSeed`, which looks like an identity and is not one —
        /// a primary shot draws it from a range of 512, so the pellets of one volley
        /// share it routinely. A label that collides is a nuisance; a STITCH that
        /// collides attributes one bullet's damage to another, which is worse, and this
        /// value is what ties `BulletImpact` to `ApplyDamage`.
        /// </summary>
        private static string ChainOf(EftBulletClass shot)
        {
            return HitFeed.ShotId(shot);
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
            PatchStats.Hit($"overlay:{nameof(HealthApplyDamagePostfix)}");
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

                // A marker where the bullet actually landed. Filtered hard on "I fired
                // it" rather than through PassesFightFilter: the tool is specified as
                // the player's own shots, and its filter must not move when someone
                // turns off "Only my fights" to watch a bot fight.
                //
                // F is what the flesh took, B is what came through the armour as blunt
                // trauma — in PLATE a blocked hit IS the BABT, so the two are exclusive
                // and the pair reads as "which of the two happened, and how much".
                if (PlateClientConfig.MarkersEnabled.Value &&
                    LocalPlayerRef.IsShooter(aggressorId))
                {
                    // hung on the bone the bullet actually hit, so it travels with the
                    // body: a marker left at the world point a target has since walked
                    // away from is evidence about nothing. The bone comes from the
                    // ballistics side — the damage event knows the victim and the number
                    // but not where on the rig it landed.
                    HitMarkers.Add(damageInfo.HitPoint, damageInfo.Direction,
                        blocked ? $"F:0 B:{applied:0.#}" : $"F:{applied:0.#} B:0",
                        blocked ? HitMarkers.BodyBlocked : HitMarkers.BodyPenetrated,
                        BallisticsPatches.HitBoneThisFrame);
                }
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
            PatchStats.Hit($"overlay:{nameof(ArmorApplyDamagePostfix)}");
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
            PatchStats.Hit($"overlay:{nameof(BulletDegradePostfix)}");
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

        /// <summary>
        /// Everything the player's shots meet that is not a person: one journal line and
        /// one world marker per interaction, penetration, stop and bounce alike.
        ///
        /// Deliberately not gated on the obstacle module. Its whole value during the
        /// module's own smoke test is that the same instrument reads the vanilla tract
        /// and the modelled one, so "before" and "after" are the same measurement.
        ///
        /// A postfix on HandleCollision runs after CreateFragments, which is what makes
        /// the outgoing state readable at all: the child exists, the bullet state is
        /// final, and the obstacle model's verdict for this collision is still on its
        /// slot.
        /// </summary>
        private static void ObstacleHitPostfix(EftBulletClass __instance)
        {
            PatchStats.Hit($"overlay:{nameof(ObstacleHitPostfix)}");
            var mode = PlateClientConfig.ObstacleLog.Value;
            var wantSurvey = mode == ObstacleLogMode.Aggregated;

            // Neither obstacle journal answers to the overlay's own live toggle. The
            // survey never did, on the argument that turning the overlay off mid-walk to
            // save frames must not silently stop a measurement — and the per-hit line
            // did, for no reason anyone wrote down. The raid that found it produced 1 142
            // model lines and ZERO engine lines, so the one comparison the two channels
            // exist for ("what the model decided" against "what the game then did") could
            // not be made at all.
            var wantLine = mode == ObstacleLogMode.EveryHit;
            if (Off && !wantSurvey && !wantLine)
            {
                return;
            }

            try
            {
                var collider = __instance.HittedBallisticCollider;
                if (collider == null || collider is BodyPartCollider)
                {
                    return; // bodies have their own marker, on the damage event
                }

                if (!LocalPlayerRef.IsShooter(__instance.PlayerProfileID))
                {
                    return; // the spec is the player's own shots, and a firefight is loud
                }

                // The far face of a crossing the model already charged. Both instruments
                // fire on every COLLISION, and without this they report a free exit as a
                // second, full-price crossing: two markers six centimetres apart, both
                // labelled with the whole thickness, and n=8 on a door four shots went
                // through.
                var freeExit = ObstaclePatches.FreeExitThisHit(__instance, collider);

                if (wantSurvey)
                {
                    var got = ObstaclePatches.TryMeasureThicknessMm(__instance, out var chord);
                    ObstacleSurvey.Note(
                        Singleton<GameWorld>.Instance?.LocationId ?? "?",
                        NameOfObject(collider),
                        ParentChain(collider),
                        ObstaclePatches.MaterialOf(collider),
                        collider.PenetrationLevel,
                        chord, __instance.Float_3, got,
                        Time.time, freeExit);
                }

                var wantMarker = !Off && PlateClientConfig.MarkersEnabled.Value;
                if (!wantMarker && !wantLine)
                {
                    return;
                }

                var state = __instance.BulletState.ToString();
                var stopped = state == nameof(EftBulletClass.EBulletState.StopHit);
                var ricochet = state == nameof(EftBulletClass.EBulletState.RicochetHit);

                var vIn = __instance.Vector3_1.magnitude;
                var dirIn = __instance.Vector3_1.sqrMagnitude > 1e-8f
                    ? __instance.Vector3_1.normalized
                    : __instance.Direction;

                var child = __instance.Fragments.Count > 0
                    ? __instance.Fragments[__instance.Fragments.Count - 1]
                    : null;

                float? vOut = null;
                float? devDeg = null;
                if (child != null)
                {
                    vOut = child.Vector3_1.magnitude;
                    if (child.Vector3_1.sqrMagnitude > 1e-8f)
                    {
                        devDeg = Vector3.Angle(dirIn, child.Vector3_1.normalized);
                    }
                }

                if (wantLine)
                {
                    // into the obstacle file, next to the model's own line for the same
                    // collision and never into the event journals: walls were most of
                    // those by volume and the event journal is about what happens to
                    // bodies. Sitting in the same file is also what makes the two
                    // readings of one collision comparable at a glance.
                    ObstacleSurvey.LogLine(WallJournal.Line(
                        ObstaclePatches.MaterialOf(collider),
                        NameOfObject(collider),
                        collider.PenetrationLevel,
                        collider.PenetrationChance,
                        collider.RicochetChance,
                        collider.FragmentationChance,
                        BallisticsPatches.AmmoLabel(__instance),
                        vIn, vOut, devDeg,
                        WallJournal.EffectOf(state, ObstaclePatches.DeformedThisHit(__instance)),
                        !stopped && !ricochet,
                        ParentChain(collider), HitFeed.ShotId(__instance), freeExit));
                }

                if (wantMarker)
                {
                    // What it was and how much of it there was. The verdict is already in
                    // the colour — green through, red stopped, yellow bounced — so
                    // spelling it out again in the label wastes the one line of text the
                    // marker gets. What the colour cannot say is the number the whole
                    // decision rests on: the thickness the model charged for, which for a
                    // shell is its wall and NOT the chord through the object. Showing the
                    // chord there reads as a contradiction — 900 mm of tyre with a hole
                    // through it — and the marker is meant to explain the verdict.
                    var text = ObstaclePatches.MaterialOf(collider);
                    if (freeExit)
                    {
                        // no thickness at all here, because none was charged: the label
                        // used to repeat the entry face's figure and read as a second
                        // full-price crossing of the same wall. One letter rather than a
                        // word — the marker gets one line, and this one has to be
                        // readable next to a neighbour that does carry a number
                        text += " F";
                    }
                    else if (ObstaclePatches.TryThicknessUsedMm(__instance, collider, out var mm,
                                 out var measured))
                    {
                        // where it came from, in one character: the book and the scene
                        // disagreeing is the thing being debugged
                        text += $" {mm:0.#}mm{(measured ? "" : "*")}";
                    }

                    var color = freeExit
                        ? HitMarkers.WallFreeExit
                        : ricochet
                            ? HitMarkers.WallRicochet
                            : stopped
                                ? HitMarkers.WallStopped
                                : HitMarkers.WallPenetrated;
                    HitMarkers.Add(__instance.HitPoint, dirIn, text, color);
                }
            }
            catch (Exception ex)
            {
                LogPatchError(nameof(ObstacleHitPostfix), ex);
            }
        }

        /// <summary>
        /// Which object in the scene this was. The whole point of the journal is that a
        /// collider carries whatever numbers its designer gave it, so the line has to
        /// say which door on which map produced them.
        /// </summary>
        private static string NameOfObject(BallisticCollider collider)
        {
            try
            {
                return collider.gameObject != null ? collider.gameObject.name : "?";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// The collider's ancestry, immediate parent first, two levels. Needed because
        /// half the maps name the collider itself nothing at all — Factory writes
        /// `Cistern_01_A_BALLISTIC_metalthick`, Shoreline writes `metal` — and the
        /// prop's real name then lives a transform or two up. Two levels rather than
        /// one for the same reason: the immediate parent is often a grouping node
        /// ("Colliders"), and the name after it is the one the survey is for.
        /// </summary>
        private static string ParentChain(BallisticCollider collider)
        {
            try
            {
                var t = collider.transform != null ? collider.transform.parent : null;
                if (t == null)
                {
                    return "-";
                }

                var chain = t.name;
                if (t.parent != null)
                {
                    chain += "/" + t.parent.name;
                }

                return chain;
            }
            catch
            {
                return "-";
            }
        }

        private static void BulletOverpenPostfix(EftBulletClass __instance)
        {
            PatchStats.Hit($"overlay:{nameof(BulletOverpenPostfix)}");
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
            PatchStats.Hit($"overlay:{nameof(BulletFragmentPostfix)}");
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