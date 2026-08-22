using System;
using System.Collections.Generic;
using EFT;
using PLATE.Client.Ballistics;
using UnityEngine;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// Winded — applies WindedModel to a participant. The player side is entirely
    /// vanilla machinery: both stamina pools are drained through their own public
    /// setter and restoration is held with the game's own DisableRestoration
    /// deadline, so heavy breathing, sway and the sprint gate all follow by
    /// themselves. A bot does not run on stamina, so it gets the mover's
    /// sprint-pause deadline (same brake CrippleSystem uses), and a blow that
    /// saturates the ramp hands it to DisorientationSystem — the custom fall-back +
    /// mag-dump effect that replaced the vanilla flash and its side effects.
    ///
    /// A volley is one blow: pellets landing in the same frame accumulate into a
    /// single severity, and each call upgrades the already-applied drain to the new
    /// total instead of stacking eight small hits multiplicatively.
    /// </summary>
    internal static class WindedSystem
    {
        private class State
        {
            public int Frame;
            public float Joules;
            public float AppliedT;
        }

        private static readonly Dictionary<string, State> States =
            new Dictionary<string, State>();

        /// <summary>Per-raid reset (called from PlateBloodManager.Clear).</summary>
        public static void Clear()
        {
            States.Clear();
        }

        /// <summary>
        /// A blunt insult to the torso, J: behind-armour energy for a blocked hit,
        /// the temporary-cavity deposit for a penetrating one. The caller has already
        /// answered "is this the torso" — this method answers "how winded". The
        /// attacker is who the disoriented bot will believe it is shooting back at.
        /// </summary>
        public static void OnTorsoImpact(Player victim, Player attacker, float joules,
            Vector3 hitPoint)
        {
            if (joules <= 0f || victim == null || victim.ProfileId == null)
            {
                return;
            }

            var ahc = victim.ActiveHealthController;
            if (ahc == null || !ahc.IsAlive)
            {
                return;
            }

            if (!PlateBloodManager.CategoryOn(victim,
                    PlateClientConfig.WindedPlayer.Value,
                    PlateClientConfig.WindedPmc.Value,
                    PlateClientConfig.WindedScav.Value))
            {
                return;
            }

            var tun = Tuning();
            if (!States.TryGetValue(victim.ProfileId, out var s))
            {
                s = new State();
                States[victim.ProfileId] = s;
            }

            if (s.Frame != Time.frameCount)
            {
                s.Frame = Time.frameCount;
                s.Joules = 0f;
                s.AppliedT = 0f;
            }

            s.Joules += joules;
            var t = WindedModel.Severity(s.Joules, tun);
            if (t <= s.AppliedT)
            {
                return;
            }

            try
            {
                Apply(victim, attacker, s.AppliedT, t, tun);
                s.AppliedT = t;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] Winded: apply failed: {ex.Message}");
            }
        }

        private static void Apply(Player victim, Player attacker, float appliedT,
            float t, WindedModel.Tuning tun)
        {
            var lockSec = WindedModel.LockSec(t, tun);
            var factor = WindedModel.UpgradeFactor(appliedT, t);

            var phys = victim.Physical;
            if (phys != null)
            {
                Drain(phys.Stamina, factor, lockSec);
                Drain(phys.HandsStamina, factor, lockSec);
            }

            var line = $"  winded t={t:0.00}: stamina x{WindedModel.StaminaFactor(t):0.00}" +
                       $", lock {lockSec:0.#} s";

            var bot = victim.AIData?.BotOwner;
            if (bot != null)
            {
                // the mover's own deadline brake, exactly as CrippleSystem rides it;
                // Sprint(false) on top stops a sprint already running
                bot.Mover?.Sprint(false, false);
                bot.Mover?.SprintPause(lockSec);

                // a blow that saturated the ramp scrambles the bot outright — our own
                // disorientation (fall back + mag-dump at the remembered shooter), not
                // the vanilla flash with its group-wide side effects
                var disorientSec = PlateClientConfig.WindedDisorientSec.Value;
                if (PlateClientConfig.WindedDisorientEnabled.Value && t >= 1f &&
                    DisorientationSystem.Start(victim, attacker, disorientSec))
                {
                    line += $", disoriented {disorientSec:0.#} s";
                }
            }

            Overlay.HitFeed.PushHit(victim, line);
        }

        /// <summary>
        /// Drain one vanilla stamina pool and hold its restoration. UpdateStamina is
        /// the game's own change path (events, exhaustion threshold), and
        /// DisableRestoration is the game's own downtime deadline — Process() refuses
        /// to restore until Time.time passes it, no patch required.
        /// </summary>
        // GClass774 is the game's stamina pool (Physical.Stamina/HandsStamina): 4.0 is
        // obfuscated, so the type has no name of its own to compile against.
        private static void Drain(GClass774 pool, float factor, float lockSec)
        {
            if (pool == null)
            {
                return;
            }

            pool.UpdateStamina(pool.Current * factor);
            pool.DisableRestoration = Mathf.Max(pool.DisableRestoration,
                Time.time + lockSec);
        }

        private static WindedModel.Tuning Tuning()
        {
            return new WindedModel.Tuning
            {
                OnsetJ = PlateClientConfig.WindedOnsetJ.Value,
                FullJ = PlateClientConfig.WindedFullJ.Value,
                MaxLockSec = PlateClientConfig.WindedMaxLockSec.Value,
            };
        }
    }
}
