using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PLATE.Client;

namespace PLATE.Tests
{
    /// <summary>
    /// Captures a real Assembly-CSharp/desktop-CLR incompatibility before Harmony
    /// touches each Player target. Unity's Mono accepts the game's unsealed
    /// DeltaTimeDelegate metadata; .NET Framework 4.8 rejects it while preparing an
    /// affected Player method, before any PLATE patch method is compiled.
    /// </summary>
    internal static class TestHostLimitations
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, string> _playerBabtFailures;

        internal static IReadOnlyDictionary<string, string> PlayerBabtFailures
        {
            get
            {
                CapturePlayerBabtEvidence();
                return _playerBabtFailures;
            }
        }

        internal static void CapturePlayerBabtEvidence()
        {
            lock (Gate)
            {
                if (_playerBabtFailures != null)
                {
                    return;
                }

                var failures = new Dictionary<string, string>();
                Probe("ProceedDamageThroughArmorPostfix",
                    PatchTargets.Player_ProceedDamageThroughArmor, failures);
                Probe("ApplyDamageInfoPostfix",
                    PatchTargets.Player_ApplyDamageInfo, failures);
                Probe("ApplyShotBabtFinalizer",
                    PatchTargets.Player_ApplyShot, failures);
                _playerBabtFailures = failures;
            }
        }

        private static void Probe(string patchLabel, MethodBase target,
            IDictionary<string, string> failures)
        {
            var method = target as MethodInfo;
            if (method == null)
            {
                return;
            }

            try
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
            }
            catch (TypeLoadException ex)
            {
                if (ex.Message != null &&
                    ex.Message.IndexOf("DeltaTimeDelegate", StringComparison.Ordinal) >= 0)
                {
                    failures[patchLabel] = ex.GetType().FullName + ": " + ex.Message;
                }
            }
        }
    }
}
