using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Whether the far face of a collider the projectile is already inside costs a second
    /// wall.
    ///
    /// The rule it replaces asked the CHORD of the collider, and that was right for a
    /// barrel and wrong for the commonest shape in the maps: a trailer, a gantry crane, a
    /// stack of pipes and a truck body are each one non-convex mesh drawn round the whole
    /// prop, so the chord is metres where the sheet is a millimetre. Every solid region
    /// inside such a mesh then handed out a correct entry charge and a spurious exit
    /// charge, and crossing two real sheets of one trailer cost up to four.
    ///
    /// The table below is the whole of the new rule, and the cases in it are the objects
    /// the raid evidence was argued over.
    /// </summary>
    public class ObstacleFarFaceTests
    {
        /// <summary>The shipped threshold, so the cases below are argued at the number
        /// the game actually runs.</summary>
        private static readonly double Cavity =
            ObstacleReference.TuningOf(ObstacleReference.Parse(ObstacleReference.DefaultJsonc))
                .ShellCavityMm;

        [Fact]
        public void The_threshold_is_the_books_and_not_a_second_number()
        {
            Assert.Equal(150, Cavity, 6);
        }

        /// <summary>
        /// A solid barrier was charged for its whole depth on the way in — the collider
        /// IS the path there — so its exit is the same crossing seen from the other side.
        /// True however far the projectile travelled inside it: a metre of log is one
        /// metre of wood, not two logs.
        /// </summary>
        [Theory]
        [InlineData(3)]
        [InlineData(600)]
        [InlineData(4000)]
        public void A_solid_barrier_never_charges_its_far_face(double insideMm)
        {
            Assert.False(ObstacleModel.FarFaceCharges(solid: true, hasAnchor: true,
                anchorDistMm: insideMm, chordMm: insideMm, cavityMm: Cavity));
        }

        /// <summary>
        /// The case the fix exists for. One non-convex mesh over a whole trailer: the
        /// chord is two metres, the sheet is three millimetres, and the old rule read the
        /// chord and charged the exit as a second skin. What actually happened to the
        /// projectile is that it flew three millimetres inside the mesh since it struck
        /// it — one sheet, one charge.
        /// </summary>
        [Fact]
        public void One_sheet_of_a_multi_sheet_mesh_is_not_charged_twice()
        {
            Assert.False(ObstacleModel.FarFaceCharges(solid: false, hasAnchor: true,
                anchorDistMm: 3, chordMm: 2000, cavityMm: Cavity));
        }

        /// <summary>
        /// And the far sheet of the same trailer, met after crossing the open bed, is a
        /// genuine second crossing: its own entry face is a forward hit and charges by
        /// itself, and its exit is three millimetres further on and does not.
        /// </summary>
        [Fact]
        public void The_far_sheet_of_the_same_mesh_charges_its_own_entry_only()
        {
            // exit of the far sheet: three millimetres since its entry
            Assert.False(ObstacleModel.FarFaceCharges(solid: false, hasAnchor: true,
                anchorDistMm: 3, chordMm: 2000, cavityMm: Cavity));
        }

        /// <summary>
        /// A drum is outline around air and pays twice, which is the behaviour the old
        /// chord rule got right and this must not lose. The anchor says the same thing
        /// the chord did — six hundred millimetres of nothing between two skins.
        /// </summary>
        [Fact]
        public void A_drum_still_pays_for_both_of_its_walls()
        {
            Assert.True(ObstacleModel.FarFaceCharges(solid: false, hasAnchor: true,
                anchorDistMm: 580, chordMm: 600, cavityMm: Cavity));
        }

        /// <summary>A loader's tyre is the same statement at a larger radius: tread going
        /// in, tread coming out.</summary>
        [Fact]
        public void A_tyre_pays_for_tread_in_and_tread_out()
        {
            Assert.True(ObstacleModel.FarFaceCharges(solid: false, hasAnchor: true,
                anchorDistMm: 870, chordMm: 900, cavityMm: Cavity));
        }

        /// <summary>
        /// A door leaf is two skins over a frame and its collider is about 50 mm deep.
        /// Both rules agree it is one crossing — the entry face is charged the book's
        /// `DoorWalls` for both skins and the exit is free — and the anchor keeps it that
        /// way rather than promoting the leaf to a drum.
        /// </summary>
        [Fact]
        public void A_door_leaf_stays_one_crossing()
        {
            Assert.False(ObstacleModel.FarFaceCharges(solid: false, hasAnchor: true,
                anchorDistMm: 51, chordMm: 51, cavityMm: Cavity));
        }

        /// <summary>
        /// No anchor — a fragment born inside the object, or a chain the engine released
        /// early — falls back to the chord, exactly the rule the module always had. Both
        /// sides of the threshold, because the fallback has to be the OLD behaviour and
        /// not a quiet "free".
        /// </summary>
        [Theory]
        [InlineData(51, false)]
        [InlineData(149.9, false)]
        [InlineData(150, true)]
        [InlineData(600, true)]
        public void With_no_anchor_the_chord_still_decides(double chordMm, bool charges)
        {
            Assert.Equal(charges, ObstacleModel.FarFaceCharges(solid: false, hasAnchor: false,
                anchorDistMm: 0, chordMm: chordMm, cavityMm: Cavity));
        }

        /// <summary>
        /// The threshold itself is inclusive on the anchor for the same reason it is on
        /// the chord: one rule, one comparison, asked of a better length.
        /// </summary>
        [Theory]
        [InlineData(149.9, false)]
        [InlineData(150, true)]
        public void The_anchor_meets_the_same_threshold_as_the_chord(double insideMm,
            bool charges)
        {
            Assert.Equal(charges, ObstacleModel.FarFaceCharges(solid: false, hasAnchor: true,
                anchorDistMm: insideMm, chordMm: 5000, cavityMm: Cavity));
        }
    }
}
