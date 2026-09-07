using System;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// Mechanical tests for the resolved-impact transfer path. Fixtures are synthetic;
/// they verify conservation and limiting behaviour, not empirical BFD or injury.
/// </summary>
public class BabtTransferModelTests
{
    [Fact]
    public void Centre_impulse_initially_moves_local_mass_only()
    {
        var construction = Steel(4.0, 4.0);
        var transfer = BabtTransferModel.Evaluate(Stop(0.008, 300),
            Coupling(construction, 1.0));

        Assert.True(transfer.IsValid, transfer.Reason);
        var impulse = 0.008 * 300;
        Assert.Equal(impulse / construction.LocalEffectiveMassKg,
            transfer.LocalInitialVelocityMps, 12);
        Assert.Equal(0.0, transfer.RigidInitialVelocityMps, 12);

        var translationOnly = BabtTransferModel.Evaluate(Stop(0.008, 300),
            Coupling(construction, 0.0));
        var commonVelocity = impulse /
                             (construction.LocalEffectiveMassKg + construction.RigidEffectiveMassKg);
        Assert.Equal(commonVelocity, translationOnly.LocalInitialVelocityMps, 12);
        Assert.Equal(commonVelocity, translationOnly.RigidInitialVelocityMps, 12);
    }

    [Fact]
    public void Stop_and_pierce_close_projectile_energy_and_momentum_ledgers()
    {
        var construction = Steel(4.0, 4.0);
        var stopped = BabtTransferModel.Evaluate(Stop(0.008, 300),
            Coupling(construction, 1.0));
        var pierced = BabtTransferModel.Evaluate(Pierce(0.010, 800, 0.010, 600,
                new BabtTransferModel.OutgoingBody
                {
                    Name = "co-moving plug",
                    MassKg = 0.002,
                    SpeedMps = 600,
                    NormalVelocityMps = 600,
                    Provenance = "synthetic existing-model plug",
                    Readiness = BabtModel.Readiness.Provisional,
                }), Coupling(construction, 1.0));

        Assert.True(stopped.IsValid, stopped.Reason);
        Assert.True(pierced.IsValid, pierced.Reason);
        Assert.Equal(0.0, stopped.EnergyBalanceErrorJ, 9);
        Assert.Equal(0.0, stopped.MomentumBalanceErrorNs, 9);
        Assert.Equal(0.0, pierced.EnergyBalanceErrorJ, 9);
        Assert.Equal(0.0, pierced.MomentumBalanceErrorNs, 9);
        Assert.Equal(1040.0, pierced.DepositedEnergyJ, 9);
        Assert.Equal(0.8, pierced.TransferredNormalImpulseNs, 9);
        Assert.True(pierced.UnresolvedArmorProjectileDissipationJ >
                    pierced.MechanicalSeedEnergyJ);
    }

    [Fact]
    public void Zero_loss_pierce_produces_zero_mechanical_and_body_response()
    {
        var construction = Steel(4.0, 4.0);
        var hit = Pierce(0.008, 300, 0.008, 300);
        var transfer = BabtTransferModel.Evaluate(hit, Coupling(construction, 1.0));
        var response = BabtModel.EvaluateTransferred(construction, transfer, Thorax(), Numerics());

        Assert.True(transfer.IsValid, transfer.Reason);
        Assert.True(response.IsValid, response.Reason);
        Assert.Equal(0, transfer.DepositedEnergyJ);
        Assert.Equal(0, transfer.MechanicalSeedEnergyJ);
        Assert.Equal(0, response.BodyNetWorkJ);
        Assert.Equal(0, response.FinalMechanicalEnergyJ);
        Assert.Equal(0, response.IntegrationSteps);
    }

