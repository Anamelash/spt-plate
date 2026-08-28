using System;
using System.Collections.Generic;
using System.Linq;
using PLATE.Client.Ballistics;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Palletised cargo: a carrier medium with packages drawn along the path
    /// (ObstacleModel.StackFill, MODEL.md "Packed media").
    ///
    /// The three things this has to get right are geometric and were the reason the old
    /// homogeneous reading was replaced: clipping a corner must cost about what a
    /// cardboard box costs, crossing a pallet must be survivable and expensive, and
    /// firing down its long axis must usually not work. The fourth is that it is a
    /// LOTTERY — two rounds on the same line disagree — so the checks here are rates
    /// over many seeds rather than single answers, the way OrganZoneTests checks its
    /// draws.
    /// </summary>
    public class ObstacleStackTests
    {
        private static readonly ObstacleReference.Book Shipped =
            ObstacleReference.Parse(ObstacleReference.DefaultJsonc);

        private static ObstacleModel.Tuning Tuning => ObstacleReference.TuningOf(Shipped);

        /// <summary>The broadside geometry the client reads with no server on the line —
        /// the same numbers the wound channel uses, as ObstaclePatches.Read supplies
        /// them. Yaw is the whole reason the stack loop hands state forward.</summary>
        private static YawModel.Tuning Geometry =>
            ClientWoundModel.Yaw(new AmmoDataCache.WoundParams());

        /// <summary>A rigid, undeformed bullet with its yaw geometry filled in.</summary>
        private static ObstacleModel.Projectile Bullet(double massG, double diaMm, double v,
            double x = 0)
        {
            return new ObstacleModel.Projectile
            {
                MassG = massG,
                DiaMm = diaMm,
                V = v,
                X = x,
                CoreAreaFrac = 1,
                CoreMassFrac = 1,
                HardnessHv = 60,
                LengthMm = YawModel.LengthMm(massG, diaMm, Geometry),
                SideAreaMm2 = YawModel.SideAreaMm2(massG, diaMm, x, Geometry),
            };
        }

        private static ObstacleModel.Projectile Rifle(double v = 850) => Bullet(3.68, 5.6, v);

        private static ObstacleModel.Projectile Pistol(double v = 380) => Bullet(8.0, 9.0, v);

        /// <summary>
        /// A pallet of the given chord. BoxCargo is Solid, so in play the thickness is the
        /// measured collider and not the book's anchor — which is exactly what makes the
        /// corner cheap and the long axis dear, and what these numbers are about.
        /// </summary>
        private static ObstacleModel.Barrier Cargo(double chordMm, double spacingMm = 0)
        {
            Assert.True(ObstacleReference.TryBarrier(Shipped, "BoxCargo", 0, out var b),
                "BoxCargo did not resolve to a barrier");
            Assert.NotNull(b.Fill);
            b.ThicknessMm = chordMm;
            if (spacingMm > 0)
            {
                b.Fill = new ObstacleModel.StackFill
                {
                    SpacingMm = spacingMm,
                    ContentFraction = b.Fill.ContentFraction,
                    Chance = b.Fill.Chance,
                    Content = b.Fill.Content,
                };
            }

            return b;
        }

        /// <summary>How many seeds every rate in this file is measured over.</summary>
        private const int Trials = 4000;

        private struct Spread
        {
            public double ThroughRate;

            /// <summary>Mean exit velocity over ALL trials, a stop counting as zero — the
            /// honest average outcome, uncontaminated by the survivors being a different
            /// set at every spacing.</summary>
            public double MeanExitV;

            public double MinExitV;
            public double MaxExitV;
        }

        private static Spread Sample(ObstacleModel.Projectile p, ObstacleModel.Barrier b)
        {
            var through = 0;
            var sum = 0.0;
            var min = double.MaxValue;
            var max = 0.0;

            for (var seed = 0; seed < Trials; seed++)
            {
                var o = ObstacleModel.Resolve(p, b, Tuning, 1, 0.5, seed);
                sum += o.ExitV;
                if (!o.Penetrates)
                {
                    continue;
                }

                through++;
                min = Math.Min(min, o.ExitV);
                max = Math.Max(max, o.ExitV);
            }

            return new Spread
            {
                ThroughRate = (double)through / Trials,
                MeanExitV = sum / Trials,
                MinExitV = through > 0 ? min : 0,
                MaxExitV = max,
            };
        }

        /// <summary>
        /// A seed whose first <paramref name="layers"/> draws come up with exactly
        /// <paramref name="wanted"/> packages — how the acceptance table below fixes the
        /// number of inclusions instead of averaging over it.
        /// </summary>
        private static int SeedDrawing(int layers, int wanted, double chance)
        {
            for (var seed = 1; seed < 1_000_000; seed++)
            {
                var hits = 0;
                for (var i = 0; i < layers; i++)
                {
                    if (ObstacleModel.StackDraw(seed, i) < chance)
                    {
                        hits++;
                    }
                }

                if (hits == wanted)
                {
                    return seed;
                }
            }

            throw new InvalidOperationException(
                $"no seed draws {wanted} of {layers} — the draw is not uniform");
        }

        // --- The book ---

        /// <summary>
        /// Every number of the mechanism comes out of the BOOK, not out of the C#
        /// initialisers behind it. A misspelt key parses cleanly and falls back to the
        /// default, so a player's edit would be silently ignored — the failure mode the
        /// rest of this module's book tests exist to catch.
        /// </summary>
        [Fact]
        public void The_stack_constants_live_in_the_book()
        {
            Assert.Contains("\"Stack\":", ObstacleReference.DefaultJsonc);
            Assert.Contains("\"SpacingMm\":", ObstacleReference.DefaultJsonc);
            Assert.Contains("\"ContentFraction\":", ObstacleReference.DefaultJsonc);
            Assert.Contains("\"BoxCargo\":", ObstacleReference.DefaultJsonc);
            Assert.Contains("\"BoxContent\":", ObstacleReference.DefaultJsonc);

            var fill = Cargo(1200).Fill;
            Assert.True(fill.SpacingMm > 0);
            Assert.InRange(fill.ContentFraction, 0.01, 1);
            Assert.InRange(fill.Chance, 0.01, 1);
            Assert.Equal(ObstacleModel.MechPoncelet, fill.Content.Mechanism);

            // the carrier is near enough to air and the contents are not: that ordering
            // IS the model, and a book that lost it would quietly be one medium again
            var carrier = Cargo(1200);
            Assert.True(fill.Content.DensityGCm3 > carrier.DensityGCm3 * 5);
            Assert.True(fill.Content.StrengthMPa > carrier.StrengthMPa);
        }

        /// <summary>
        /// A packing whose content the book does not define would mean "no packing" at
        /// runtime — a typo silently turning palletised cargo back into empty boxes. Same
        /// argument as the name-override integrity check, and the same reason to make it
        /// impossible to ship.
        /// </summary>
        [Fact]
        public void Every_stack_names_content_the_book_defines_and_does_not_nest()
        {
            var bad = new List<string>();

            foreach (var kv in Shipped.Materials.Where(kv => kv.Value.Stack != null))
            {
                var s = kv.Value.Stack;
                if (string.IsNullOrEmpty(s.Content) ||
                    !Shipped.Materials.TryGetValue(s.Content, out var content))
                {
                    bad.Add($"{kv.Key}: content '{s.Content}' is not a material");
                    continue;
                }

                if (content.Stack != null)
                {
                    bad.Add($"{kv.Key}: content '{s.Content}' is itself packed");
                }

                if (s.SpacingMm <= 0 || s.ContentFraction <= 0 || s.ContentFraction > 1 ||
                    s.Chance <= 0 || s.Chance > 1)
                {
                    bad.Add($"{kv.Key}: nonsense spacing/fraction/chance");
                }
            }

            Assert.True(bad.Count == 0, "Broken packings: " + string.Join(", ", bad));
        }

        /// <summary>
        /// And when a book DOES carry one of those mistakes, the carrier is crossed as
        /// the plain medium it is. The fallback rule of every layer in this module: a
        /// mistake costs the feature, never the physics.
        /// </summary>
        [Theory]
        // content nobody defined
        [InlineData(300, "Nonexistent")]
        // cargo inside cargo
        [InlineData(300, "Packed")]
        // no spacing at all
        [InlineData(0, "Goods")]
        // no content named
        [InlineData(300, "")]
        public void A_packing_the_book_cannot_resolve_leaves_a_plain_medium(int spacing,
            string content)
        {
            var json = @"{ ""Materials"": {
                ""Packed"": { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 0.1,
                              ""DensityGCm3"": 0.03, ""Anchors"": { ""0"": 30 },
                              ""Stack"": { ""SpacingMm"": " + spacing + @",
                                         ""ContentFraction"": 0.3, ""Chance"": 0.5,
                                         ""Content"": """ + content + @""" } },
                ""Goods"":  { ""Mechanism"": ""poncelet"", ""StrengthMPa"": 1,
                              ""DensityGCm3"": 0.40, ""Anchors"": { ""0"": 90 } } },
                ""Version"": 1 }";

            var book = ObstacleReference.Parse(json);
            Assert.True(ObstacleReference.TryBarrier(book, "Packed", 0, out var b));
            Assert.Null(b.Fill);
        }

        /// <summary>
        /// Nothing else in the book grew a stack by accident. GenericSoft especially: its
        /// 9 459 colliders are books, garbage piles and sacks, and those really are
        /// homogeneous — the cargo rules were moved off it, not it off itself.
        /// </summary>
        [Fact]
        public void Only_palletised_cargo_is_packed()
        {
            var packed = Shipped.Materials
                .Where(kv => kv.Value.Stack != null)
                .Select(kv => kv.Key)
                .ToList();

            Assert.Equal(new[] { "BoxCargo" }, packed);
            Assert.Null(Shipped.Materials["GenericSoft"].Stack);
        }

        /// <summary>
        /// A medium with no packing goes down exactly the path it always did, seed or no
        /// seed. The regression that lets the whole mechanism be added at all.
        /// </summary>
        [Fact]
        public void An_unpacked_medium_ignores_the_seed_entirely()
        {
            Assert.True(ObstacleReference.TryBarrier(Shipped, "GenericSoft", 0, out var b));
            Assert.Null(b.Fill);

            var p = Rifle();
            var plain = ObstacleModel.Resolve(p, b, Tuning, 0.8);

            for (var seed = 0; seed < 32; seed++)
            {
                var o = ObstacleModel.Resolve(p, b, Tuning, 0.8, 0.5, seed);
                Assert.Equal(plain.Penetrates, o.Penetrates);
                Assert.Equal(plain.ExitV, o.ExitV);
                Assert.Equal(plain.ExitYaw, o.ExitYaw);
                Assert.Equal(plain.Deviation, o.Deviation);
            }
        }

        // --- The three geometries ---

        /// <summary>
        /// Clipping a corner is still a cardboard box: 300 mm of stack is one draw, and
        /// whichever way it falls the round comes out with most of its speed. This is the
        /// half the old homogeneous reading got worst — 300 mm of GenericSoft at 0.40
        /// stopped rifle rounds.
        /// </summary>
        [Fact]
        public void Clipping_a_corner_of_a_pallet_is_a_cardboard_box()
        {
            var corner = Sample(Rifle(), Cargo(300));

            Assert.Equal(1.0, corner.ThroughRate);
            Assert.InRange(corner.MinExitV, 640, 780);
            Assert.InRange(corner.MaxExitV, 780, 850);
        }

        /// <summary>
        /// Crossing a loaded pallet is survivable and expensive: most rounds get through,
        /// and what gets through has lost well over half its speed. Both halves matter —
        /// a barrier nothing crosses and a barrier nothing notices are equally wrong.
        /// </summary>
        [Fact]
        public void Crossing_a_pallet_is_survivable_and_expensive()
        {
            var across = Sample(Rifle(), Cargo(1200));

            Assert.InRange(across.ThroughRate, 0.85, 0.99);
            Assert.InRange(across.MeanExitV, 250, 420);
        }

        /// <summary>
        /// Down the long axis it usually does not work. Nothing in the model says so
        /// directly: the path is longer, so there are more draws, so more cargo — which
        /// is the entire argument for drawing packages instead of averaging them.
        /// </summary>
        [Fact]
        public void Shooting_down_the_length_of_a_pallet_usually_stops_the_round()
        {
            var across = Sample(Rifle(), Cargo(1200));
            var along = Sample(Rifle(), Cargo(2400));

            Assert.InRange(along.ThroughRate, 0.02, 0.35);
            Assert.True(along.ThroughRate < across.ThroughRate / 2,
                $"along {along.ThroughRate:P0} is not far below across {across.ThroughRate:P0}");
        }

        /// <summary>
        /// And it is a lottery. Same round, same line, same pallet: some seeds thread the
        /// voids and come out fast, some meet three boxes of goods and stop. A
        /// homogeneous medium cannot do this at all, and it is what shooting into stacked
        /// cargo looks like.
        /// </summary>
        [Fact]
        public void The_same_pallet_answers_differently_shot_to_shot()
        {
            var across = Sample(Rifle(), Cargo(1200));

            Assert.True(across.ThroughRate > 0 && across.ThroughRate < 1,
                "a pallet that always answers the same way is not a lottery");
            Assert.True(across.MaxExitV > across.MinExitV * 3,
                $"exit velocities {across.MinExitV:F0}-{across.MaxExitV:F0} barely spread");
        }

        // --- The acceptance table ---

        /// <summary>
        /// What a crossing costs, package by package: the calibration table the mechanism
        /// was signed off on. Four draws over 1200 mm, and the answer is fixed by hunting
        /// a seed that draws exactly n of them, so this is the deterministic spine under
        /// the rates above.
        /// </summary>
        [Theory]
        // 5.45 BS at 850: the carrier alone barely slows it, each package costs ~200 m/s,
        // and the fourth stops it
        [InlineData(3.68, 5.6, 850, 0, 660, 750)]
        [InlineData(3.68, 5.6, 850, 1, 470, 550)]
        [InlineData(3.68, 5.6, 850, 2, 275, 340)]
        [InlineData(3.68, 5.6, 850, 3, 130, 175)]
        // 9x19 ball at 380: same ladder an octave down
        [InlineData(8.0, 9.0, 380, 0, 290, 340)]
        [InlineData(8.0, 9.0, 380, 1, 228, 270)]
        [InlineData(8.0, 9.0, 380, 2, 160, 196)]
        [InlineData(8.0, 9.0, 380, 3, 85, 108)]
        public void The_cost_of_crossing_a_pallet_by_package_count(double massG, double diaMm,
            double v, int packages, double lowMs, double highMs)
        {
            var pallet = Cargo(1200);
            var seed = SeedDrawing(4, packages, pallet.Fill.Chance);
            var o = ObstacleModel.Resolve(Bullet(massG, diaMm, v), pallet, Tuning, 1, 0.5, seed);

            Assert.True(o.Penetrates, $"{packages} packages stopped it outright");
            Assert.InRange(o.ExitV, lowMs, highMs);
        }

        /// <summary>Four packages in 1200 mm is the end of the road for both.</summary>
        [Theory]
        [InlineData(3.68, 5.6, 850)]
        [InlineData(8.0, 9.0, 380)]
        public void A_pallet_that_draws_cargo_every_layer_stops_the_round(double massG,
            double diaMm, double v)
        {
            var pallet = Cargo(1200);
            var seed = SeedDrawing(4, 4, pallet.Fill.Chance);

            Assert.False(ObstacleModel.Resolve(Bullet(massG, diaMm, v), pallet, Tuning, 1,
                0.5, seed).Penetrates);
        }

        /// <summary>
        /// What a stack reports having crossed is what it crossed. The refusal used to
        /// carry the depth a fresh projectile would reach in an infinite slab of the
        /// carrier — a different medium from the one that actually stopped it, and a
        /// number with no relation to the path — so the journal printed refusals like
        /// "reached 15913 of 2194 mm", deeper than the object is thick. Nothing about
        /// the verdict was wrong; the report was.
        /// </summary>
        [Fact]
        public void A_stack_reports_how_far_it_was_crossed_and_no_further()
        {
            var pallet = Cargo(1200);
            var seed = SeedDrawing(4, 4, pallet.Fill.Chance);

            var held = ObstacleModel.Resolve(Bullet(8.0, 9.0, 380), pallet, Tuning, 1,
                0.5, seed);

            Assert.False(held.Penetrates);
            Assert.InRange(held.DepthMm, 0, held.PathMm);
        }

        // --- The spacing ---

        /// <summary>
        /// The expected amount of cargo per metre of path does not depend on the spacing,
        /// because a package is a FRACTION of the layer it sits in rather than a fixed
        /// thickness. That is what makes the spacing a grain size instead of a strength
        /// knob, and it is arithmetic rather than an accident: fraction·chance·path,
        /// whichever way the path is sliced.
        /// </summary>
        [Theory]
        [InlineData(600.0)]
        [InlineData(300.0)]
        [InlineData(150.0)]
        [InlineData(100.0)]
        public void The_expected_cargo_per_metre_does_not_depend_on_the_spacing(double spacing)
        {
            var fill = Cargo(1200, spacing).Fill;
            const double path = 1200.0;

            var total = 0.0;
            for (var seed = 0; seed < Trials; seed++)
            {
                var remaining = path;
                for (var i = 0; remaining > 1e-6; i++)
                {
                    var step = Math.Min(spacing, remaining);
                    remaining -= step;
                    if (ObstacleModel.StackDraw(seed, i) < fill.Chance)
                    {
                        total += fill.ContentFraction * step;
                    }
                }
            }

            var expected = fill.ContentFraction * fill.Chance * path;
            Assert.InRange(total / Trials, expected * 0.95, expected * 1.05);
        }

        /// <summary>
        /// What the spacing DOES cost, honestly measured and pinned as a band rather than
        /// claimed to be nothing. Slicing the same medium finer is not free once yaw
        /// exists: every layer boundary asks the destabilisation question again, and the
        /// sum of Work over slices exceeds the Work of one crossing. Halving the spacing
        /// therefore takes a little more speed — under a fifth of it, which is the honest
        /// residue of the mechanism and the reason MODEL.md calls the spacing a weak
        /// lever and not a knob.
        /// </summary>
        [Fact]
        public void Halving_the_spacing_moves_the_answer_by_less_than_a_fifth()
        {
            var coarse = Sample(Rifle(), Cargo(1200, 300));
            var fine = Sample(Rifle(), Cargo(1200, 150));

            var drift = (coarse.MeanExitV - fine.MeanExitV) / coarse.MeanExitV;

            Assert.True(drift > 0,
                "finer slicing should cost slightly more, not less — see MODEL.md");
            Assert.True(drift < 0.20,
                $"spacing drift {drift:P1} is a knob, not a grain size");
        }

        /// <summary>
        /// The shipped spacing is the one the table above was measured at, and the
        /// default has to keep answering the same. A book edit that changed it silently
        /// would move every number in this file.
        /// </summary>
        [Fact]
        public void The_shipped_spacing_is_the_one_the_table_was_measured_at()
        {
            var shipped = Sample(Rifle(), Cargo(1200));
            var explicitly = Sample(Rifle(), Cargo(1200, Cargo(1200).Fill.SpacingMm));

            Assert.Equal(shipped.ThroughRate, explicitly.ThroughRate);
            Assert.Equal(shipped.MeanExitV, explicitly.MeanExitV, 6);
        }

        // --- What accumulates between layers ---

        /// <summary>
        /// Yaw carries from one layer to the next, which is the whole reason the loop
        /// hands the projectile's state forward instead of summing costs. A bullet that
        /// clipped a corner is still nearly nose-on; one that crossed a whole pallet
        /// comes out well on its way over, and every package it met after the first was
        /// paid for at that larger presented area.
        /// </summary>
        [Fact]
        public void Yaw_accumulates_from_layer_to_layer()
        {
            var p = Rifle();
            var pallet = Cargo(1200);
            var chance = pallet.Fill.Chance;

            var corner = ObstacleModel.Resolve(p, Cargo(300), Tuning, 1, 0.5,
                SeedDrawing(1, 0, chance));
            var across = ObstacleModel.Resolve(p, pallet, Tuning, 1, 0.5,
                SeedDrawing(4, 0, chance));

            Assert.True(across.ExitYaw > corner.ExitYaw * 3,
                $"corner {corner.ExitYaw:F2} vs pallet {across.ExitYaw:F2}: the layers " +
                "are not handing yaw forward");
            Assert.InRange(across.ExitYaw, 0.3, 1.0);
        }

        /// <summary>
        /// Deflections do NOT add up — each layer throws the round off in its own
        /// direction, so they combine in quadrature like a random walk. A pallet
        /// therefore deflects more than a corner does but far less than four corners in a
        /// row would.
        /// </summary>
        [Fact]
        public void Layer_deflections_add_in_quadrature_and_not_in_series()
        {
            var p = Rifle();
            var chance = Cargo(1200).Fill.Chance;

            var corner = ObstacleModel.Resolve(p, Cargo(300), Tuning, 1, 0.5,
                SeedDrawing(1, 0, chance));
            var across = ObstacleModel.Resolve(p, Cargo(1200), Tuning, 1, 0.5,
                SeedDrawing(4, 0, chance));

            Assert.True(across.Deviation > corner.Deviation);
            Assert.True(across.Deviation < 4 * corner.Deviation,
                "four layers deflecting in series would be a sum, not a random walk");
        }

        /// <summary>
        /// A projectile that says nothing about its shape yaws not at all, inside a stack
        /// as everywhere else — the safe default every caller that does not model yaw
        /// relies on.
        /// </summary>
        [Fact]
        public void A_shapeless_projectile_leaves_a_stack_as_it_entered_it()
        {
            var p = Rifle();
            p.LengthMm = 0;
            p.SideAreaMm2 = 0;

            var o = ObstacleModel.Resolve(p, Cargo(1200), Tuning, 1, 0.5, 7);
            Assert.Equal(0, o.ExitYaw);
        }

        // --- The draw itself ---

        /// <summary>
        /// The draw is pure in its seed. A collision is resolved more than once — the
        /// ricochet gate asks before the penetration verdict does — so the same seed has
        /// to lay the same boxes out both times, and a replayed shot has to replay.
        /// </summary>
        [Fact]
        public void The_same_seed_lays_the_cargo_out_the_same_way()
        {
            var p = Rifle();
            var pallet = Cargo(1200);

            for (var seed = 0; seed < 64; seed++)
            {
                var a = ObstacleModel.Resolve(p, pallet, Tuning, 1, 0.5, seed);
                var b = ObstacleModel.Resolve(p, pallet, Tuning, 1, 0.5, seed);
                Assert.Equal(a.Penetrates, b.Penetrates);
                Assert.Equal(a.ExitV, b.ExitV);
            }
        }

        /// <summary>
        /// And it is a fair coin. Nothing here needs the draw to be cryptographic, but a
        /// mixer that clumped — every even seed empty, say — would turn the lottery into
        /// a pattern the player could learn.
        /// </summary>
        [Fact]
        public void The_draw_is_uniform_across_seeds_and_across_layers()
        {
            for (var layer = 0; layer < 8; layer++)
            {
                var hits = 0;
                for (var seed = 0; seed < 20000; seed++)
                {
                    if (ObstacleModel.StackDraw(seed, layer) < 0.5)
                    {
                        hits++;
                    }
                }

                Assert.InRange(hits / 20000.0, 0.47, 0.53);
            }

            // and the layers of one seed are independent of each other: a mixer that
            // repeated itself down the stack would make every pallet all-or-nothing
            var agree = 0;
            for (var seed = 0; seed < 20000; seed++)
            {
                if (ObstacleModel.StackDraw(seed, 0) < 0.5 ==
                    ObstacleModel.StackDraw(seed, 1) < 0.5)
                {
                    agree++;
                }
            }

            Assert.InRange(agree / 20000.0, 0.47, 0.53);
        }

        // --- Degenerate input ---

        /// <summary>
        /// The stack path answers the same nonsense the plain one does: nothing to
        /// compute with is a barrier that holds, not a crash and not a free pass.
        /// </summary>
        [Theory]
        [InlineData(0, 5.6, 850)]
        [InlineData(3.68, 0, 850)]
        [InlineData(3.68, 5.6, 0)]
        public void A_projectile_with_no_state_does_not_cross_a_pallet(double massG,
            double diaMm, double v)
        {
            Assert.False(ObstacleModel.Resolve(Bullet(massG, diaMm, v), Cargo(1200),
                Tuning, 1, 0.5, 3).Penetrates);
        }

        /// <summary>
        /// An absurd spacing must coarsen the graining, not hang the game: the layer
        /// count is capped and the last layer swallows the rest of the path. A book is a
        /// file the player edits.
        /// </summary>
        [Fact]
        public void A_microscopic_spacing_is_capped_rather_than_walked()
        {
            var o = ObstacleModel.Resolve(Rifle(), Cargo(4000, 0.001), Tuning, 1, 0.5, 11);
            Assert.InRange(o.PathMm, 3999, 4001);
        }
    }
}
