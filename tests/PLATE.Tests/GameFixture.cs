using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PLATE.Client;
using PLATE.Client.Patches;

namespace PLATE.Tests
{
    /// <summary>
    /// Loads the game assemblies and brings the client to the state it has just before
    /// it patches anything: logger attached, config bound. Shared across the test class
    /// so the assemblies load once.
    ///
    /// If the game is not installed the fixture reports Available=false and the tests
    /// skip: these are developer-machine tests, and a machine without a game copy
    /// should not produce a red build.
    /// </summary>
    public class GameFixture
    {
        public bool Available { get; }

        public string ManagedDir { get; }

        public GameFixture()
        {
            ManagedDir = Path.Combine(GameDir.Path, "EscapeFromTarkov_Data", "Managed");
            Available = Directory.Exists(ManagedDir) &&
                        File.Exists(Path.Combine(ManagedDir, "Assembly-CSharp.dll"));

            if (!Available)
            {
                return;
            }

            // Assembly-CSharp drags in the rest of the Unity assemblies; the test host
            // has no probing path for them
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromManaged;

            Plugin.Log = Logger.CreateLogSource("PLATE.Tests");

            var cfgPath = Path.Combine(Path.GetTempPath(),
                "plate-tests-" + Guid.NewGuid().ToString("N") + ".cfg");
            PlateClientConfig.Bind(new ConfigFile(cfgPath, true));
        }

        private Assembly ResolveFromManaged(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(ManagedDir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private Harmony _applied;

        /// <summary>
        /// Applies the same patch set the plugin applies at startup, in the same order,
        /// exactly once per test run. Patch failures are recorded by PatchStats rather
        /// than thrown — the tests assert on the recorded result.
        ///
        /// Patches are never removed afterwards: Harmony's own unpatch path throws on
        /// this runtime, and the process is torn down at the end of the run anyway.
        /// Applying once also keeps the duplicate-patch assertion meaningful.
        /// </summary>
        public Harmony ApplyAllPatchesOnce()
        {
            if (_applied != null)
            {
                return _applied;
            }

            var harmony = new Harmony("plate.tests.apply");

            // GrenadePatches is deliberately absent: it is a transpiler, and MonoMod
            // cannot prepare one outside the Unity runtime (its detour path reaches for
            // BCL members net471's mscorlib does not have). Everything else detours
            // fine here, which is what makes this test worth running at all.
            BallisticsPatches.Apply(harmony);
            BloodPatches.Apply(harmony);
            HealthTabPatch.Apply(harmony);
            CripplePatches.Apply(harmony);
            OverlayPatches.Apply(harmony);

            _applied = harmony;
            return harmony;
        }
    }
}
