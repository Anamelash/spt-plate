using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// Cripple effects from destroyed body parts: sprint ban, zeroing of the
    /// Endurance/Strength bonuses (equivalent to a rollback to level 1), speed limit.
    /// The state is recomputed from actual health once a second — it lifts itself
    /// after surgery and does not depend on events.
    /// </summary>
    internal static class CrippleSystem
    {
        /// <summary>MovementContexts banned from sprinting (read by the patches).</summary>
        public static readonly HashSet<MovementContext> SprintBanned =
            new HashSet<MovementContext>();

        /// <summary>MovementContexts banned from jumping: a leg fracture without a splint,
        /// a destroyed stomach or a destroyed leg (read by the CanJump/TryJump patches).</summary>
        public static readonly HashSet<MovementContext> JumpBanned =
            new HashSet<MovementContext>();

        /// <summary>
        /// Bots that are to stay on the ground while a leg of theirs is broken (read by
        /// the BotLay.GetUp patch). Standing up is not one decision in the AI — a dozen
        /// places call GetUp directly, from pathing to being shot at — so the ban is held
        /// here and enforced at the one point they all pass through, the same way the
        /// sprint and jump bans are held for the player.
        /// </summary>
        public static readonly HashSet<BotOwner> GroundedBots = new HashSet<BotOwner>();

        /// <summary>Whether this bot is currently held down. Null-safe: a null owner is a
        /// human player, and this ban is not theirs.</summary>
        public static bool IsGrounded(BotOwner owner)
        {
            return owner != null && GroundedBots.Contains(owner);
        }

        private static MethodInfo _findFracture;
        private static bool _findFractureBroken;

        /// <summary>Whether the part has an active fracture (a splint removes the effect — clears itself).</summary>
        public static bool HasActiveFracture(EFT.HealthSystem.ActiveHealthController ahc, EBodyPart part)
        {
            if (ahc == null || _findFractureBroken)
            {
                return false;
            }

            if (_findFracture == null)
            {
                // resolve once; on failure disable forever — no retries on every
                // tick (a repeat resolve means scanning all of the game's types)
                try
                {
                    var mi = PatchTargets.Health_FindActiveEffect;
                    var ft = PatchTargets.FractureEffect;
                    if (mi == null || ft == null)
                    {
                        _findFractureBroken = true;
                        Plugin.Log.LogError("[PLATE] Fracture poll disabled: FindActiveEffect/Fracture not resolved");
                        return false;
                    }

                    _findFracture = mi.MakeGenericMethod(ft);
                }
                catch (Exception ex)
                {
                    _findFractureBroken = true;
                    Plugin.Log.LogError($"[PLATE] Fracture poll disabled: {ex.Message}");
                    return false;
                }
            }

            try
            {
                return _findFracture.Invoke(ahc, new object[] { part }) != null;
            }
            catch
            {
                _findFractureBroken = true;
                return false;
            }
        }

        /// <summary>Skill bonus fields we zero out (SkillBuffClass.Value).</summary>
        private static readonly string[] SkillBuffFields =
        {
            "EnduranceBuffEnduranceInc", "EnduranceHands", "EnduranceBuffJumpCostRed",
            "EnduranceBuffBreathTimeInc", "EnduranceBuffRestoration",
            "StrengthBuffLiftWeightInc", "StrengthBuffSprintSpeedInc",
            "StrengthBuffJumpHeightInc", "StrengthBuffAimFatigue",
            "StrengthBuffThrowDistanceInc", "StrengthBuffMeleePowerInc",
        };

        private static FieldInfo[] _buffFields;
        private static FieldInfo _buffValueField;

        /// <summary>Snapshots of the original bonus values — for restoration after healing.</summary>
        private static readonly Dictionary<SkillManager, float[]> SavedBuffs =
            new Dictionary<SkillManager, float[]>();

        private static readonly EBodyPart[] CheckedParts =
        {
            EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm,
            EBodyPart.LeftLeg, EBodyPart.RightLeg,
        };

        /// <summary>Recompute the player's cripple state. Returns the number of destroyed parts.</summary>
        public static int Refresh(BloodState s)
        {
            var player = s.Player;
            var ahc = player?.ActiveHealthController;
            if (ahc == null)
            {
                return 0;
            }

            var destroyed = 0;
            var destroyedStomach = false;
            var destroyedLeg = false;
            foreach (var part in CheckedParts)
            {
                try
                {
                    if (ahc.GetBodyPartHealth(part, false).Current <= 0f)
                    {
                        destroyed++;
                        destroyedStomach |= part == EBodyPart.Stomach;
                        destroyedLeg |= part == EBodyPart.LeftLeg || part == EBodyPart.RightLeg;
                    }
                }
                catch
                {
                    // part unavailable — skip it
                }
            }

            // leg fractures and the jump ban (Fracture collapse category toggles)
            var collapseOn = PlateBloodManager.CategoryOn(player,
                PlateClientConfig.FractureCollapsePlayer.Value,
                PlateClientConfig.FractureCollapsePmc.Value,
                PlateClientConfig.FractureCollapseScav.Value);
            s.HasBrokenLeg = collapseOn &&
                             (HasActiveFracture(ahc, EBodyPart.LeftLeg) ||
                              HasActiveFracture(ahc, EBodyPart.RightLeg));

            var jumpBan = collapseOn && (s.HasBrokenLeg || destroyedStomach || destroyedLeg);
            var mcJump = player.MovementContext;
            if (mcJump != null)
            {
                if (jumpBan && !s.JumpBanned)
                {
                    JumpBanned.Add(mcJump);
                    Overlay.HitFeed.PushPanel($"{Overlay.OverlayHud.NameOf(player)} JUMP BANNED " +
                        $"(brokenLeg={s.HasBrokenLeg}, stomach={destroyedStomach}, leg={destroyedLeg})");
                }
                else if (!jumpBan && s.JumpBanned)
                {
                    JumpBanned.Remove(mcJump);
                }
            }

            s.JumpBanned = jumpBan;

            // a bot on a broken leg stays on the ground: getting up only to fall over a
            // step later is the part that reads as slapstick rather than injury
            var owner = player.AIData?.BotOwner;
            if (owner != null)
            {
                if (s.HasBrokenLeg && PlateClientConfig.BrokenLegGroundsBots.Value)
                {
                    GroundedBots.Add(owner);
                }
                else
                {
                    GroundedBots.Remove(owner);
                }
            }

            if (!PlateClientConfig.CrippleEnabled.Value)
            {
                if (s.Crippled)
                {
                    Release(player);
                    s.Crippled = false;
                }

                return destroyed;
            }

            var shouldBeCrippled = destroyed > 0;
            if (shouldBeCrippled)
            {
                Apply(player); // re-apply: the game/mods may reset the limits and bonuses
                if (!s.Crippled)
                {
                    Overlay.HitFeed.PushPanel(
                        $"{Overlay.OverlayHud.NameOf(player)} CRIPPLED ({destroyed} part(s) destroyed): " +
                        "sprint banned, Endurance/Strength zeroed");
                }
            }
            else if (s.Crippled)
            {
                Release(player);
                Overlay.HitFeed.PushPanel($"{Overlay.OverlayHud.NameOf(player)} cripple lifted");
            }

            s.Crippled = shouldBeCrippled;
            return destroyed;
        }

        private static void Apply(Player player)
        {
            var mc = player.MovementContext;
            if (mc != null)
            {
                SprintBanned.Add(mc);
                mc.EnableSprint(false);

                // the speed limit is read where movement is actually resolved, which both
                // sides go through, so this one knob covers everyone
                mc.AddStateSpeedLimit(PlateClientConfig.CrippleSpeedLimit.Value,
                    Player.ESpeedLimit.HealthCondition);
            }

            BanBotSprint(player);
            ZeroSkillBuffs(player.Skills);
        }

        /// <summary>
        /// A bot never asks the gates the player runs into: it decides to sprint inside its
        /// own mover, so <c>EnableSprint(false)</c> and the CanSprint patch slide straight
        /// past it — which is how a boss kept running down the map on two destroyed legs.
        ///
        /// Its own brake is <c>NoSprint</c>, and that is a deadline rather than a flag:
        /// <c>SprintPause</c> writes "no sprinting until t", and the mover consults it every
        /// time it tries to start. So the ban has to be renewed rather than set, and the
        /// renewal has to outlast the gap between two cripple recalculations — hence the
        /// margin over that interval. <c>Sprint(false)</c> on top of it stops a sprint that
        /// is already running instead of waiting for the bot to finish it.
        /// </summary>
        private static void BanBotSprint(Player player)
        {
            var mover = player.AIData?.BotOwner?.Mover;
            if (mover == null)
            {
                return;
            }

            try
            {
                mover.Sprint(false, false);
                mover.SprintPause(PlateBloodManager.CrippleRefreshMaxSec * 2f);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Cripple: bot sprint ban failed: {ex.Message}");
            }
        }

        private static void Release(Player player)
        {
            var mc = player.MovementContext;
            if (mc != null)
            {
                SprintBanned.Remove(mc);
                mc.RemoveStateSpeedLimit(Player.ESpeedLimit.HealthCondition);
            }

            // expire the deadline now rather than leaving the bot walking out the rest of
            // it after the limb it lost was put back
            var mover = player.AIData?.BotOwner?.Mover;
            if (mover != null)
            {
                try
                {
                    mover.SprintPause(0f);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[PLATE] Cripple: bot sprint release failed: {ex.Message}");
                }
            }

            RestoreSkillBuffs(player.Skills);
        }

        /// <summary>
        /// Zeroes the Endurance/Strength bonus values (the "skill rolled back to
        /// level 1" effect), first taking a snapshot of the original values for
        /// restoration after healing. Only touches the raid's runtime SkillManager
        /// object — skill progress in the profile is not affected.
        /// </summary>
        private static void ZeroSkillBuffs(SkillManager skills)
        {
            if (skills == null || !EnsureBuffReflection())
            {
                return;
            }

            try
            {
                if (!SavedBuffs.ContainsKey(skills))
                {
                    var snapshot = new float[_buffFields.Length];
                    for (var i = 0; i < _buffFields.Length; i++)
                    {
                        var buff = _buffFields[i].GetValue(skills);
                        snapshot[i] = buff != null ? (float)_buffValueField.GetValue(buff) : 0f;
                    }

                    SavedBuffs[skills] = snapshot;
                }

                foreach (var f in _buffFields)
                {
                    var buff = f.GetValue(skills);
                    if (buff != null)
                    {
                        _buffValueField.SetValue(buff, 0f);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Cripple: skill buff zeroing failed: {ex.Message}");
            }
        }

        /// <summary>Restores skill bonuses to their pre-cripple values (after surgery).</summary>
        private static void RestoreSkillBuffs(SkillManager skills)
        {
            if (skills == null || !SavedBuffs.TryGetValue(skills, out var snapshot) ||
                !EnsureBuffReflection())
            {
                return;
            }

            try
            {
                for (var i = 0; i < _buffFields.Length && i < snapshot.Length; i++)
                {
                    var buff = _buffFields[i].GetValue(skills);
                    if (buff != null)
                    {
                        _buffValueField.SetValue(buff, snapshot[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Cripple: skill buff restore failed: {ex.Message}");
            }
            finally
            {
                SavedBuffs.Remove(skills);
            }
        }

        private static bool EnsureBuffReflection()
        {
            if (_buffFields != null)
            {
                return _buffValueField != null;
            }

            var list = new List<FieldInfo>();
            foreach (var name in SkillBuffFields)
            {
                var f = typeof(SkillManager).GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    list.Add(f);
                }
            }

            _buffFields = list.ToArray();
            _buffValueField = typeof(SkillManager.SkillBuffClass).GetField("Value",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return _buffValueField != null;
        }

        /// <summary>
        /// Whether this participant is already on the ground, asked of whoever actually
        /// knows. Not of <c>MovementContext.IsInPronePose</c>: that flag is maintained by
        /// the movement state machine, and code that sets it from outside desynchronises
        /// it from the pose the body is really in — which is how the player used to fall
        /// exactly once and then walk on two broken legs forever, the flag stuck at true
        /// while the state machine had never entered prone at all.
        /// </summary>
        private static bool IsDown(Player player)
        {
            var lay = BotLayOf(player);
            return lay != null ? lay.IsLay : player.MovementContext.IsInPronePose;
        }

        /// <summary>
        /// Puts this participant on the ground through the path their own side uses, and
        /// says whether it took. A bot is not a player with a different skin: writing the
        /// prone flag at one is silently dropped (<c>set_IsInPronePose</c> returns early
        /// for a simplified skeleton), which is why they used to "collapse" on a metronome
        /// every fall-delay seconds without ever leaving their feet. They have their own
        /// lay system, and it answers whether it managed.
        /// </summary>
        private static bool TryPutDown(Player player)
        {
            _fallsAsked++;
            var lay = BotLayOf(player);
            if (lay != null)
            {
                // Not TryLay(): that is the bot's tactical decision to go prone and it
                // asks tactical questions — is there an enemy, is he far enough, has the
                // period elapsed. A broken femur is none of those, and in a close fight
                // the answer came back no four times out of five. The work itself is done
                // by the IsLay setter, which TryLay reaches only after its checks: pose to
                // zero, DoProne, the rest of it. So say what happened rather than ask.
                lay.IsLay = true;
                if (!lay.IsLay)
                {
                    return false; // the lay system refused it anyway
                }

                _fallsDone++;
                return true;
            }

            // the player goes through the movement state, so the state machine owns the
            // pose and can bring them back up again
            if (!(player.CurrentState is MovementState state))
            {
                return false;
            }

            state.Prone();
            _fallsDone++;
            return true;
        }

        private static BotLay BotLayOf(Player player)
        {
            return player.AIData?.BotOwner?.BotLay;
        }

        // asked versus happened, per raid: a collapse that is requested and quietly
        // dropped reads exactly like a collapse that worked, and that cost a raid once
        private static int _fallsAsked;
        private static int _fallsDone;

        /// <summary>Counted by the GetUp patch: how often a bot on a broken leg was kept
        /// down, and how often it was let up anyway for something worth standing for.</summary>
        public static int GetUpsBlocked;
        public static int GetUpsAllowed;

        public static IEnumerable<string> FallReport()
        {
            yield return $"-- collapses: {_fallsDone} of {_fallsAsked} requested; " +
                         $"bots kept down {GetUpsBlocked}, let up {GetUpsAllowed}";
        }

        public static void ResetFallTally()
        {
            _fallsAsked = 0;
            _fallsDone = 0;
            GetUpsBlocked = 0;
            GetUpsAllowed = 0;
        }

        /// <summary>
        /// Every frame: moving on a broken leg without a splint -> a delayed collapse
        /// to prone ("take a step — fall down"). Changing stance is allowed — but walk
        /// again and you fall again.
        /// </summary>
        public static void TickFall(BloodState s, float dt)
        {
            if (!s.HasBrokenLeg || s.Dead)
            {
                s.FallTimer = 0f;
                return;
            }

            var mc = s.Player?.MovementContext;
            if (mc == null || IsDown(s.Player))
            {
                s.FallTimer = 0f;
                return;
            }

            if (mc.SmoothedCharacterMovementSpeed < 0.25f)
            {
                s.FallTimer = 0f;
                return;
            }

            s.FallTimer += dt;
            if (s.FallTimer < PlateClientConfig.FractureFallDelay.Value)
            {
                return;
            }

            s.FallTimer = 0f;
            if (!TryPutDown(s.Player))
            {
                return; // refused — no pain, and nothing in the journal claiming a fall
            }

            var ahc = s.Player.ActiveHealthController;
            if (ahc != null)
            {
                EffectUtil.Add(ahc, PatchTargets.PainEffect, EBodyPart.Chest, 8f, 1f);
            }

            Overlay.HitFeed.PushPanel($"{Overlay.OverlayHud.NameOf(s.Player)} COLLAPSED (broken leg)");
            if (!s.Player.IsYourPlayer)
            {
                Overlay.HitFeed.PushFloat(s.Player.ProfileId,
                    s.Player.Position + UnityEngine.Vector3.up * 1.2f,
                    "COLLAPSED", new UnityEngine.Color(1f, 0.6f, 0.1f));
            }
        }

        public static void Clear()
        {
            SprintBanned.Clear();
            JumpBanned.Clear();
            GroundedBots.Clear();
            SavedBuffs.Clear();
        }
    }
}
