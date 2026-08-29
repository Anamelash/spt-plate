using System.Runtime.Serialization;
using EFT.Ballistics;
using PLATE.Client.Patches;
using UnityEngine;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// The two engine facts a whole class of bugs rested on, pinned against the real
    /// assembly.
    ///
    /// Neither needs a running game: an EftBulletClass constructs headless (its constructor only
    /// wires a delegate), a BallisticCollider can be conjured uninitialised, and a
    /// RaycastHit's point is a plain field. What cannot be built that way is anything
    /// that reaches into the native side — a collider's bounds, a hit's collider — so the
    /// code under test is written to take those as arguments rather than read them.
    /// </summary>
    public class ShotLifecycleTests : IClassFixture<GameFixture>
    {
        private readonly GameFixture _game;

        public ShotLifecycleTests(GameFixture game)
        {
            _game = game;
        }

        private bool Skip => !_game.Available;

        private static BallisticCollider Collider()
        {
            return (BallisticCollider)FormatterServices
                .GetUninitializedObject(typeof(BallisticCollider));
        }

        private static EftBulletClass Node(BallisticCollider hit, Vector3 at, EftBulletClass parent)
        {
            var shot = new EftBulletClass
            {
                Parent = parent,
                HittedBallisticCollider = hit,
            };

            var raycast = default(RaycastHit);
            raycast.point = at;
            shot.RaycastHit_0 = raycast;
            return shot;
        }

        /// <summary>
        /// The premise of the whole state-clearing fix, and the one thing about the pool
        /// nobody had checked. `method_0` nulls the engine's own references and does
        /// not touch the identity — so a record the mod keys on the shot OBJECT is not
        /// invalidated by pooling, and stamping records with an identity would not have
        /// saved them either. Clearing at birth is the only thing that works.
        /// </summary>
        [Fact]
        public void Pooling_a_shot_does_not_change_its_identity()
        {
            if (Skip) return;

            var shot = new EftBulletClass { RandomSeed = 12345 };
            shot.method_0();

            Assert.Equal(12345, shot.RandomSeed);
        }

        /// <summary>
        /// And what pooling DOES clear, which is why a released ancestor stops matching
        /// the collider walk by itself.
        /// </summary>
        [Fact]
        public void Pooling_a_shot_forgets_its_parent_and_what_it_hit()
        {
            if (Skip) return;

            var wall = Collider();
            var shot = Node(wall, new Vector3(1, 0, 0), Node(wall, Vector3.zero, null));
            shot.method_0();

            Assert.Null(shot.Parent);
            Assert.Null(shot.HittedBallisticCollider);
        }

        /// <summary>
        /// The far-face anchor walk, against the real fields. Three nodes: a bullet that
        /// entered a barrel's near wall, the child that crossed the cavity, and the exit
        /// this is asked about. The answer is the cavity, not the chord of whatever mesh
        /// the level designer drew around the whole prop.
        ///
        /// This also fails loudly if a future SPT renames `Parent` or
        /// `HittedBallisticCollider`, which is half the reason it is written against the
        /// game assembly rather than a stand-in.
        /// </summary>
        [Fact]
        public void The_walk_finds_the_last_hit_on_the_same_collider()
        {
            if (Skip) return;

            var drum = Collider();
            var entry = Node(drum, Vector3.zero, null);
            var inside = Node(Collider(), new Vector3(0.1f, 0, 0), entry);
            var exit = Node(drum, new Vector3(0.6f, 0, 0), inside);

            Assert.True(ObstaclePatches.TryAnchorDistanceMm(exit, drum, 0, out var mm));
            Assert.Equal(600, mm, 3);
        }

        /// <summary>
        /// The nearest such ancestor, not the first one. A projectile crossing the second
        /// sheet of a trailer has an older hit on the same mesh several metres back, and
        /// taking that one would charge the sheet it is leaving as if it were a drum.
        /// </summary>
        [Fact]
        public void The_walk_takes_the_nearest_ancestor_on_that_collider()
        {
            if (Skip) return;

            var trailer = Collider();
            var sheet1In = Node(trailer, Vector3.zero, null);
            var sheet1Out = Node(trailer, new Vector3(0.003f, 0, 0), sheet1In);
            var sheet2In = Node(trailer, new Vector3(2f, 0, 0), sheet1Out);
            var sheet2Out = Node(trailer, new Vector3(2.003f, 0, 0), sheet2In);

            Assert.True(ObstaclePatches.TryAnchorDistanceMm(sheet2Out, trailer, 0, out var mm));
            Assert.Equal(3, mm, 3);
        }

        /// <summary>
        /// A chain that never touched this collider — a fragment born inside something,
        /// or the far face of an object whose entry the module never saw — has no anchor,
        /// and the caller falls back to the chord rule.
        /// </summary>
        [Fact]
        public void A_chain_with_no_hit_on_this_collider_has_no_anchor()
        {
            if (Skip) return;

            var wall = Collider();
            var chain = Node(Collider(), new Vector3(1, 0, 0), Node(Collider(), Vector3.zero, null));

            Assert.False(ObstaclePatches.TryAnchorDistanceMm(chain, wall, 0, out var mm));
            Assert.Equal(0, mm);
        }

        [Fact]
        public void A_shot_with_no_parent_has_no_anchor()
        {
            if (Skip) return;

            var wall = Collider();
            Assert.False(ObstaclePatches.TryAnchorDistanceMm(
                Node(wall, Vector3.zero, null), wall, 0, out _));
        }

        /// <summary>
        /// The guard against a chain the engine released and the pool reissued: an anchor
        /// further away than the object it is supposed to be inside is not an anchor.
        /// Rejected rather than clamped — a wrong number here charges or refuses a wall.
        /// </summary>
        [Fact]
        public void An_anchor_outside_the_object_is_refused()
        {
            if (Skip) return;

            var wall = Collider();
            var far = Node(wall, new Vector3(9f, 0, 0), Node(wall, Vector3.zero, null));

            // 9 m apart inside an object whose bounding diagonal is 1 m
            Assert.False(ObstaclePatches.TryAnchorDistanceMm(far, wall, 1000, out _));
            // and the same pair with the object big enough to hold it
            Assert.True(ObstaclePatches.TryAnchorDistanceMm(far, wall, 20000, out var mm));
            Assert.Equal(9000, mm, 3);
        }

        /// <summary>
        /// Two faces at the same point are a graze along a surface, not a crossing, and a
        /// zero-length cavity must not read as "measured, and under the threshold" — that
        /// answer is already what the chord fallback gives.
        /// </summary>
        [Fact]
        public void A_zero_length_anchor_is_refused()
        {
            if (Skip) return;

            var wall = Collider();
            var same = Node(wall, Vector3.zero, Node(wall, Vector3.zero, null));

            Assert.False(ObstaclePatches.TryAnchorDistanceMm(same, wall, 0, out _));
        }
    }
}
