using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// Custom disorientation for a bot at full winded severity — our own effect in
    /// place of the vanilla flashbang, whose machinery does far more than blind:
    /// it wipes the bot's enemy memory and posts a search point for its whole
    /// group, and raid testing had entire groups standing dazed at walls. This one
    /// owns exactly two behaviours and nothing else: the bot falls back away from
    /// the shooter, and — only if it could SEE the shooter at the moment of the
    /// blow — mag-dumps at where it believes the shooter is: a point drawn once on
    /// a circle around the shooter's torso, sprayed around between trigger pulls,
    /// until the effect expires or the bot dies. The trigger stays pressed through
    /// the bot's own Shoot() path, so reloads, weapon readiness and fire modes are
    /// all its own logic. Hit from an unseen direction, it only falls back.
    /// </summary>
    internal static class DisorientationSystem
    {
        private class State
        {
            public Player Bot;
            public Player Attacker;
            public float Until;

            /// <summary>Where the bot believes the shooter is; drawn once. Only set
            /// when the bot could actually SEE the shooter at the moment of the blow —
            /// a bot hit from an unseen direction has nothing to dump a mag at and
            /// only falls back.</summary>
            public Vector3 AimAnchor;

            public bool HasAnchor;
            public float NextRetreat;
        }

        /// <summary>
        /// How often the retreat point is renewed, s. A deadline like the bot's own
        /// sprint brake: the mover forgets orders, so the order is repeated.
        /// </summary>
        private const float RetreatRenewSec = 1f;

        /// <summary>Chest height of a standing man over his feet, m — the circle the
        /// mag-dump is drawn on is centred on the torso, not the floor.</summary>
        private const float TorsoUpM = 1.2f;

        private static readonly Dictionary<string, State> Active =
            new Dictionary<string, State>();

        private static readonly System.Random Rng =
            new System.Random(Environment.TickCount);

        /// <summary>Per-raid reset (called from PlateBloodManager.Clear).</summary>
        public static void Clear()
        {
            Active.Clear();
        }

        /// <summary>
        /// Starts (or refreshes) the disorientation. False when there is nothing to
        /// disorient: not a bot, no shooter to believe anything about, zero duration.
        /// The mag-dump anchor is drawn only when the bot can SEE the shooter at this
        /// moment; hit from an unseen direction it just falls back.
        /// </summary>
        public static bool Start(Player bot, Player attacker, float seconds)
        {
            var owner = bot?.AIData?.BotOwner;
            if (seconds <= 0f || attacker == null ||
                bot.ProfileId == null || owner == null)
            {
                return false;
            }

            var state = new State
            {
                Bot = bot,
                Attacker = attacker,
                Until = Time.time + seconds,
            };

            if (SeesAttacker(owner, attacker))
            {
                var angle = Rng.NextDouble() * 2.0 * Math.PI;
                var center = attacker.Position + Vector3.up * TorsoUpM;
                var radius = PlateClientConfig.DisorientAimRadiusM.Value;
                state.AimAnchor = center + new Vector3(
                    (float)Math.Cos(angle), 0f, (float)Math.Sin(angle)) * radius;
                state.HasAnchor = true;
            }

            Active[bot.ProfileId] = state;
            return true;
        }

        /// <summary>Whether the bot's own vision currently has the shooter.</summary>
        private static bool SeesAttacker(BotOwner owner, Player attacker)
        {
            try
            {
                var infos = owner.EnemiesController?.EnemyInfos;
                return infos != null && infos.TryGetValue(attacker, out var info) &&
                       info != null && info.IsVisible;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Frame tick (DisorientationTicker). Removes what expired.</summary>
        public static void Tick()
        {
            if (Active.Count == 0)
            {
                return;
            }

            List<string> done = null;
            foreach (var kv in Active)
            {
                if (!TickOne(kv.Value))
                {
                    (done = done ?? new List<string>()).Add(kv.Key);
                }
            }

            if (done != null)
            {
                foreach (var key in done)
                {
                    Active.Remove(key);
                }
            }
        }

        private static bool TickOne(State s)
        {
            var owner = s.Bot?.AIData?.BotOwner;
            var ahc = s.Bot?.ActiveHealthController;
            if (owner == null || ahc == null || !ahc.IsAlive)
            {
                return false; // dead or despawned: nothing to release
            }

            if (Time.time >= s.Until)
            {
                try
                {
                    owner.ShootData?.EndShoot(); // effect over: finger off the trigger
                }
                catch
                {
                    // a despawning bot mid-frame is not worth a log line
                }

                return false;
            }

            try
            {
                // fall back: straight away from the shooter, renewed on a deadline
                if (Time.time >= s.NextRetreat)
                {
                    s.NextRetreat = Time.time + RetreatRenewSec;
                    var away = s.Bot.Position -
                               (s.Attacker != null ? s.Attacker.Position : s.AimAnchor);
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-4f)
                    {
                        away = Vector3.forward; // shooter on top of the bot: any way out
                    }

                    var target = s.Bot.Position +
                                 away.normalized * PlateClientConfig.DisorientRetreatM.Value;
                    owner.Mover?.GoToPoint(target, false,
                        PlateClientConfig.DisorientRetreatM.Value * 0.5f,
                        false, false, true);
                }

                // mag-dump: the anchor plus a fresh scatter every frame — a panic fan
                // around where the shooter was, not a beam into one spot. Only when
                // the bot saw the shooter at the moment of the blow (HasAnchor).
                if (s.HasAnchor)
                {
                    var spray = PlateClientConfig.DisorientSprayM.Value;
                    var aim = s.AimAnchor + UnityEngine.Random.insideUnitSphere * spray;
                    owner.AimingManager?.CurrentAiming?.SetTarget(aim);
                    owner.ShootData?.Shoot();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Disorientation tick failed: {ex.Message}");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The frame driver. A separate component rather than a ride on
    /// BloodSystemComponent: the trigger lives in the ballistics module and must not
    /// depend on the blood one being enabled.
    /// </summary>
    internal sealed class DisorientationTicker : MonoBehaviour
    {
        private void Update()
        {
            DisorientationSystem.Tick();
        }
    }
}
