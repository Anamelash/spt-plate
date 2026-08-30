using System;
using System.Collections.Generic;
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

        // xunit gives every test class its own fixture instance and runs the classes in
        // parallel, but what the fixture sets up — the logger, the bound config, the
        // assembly resolver — is process-global. Three classes racing to bind the same
        // static config put entries from one ConfigFile into another and threw on the
        // duplicate. Once per process, under a lock.
        private static readonly object Gate = new object();
        private static bool _prepared;

        public GameFixture()
        {
            ManagedDir = Path.Combine(GameDir.Path, "EscapeFromTarkov_Data", "Managed");
            Available = Directory.Exists(ManagedDir) &&
                        File.Exists(Path.Combine(ManagedDir, "Assembly-CSharp.dll"));

            if (!Available)
            {
                return;
            }

            lock (Gate)
            {
                if (_prepared)
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
                _prepared = true;
            }
        }

        private Assembly ResolveFromManaged(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(ManagedDir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private Harmony _applied;
        private Harmony _classZeroApplied;

        /// <summary>
        /// Applies only the item-construction compatibility patch. The functional
        /// class-zero tests need it before creating an ArmorPlate, but applying every
        /// ballistics detour that early can race unrelated arithmetic tests under the
        /// desktop CLR, whose Unity ECall stubs are not executable outside the game.
        /// </summary>
        public Harmony ApplyClassZeroArmorPatchOnce()
        {
            if (_classZeroApplied != null)
            {
                return _classZeroApplied;
            }

            _classZeroApplied = new Harmony("plate.tests.apply");

            // A real protected collider makes ArmorComponent localize its zone names
            // while the constructor is still adding item attributes. Localization's
            // font cache uses KeyValuePair.Deconstruct, which Unity supplies but the
            // desktop net471 host does not. Keep the real component constructor and
            // replace only its localized display list in tests.
            var localizedZones = AccessTools.Method(typeof(EFT.InventoryLogic.ArmorComponent),
                nameof(EFT.InventoryLogic.ArmorComponent.UniqueLocalizedZones));
            _classZeroApplied.Patch(localizedZones,
                prefix: new HarmonyMethod(typeof(GameFixture), nameof(SkipLocalizedZones)));

            ClassZeroArmorPatches.Apply(_classZeroApplied);
            return _classZeroApplied;
        }

        private static bool SkipLocalizedZones(ref IEnumerable<string> __result)
        {
            __result = Array.Empty<string>();
            return false;
        }

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

            var harmony = ApplyClassZeroArmorPatchOnce();

            // GrenadePatches is deliberately absent: it is a transpiler, and MonoMod
            // cannot prepare one outside the Unity runtime (its detour path reaches for
            // BCL members net471's mscorlib does not have). Everything else detours
            // fine here, which is what makes this test worth running at all.
            ShotLifecyclePatches.Apply(harmony);
            BallisticsPatches.Apply(harmony);
            ObstaclePatches.Apply(harmony);
            BloodPatches.Apply(harmony);
            HealthTabPatch.Apply(harmony);
            CripplePatches.Apply(harmony);
            OverlayPatches.Apply(harmony);

            _applied = harmony;
            return harmony;
        }
    }
}
