using Comfort.Common;
using EFT;
using UnityEngine;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// Blood system ticker (raid lifecycle) + the HUD for your own blood volume.
    /// </summary>
    internal class BloodSystemComponent : MonoBehaviour
    {
        private bool _inRaid;
        private bool _synced;
        private string _mainId;
        private float _nextScan;
        private readonly BloodHudView _hud = new BloodHudView();

        private void Update()
        {
            if (!PlateClientConfig.BloodEnabled.Value)
            {
                return;
            }

            var gw = Singleton<GameWorld>.Instance;
            if (gw == null || gw.MainPlayer == null)
            {
                if (_inRaid)
                {
                    // raid end: save your blood to the profile (death = reset to full)
                    _inRaid = false;
                    var s = PlateBloodManager.Get(_mainId);
                    if (s != null && _synced)
                    {
                        BloodSync.Push(s.Cur, s.Max, s.Dead);
                    }

                    _synced = false;
                    _mainId = null;
                    PlateBloodManager.Clear();
                    _hud.Destroy();
                    _controlLogged = false;
                }

                return;
            }

            _inRaid = true;
            _mainId = gw.MainPlayer.ProfileId;

            if (!_synced)
            {
                // raid start: pull the saved volume from the profile
                _synced = true;
                var state = PlateBloodManager.GetOrCreate(gw.MainPlayer);
                var saved = BloodSync.GetCached();
                if (state != null && saved != null)
                {
                    state.Max = (float)saved.Max;
                    state.Cur = Mathf.Clamp((float)saved.Cur, 0f, state.Max);
                    Plugin.Log.LogInfo($"[PLATE] Blood restored from profile: " +
                                       $"{state.Cur:0}/{state.Max:0} ml");
                }
            }

            // register everyone alive: cripples/blood must also work for those
            // we did not shoot (or who got caught in an explosion)
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 2f;
                foreach (var p in gw.AllAlivePlayersList)
                {
                    PlateBloodManager.GetOrCreate(p);
                }
            }

            var t = PerfTrace.Begin();
            PlateBloodManager.TickAll(Time.deltaTime);
            PerfTrace.End("blood.tickall", t);

            var th = PerfTrace.Begin();
            _hud.Build();
            _hud.Tick(InControl() ? PlateBloodManager.Get(_mainId) : null);
            PerfTrace.End("blood.hud", th);

            PerfTrace.Report(Time.time);
        }

        private bool _controlLogged;

        /// <summary>
        /// Whether the player actually has their character, rather than watching the
        /// deploy screen with the world already built around them. The blood state
        /// exists from the moment the world does, which is several seconds too early to
        /// put a panel on screen.
        ///
        /// The raid timer is what says so. Neither of the two obvious candidates does:
        /// on the deploy screen the game already reports InRaid=True and Status=Running,
        /// while GameTimer.StartDateTime is still unset — it is only stamped when the
        /// raid actually starts, which is the same moment the vanilla HUD fades in.
        ///
        /// An absent session object is treated as "in control": the panel not showing at
        /// all is a worse failure than showing a few seconds early, and this is a display
        /// gate rather than anything the model depends on.
        /// </summary>
        private bool InControl()
        {
            var game = Singleton<AbstractGame>.Instance;
            if (game == null)
            {
                return true;
            }

            var timer = game.GameTimer;
            if (!game.InRaid || timer == null || !timer.StartDateTime.HasValue)
            {
                return false;
            }

            if (!_controlLogged)
            {
                _controlLogged = true;
                Plugin.Log.LogInfo($"[PLATE] HUD shown: InRaid={game.InRaid}, " +
                                   $"status={game.Status}, timer={timer.StartDateTime}");
            }

            return true;
        }
    }
}
