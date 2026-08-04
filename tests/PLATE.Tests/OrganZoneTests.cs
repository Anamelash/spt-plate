using EFT;
using PLATE.Client.Ballistics;
using UnityEngine;
using Xunit;

namespace PLATE.Tests
{
    /// <summary>
    /// Organs as thirds of the hitboxes, the channel measured against them, and the
    /// temporary cavity measured against what the channel missed.
    ///
    /// The numbers in here are the ones dumped off a live body: RibcageUp comes in two
    /// boxes of very different depth, and the same side-on shot is fatal through one and
    /// not through the other. That is not a rounding artefact — it is the model saying a
    /// broad shallow chest box is mostly mediastinum and a deep narrow one is mostly
    /// lung, which is what the criterion is for.
    ///
    /// Pure geometry: frames built by hand, no colliders, no game running. The rolls are
    /// tested as probabilities rather than outcomes — the dice belong to the patch.
    /// </summary>
    public class OrganZoneTests : IClassFixture<GameFixture>
    {
        public OrganZoneTests(GameFixture fixture)
        {
            _ = fixture; // installs the assembly resolver for the EFT enums
        }

        /// <summary>The shipped defaults, so the tests speak about the model that runs.</summary>
        private static OrganZones.Tuning Tuning()
        {
            return new OrganZones.Tuning
            {
                TissueStrengthMPa = 1f,
                KHeart = 2.8f,
                KLiver = 1.8f,
                KSpine = 2.3f,
                ArrestChance = 0.15f,
                AvulsionChance = 0.35f,
                LiverRadiusMm = 70f,
                VelocityCenter = 600f,
                VelocityWidth = 80f,
            };
        }

        /// <summary>A box centred on the origin, facing the world axes, sizes in mm.</summary>
        private static Anatomy.Frame Frame(float widthMm, float heightMm, float depthMm,
            float widthSign = 1f)
        {
            return new Anatomy.Frame
            {
                Valid = true,
                Center = Vector3.zero,
                Width = new Vector3(widthSign, 0f, 0f),
                Height = new Vector3(0f, 1f, 0f),
                Depth = new Vector3(0f, 0f, 1f),
                HalfMm = new Vector3(widthMm, heightMm, depthMm) * 0.5f,
            };
        }

        /// <summary>dE/dx that produces a cavity of the given radius, J/mm.</summary>
        private static float DepositFor(float radiusMm)
        {
            return Mathf.PI * radiusMm * radiusMm / 1000f;
        }

        private static bool Hit(EBodyPartColliderType collider, Anatomy.Frame frame,
            Vector3 entry, Vector3 dir, float channelMm, out OrganZones.Result result,
            float dEdx = 0f)
        {
            return OrganZones.TryHit(collider, frame, entry, dir, channelMm, dEdx,
                Tuning(), out result);
        }

        // the two RibcageUp boxes and the RibcageLow volume, as dumped
        private static Anatomy.Frame WideChest(float widthSign = 1f) =>
            Frame(386f, 310f, 93f, widthSign);

        private static Anatomy.Frame DeepChest() => Frame(233f, 310f, 194f);

        private static Anatomy.Frame Belly(float widthSign = 1f) =>
            Frame(223f, 240f, 65f, widthSign);

        private static readonly Vector3 Ahead = new Vector3(0f, 0f, -1f); // front to back

        /// <summary>Entry on the front face of a frame, x millimetres to the body's own right.</summary>
        private static Vector3 FrontFace(in Anatomy.Frame f, float xMm)
        {
            return new Vector3(xMm / 1000f * f.Width.x, 0f, f.HalfMm.z / 1000f);
        }

        // --- Geometry ---

