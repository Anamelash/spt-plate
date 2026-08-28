using System.Globalization;
using System.Linq;
using System.Threading;
using PLATE.Client.Overlay;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The survey aggregator: hits in, one line per prop per window out. Pure — the
    /// clock and the stamp are arguments — so the window algebra and the formatting
    /// are checked without a raid.
    ///
    /// The tests drain with `everything: true` at the end of each case so state never
    /// leaks between them (the aggregator is a static, like the code it feeds).
    /// </summary>
    public class ObstacleSurveyTests
    {
        private const string T = "12:00:00.0";

        private static string One(float drainAt)
        {
            var lines = ObstacleSurvey.Drain(drainAt, T);
            Assert.Single(lines);
            return lines[0];
        }

        private static void Reset()
        {
            ObstacleSurvey.Drain(0, T, everything: true);
        }

        [Fact]
        public void A_window_holds_for_fifteen_seconds_and_then_closes()
        {
            Reset();
            ObstacleSurvey.Note("factory4_day", "Door_01", "-", "MetalThin", 4, 100, 1, true, now: 10);

            Assert.Empty(ObstacleSurvey.Drain(10, T));
            Assert.Empty(ObstacleSurvey.Drain(24.9f, T));
            var line = One(25f);
            Assert.Contains("n=1", line);
            Assert.Contains("obj=Door_01", line);

            // and it is gone: a later drain has nothing left to say
            Assert.Empty(ObstacleSurvey.Drain(100, T));
        }

        [Fact]
        public void Repeat_hits_on_one_prop_collapse_into_one_averaged_line()
        {
            Reset();
            ObstacleSurvey.Note("factory4_day", "Barrel", "-", "MetalThick", 69, 100, 1, true, 0);
            ObstacleSurvey.Note("factory4_day", "Barrel", "-", "MetalThick", 69, 200, 1, true, 1);
            ObstacleSurvey.Note("factory4_day", "Barrel", "-", "MetalThick", 69, 600, 1, true, 2);

            var line = One(15f);
            Assert.Contains("n=3", line);
            Assert.Contains("avg=300", line);
            Assert.Contains("min=100", line);
            Assert.Contains("max=600", line);
            Assert.Contains("miss=0", line);
        }

        [Fact]
        public void Different_props_and_different_levels_stay_apart()
        {
            Reset();
            ObstacleSurvey.Note("f", "Barrel", "-", "MetalThick", 69, 100, 1, true, 0);
            ObstacleSurvey.Note("f", "Gate", "-", "MetalThick", 69, 100, 1, true, 0);
            // the same name at another level is another prop: the level picks the
            // book thickness, so mixing them would average two different objects
            ObstacleSurvey.Note("f", "Barrel", "-", "MetalThick", 7, 100, 1, true, 0);

            Assert.Equal(3, ObstacleSurvey.Drain(15f, T).Count);
        }

        /// <summary>
        /// Half the maps name the collider itself nothing at all — "metal",
        /// "LOD0Collider" — so the parent is part of the key: two different cupboards
        /// both carrying a collider called "metal" must not average into one row.
        /// </summary>
        [Fact]
        public void The_parent_tells_two_anonymous_colliders_apart()
        {
            Reset();
            ObstacleSurvey.Note("f", "metal", "cupboard_01/room", "MetalThin", 4, 20, 1, true, 0);
            ObstacleSurvey.Note("f", "metal", "locker_03/hall", "MetalThin", 4, 900, 1, true, 0);

            var lines = ObstacleSurvey.Drain(15f, T);
            Assert.Equal(2, lines.Count);
            Assert.Contains(lines, l => l.Contains("par=cupboard_01/room "));
            Assert.Contains(lines, l => l.Contains("par=locker_03/hall "));
        }

        /// <summary>Scene nodes are named with spaces ("Terrain Ballistic"); the par
        /// field must stay one awk column anyway.</summary>
        [Fact]
        public void Spaces_in_the_parent_become_underscores()
        {
            Reset();
            ObstacleSurvey.Note("f", "grass", "Terrain Ballistic/Map Root", "GrassHigh", 0,
                0, 1, false, 0);

            Assert.Contains("par=Terrain_Ballistic/Map_Root ", One(15f));
        }

        /// <summary>
        /// The normal-reduced chord is the object's own thickness: three oblique hits
        /// on one 100 mm wall measure three different chords and one norm.
        /// </summary>
        [Fact]
        public void The_norm_column_strips_obliquity()
        {
            Reset();
            ObstacleSurvey.Note("f", "Wall", "-", "Concrete", 100, 100, 1.0, true, 0);
            ObstacleSurvey.Note("f", "Wall", "-", "Concrete", 100, 200, 0.5, true, 0);
            ObstacleSurvey.Note("f", "Wall", "-", "Concrete", 100, 400, 0.25, true, 0);

            var line = One(15f);
            Assert.Contains("avg=233.3", line);
            Assert.Contains("norm=100", line);
        }

        [Fact]
        public void Unmeasured_hits_are_counted_and_kept_out_of_the_statistics()
        {
            Reset();
            ObstacleSurvey.Note("f", "Fence", "-", "MetalThin", 4, 50, 1, true, 0);
            ObstacleSurvey.Note("f", "Fence", "-", "MetalThin", 4, 0, 1, false, 1);
            ObstacleSurvey.Note("f", "Fence", "-", "MetalThin", 4, 0, 1, false, 2);

            var line = One(15f);
            Assert.Contains("n=3", line);
            Assert.Contains("miss=2", line);
            Assert.Contains("avg=50", line);
        }

        /// <summary>
        /// A prop the probe never lands on is a finding, not a formatting accident:
        /// the line still comes out, with dashes where the numbers would be.
        /// </summary>
        [Fact]
        public void A_prop_with_no_measurements_prints_dashes()
        {
            Reset();
            ObstacleSurvey.Note("f", "Grass", "-", "GrassHigh", 0, 0, 1, false, 0);

            var line = One(15f);
            Assert.Contains("avg=- min=- max=- norm=-", line);
            Assert.Contains("miss=1", line);
        }

        [Fact]
        public void The_same_prop_after_a_flush_opens_a_new_window()
        {
            Reset();
            ObstacleSurvey.Note("f", "Barrel", "-", "MetalThick", 69, 100, 1, true, 0);
            Assert.Single(ObstacleSurvey.Drain(15f, T));

            ObstacleSurvey.Note("f", "Barrel", "-", "MetalThick", 69, 300, 1, true, 20);
            var line = One(35f);
            Assert.Contains("n=1", line);
            Assert.Contains("avg=300", line);
        }

        /// <summary>The file is read by grep and awk; a decimal comma would shear
        /// every script run on it.</summary>
        [Fact]
        public void Lines_do_not_depend_on_the_locale()
        {
            Reset();
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                ObstacleSurvey.Note("f", "Wall", "-", "Concrete", 100, 123.45, 0.5, true, 0);
                var line = One(15f);
                Assert.Contains("avg=123.5", line);
                Assert.DoesNotContain(",", line.Split(new[] { "obj=" }, System.StringSplitOptions.None)[0]);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Fact]
        public void Everything_flushes_at_raid_end_regardless_of_age()
        {
            Reset();
            ObstacleSurvey.Note("f", "A", "-", "MetalThin", 4, 1, 1, true, 0);
            ObstacleSurvey.Note("f", "B", "-", "MetalThin", 4, 1, 1, true, 0);

            var lines = ObstacleSurvey.Drain(0.1f, T, everything: true);
            Assert.Equal(2, lines.Count);
            Assert.Equal(2, lines.Select(l => l.Split(new[] { "obj=" }, System.StringSplitOptions.None)[1]).Distinct().Count());
        }
    }
}