    [Fact]
    public void Near_threshold_pierce_converges_to_stop_ledger()
    {
        var construction = Steel(4.0, 4.0);
        var stopped = BabtTransferModel.Evaluate(Stop(0.008, 300),
            Coupling(construction, 1.0));
        var barelyPierced = BabtTransferModel.Evaluate(Pierce(0.008, 300, 0.008, 0.01),
            Coupling(construction, 1.0));

        Assert.True(barelyPierced.IsValid, barelyPierced.Reason);
        Assert.InRange(Math.Abs(stopped.DepositedEnergyJ - barelyPierced.DepositedEnergyJ), 0, 1e-6);
        Assert.InRange(Math.Abs(stopped.MechanicalSeedEnergyJ - barelyPierced.MechanicalSeedEnergyJ),
            0, 1e-4);
    }

    [Fact]
    public void Heavy_steel_gong_seeds_less_motion_and_body_work_than_soft_pack()
    {
        var steel = Steel(4.0, 4.0);
        var soft = SoftPack(0.25, 0.25);
        var hit = Stop(0.008, 300);
        var steelTransfer = BabtTransferModel.Evaluate(hit, Coupling(steel, 1.0));
        var softTransfer = BabtTransferModel.Evaluate(hit, Coupling(soft, 1.0));
        var steelResponse = BabtModel.EvaluateTransferred(steel, steelTransfer, Thorax(), Numerics());
        var softResponse = BabtModel.EvaluateTransferred(soft, softTransfer, Thorax(), Numerics());
        var steelExtended = BabtModel.EvaluateTransferred(steel, steelTransfer, Thorax(),
            Numerics(0.10));

        Assert.True(steelResponse.IsValid, steelResponse.Reason);
        Assert.True(softResponse.IsValid, softResponse.Reason);
        Assert.True(steelResponse.BodyWorkConverged, ResponseState("steel", steelResponse));
        Assert.True(softResponse.BodyWorkConverged, ResponseState("soft", softResponse));
        Assert.True(steelExtended.BodyWorkConverged,
            ResponseState("steel doubled horizon", steelExtended));
        Assert.InRange(Math.Abs(steelResponse.BodyNetWorkJ - steelExtended.BodyNetWorkJ),
            0, 0.01 * steelTransfer.MechanicalSeedEnergyJ);
        Assert.True(steelTransfer.MechanicalSeedEnergyJ < softTransfer.MechanicalSeedEnergyJ / 10.0);
        Assert.True(steelResponse.BodyNetWorkJ < softResponse.BodyNetWorkJ);
        Assert.InRange(steelResponse.BodyNetWorkJ, 0, steelTransfer.MechanicalSeedEnergyJ * 1.03);
    }

    [Fact]
    public void Body_net_work_subtracts_elastic_return_and_global_budget_counts_it_once()
    {
        var construction = Steel(4.0, 4.0);
        var transfer = BabtTransferModel.Evaluate(Stop(0.008, 300),
            Coupling(construction, 1.0));
        var response = BabtModel.EvaluateTransferred(construction, transfer, Thorax(), Numerics());
        var closed = BabtTransferModel.Close(transfer, response);

        Assert.True(response.IsValid, response.Reason);
        Assert.True(closed.IsValid, closed.Reason);
        var bodyLedger = response.BodyRetainedMechanicalEnergyJ +
                         response.BodyFoundationDissipationJ;
        Assert.InRange(Math.Abs(response.BodyNetWorkJ - bodyLedger), 0,
            0.03 * Math.Max(1e-9, response.BodyNetWorkJ));
        Assert.Equal(response.EnergyBalanceErrorJ, closed.EnergyBalanceErrorJ, 9);
        Assert.InRange(Math.Abs(closed.EnergyBalanceErrorJ), 0,
            0.03 * transfer.MechanicalSeedEnergyJ);
    }

    [Fact]
    public void Aggregate_chain_charges_initial_to_final_loss_once()
    {
        var construction = Steel(4.0, 4.0);
        var first = Pierce(0.010, 800, 0.010, 700);
        var second = Pierce(0.010, 700, 0.010, 500);
        var aggregate = BabtTransferModel.EvaluateAggregate(new[] { first, second },
            Coupling(construction, 1.0));
        var audit = BabtTransferModel.EvaluateChain(new[] { first, second },
            new[] { Coupling(construction, 1.0), Coupling(construction, 1.0) });

        Assert.True(aggregate.IsValid, aggregate.Reason);
        Assert.True(audit.IsValid, audit.Reason);
        Assert.Equal(1950.0, aggregate.DepositedEnergyJ, 9);
        Assert.Equal(audit.DepositedEnergyJ, aggregate.DepositedEnergyJ, 9);
        Assert.Equal(3.0, aggregate.TransferredNormalImpulseNs, 9);
        Assert.Equal(0.0, aggregate.EnergyBalanceErrorJ, 9);
    }

