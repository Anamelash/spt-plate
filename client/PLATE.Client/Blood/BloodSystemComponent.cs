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
        private GUIStyle _hudStyle;

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
            PerfTrace.Report(Time.time);
        }

        private void OnGUI()
        {
            if (!_inRaid || !PlateClientConfig.BloodEnabled.Value ||
                !PlateClientConfig.BloodHudVisible.Value)
            {
                return;
            }

            var tg = PerfTrace.Begin();
            try
            {
                DrawHud();
            }
            finally
            {
                PerfTrace.End("blood.hud", tg);
            }
        }

        private void DrawHud()
        {

            var gw = Singleton<GameWorld>.Instance;
            var me = gw?.MainPlayer;
            if (me == null)
            {
                return;
            }

            var s = PlateBloodManager.Get(me.ProfileId);
            var frac = s != null ? s.Cur / s.Max : 1f;
            var bp = s != null ? PlateBloodManager.PressurePct(s) : 100f;

            _hudStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            var color = frac > 0.85f ? new Color(0.85f, 0.9f, 0.85f)
                : frac > 0.7f ? new Color(1f, 0.85f, 0.5f)
                : frac > 0.6f ? new Color(1f, 0.55f, 0.3f)
                : new Color(1f, 0.25f, 0.25f);

            var text = $"BP {bp:0}%";
            var rect = new Rect(12f, Screen.height - 96f, 260f, 20f);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, _hudStyle);
            GUI.color = color;
            GUI.Label(rect, text, _hudStyle);
            GUI.color = Color.white;
        }
    }
}
