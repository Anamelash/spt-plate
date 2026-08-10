using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using PLATE.Client.Overlay;
using UnityEngine;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// What opened an internal bleed. Every source here is a vessel inside a body
    /// cavity: no dressing, tourniquet or hemostatic reaches those, which is what
    /// separates them from the external bleeds vanilla already models.
    /// </summary>
    internal enum EInternalBleedSource
    {
        /// <summary>Cavity vessels of a destroyed body part (stomach: aorta / vena cava).</summary>
        PartDestroyed,

        /// <summary>Behind-armor blunt trauma: vessels torn under an intact plate.</summary>
        Babt,

        /// <summary>Blast barotrauma.</summary>
        Blast,

        /// <summary>
        /// A solid organ opened up. The liver is the case it exists for: the
        /// retrohepatic vena cava runs through it, which is why its wounds kill — there
        /// is nothing to press on.
        /// </summary>
        Organ,
    }

    /// <summary>
    /// One internal bleed, kept per causing hit rather than merged into a running
    /// total. A single number cannot answer the only question a bug report ever
    /// asks — where is this blood going.
    /// </summary>
    internal struct InternalBleed
    {
        public EInternalBleedSource Source;

        /// <summary>Body part, when the source works in those terms (a destroyed part, a blast).</summary>
        public EBodyPart? Part;

        /// <summary>Hit zone, when the source knows one (BABT).</summary>
        public EBodyPartColliderType? Collider;
        public float MlSec;
        public float StartedAt;

        /// <summary>
        /// Whichever of the two the source actually knew. Neither is derived from the
        /// other: the collider-to-part mapping is the game's, not ours, and a guess here
        /// would put invented anatomy into the one line a bug report is read from.
        /// </summary>
        public string Zone =>
            Collider.HasValue ? Collider.Value.ToString()
            : Part.HasValue ? Part.Value.ToString()
            : "?";

        public override string ToString() => $"{MlSec:0.#} ml/s {Source} ({Zone})";
    }

    internal class BloodState
    {
        public Player Player;
        public float Cur;
        public float Max;

        /// <summary>Open internal bleeds, one entry per causing hit. No field medicine closes these.</summary>
        public readonly List<InternalBleed> InternalBleeds = new List<InternalBleed>();

        /// <summary>Cached sum over <see cref="InternalBleeds"/> — the tick reads it every frame.</summary>
        public float InternalMlSec;
        /// <summary>
        /// Accumulated by the bleeding patches during the frame, kept apart by region.
        /// Not for the arithmetic — the total is all the drain needs — but for the one
        /// question the whole zone design has to answer: is 67% of the blood coming out
        /// of the torso, the way it does in the combat mortality data.
        /// </summary>
        public readonly float[] PendingExternalMl = new float[4];
        public float LastExternalDrainAt; // to pause passive regeneration

        /// <summary>
        /// Blood actually leaving the body, ml/s, smoothed. The open bleeds' rates do not
        /// answer this on their own: they are throttled by hypotension and capped by
        /// cardiac output, and external bleeding arrives in per-frame bursts. What the
        /// HUD needs is what came out, so that is what is measured.
        /// </summary>
        public float DrainMlSec;

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
        /// The next threshold below the current tier, as a fraction of maximum volume,
        /// with the tag the HUD prints for it. Below tier 3 there is nothing left but the
        /// death point, so that is what the countdown runs to.
        /// </summary>
        public static void NextThreshold(int tier, out float fraction, out string label)
        {
            switch (tier)
            {
                case 0:
                    fraction = PlateClientConfig.ThresholdTier1.Value;
                    label = "T1";
                    return;
                case 1:
                    fraction = PlateClientConfig.ThresholdTier2.Value;
                    label = "T2";
                    return;
                case 2:
                    fraction = PlateClientConfig.ThresholdTier3.Value;
                    label = "T3";
                    return;
                default:
                    fraction = PlateClientConfig.DeathThreshold.Value;
                    label = "OUT";
                    return;
            }
        }

        /// <summary>
        /// External bleeding accumulates during the frame and is applied in the tick —
        /// this way the total loss (external + internal) is capped by cardiac output.
        /// </summary>
        public static void QueueExternalDrain(Player player, float ml,
            Ballistics.BleedRegion region)
        {
            var s = GetOrCreate(player);
            if (s == null || s.Dead)
            {
                return;
            }

            var i = (int)region;
            s.PendingExternalMl[i >= 0 && i < s.PendingExternalMl.Length ? i : 0] += ml;
            s.LastExternalDrainAt = Time.time;
        }

        /// <summary>
        /// Whether internal bleeding applies to this participant. Internal bleeds
        /// cannot be closed by any field medicine, so this is the switch for anyone
        /// who does not want that in their raids.
        /// </summary>
        public static bool InternalAllowed(Player p)
        {
            return CategoryOn(p, PlateClientConfig.InternalBleedPlayer.Value,
                PlateClientConfig.InternalBleedPmc.Value,
                PlateClientConfig.InternalBleedScav.Value);
        }

        public static void AddInternal(Player player, float mlSec, EInternalBleedSource source,
            EBodyPart? part = null, EBodyPartColliderType? collider = null)
        {
            var s = GetOrCreate(player);
            if (s == null || s.Dead)
            {
                return;
            }

            if (!InternalAllowed(player))
            {
                return;
            }

            // section 7: the player turned the bleeding chance down, and the internal ones
            // count. Rolled here rather than at the call sites so every source — organ,
            // destroyed part, blast — goes through the same gate.
            if (!BleedRollPasses(player))
            {
                return;
            }

            var bleed = new InternalBleed
            {
                Source = source,
                Part = part,
                Collider = collider,
                MlSec = mlSec,
                StartedAt = Time.time,
            };
            s.InternalBleeds.Add(bleed);
            s.InternalMlSec += mlSec;

            HitFeed.PushPanel($"{OverlayHud.NameOf(player)} +internal bleeding {bleed} " +
                              $"(total {s.InternalMlSec:0.#} ml/s)");
        }

        /// <summary>
        /// The open internal bleeds, one line. Printed next to every threshold change so
        /// that a journal shows the cause of the drain right beside the symptom.
        /// </summary>
        public static string InternalSummary(BloodState s)
        {
            if (s == null || s.InternalBleeds.Count == 0)
            {
                return "none";
            }

            var parts = new string[s.InternalBleeds.Count];
            for (var i = 0; i < s.InternalBleeds.Count; i++)
            {
                parts[i] = s.InternalBleeds[i].ToString();
            }

            return string.Join(" + ", parts);
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

        /// <summary>
        /// Survivability override (config section 7): whether critical organ and vital-zone
        /// damage applies to this target. Only the local player can have it switched off —
        /// it is a choice about your own survivability, not a change to the model.
        /// </summary>
        public static bool OrganCritsAllowed(Player p)
        {
            return p == null || !p.IsYourPlayer || PlateClientConfig.PlayerOrganCrits.Value;
        }

        /// <summary>
        /// Survivability override (config section 7): the chance multiplier for a bleeding
        /// started by a hit on the local player. 1 for everyone else.
        /// </summary>
        public static float BleedChanceFactor(Player p)
        {
            return p != null && p.IsYourPlayer
                ? Mathf.Clamp01(PlateClientConfig.PlayerBleedChance.Value)
                : 1f;
        }

        /// <summary>
        /// The same factor as a single yes/no, for the bleedings the model applies outright
        /// rather than as a chance: the guaranteed light bleeding and the internal ones.
        /// Rolled per event, so a factor of 0.3 means three in ten of them survive.
        /// </summary>
        public static bool BleedRollPasses(Player p)
        {
            var factor = BleedChanceFactor(p);
            if (factor >= 1f)
            {
                return true;
            }

            // System.Random, not UnityEngine.Random: the latter is a native call that does
            // not exist outside the game, and a draw built on it could never be tested.
            // Same reasoning as ShotSpread.NewRng.
            return factor > 0f && BleedRng.NextDouble() < factor;
        }

        private static readonly System.Random BleedRng =
            new System.Random(System.Environment.TickCount);

        /// <summary>Per-category toggle: you / PMC bots / the whole Savage side.</summary>
        public static bool CategoryOn(Player p, bool player, bool pmc, bool scav)
        {
            if (p.IsYourPlayer)
            {
                return player;
            }

            return p.Side == EPlayerSide.Savage ? scav : pmc;
        }

        /// <summary>
        /// Same split for numeric knobs. An unidentified participant falls into the
        /// PMC bucket, matching the "everyone who is not you and not Savage" rule above.
        /// </summary>
        public static float CategoryValue(Player p, float player, float pmc, float scav)
        {
            if (p == null)
            {
                return pmc;
            }

            if (p.IsYourPlayer)
            {
                return player;
            }

            return p.Side == EPlayerSide.Savage ? scav : pmc;
        }

        /// <summary>
        /// How fast blood leaves this participant relative to the model: scales every
        /// bleed and the cardiac output cap together, so the whole loss timeline is
        /// stretched or compressed without changing which wound dominates.
        /// </summary>
        public static float BleedRateMult(Player p)
        {
            return CategoryValue(p, PlateClientConfig.BleedRatePlayer.Value,
                PlateClientConfig.BleedRatePmc.Value,
                PlateClientConfig.BleedRateScav.Value);
        }

        /// <summary>
        /// Internal bleed rate when a body part gets destroyed.
        ///
        /// Only the abdomen qualifies: its vessels (aorta, vena cava, iliacs) bleed into
        /// a cavity nothing in a med pouch can reach. A destroyed limb bleeds from the
        /// femoral or brachial bundle, which is exactly what a tourniquet or a hemostatic
        /// is for — that case is a heavy external bleed instead (see PartDestroyedPostfix),
        /// and the arterial branch of it already lives in the bleed-rate table.
        /// </summary>
        public static float DestroyedPartBleed(EBodyPart part)
        {
            // head/thorax are absent as well — vanilla kills instantly anyway
            return part == EBodyPart.Stomach ? PlateClientConfig.StomachDestroyedBleed.Value : 0f;
        }

        /// <summary>Limbs: a destroyed one bleeds externally, and can be treated.</summary>
        public static bool IsLimb(EBodyPart part)
        {
            return part == EBodyPart.LeftLeg || part == EBodyPart.RightLeg ||
                   part == EBodyPart.LeftArm || part == EBodyPart.RightArm;
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

        // --- Raid tally: the acceptance check for the whole zone design ---
        //
        // The combat mortality series this was built against measured 35.2% of deaths at
        // the moment of wounding and 52.1% over the minutes and hours that followed, and
        // of the bleeding that killed the second group, 67.3% came out of the torso
        // against 19.2% junctional and 13.5% from limbs. Those proportions are not an
        // input anywhere in the model — they are what a correctly placed set of zones
        // should produce on its own. So they get counted and printed, and if they come
        // out wrong the zones are wrong.

        private static readonly float[] DrainedByRegion = new float[4];
        private static int _deathsFromWounds;
        private static int _deathsFromBleeding;

        public static void CountDeath(bool fromBleeding)
        {
            if (fromBleeding)
            {
                _deathsFromBleeding++;
            }
            else
            {
                _deathsFromWounds++;
            }
        }

        public static System.Collections.Generic.IEnumerable<string> Report()
        {
            var deaths = _deathsFromWounds + _deathsFromBleeding;
            yield return deaths == 0
                ? "-- deaths: none"
                : $"-- deaths: {_deathsFromWounds} of wounds " +
                  $"({100f * _deathsFromWounds / deaths:0}%), " +
                  $"{_deathsFromBleeding} of blood loss " +
                  $"({100f * _deathsFromBleeding / deaths:0}%) [measured 35/52]";

            var total = 0f;
            foreach (var ml in DrainedByRegion)
            {
                total += ml;
            }

            yield return total < 1f
                ? "-- blood lost: none"
                : $"-- blood lost {total:0} ml: torso {Share(0, total)}, " +
                  $"junction {Share(1, total)}, limbs {Share(2, total)}, " +
                  $"head {Share(3, total)} [measured 67/19/13]";
        }

        private static string Share(int region, float total)
        {
            return $"{100f * DrainedByRegion[region] / total:0}%";
        }

        public static void ResetTally()
        {
            for (var i = 0; i < DrainedByRegion.Length; i++)
            {
                DrainedByRegion[i] = 0f;
            }

            _deathsFromWounds = 0;
            _deathsFromBleeding = 0;
        }

        /// <summary>
        /// Charges the blood that actually came out back to where it came from. Under the
        /// cardiac-output cap every source is throttled by the same factor, so each one's
        /// share of the loss is its share of the rate.
        /// </summary>
        private static void AttributeDrain(BloodState s, float applied, float wanted, float mult)
        {
            var scale = wanted > 1e-6f ? applied / wanted : 0f;

            var externalWanted = 0f;
            for (var i = 0; i < s.PendingExternalMl.Length; i++)
            {
                var ml = s.PendingExternalMl[i] * mult;
                externalWanted += ml;
                DrainedByRegion[i] += ml * scale;
                s.PendingExternalMl[i] = 0f;
            }

            // the internal side arrives as one number; split it across the open bleeds in
            // proportion to their rates, which is what made it that number
            var internalApplied = (wanted - externalWanted) * scale;
            if (internalApplied <= 0f || s.InternalMlSec <= 1e-6f)
            {
                return;
            }

            foreach (var bleed in s.InternalBleeds)
            {
                DrainedByRegion[(int)RegionOf(bleed)] +=
                    internalApplied * bleed.MlSec / s.InternalMlSec;
            }
        }

        /// <summary>Where an internal bleed sits, from whichever of the two the source knew.</summary>
        private static Ballistics.BleedRegion RegionOf(InternalBleed bleed)
        {
            if (bleed.Collider.HasValue)
            {
                return Ballistics.WoundBleeding.Region(bleed.Collider.Value);
            }

            switch (bleed.Part)
            {
                case EBodyPart.Head: return Ballistics.BleedRegion.Head;
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm:
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg: return Ballistics.BleedRegion.Limb;
                default: return Ballistics.BleedRegion.Torso;
            }
        }

        /// <summary>
        /// The safety-net poll interval for cripple state, jittered between these two so
        /// bots do not all recalculate on the same frame. Named because anything that has
        /// to outlive one poll — a bot's sprint ban is a deadline, not a flag — has to be
        /// able to say so in terms of this rather than by guessing a number.
        /// </summary>
        internal const float CrippleRefreshMinSec = 5f;

        internal const float CrippleRefreshMaxSec = 7f;

        private static void TickOne(BloodState s, float dt)
        {
            // cripples: push model (RequestRefresh on damage/fracture/splint/surgery) +
            // infrequent safety-net polling; jitter desyncs bots across frames
            if (Time.time >= s.NextCrippleCheck)
            {
                s.NextCrippleCheck = Time.time + CrippleRefreshMinSec +
                    UnityEngine.Random.Range(0f, CrippleRefreshMaxSec - CrippleRefreshMinSec);
                var t = PerfTrace.Begin();
                CrippleSystem.Refresh(s);
                ApplyStamina(s);
                PerfTrace.End("cripple.refresh", t);
            }

            // falling while moving on a broken leg — every frame (needs a precise delay)
            var tf = PerfTrace.Begin();
            CrippleSystem.TickFall(s, dt);
            PerfTrace.End("cripple.fall", tf);

            // read through the toggle and the multiplier rather than the stored rate, so
            // both take effect immediately in F12 instead of after the raid
            var mult = BleedRateMult(s.Player);
            var internalMlSec = InternalAllowed(s.Player) ? s.InternalMlSec * mult : 0f;

            // total blood loss per frame is capped by cardiac output (~5 L/min):
            // no matter how many wounds there are, blood physically cannot drain faster.
            // The cap is scaled by the same multiplier — otherwise a raised bleed rate
            // would do nothing for anyone already bleeding at the physiological ceiling
            var external = 0f;
            for (var i = 0; i < s.PendingExternalMl.Length; i++)
            {
                external += s.PendingExternalMl[i] * mult;
            }

            var internalMl = internalMlSec * SelfLimit(s) * dt;
            var drain = external + internalMl;
            var cap = PlateClientConfig.CardiacOutputMlSec.Value * mult * dt;
            var applied = Mathf.Min(drain, cap);
            s.Cur = Mathf.Max(0f, s.Cur - applied);

            // What actually came out, charged back to where it came from. Under the cap
            // every source is throttled equally, so the shares are the shares of the rate.
            AttributeDrain(s, applied, drain, mult);

            // The same number as a rate, low-passed for the HUD: external bleeding lands
            // in bursts, and an unsmoothed reading flickers between zero and a spike.
            var tau = PlateClientConfig.HudRateSmoothing.Value;
            var instant = dt > 1e-6f ? applied / dt : 0f;
            var alpha = tau > 1e-3f ? 1f - Mathf.Exp(-dt / tau) : 1f;
            s.DrainMlSec += (instant - s.DrainMlSec) * alpha;

            // passive regeneration: only after 5+ s with no external drain and no internal bleeding
            if (internalMlSec <= 0f && Time.time - s.LastExternalDrainAt > 5f && s.Cur < s.Max)
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
            HitFeed.PushPanel($"{OverlayHud.NameOf(s.Player)} BP {bp}% ({s.Cur:0} ml) -> tier {newTier}" +
                              (s.InternalBleeds.Count > 0 ? $", internal: {InternalSummary(s)}" : ""));
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