    [Fact]
    public void Invalid_or_unbounded_inputs_fail_before_integration()
    {
        var construction = Steel(4.0, 4.0);
        var hit = Stop(0.008, 300);
        hit.Incoming.SpeedMps = double.NaN;
        var invalidTransfer = BabtTransferModel.Evaluate(hit, Coupling(construction, 1.0));
        Assert.False(invalidTransfer.IsValid);
        Assert.Equal(BabtTransferModel.EvaluationStatus.InvalidInput, invalidTransfer.Status);

        var valid = BabtTransferModel.Evaluate(Stop(0.008, 300), Coupling(construction, 1.0));
        var numerics = Numerics();
        numerics.MaximumIntegrationSteps = 10;
        var invalidResponse = BabtModel.EvaluateTransferred(construction, valid, Thorax(), numerics);
        Assert.False(invalidResponse.IsValid);
        Assert.Equal(BabtModel.EvaluationStatus.InvalidInput, invalidResponse.Status);
    }

    [Fact]
    public void Retained_thickness_and_net_section_have_distinct_stiffness_scaling()
    {
        var intactSteel = Steel(4.0, 4.0);
        var wornSteel = Steel(4.0, 4.0);
        wornSteel.Layers[0].EffectiveThicknessFraction = 0.5;
        var netSectionSteel = Steel(4.0, 4.0);
        netSectionSteel.Layers[0].CoherentFraction = 0.5;

        var intactResponse = ZeroSeedResponse(intactSteel);
        var wornResponse = ZeroSeedResponse(wornSteel);
        var netSectionResponse = ZeroSeedResponse(netSectionSteel);
        Assert.Equal(0.125, wornResponse.LinearFlexuralStiffnessNPerM /
                            intactResponse.LinearFlexuralStiffnessNPerM, 12);
        Assert.Equal(0.5, netSectionResponse.LinearFlexuralStiffnessNPerM /
                          intactResponse.LinearFlexuralStiffnessNPerM, 12);

        var intactSoft = SoftPack(0.25, 0.25);
        var wornSoft = SoftPack(0.25, 0.25);
        wornSoft.Layers[0].EffectiveThicknessFraction = 0.5;
        Assert.Equal(0.5, ZeroSeedResponse(wornSoft).MembraneCubicStiffnessNPerM3 /
                          ZeroSeedResponse(intactSoft).MembraneCubicStiffnessNPerM3, 12);
    }

    [Fact]
    public void Brittle_face_dropout_continues_from_current_state_and_closes_energy()
    {
        var construction = CeramicBacked(4.0, 4.0);
        var transfer = BabtTransferModel.Evaluate(Stop(0.048, 780),
            Coupling(construction, 1.0));
        var response = BabtModel.EvaluateTransferred(construction, transfer, Thorax(),
            Numerics(0.10));
        var closed = BabtTransferModel.Close(transfer, response);

        Assert.True(response.IsValid, ResponseState("ceramic stop", response));
        Assert.True(response.BodyWorkConverged, ResponseState("ceramic stop", response));
        Assert.Equal(1, response.ResolvedBrittleDropoutCount);
        Assert.True(response.BrittleFractureDissipationJ > 0);
        Assert.True(response.BodyNetWorkJ > 0);
        Assert.True(response.BodyNetWorkJ < transfer.MechanicalSeedEnergyJ);
        Assert.True(closed.IsValid, closed.Reason);
        Assert.InRange(Math.Abs(closed.EnergyBalanceErrorJ), 0,
            0.03 * transfer.MechanicalSeedEnergyJ);
    }

