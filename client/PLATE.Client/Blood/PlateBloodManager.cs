using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using PLATE.Client.Overlay;
using UnityEngine;

namespace PLATE.Client.Blood
{
    internal class BloodState
    {
        public Player Player;
        public float Cur;
        public float Max;
        public float InternalMlSec;      // internal bleedings (not stopped by a tourniquet)
        public float PendingExternalMl;  // accumulated by the bleeding patches during the frame
        public float LastExternalDrainAt; // to pause passive regeneration
        public int Tier;                  // 0..3
        public float NextEffectRefresh;
        public float NextCrippleCheck;
        public bool Crippled;
        public bool Dead;
        public bool DeathSuppressedLogged;
        public bool HasBrokenLeg;      // active leg Fracture (a splint removes it)
        public bool JumpBanned;        // broken leg / destroyed stomach / destroyed leg
        public bool TierMobilityBanned; // tier 3 hypovolemia: sprint/jump banned
        public float FallTimer;        // time moving on a broken leg before falling

        /// <summary>Vanilla LowEdgeHealth instance held by the blood system (own player only).</summary>
        public object LowEdgeHandle;
        public readonly Dictionary<EBodyPart, float> LastGuaranteedBleedAt = new Dictionary<EBodyPart, float>();

        /// <summary>Collider of the last bullet wound per body part (for the femoral artery).</summary>
        public readonly Dictionary<EBodyPart, (EBodyPartColliderType Collider, float Time)> LastHitCollider =
            new Dictionary<EBodyPart, (EBodyPartColliderType, float)>();
    }

    /// <summary>
    /// Blood state of every raid participant. NOT a health effect — a standalone
    /// manager (the game/mods clearing buffs cannot touch blood volume). Effects are
    /// used only as inputs (patched Bleeding) and outputs (threshold debuffs).
    /// </summary>
    internal static class PlateBloodManager
    {
        private static readonly Dictionary<string, BloodState> States =
            new Dictionary<string, BloodState>();

        private static MethodInfo _addTremor;
        private static MethodInfo _addTunnelVision;

        public static BloodState GetOrCreate(Player player)
        {
            if (player?.ProfileId == null)
            {
                return null;
            }

            if (!States.TryGetValue(player.ProfileId, out var s))
            {
                s = new BloodState
                {
                    Player = player,
                    Max = PlateClientConfig.BloodMaxMl.Value,
                    Cur = PlateClientConfig.BloodMaxMl.Value,
                };
                States[player.ProfileId] = s;
            }

            return s;
        }

        public static BloodState Get(string profileId)
        {
            return profileId != null && States.TryGetValue(profileId, out var s) ? s : null;
        }

        /// <summary>Flow self-limiting via hypotension: Q = Q0 * (V/Vmax)^beta.</summary>
        public static float SelfLimit(BloodState s)
        {
            return Mathf.Pow(Mathf.Clamp01(s.Cur / s.Max), PlateClientConfig.SelfLimitBeta.Value);
        }

        /// <summary>
        /// "Blood pressure", %: 100 at full volume, 0 at the death point (ATLS-based
        /// threshold from the config). One scale shared by the HUD, the Health tab
        /// and the overlay.
        /// </summary>
        public static float PressurePct(BloodState s)
        {
            var death = PlateClientConfig.DeathThreshold.Value;
            var frac = Mathf.Clamp01(s.Cur / s.Max);
            return Mathf.Clamp01((frac - death) / (1f - death)) * 100f;
        }

        /// <summary>
        /// External bleeding accumulates during the frame and is applied in the tick —
        /// this way the total loss (external + internal) is capped by cardiac output.
        /// </summary>
        public static void QueueExternalDrain(Player player, float ml)
        {
            var s = GetOrCreate(player);
            if (s == null || s.Dead)
            {
                return;
            }

            s.PendingExternalMl += ml;
            s.LastExternalDrainAt = Time.time;
        }

        public static void AddInternal(Player player, float mlSec)
        {
            var s = GetOrCreate(player);
            if (s == null || s.Dead)
            {
                return;
            }

            s.InternalMlSec += mlSec;
            HitFeed.PushPanel($"{OverlayHud.NameOf(player)} +internal bleeding {mlSec:0.#} ml/s " +
                              $"(total {s.InternalMlSec:0.#})");
        }

