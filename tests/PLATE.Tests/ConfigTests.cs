using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using PLATE.Client;
using PLATE.Client.Blood;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Shape of the F12 settings. Cheap to check, and the mistakes it catches
    /// (a knob group missing a side, a range that lets a category become immortal)
    /// are invisible until someone plays a raid with them.
    /// </summary>
    public class ConfigTests : IClassFixture<GameFixture>
    {
        private readonly GameFixture _game;

        public ConfigTests(GameFixture game)
        {
            _game = game;
        }

        private bool Skip => !_game.Available;

        private const string PlayerSuffix = ": Player";

        /// <summary>
        /// Anything split per side must be split all the way: half a group means one
        /// faction silently keeps the default while the other two are tunable.
        /// </summary>
        [Fact]
        public void Per_category_knobs_come_in_complete_sets()
        {
            if (Skip) return;

            var keys = PlateClientConfig.BleedRatePlayer.ConfigFile.Keys.ToList();
            var groups = keys.Where(k => k.Key.EndsWith(PlayerSuffix)).ToList();

            Assert.True(groups.Count >= 5,
                $"expected the known per-category groups, found {groups.Count}");

            var missing = new List<string>();
            foreach (var player in groups)
            {
                var stem = player.Key.Substring(0, player.Key.Length - PlayerSuffix.Length);
                foreach (var side in new[] { ": PMC", ": Scav" })
                {
                    if (!keys.Any(k => k.Section == player.Section && k.Key == stem + side))
                    {
                        missing.Add(player.Section + "/" + stem + side);
                    }
                }
            }

            Assert.True(missing.Count == 0, "incomplete per-category groups: " +
                                            string.Join(", ", missing));
        }

        /// <summary>
        /// Bleeding may be switched off entirely for a side; damage may not — a zero
        /// damage scale is not a tuning option, it is an invulnerable faction.
        /// </summary>
        [Fact]
        public void Tuning_multiplier_ranges_are_intentional()
        {
            if (Skip) return;

            foreach (var entry in new[]
            {
                PlateClientConfig.BleedRatePlayer,
                PlateClientConfig.BleedRatePmc,
                PlateClientConfig.BleedRateScav,
            })
            {
                AssertRange(entry, 0f, 10f);
            }

            foreach (var entry in new[]
            {
                PlateClientConfig.DamageScalePlayer,
                PlateClientConfig.DamageScalePmc,
                PlateClientConfig.DamageScaleScav,
            })
            {
                AssertRange(entry, 0.1f, 10f);
            }
        }

        /// <summary>
        /// Splitting a knob per side must not quietly reset everyone's tuning: the v3
        /// migration carries the value saved under the retired key into the three that
        /// replaced it. That read only works while section and key match what the older
        /// release wrote into the cfg, so the strings are pinned here and the read is
        /// exercised against a real file.
        /// </summary>
        [Fact]
        public void Retired_damage_scale_seeds_the_per_category_ones()
        {
            if (Skip) return;

            var def = PlateClientConfig.LegacyDamageScale.Definition;
            Assert.Equal("2. Ballistics", def.Section);
            Assert.Equal("Damage scale", def.Key);

            var path = Path.Combine(Path.GetTempPath(),
                "plate-legacy-" + Guid.NewGuid().ToString("N") + ".cfg");
            File.WriteAllText(path, "[" + def.Section + "]" + Environment.NewLine +
                                    def.Key + " = 0.7" + Environment.NewLine);

            var seeded = new ConfigFile(path, true)
                .Bind(def.Section, def.Key, 1.0f, "").Value;
            File.Delete(path);

            Assert.Equal(0.7f, seeded, 3);
        }

        /// <summary>
        /// A hit whose victim cannot be identified still has to be scaled by something.
        /// </summary>
        [Fact]
        public void Unidentified_participant_falls_into_the_pmc_bucket()
        {
            if (Skip) return;

            Assert.Equal(2f, PlateBloodManager.CategoryValue(null, 1f, 2f, 3f));
        }

        private static void AssertRange(ConfigEntry<float> entry, float min, float max)
        {
            var range = entry.Description.AcceptableValues as AcceptableValueRange<float>;
            Assert.True(range != null, $"{entry.Definition.Key} has no range");
            Assert.Equal(min, range.MinValue);
            Assert.Equal(max, range.MaxValue);
        }
    }
}
