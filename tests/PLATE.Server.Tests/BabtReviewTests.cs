using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>Independent review of the reduction's limiting cases, not product calibration.</summary>
public class BabtReviewTests
{
    [Fact]
    public void Elastic_response_has_no_energy_floor_or_damage_style_plateau()
    {
        var small = BabtModel.Evaluate(Panel(), Projectile(1), Body());
        var large = BabtModel.Evaluate(Panel(), Projectile(3), Body());

        Assert.True(small.IsValid, small.Reason);
        Assert.True(large.IsValid, large.Reason);
        Assert.True(small.BodyWorkJ > 0);
        // Linear passive mechanics with a zero gap is homogeneous: displacement
        // and impulse scale with velocity; work scales with velocity squared.
        Assert.InRange(large.RearDynamicDeflectionMm / small.RearDynamicDeflectionMm, 2.999, 3.001);
        Assert.InRange(large.BodyImpulseNs / small.BodyImpulseNs, 2.999, 3.001);
        Assert.InRange(large.BodyWorkJ / small.BodyWorkJ, 8.999, 9.001);
    }

    [Fact]
    public void No_body_contact_means_no_body_work_even_when_the_plate_is_hit()
    {
        var body = Body();
        body.InitialGapM = 10; // deliberately unreachable synthetic target
        var result = BabtModel.Evaluate(Panel(), Projectile(10), body);

        Assert.True(result.IsValid, result.Reason);
        Assert.True(result.RearDynamicDeflectionMm > 0);
        Assert.Equal(0, result.BodyWorkJ);
        Assert.Equal(0, result.BodyImpulseNs);
        Assert.Equal(0, result.BodyCompressionMaxMm);
        Assert.Equal(0, result.PeakBodyForceN);
    }

    [Fact]
    public void A_short_observation_window_is_not_a_completed_impact()
    {
        var impact = Projectile(10);
        impact.SimulationDurationS = 0.0001;
        var result = BabtModel.Evaluate(Panel(), impact, Body());

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(BabtModel.EvaluationStatus.IncompleteTimeHorizon, result.Status);
    }

    [Fact]
    public void A_constitutive_domain_exit_does_not_produce_an_injury_ready_result()
    {
        var panel = Panel();
        panel.Layers[0].Form = BabtModel.LayerForm.BrittleFace;
        panel.Layers[0].FailureStrain = 1e-12;
        var result = BabtModel.Evaluate(panel, Projectile(10), Body());

        Assert.False(result.IsValid);
        Assert.Equal(BabtModel.EvaluationStatus.Unsupported, result.Status);
        Assert.Equal(BabtModel.Readiness.Unsupported, result.Readiness);
    }

    [Fact]
    public void Nonfinite_material_data_is_rejected_before_time_integration()
    {
        var panel = Panel();
        panel.Layers[0].YoungModulusPa = double.NaN;
        var result = BabtModel.Evaluate(panel, Projectile(10), Body());

        Assert.False(result.IsValid);
        Assert.Equal(0, result.IntegrationSteps);
        Assert.Equal(BabtModel.EvaluationStatus.InvalidInput, result.Status);
    }

    private const string Evidence = "synthetic review fixture; no real armor calibration";

    private static BabtModel.Construction Panel() => new()
    {
        WidthM = 0.30,
        HeightM = 0.25,
        LocalEffectiveMassKg = 0.25,
        RigidEffectiveMassKg = 2.5,
        FlexuralDampingNsPerM = 100,
        Boundary = BabtModel.BoundaryCondition.SimplySupported,
        Readiness = BabtModel.Readiness.Provisional,
        Provenance = Evidence,
        Layers = new[]
        {
            new BabtModel.Layer
            {
                Name = "review elastic face",
                Form = BabtModel.LayerForm.MetalPlate,
                ThicknessM = 0.006,
                DensityKgM3 = 7850,
                YoungModulusPa = 200e9,
                PoissonRatio = 0.3,
                YieldStrengthPa = 1e9,
                FailureStrain = 0.2,
                Readiness = BabtModel.Readiness.Provisional,
                Provenance = Evidence,
            },
        },
    };

    private static BabtModel.Impact Projectile(double speed) => new()
    {
        ProjectileMassKg = 0.008,
        NormalVelocityMps = speed,
        ProjectileContactStiffnessNPerM = 2e6,
        ProjectileContactDampingNsPerM = 20,
        ContactAreaM2 = 0.000064,
        TimeStepS = 1e-6,
        SimulationDurationS = 0.020,
        MaximumIntegrationSteps = 30000,
        MinimumStepsPerPeriod = 30,
        MaximumRelativeEnergyError = 0.02,
        SettledVelocityToleranceMps = 0.001,
        SettledForceToleranceN = 0.01,
        Readiness = BabtModel.Readiness.Provisional,
        Provenance = Evidence,
    };

    private static BabtModel.BodyProfile Body() => new()
    {
        EffectiveMassKg = 5,
        ContactStiffnessNPerM = 1e5,
        ContactDampingNsPerM = 20,
        FoundationStiffnessNPerM = 2e4,
        FoundationDampingNsPerM = 40,
        InitialGapM = 0,
        EffectiveContactAreaM2 = 0.01,
        Readiness = BabtModel.Readiness.Provisional,
        Provenance = Evidence,
    };
}