    private static BabtModel.Response ZeroSeedResponse(BabtModel.Construction construction)
    {
        var transfer = BabtTransferModel.Evaluate(Pierce(0.008, 300, 0.008, 300),
            Coupling(construction, 1.0));
        var response = BabtModel.EvaluateTransferred(construction, transfer, Thorax(), Numerics());
        Assert.True(response.IsValid, response.Reason);
        return response;
    }

    private static string ResponseState(string name, BabtModel.Response response)
    {
        return name + ": " + response.Status + "; " + response.Reason +
               "; stable=" + response.BodyWorkStableDurationS.ToString("G6") +
               "s; q=" + response.MaximumAbsoluteFlexuralDeflectionMm.ToString("G6") +
               "mm; K=" + response.LinearFlexuralStiffnessNPerM.ToString("G6") +
               "N/m; K3=" + response.MembraneCubicStiffnessNPerM3.ToString("G6") +
               "N/m3; recoil=" + response.FreePlateRecoilVelocityMps.ToString("G6") +
               "m/s; Wbody=" + response.BodyNetWorkJ.ToString("G6") +
               "J; brittle=" + response.ResolvedBrittleDropoutCount + "/" +
               response.BrittleFractureDissipationJ.ToString("G6") +
               "J; steps=" + response.IntegrationSteps +
               "; dt=" + response.IntegrationTimeStepS.ToString("G6") + "s";
    }

    private static BabtTransferModel.ResolvedImpact Stop(double mass, double speed)
    {
        return new BabtTransferModel.ResolvedImpact
        {
            LayerId = "synthetic layer",
            Outcome = BabtTransferModel.Outcome.Stop,
            Incoming = Projectile(mass, speed),
            OutgoingPrimary = Projectile(0, 0, true),
            ExistingBarrierWorkJ = 0.5 * mass * speed * speed,
            Provenance = "synthetic resolved stop",
            Readiness = BabtModel.Readiness.Provisional,
        };
    }

    private static BabtTransferModel.ResolvedImpact Pierce(double incomingMass,
        double incomingSpeed, double outgoingMass, double outgoingSpeed,
        params BabtTransferModel.OutgoingBody[] secondaries)
    {
        return new BabtTransferModel.ResolvedImpact
        {
            LayerId = "synthetic layer",
            Outcome = BabtTransferModel.Outcome.Pierce,
            Incoming = Projectile(incomingMass, incomingSpeed),
            OutgoingPrimary = Projectile(outgoingMass, outgoingSpeed),
            OutgoingSecondaries = secondaries,
            ExistingBarrierWorkJ = 0,
            Provenance = "synthetic resolved pierce",
            Readiness = BabtModel.Readiness.Provisional,
        };
    }

    private static BabtTransferModel.ProjectileState Projectile(double mass, double speed,
        bool stopped = false)
    {
        return new BabtTransferModel.ProjectileState
        {
            MassKg = mass,
            SpeedMps = speed,
            NormalVelocityMps = speed,
            Provenance = stopped ? "synthetic stopped state" : "synthetic projectile state",
            Readiness = BabtModel.Readiness.Provisional,
        };
    }

    private static BabtTransferModel.Coupling Coupling(BabtModel.Construction construction,
        double mode)
    {
        return new BabtTransferModel.Coupling
        {
            PostImpactConstruction = construction,
            ImpactModeFactor = mode,
            OutgoingInventoryComplete = true,
            Provenance = "synthetic centre-mode coupling",
            Readiness = BabtModel.Readiness.Provisional,
        };
    }

    private static BabtModel.Construction Steel(double localMass, double rigidMass)
    {
        return new BabtModel.Construction
        {
            WidthM = 0.30,
            HeightM = 0.25,
            LocalEffectiveMassKg = localMass,
            RigidEffectiveMassKg = rigidMass,
            FlexuralDampingNsPerM = 100,
            Boundary = BabtModel.BoundaryCondition.SimplySupported,
            Provenance = "synthetic 10 mm steel gong",
            Readiness = BabtModel.Readiness.Provisional,
            Layers = new[]
            {
                new BabtModel.Layer
                {
                    Name = "synthetic steel",
                    Form = BabtModel.LayerForm.MetalPlate,
                    State = BabtModel.LayerState.Intact,
                    ThicknessM = 0.010,
                    DensityKgM3 = 7850,
                    CoherentFraction = 1,
                    YoungModulusPa = 200e9,
                    PoissonRatio = 0.3,
                    YieldStrengthPa = 1.0e9,
                    FailureStrain = 0.20,
                    Provenance = "synthetic constitutive fixture",
                    Readiness = BabtModel.Readiness.Provisional,
                },
            },
        };
    }

