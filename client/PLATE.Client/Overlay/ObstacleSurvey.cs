using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PLATE.Client.Overlay
{
    /// <summary>
    /// The obstacle survey: a walk across a map, shooting everything a bot could hide
    /// behind, distilled into one line per prop.
    ///
    /// The question the survey answers is geometric — which name means a sheet, which a
    /// shell, which a solid — and per-hit journal lines are the wrong shape for it: a
    /// magazine into one barrel is ten lines saying the same thing, and the chord that
    /// matters is the average over angles, not any single draw. So hits are aggregated
    /// by (object, material, level) over a <see cref="WindowSec"/> window and written as
    /// one line with the count, the chord statistics, and the chord reduced to the
    /// surface normal (chord · |cos|), which strips obliquity — the number a thin gate
    /// keeps small however sideways it was shot.
    ///
    /// Its own file family (`events-obstacles-hits*.log`), never rotated away: the
    /// survey accumulates across raids and maps by design. Chunked instead — see
    /// <see cref="ChunkBytes"/> — so one campaign never turns into one unopenable file.
    ///
    /// The aggregation and the formatting are pure (no Unity, no clock of their own:
    /// `now` comes in, the stamp comes in) so `ObstacleSurveyTests` can drive them
    /// arithmetically. Only the writer below touches the filesystem.
    /// </summary>
    internal static class ObstacleSurvey
    {
        /// <summary>How long one aggregation window stays open after its first hit.</summary>
        internal const float WindowSec = 15f;

        /// <summary>
        /// Chunk threshold. 4 MB is ~30k aggregated lines — a whole survey campaign fits
        /// in one or two chunks, and any chunk still opens instantly in anything.
        /// </summary>
        internal const long ChunkBytes = 4L * 1024 * 1024;

        internal const string FileBase = "events-obstacles-hits";

        internal sealed class Entry
        {
            public string Loc;
            public string Obj;
            public string Par;
            public string Material;
            public float Pl;
            public float OpenedAt;

            public int Hits;
            public int Exits;
            public int Measured;
            public double ChordSum;
            public double ChordMin = double.MaxValue;
            public double ChordMax;
            public double NormSum;
        }

        private static readonly Dictionary<string, Entry> Open =
            new Dictionary<string, Entry>();

        /// <summary>
        /// One hit on a prop. <paramref name="chordMm"/> is the full measured chord
        /// along the trajectory (0 with <paramref name="measured"/> false when the probe
        /// failed); <paramref name="cos"/> is the impact cosine against the surface
        /// normal, which reduces the chord to the object's own thickness.
        ///
        /// <paramref name="parent"/> is the collider's ancestry ("parent/grandparent"),
        /// because half the maps name the collider itself nothing at all — "metal",
        /// "LOD0Collider" — and the name that identifies the prop is a level or two up.
        /// It is part of the key: two different cupboards both carrying a collider
        /// called "metal" must not average into one row. Spaces become underscores so
        /// the field stays one awk column.
        ///
        /// <paramref name="freeExit"/> marks the far face of a crossing the model already
        /// charged on the way in. Such a collision is real — the engine raises one for
        /// every solid region a non-convex mesh has — but it is not a hit on the prop in
        /// any sense the survey is asking about, and counting it doubled `n` on every
        /// door on the map. It gets its own column instead of vanishing, because "this
        /// prop produces two events per shot" is itself a fact about its geometry.
        /// </summary>
        public static void Note(string loc, string obj, string parent, string material,
            float pl, double chordMm, double cos, bool measured, float now,
            bool freeExit = false)
        {
            var par = string.IsNullOrEmpty(parent) ? "-" : parent.Replace(' ', '_');
            var key = obj + "|" + par + "|" + material + "|" +
                      pl.ToString("0.#", CultureInfo.InvariantCulture);
            if (!Open.TryGetValue(key, out var e))
            {
                e = new Entry
                {
                    Loc = loc,
                    Obj = obj,
                    Par = par,
                    Material = material,
                    Pl = pl,
                    OpenedAt = now,
                };
                Open[key] = e;
            }

            if (freeExit)
            {
                // out of n and out of the chord statistics both: the chord it would
                // contribute is the same chord its entry face already contributed
                e.Exits++;
                return;
            }

            e.Hits++;
            if (measured && chordMm > 0)
            {
                e.Measured++;
                e.ChordSum += chordMm;
                e.NormSum += chordMm * Math.Abs(cos);
                if (chordMm < e.ChordMin)
                {
                    e.ChordMin = chordMm;
                }

                if (chordMm > e.ChordMax)
                {
                    e.ChordMax = chordMm;
                }
            }
        }

        internal static bool WindowClosed(Entry e, float now)
        {
            return now - e.OpenedAt >= WindowSec;
        }

        /// <summary>
        /// Closes every window whose time is up (or all of them) and returns their
        /// formatted lines. Pure: the caller supplies the wall-clock stamp.
        /// </summary>
        internal static List<string> Drain(float now, string stamp, bool everything = false)
        {
            List<string> lines = null;
            List<string> done = null;
            foreach (var kv in Open)
            {
                if (!everything && !WindowClosed(kv.Value, now))
                {
                    continue;
                }

                (lines ?? (lines = new List<string>())).Add(Line(kv.Value, stamp));
                (done ?? (done = new List<string>())).Add(kv.Key);
            }

            if (done != null)
            {
                foreach (var key in done)
                {
                    Open.Remove(key);
                }
            }

            return lines ?? new List<string>();
        }

        /// <summary>
        /// One window as one line. All lengths in mm; `-` where nothing was measured —
        /// a prop whose probe never lands is itself a finding. InvariantCulture
        /// throughout: a decimal comma would shear every awk script run on this file.
        /// </summary>
        internal static string Line(Entry e, string stamp)
        {
            string avg = "-", min = "-", max = "-", norm = "-";
            if (e.Measured > 0)
            {
                avg = Mm(e.ChordSum / e.Measured);
                min = Mm(e.ChordMin);
                max = Mm(e.ChordMax);
                norm = Mm(e.NormSum / e.Measured);
            }

            var miss = e.Hits - e.Measured;
            return string.Format(CultureInfo.InvariantCulture,
                "[{0}] loc={1} mat={2} pl={3} n={4} avg={5} min={6} max={7} norm={8} miss={9} exits={10} par={11} obj={12}",
                stamp, e.Loc, e.Material,
                e.Pl.ToString("0.#", CultureInfo.InvariantCulture),
                e.Hits, avg, min, max, norm, miss, e.Exits, e.Par, e.Obj);
        }

        private static string Mm(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }

        // --- The writer: buffered, chunked, never rotated away ---

        private static readonly List<string> Buffer = new List<string>();
        private static string _path;
        private static float _nextFlush;

        /// <summary>
        /// A per-hit line into the same file family. This is where the obstacle
        /// journal's own lines land — the model line, the engine line and the `wall?`
        /// discovery — now that events.log carries no obstacle traffic at all: walls
        /// were most of that file by volume, and the event journal is for what happens
        /// to bodies. The two shapes coexist and grep apart cleanly — per-hit lines
        /// contain " wall", aggregated ones start their data with "loc=".
        /// </summary>
        public static void LogLine(string line)
        {
            Buffer.Add("[" + Stamp() + "] " + line);
        }

        /// <summary>Frame heartbeat: closes due windows, flushes once a second.</summary>
        public static void Tick(float now)
        {
            if (Open.Count > 0)
            {
                var lines = Drain(now, Stamp());
                if (lines.Count > 0)
                {
                    Buffer.AddRange(lines);
                }
            }

            if (Buffer.Count > 0 && now >= _nextFlush)
            {
                _nextFlush = now + 1f;
                Write();
            }
        }

        /// <summary>Raid end: every open window closes now, whatever its age.</summary>
        public static void FlushAll(float now)
        {
            if (Open.Count > 0)
            {
                Buffer.AddRange(Drain(now, Stamp(), everything: true));
            }

            Write();
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);
        }

        private static void Write()
        {
            if (Buffer.Count == 0)
            {
                return;
            }

            try
            {
                if (_path == null)
                {
                    // same directory resolution as HitFeed: next to the plugin assembly
                    var dir = Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                    if (string.IsNullOrEmpty(dir))
                    {
                        dir = Path.Combine(BepInEx.Paths.PluginPath, "PLATE");
                    }

                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, FileBase + ".log");
                }

                // chunking, not rotation: the full file is renamed aside and kept, and
                // the survey continues into a fresh one. Nothing is ever deleted — the
                // whole point of the file is to accumulate a campaign.
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > ChunkBytes)
                {
                    var dir = Path.GetDirectoryName(_path) ?? "";
                    var n = 1;
                    string chunk;
                    do
                    {
                        chunk = Path.Combine(dir, $"{FileBase}-{n:000}.log");
                        n++;
                    }
                    while (File.Exists(chunk));

                    File.Move(_path, chunk);
                }

                if (!File.Exists(_path))
                {
                    File.AppendAllText(_path,
                        "# PLATE obstacle journal. Survey lines: one per prop per " +
                        $"{WindowSec:0}s window, lengths in mm; avg/min/max = full " +
                        "chord along the trajectory, norm = chord reduced to the " +
                        "surface normal, miss = hits the probe could not measure, " +
                        "exits = far-face collisions the model let through free (already " +
                        "paid for on the way in; kept out of n), " +
                        "par = the collider's parents (the prop's real name when the " +
                        "collider itself is called nothing but 'metal')." +
                        Environment.NewLine +
                        "# Per-hit 'wall' lines (the model's and the engine's reading " +
                        "of one collision) appear instead when 'Log obstacle hits' is " +
                        "EveryHit." + Environment.NewLine);
                }

                File.AppendAllLines(_path, Buffer);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    $"[PLATE] survey log write failed ({_path ?? FileBase}): {ex.Message}");
            }

            Buffer.Clear();
        }
    }
}
