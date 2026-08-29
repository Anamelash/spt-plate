using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace PLATE.Client.Patches
{
    /// <summary>
    /// Field-survey tools (section 6, Debug): ghost mode and the player speed
    /// multiplier. Neither is gameplay — they exist so a survey walk across a map
    /// (shooting props to measure them, see ObstacleSurvey) does not have to be fought
    /// through.
    ///
    /// Ghost mode is two prefixes and a sweep, all found by decompilation rather than
    /// guessed:
    ///
    ///  - `BotsGroup.AddEnemy` is the single funnel through which anyone becomes a
    ///    bot group's enemy (spawn-time faction fill and mid-raid discovery both end
    ///    here). Refusing the player there means no bot ever targets them.
    ///  - `BotHearingSensor.OnSoundPlayed` is each bot's ear. Vanilla's own mute flag
    ///    (`AIData.IsMute`) is read-only — it proxies a map-zone property — so the ear
    ///    is covered directly. Without this, every survey shot posts a PlaceForCheck
    ///    and the map converges on the surveyor out of curiosity.
    ///  - The sweep handles what the prefixes cannot: groups that already held the
    ///    player as an enemy when the toggle went on. `RemoveEnemy` also clears every
    ///    member's memory of the player, which is what breaks off an attack in
    ///    progress.
    ///
    /// The speed multiplier scales the motion vector in
    /// `MovementContext.DirectApplyMotion` — the one place displacement is actually
    /// handed to the CharacterController, which vanilla's own animation-speed clamps
    /// sit upstream of. The controller's own SpeedLimit is lifted while the multiplier
    /// is engaged (the same `-1` vanilla uses for platform motion) and restored when it
    /// returns to 1. Bots pass through untouched: everything is gated on IsYourPlayer.
    ///
    /// Both hooks are applied unconditionally, like SurvivabilityPatches, and gated at
    /// runtime by their F12 values: flipping the toggle mid-raid works the way every
    /// other setting in the mod does.
    /// </summary>
    internal static class GhostPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchSafe(harmony, PatchTargets.Bots_AddEnemy, nameof(AddEnemyPrefix));
            PatchSafe(harmony, PatchTargets.Bots_HearSound, nameof(HearPrefix));
            PatchSafe(harmony, PatchTargets.Player_DirectApplyMotion, nameof(MotionPrefix));
        }

        private static void PatchSafe(Harmony harmony, MethodBase target, string name)
        {
            if (target == null)
            {
                PatchStats.MarkFailed(null, Label(name), "target not resolved");
                Plugin.Log.LogError($"[PLATE] Ghost: target for {name} not resolved, skipped");
                return;
            }

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(GhostPatches), name));
                PatchStats.Track(harmony, target, Label(name));
            }
            catch (Exception ex)
            {
                PatchStats.MarkFailed(target, Label(name), ex.Message);
                Plugin.Log.LogError($"[PLATE] Ghost: failed to patch {target.Name}: {ex.Message}");
            }
        }

        private static string Label(string name)
        {
            return "ghost:" + name;
        }

        private static bool GhostOn => PlateClientConfig.GhostMode.Value;

        /// <summary>The player never becomes anyone's enemy while ghosted.</summary>
        private static bool AddEnemyPrefix(IPlayer person, ref bool __result)
        {
            PatchStats.Hit(Label(nameof(AddEnemyPrefix)));
            if (!GhostOn || person == null || !person.IsYourPlayer)
            {
                return true;
            }

            __result = false;
            return false;
        }

        /// <summary>And no bot hears a sound the player made.</summary>
        private static bool HearPrefix(IPlayer player)
        {
            PatchStats.Hit(Label(nameof(HearPrefix)));
            return !GhostOn || player == null || !player.IsYourPlayer;
        }

        // --- Speed ---

        // whose CharacterController speed limit we lifted, and what it was: the limit
        // must go back exactly where it was when the multiplier returns to 1, and a
        // fresh raid builds a fresh context, so the reference doubles as the reset
        private static MovementContext _liftedOn;
        private static float _savedLimit;

        private static void MotionPrefix(MovementContext __instance, ref Vector3 motion)
        {
            PatchStats.Hit(Label(nameof(MotionPrefix)));
            try
            {
                // context → player is protected, but player → context is public, and
                // only one context on the map can be the main player's
                var main = Singleton<GameWorld>.Instance?.MainPlayer;
                if (main == null || !ReferenceEquals(main.MovementContext, __instance))
                {
                    return;
                }

                var mult = PlateClientConfig.PlayerSpeedMult.Value;
                if (mult > 0.999f && mult < 1.001f)
                {
                    if (ReferenceEquals(_liftedOn, __instance))
                    {
                        __instance.CharacterController.SpeedLimit = _savedLimit;
                        _liftedOn = null;
                    }

                    return;
                }

                if (!ReferenceEquals(_liftedOn, __instance))
                {
                    _savedLimit = __instance.CharacterController.SpeedLimit;
                    _liftedOn = __instance;
                    // -1 is vanilla's own "no limit" — DirectApplyMotion uses it for
                    // platform motion, so the controller is guaranteed to honour it
                    __instance.CharacterController.SpeedLimit = -1f;
                }

                motion *= mult;
            }
            catch
            {
                // debug tool: never let it take the movement tract down with it
            }
        }

        // --- The sweep ---

        private static float _nextSweep;
        private static bool _wasOn;

        /// <summary>
        /// Periodic while ghosted: removes the player from every bot group that already
        /// counts them an enemy. The prefixes stop new additions; this clears the state
        /// that predates the toggle, and keeps clearing in case some path slips one in.
        /// </summary>
        public static void Tick(float now)
        {
            var on = GhostOn;
            var flipped = on && !_wasOn;
            _wasOn = on;
            if (!on || (!flipped && now < _nextSweep))
            {
                return;
            }

            _nextSweep = now + 2f;
            try
            {
                var main = Singleton<GameWorld>.Instance?.MainPlayer;
                var bots = Singleton<IBotGame>.Instance?.BotsController?.Bots;
                if (main == null || bots == null)
                {
                    return;
                }

                foreach (var bot in bots.BotOwners)
                {
                    if (bot == null || bot.IsDead)
                    {
                        continue;
                    }

                    if (bot.BotsGroup != null && bot.BotsGroup.Enemies.ContainsKey(main))
                    {
                        bot.BotsGroup.RemoveEnemy(main);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PLATE] ghost sweep failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The heartbeat for the ghost sweep. Its own component rather than OverlayHud's
    /// Update on purpose: the overlay is a module with a toggle, and a debug tool that
    /// silently dies when an unrelated module is off is the kind of coupling this
    /// codebase keeps paying for.
    /// </summary>
    internal sealed class GhostTicker : MonoBehaviour
    {
        private void Update()
        {
            GhostPatches.Tick(Time.time);

            // the obstacle file's heartbeat as well: its writers include
            // ObstaclePatches, which runs whether the overlay module is on or not, so
            // its flushing cannot live on OverlayHud alone (double-ticking is fine —
            // the flush is paced internally)
            Overlay.ObstacleSurvey.Tick(Time.time);
        }
    }
}
