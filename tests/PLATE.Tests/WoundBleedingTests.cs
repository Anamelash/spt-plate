using EFT;
using PLATE.Client.Ballistics;
using UnityEngine;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Whether a wound bleeds badly, decided by what the channel crossed.
    ///
    /// It used to be decided by the cartridge — the server wrote a bleed chance per ammo
    /// template out of calibre and expansiveness, and the same round then bled the same
    /// through a thigh as through the mediastinum. The replacement is geometric: a
    /// channel of diameter d over a length L sweeps a plane of d·L, and the vessels it
    /// cuts are the ones that crossed that plane. Everything below is that one sentence
    /// in numbers.
    /// </summary>
    public class WoundBleedingTests : IClassFixture<GameFixture>
    {
        public WoundBleedingTests(GameFixture fixture)
        {
            _ = fixture; // installs the assembly resolver for the EFT enums
        }

        /// <summary>The shipped defaults, so the numbers below mean what they mean in a raid.</summary>
        private static WoundBleeding.Tuning Tuning() => new WoundBleeding.Tuning
        {
            VesselsTorso = 6e-5f,
            VesselsJunction = 1.8e-4f,
            VesselsLimb = 2.4e-5f,
            VesselsHead = 2.4e-5f,
            MaxChance = 0.95f,
        };

        // 7.62x51 across a chest: the channel averages a 10.8 mm cross-section over
        // 210 mm of tissue once it has turned
        private const float ChestPathMm = 210f;
        private static float ChestVolume => 91.1f * ChestPathMm;

        /// <summary>
        /// The junctional set is the one the combat mortality data keeps separate, and it
        /// is worth reading the list rather than trusting it: a thigh is junctional and a
        /// calf is not, because the femoral bundle stops being reachable at the knee.
        /// </summary>
        [Fact]
        public void Hitboxes_map_to_the_regions_the_mortality_data_is_kept_in()
        {
            AssertRegion(EBodyPartColliderType.NeckFront, BleedRegion.Junction);
            AssertRegion(EBodyPartColliderType.Pelvis, BleedRegion.Junction);
            AssertRegion(EBodyPartColliderType.LeftThigh, BleedRegion.Junction);
            AssertRegion(EBodyPartColliderType.RightUpperArm, BleedRegion.Junction);

            AssertRegion(EBodyPartColliderType.LeftCalf, BleedRegion.Limb);
            AssertRegion(EBodyPartColliderType.RightForearm, BleedRegion.Limb);

            AssertRegion(EBodyPartColliderType.HeadCommon, BleedRegion.Head);
            AssertRegion(EBodyPartColliderType.Jaw, BleedRegion.Head);

            AssertRegion(EBodyPartColliderType.RibcageUp, BleedRegion.Torso);
            AssertRegion(EBodyPartColliderType.SpineDown, BleedRegion.Torso);
        }

        private static void AssertRegion(EBodyPartColliderType collider, BleedRegion expected)
        {
            Assert.Equal(expected, WoundBleeding.Region(collider));
        }

        /// <summary>
        /// The swept plane is diameter times length, and it has to come out that way from
        /// a volume and a path — otherwise the whole model rests on an algebra slip.
        /// </summary>
        [Fact]
        public void The_swept_plane_is_the_channels_width_times_its_length()
        {
            const float dia = 10f;
            const float path = 200f;
            var volume = Mathf.PI * dia * dia / 4f * path;

            Assert.Equal(dia * path, WoundBleeding.SweptMm2(volume, path), 1);
        }

        [Fact]
        public void No_channel_sweeps_nothing()
        {
            Assert.Equal(0f, WoundBleeding.SweptMm2(0f, 200f));
            Assert.Equal(0f, WoundBleeding.SweptMm2(15000f, 0f));
        }

        /// <summary>
        /// The anchor. A rifle round straight across a chest has to land near where the
        /// old per-cartridge chance was, or the change is a balance rewrite wearing a
        /// physics argument.
        /// </summary>
        [Fact]
        public void A_rifle_round_across_a_chest_bleeds_about_as_often_as_it_used_to()
        {
            var swept = WoundBleeding.SweptMm2(ChestVolume, ChestPathMm);
            var chance = WoundBleeding.HeavyChance(BleedRegion.Torso, swept, Tuning());

            Assert.InRange(chance, 0.10f, 0.15f);
        }

        /// <summary>
        /// A graze is a graze. Before this the cartridge decided, so a round that clipped
        /// a forearm carried the same arterial chance as one that crossed a torso.
        /// </summary>
        [Fact]
        public void A_clipped_limb_is_nothing_like_a_crossed_torso()
        {
            var t = Tuning();
            var torso = WoundBleeding.HeavyChance(BleedRegion.Torso,
                WoundBleeding.SweptMm2(ChestVolume, ChestPathMm), t);
            var graze = WoundBleeding.HeavyChance(BleedRegion.Limb,
                WoundBleeding.SweptMm2(80f * 60f, 60f), t);

            Assert.True(graze * 8f < torso, $"a forearm graze bled {graze:P1} against {torso:P1}");
        }

        /// <summary>
        /// Junctional wounds are kept apart in the mortality data because a tourniquet
        /// has nothing to squeeze against there, and the model has to agree that they are
        /// worse than a wound through muscle.
        /// </summary>
        [Fact]
        public void A_junctional_wound_bleeds_worse_than_the_same_wound_in_a_limb()
        {
            var t = Tuning();
            var swept = WoundBleeding.SweptMm2(90f * 150f, 150f);

            Assert.True(WoundBleeding.HeavyChance(BleedRegion.Junction, swept, t) >
                        3f * WoundBleeding.HeavyChance(BleedRegion.Limb, swept, t));
        }

        /// <summary>More channel is never less chance, and the cartridge is in it through the channel.</summary>
        [Fact]
        public void A_longer_or_wider_channel_cuts_more()
        {
            var t = Tuning();
            var last = -1f;
            foreach (var path in new[] { 40f, 90f, 150f, 250f, 350f })
            {
                var chance = WoundBleeding.HeavyChance(BleedRegion.Torso,
                    WoundBleeding.SweptMm2(91f * path, path), t);
                Assert.True(chance > last, $"{path} mm of channel bled less than the shorter one");
                last = chance;
            }
        }

        [Fact]
        public void Nothing_is_ever_certain()
        {
            var t = Tuning();
            var huge = WoundBleeding.HeavyChance(BleedRegion.Junction, 500000f, t);

            Assert.Equal(t.MaxChance, huge, 4);
        }
    }
}
