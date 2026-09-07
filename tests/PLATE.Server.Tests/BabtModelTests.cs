using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// Mechanical verification of the reduced BABT core. Every positive fixture is
/// synthetic and says so in provenance: these tests exercise conservation, passivity,
/// constitutive scaling and numerical convergence. They are not armour or injury
/// calibration evidence.
/// </summary>
public class BabtModelTests
{
    [Fact]
    public void Zero_normal_velocity_produces_no_motion_or_work()
    {
        var result = Evaluate(MetalConstruction(0.006), Impact(0), Body());

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(BabtModel.EvaluationStatus.Complete, result.Status);
        Assert.Equal(0, result.RearDynamicDeflectionMm);
        Assert.Equal(0, result.RigidTranslationMaxMm);
        Assert.Equal(0, result.BodyCompressionMaxMm);
        Assert.Equal(0, result.BodyImpulseNs);
        Assert.Equal(0, result.BodyWorkJ);
        Assert.Equal(0, result.FinalMechanicalEnergyJ);
    }

    [Fact]
    public void Isotropic_plate_bending_stiffness_scales_with_thickness_cubed()
    {
        var thin = Evaluate(MetalConstruction(0.003), Impact(0), Body());
        var thick = Evaluate(MetalConstruction(0.006), Impact(0), Body());

        Assert.True(thin.IsValid, thin.Reason);
        Assert.True(thick.IsValid, thick.Reason);
        Assert.Equal(8.0,
            thick.LinearFlexuralStiffnessNPerM / thin.LinearFlexuralStiffnessNPerM, 10);
    }

    [Fact]
    public void Bonding_two_faces_adds_parallel_axis_stiffness()
    {
        var unbonded = TwoMetalFaces(false);
        var bonded = TwoMetalFaces(true);

        var slip = Evaluate(unbonded, Impact(0), Body());
        var composite = Evaluate(bonded, Impact(0), Body());

        Assert.True(slip.IsValid, slip.Reason);
        Assert.True(composite.IsValid, composite.Reason);
        Assert.InRange(composite.LinearFlexuralStiffnessNPerM /
                       slip.LinearFlexuralStiffnessNPerM, 3.99, 4.01);
    }

    [Fact]
    public void A_soft_package_is_a_membrane_not_a_monolithic_plate()
    {
        var soft = MetalConstruction(0.006);
        soft.Layers = new[]
        {
            new BabtModel.Layer
            {
                Name = "synthetic unbonded fabric package",
                Form = BabtModel.LayerForm.SoftFabric,
                State = BabtModel.LayerState.Intact,
                ThicknessM = 0.006,
                DensityKgM3 = 700,
                MembraneStiffnessNPerM = 1.0e6,
                FailureStrain = 0.5,
                Provenance = Synthetic,
                Readiness = BabtModel.Readiness.Calibrated,
            },
        };

        var result = Evaluate(soft, Impact(0), Body());

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(0, result.LinearFlexuralStiffnessNPerM);
        Assert.True(result.MembraneCubicStiffnessNPerM3 > 0);
    }

    [Fact]
    public void The_coupled_system_closes_momentum_and_has_only_passive_losses()
    {
        var impact = Impact(100);
        impact.TimeStepS = 0.000001;
        impact.MaximumRelativeEnergyError = 0.08;

        var result = Evaluate(MetalConstruction(0.006), impact, Body());

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(BabtModel.Readiness.Provisional, result.Readiness);
        Assert.True(result.ContactDissipationJ >= 0);
        Assert.True(result.BodyDissipationJ >= 0);
        Assert.True(result.StructuralDissipationJ >= 0);
        Assert.True(result.PlasticDissipationJ >= 0);
        Assert.InRange(System.Math.Abs(result.MomentumBalanceErrorNs), 0, 1e-8);
        Assert.InRange(System.Math.Abs(result.EnergyBalanceErrorJ), 0,
            result.IncidentNormalEnergyJ * impact.MaximumRelativeEnergyError);
    }

    [Fact]
    public void Halving_a_resolved_time_step_converges_the_peak_deflection()
    {
        var coarseImpact = Impact(100);
        coarseImpact.TimeStepS = 0.000002;
        var fineImpact = Impact(100);
        fineImpact.TimeStepS = 0.000001;

        var coarse = Evaluate(MetalConstruction(0.006), coarseImpact, Body());
        var fine = Evaluate(MetalConstruction(0.006), fineImpact, Body());

        Assert.True(coarse.IsValid, coarse.Reason);
        Assert.True(fine.IsValid, fine.Reason);
        Assert.True(fine.RearDynamicDeflectionMm > 0);
        Assert.InRange(coarse.RearDynamicDeflectionMm / fine.RearDynamicDeflectionMm,
            0.97, 1.03);
    }

    [Fact]
    public void Translation_and_local_rear_deflection_are_separate_observables()
    {
        var result = Evaluate(MetalConstruction(0.010), Impact(100), Body());

        Assert.True(result.IsValid, result.Reason);
        Assert.True(result.RigidTranslationMaxMm > 0);
        Assert.True(result.RearDynamicDeflectionMm >= 0);
        Assert.NotEqual(result.RigidTranslationMaxMm, result.RearDynamicDeflectionMm);
        Assert.Equal(0, result.RearResidualDeflectionMm, 8);
    }