        [Fact]
        public void Straight_through_the_middle_of_the_chest_is_the_heart()
        {
            var f = WideChest();
            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 0f), Ahead,
                400f, out var hit));

            Assert.Equal(OrganZone.Heart, hit.Kind);
            Assert.Equal(93f, hit.PathMm, 0);
            Assert.Equal(46.5f, hit.NeedMm, 1);
            Assert.True(hit.Through);
            Assert.True(hit.Lethal);
        }

        /// <summary>
        /// A miss has to come back as a miss, not as silence. "The channel went past the
        /// heart" and "there is no heart in the code" look identical in a journal that
        /// only prints hits, and one of those is a bug.
        /// </summary>
        [Fact]
        public void A_channel_past_the_zone_still_reports_the_zone()
        {
            var f = WideChest();
            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, -150f), Ahead,
                400f, out var hit));

            Assert.Equal(OrganZone.Heart, hit.Kind);
            Assert.False(hit.Through);
            Assert.False(hit.Lethal);
            Assert.Equal(0f, hit.PathMm);
            Assert.True(hit.NeedMm > 0f, "the criterion belongs in the log even on a miss");
        }

        /// <summary>
        /// Clipping the corner of the zone is a tangential wound of the myocardium, which
        /// people survive. The criterion is the whole reason the zone is a volume and not
        /// a flag.
        /// </summary>
        [Fact]
        public void Clipping_the_edge_of_the_heart_is_not_fatal()
        {
            var f = WideChest();
            var slanted = new Vector3(1f, 0f, -1f).normalized; // out through the side of the zone

            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 60f), slanted,
                400f, out var hit));

            Assert.True(hit.PathMm > 0f, "it did go through some of the zone");
            Assert.True(hit.PathMm < hit.NeedMm);
            Assert.False(hit.Lethal);
        }

        /// <summary>
        /// Side on, the same third of the same organ, and the answer differs by which box
        /// the bullet went into: 129 mm of mediastinum through the broad shallow box
        /// against 78 mm through the deep narrow one, which has to beat 97. The zone is a
        /// share of the box it was found in — that is a decision, and this is it in
        /// numbers.
        /// </summary>
        [Fact]
        public void Side_on_the_shallow_chest_box_is_fatal_and_the_deep_one_is_not()
        {
            var across = new Vector3(1f, 0f, 0f);

            Assert.True(Hit(EBodyPartColliderType.RibcageUp, WideChest(),
                new Vector3(-0.193f, 0f, 0f), across, 400f, out var throughWide));
            Assert.True(Hit(EBodyPartColliderType.RibcageUp, DeepChest(),
                new Vector3(-0.1165f, 0f, 0f), across, 400f, out var throughDeep));

            Assert.True(throughWide.Lethal);
            Assert.False(throughDeep.Lethal);
        }

        /// <summary>A projectile that runs out before it reaches the zone has not reached it.</summary>
        [Fact]
        public void A_channel_that_stops_short_of_the_zone_never_gets_there()
        {
            var f = WideChest();
            var across = new Vector3(1f, 0f, 0f);

            // the zone starts 129 mm in from that side of the box
            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, new Vector3(-0.193f, 0f, 0f),
                across, 100f, out var hit));

            Assert.True(hit.ToZoneMm > 100f);
            Assert.Equal(0f, hit.PathMm);
            Assert.False(hit.Lethal);
        }

        [Fact]
        public void The_liver_is_the_right_third_of_the_lower_ribcage()
        {
            var f = Belly();

            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, 75f), Ahead,
                400f, out var onTheRight));
            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, -75f), Ahead,
                400f, out var onTheLeft));

            Assert.Equal(OrganZone.Liver, onTheRight.Kind);
            Assert.True(onTheRight.Through);
            Assert.False(onTheLeft.Through);
        }

        /// <summary>
        /// Deep through the liver is not death on its own. Avulsion is what the autopsy
        /// series calls unsurvivable, and avulsion is a roll — the geometry only says the
        /// bullet went through it.
        /// </summary>
        [Fact]
        public void A_liver_run_through_is_not_lethal_by_geometry_alone()
        {
            var f = Belly();
            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, 75f), Ahead,
                400f, out var hit));

            Assert.True(hit.Through);
            Assert.False(hit.Lethal);
        }

        /// <summary>
        /// The sign test at the level that matters. Turn the target around and the same
        /// world point has to stop being the liver — otherwise every shot from behind
        /// treats the spleen as the liver and nothing looks wrong.
        /// </summary>
        [Fact]
        public void The_liver_stays_on_the_bodys_own_right_when_the_target_turns_around()
        {
            var world = new Vector3(0.075f, 0f, 0.0325f);

            Assert.True(Hit(EBodyPartColliderType.RibcageLow, Belly(), world, Ahead, 400f,
                out var a));
            Assert.True(Hit(EBodyPartColliderType.RibcageLow, Belly(widthSign: -1f), world,
                Ahead, 400f, out var b));

            Assert.True(a.Through);
            Assert.False(b.Through);
        }

        /// <summary>
        /// The cord has no half-depth to be short of: 17 mm of collider, and whatever
        /// comes out the far side has been through it. Whatever stops inside has not.
        /// </summary>
        [Fact]
        public void The_spine_plate_is_severed_by_anything_that_comes_out_the_other_side()
        {
            var f = Frame(226f, 325f, 17f);

            Assert.True(Hit(EBodyPartColliderType.SpineTop, f, FrontFace(f, 0f), Ahead,
                200f, out var through));
            Assert.True(Hit(EBodyPartColliderType.SpineTop, f, FrontFace(f, 0f), Ahead,
                8f, out var spent));

            Assert.Equal(OrganZone.Spine, through.Kind);
            Assert.True(through.Through);
            Assert.True(through.Lethal);

            Assert.False(spent.Through);
            Assert.False(spent.Lethal);
        }

        /// <summary>
        /// The other collider called SpineTop is half a metre of upper back. Without the
        /// thickness rule, every shoulder hit would be a severed spinal cord.
        /// </summary>
        [Fact]
        public void The_upper_back_box_carries_no_zone_at_all()
        {
            var f = Frame(502f, 350f, 100f);

            Assert.False(Hit(EBodyPartColliderType.SpineTop, f, FrontFace(f, 0f), Ahead,
                400f, out _));
        }

        [Fact]
        public void Colliders_without_an_organ_say_so()
        {
            var f = Frame(120f, 400f, 120f);

            Assert.False(Hit(EBodyPartColliderType.LeftThigh, f, FrontFace(f, 0f), Ahead,
                400f, out _));
            Assert.False(Hit(EBodyPartColliderType.Pelvis, f, FrontFace(f, 0f), Ahead,
                400f, out _));
        }

        [Fact]
        public void Zone_names_are_the_ones_the_journal_prints()
        {
            var f = WideChest();
            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 0f), Ahead,
                400f, out var hit));

            Assert.Equal("heart", hit.Name);
            Assert.Contains("mid third", hit.Where);
        }

        // --- The temporary cavity ---

        /// <summary>
        /// The one constant in the cavity is tied to published gelatin profiles: a rifle
        /// round opens something like 12 cm across, a service pistol around 6.
        /// </summary>
        [Fact]
        public void The_cavity_radius_lands_on_the_published_gelatin_diameters()
        {
            var rifle = OrganZones.CavityRadiusMm(12.4f, 1f); // 7.62x51 through a chest
            var pistol = OrganZones.CavityRadiusMm(2.6f, 1f); // 9x19 through the same

            Assert.InRange(rifle, 55f, 70f);
            Assert.InRange(pistol, 25f, 33f);
        }

        [Fact]
        public void No_energy_left_behind_is_no_cavity()
        {
            Assert.Equal(0f, OrganZones.CavityRadiusMm(0f, 1f));
            Assert.Equal(0f, OrganZones.CavityRadiusMm(-5f, 1f));
        }

        /// <summary>
        /// A cavity that never reaches the organ earns nothing. Without this the model
        /// would hand out a mediastinum multiplier for a hit through the far shoulder.
        /// </summary>
        [Fact]
        public void A_cavity_that_falls_short_of_the_zone_earns_nothing()
        {
            var f = WideChest();

            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 150f), Ahead,
                200f, out var hit, DepositFor(20f)));

            Assert.True(hit.DistanceMm > 20f);
            Assert.Equal(0f, hit.Overlap);
            Assert.Equal(1f, hit.Multiplier, 3);
        }

        /// <summary>
        /// The case the whole cavity model exists for: the bullet missed the heart and
        /// the stretch reached it anyway. The multiplier is the AIS severity scaled by
        /// how much of the zone the cavity got into.
        /// </summary>
        [Fact]
        public void A_cavity_beside_the_heart_earns_the_share_of_the_zone_it_reached()
        {
            var f = WideChest();

            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 120f), Ahead,
                200f, out var hit, DepositFor(80f)));

            Assert.False(hit.Through, "the channel itself missed the zone");
            Assert.InRange(hit.DistanceMm, 50f, 60f);
            Assert.InRange(hit.Overlap, 0.4f, 0.65f);
            Assert.Equal(1f + (2.8f - 1f) * hit.Overlap, hit.Multiplier, 3);
        }

        /// <summary>
        /// A channel through the organ has already done the worst the severity describes,
        /// so it takes the whole of it rather than a share.
        /// </summary>
        [Fact]
        public void A_channel_through_the_liver_takes_the_full_severity()
        {
            var f = Belly();

            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, 75f), Ahead,
                400f, out var hit, DepositFor(60f)));

            Assert.True(hit.Through);
            Assert.Equal(1.8f, hit.Multiplier, 3);
        }

        /// <summary>A death needs no multiplier — the damage is floored to what is left.</summary>
        [Fact]
        public void A_lethal_zone_asks_for_no_multiplier()
        {
            var f = WideChest();

            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 0f), Ahead,
                400f, out var hit, DepositFor(60f)));

            Assert.True(hit.Lethal);
            Assert.Equal(1f, hit.Multiplier, 3);
        }

        /// <summary>
        /// Why the reach is normalised on the narrowest way across the zone rather than
        /// its width. The cord is 226 mm wide and 17 mm thick; a cavity that gets within
        /// 20 mm of it has the whole cord inside it, and normalising on the width would
        /// have called that a tenth of an organ.
        /// </summary>
        [Fact]
        public void A_cavity_that_reaches_the_cord_engulfs_it()
        {
            var f = Frame(226f, 325f, 17f);
            var across = new Vector3(1f, 0f, 0f);
            var beside = new Vector3(-0.2f, 0f, 0.0285f); // 20 mm off the face of the plate

            Assert.True(Hit(EBodyPartColliderType.SpineTop, f, beside, across, 300f,
                out var hit, DepositFor(30f)));

            Assert.False(hit.Through);
            Assert.Equal(20f, hit.DistanceMm, 0);
            Assert.Equal(1f, hit.Overlap, 3);
            Assert.Equal(2.3f, hit.Multiplier, 3);
        }

        // --- The rolls ---

        /// <summary>
        /// Traumatic cardiac arrest needs rifle velocities. Below Fackler's boundary the
        /// tissue rides the stretch out, and a pistol cavity has neither the size nor the
        /// speed to stop a heart.
        /// </summary>
        [Fact]
        public void A_pistol_practically_never_stops_a_heart()
        {
            var f = WideChest();
            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 120f), Ahead,
                200f, out var hit, DepositFor(80f)));

            var rifle = OrganZones.ArrestChance(hit, 838f, Tuning());
            var pistol = OrganZones.ArrestChance(hit, 390f, Tuning());

            Assert.InRange(rifle, 0.05f, 0.10f);
            Assert.True(pistol * 10f < rifle,
                $"a pistol got {pistol:0.000} against a rifle's {rifle:0.000}");
        }

        /// <summary>
        /// Arrest is for a cavity that passed BESIDE the heart. Through it there is
        /// nothing left to stop.
        /// </summary>
        [Fact]
        public void Arrest_is_not_rolled_for_a_channel_through_the_heart()
        {
            var f = WideChest();
            Assert.True(Hit(EBodyPartColliderType.RibcageUp, f, FrontFace(f, 0f), Ahead,
                400f, out var hit, DepositFor(80f)));

            Assert.Equal(0f, OrganZones.ArrestChance(hit, 838f, Tuning()));
        }

        /// <summary>
        /// Avulsion is a tearing injury, so its chance comes from the cavity and not from
        /// the hole. A rifle through the liver gets about a third; a pistol through the
        /// same liver gets a few percent, which is what the model should say — pistols
        /// do not tear organs off their ligaments.
        /// </summary>
        [Fact]
        public void Avulsion_belongs_to_rifles_and_needs_the_channel_through_the_organ()
        {
            var f = Belly();
            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, 75f), Ahead,
                400f, out var through), "the setup has to be a run-through");
            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, -75f), Ahead,
                400f, out var beside));

            // both fed a rifle's cavity, so only the geometry differs
            var rifle = OrganZones.AvulsionChance(WithCavity(through, 80f), 838f, Tuning());
            var pistol = OrganZones.AvulsionChance(WithCavity(through, 80f), 390f, Tuning());

            Assert.InRange(rifle, 0.28f, 0.36f);
            Assert.True(pistol < 0.03f, $"a pistol avulsed the liver {pistol:0.000} of the time");
            Assert.Equal(0f, OrganZones.AvulsionChance(WithCavity(beside, 80f), 838f, Tuning()));
        }

        /// <summary>A small cavity cannot tear off a whole organ, however fast it arrived.</summary>
        [Fact]
        public void Avulsion_scales_with_the_cavity_against_the_organs_own_size()
        {
            var f = Belly();
            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, 75f), Ahead,
                400f, out var hit));

            var big = OrganZones.AvulsionChance(WithCavity(hit, 70f), 838f, Tuning());
            var small = OrganZones.AvulsionChance(WithCavity(hit, 35f), 838f, Tuning());

            Assert.Equal(0.5f, small / big, 2);
        }

        private static OrganZones.Result WithCavity(OrganZones.Result r, float radiusMm)
        {
            r.TcRadiusMm = radiusMm;
            return r;
        }

        [Fact]
        public void The_high_velocity_boundary_sits_where_the_wound_model_puts_it()
        {
            var t = Tuning();

            Assert.Equal(0.5f, OrganZones.HighVelocity(600f, t), 3);
            Assert.True(OrganZones.HighVelocity(900f, t) > 0.95f);
            Assert.True(OrganZones.HighVelocity(340f, t) < 0.05f);
        }

        // --- How much of the organ was involved ---

        /// <summary>
        /// What bleeding is scaled by. Half an organ opened bleeds like half an organ,
        /// and a run-through opens all of it — the liver is the case this exists for,
        /// because a channel across its right lobe crosses the vena cava that runs
        /// through it.
        /// </summary>
        [Fact]
        public void Involvement_is_how_much_of_the_organ_the_channel_crossed()
        {
            var f = Belly();

            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, 75f), Ahead,
                400f, out var through, DepositFor(50f)));
            Assert.True(through.Through);
            Assert.Equal(1f, through.Involvement, 3);

            // stopped a third of the way into the organ, with no cavity to speak of
            Assert.True(Hit(EBodyPartColliderType.RibcageLow, f, FrontFace(f, 75f), Ahead,
                22f, out var partial));
            Assert.False(partial.Through);
            Assert.InRange(partial.Involvement, 0.3f, 0.4f);
        }

        /// <summary>
        /// The cord has no depth to be part-way through, so only the cavity says how much
        /// of it was involved.
        /// </summary>
        [Fact]
        public void The_cord_is_involved_only_as_far_as_the_cavity_reached()
        {
            var f = Frame(226f, 325f, 17f);
            var across = new Vector3(1f, 0f, 0f);

            Assert.True(Hit(EBodyPartColliderType.SpineTop, f, new Vector3(-0.2f, 0f, 0.0285f),
                across, 300f, out var beside, DepositFor(30f)));

            Assert.Equal(beside.Overlap, beside.Involvement, 4);
        }

        // --- The distance itself ---

        [Fact]
        public void Distance_to_a_box_is_measured_from_the_nearest_point_of_the_channel()
        {
            var half = new Vector3(10f, 10f, 10f);
            var across = new Vector3(1f, 0f, 0f);

            // runs past the box 50 mm off its centre in z, so 40 mm off its face
            Assert.Equal(40f, Anatomy.SegmentToBox(new Vector3(-50f, 0f, 50f), across, 100f,
                half), 2);

            // straight through it
            Assert.Equal(0f, Anatomy.SegmentToBox(new Vector3(-50f, 0f, 0f), across, 100f,
                half), 3);

            // aimed at it but stopping short: 30 mm of gap left
            Assert.Equal(30f, Anatomy.SegmentToBox(new Vector3(-50f, 0f, 0f), across, 10f,
                half), 2);
        }
    }
}
