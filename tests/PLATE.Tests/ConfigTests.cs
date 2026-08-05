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
        ///
        /// The sibling is looked up by key across the whole file rather than within the
        /// section, because four of these groups are deliberately read across two: the
        /// ": Player" half sits in "7. Player Survivability" so that everything deciding
        /// how long you last is in one place, and PMC and Scav stay in "3. Blood &amp;
        /// trauma". What must not happen is a side going missing altogether.
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
                    if (!keys.Any(k => k.Key == stem + side))
                    {
                        missing.Add(stem + side);
                    }
                }
            }

            Assert.True(missing.Count == 0, "incomplete per-category groups: " +
                                            string.Join(", ", missing));
        }

        /// <summary>
        /// The ": Player" knobs that moved into section 7 are bound where the
        /// reorganisation put them, and each retired key they moved from is still bound
        /// in the section it came from — that read is the only thing standing between an
        /// upgrading player and a silently reset tuning. The old section is pinned per
        /// key: four came from "3. Blood &amp; trauma" (v4) and the damage scale from
        /// "2. Ballistics" (v5).
        /// </summary>
        [Fact]
        public void Moved_player_knobs_can_still_read_their_old_home()
        {
            if (Skip) return;

            foreach (var entry in new ConfigEntryBase[]
            {
                PlateClientConfig.DeathForPlayer,
                PlateClientConfig.InternalBleedPlayer,
                PlateClientConfig.BleedRatePlayer,
                PlateClientConfig.FractureCollapsePlayer,
                PlateClientConfig.DamageScalePlayer,
            })
            {
                Assert.StartsWith("7.", entry.Definition.Section);
            }

            var cameFrom = new (ConfigEntryBase Legacy, string Section, string Key)[]
            {
                (PlateClientConfig.LegacyDeathForPlayer, "3. Blood & trauma",
                    "Death from bleeding: Player"),
                (PlateClientConfig.LegacyInternalBleedPlayer, "3. Blood & trauma",
                    "Internal bleeding: Player"),
                (PlateClientConfig.LegacyBleedRatePlayer, "3. Blood & trauma",
                    "Bleed rate: Player"),
                (PlateClientConfig.LegacyFractureCollapsePlayer, "3. Blood & trauma",
                    "Fracture collapse: Player"),
                (PlateClientConfig.LegacyDamageScalePlayer, "2. Ballistics",
                    "Damage scale: Player"),
            };

            foreach (var (legacy, section, key) in cameFrom)
            {
                Assert.Equal(section, legacy.Definition.Section);
                Assert.Equal(key, legacy.Definition.Key);
            }

            // it changes how a destroyed limb behaves for bots too, so it is not a
            // player-survivability switch and must not drift back into section 7
            Assert.StartsWith("3.", PlateClientConfig.LimbHitsCanKill.Definition.Section);
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

        /// <summary>
        /// The survivability overrides exist to be switched on by someone who wants them.
        /// Out of the box they must change nothing at all: a default that drifted here
        /// would quietly rewrite every raid for everyone who never opened section 7, and
        /// it would look like the wound model had changed rather than a switch.
        /// </summary>
        [Fact]
        public void Survivability_overrides_are_off_by_default()
        {
            if (Skip) return;

            Assert.False(PlateClientConfig.PreventPlayerDeath.Value);
            Assert.True(PlateClientConfig.LimbHitsCanKill.Value);
            Assert.Equal(1f, PlateClientConfig.PlayerBleedChance.Value);
            Assert.True(PlateClientConfig.PlayerOrganCrits.Value);

            // a bleeding chance above 1 would invent bleedings the model never found,
            // which is the one thing this knob is not for
            AssertRange(PlateClientConfig.PlayerBleedChance, 0f, 1f);

            foreach (var entry in new ConfigEntryBase[]
            {
                PlateClientConfig.PreventPlayerDeath,
                PlateClientConfig.PlayerBleedChance,
                PlateClientConfig.PlayerOrganCrits,
            })
            {
                Assert.StartsWith("7.", entry.Definition.Section);
            }
        }

        /// <summary>
        /// Every override is about the local player, so anyone the mod cannot identify as
        /// you has to come out of the gates untouched — a null target reaching these from
        /// a grenade or a fragment must not silently become invulnerable.
        /// </summary>
        [Fact]
        public void Overrides_leave_everyone_who_is_not_you_alone()
        {
            if (Skip) return;

            Assert.True(PlateBloodManager.OrganCritsAllowed(null));
            Assert.Equal(1f, PlateBloodManager.BleedChanceFactor(null));
            Assert.True(PlateBloodManager.BleedRollPasses(null));
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
