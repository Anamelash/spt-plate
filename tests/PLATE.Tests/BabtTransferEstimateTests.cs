using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    public class BabtTransferEstimateTests
    {
        [Fact]
        public void Piercing_chain_charges_each_resolved_loss_once()
        {
            var result = BabtTransferEstimate.Evaluate(new[]
            {
                Contact(500, 320, 20, 0.25, 0.01, 0.08),
                Contact(320, 180, 10, 0.50, 0.01, 0.04),
            }, 1.0);

            Assert.True(result.IsValid);
            Assert.Equal(290, result.AvailableEnergyLossJ, 6);
            Assert.Equal(105, result.BodyWorkEstimateJ, 6);
            Assert.True(result.BodyWorkEstimateJ <= result.AvailableEnergyLossJ);
        }

        [Fact]
        public void Coupling_scale_is_bounded_by_resolved_available_energy()
        {
            var result = BabtTransferEstimate.Evaluate(new[]
            {
                Contact(100, 20, 10, 0.8, 0.009, 0.05),
            }, 4.0);

            Assert.True(result.IsValid);
            Assert.Equal(70, result.AvailableEnergyLossJ, 6);
            Assert.Equal(70, result.BodyWorkEstimateJ, 6);
        }

        [Fact]
        public void Explicit_secondary_energy_is_never_given_to_the_body_again()
        {
            var withoutSecondary = BabtTransferEstimate.Evaluate(new[]
            {
                Contact(200, 100, 0, 1.0, 0.01, 0.04),
            }, 1.0);
            var withSecondary = BabtTransferEstimate.Evaluate(new[]
            {
                Contact(200, 100, 35, 1.0, 0.01, 0.04),
            }, 1.0);

            Assert.Equal(100, withoutSecondary.BodyWorkEstimateJ, 6);
            Assert.Equal(65, withSecondary.BodyWorkEstimateJ, 6);
        }

        [Fact]
        public void Spread_controls_area_without_changing_the_energy_ledger()
        {
            var narrow = BabtTransferEstimate.Evaluate(new[]
            {
                Contact(100, 50, 0, 0.4, 0.01, 0.02),
            }, 1.0);
            var wide = BabtTransferEstimate.Evaluate(new[]
            {
                Contact(100, 50, 0, 0.4, 0.01, 0.10),
            }, 1.0);

            Assert.Equal(narrow.BodyWorkEstimateJ, wide.BodyWorkEstimateJ, 9);
            Assert.True(wide.EffectiveContactAreaM2 > narrow.EffectiveContactAreaM2);
        }

        [Fact]
        public void Inconsistent_contact_budget_is_rejected()
        {
            var result = BabtTransferEstimate.Evaluate(new[]
            {
                Contact(100, 90, 20, 0.5, 0.01, 0.04),
            }, 1.0);

            Assert.False(result.IsValid);
        }

        private static BabtTransferEstimate.ContactInput Contact(double incoming,
            double outgoing, double secondary, double throughput,
            double diameter, double spread)
        {
            return new BabtTransferEstimate.ContactInput
            {
                IncomingEnergyJ = incoming,
                OutgoingPrimaryEnergyJ = outgoing,
                ReservedOrLeavingSecondaryEnergyJ = secondary,
                BluntThroughput = throughput,
                ProjectileDiameterM = diameter,
                SpreadDiameterM = spread,
            };
        }
    }
}