        /// <summary>
        /// Push request for an immediate cripple recalculation (damage, fracture,
        /// splint, surgery): Refresh runs on the next tick. The infrequent polling
        /// remains as a safety net for missed effect-removal paths.
        /// </summary>
        public static void RequestRefresh(Player player)
        {
            var s = Get(player?.ProfileId);
            if (s != null)
            {
                s.NextCrippleCheck = 0f;
            }
        }

        public static void MarkDead(string profileId)
        {
            var s = Get(profileId);
            if (s != null)
            {
                s.Dead = true;
            }
        }

        public static void Clear()
        {
            States.Clear();
            CrippleSystem.Clear();
        }

        /// <summary>
        /// Whether death from blood loss is allowed for this participant.
        /// Player = you; PMC = USEC/BEAR bots; Scav = the whole Savage side
        /// (scavs, bosses, raiders, cultists and other NPCs).
        /// </summary>
        private static bool DeathAllowed(Player p)
        {
            return CategoryOn(p, PlateClientConfig.DeathForPlayer.Value,
                PlateClientConfig.DeathForPmc.Value, PlateClientConfig.DeathForScav.Value);
        }

        /// <summary>Per-category toggle: you / PMC bots / the whole Savage side.</summary>
        public static bool CategoryOn(Player p, bool player, bool pmc, bool scav)
        {
            if (p.IsYourPlayer)
            {
                return player;
            }

            return p.Side == EPlayerSide.Savage ? scav : pmc;
        }

        /// <summary>Internal bleed rate when a body part gets destroyed.</summary>
        public static float DestroyedPartBleed(EBodyPart part)
        {
            switch (part)
            {
                case EBodyPart.Stomach:
                    return PlateClientConfig.StomachDestroyedBleed.Value;
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg:
                    return PlateClientConfig.LegDestroyedBleed.Value;
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm:
                    return PlateClientConfig.ArmDestroyedBleed.Value;
                default:
                    return 0f; // head/thorax — vanilla kills instantly anyway
            }
        }

        public static void TickAll(float dt)
        {
            foreach (var s in States.Values)
            {
                if (s.Dead || s.Player == null)
                {
                    continue;
                }

                try
                {
                    TickOne(s, dt);
                }
                catch (Exception ex)
                {
                    LogError(ex);
                }
            }
        }

        private static void TickOne(BloodState s, float dt)
        {
            // cripples: push model (RequestRefresh on damage/fracture/splint/surgery) +
            // infrequent safety-net polling; jitter desyncs bots across frames
            if (Time.time >= s.NextCrippleCheck)
            {
                s.NextCrippleCheck = Time.time + 5f + UnityEngine.Random.Range(0f, 2f);
                var t = PerfTrace.Begin();
                CrippleSystem.Refresh(s);
                ApplyStamina(s);
                PerfTrace.End("cripple.refresh", t);
            }

            // falling while moving on a broken leg — every frame (needs a precise delay)
            var tf = PerfTrace.Begin();
            CrippleSystem.TickFall(s, dt);
            PerfTrace.End("cripple.fall", tf);

            // total blood loss per frame is capped by cardiac output (~5 L/min):
            // no matter how many wounds there are, blood physically cannot drain faster
            var drain = s.PendingExternalMl + s.InternalMlSec * SelfLimit(s) * dt;
            s.PendingExternalMl = 0f;
            var cap = PlateClientConfig.CardiacOutputMlSec.Value * dt;
            s.Cur = Mathf.Max(0f, s.Cur - Mathf.Min(drain, cap));

            // passive regeneration: only after 5+ s with no external drain and no internal bleeding
            if (s.InternalMlSec <= 0f && Time.time - s.LastExternalDrainAt > 5f && s.Cur < s.Max)
            {
                s.Cur = Mathf.Min(s.Max, s.Cur + PlateClientConfig.PassiveRegenMlMin.Value / 60f * dt);
            }

            var frac = s.Cur / s.Max;
            var pinned = false;

            // death from blood loss (per category — see the Death from bleeding flags)
            if (frac <= PlateClientConfig.DeathThreshold.Value)
            {
                if (DeathAllowed(s.Player))
                {
                    s.Dead = true;
                    HitFeed.PushPanel($"{OverlayHud.NameOf(s.Player)} EXSANGUINATED " +
                                      $"({s.Cur:0}/{s.Max:0} ml)");
                    s.Player.ActiveHealthController?.Kill(EDamageType.HeavyBleeding);
                    return;
                }

                // death disabled for this category: pressure bottoms out at 0%,
                // volume is held at the threshold, tier 3 debuffs remain
                s.Cur = s.Max * PlateClientConfig.DeathThreshold.Value;
                frac = PlateClientConfig.DeathThreshold.Value;
                pinned = true;
                if (!s.DeathSuppressedLogged)
                {
                    s.DeathSuppressedLogged = true;
                    HitFeed.PushPanel($"{OverlayHud.NameOf(s.Player)} BP 0% — death disabled " +
                                      "for this category, pinned at threshold");
                }
            }

            if (!pinned)
            {
                s.DeathSuppressedLogged = false; // healed above the threshold — the next pin gets logged again
            }

            // threshold debuffs
            var tier = frac <= PlateClientConfig.ThresholdTier3.Value ? 3
                : frac <= PlateClientConfig.ThresholdTier2.Value ? 2
                : frac <= PlateClientConfig.ThresholdTier1.Value ? 1
                : 0;

            if (tier != s.Tier)
            {
                OnTierChanged(s, tier);
            }

            s.Tier = tier;
            EnforceTierMobility(s, tier);

            if (tier >= 2 && Time.time >= s.NextEffectRefresh)
            {
                s.NextEffectRefresh = Time.time + 4f;
                RefreshTierEffects(s, tier);
            }

            UpdateHeartbeat(s, tier);
        }

