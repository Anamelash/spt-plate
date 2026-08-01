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
            public float LastAt;
        }

        private static readonly Dictionary<MethodBase, Entry> ByMethod =
            new Dictionary<MethodBase, Entry>();

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

        /// <summary>Shared counting postfix — see the class summary.</summary>
        public static void Counter(MethodBase __originalMethod)
        {
            if (__originalMethod != null && ByMethod.TryGetValue(__originalMethod, out var e))
            {
                e.Fired++;
                e.LastAt = UnityEngine.Time.time;
            }
        }

        private static readonly HarmonyMethod CounterMethod =
            new HarmonyMethod(typeof(PatchStats), nameof(Counter));

        /// <summary>
        /// Records the target as applied and attaches the fire counter to it. Safe to
        /// call for prefix-patched targets too: Harmony still runs postfixes.
        /// </summary>
        public static void Track(Harmony harmony, MethodBase target, string label)
        {
            MarkApplied(target, label);
            try
            {
                harmony.Patch(target, postfix: CounterMethod);
            }
            catch (Exception ex)
            {
                // the real patch is already in place; losing the counter is not fatal
                Plugin.Log.LogWarning($"[PLATE] hook counter not attached to {label}: {ex.Message}");
            }
        }

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
