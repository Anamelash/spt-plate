using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PLATE.Client
{
    /// <summary>
    /// Lightweight built-in profiler for PLATE subsystems, used to diagnose freezes.
    /// Every 5 seconds writes total/max time per bucket to the log.
    /// Enabled via Debug -> Perf trace.
    /// </summary>
    internal static class PerfTrace
    {
        private class Bucket
        {
            public long Calls;
            public double TotalMs;
            public double MaxMs;
        }

        private static readonly Dictionary<string, Bucket> Buckets =
            new Dictionary<string, Bucket>();

        private static double _nextReport;
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        public static bool Enabled => PlateClientConfig.PerfTrace?.Value == true;

        public static long Begin()
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void End(string bucket, long begin)
        {
            if (begin == 0L)
            {
                return;
            }

            var ms = (Stopwatch.GetTimestamp() - begin) * TicksToMs;
            if (!Buckets.TryGetValue(bucket, out var b))
            {
                b = new Bucket();
                Buckets[bucket] = b;
            }

            b.Calls++;
            b.TotalMs += ms;
            if (ms > b.MaxMs)
            {
                b.MaxMs = ms;
            }
        }

        /// <summary>Called once per frame from BloodSystemComponent.</summary>
        public static void Report(float now)
        {
            if (!Enabled || now < _nextReport)
            {
                return;
            }

            _nextReport = now + 5f;
            if (Buckets.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder("[PLATE-PERF]");
            foreach (var kv in Buckets)
            {
                sb.Append($" {kv.Key}: n={kv.Value.Calls} sum={kv.Value.TotalMs:0.0}ms max={kv.Value.MaxMs:0.00}ms;");
            }

            Plugin.Log.LogInfo(sb.ToString());
            Buckets.Clear();
        }
    }
}