    [Fact]
    public void Contact_compression_is_not_reported_as_body_foundation_compression()
    {
        var result = Evaluate(MetalConstruction(0.006), Impact(100), Body());

        Assert.True(result.IsValid, result.Reason);
        Assert.True(result.ContactCompressionMaxMm > 0);
        Assert.True(result.BodyCompressionMaxMm > 0);
        Assert.NotEqual(result.ContactCompressionMaxMm, result.BodyCompressionMaxMm);
    }

    [Fact]
    public void Missing_contact_data_stays_explicitly_unsupported()
    {
        var impact = Impact(100);
        impact.Readiness = BabtModel.Readiness.Unsupported;
        impact.ProjectileContactStiffnessNPerM = 0;

        var result = Evaluate(MetalConstruction(0.006), impact, Body());

        Assert.False(result.IsValid);
        Assert.Equal(BabtModel.Readiness.Unsupported, result.Readiness);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void A_failed_brittle_face_is_not_treated_as_an_elastic_plate()
    {
        var construction = MetalConstruction(0.008);
        construction.Layers[0].Name = "synthetic failed ceramic";
        construction.Layers[0].Form = BabtModel.LayerForm.BrittleFace;
        construction.Layers[0].State = BabtModel.LayerState.Failed;
        construction.Layers[0].YieldStrengthPa = 0;
        construction.Layers[0].FailureStrain = 0.002;

        var result = Evaluate(construction, Impact(100), Body());

        Assert.False(result.IsValid);
        Assert.Equal(BabtModel.EvaluationStatus.Unsupported, result.Status);
        Assert.Contains("Failed", result.Reason);
    }

    [Fact]
    public void A_perforating_hit_is_left_to_terminal_ballistics()
    {
        var impact = Impact(100);
        impact.Perforates = true;

        var result = Evaluate(MetalConstruction(0.006), impact, Body());

        Assert.False(result.IsValid);
        Assert.Equal(BabtModel.EvaluationStatus.Unsupported, result.Status);
        Assert.Equal(0, result.IntegrationSteps);
    }

    [Fact]
    public void Caller_workload_limit_rejects_the_hit_before_integration()
    {
        var impact = Impact(100);
        impact.MaximumIntegrationSteps = 10;

        var result = Evaluate(MetalConstruction(0.006), impact, Body());

        Assert.False(result.IsValid);
        Assert.Equal(BabtModel.EvaluationStatus.InvalidInput, result.Status);
        Assert.Equal(0, result.IntegrationSteps);
    }

    private const string Synthetic = "synthetic mechanical-verification fixture; not empirical calibration";

    private static BabtModel.Response Evaluate(BabtModel.Construction construction,
        BabtModel.Impact impact, BabtModel.BodyProfile body)
    {
        return BabtModel.Evaluate(construction, impact, body);
    }

    private static BabtModel.Construction MetalConstruction(double thicknessM)
    {
        return new BabtModel.Construction
        {
            WidthM = 0.30,
            HeightM = 0.25,
            LocalEffectiveMassKg = 0.25,
            RigidEffectiveMassKg = 2.5,
            FlexuralDampingNsPerM = 100,
            Boundary = BabtModel.BoundaryCondition.SimplySupported,
            Provenance = Synthetic,
            Readiness = BabtModel.Readiness.Calibrated,
            Layers = new[] { MetalLayer("synthetic metal face", thicknessM) },
        };
    }

    private static BabtModel.Construction TwoMetalFaces(bool bonded)
    {
        var result = MetalConstruction(0.001);
        result.Layers = new[]
        {
            MetalLayer("synthetic front face", 0.001),
            MetalLayer("synthetic rear face", 0.001),
        };
        result.Layers[1].BondedToPrevious = bonded;
        return result;
    }

    private static BabtModel.Layer MetalLayer(string name, double thicknessM)
    {
        return new BabtModel.Layer
        {
            Name = name,
            Form = BabtModel.LayerForm.MetalPlate,
            State = BabtModel.LayerState.Intact,
            ThicknessM = thicknessM,
            DensityKgM3 = 7850,
            YoungModulusPa = 200e9,
            PoissonRatio = 0.30,
            YieldStrengthPa = 1.0e9,
            FailureStrain = 0.20,
            Provenance = Synthetic,
            Readiness = BabtModel.Readiness.Calibrated,
        };
    }

    private static BabtModel.Impact Impact(double normalVelocityMps)
    {
        return new BabtModel.Impact
        {
            ProjectileMassKg = 0.008,
            NormalVelocityMps = normalVelocityMps,
            ProjectileContactStiffnessNPerM = 2.0e6,
            ProjectileContactDampingNsPerM = 20,
            ContactAreaM2 = 0.000064,
            TimeStepS = 0.000002,
            SimulationDurationS = 0.020,
            MaximumIntegrationSteps = 100000,
            MinimumStepsPerPeriod = 20,
            MaximumRelativeEnergyError = 0.10,
            SettledVelocityToleranceMps = 0.05,
            SettledForceToleranceN = 1,
            Provenance = Synthetic,
            Readiness = BabtModel.Readiness.Calibrated,
        };
    }

    private static BabtModel.BodyProfile Body()
    {
        return new BabtModel.BodyProfile
        {
            EffectiveMassKg = 5,
            ContactStiffnessNPerM = 100000,
            ContactDampingNsPerM = 20,
            FoundationStiffnessNPerM = 20000,
            FoundationDampingNsPerM = 40,
            InitialGapM = 0,
            EffectiveContactAreaM2 = 0.010,
            Provenance = Synthetic,
            Readiness = BabtModel.Readiness.Calibrated,
        };
    }
}
