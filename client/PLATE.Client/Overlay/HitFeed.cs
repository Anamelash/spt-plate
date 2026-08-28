using System;
using System.Collections.Generic;
using System.IO;
using PLATE.Client.Ballistics;
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

        /// <summary>
        /// One journal file: its own buffer, its own path, its own rotation.
        ///
        /// There are two. `events.log` takes everything, unchanged and unfiltered — it
        /// is what a bug report is built from and it must stay a complete account of the
        /// raid. `events-player.log` takes the subset produced by the player's own
        /// shots, because reading your own three doorways out of four thousand lines of
        /// other people's firefights is not reading.
        ///
        /// A second file rather than a filter on the first: a filter makes the two
        /// readings exclusive, and the complete one is the one you cannot reconstruct
        /// afterwards.
        /// </summary>
        private class Sink
        {
            public readonly string FileName;
            public readonly List<string> Buffer = new List<string>();
            public string Path;

            public Sink(string fileName)
            {
                FileName = fileName;
            }
        }

        private static readonly Sink All = new Sink("events.log");
        private static readonly Sink Player = new Sink("events-player.log");

        private static float _nextFlush;

        // Whose shot the lines being written right now belong to. Stamped with the frame
        // so it cannot leak into anything that happens later — a bleeding tick two
        // seconds afterwards has no shot behind it and must not inherit the last one's
        // attribution.
        private static int _attributedFrame = -1;
        private static bool _attributedToPlayer;

        /// <summary>
        /// Says whose shot the journal lines that follow belong to. Called by whoever is
        /// holding the projectile — the wound model as it stamps its shot context, the
        /// obstacle gate as it decides a barrier — because by the time a line is written
        /// the shooter is several frames of call stack away.
        /// </summary>
        public static void Attribute(bool playersOwnShot)
        {
            _attributedFrame = UnityEngine.Time.frameCount;
            _attributedToPlayer = playersOwnShot;
        }

        /// <summary>Is what is being written now the player's own doing.</summary>
        private static bool MineNow =>
            _attributedFrame == UnityEngine.Time.frameCount && _attributedToPlayer;

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
                All.Buffer.Add(stamped);
                if (MineNow)
                {
                    Player.Buffer.Add(stamped);
                }
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

            // the tallies are whole-raid figures over every shooter, so they belong to
            // the complete journal and would be a lie in the player's own
            All.Buffer.AddRange(PatchStats.Report());
            All.Buffer.AddRange(Ballistics.OrganZones.Report());
            All.Buffer.AddRange(Patches.BloodPatches.FractureReport());
            All.Buffer.AddRange(Blood.CrippleSystem.FallReport());
            All.Buffer.AddRange(Blood.PlateBloodManager.Report());
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

        /// <summary>
        /// Which projectile chain a journal line belongs to.
        ///
        /// The obstacle journal was written for one line per collision and read as if
        /// consecutive lines were one bullet, which is only true when nobody is shooting
        /// fast: two rounds of a burst into a flat wall from a static stance arrive at
        /// nearly the same speed and, once the angle is rounded to the degree, produce
        /// identical lines. A whole raid's worth of evidence about double-charged walls
        /// could not be quantified for exactly that reason — the mechanism was provable
        /// from the code and the magnitude was not provable from the file.
        ///
        /// The chain is identified by its ROOT, because a bullet, the child it spawns
        /// through a door and that child's own child are one projectile as far as
        /// anything downstream is concerned: the muzzle shot's fire index and the low
        /// bits of its seed, then how many barriers deep this node is. The seed alone
        /// would not do — the engine draws a primary seed out of 512 values — and the
        /// fire index alone would not either, since it counts shots and not chains.
        /// </summary>
        public static string ShotId(EFT.Ballistics.Shot shot)
        {
            if (shot == null)
            {
                return "-";
            }

            var root = shot;
            var depth = 0;

            // ceiling for the same reason the obstacle module's parent walk has one: a
            // shot released while its children still fly goes back into the pool, and a
            // reissued object could in principle appear as an ancestor of its own
            // descendant. Engine chains are a dozen nodes at the outside.
            while (root.Parent != null && depth < 32)
            {
                root = root.Parent;
                depth++;
            }

            // OUR serial, not the engine's seed: a primary shot's seed comes out of a
            // range of 512, so the pellets of one shotgun volley share it as a matter of
            // course — the journal carried the same id three times in a single shell, and
            // an analysis keyed on it stitched one pellet's exit onto another's entry.
            // The serial is assigned when the projectile is created, to the root of the
            // chain, so a bullet and everything it spawns read the same number and no two
            // bullets ever do.
            return $"{ProjectileState.Serial(root)}/{depth}";
        }

        /// <summary>General event journal entry (system events, not tied to a hit).</summary>
        public static void LogEvent(string line)
        {
            if (!PlateClientConfig.OverlayLogHits.Value)
            {
                return;
            }

            var stamped = $"[{DateTime.Now:HH:mm:ss.f}] {line}";
            All.Buffer.Add(stamped);
            if (MineNow)
            {
                Player.Buffer.Add(stamped);
            }
        }

        public static void FlushLog()
        {
            Flush(All);
            Flush(Player);
        }

        private static void Flush(Sink sink)
        {
            if (sink.Buffer.Count == 0)
            {
                return;
            }

            try
            {
                if (sink.Path == null)
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
                    sink.Path = Path.Combine(dir, sink.FileName);
                    // the changed settings go in the header: a good share of "the mod
                    // is broken" reports are one knob turned to an extreme. Both files
                    // carry it, so either one stands on its own as evidence.
                    File.AppendAllText(sink.Path,
                        $"{Environment.NewLine}===== session {DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
                        $"(PLATE {Plugin.Version}) ====={Environment.NewLine}" +
                        $"settings: {PlateClientConfig.ChangedSettings()}{Environment.NewLine}");
                }

                // size-based rotation: keep one previous generation beside it
                var fi = new FileInfo(sink.Path);
                if (fi.Exists && fi.Length > MaxLogBytes)
                {
                    var old = Path.Combine(Path.GetDirectoryName(sink.Path) ?? "",
                        Path.GetFileNameWithoutExtension(sink.FileName) + ".old.log");
                    File.Delete(old);
                    File.Move(sink.Path, old);
                }

                File.AppendAllLines(sink.Path, sink.Buffer);
            }
            catch (Exception ex)
            {
                // path included: without it this is undiagnosable from a user's log
                Plugin.Log.LogError(
                    $"[PLATE] event log write failed ({sink.Path ?? sink.FileName}): {ex.Message}");
            }

            sink.Buffer.Clear();
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