        /// <summary>
        /// Hypovolemic shock (tier 3): sprinting and jumping are impossible until
        /// blood volume recovers above the threshold. Re-applied every tick — the
        /// game and mods may reset the restrictions.
        /// </summary>
        private static void EnforceTierMobility(BloodState s, int tier)
        {
            var mc = s.Player?.MovementContext;
            if (mc == null)
            {
                return;
            }

            var ban = PlateClientConfig.Tier3MovementBan.Value && tier >= 3 && !s.Dead;
            if (ban)
            {
                CrippleSystem.SprintBanned.Add(mc);
                CrippleSystem.JumpBanned.Add(mc);
                mc.EnableSprint(false);
                if (!s.TierMobilityBanned)
                {
                    HitFeed.PushPanel($"{OverlayHud.NameOf(s.Player)} TIER 3: " +
                                      "sprint/jump banned (hypovolemia)");
                }

                s.TierMobilityBanned = true;
            }
            else if (s.TierMobilityBanned)
            {
                s.TierMobilityBanned = false;

                // leave alone the bans held by the cripple system (fracture/destroyed part)
                if (!s.Crippled)
                {
                    CrippleSystem.SprintBanned.Remove(mc);
                }

                if (!s.JumpBanned)
                {
                    CrippleSystem.JumpBanned.Remove(mc);
                }
            }
        }

        /// <summary>
        /// The vanilla critical-state package (LowEdgeHealth: heartbeat + desaturation)
        /// for the own player at tier >= 2. The effect's self-removal is muted by the
        /// BloodPatches.LowEdgeKeepAlivePrefix prefix while we hold the handle.
        /// </summary>
        private static void UpdateHeartbeat(BloodState s, int tier)
        {
            if (!s.Player.IsYourPlayer)
            {
                return; // screen/sound effects only exist for your own camera
            }

            var wantOn = tier >= 2 && PlateClientConfig.HeartbeatAtTier2.Value;
            if (wantOn && s.LowEdgeHandle == null)
            {
                var ahc = s.Player.ActiveHealthController;
                var effType = PatchTargets.LowEdgeHealthEffect;
                if (ahc == null || effType == null || PatchTargets.Health_AddEffect == null)
                {
                    return;
                }

                _addLowEdge ??= PatchTargets.Health_AddEffect.MakeGenericMethod(effType);
                s.LowEdgeHandle = _addLowEdge.Invoke(ahc,
                    new object[] { EBodyPart.Head, null, null, null, 1f, null });
                HitFeed.PushPanel("YOU heartbeat ON (LowEdgeHealth held by blood)");
            }
            else if (!wantOn && s.LowEdgeHandle != null)
            {
                ReleaseHeartbeat(s);
            }
        }

