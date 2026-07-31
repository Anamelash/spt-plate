using Comfort.Common;
using EFT;
using UnityEngine;

namespace PLATE.Client.Overlay
{
    /// <summary>
    /// Overlay rendering (floating text + log panel) and raid lifecycle.
    /// All data comes from Harmony postfixes (OverlayPatches) — event subscriptions
    /// are not used: the vanilla EffectAddedEvent is dead in 0.16.9, and
    /// Died/PartDestroyed are caught more reliably by the Kill/DestroyBodyPart patches.
    /// </summary>
    internal class OverlayHud : MonoBehaviour
    {
        private static string _mainProfileId;
        private static bool _inRaid;

        private GUIStyle _floatStyle;
        private GUIStyle _panelStyle;

        /// <summary>
        /// Event filter. A null argument = participant unknown.
        /// Events ON the player are disabled by default (they are noise);
        /// bring them back with the Debug -> Track hits on you toggle.
        /// </summary>
        public static bool PassesFightFilter(string victimId, string aggressorId)
        {
            if (!_inRaid)
            {
                return false;
            }

            var me = _mainProfileId;
            if (me != null && victimId == me && !PlateClientConfig.TrackSelfHits.Value)
            {
                return false;
            }

            if (!PlateClientConfig.OverlayOnlyMyFights.Value || me == null)
            {
                return true;
            }

            // own shots + events with an unknown shooter (deaths/effects after our hits)
            return aggressorId == me || aggressorId == null;
        }

        public static string NameOf(Player p)
        {
            try
            {
                var nick = p?.Profile?.Nickname ?? "?";
                return p != null && p.IsYourPlayer ? "YOU" : nick;
            }
            catch
            {
                return "?";
            }
        }

        private void Update()
        {
            if (!PlateClientConfig.OverlayEnabled.Value)
            {
                return;
            }

            var gw = Singleton<GameWorld>.Instance;
            if (gw == null || gw.MainPlayer == null)
            {
                if (_inRaid)
                {
                    _inRaid = false;
                    _mainProfileId = null;
                    HitFeed.Clear();
                    Patches.OverlayPatches.ResetRaidState();
                }

                return;
            }

            _inRaid = true;
            _mainProfileId = gw.MainPlayer.ProfileId;

            if (PlateClientConfig.OverlayPanelKey.Value.IsDown())
            {
                PlateClientConfig.OverlayPanelVisible.Value =
                    !PlateClientConfig.OverlayPanelVisible.Value;
            }

            HitFeed.Tick(Time.time);
        }

        private void OnGUI()
        {
            if (!PlateClientConfig.OverlayEnabled.Value || !_inRaid)
            {
                return;
            }

            var t = PerfTrace.Begin();
            EnsureStyles();
            DrawFloats();
            if (PlateClientConfig.OverlayPanelVisible.Value)
            {
                DrawPanel();
            }

            PerfTrace.End("overlay.gui", t);
        }

        private void EnsureStyles()
        {
            if (_floatStyle != null)
            {
                return;
            }

            _floatStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _panelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
            };
        }

        private static Camera WorldCamera()
        {
            // Scope and post-effect cameras give a wrong WorldToScreenPoint —
            // use EFT's main world camera, Camera.main only as a fallback.
            try
            {
                var eftCam = CameraClass.Instance?.Camera;
                if (eftCam != null)
                {
                    return eftCam;
                }
            }
            catch
            {
                // CameraClass not initialized yet
            }

            return Camera.main;
        }

        private void DrawFloats()
        {
            var cam = WorldCamera();
            if (cam == null)
            {
                return;
            }

            var maxDist = PlateClientConfig.OverlayMaxFloatDistance.Value;
            var maxDistSqr = maxDist * maxDist;
            var camPos = cam.transform.position;

            var ttl = PlateClientConfig.OverlayFloatSeconds.Value;
            foreach (var f in HitFeed.Floats)
            {
                if ((f.WorldPos - camPos).sqrMagnitude > maxDistSqr)
                {
                    continue;
                }

                var sp = cam.WorldToScreenPoint(f.WorldPos);
                if (sp.z <= 0f)
                {
                    continue;
                }

                var age = (Time.time - f.BornAt) / ttl;
                var rect = new Rect(sp.x - 150f,
                    Screen.height - sp.y - age * 45f - f.Stack * 16f, 300f, 20f);

                var alpha = age > 0.7f ? 1f - (age - 0.7f) / 0.3f : 1f;
                GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height),
                    f.Text, _floatStyle);
                GUI.color = new Color(f.Color.r, f.Color.g, f.Color.b, alpha);
                GUI.Label(rect, f.Text, _floatStyle);
            }

            GUI.color = Color.white;
        }

        private void DrawPanel()
        {
            const float width = 660f;
            var lines = HitFeed.Panel.Count;
            var height = 8f + Mathf.Max(1, lines) * 17f;
            // vertical screen center — avoids covering the health UI at the top left
            var top = (Screen.height - height) / 2f;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(10f, top, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var y = top + 4f;
            foreach (var line in HitFeed.Panel)
            {
                GUI.Label(new Rect(16f, y, width - 12f, 17f), line, _panelStyle);
                y += 17f;
            }
        }
    }
}
