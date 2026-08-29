using System.Globalization;
using System.Threading;
using EFT.Ballistics;
using PLATE.Client.Overlay;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The obstacle journal line. It exists to be grepped out of a raid log months
    /// later, so its shape is a contract: the fields are in a fixed order, absent
    /// numbers say "-" rather than "0", and nothing is formatted through the machine's
    /// locale.
    /// </summary>
    public class WallJournalTests
    {
        [Fact]
        public void A_penetration_reports_both_speeds_and_the_turn()
        {
            var line = WallJournal.Line("WoodThick", "door_wood_01", 10f, 0.54f, 0.28f, 0.1f,
                "762x39_PS", 715f, 445f, 2.4f, WallJournal.EffectIntact, penetrated: true);

            Assert.Equal(
                "wall WoodThick(door_wood_01) pl=10 pc=0.54 rc=0.28 fc=0.1 | 762x39_PS " +
                "v_in=715 -> v_out=445 dev=2.4 | effect=intact PEN:T",
                line);
        }

        /// <summary>
        /// Nothing came out, so there is no speed and no angle to report. A zero there
        /// would read as "it came out at zero", which is a different claim.
        /// </summary>
        [Fact]
        public void A_stop_reports_dashes_rather_than_zeroes()
        {
            var line = WallJournal.Line("Concrete", "wall_concrete_04", 100f, 0f, 0.41f, 0.16f,
                "9x19_PST", 380f, null, null, WallJournal.EffectStopped, penetrated: false);

            Assert.Contains("v_out=- dev=-", line);
            Assert.EndsWith("effect=stopped PEN:F", line);
        }

        /// <summary>
        /// Every state the engine can leave a collision in has to map to something, and
        /// the four that matter map to the four words the smoke checklist reads.
        /// </summary>
        [Theory]
        [InlineData(EftBulletClass.EBulletState.DeviationHit, false, "intact")]
        [InlineData(EftBulletClass.EBulletState.DeviationHit, true, "deformed")]
        [InlineData(EftBulletClass.EBulletState.FragmentationHit, false, "destroyed")]
        [InlineData(EftBulletClass.EBulletState.StopHit, false, "stopped")]
        [InlineData(EftBulletClass.EBulletState.RicochetHit, false, "ricochet")]
        public void Every_bullet_state_maps_to_an_effect(EftBulletClass.EBulletState state,
            bool deformed, string expected)
        {
            Assert.Equal(expected, WallJournal.EffectOf(state.ToString(), deformed));
        }

        /// <summary>
        /// "Deformed" is the one word vanilla cannot produce — the engine has no concept
        /// of a wall changing a bullet — so it may only ever come from the obstacle
        /// model, and only on a pass-through. A stop or a bounce is not upgraded by it.
        /// </summary>
        [Fact]
        public void Deformed_only_overrides_a_pass_through()
        {
            Assert.Equal("stopped",
                WallJournal.EffectOf(nameof(EftBulletClass.EBulletState.StopHit), deformed: true));
            Assert.Equal("ricochet",
                WallJournal.EffectOf(nameof(EftBulletClass.EBulletState.RicochetHit), deformed: true));
        }

        /// <summary>
        /// A state nobody mapped is reported as itself rather than dressed up as
        /// something the game did not say. Flying is reachable: a projectile that got
        /// through with its deviation budget spent spawns no child and the engine leaves
        /// the state alone.
        /// </summary>
        [Fact]
        public void An_unmapped_state_is_reported_as_itself()
        {
            Assert.Equal("flying",
                WallJournal.EffectOf(nameof(EftBulletClass.EBulletState.Flying), deformed: false));
        }

        /// <summary>
        /// The line is grepped, so a decimal comma from a ru or de locale would break
        /// every filter written against it — and this mod is developed on a ru machine.
        /// </summary>
        [Fact]
        public void Numbers_do_not_follow_the_machine_locale()
        {
            var was = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                var line = WallJournal.Line("MetalThin", "fence_01", 4f, 0.58f, 0.21f, 0.07f,
                    "9x19_PST", 380.5f, 340.25f, 1.75f, WallJournal.EffectDeformed, true);

                Assert.DoesNotContain(",", line);
                Assert.Contains("pc=0.58", line);
                Assert.Contains("dev=1.8", line);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = was;
            }
        }

        [Fact]
        public void Missing_names_degrade_instead_of_throwing()
        {
            var line = WallJournal.Line(null, null, 0f, 0f, 0f, 0f, null,
                100f, null, null, null, false);

            Assert.StartsWith("wall ?(?)", line);
            Assert.Contains("| ? v_in=100", line);
            Assert.Contains("effect=? PEN:F", line);
        }
    }
}
