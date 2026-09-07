using System;
using PLATE.Client.Ballistics;
using Xunit;

namespace PLATE.Tests
{
    public class BabtInjuryModelTests
    {
        private const double ContactArea = 0.01;

        [Fact]
        public void Zero_work_cannot_create_damage_or_effects()
        {
            var result = Evaluate(0);
            Assert.True(result.IsValid);
            Assert.False(result.HasLoad);
            Assert.Equal(0, result.DamageHp);
            Assert.Equal(0, result.Severity);
        }

        [Theory]
        [InlineData(0.07, 0.01)]
        [InlineData(7.0, 1.0)]
        [InlineData(28.0, 4.0)]
        [InlineData(2800.0, 400.0)]
        public void Reuses_contact_energy_scale_without_fixed_floor_or_ceiling(double work, double hp)
        {
            var result = Evaluate(work);
            Assert.True(result.IsValid);
            Assert.True(result.HasLoad);
            Assert.Equal(hp, result.DamageHp, 10);
            Assert.Equal(work, result.DamageHp * 7.0, 10);
        }

        [Fact]
        public void Area_changes_effects_criterion_without_spending_body_work_again()
        {
            var narrow = Evaluate(500, ContactArea);
            var wide = Evaluate(500, ContactArea * 4);
            Assert.Equal(narrow.DamageHp, wide.DamageHp);
            Assert.Equal(narrow.EffectiveDiameterCm * 2, wide.EffectiveDiameterCm, 10);
            Assert.Equal(Math.Log(2), narrow.BluntCriterion - wide.BluntCriterion, 10);
            Assert.True(wide.Severity <= narrow.Severity);
        }

        [Theory]
        [InlineData(1.8, 0.0)]
        [InlineData(2.6, 0.5)]
        [InlineData(3.4, 1.0)]
        public void Legacy_effect_thresholds_do_not_limit_hp(double bc, double severity)
        {
            var diameterCm = 200 * Math.Sqrt(ContactArea / Math.PI);
            var work = Math.Exp(bc) * Math.Pow(80, 1.0 / 3.0) * 3.5 * diameterCm;
            var result = Evaluate(work);
            Assert.Equal(bc, result.BluntCriterion, 10);
            Assert.Equal(severity, result.Severity, 10);
            Assert.Equal(work / 7, result.DamageHp, 10);
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Invalid_work_fails_without_creating_damage(double work)
        {
            var result = Evaluate(work);
            Assert.False(result.IsValid);
            Assert.Equal(0, result.DamageHp);
        }

        [Fact]
        public void Missing_or_invalid_material_area_and_scale_are_not_silent_defaults()
        {
            Assert.False(Evaluate(10, 0).IsValid);
            Assert.False(BabtInjuryModel.Evaluate(10, ContactArea, 80, 3.5, 0, 1.8, 3.4).IsValid);
            Assert.False(BabtInjuryModel.Evaluate(10, ContactArea, 0, 3.5, 7, 1.8, 3.4).IsValid);
            Assert.False(BabtInjuryModel.Evaluate(10, ContactArea, 80, 3.5, 7, 3.4, 1.8).IsValid);
        }

        private static BabtInjuryModel.Result Evaluate(double work, double area = ContactArea) =>
            BabtInjuryModel.Evaluate(work, area, 80, 3.5, 7, 1.8, 3.4);
    }
}