    private static BabtModel.Construction SoftPack(double localMass, double rigidMass)
    {
        return new BabtModel.Construction
        {
            WidthM = 0.30,
            HeightM = 0.25,
            LocalEffectiveMassKg = localMass,
            RigidEffectiveMassKg = rigidMass,
            FlexuralDampingNsPerM = 20,
            Boundary = BabtModel.BoundaryCondition.SimplySupported,
            Provenance = "synthetic soft package",
            Readiness = BabtModel.Readiness.Provisional,
            Layers = new[]
            {
                new BabtModel.Layer
                {
                    Name = "synthetic fabric package",
                    Form = BabtModel.LayerForm.SoftFabric,
                    State = BabtModel.LayerState.Intact,
                    ThicknessM = 0.010,
                    DensityKgM3 = 500,
                    CoherentFraction = 1,
                    MembraneStiffnessNPerM = 1.0e6,
                    FailureStrain = 1.0,
                    Provenance = "synthetic membrane fixture",
                    Readiness = BabtModel.Readiness.Provisional,
                },
            },
        };
    }

    private static BabtModel.Construction CeramicBacked(double localMass, double rigidMass)
    {
        var construction = Steel(localMass, rigidMass);
        construction.Provenance = "synthetic brittle face and retained laminate backing";
        construction.Layers = new[]
        {
            new BabtModel.Layer
            {
                Name = "synthetic alumina face",
                Form = BabtModel.LayerForm.BrittleFace,
                State = BabtModel.LayerState.Intact,
                ThicknessM = 0.010,
                DensityKgM3 = 3900,
                YoungModulusPa = 370e9,
                PoissonRatio = 0.22,
                FailureStrain = 0.00081,
                Provenance = "synthetic ideal-brittle face fixture",
                Readiness = BabtModel.Readiness.Provisional,
            },
            new BabtModel.Layer
            {
                Name = "synthetic UHMWPE laminate backing",
                Form = BabtModel.LayerForm.RigidLaminate,
                State = BabtModel.LayerState.Intact,
                ThicknessM = 0.012,
                DensityKgM3 = 970,
                ExtensionalStiffnessNPerM = 5.42e8,
                FlexuralRigidityNm = 6500,
                FailureStrain = 0.035,
                BondedToPrevious = true,
                Provenance = "synthetic retained laminate backing fixture",
                Readiness = BabtModel.Readiness.Provisional,
            },
        };
        return construction;
    }

    private static BabtModel.BodyProfile Thorax()
    {
        return new BabtModel.BodyProfile
        {
            EffectiveMassKg = 0.45,
            ContactStiffnessNPerM = 2.63e6,
            ContactDampingNsPerM = 0,
            FoundationStiffnessNPerM = 26.3e3,
            FoundationDampingNsPerM = 525,
            InitialGapM = 0,
            EffectiveContactAreaM2 = 0.075,
            Provenance = "synthetic reproduced Lobdell m2 branch plus one-percent penalty contact",
            Readiness = BabtModel.Readiness.Provisional,
        };
    }

    private static BabtModel.Numerics Numerics(double duration = 0.05)
    {
        return new BabtModel.Numerics
        {
            TimeStepS = 2e-5,
            SimulationDurationS = duration,
            MaximumIntegrationSteps = 100000,
            MinimumStepsPerPeriod = 160,
            MaximumRelativeEnergyError = 0.03,
            SettledVelocityToleranceMps = 0.01,
            SettledForceToleranceN = 10,
            Provenance = "synthetic resolved-response integration",
            Readiness = BabtModel.Readiness.Provisional,
        };
    }
}
