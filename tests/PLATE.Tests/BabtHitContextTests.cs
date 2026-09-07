using PLATE.Client.Ballistics;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Tests
{
    public class BabtHitContextTests
    {
        [Fact]
        public void Same_frame_projectiles_keep_their_own_ordered_contact_chains()
        {
            var firstShot = new object();
            var secondShot = new object();
            BabtHitContext.AppendResolved(firstShot, Contact(new object(), "a", 0.008));
            BabtHitContext.AppendResolved(secondShot, Contact(new object(), "b", 0.012));

            var firstFingerprint = Fingerprint(firstShot, 1);
            var secondFingerprint = Fingerprint(secondShot, 2);
            var first = BabtHitContext.Activate(firstShot, firstFingerprint);
            var second = BabtHitContext.Activate(secondShot, secondFingerprint);

            Assert.True(BabtHitContext.TryPrepare(first, firstFingerprint, out var a));
            Assert.True(BabtHitContext.TryPrepare(second, secondFingerprint, out var b));
            Assert.Equal(0.008, a[0].Impact.Incoming.MassKg, 6);
            Assert.Equal(0.012, b[0].Impact.Incoming.MassKg, 6);
        }

        [Fact]
        public void Components_of_one_item_remain_distinct_and_ordered()
        {
            var shot = new object();
            var face = new object();
            var backing = new object();
            BabtHitContext.AppendResolved(shot, Contact(face, "one-item", 0.010));
            BabtHitContext.AppendResolved(shot, Contact(backing, "one-item", 0.007));

            var fingerprint = Fingerprint(shot, 1);
            var token = BabtHitContext.Activate(shot, fingerprint);

            Assert.True(BabtHitContext.TryPrepare(token, fingerprint, out var contacts));
            Assert.Equal(2, contacts.Length);
            Assert.Same(face, contacts[0].Armor);
            Assert.Same(backing, contacts[1].Armor);
            Assert.Equal(0, contacts[0].DecisionOrdinal);
            Assert.Equal(1, contacts[1].DecisionOrdinal);
        }

        [Fact]
        public void Preparation_and_health_delivery_are_each_one_shot()
        {
            var shot = new object();
            var fingerprint = Fingerprint(shot, 1);
            BabtHitContext.AppendResolved(shot, Contact(new object(), "armor", 0.008));
            var token = BabtHitContext.Activate(shot, fingerprint);

            Assert.False(BabtHitContext.TryConsume(token, fingerprint));
            Assert.True(BabtHitContext.TryPrepare(token, fingerprint, out _));
            Assert.False(BabtHitContext.TryPrepare(token, fingerprint, out _));
            Assert.True(BabtHitContext.TryConsume(token, fingerprint));
            Assert.False(BabtHitContext.TryConsume(token, fingerprint));
        }

        [Fact]
        public void Delivery_must_match_victim_hit_and_forward_side()
        {
            var shot = new object();
            var correct = Fingerprint(new object(), 7);
            var wrongVictim = Fingerprint(new object(), 7);
            var backface = correct;
            backface.IsForwardHit = false;
            BabtHitContext.AppendResolved(shot, Contact(new object(), "armor", 0.008));
            var token = BabtHitContext.Activate(shot, correct);

            Assert.False(BabtHitContext.TryPrepare(token, wrongVictim, out _));
            Assert.False(BabtHitContext.TryPrepare(token, backface, out _));
            Assert.True(BabtHitContext.TryPrepare(token, correct, out _));
        }

        [Fact]
        public void New_activation_invalidates_a_stale_delivery_token()
        {
            var shot = new object();
            var fingerprint = Fingerprint(shot, 1);
            BabtHitContext.AppendResolved(shot, Contact(new object(), "armor", 0.008));

            var stale = BabtHitContext.Activate(shot, fingerprint);
            var current = BabtHitContext.Activate(shot, fingerprint);

            Assert.False(BabtHitContext.TryPrepare(stale, fingerprint, out _));
            Assert.True(BabtHitContext.TryPrepare(current, fingerprint, out _));
        }

        [Fact]
        public void Reissued_pooled_shot_invalidates_retained_token_and_chain()
        {
            var shot = new object();
            var fingerprint = Fingerprint(shot, 1);
            BabtHitContext.AppendResolved(shot, Contact(new object(), "old", 0.008));
            var retained = BabtHitContext.Activate(shot, fingerprint);

            BabtHitContext.Forget(shot);

            Assert.False(BabtHitContext.TryPrepare(retained, fingerprint, out _));
            Assert.Null(BabtHitContext.Activate(shot, fingerprint));
        }

        [Fact]
        public void Later_collision_on_same_projectile_invalidates_old_delivery()
        {
            var shot = new object();
            var oldFingerprint = Fingerprint(shot, 1);
            BabtHitContext.AppendResolved(shot, Contact(new object(), "old", 0.008));
            var oldToken = BabtHitContext.Activate(shot, oldFingerprint);

            BabtHitContext.AppendResolved(shot, Contact(new object(), "new", 0.007));
            var newFingerprint = Fingerprint(shot, 2);
            var newToken = BabtHitContext.Activate(shot, newFingerprint);

            Assert.False(BabtHitContext.TryPrepare(oldToken, oldFingerprint, out _));
            Assert.True(BabtHitContext.TryPrepare(newToken, newFingerprint, out var current));
            Assert.Single(current);
            Assert.Equal("new", current[0].ArmorItemId);
        }

        [Fact]
        public void Layer_metadata_normals_are_reprojected_to_one_delivery_basis()
        {
            var shot = new object();
            var first = Contact(new object(), "outer", 0.010);
            first.RawNormalCosine = 0.25;
            first.IncomingDirectionX = first.OutgoingDirectionX = 1.0;
            first.Impact.Outcome = BabtTransferModel.Outcome.Pierce;
            first.Impact.OutgoingPrimary = new BabtTransferModel.ProjectileState
            {
                MassKg = 0.008,
                SpeedMps = 300,
                NormalVelocityMps = 75,
                Provenance = "test exit",
                Readiness = BabtModel.Readiness.Provisional,
            };
            var second = Contact(new object(), "inner", 0.008);
            second.RawNormalCosine = 0.8;
            second.IncomingDirectionX = 1.0;
            second.Impact.Incoming.SpeedMps = 300;
            second.Impact.Incoming.NormalVelocityMps = 240;
            BabtHitContext.AppendResolved(shot, first);
            BabtHitContext.AppendResolved(shot, second);

            var fingerprint = Fingerprint(shot, 1);
            fingerprint.NormalX = 1.0;
            fingerprint.NormalZ = 0.0;
            var token = BabtHitContext.Activate(shot, fingerprint);

            Assert.True(BabtHitContext.TryPrepare(token, fingerprint, out var chain));
            Assert.Equal(300, chain[0].Impact.OutgoingPrimary.NormalVelocityMps, 6);
            Assert.Equal(300, chain[1].Impact.Incoming.NormalVelocityMps, 6);
            Assert.NotEqual(chain[0].RawNormalCosine, chain[1].RawNormalCosine);
        }

        [Fact]
        public void Direction_change_is_reprojected_before_chain_continuity_is_checked()
        {
            var shot = new object();
            var first = Contact(new object(), "outer", 0.010);
            first.Impact.Outcome = BabtTransferModel.Outcome.Pierce;
            first.OutgoingDirectionX = 0.6;
            first.OutgoingDirectionZ = 0.8;
            first.Impact.OutgoingPrimary = new BabtTransferModel.ProjectileState
            {
                MassKg = 0.008,
                SpeedMps = 300,
                NormalVelocityMps = 30,
                Provenance = "changed direction",
                Readiness = BabtModel.Readiness.Provisional,
            };
            var second = Contact(new object(), "inner", 0.008);
            second.IncomingDirectionX = 0.6;
            second.IncomingDirectionZ = 0.8;
            second.Impact.Incoming.SpeedMps = 300;
            second.Impact.Incoming.NormalVelocityMps = 270;
            BabtHitContext.AppendResolved(shot, first);
            BabtHitContext.AppendResolved(shot, second);

            var fingerprint = Fingerprint(shot, 3);
            var token = BabtHitContext.Activate(shot, fingerprint);

            Assert.True(BabtHitContext.TryPrepare(token, fingerprint, out var chain));
            Assert.Equal(240, chain[0].Impact.OutgoingPrimary.NormalVelocityMps, 6);
            Assert.Equal(240, chain[1].Impact.Incoming.NormalVelocityMps, 6);
        }

        private static BabtHitContext.DeliveryFingerprint Fingerprint(object victim,
            int fireIndex)
        {
            return new BabtHitContext.DeliveryFingerprint
            {
                Victim = victim,
                Collider = victim,
                FireIndex = fireIndex,
                ColliderType = 4,
                IsForwardHit = true,
                NormalZ = 1.0,
                HitX = 1.0,
                HitY = 2.0,
                HitZ = 3.0,
                SourceId = "source",
            };
        }

        private static BabtHitContext.ArmorContactResult Contact(object armor,
            string itemId, double massKg)
        {
            return new BabtHitContext.ArmorContactResult
            {
                Armor = armor,
                ArmorItemId = itemId,
                ArmorTemplateId = "template-" + itemId,
                IncomingDirectionZ = 1.0,
                Impact = new BabtTransferModel.ResolvedImpact
                {
                    LayerId = itemId,
                    Outcome = BabtTransferModel.Outcome.Stop,
                    Incoming = new BabtTransferModel.ProjectileState
                    {
                        MassKg = massKg,
                        SpeedMps = 350.0,
                        NormalVelocityMps = 350.0,
                        Provenance = "test incoming",
                        Readiness = BabtModel.Readiness.Provisional,
                    },
                    OutgoingPrimary = new BabtTransferModel.ProjectileState
                    {
                        Provenance = "test stop",
                        Readiness = BabtModel.Readiness.Provisional,
                    },
                    Provenance = "test decision",
                    Readiness = BabtModel.Readiness.Provisional,
                    FaceWearFraction = 1.0,
                    BackingWearFraction = 1.0,
                },
            };
        }
    }
}
