using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

public class BabtReferenceTests
{
    [Fact]
    public void Shipped_babt_mechanics_extend_the_existing_material_table()
    {
        var book = ReferenceBookTests.ShippedBook();

        Assert.Equal(1, book.BabtSchemaVersion);
        Assert.Empty(book.BabtConstructions);

        var steel = book.ArmorMaterials["ArmoredSteel"];
        Assert.Equal(7.85, steel.DensityGCm3);
        Assert.Equal(1250, steel.YieldMPa);
        AssertEstimated(steel.YoungModulusGPa, 210);
        AssertEstimated(steel.PoissonRatio, 0.30);

        var aramid = book.ArmorMaterials["Aramid"];
        Assert.Equal(2900, aramid.FibreTensileMPa);
        Assert.Equal(0.034, aramid.FailureStrain);
        AssertEstimated(aramid.RigidLaminateModulusGPa, 10.06);

        static void AssertEstimated(ReferenceBook.BabtParameterRef parameter, double value)
        {
            Assert.Equal("Estimated", parameter.Status);
            Assert.Equal(value, parameter.Value);
            Assert.False(string.IsNullOrWhiteSpace(parameter.Source));
        }
    }

    [Fact]
    public void Shipped_profiles_do_not_duplicate_penetration_constructions()
    {
        var book = ReferenceBookTests.ShippedBook();
        Assert.Empty(book.BabtConstructions);
        Assert.Empty(book.BabtImpactProfiles);
        Assert.Equal("Estimated", book.BabtBodyProfiles["Thorax"].EffectiveMassKg.Status);
        Assert.Equal(0.45, book.BabtBodyProfiles["Thorax"].EffectiveMassKg.Value);
        Assert.Equal(525, book.BabtBodyProfiles["Thorax"].FoundationDampingNsPerM.Value);
        Assert.Equal("TissueSlab", book.BabtBodyProfiles["Abdomen"].MassModel);
        Assert.Equal(2e-5, book.BabtNumerics.TimeStepS.Value);
        Assert.Equal(100000, book.BabtNumerics.MaximumIntegrationSteps.Value);
        Assert.Equal(160, book.BabtNumerics.MinimumStepsPerPeriod.Value);
        Assert.Equal("Estimated", book.BabtNumerics.MaximumRelativeEnergyError.Status);
        Assert.Contains("integration", book.BabtNumerics.Source.ToLowerInvariant());
        AssertMissing(book.BabtBodyProfiles["Head"].EffectiveMassKg, "thorax");

        static void AssertMissing(ReferenceBook.BabtParameterRef parameter, string reasonPart)
        {
            Assert.Equal("Missing", parameter.Status);
            Assert.Null(parameter.Value);
            Assert.Contains(reasonPart, parameter.Reason);
        }
    }

    [Fact]
    public void Old_reference_files_gain_babt_schema_without_changing_penetration_tables()
    {
        var original = new ReferenceBook.ArmorPlateRef
        {
            Prototype = "user plate",
            ThicknessMm = 9,
        };
        var old = new ReferenceBook.AmmoReference
        {
            Version = 21,
            ArmorPlates = { ["mine"] = original },
        };

        var filled = ReferenceBook.MergeShippedDefaults(old);

        Assert.Same(original, old.ArmorPlates["mine"]);
        Assert.Equal(9, old.ArmorPlates["mine"].ThicknessMm);
        Assert.Equal(1, old.BabtSchemaVersion);
        Assert.Empty(old.BabtConstructions);
        Assert.Equal(210, old.ArmorMaterials["ArmoredSteel"].YoungModulusGPa.Value);
        Assert.Equal(0.05, old.BabtNumerics.SimulationDurationS.Value);
        Assert.Contains(nameof(old.BabtSchemaVersion), filled);
        Assert.Contains(filled, entry => entry.Contains(nameof(old.BabtNumerics)));
    }
}
