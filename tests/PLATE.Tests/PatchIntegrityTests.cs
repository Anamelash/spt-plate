using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PLATE.Client;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Structural tests against the real game assemblies.
    ///
    /// These exist because two releases shipped features that were silently dead:
    /// 0.9.0 patched an abstract method with no body (the blood bag did nothing), and
    /// 0.9.2 attached a second patch to a bool-returning applicability gate and broke
    /// every medical item in raid. Both are mechanical, both are caught here, neither
    /// needs the game to be running.
    ///
    /// Requires the game to be installed at the configured SptGameDir. Without it the
    /// tests skip rather than fail — CI without a game copy is not a red build.
    /// </summary>
    public class PatchIntegrityTests : IClassFixture<GameFixture>
    {
        private readonly GameFixture _game;

        public PatchIntegrityTests(GameFixture game)
        {
            _game = game;
        }

        private bool Skip => !_game.Available;

        [Fact]
        public void Every_patch_target_resolves()
        {
            if (Skip) return;

            var failed = PatchTargets.SelfTest();
            Assert.True(failed.Count == 0,
                "Unresolved patch targets (remapped names drifted after a game update): " +
                string.Join(", ", failed));
        }

        /// <summary>
        /// The 0.9.0 regression: ApplyItem is abstract on the generic base the health
        /// controllers derive from. An abstract method has no body, Harmony cannot
        /// compile a patch for it, and the failure is one line in a log nobody reads.
        /// </summary>
        [Fact]
        public void Every_patch_target_is_patchable()
        {
            if (Skip) return;

            var problems = new List<string>();
            foreach (var kv in ResolveAllTargets())
            {
                foreach (var m in kv.Value)
                {
                    if (m.IsAbstract)
                    {
                        problems.Add($"{kv.Key}: {Describe(m)} is ABSTRACT (no body to patch)");
                    }
                    else if (m.GetMethodBody() == null)
                    {
                        problems.Add($"{kv.Key}: {Describe(m)} has no method body");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                "Patch targets that cannot be patched:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// The item-application hooks must land on concrete controllers. If this list
        /// is empty or abstract, the transfusion item does nothing in raid and nothing
        /// says so.
        /// </summary>
        [Fact]
        public void ApplyItem_hooks_are_concrete_implementations()
        {
            if (Skip) return;

            var overloads = PatchTargets.Health_ApplyItemOverloads;

            Assert.True(overloads.Count >= 2,
                $"expected at least the two in-raid overloads, found {overloads.Count}");

            Assert.All(overloads, m =>
            {
                Assert.False(m.IsAbstract, $"{Describe(m)} is abstract");
                Assert.NotNull(m.GetMethodBody());
                Assert.False(m.DeclaringType.IsAbstract,
                    $"{Describe(m)} is declared on an abstract type, which is never instantiated");
            });

            // the in-raid controller derives from ActiveHealthController; without an
            // overload on that branch the player cannot use the item during a raid,
            // which is exactly what shipped in 0.9.0
            var inRaidBase = PatchTargets.ActiveHealthController;
            Assert.Contains(overloads, m => inRaidBase.IsAssignableFrom(m.DeclaringType));
        }

        /// <summary>
        /// The ballistic limit looks an armour item up by its template id, and the whole
        /// of stage three quietly does nothing if that lookup is handed something that is
        /// not an id. `Item.Template` compiles either way — everything has a ToString —
        /// so nothing but this catches it: a mismatch means every plate in the game falls
        /// back to its class threshold and its construction is never read.
        /// </summary>
        [Fact]
        public void An_item_can_still_be_asked_which_template_it_is()
        {
            if (Skip) return;

            var item = AccessTools.TypeByName("EFT.InventoryLogic.Item");
            Assert.NotNull(item);

            var prop = AccessTools.Property(item, "TemplateId");
            Assert.NotNull(prop);

            // a MongoID is a struct whose ToString is the id itself. Item.Template, the
            // one that reads like the right member, is an ItemTemplate object whose
            // ToString is a type name — which is what this test was written after.
            Assert.True(
                prop.PropertyType.Name.IndexOf("MongoID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prop.PropertyType == typeof(string),
                $"Item.TemplateId is {prop.PropertyType.FullName}; the armour lookup keys " +
                "on its ToString and needs an id, not an object");
        }

        /// <summary>
        /// The 0.9.2 regression: hook telemetry attached a second Harmony patch to
        /// every target. Observing something must never modify it.
        /// </summary>
        [Fact]
        public void Telemetry_does_not_patch_anything()
        {
            if (Skip) return;

            var harmony = new Harmony("plate.tests.telemetry");
            var before = CountAllPatches();

            var target = AccessTools.Method(typeof(PatchIntegrityTests), nameof(Dummy));
            PatchStats.Track(harmony, target, "dummy");
            PatchStats.Hit("dummy");

            Assert.Equal(before, CountAllPatches());
            Assert.Empty(harmony.GetPatchedMethods());
        }

        /// <summary>
        /// Applying the real patch set must not throw and must not report failures.
        /// This is the test that would have gone red for 0.9.0 before release.
        /// </summary>
        [Fact]
        public void All_patches_apply_cleanly()
        {
            if (Skip) return;

            _game.ApplyAllPatchesOnce();

            // MonoMod cannot detour these outside the Unity runtime — its prepare step
            // reaches for BCL members net471's mscorlib does not have. Verified working
            // in game: the client log has never carried a patch failure for them, and
            // the features they drive demonstrably run. The set is asserted exactly, so
            // a genuinely new failure still turns this red.
            var hostLimited = new HashSet<string> { "GuaranteedBleedPostfix" };

            var actual = new HashSet<string>(PatchStats.FailedLabels());

            Assert.True(actual.SetEquals(hostLimited),
                "patch application changed. Unexpected failures: " +
                string.Join(", ", actual.Except(hostLimited)) +
                "; expected-but-now-passing: " +
                string.Join(", ", hostLimited.Except(actual)) +
                Environment.NewLine +
                string.Join(Environment.NewLine, PatchStats.Report()));
        }

        /// <summary>
        /// No target may receive the same patch method twice. Duplicates mean a hook
        /// runs twice per call, and on methods whose result we rewrite that corrupts
        /// the return value.
        /// </summary>
        [Fact]
        public void No_target_gets_the_same_patch_twice()
        {
            if (Skip) return;

            var harmony = _game.ApplyAllPatchesOnce();

            var problems = new List<string>();
            foreach (var method in harmony.GetPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null)
                {
                    continue;
                }

                foreach (var group in new[] { info.Prefixes, info.Postfixes, info.Finalizers })
                {
                    var dupes = group
                        .Where(p => p.owner == harmony.Id)
                        .GroupBy(p => p.PatchMethod)
                        .Where(g => g.Count() > 1);

                    foreach (var d in dupes)
                    {
                        problems.Add($"{Describe(method)} <- {d.Key.Name} x{d.Count()}");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                "duplicate patches:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems));
        }

        private static void Dummy()
        {
        }

        private static int CountAllPatches() =>
            Harmony.GetAllPatchedMethods()
                .Select(Harmony.GetPatchInfo)
                .Where(i => i != null)
                .Sum(i => i.Prefixes.Count + i.Postfixes.Count +
                          i.Transpilers.Count + i.Finalizers.Count);

        private static string Describe(MethodBase m) =>
            $"{m.DeclaringType?.Name}.{m.Name}";

        /// <summary>All targets the registry exposes, flattened to method lists.</summary>
        private static Dictionary<string, List<MethodBase>> ResolveAllTargets()
        {
            var result = new Dictionary<string, List<MethodBase>>();

            var all = (System.Collections.IDictionary)typeof(PatchTargets)
                .GetField("All", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                .GetValue(null);

            foreach (System.Collections.DictionaryEntry entry in all)
            {
                var value = ((Delegate)entry.Value).DynamicInvoke();
                var methods = new List<MethodBase>();

                switch (value)
                {
                    case MethodBase mb:
                        methods.Add(mb);
                        break;
                    case IEnumerable<MethodBase> list:
                        methods.AddRange(list);
                        break;
                }

                if (methods.Count > 0)
                {
                    result[(string)entry.Key] = methods;
                }
            }

            return result;
        }
    }
}
