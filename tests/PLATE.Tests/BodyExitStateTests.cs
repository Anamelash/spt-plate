using System;
using PLATE.Client.Ballistics;
using PLATE.Client.Patches;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// What a resolved BODY collision hands the projectile the engine is about to build.
    ///
    /// The same engine behaviour that discarded a barrier's exit speed applies here: the
    /// trajectory table is formed when a projectile is created, from the arguments it is
    /// created with, and every tick afterwards overwrites the projectile's velocity out
    /// of that table. A speed written into a freshly spawned child therefore survived
    /// until that child's first tick — invisible inside a torso, where the next collider
    /// is met within the same tick and the interpolation returns almost all of it, and
    /// worth a through-and-through arriving at the next person with nearly the speed it
    /// entered the first one.
    ///
    /// Nothing about the numbers changed; only when they are applied. So the decision is
    /// pinned as the pure function it is — the drag law, the fragment split, and the two
    /// floors that keep an inert projectile from flying on.
    /// </summary>
    public class BodyExitStateTests : IClassFixture<GameFixture>
    {
        private readonly GameFixture _game;

        public BodyExitStateTests(GameFixture game)
        {
            _game = game;
        }

        private bool Skip => !_game.Available;

        // the shipped defaults, so the numbers below mean what they mean in a raid
        private static AmmoDataCache.WoundParams Params() => new AmmoDataCache.WoundParams
        {
            Enabled = true,
            GelDepthK = 2700,
            GelStopVelocity = 50,
            ExpansionDepthFactor = 0.4,
            ExpansionAreaFactor = 1.35,
            BodyDepthMm = 250,
            WoundVolumePerHp = 381,
            TcVelocityCenter = 600,
            TcVelocityWidth = 80,
            TcEnergyPerHp = 74,
            TcFragBonus = 0.5,
            EnergyCapPerHp = 7,
        };

        // 7.62x51 M80 as the game has it, at the impact velocity seen in raid logs
        private const float MassG = 9.5f;
        private const float DiaMm = 7.85f;
        private const float V = 803f;
        private const float X = 0.79f;

        /// <summary>A chest, entry plate to back plate, as the chord measurement gives it.</summary>
        private const float ChestMm = 250f;

        /// <summary>
        /// A projectile that came out the far side leaves at the speed the drag law gives
        /// it, and that is the whole content of the fix: the number was always this one,
        /// it simply never reached the flight.
        /// </summary>
        [Fact]
        public void A_pass_through_launches_at_the_wound_models_exit_speed()
        {
            var p = Params();
            var l = ClientWoundModel.ChannelMm(MassG, DiaMm, V, X, p);
            Assert.True(l > ChestMm, "the test needs a projectile that gets out");

            // v·exp(−T/λ), λ = L/ln(v/v_stop) — written out rather than called, so the
            // assertion is against the law and not against the implementation of it
            var lambda = l / (float)Math.Log(V / p.GelStopVelocity);
            var expected = V * (float)Math.Exp(-ChestMm / lambda);

            var speed = BodyExit.LaunchSpeed(MassG, DiaMm, V, X, ChestMm, 1f, 0f, p);

            Assert.Equal(expected, speed, 2);

            // and the anchor: a rifle round spends about half its speed crossing a chest
            Assert.InRange(speed, 0.45f * V, 0.55f * V);
        }

        /// <summary>
        /// One that does not get out launches inert. Not at zero — a spawn with no speed
        /// has no direction either, and the engine builds the whole trajectory from
        /// direction × speed — but at a speed that drops it where it was born, which is
        /// what "it stayed inside" looks like from the outside.
        /// </summary>
        [Fact]
        public void A_projectile_that_does_not_get_out_launches_inert()
        {
            var p = Params();
            const float slow = 90f;
            var l = ClientWoundModel.ChannelMm(MassG, DiaMm, slow, X, p);
            Assert.True(l < ChestMm, "the test needs a channel that ends inside the part");

            Assert.Equal(BodyExit.InertSpeedMs,
                BodyExit.LaunchSpeed(MassG, DiaMm, slow, X, ChestMm, 1f, 0f, p));
        }

        /// <summary>A contact impact cuts no tissue at all, so there is no channel to
        /// leave by however deep the part is.</summary>
        [Fact]
        public void A_contact_impact_launches_inert()
        {
            var p = Params();
            var atRest = (float)p.GelStopVelocity;

            Assert.Equal(BodyExit.InertSpeedMs,
                BodyExit.LaunchSpeed(MassG, DiaMm, atRest, X, 1f, 1f, 0f, p));
        }

        /// <summary>The tissue only ever takes: a body cannot launch a projectile faster
        /// than the one that entered it, at any path length.</summary>
        [Theory]
        [InlineData(1f)]
        [InlineData(50f)]
        [InlineData(ChestMm)]
        [InlineData(900f)]
        public void No_path_through_a_body_gives_speed_back(float pathMm)
        {
            var p = Params();

            Assert.True(BodyExit.LaunchSpeed(MassG, DiaMm, V, X, pathMm, 1f, 0f, p) <= V);
        }

        /// <summary>More tissue, less speed — monotone, because the drag law is.</summary>
        [Fact]
        public void More_tissue_leaves_less_speed()
        {
            var p = Params();
            var thin = BodyExit.LaunchSpeed(MassG, DiaMm, V, X, 60f, 1f, 0f, p);
            var thick = BodyExit.LaunchSpeed(MassG, DiaMm, V, X, ChestMm, 1f, 0f, p);

            Assert.True(thin > thick, $"60 mm left {thin:0}, {ChestMm:0} mm left {thick:0}");
        }

        // --- Fragments ---

        /// <summary>
        /// Every fragment of a batch is the same fragment — same mass, same calibre, and
        /// therefore the same exit speed against the same remaining tissue. That is what
        /// lets one answer, computed once before the first of them is created, cover the
        /// whole batch.
        /// </summary>
        [Fact]
        public void Every_fragment_of_a_batch_is_the_same_fragment()
        {
            var p = Params();
            const int n = 5;
            const float share = 0.4f;

            BodyExit.FragmentSplit(MassG, DiaMm, share, n, out var mass0, out var dia0);
            var speed0 = BodyExit.LaunchSpeed(mass0, dia0, V, X, 0.5f * ChestMm, 1f, 0.3f, p);

            for (var i = 1; i < n; i++)
            {
                BodyExit.FragmentSplit(MassG, DiaMm, share, n, out var mass, out var dia);
                Assert.Equal(mass0, mass);
                Assert.Equal(dia0, dia);
                Assert.Equal(speed0,
                    BodyExit.LaunchSpeed(mass, dia, V, X, 0.5f * ChestMm, 1f, 0.3f, p));
            }
        }

        /// <summary>
        /// A fragment lighter than the floor is inert: its energy was already deposited
        /// in the part as the wound model's fragmentation bonus, and letting it fly on
        /// would spend the same energy a second time.
        /// </summary>
        [Fact]
        public void A_fragment_below_the_minimum_mass_is_inert()
        {
            var p = Params();
            const float minMassG = 0.3f;

            BodyExit.FragmentSplit(MassG, DiaMm, 0.4f, 16, out var mass, out var dia);
            Assert.True(mass < minMassG, $"the test needs a fragment under the floor, got {mass:0.000} g");

            Assert.Equal(BodyExit.InertSpeedMs,
                BodyExit.LaunchSpeed(mass, dia, V, X, 0.5f * ChestMm, 1f, minMassG, p));
        }

        /// <summary>...and the projectile that merely crossed the part has no such floor:
        /// it is not a splinter, it is the bullet.</summary>
        [Fact]
        public void The_overpenetration_child_has_no_mass_floor()
        {
            var p = Params();
            const float pelletG = 0.05f;
            const float pelletMm = 2.3f;

            var speed = BodyExit.LaunchSpeed(pelletG, pelletMm, V, X, 8f, 1f, 0f, p);

            Assert.True(speed > BodyExit.InertSpeedMs, $"a light pellet through 8 mm left {speed:0.0}");
        }

        /// <summary>The split takes mass, and the calibre follows from it by the cube
        /// root — the fragment is made of the same metal as the bullet was.</summary>
        [Fact]
        public void A_fragment_keeps_the_parents_density()
        {
            BodyExit.FragmentSplit(MassG, DiaMm, 0.4f, 4, out var mass, out var dia);

            var parentDensity = MassG / (DiaMm * DiaMm * DiaMm);
            var fragDensity = mass / (dia * dia * dia);

            Assert.Equal(parentDensity, fragDensity, 6);
        }

        /// <summary>A bigger batch is made of smaller pieces: the parent's mass is
        /// divided, never multiplied.</summary>
        [Fact]
        public void A_bigger_batch_is_made_of_smaller_pieces()
        {
            BodyExit.FragmentSplit(MassG, DiaMm, 0.4f, 2, out var few, out _);
            BodyExit.FragmentSplit(MassG, DiaMm, 0.4f, 8, out var many, out _);

            Assert.True(many < few);
            Assert.True(few < MassG);
        }

        /// <summary>A batch nobody sized cannot divide a fragment down to nothing and
        /// take its diameter — and with it the whole channel — with it.</summary>
        [Fact]
        public void The_split_cannot_reach_zero()
        {
            BodyExit.FragmentSplit(MassG, DiaMm, 0.4f, int.MaxValue, out var mass, out var dia);

            Assert.True(mass > 0f);
            Assert.True(dia > 0f);
        }

        // --- The two sources of an exit state, and what they decline ---

        /// <summary>
        /// Neither source claims a projectile nobody resolved a collision for. The spawn
        /// hook asks the environment first and the body second on every projectile the
        /// game creates, muzzle shots included, so "no verdict" has to mean "leave
        /// vanilla's arguments alone" in both of them.
        /// </summary>
        [Fact]
        public void Neither_source_claims_a_projectile_with_no_parent()
        {
            if (Skip) return;

            Assert.False(ObstaclePatches.TryChildLaunch(null, UnityEngine.Vector3.forward,
                out _, out _, out _));
            Assert.False(BallisticsPatches.TryChildLaunch(null, out _, out _, out _));
        }

        /// <summary>
        /// And the body source declines a collision with no body in it. The two sources
        /// are mutually exclusive by the collider — the environment module never resolves
        /// a body's collision and this one never resolves anything else — which is why
        /// asking both in a fixed order can only ever produce one answer.
        /// </summary>
        [Fact]
        public void The_body_source_declines_a_collision_that_hit_no_body()
        {
            if (Skip) return;

            var shot = new EftBulletClass();

            Assert.False(BallisticsPatches.TryChildLaunch(shot, out _, out _, out _));
        }
    }
}
