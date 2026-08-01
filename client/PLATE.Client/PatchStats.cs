using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PLATE.Client
{
    /// <summary>
    /// Per-hook telemetry: whether a patch was applied, and how many times it has
    /// actually run.
    ///
    /// "Applied" and "fires" are different failures with identical symptoms — a
    /// feature that quietly does nothing. A patch can fail to attach, or attach to a
    /// method the game never calls, or run and bail out early. Without the fire
    /// counter the first two are indistinguishable in a bug report, which is exactly
    /// how a dead transfusion item shipped in 0.9.0.
    ///
    /// Counting is generic: one shared postfix takes Harmony's __originalMethod, so
    /// every patched target is covered without hand-instrumenting each patch body.
    /// </summary>
    internal static class PatchStats
    {
        private class Entry
        {
            public string Label;
            public string Target;
            public bool Applied;
            public string Error;
            public long Fired;

            /// <summary>
            /// Environment.TickCount, not Time.time: this type is exercised by tests
            /// outside Unity, where Time.time is an engine ECall and throws.
            /// </summary>
            public int LastAt;
        }

        private static readonly Dictionary<MethodBase, Entry> ByMethod =
            new Dictionary<MethodBase, Entry>();

        private static readonly Dictionary<string, Entry> ByLabel =
            new Dictionary<string, Entry>();

        private static readonly List<Entry> Ordered = new List<Entry>();

        /// <summary>Patches that could not be attached.</summary>
        public static int Failures { get; private set; }

        private static Entry GetOrAdd(MethodBase target, string label)
        {
            if (target != null && ByMethod.TryGetValue(target, out var existing))
            {
                return existing;
            }

            var entry = new Entry
            {
                Label = label,
                Target = target == null
                    ? "(unresolved)"
                    : $"{Short(target.DeclaringType)}.{target.Name}",
            };

            Ordered.Add(entry);
            ByLabel[label] = entry;
            if (target != null)
            {
                ByMethod[target] = entry;
            }

            return entry;
        }

        private static string Short(Type t)
        {
            if (t == null)
            {
                return "?";
            }

            var name = t.Name;
            var tick = name.IndexOf('`');
            return tick > 0 ? name.Substring(0, tick) : name;
        }

        public static void MarkApplied(MethodBase target, string label)
        {
            GetOrAdd(target, label).Applied = true;
        }

        public static void MarkFailed(MethodBase target, string label, string error)
        {
            var e = GetOrAdd(target, label);
            e.Applied = false;
            e.Error = error;
            Failures++;
        }

        /// <summary>
        /// Counts one invocation. Called explicitly from the patch bodies.
        ///
        /// Do NOT go back to attaching a generic counting postfix via a second
        /// harmony.Patch on the same target: that shipped in 0.9.2 and broke
        /// CanApplyItem, which returns bool and lives on a generic base HarmonyX
        /// already warns about. Re-patching it mangled __result, the applicability
        /// gate started refusing every medical item in raid, and nothing in the log
        /// pointed at us. Telemetry must never be able to change behaviour.
        /// </summary>
        public static void Hit(string label)
        {
            if (ByLabel.TryGetValue(label, out var e))
            {
                e.Fired++;
                e.LastAt = Environment.TickCount;
            }
        }

        /// <summary>
        /// Exact variant for hooks shared by several overloads. Harmony injects
        /// __originalMethod as a parameter of the existing patch — that is plain
        /// argument injection, not an additional patch, and is safe.
        /// </summary>
        public static void Hit(MethodBase original)
        {
            if (original != null && ByMethod.TryGetValue(original, out var e))
            {
                e.Fired++;
                e.LastAt = Environment.TickCount;
            }
        }

        /// <summary>Records the target as applied. Bookkeeping only — patches nothing.</summary>
        public static void Track(Harmony harmony, MethodBase target, string label)
        {
            MarkApplied(target, label);
        }

        /// <summary>Labels of hooks that could not be attached.</summary>
        public static IEnumerable<string> FailedLabels() =>
            Ordered.Where(e => !e.Applied).Select(e => e.Label);

        /// <summary>Human-readable table for the event journal.</summary>
        public static IEnumerable<string> Report()
        {
            yield return "===== PLATE hook report =====";

            var width = Ordered.Count == 0 ? 10 : Ordered.Max(e => e.Label.Length);
            foreach (var e in Ordered)
            {
                var status = e.Applied ? "applied" : "NOT APPLIED";
                var fired = e.Applied
                    ? (e.Fired > 0 ? $"fired {e.Fired}" : "fired 0  <-- never ran")
                    : e.Error;
                yield return $"  {e.Label.PadRight(width)}  {status,-11}  {fired}  [{e.Target}]";
            }

            var dead = Ordered.Count(e => e.Applied && e.Fired == 0);
            yield return $"  -- {Ordered.Count} hooks, {Failures} not applied, {dead} never ran";
        }
    }
}
