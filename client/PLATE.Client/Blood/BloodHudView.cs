using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PLATE.Client.Blood
{
    /// <summary>
    /// The on-screen blood panel.
    ///
    /// Every object it draws is one it made: a canvas, an icon and two lines of text.
    /// That is the whole point of it. The first version copied a widget out of the
    /// Health tab to get EFT's own font and glyph for free, and the copy could not be
    /// made in a raid because that screen is menu UI and is not loaded there — so the
    /// feature depended on finding something it did not own, at a moment it had guessed
    /// at, and simply did not appear. Nothing here can fail that way: the panel is built
    /// from primitives that always exist.
    ///
    /// The game's own font is still preferred, but as decoration — if none is on screen
    /// to borrow, TextMeshPro's default is used and the panel is otherwise identical.
    ///
    /// Reads nothing per frame that has not changed: the layout is applied when a
    /// setting moves rather than every frame, and text is only assigned when the string
    /// differs, because assigning to a TMP_Text rebuilds its mesh whether or not the
    /// characters are the same.
    /// </summary>
    internal sealed class BloodHudView
    {
        // Panel geometry, in 1920x1080 units before the user's scale. Presentation, not
        // model: nothing downstream reads these.
        private const float MainFontSize = 24f;
        private const float FootFontSize = 16f;
        private const float TextWidth = 420f;

        // The palette. Current volume bright, capacity muted behind it — the vanilla
        // Health tab's own arrangement, which is what makes the pair read as one value.
        private static readonly Color CurrentColor = new Color(0.90f, 0.90f, 0.88f);
        private static readonly Color MutedColor = new Color(0.55f, 0.55f, 0.53f);
        private static readonly Color WarningColor = new Color(0.85f, 0.25f, 0.20f);

        private GameObject _canvas;
        private RectTransform _root;
        private RectTransform _iconRect;
        private RectTransform _mainRect;
        private RectTransform _footRect;
        private Image _icon;
        private TMP_Text _main;
        private TMP_Text _foot;

        private bool _layoutDirty = true;
        private bool _formatDirty = true;
        private string _lastMain;
        private string _lastFoot;
        private bool _lastFootShown;

        // What the last main line was built from. Rebuilding it every frame would
        // allocate a string and rebuild a TMP mesh to print the same characters; the
        // volume only moves while blood is actually leaving.
        private int _lastVolumeMl = int.MinValue;
        private int _lastTier = int.MinValue;
        private float _nextSecondLineAt;

        private string _currentHex;
        private string _mutedHex;
        private string _warningHex;

        public bool Built => _canvas != null;

        /// <summary>
        /// Builds the panel. Cheap to call every frame — after the first it is a null
        /// check. No searching for anything the panel needs, so there is no retry, no
        /// grace period and no failure state to report.
        /// </summary>
        public void Build()
        {
            if (Built)
            {
                return;
            }

            _canvas = new GameObject("PLATE_Hud", typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(_canvas);

            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = PlateClientConfig.HudSortingOrder.Value;

            // TextMeshPro packs its SDF parameters into these channels; without them the
            // text renders as a smear rather than as glyphs.
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                              AdditionalCanvasShaderChannels.Normal |
                                              AdditionalCanvasShaderChannels.Tangent;

            // Scaled to the screen rather than measured in raw pixels, so an offset lands
            // in the same place on every resolution.
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root = NewRect("Root", _canvas.transform);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            _iconRect = (RectTransform)iconGo.transform;
            _iconRect.SetParent(_root, false);
            Corner(_iconRect);
            _icon = iconGo.GetComponent<Image>();
            _icon.sprite = DropSprite();
            _icon.raycastTarget = false;

            var font = ResolveFont();
            _main = NewText("Main", font, MainFontSize, out _mainRect);
            _foot = NewText("Foot", font, FootFontSize, out _footRect);

            _currentHex = Hex(CurrentColor);
            _mutedHex = Hex(MutedColor);
            _warningHex = Hex(WarningColor);

            // F12 edits should move the panel while looking at it, and polling eleven
            // settings every frame to notice would be the wrong way round.
            PlateClientConfig.Source.SettingChanged += OnSettingChanged;
            _layoutDirty = true;

            Plugin.Log.LogInfo($"[PLATE] HUD built (font: {(font != null ? font.name : "none")})");
        }

        private TMP_Text NewText(string name, TMP_FontAsset font, float size, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            Corner(rect);

            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = size;
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.richText = true;
            return text;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Corner(rect);
            return rect;
        }

        /// <summary>Pins a rect to its parent's bottom-left, so a position is a position.</summary>
        private static void Corner(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            _layoutDirty = true;
            _formatDirty = true;
            _nextSecondLineAt = 0f;
        }

        /// <summary>Feeds the panel one frame of blood state.</summary>
        public void Tick(BloodState state)
        {
            if (!Built)
            {
                return;
            }

            var visible = state != null && PlateClientConfig.HudVisible.Value;
            if (_canvas.activeSelf != visible)
            {
                _canvas.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            if (_layoutDirty)
            {
                ApplyLayout();
            }

            // The second line is read off a rate that moves every frame, so refreshing it
            // every frame prints a number that jitters instead of one that can be read.
            var now = Time.unscaledTime;
            var secondLineDue = now >= _nextSecondLineAt;

            var volumeMl = Mathf.RoundToInt(state.Cur);
            var mainDue = _formatDirty || volumeMl != _lastVolumeMl || state.Tier != _lastTier;

            if (!mainDue && !secondLineDue)
            {
                return;
            }

            PlateBloodManager.NextThreshold(state.Tier, out var next, out var label);
            var lines = BloodReadout.Build(state.Cur, state.Max, state.Tier, state.DrainMlSec,
                new BloodReadout.Thresholds
                {
                    Warning = PlateClientConfig.ThresholdTier2.Value,
                    Death = PlateClientConfig.DeathThreshold.Value,
                    Next = next,
                    NextLabel = label,
                },
                new BloodReadout.Format
                {
                    Units = PlateClientConfig.HudUnits.Value,
                    Range = PlateClientConfig.HudRange.Value,
                });

            if (mainDue)
            {
                _formatDirty = false;
                _lastVolumeMl = volumeMl;
                _lastTier = state.Tier;
                UpdateMain(lines);
            }

            if (secondLineDue)
            {
                _nextSecondLineAt = now + PlateClientConfig.HudEtaRefresh.Value;
                UpdateSecondLine(lines);
            }
        }

        private void UpdateMain(BloodReadout.Lines lines)
        {
            var volumeHex = lines.Warning ? _warningHex : _currentHex;

            var main = $"<color=#{volumeHex}>{lines.Volume}</color>";
            if (lines.Capacity.Length > 0)
            {
                main += $"<color=#{_mutedHex}>/{lines.Capacity}</color>";
            }

            main += $"<color=#{_mutedHex}>  {lines.Tag}</color>";

            SetText(_main, main, ref _lastMain);
            _icon.color = IconColor(lines.Warning);
        }

        private void UpdateSecondLine(BloodReadout.Lines lines)
        {
            var rate = PlateClientConfig.HudRateArrow.Value && lines.Rate.Length > 0
                ? "▼ " + lines.Rate
                : string.Empty;
            var estimate = PlateClientConfig.HudEta.Value ? lines.Estimate : string.Empty;

            var show = rate.Length > 0 || estimate.Length > 0;
            if (show != _lastFootShown)
            {
                _foot.gameObject.SetActive(show);
                _lastFootShown = show;
            }

            if (!show)
            {
                return;
            }

            var text = rate.Length > 0 && estimate.Length > 0
                ? rate + "   " + estimate
                : rate + estimate;

            SetText(_foot, $"<color=#{_warningHex}>{text}</color>", ref _lastFoot);
        }

        /// <summary>
        /// Assigning to a TMP_Text rebuilds its mesh even when the characters are
        /// identical, and this runs every frame of a raid.
        /// </summary>
        private static void SetText(TMP_Text target, string value, ref string last)
        {
            if (string.Equals(value, last, StringComparison.Ordinal))
            {
                return;
            }

            target.text = value;
            last = value;
        }

        private void ApplyLayout()
        {
            _layoutDirty = false;

            var scale = PlateClientConfig.HudScale.Value;
            _root.anchoredPosition = new Vector2(PlateClientConfig.HudOffsetX.Value,
                PlateClientConfig.HudOffsetY.Value);
            _root.localScale = new Vector3(scale, scale, 1f);

            var gap = PlateClientConfig.HudLineGap.Value;

            // The icon is squared off the main line's height, and both text lines share a
            // left edge past it so the block reads as one column.
            var indent = MainFontSize + gap;
            _footRect.sizeDelta = new Vector2(TextWidth, FootFontSize);
            _footRect.anchoredPosition = new Vector2(indent, 0f);

            var mainY = FootFontSize + gap;
            _mainRect.sizeDelta = new Vector2(TextWidth, MainFontSize);
            _mainRect.anchoredPosition = new Vector2(indent, mainY);

            _iconRect.sizeDelta = new Vector2(MainFontSize, MainFontSize);
            _iconRect.anchoredPosition = new Vector2(0f, mainY);

            if (_canvas.TryGetComponent<Canvas>(out var canvas))
            {
                canvas.sortingOrder = PlateClientConfig.HudSortingOrder.Value;
            }

            _icon.color = IconColor(false);
        }

        private static Color IconColor(bool warning)
        {
            // Hue and saturation are the player's; value is full so the drop keeps its
            // weight against the world behind it.
            var hue = warning ? 0f : PlateClientConfig.HudIconHue.Value;
            return Color.HSVToRGB(hue, PlateClientConfig.HudIconSaturation.Value, 1f);
        }

        /// <summary>
        /// The game's own interface font, decided rather than stumbled upon.
        ///
        /// There is no name to ask for: Assembly-CSharp contains no font name at all —
        /// the faces live in asset bundles, and LocalizationManager's registry is the
        /// per-language set, which does not say which one the interface is set in.
        /// Hardcoding "Bender" would be a magic string that survives exactly until BSG
        /// restyles. So it is counted instead: the interface font is, by definition, the
        /// one most of the interface's text is set in. That is a function of what is
        /// loaded, not of enumeration order.
        ///
        /// Still decoration rather than a dependency — with nothing loaded to count, the
        /// panel is built and laid out identically in TextMeshPro's default face.
        /// </summary>
        private static TMP_FontAsset ResolveFont()
        {
            var forced = PlateClientConfig.HudFontName.Value;
            var counts = new Dictionary<TMP_FontAsset, int>();

            foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                var font = text == null ? null : text.font;
                if (font == null)
                {
                    continue;
                }

                if (forced.Length > 0 &&
                    font.name.IndexOf(forced, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Plugin.Log.LogInfo($"[PLATE] HUD font forced by config: {font.name}");
                    return font;
                }

                counts.TryGetValue(font, out var seen);
                counts[font] = seen + 1;
            }

            TMP_FontAsset best = null;
            var bestCount = 0;
            foreach (var pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }

            // The tally, not just the winner: this is where the name to force in the
            // config comes from when the winner is not the wanted face.
            Plugin.Log.LogInfo("[PLATE] HUD fonts in use: " + Tally(counts));

            if (best != null)
            {
                return best;
            }

            if (forced.Length > 0)
            {
                Plugin.Log.LogWarning(
                    $"[PLATE] HUD font '{forced}' not found among the loaded fonts — " +
                    "using the TextMeshPro default. See the tally above for the names.");
            }

            return TMP_Settings.defaultFontAsset;
        }

        /// <summary>The five most-used faces, biggest first: "Bender SDF x412, ...".</summary>
        private static string Tally(Dictionary<TMP_FontAsset, int> counts)
        {
            if (counts.Count == 0)
            {
                return "none loaded";
            }

            var ordered = new List<KeyValuePair<TMP_FontAsset, int>>(counts);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < ordered.Count && i < 5; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(ordered[i].Key.name).Append(" x").Append(ordered[i].Value);
            }

            if (ordered.Count > 5)
            {
                sb.Append(", +").Append(ordered.Count - 5).Append(" more");
            }

            return sb.ToString();
        }

        private static Sprite _drop;

        /// <summary>
        /// The drop, drawn rather than found. A sprite lifted out of the game's UI would
        /// put this panel back where the last one was — unable to appear because
        /// something it did not own was not loaded yet. White with an alpha shape, so the
        /// hue above tints it cleanly.
        /// </summary>
        private static Sprite DropSprite()
        {
            if (_drop != null)
            {
                return _drop;
            }

            const int size = 64;
            const int samples = 3; // supersampling per axis, for a smooth edge

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var hits = 0;
                    for (var sy = 0; sy < samples; sy++)
                    {
                        for (var sx = 0; sx < samples; sx++)
                        {
                            var u = (x + (sx + 0.5f) / samples) / size;
                            var v = (y + (sy + 0.5f) / samples) / size;
                            if (InDrop(u, v))
                            {
                                hits++;
                            }
                        }
                    }

                    var alpha = (byte)(255 * hits / (samples * samples));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            _drop = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _drop;
        }

        /// <summary>A bulb with a taper off the top of it, in unit coordinates, y up.</summary>
        private static bool InDrop(float x, float y)
        {
            const float bulbX = 0.5f;
            const float bulbY = 0.34f;
            const float radius = 0.28f;
            const float tipY = 0.96f;

            var dx = x - bulbX;
            var dy = y - bulbY;
            if (dx * dx + dy * dy <= radius * radius)
            {
                return true;
            }

            if (y < bulbY || y > tipY)
            {
                return false;
            }

            var halfWidth = radius * (tipY - y) / (tipY - bulbY);
            return Mathf.Abs(dx) <= halfWidth;
        }

        private static string Hex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }

        public void Destroy()
        {
            if (_canvas == null)
            {
                return;
            }

            PlateClientConfig.Source.SettingChanged -= OnSettingChanged;
            Object.Destroy(_canvas);

            _canvas = null;
            _root = null;
            _icon = null;
            _main = null;
            _foot = null;
            _lastMain = null;
            _lastFoot = null;
            _lastFootShown = false;
            _lastVolumeMl = int.MinValue;
            _lastTier = int.MinValue;
            _nextSecondLineAt = 0f;
            _layoutDirty = true;
            _formatDirty = true;
        }
    }
}
