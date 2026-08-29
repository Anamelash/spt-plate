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
        private GUIStyle _markerStyle;

        /// <summary>
        /// The marker sizes are applied when they change rather than read every frame —
        /// the rule BloodHudView established. The geometry is re-placed and the label
        /// style is rebuilt, so a scale turned in F12 lands on markers already on screen
        /// instead of only on the next shot.
        /// </summary>
        private void OnEnable()
        {
            if (PlateClientConfig.Source != null)
            {
                PlateClientConfig.Source.SettingChanged += OnSettingChanged;
            }
        }

        private void OnDisable()
        {
            if (PlateClientConfig.Source != null)
            {
                PlateClientConfig.Source.SettingChanged -= OnSettingChanged;
            }
        }

        private void OnSettingChanged(object sender, BepInEx.Configuration.SettingChangedEventArgs e)
        {
            var key = e.ChangedSetting?.Definition?.Key;
            if (key == null || !key.StartsWith("Marker") && key != "Hit point scale" &&
                key != "Trajectory ray scale")
            {
                return;
            }

            _markerStyle = null;
            HitMarkers.ApplyLayout();
        }

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
                    HitMarkers.Clear();
                    ObstacleSurvey.FlushAll(Time.time);
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
            HitMarkers.Tick(Time.time);
            ObstacleSurvey.Tick(Time.time);
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
            DrawMarkerLabels();
            if (PlateClientConfig.OverlayPanelVisible.Value)
            {
                DrawPanel();
            }

            PerfTrace.End("overlay.gui", t);
        }

        /// <summary>
        /// Marker labels: projected to the screen and pinned to the point, with no
        /// upward drift. A floating label belongs to a victim and wants to be read as it
        /// rises; this one belongs to a place and has to stay on it, or it stops being
        /// evidence about where the bullet went.
        /// </summary>
        private void DrawMarkerLabels()
        {
            if (!PlateClientConfig.MarkersEnabled.Value)
            {
                return;
            }

            var mode = PlateClientConfig.MarkerLabelProjection.Value;
            var cam = LabelCamera(mode);
            if (cam == null)
            {
                return;
            }

            var maxDist = PlateClientConfig.OverlayMaxFloatDistance.Value;
            var maxDistSqr = maxDist * maxDist;
            var camPos = cam.transform.position;

            int live = 0, drawn = 0, tooFar = 0, behind = 0, noText = 0;
            var sample = "";

            foreach (var m in HitMarkers.Live)
            {
                live++;
                var pos = m.WorldPos;

                if ((pos - camPos).sqrMagnitude > maxDistSqr)
                {
                    tooFar++;
                    continue;
                }

                if (!Project(cam, mode, pos, out var gui))
                {
                    behind++;
                    continue;
                }

                if (sample.Length == 0)
                {
                    sample = $"first at {pos} -> gui {gui} of {Screen.width}x{Screen.height}" +
                             $" (cam rect {cam.pixelRect}) text '{m.Text}'";
                }

                if (string.IsNullOrEmpty(m.Text))
                {
                    noText++;
                    continue;
                }

                var rect = new Rect(gui.x - 150f, gui.y - 20f, 300f, 20f);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height),
                    m.Text, _markerStyle);
                GUI.color = m.Color;
                GUI.Label(rect, m.Text, _markerStyle);
                drawn++;
            }

            GUI.color = Color.white;
            ReportLabels(live, drawn, tooFar, behind, noText, sample);
        }

        /// <summary>
        /// World point to GUI point (origin top-left), by whichever of the candidate
        /// projections is selected. False = behind the camera, nothing to draw.
        ///
        /// The three that are not the obvious one exist because EFT does not render
        /// through a plain full-screen camera: WorldToScreenPoint answers in the
        /// CAMERA's pixel space, GUI wants the window's, and the two are the same thing
        /// only when the camera owns the whole window.
        /// </summary>
        /// <summary>Which camera this projection reads the world through.</summary>
        private static Camera LabelCamera(LabelProjection mode)
        {
            if (mode == LabelProjection.MainCamera && Camera.main != null)
            {
                return Camera.main;
            }

            return WorldCamera();
        }

        private static bool Project(Camera cam, LabelProjection mode,
            Vector3 world, out Vector2 gui)
        {
            gui = default;

            if (mode == LabelProjection.Viewport)
            {
                var vp = cam.WorldToViewportPoint(world);
                if (vp.z <= 0f)
                {
                    return false;
                }

                gui = new Vector2(vp.x * Screen.width, (1f - vp.y) * Screen.height);
                return true;
            }

            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
            {
                return false;
            }

            if (mode == LabelProjection.CameraPixels)
            {
                var r = cam.pixelRect;
                gui = new Vector2(r.x + sp.x, Screen.height - (r.y + sp.y));
                return true;
            }

            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        private float _nextLabelReport;

        /// <summary>
        /// Why the marker labels are or are not on screen, once a second under Verbose.
        ///
        /// Here because the labels went missing once and reading the code did not find
        /// it: the geometry rendered, no exception was thrown, the config was on, and
        /// every explanation that fit one of those facts contradicted another. This
        /// prints the four things that can silently swallow a label — nothing live, too
        /// far, behind the camera, no text — plus where the first one actually projects,
        /// so the next raid settles it instead of a fifth theory.
        /// </summary>
        private void ReportLabels(int live, int drawn, int tooFar, int behind, int noText,
            string sample)
        {
            if (!PlateClientConfig.VerboseLog.Value || Time.time < _nextLabelReport)
            {
                return;
            }

            _nextLabelReport = Time.time + 1f;
            if (live == 0 && drawn == 0)
            {
                return; // nothing has been shot yet; not worth a line a second
            }

            Plugin.Log.LogInfo(
                $"[PLATE] marker labels: live {live}, drawn {drawn}, too far {tooFar}, " +
                $"behind camera {behind}, no text {noText}. {sample}");
        }

        private void EnsureStyles()
        {
            if (_markerStyle == null)
            {
                _markerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(6, Mathf.RoundToInt(
                        HitMarkers.BaseFontSize * PlateClientConfig.MarkerTextScale.Value)),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

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
            // the same projection the markers use: both are world points turned into
            // screen text, and they were both landing in the wrong place for the same
            // reason. One switch governs both.
            var mode = PlateClientConfig.MarkerLabelProjection.Value;
            var cam = LabelCamera(mode);
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

                if (!Project(cam, mode, f.WorldPos, out var gui))
                {
                    continue;
                }

                var age = (Time.time - f.BornAt) / ttl;

                // unlike a marker label, a float is meant to drift upward as it ages
                var rect = new Rect(gui.x - 150f,
                    gui.y - age * 45f - f.Stack * 16f, 300f, 20f);

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
