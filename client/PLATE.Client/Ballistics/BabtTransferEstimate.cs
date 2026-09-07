using System;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Conservative gameplay fallback for a resolved armor chain whose full reduced
    /// mechanical construction cannot be evaluated. This is not a BFD solver: it
    /// applies the existing BluntThroughput coupling only to energy lost at each real
    /// contact, after reserving secondary energy whose fate is unknown or which
    /// leaves the assembly. Material known to remain in the assembly is retained.
    /// </summary>
    internal static class BabtTransferEstimate
    {
        internal struct ContactInput
        {
            public double IncomingEnergyJ;
            public double OutgoingPrimaryEnergyJ;
            public double ReservedOrLeavingSecondaryEnergyJ;
            public double BluntThroughput;
            public double ProjectileDiameterM;
            public double SpreadDiameterM;
        }

        internal struct Result
        {
            public bool IsValid;
            public bool HasLoad;
            public double AvailableEnergyLossJ;
            public double BodyWorkEstimateJ;
            public double EffectiveContactAreaM2;
            public double EffectiveDiameterM;
            public string Diagnostic;
        }

        internal static Result Evaluate(ContactInput[] contacts, double energyScale)
        {
            if (contacts == null || contacts.Length == 0 || !Finite(energyScale) ||
                energyScale < 0)
            {
                return new Result { Diagnostic = "missing contacts or invalid gameplay energy scale" };
            }

            var available = 0.0;
            var coupled = 0.0;
            var diameter = 0.0;
            for (var i = 0; i < contacts.Length; i++)
            {
                var contact = contacts[i];
                if (!NonNegative(contact.IncomingEnergyJ) ||
                    !NonNegative(contact.OutgoingPrimaryEnergyJ) ||
                    !NonNegative(contact.ReservedOrLeavingSecondaryEnergyJ) ||
                    !NonNegative(contact.BluntThroughput) ||
                    !NonNegative(contact.ProjectileDiameterM) ||
                    !NonNegative(contact.SpreadDiameterM))
                {
                    return new Result { Diagnostic = "contact estimate contains a non-finite or negative value" };
                }

                var ownLoss = contact.IncomingEnergyJ -
                              contact.OutgoingPrimaryEnergyJ -
                              contact.ReservedOrLeavingSecondaryEnergyJ;
                var tolerance = 1e-9 * Math.Max(1.0, contact.IncomingEnergyJ);
                if (ownLoss < -tolerance)
                {
                    return new Result { Diagnostic = "a contact's outgoing energy exceeds its incoming energy" };
                }
                if (ownLoss < 0)
                {
                    ownLoss = 0;
                }

                var coupling = Math.Min(1.0,
                    Math.Max(0.0, contact.BluntThroughput * energyScale));
                available += ownLoss;
                coupled += ownLoss * coupling;
                diameter = Math.Max(diameter,
                    Math.Max(contact.ProjectileDiameterM, contact.SpreadDiameterM));
            }

            if (!(diameter > 0) || !Finite(available) || !Finite(coupled))
            {
                return new Result { Diagnostic = "the resolved chain has no finite contact footprint or energy budget" };
            }

            // Roundoff cannot let the gameplay coupling manufacture energy.
            coupled = Math.Min(available, coupled);
            return new Result
            {
                IsValid = true,
                HasLoad = coupled > 0,
                AvailableEnergyLossJ = available,
                BodyWorkEstimateJ = coupled,
                EffectiveDiameterM = diameter,
                EffectiveContactAreaM2 = Math.PI * diameter * diameter / 4.0,
                Diagnostic = "TRANSFER_ESTIMATE_FALLBACK: resolved per-contact energy loss; " +
                             "unknown or system-leaving secondary energy reserved; existing BluntThroughput and " +
                             "energy scale used as a gameplay coupling estimate; no BFD or " +
                             "clinically calibrated body-work claim",
            };
        }

        private static bool NonNegative(double value) => Finite(value) && value >= 0;

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
