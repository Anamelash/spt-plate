using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PLATE.Server.Services;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Correlates the ordered armour decisions made for one projectile with the later
    /// DamageInfo delivery, whose value type contains no Shot reference.
    ///
    /// A frame is only a lifetime bound used by the patch layer. Identity here is the
    /// pooled Shot object plus a generation and an exact delivery fingerprint. Every
    /// successful physical armour decision appends one immutable result, including
    /// components which belong to the same item. That preserves the projectile's
    /// telescoping input/output chain instead of collapsing a carrier and its inserts.
    /// </summary>
    internal static class BabtHitContext
    {
        internal sealed class ArmorContactResult
        {
            public object Armor;
            public string ArmorItemId;
            public string ArmorTemplateId;
            public string AmmoTemplateId;
            public string Material;
            public int ArmorClass;
            public double BluntThroughput;
            public string ShotIdentity;
            public int DecisionOrdinal;

            public double IncomingDiameterM;
            public double IncomingExpansiveness;
            public double IncomingDirectionX;
            public double IncomingDirectionY;
            public double IncomingDirectionZ;
            public double OutgoingDiameterM;
            public double OutgoingExpansiveness;
            public double OutgoingDirectionX;
            public double OutgoingDirectionY;
            public double OutgoingDirectionZ;
            public double NominalImpactAreaM2;
            public double ProjectileEnergyLossJ;
            public double ExplicitOutgoingSecondaryEnergyJ;
            public double RawNormalCosine;
            public double EffectiveNormalCosine;
            public bool HasSurfaceNormal;

            public double LocalX;
            public double LocalY;
            public double LocalZ;
            public string LocalCoordinateSpace;
            public object HitBodyCollider;
            public int HitsNearby;
            public double DurabilityFraction;
            public double WearRoll;

            public bool HasBarrier;
            public BallisticLimit.Barrier PristineBarrier;
            public BallisticLimit.Barrier WornBarrier;
            public double BallisticLimitVelocityMps;
            public BallisticLimit.CoreFate CoreFate;
            public double EstimatedPlugMassKg;

            public BabtTransferModel.ResolvedImpact Impact;
            public string Provenance;
        }

        internal struct DeliveryFingerprint
        {
            public object Victim;
            public object Collider;
            public int FireIndex;
            public int ColliderType;
            public bool IsForwardHit;
            public double NormalX;
            public double NormalY;
            public double NormalZ;
            public double HitX;
            public double HitY;
            public double HitZ;
            public string SourceId;
        }

        internal sealed class Token
        {
            internal ShotRecord Record;
            internal int Generation;
            internal DeliveryFingerprint Fingerprint;

            internal Token(ShotRecord record, int generation,
                DeliveryFingerprint fingerprint)
            {
                Record = record;
                Generation = generation;
                Fingerprint = fingerprint;
            }
        }

        internal sealed class ShotRecord
        {
            public readonly List<ArmorContactResult> Contacts =
                new List<ArmorContactResult>(2);
            public int Generation;
            public bool Activated;
            public bool Prepared;
            public bool Consumed;
        }

        private static readonly ConditionalWeakTable<object, ShotRecord> Records =
            new ConditionalWeakTable<object, ShotRecord>();

        private static readonly ConditionalWeakTable<object, ShotRecord>.CreateValueCallback
            NewRecord = _ => new ShotRecord();

        /// <summary>Appends one completed armour decision in traversal order.</summary>
        internal static void AppendResolved(object shot, ArmorContactResult contact)
        {
            if (shot == null || contact == null || contact.Armor == null ||
                contact.Impact == null)
            {
                return;
            }

            var record = Records.GetValue(shot, NewRecord);
            if (record.Activated)
            {
                // A later impact by the same continuing projectile is a different
                // delivery. Retire a constructor path which never reached health.
                Invalidate(record);
            }

            contact.DecisionOrdinal = record.Contacts.Count;
            record.Contacts.Add(contact);
        }

        /// <summary>
        /// Freezes the decision chain once DamageInfo has copied the final projectile
        /// state. A second activation invalidates the older constructor token.
        /// </summary>
        internal static Token Activate(object shot, DeliveryFingerprint fingerprint)
        {
            if (shot == null || !Records.TryGetValue(shot, out var record) ||
                record.Contacts.Count == 0)
            {
                return null;
            }

            record.Generation++;
            record.Activated = true;
            record.Prepared = false;
            record.Consumed = false;

            ProjectOntoDeliveryNormal(record, fingerprint);

            // A co-moving plug from the final armor decision leaves the resolved
            // armor stack. A plug from an earlier component enters another component,
            // but the current ballistic path does not track its terminal fate; keep it
            // Unknown so the aggregate cannot apply an overstated body response.
            for (var n = 0; n < record.Contacts.Count; n++)
            {
                var secondary = record.Contacts[n].Impact?.OutgoingSecondaries;
                if (secondary == null)
                {
                    continue;
                }
                for (var s = 0; s < secondary.Length; s++)
                {
                    secondary[s].Disposition = n == record.Contacts.Count - 1
                        ? BabtTransferModel.AggregateDisposition.LeavesAggregateSystem
                        : BabtTransferModel.AggregateDisposition.Unknown;
                }
            }
            return new Token(record, record.Generation, fingerprint);
        }

        private static void ProjectOntoDeliveryNormal(ShotRecord record,
            DeliveryFingerprint fingerprint)
        {
            var length = Math.Sqrt(fingerprint.NormalX * fingerprint.NormalX +
                                   fingerprint.NormalY * fingerprint.NormalY +
                                   fingerprint.NormalZ * fingerprint.NormalZ);
            if (!(length > 0) || double.IsNaN(length) || double.IsInfinity(length))
            {
                return;
            }

            var nx = fingerprint.NormalX / length;
            var ny = fingerprint.NormalY / length;
            var nz = fingerprint.NormalZ / length;
            for (var n = 0; n < record.Contacts.Count; n++)
            {
                var contact = record.Contacts[n];
                var impact = contact.Impact;
                impact.Incoming.NormalVelocityMps = impact.Incoming.SpeedMps * Math.Abs(
                    contact.IncomingDirectionX * nx + contact.IncomingDirectionY * ny +
                    contact.IncomingDirectionZ * nz);
                impact.OutgoingPrimary.NormalVelocityMps =
                    impact.OutgoingPrimary.SpeedMps * Math.Abs(
                        contact.OutgoingDirectionX * nx + contact.OutgoingDirectionY * ny +
                        contact.OutgoingDirectionZ * nz);
                for (var s = 0; s < impact.OutgoingSecondaries.Length; s++)
                {
                    // The current ArmorExit plug is explicitly collinear with the
                    // outgoing primary. Preserve that stated estimate in the common
                    // delivery basis as well.
                    impact.OutgoingSecondaries[s].NormalVelocityMps =
                        impact.OutgoingSecondaries[s].SpeedMps * Math.Abs(
                            contact.OutgoingDirectionX * nx + contact.OutgoingDirectionY * ny +
                            contact.OutgoingDirectionZ * nz);
                }
            }
        }

        /// <summary>
        /// Claims the exact armour traversal for mechanical evaluation after all
        /// ArmorComponent.ApplyDamage postfixes have restored the residual wound.
        /// Preparation is one-shot, but final consumption waits for ApplyDamageInfo.
        /// </summary>
        internal static bool TryPrepare(Token token, DeliveryFingerprint fingerprint,
            out ArmorContactResult[] contacts)
        {
            contacts = null;
            if (!Valid(token, fingerprint) || token.Record.Prepared)
            {
                return false;
            }

            token.Record.Prepared = true;
            contacts = token.Record.Contacts.ToArray();
            return contacts.Length > 0;
        }

        /// <summary>Consumes a prepared hit at the single health-delivery boundary.</summary>
        internal static bool TryConsume(Token token, DeliveryFingerprint fingerprint)
        {
            if (!Valid(token, fingerprint) || !token.Record.Prepared)
            {
                return false;
            }

            token.Record.Consumed = true;
            return true;
        }

        internal static bool SameDelivery(DeliveryFingerprint a,
            DeliveryFingerprint b)
        {
            return ReferenceEquals(a.Victim, b.Victim) &&
                   ReferenceEquals(a.Collider, b.Collider) &&
                   a.FireIndex == b.FireIndex &&
                   a.ColliderType == b.ColliderType &&
                   a.IsForwardHit == b.IsForwardHit &&
                   a.NormalX.Equals(b.NormalX) &&
                   a.NormalY.Equals(b.NormalY) &&
                   a.NormalZ.Equals(b.NormalZ) &&
                   a.HitX.Equals(b.HitX) &&
                   a.HitY.Equals(b.HitY) &&
                   a.HitZ.Equals(b.HitZ) &&
                   string.Equals(a.SourceId, b.SourceId, StringComparison.Ordinal);
        }

        internal static bool Matches(Token token, DeliveryFingerprint fingerprint)
        {
            return Valid(token, fingerprint);
        }

        internal static bool IsBlockingArmor(string armorItemId, string blockedByItemId)
        {
            return !string.IsNullOrEmpty(armorItemId) &&
                   string.Equals(armorItemId, blockedByItemId, StringComparison.Ordinal);
        }

        private static bool Valid(Token token, DeliveryFingerprint fingerprint)
        {
            return token?.Record != null && token.Generation == token.Record.Generation &&
                   token.Record.Activated && !token.Record.Consumed &&
                   SameDelivery(token.Fingerprint, fingerprint);
        }

        private static void Invalidate(ShotRecord record)
        {
            record.Generation++;
            record.Activated = false;
            record.Prepared = false;
            record.Consumed = true;
            record.Contacts.Clear();
        }

        /// <summary>Drops state belonging to the previous tenant of a pooled Shot.</summary>
        internal static void Forget(object shot)
        {
            if (shot == null)
            {
                return;
            }

            if (Records.TryGetValue(shot, out var record))
            {
                Invalidate(record);
            }
            Records.Remove(shot);
        }
    }
}
