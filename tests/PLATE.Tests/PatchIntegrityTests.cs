using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EFT.Ballistics;
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
        /// Whether a wound bleeds is decided by writing the chance onto the hit, which
        /// only works while the hit is what carries it. If the field moves off
        /// DamageInfo, the write compiles nowhere else and every wound quietly
        /// falls back to the cartridge's own number — the same silent-no-op shape as the
        /// template-id bug above, and just as invisible in a raid.
        /// </summary>
        [Fact]
        public void A_hit_still_carries_its_own_bleed_chance()
        {
            if (Skip) return;

            var field = typeof(DamageInfo).GetField("HeavyBleedingDelta");

            Assert.NotNull(field);
            Assert.Equal(typeof(float), field.FieldType);
            Assert.False(field.IsInitOnly, "the bleed chance has to be writable per hit");
        }

        /// <summary>
        /// The obstacle module patches the base collider's two virtuals, and it must
        /// never reach a body through them. What guarantees that is that
        /// BodyPartCollider declares its own overrides: Harmony rewrites the method it
        /// is given, and a call that dispatches to an override never enters the base
        /// body. If a game update drops either override, every hit on a person starts
        /// going through the wall model — the guards in the patch bodies are the
        /// backstop, this is the thing that makes them unnecessary.
        /// </summary>
        [Theory]
        [InlineData("IsPenetrated")]
        [InlineData("Deflects")]
        public void The_body_overrides_the_collider_virtuals_the_wall_model_patches(string name)
        {
            if (Skip) return;

            var baseType = PatchTargets.BallisticCollider;
            var bodyType = PatchTargets.BodyPartCollider;
            Assert.NotNull(baseType);
            Assert.NotNull(bodyType);
            Assert.True(baseType.IsAssignableFrom(bodyType));

            var onBase = AccessTools.Method(baseType, name);
            Assert.NotNull(onBase);
            Assert.True(onBase.IsVirtual, $"{Describe(onBase)} is not virtual");

            var onBody = bodyType.GetMethods(AccessTools.all | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == name);
            Assert.True(onBody != null,
                $"BodyPartCollider no longer overrides {name}: the base patch would " +
                "start deciding hits on people as if they were walls");
        }

        /// <summary>
        /// The obstacle model reads the material and the level straight off the collider
        /// — the presets are a designer's palette and any scene object may carry any
        /// value, so the reference book is keyed on the pair.
        /// </summary>
        [Fact]
        public void A_collider_still_says_what_it_is_and_how_hard_it_is()
        {
            if (Skip) return;

            var type = PatchTargets.BallisticCollider;
            var material = AccessTools.Property(type, "TypeOfMaterial");
            var level = AccessTools.Field(type, "PenetrationLevel");

            Assert.NotNull(material);
            Assert.Equal("MaterialType", material.PropertyType.Name);
            Assert.NotNull(level);
            Assert.Equal(typeof(float), level.FieldType);
        }

        /// <summary>
        /// Harmony binds a prefix's arguments to the target's parameters BY NAME, and the
        /// prefix on the projectile constructor rewrites three of them. A rename in a
        /// future SPT would not fail to compile and would not fail to patch: the hook
        /// would simply stop receiving the arguments it exists to change, and both the
        /// barrier exit state and every ricochet would quietly revert to vanilla speeds
        /// with nothing in any log to say so.
        ///
        /// `parent` is in the list for the same reason: it is what identifies whose
        /// collision spawned this projectile, and without it the prefix cannot tell a
        /// muzzle shot from a bullet coming out of a door.
        ///
        /// The mass and the calibre are there because a fragment is a smaller projectile
        /// than the bullet it broke off, and saying so at the spawn is also what gives
        /// vanilla's own drag the fragment's sectional density instead of the parent's.
        /// </summary>
        [Theory]
        [InlineData("origin")]
        [InlineData("direction")]
        [InlineData("speed")]
        [InlineData("bulletMassGram")]
        [InlineData("bulletDiameterMilimeters")]
        [InlineData("parent")]
        public void The_projectile_constructor_still_names_its_arguments(string name)
        {
            if (Skip) return;

            var create = PatchTargets.Bullet_Create;
            Assert.NotNull(create);
            Assert.True(create.IsStatic, "Shot.Create is expected to be the static factory");

            var names = create.GetParameters().Select(p => p.Name).ToList();
            Assert.True(names.Contains(name),
                $"Shot.Create no longer has a parameter called '{name}': " +
                string.Join(", ", names));
        }

        /// <summary>
        /// And the three it rewrites must still be the types the prefix takes them by
        /// reference as — a float speed turned into a Vector3 velocity would fail to
        /// patch loudly, but a Vector3 turned into a Vector2 would not.
        /// </summary>
        [Fact]
        public void The_projectile_constructor_still_takes_a_point_a_direction_and_a_speed()
        {
            if (Skip) return;

            var byName = PatchTargets.Bullet_Create.GetParameters()
                .ToDictionary(p => p.Name, p => p.ParameterType);

            Assert.Equal(typeof(UnityEngine.Vector3), byName["origin"]);
            Assert.Equal(typeof(UnityEngine.Vector3), byName["direction"]);
            Assert.Equal(typeof(float), byName["speed"]);
            Assert.Equal(typeof(float), byName["bulletMassGram"]);
            Assert.Equal(typeof(float), byName["bulletDiameterMilimeters"]);
            Assert.Equal(PatchTargets.Shot, byName["parent"]);
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
        /// Two hooks on one method are ordinary — the survivability prefix and the organ
        /// model's postfix both sit on ApplyDamage — and each has to keep its own row and
        /// its own counter. Registering by target first folded the second into the first,
        /// leaving Hit(label) with nothing to find: the hook reported "never ran" while
        /// running on every hit, which is the exact lie this telemetry exists to prevent.
        /// </summary>
        [Fact]
        public void Two_hooks_on_one_method_count_separately()
        {
            if (Skip) return;

            var harmony = new Harmony("plate.tests.two-hooks");
            var target = AccessTools.Method(typeof(PatchIntegrityTests), nameof(Dummy));

            PatchStats.Track(harmony, target, "shared-first");
            PatchStats.Track(harmony, target, "shared-second");

            PatchStats.Hit("shared-first");
            PatchStats.Hit("shared-second");
            PatchStats.Hit("shared-second");

            var report = string.Join(Environment.NewLine, PatchStats.Report());

            Assert.Contains("shared-first", report);
            Assert.Contains("shared-second", report);
            Assert.Contains("shared-first", report.Split('\n')
                .First(l => l.Contains("shared-first") && l.Contains("fired 1")));
            Assert.Contains("shared-second", report.Split('\n')
                .First(l => l.Contains("shared-second") && l.Contains("fired 2")));
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

            // MonoMod cannot detour ActiveHealthController.ApplyDamage outside the Unity
            // runtime — its prepare step reaches for BCL members net471's mscorlib does
            // not have. It is the target that cannot be detoured here, not the patch, so
            // every hook aimed at it fails, all three of them.
            //
            // This used to assert a count of one, on the reasoning that the first hook
            // took the error and the rest attached to an already-patched method. That
            // reasoning was reading a telemetry bug: PatchStats keyed rows by target
            // before label, so the second and third hooks folded into the first one's row
            // and only one label was ever reported. They were all failing the whole time.
            //
            // In game all three attach: the raid journal's hook report carries no failures
            // and the features they drive demonstrably run.
            var onApplyDamage = new HashSet<string>
            {
                "GuaranteedBleedPostfix",
                "CentralWoundPostfix",
                "overlay:HealthApplyDamagePostfix",
            };

            var actual = new HashSet<string>(PatchStats.FailedLabels());

            Assert.True(actual.SetEquals(onApplyDamage),
                "patch application changed. Failures outside ApplyDamage: " +
                string.Join(", ", actual.Except(onApplyDamage)) +
                "; expected but absent: " + string.Join(", ", onApplyDamage.Except(actual)) +
                "; total failures: " + actual.Count + " (" + string.Join(", ", actual) + ")" +
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
