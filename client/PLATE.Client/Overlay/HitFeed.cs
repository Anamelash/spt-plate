using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLATE.Client.Overlay
{
    /// <summary>A floating label in the world.</summary>
    internal class FloatingLabel
    {
        public string Text;
        public Color Color;
        public Vector3 WorldPos;
        public float BornAt;
        public int Stack; // index in the stack above one victim, so labels do not overlap
    }

    /// <summary>
    /// Central overlay event feed: patches and subscriptions write here,
    /// OverlayHud reads from here. Everything on the Unity main thread.
    /// The file log is buffered and flushed once a second (OverlayHud.Update)
    /// so disk writes cause no freezes during the hit frame.
    /// </summary>
    internal static class HitFeed
    {
        public static readonly List<FloatingLabel> Floats = new List<FloatingLabel>();
        public static readonly Queue<string> Panel = new Queue<string>();

        private static readonly Dictionary<string, int> StackByVictim = new Dictionary<string, int>();
        private static readonly Dictionary<string, BulletImpact> LastImpactByVictim =
            new Dictionary<string, BulletImpact>();

        private static readonly List<string> LogBuffer = new List<string>();
        private static string _logPath;
        private static float _nextFlush;

        /// <summary>Bullet-level info (method_4/24/22), correlated with the next ApplyDamage.</summary>
        internal struct BulletImpact
        {
            public float EnergyJ;
            public float SpeedMs;
            public float PenPower;
            public string ChainId; // shot chain id + fragment index, for stitching events together
            public string Flags;   // AVOID / DELAY — diagnostic bullet flags
            public string Tag;     // "", "OVERPEN k=0.82", "FRAG x3"
            public float Time;
        }

        public static void RememberImpact(string victimProfileId, BulletImpact impact)
        {
            if (victimProfileId == null)
            {
                return;
            }

            impact.Time = UnityEngine.Time.time;
            LastImpactByVictim[victimProfileId] = impact;
        }

        public static void AmendImpactTag(string victimProfileId, string tag)
        {
            if (victimProfileId == null || !LastImpactByVictim.TryGetValue(victimProfileId, out var imp))
            {
                return;
            }

            imp.Tag = string.IsNullOrEmpty(imp.Tag) ? tag : imp.Tag + " " + tag;
            LastImpactByVictim[victimProfileId] = imp;
        }

        /// <summary>Take the victim's last impact if it is fresh (within the same second).</summary>
        public static bool TryConsumeImpact(string victimProfileId, out BulletImpact impact)
        {
            impact = default;
            if (victimProfileId == null ||
                !LastImpactByVictim.TryGetValue(victimProfileId, out impact))
            {
                return false;
            }

            LastImpactByVictim.Remove(victimProfileId);
            return UnityEngine.Time.time - impact.Time < 1.0f;
        }

        public static void PushFloat(string victimProfileId, Vector3 worldPos, string text, Color color)
        {
            if (!PlateClientConfig.OverlayFloatingText.Value)
            {
                return;
            }

            int stack;
            StackByVictim.TryGetValue(victimProfileId ?? "", out stack);
            StackByVictim[victimProfileId ?? ""] = stack + 1;

            Floats.Add(new FloatingLabel
            {
                Text = text,
                Color = color,
                WorldPos = worldPos,
                BornAt = UnityEngine.Time.time,
                Stack = stack,
            });

            if (Floats.Count > 40)
            {
                Floats.RemoveAt(0);
            }
        }

        /// <summary>
        /// A sub-line of one shot: armor, wound channel, BABT, exit speed.
        ///
        /// The victim's name belongs on every one of them. Several fights run in
        /// parallel in any raid and the journal interleaves them frame by frame, so an
        /// anonymous "  armor Ceramic cl.4 -> block" sitting above your own event reads
        /// as yours when it belongs to someone across the map. That has already cost one
        /// bug report its diagnosis.
        /// </summary>
        public static void PushHit(EFT.Player victim, string line)
        {
            PushPanel($"  {OverlayHud.NameOf(victim)} | {line}");
        }

        /// <summary>
        /// The head line of a hit: who fired, from what, into whom. Only the head line
        /// carries the origin — the chain under it (armor, zones, bleed) stays keyed by
        /// the victim alone, so one hit reads as one block.
        /// </summary>
        public static void PushHit(EFT.Player victim, string origin, string line)
        {
            PushPanel(string.IsNullOrEmpty(origin)
                ? $"  {OverlayHud.NameOf(victim)} | {line}"
                : $"  {origin} -> {OverlayHud.NameOf(victim)} | {line}");
        }

        public static void PushPanel(string line)
        {
            var stamped = $"[{DateTime.Now:HH:mm:ss.f}] {line}";
            Panel.Enqueue(stamped);
            while (Panel.Count > PlateClientConfig.OverlayPanelMaxLines.Value)
            {
                Panel.Dequeue();
            }

            if (PlateClientConfig.OverlayLogHits.Value)
            {
                LogBuffer.Add(stamped);
            }
        }

        /// <summary>Overlay visuals only — driven by OverlayHud, which may not exist.</summary>
        public static void Tick(float now)
        {
            var ttl = PlateClientConfig.OverlayFloatSeconds.Value;
            Floats.RemoveAll(f => now - f.BornAt > ttl);
            if (Floats.Count == 0)
            {
                StackByVictim.Clear();
            }
        }

        /// <summary>
        /// Journal flush, once a second. Driven by the plugin itself and not by the
        /// overlay: the journal is what bug reports are built from, so it must not
        /// depend on a debug visualisation being switched on.
        /// </summary>
        public static void FlushTick(float now)
        {
            if (now >= _nextFlush)
            {
                _nextFlush = now + 1f;
                FlushLog();
            }
        }

        /// <summary>Appends the hook telemetry table, then flushes.</summary>
        public static void WriteHookReport()
        {
            if (!PlateClientConfig.OverlayLogHits.Value)
            {
                return;
            }

            LogBuffer.AddRange(PatchStats.Report());
            LogBuffer.AddRange(Ballistics.OrganZones.Report());
            LogBuffer.AddRange(Patches.BloodPatches.FractureReport());
            LogBuffer.AddRange(Blood.CrippleSystem.FallReport());
            LogBuffer.AddRange(Blood.PlateBloodManager.Report());
            FlushLog();

            // the counts are per raid
            Ballistics.OrganZones.ResetTally();
            Patches.BloodPatches.ResetFractureTally();
            Blood.CrippleSystem.ResetFallTally();
            Blood.PlateBloodManager.ResetTally();
        }

        // one busy raid is worth ~1 MB; rotating inside a raid splits the evidence and
        // loses the session header that says which settings produced it
        private const long MaxLogBytes = 4 * 1024 * 1024;

        /// <summary>General event journal entry (system events, not tied to a hit).</summary>
        public static void LogEvent(string line)
        {
            if (PlateClientConfig.OverlayLogHits.Value)
            {
                LogBuffer.Add($"[{DateTime.Now:HH:mm:ss.f}] {line}");
            }
        }

        public static void FlushLog()
        {
            if (LogBuffer.Count == 0)
            {
                return;
            }

            try
            {
                if (_logPath == null)
                {
                    // next to our own assembly rather than a hardcoded folder name:
                    // the plugin may sit directly in plugins/, or in a differently
                    // named folder, and on a case-sensitive filesystem "PLATE" and
                    // "plate" are not the same directory
                    var dir = Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                    if (string.IsNullOrEmpty(dir))
                    {
                        dir = Path.Combine(BepInEx.Paths.PluginPath, "PLATE");
                    }

                    Directory.CreateDirectory(dir);
                    _logPath = Path.Combine(dir, "events.log");
                    // the changed settings go in the header: a good share of "the mod
                    // is broken" reports are one knob turned to an extreme
                    File.AppendAllText(_logPath,
                        $"{Environment.NewLine}===== session {DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
                        $"(PLATE {Plugin.Version}) ====={Environment.NewLine}" +
                        $"settings: {PlateClientConfig.ChangedSettings()}{Environment.NewLine}");
                }

                // size-based rotation: keep one previous generation as events.old.log
                var fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > MaxLogBytes)
                {
                    var old = Path.Combine(Path.GetDirectoryName(_logPath) ?? "", "events.old.log");
                    File.Delete(old);
                    File.Move(_logPath, old);
                }

                File.AppendAllLines(_logPath, LogBuffer);
            }
            catch (Exception ex)
            {
                // path included: without it this is undiagnosable from a user's log
                Plugin.Log.LogError(
                    $"[PLATE] event log write failed ({_logPath ?? "path unresolved"}): {ex.Message}");
            }

            LogBuffer.Clear();
        }

        public static void Clear()
        {
            FlushLog();
            Floats.Clear();
            Panel.Clear();
            StackByVictim.Clear();
            LastImpactByVictim.Clear();
        }
    }
}
