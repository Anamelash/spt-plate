using PLATE.Client.Ballistics;
using UnityEngine;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Which way round a hitbox is.
    ///
    /// The organ zones are thirds of the torso boxes, so everything downstream rests on
    /// knowing which local axis of a box runs across the body. Get it wrong and the liver
    /// sits on the left, the spine plate reads as a shoulder, and nothing in the game
    /// looks broken — the damage numbers stay plausible. Hence a file of tests for what
    /// is, on the face of it, three dot products.
    ///
    /// Pure arithmetic: no game assemblies, no Unity runtime beyond Vector3 and Mathf.
    /// </summary>
    public class AnatomyTests
    {
        private static readonly Vector3 Right = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 Up = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 Fwd = new Vector3(0f, 0f, 1f);

        /// <summary>A box built straight from measurements, without a collider to hang it on.</summary>
        private static Anatomy.Box Make(Vector3 sizeM, Vector3 localX, Vector3 localY,
            Vector3 localZ, Vector3 bodyUp, Vector3 bodyRight)
        {
            return new Anatomy.Box
            {
                Valid = true,
                Axes = Anatomy.Resolve(localX, localY, localZ, bodyUp, bodyRight),
                SizeLocal = sizeM,
                SizeMm = sizeM * 1000f,
                CenterLocal = Vector3.zero,
            };
        }

        [Fact]
        public void An_upright_box_reads_off_its_own_axes()
        {
            var a = Anatomy.Resolve(Right, Up, Fwd, Up, Right);

            Assert.Equal(0, a.Width);
            Assert.Equal(1, a.Height);
            Assert.Equal(2, a.Depth);
            Assert.Equal(1f, a.WidthSign);
            Assert.Equal(1f, a.HeightSign);
            Assert.Equal(1f, a.DepthSign);
        }

        /// <summary>
        /// The reason none of this is a constant. RibcageUp measures 0.386 x 0.093 x 0.310
        /// in its own local order — read in that order it is a chest 93 mm tall, which is
        /// not a chest. Pointed at the character it comes out 386 wide, 310 tall and 93
        /// deep, which is.
        /// </summary>
        [Fact]
        public void The_dumped_ribcage_only_makes_anatomical_sense_once_the_axes_are_pointed_at_the_body()
        {
            // local Z is this box's up, local Y runs front to back
            var box = Make(new Vector3(0.386f, 0.093f, 0.310f),
                localX: Right, localY: Fwd, localZ: Up, bodyUp: Up, bodyRight: Right);

            Assert.Equal(386f, box.WidthMm, 0);
            Assert.Equal(310f, box.HeightMm, 0);
            Assert.Equal(93f, box.DepthMm, 0);
        }

        /// <summary>
        /// Width is the character's right, not the world's. Turn the target around and
        /// the same local axis has to change sign, or every liver hit lands on someone
        /// facing the other way.
        /// </summary>
        [Fact]
        public void Width_follows_the_character_and_not_the_world()
        {
            var facingAway = Anatomy.Resolve(Right, Up, Fwd, Up, bodyRight: -Right);

            Assert.Equal(0, facingAway.Width);
            Assert.Equal(-1f, facingAway.WidthSign);
        }

        [Fact]
        public void The_liver_third_stays_on_the_bodys_own_right_when_the_target_turns_around()
        {
            var size = new Vector3(0.213f, 0.028f, 0.325f);
            var entry = new Vector3(0.08f, 0f, 0f); // well into +x of the collider

            var facingUs = Make(size, Right, Fwd, Up, Up, Right);
            var facingAway = Make(size, Right, Fwd, Up, Up, -Right);

            Assert.Equal(1, Anatomy.Third(Anatomy.Fractions(facingUs, entry).x));
            Assert.Equal(-1, Anatomy.Third(Anatomy.Fractions(facingAway, entry).x));
        }

        /// <summary>
        /// A box lying on its side is still a box. Height is taken first because up is
        /// the least ambiguous direction on a body; width and depth follow from it.
        /// </summary>
        [Fact]
        public void A_box_turned_on_its_side_still_resolves()
        {
            var a = Anatomy.Resolve(localX: Up, localY: -Right, localZ: Fwd,
                bodyUp: Up, bodyRight: Right);

            Assert.Equal(0, a.Height);
            Assert.Equal(1f, a.HeightSign);
            Assert.Equal(1, a.Width);
            Assert.Equal(-1f, a.WidthSign);
            Assert.Equal(2, a.Depth);
            Assert.Equal(1f, a.DepthSign);
        }

        /// <summary>
        /// A box sitting at 45 degrees has two axes equally close to the character's
        /// right. Whichever one wins, the three roles must still land on three different
        /// axes — a duplicate would silently measure width twice and never measure depth.
        /// </summary>
        [Fact]
        public void An_awkward_angle_never_gives_one_axis_two_jobs()
        {
            var d = 0.70710678f;
            var a = Anatomy.Resolve(new Vector3(d, 0f, d), Up, new Vector3(-d, 0f, d),
                Up, Right);

            Assert.NotEqual(a.Width, a.Height);
            Assert.NotEqual(a.Width, a.Depth);
            Assert.NotEqual(a.Height, a.Depth);
        }

        [Fact]
        public void A_point_on_a_face_reads_as_half_a_side()
        {
            var box = Make(new Vector3(0.30f, 0.10f, 0.40f), Right, Fwd, Up, Up, Right);
            var f = Anatomy.Fractions(box, new Vector3(0.15f, 0.05f, 0.20f));

            Assert.Equal(0.5f, f.x, 3);
            Assert.Equal(0.5f, f.z, 3); // local y is depth here
            Assert.Equal(0.5f, f.y, 3);
        }

        [Fact]
        public void The_box_centre_offset_is_where_the_fractions_start_from()
        {
            var box = Make(new Vector3(0.30f, 0.10f, 0.40f), Right, Fwd, Up, Up, Right);
            box.CenterLocal = new Vector3(0.05f, 0f, 0f);

            Assert.Equal(0f, Anatomy.Fractions(box, new Vector3(0.05f, 0f, 0f)).x, 3);
        }

        [Fact]
        public void Thirds_split_at_a_sixth_either_side_of_the_middle()
        {
            Assert.Equal(0, Anatomy.Third(0f));
            Assert.Equal(0, Anatomy.Third(0.16f));
            Assert.Equal(0, Anatomy.Third(-0.16f));
            Assert.Equal(1, Anatomy.Third(0.17f));
            Assert.Equal(-1, Anatomy.Third(-0.17f));
            Assert.Equal(1, Anatomy.Third(0.5f));
        }

        /// <summary>
        /// SpineTop is two colliders under one name. Without this rule "a channel through
        /// SpineTop severs the cord" would make the whole upper back, shoulders included,
        /// instantly fatal — the measured back box is half a metre across.
        /// </summary>
        [Fact]
        public void Only_the_thin_spine_collider_is_the_spine()
        {
            var plate = Make(new Vector3(0.226f, 0.017f, 0.325f), Right, Fwd, Up, Up, Right);
            var lower = Make(new Vector3(0.220f, 0.013f, 0.325f), Right, Fwd, Up, Up, Right);
            var back = Make(new Vector3(0.502f, 0.100f, 0.350f), Right, Fwd, Up, Up, Right);

            Assert.True(Anatomy.IsSpinePlate(plate));
            Assert.True(Anatomy.IsSpinePlate(lower));
            Assert.False(Anatomy.IsSpinePlate(back));
        }

        /// <summary>
        /// The volume-shaped torso colliders stay on the far side of the line. These are
        /// the ones the rule would have to misread for a spine zone to leak out of the
        /// spine, and they clear it by a factor of two or more.
        /// </summary>
        [Fact]
        public void The_torso_volumes_are_not_mistaken_for_spine_plates()
        {
            var others = new[]
            {
                new Vector3(0.200f, 0.200f, 0.280f), // Pelvis
                new Vector3(0.200f, 0.050f, 0.280f), // PelvisBack
                new Vector3(0.233f, 0.194f, 0.310f), // RibcageUp
                new Vector3(0.386f, 0.093f, 0.310f), // RibcageUp, the wide one
                new Vector3(0.223f, 0.240f, 0.065f), // RibcageLow
                new Vector3(0.223f, 0.184f, 0.154f), // SideChestDown
                new Vector3(0.502f, 0.100f, 0.350f), // SpineTop, the upper back
            };

            foreach (var size in others)
            {
                var box = Make(size, Right, Fwd, Up, Up, Right);
                Assert.False(Anatomy.IsSpinePlate(box),
                    $"{size.x:0.000}x{size.y:0.000}x{size.z:0.000} read as a spine plate " +
                    $"at {box.ThinnestMm:0} mm");
            }
        }

        /// <summary>
        /// Why the thickness rule may only ever be asked of SpineTop and SpineDown:
        /// RibcageLow has a 28 mm panel and SideChestUp an 11 mm one, both thinner than
        /// the line. The rule tells two colliders of the same name apart; it does not say
        /// what a collider is.
        /// </summary>
        [Fact]
        public void Thin_panels_live_outside_the_spine_too()
        {
            var ribPanel = Make(new Vector3(0.213f, 0.028f, 0.325f), Right, Fwd, Up, Up, Right);
            var sideChest = Make(new Vector3(0.089f, 0.190f, 0.011f), Right, Fwd, Up, Up, Right);

            Assert.True(Anatomy.IsSpinePlate(ribPanel));
            Assert.True(Anatomy.IsSpinePlate(sideChest));
        }
    }
}