        private static void ReleaseHeartbeat(BloodState s)
        {
            try
            {
                _forceRemove ??= PatchTargets.EffectBase?.GetMethod("ForceRemove");
                _forceRemove?.Invoke(s.LowEdgeHandle, null);
                HitFeed.PushPanel("YOU heartbeat OFF");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] heartbeat release: {ex.Message}");
            }
            finally
            {
                s.LowEdgeHandle = null;
            }
        }

        /// <summary>Whether the blood system holds this controller's LowEdgeHealth (for the keep-alive prefix).</summary>
        public static bool IsHeldLowEdge(object effectInstance)
        {
            foreach (var s in States.Values)
            {
                if (ReferenceEquals(s.LowEdgeHandle, effectInstance))
                {
                    return true;
                }
            }

            return false;
        }

        private static MethodInfo _addLowEdge;
        private static MethodInfo _forceRemove;

        /// <summary>
        /// Single point for applying the stamina penalty: blood loss and cripples
        /// compete for one vanilla coefficient — take the worst.
        /// </summary>
        private static void ApplyStamina(BloodState s)
        {
            var ahc = s.Player?.ActiveHealthController;
            if (ahc == null)
            {
                return;
            }

            var byTier = s.Tier switch
            {
                3 => 0.45f,
                2 => 0.65f,
                1 => 0.85f,
                _ => 1f,
            };
            var byCripple = s.Crippled ? PlateClientConfig.CrippleStaminaCoeff.Value : 1f;
            ahc.SetStaminaCoeff(Mathf.Min(byTier, byCripple));
        }

        private static void OnTierChanged(BloodState s, int newTier)
        {
            var ahc = s.Player.ActiveHealthController;
            if (ahc == null)
            {
                return;
            }

            s.Tier = newTier;
            ApplyStamina(s);

            var bp = (int)PressurePct(s);
            HitFeed.PushPanel($"{OverlayHud.NameOf(s.Player)} BP {bp}% ({s.Cur:0} ml) -> tier {newTier}");
            if (!s.Player.IsYourPlayer)
            {
                HitFeed.PushFloat(s.Player.ProfileId, s.Player.Position + Vector3.up * 1.6f,
                    $"BP {bp}%", new Color(0.85f, 0.2f, 0.2f));
            }
        }

        private static void RefreshTierEffects(BloodState s, int tier)
        {
            var ahc = s.Player.ActiveHealthController;
            if (ahc == null)
            {
                return;
            }

            // ATLS class III: tremor + tunnel vision + fatigue (StaminaZero — disrupted breathing)
            AddEffect(ahc, ref _addTremor, PatchTargets.TremorEffect, 6f, 1f);
            AddEffect(ahc, ref _addTunnelVision, PatchTargets.TunnelVisionEffect, 6f,
                tier >= 3 ? 1f : 0.6f);
            if (PlateClientConfig.FatigueAtTier2.Value)
            {
                ahc.AddStaminaZeroffect(6f);
            }

            // ATLS class III-IV: continuous concussion (5 s with a 4 s refresh cycle — no gaps)
            if (tier >= 3 && PlateClientConfig.ContusionTier3Strength.Value > 0f)
            {
                ahc.DoContusion(5f, PlateClientConfig.ContusionTier3Strength.Value);
            }
        }

        /// <summary>AddEffect for protected effects via generic reflection (cached per type).</summary>
        private static void AddEffect(ActiveHealthController ahc, ref MethodInfo cache,
            Type effectType, float workTime, float strength)
        {
            if (effectType == null || PatchTargets.Health_AddEffect == null)
            {
                return;
            }

            cache ??= PatchTargets.Health_AddEffect.MakeGenericMethod(effectType);
            cache.Invoke(ahc, new object[] { EBodyPart.Head, null, workTime, null, strength, null });
        }

        private static float _lastErrorLogged;

        private static void LogError(Exception ex)
        {
            if (Time.unscaledTime - _lastErrorLogged < 5f)
            {
                return;
            }

            _lastErrorLogged = Time.unscaledTime;
            Plugin.Log.LogError($"[PLATE] Blood tick: {ex}");
        }
    }
}
