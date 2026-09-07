using System;

namespace PLATE.Server.Services;

/// <summary>
/// Closes the projectile-to-armour energy and normal-momentum budget after penetration
/// has already been decided elsewhere. It never computes a ballistic limit, residual
/// velocity, armour work or projectile fate.
///
/// The coherent mechanical seed is the impulse response of two orthogonal reduced
/// coordinates: translation of the participating construction and one flexural mode.
/// E_translation = J^2/(2 M). For q=x_local-x_rigid, the flexural generalized impulse
/// is (m_rigid/M) phi J and E_flex=Q_q^2/(2 M_mode). Everything deposited but not
/// carried by those coordinates remains explicitly unresolved armour/projectile
/// dissipation. It is not silently called heat, armour work or body work.
/// </summary>
public static class BabtTransferModel
{
    private const double NumericalRelativeTolerance = 1e-9;

    public enum Outcome
    {
        Stop,
        Pierce,
    }

    public enum EvaluationStatus
    {
        Complete,
        IncompleteOutgoingInventory,
        InvalidInput,
        InconsistentBudget,
        InvalidChain,
    }

    public enum AggregateDisposition
    {
        Unknown = 0,
        LeavesAggregateSystem = 1,
        RetainedInAggregateConstruction = 2,
    }

    public sealed class ProjectileState
    {
        public double MassKg { get; set; }
        public double SpeedMps { get; set; }
        /// <summary>
        /// Component in one shared inward body/assembly normal basis for the whole hit
        /// chain. Per-collider normal components must be reprojected before aggregation.
        /// </summary>
        public double NormalVelocityMps { get; set; }
        public string Provenance { get; set; } = "";
        public BabtModel.Readiness Readiness { get; set; }
    }

    public sealed class OutgoingBody
    {
        public string Name { get; set; } = "";
        public double MassKg { get; set; }
        public double SpeedMps { get; set; }
        /// <summary>Component in the same shared normal basis as the primary states.</summary>
        public double NormalVelocityMps { get; set; }
        public AggregateDisposition Disposition { get; set; }
        public string Provenance { get; set; } = "";
        public BabtModel.Readiness Readiness { get; set; }
    }

    public sealed class ResolvedImpact
    {
        public string LayerId { get; set; } = "";
        public Outcome Outcome { get; set; }
        public ProjectileState Incoming { get; set; } = new ProjectileState();
        public ProjectileState OutgoingPrimary { get; set; } = new ProjectileState();
        public OutgoingBody[] OutgoingSecondaries { get; set; } = new OutgoingBody[0];

        /// <summary>
        /// Existing BallisticLimit.WorkJ/eCost figure for audit only. That calculation
        /// prices penetration; it does not identify heat, coherent motion or body work.
        /// </summary>
        public double ExistingBarrierWorkJ { get; set; }

        /// <summary>
        /// Energy carried by projectile material retained in the armour, for audit. It
        /// remains inside deposited energy and is not automatically irreversible.
        /// </summary>
        public double EmbeddedProjectileEnergyJ { get; set; }

        public double FaceWearFraction { get; set; }
        public double BackingWearFraction { get; set; }
        public string Provenance { get; set; } = "";
        public BabtModel.Readiness Readiness { get; set; }
    }

    public sealed class Coupling
    {
        /// <summary>The surviving construction after this event, including a hole on pierce.</summary>
        public BabtModel.Construction PostImpactConstruction { get; set; } =
            new BabtModel.Construction();

        /// <summary>
        /// Value of the retained spatial mode at the impact point, 0..1. A centre-hit
        /// estimate is one and cannot by itself be marked empirically calibrated.
        /// </summary>
        public double ImpactModeFactor { get; set; }

        /// <summary>
        /// True when the primary projectile and every detached body represented by the
        /// existing ballistic model (for example its co-moving plug) are included. This
        /// does not claim that unmodelled microscopic debris vectors were measured.
        /// </summary>
        public bool OutgoingInventoryComplete { get; set; }

        public string Provenance { get; set; } = "";
        public BabtModel.Readiness Readiness { get; set; }
    }

    public sealed class Transfer
    {
        public bool IsValid { get; set; }
        public EvaluationStatus Status { get; set; }
        public BabtModel.Readiness Readiness { get; set; }
        public string Reason { get; set; } = "";
        public string ProvenanceSummary { get; set; } = "";
        public Outcome Outcome { get; set; }

        public double IncomingEnergyJ { get; set; }
        public double OutgoingPrimaryEnergyJ { get; set; }
        public double OutgoingSecondaryEnergyJ { get; set; }
        public double DepositedEnergyJ { get; set; }
        public double IncomingNormalMomentumNs { get; set; }
        public double OutgoingPrimaryNormalMomentumNs { get; set; }
        public double OutgoingSecondaryNormalMomentumNs { get; set; }
        public double TransferredNormalImpulseNs { get; set; }

        public double TranslationMechanicalEnergyJ { get; set; }
        public double FlexuralMechanicalEnergyJ { get; set; }
        public double MechanicalSeedEnergyJ { get; set; }
        public double UnresolvedArmorProjectileDissipationJ { get; set; }
        public double TranslationVelocityMps { get; set; }
        public double FlexuralVelocityMps { get; set; }
        public double LocalInitialVelocityMps { get; set; }
        public double RigidInitialVelocityMps { get; set; }

        public double ExistingBarrierWorkJ { get; set; }
        public double EmbeddedProjectileEnergyJ { get; set; }
        public double FaceWearFraction { get; set; }
        public double BackingWearFraction { get; set; }
        public double EnergyBalanceErrorJ { get; set; }
        public double MomentumBalanceErrorNs { get; set; }
    }

    public sealed class Chain
    {
        public bool IsValid { get; set; }
        public EvaluationStatus Status { get; set; }
        public string Reason { get; set; } = "";
        public Transfer[] Layers { get; set; } = new Transfer[0];
        public double IncomingEnergyJ { get; set; }
        public double FinalPrimaryEnergyJ { get; set; }
        public double OutgoingSecondaryEnergyJ { get; set; }
        public double DepositedEnergyJ { get; set; }
        public double MechanicalSeedEnergyJ { get; set; }
        public double UnresolvedArmorProjectileDissipationJ { get; set; }
        public double EnergyBalanceErrorJ { get; set; }
    }

    public sealed class ClosedBudget
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; } = "";
        public double IncomingEnergyJ { get; set; }
        public double OutgoingEnergyJ { get; set; }
        public double UnresolvedArmorProjectileDissipationJ { get; set; }
        public double FinalMechanicalEnergyJ { get; set; }
        public double MechanicalDissipationJ { get; set; }
        public double BodyNetWorkJ { get; set; }
        public double EnergyBalanceErrorJ { get; set; }
    }

    public static Transfer Evaluate(ResolvedImpact impact, Coupling coupling)
    {
        if (impact == null || coupling == null || impact.Incoming == null ||
            impact.OutgoingPrimary == null || coupling.PostImpactConstruction == null)
            return Failure(EvaluationStatus.InvalidInput, "Impact, states and post-impact coupling are required.");

        var invalid = Validate(impact, coupling);
        if (invalid != null) return Failure(EvaluationStatus.InvalidInput, invalid);

        var incomingEnergy = Energy(impact.Incoming.MassKg, impact.Incoming.SpeedMps);
        var outgoingPrimaryEnergy = Energy(impact.OutgoingPrimary.MassKg,
            impact.OutgoingPrimary.SpeedMps);
        var incomingMomentum = impact.Incoming.MassKg * impact.Incoming.NormalVelocityMps;
        var outgoingPrimaryMomentum = impact.OutgoingPrimary.MassKg *
                                      impact.OutgoingPrimary.NormalVelocityMps;
        var secondaryEnergy = 0.0;
        var secondaryMomentum = 0.0;
        var readiness = Min(BabtModel.Readiness.Provisional,
            Min(impact.Readiness, coupling.Readiness));
        readiness = Min(readiness, impact.Incoming.Readiness);
        readiness = Min(readiness, impact.OutgoingPrimary.Readiness);
        for (var n = 0; n < impact.OutgoingSecondaries.Length; n++)
        {
            var body = impact.OutgoingSecondaries[n];
            secondaryEnergy += Energy(body.MassKg, body.SpeedMps);
            secondaryMomentum += body.MassKg * body.NormalVelocityMps;
            readiness = Min(readiness, body.Readiness);
        }

        var deposited = incomingEnergy - outgoingPrimaryEnergy - secondaryEnergy;
        var energyTolerance = NumericalRelativeTolerance * Math.Max(1.0, incomingEnergy);
        if (deposited < -energyTolerance)
            return Failure(EvaluationStatus.InconsistentBudget,
                "Outgoing kinetic energy exceeds incoming kinetic energy.");
        if (deposited < 0) deposited = 0;

        var impulse = incomingMomentum - outgoingPrimaryMomentum - secondaryMomentum;
        if (impulse < 0)
            return Failure(EvaluationStatus.InconsistentBudget,
                "Outgoing forward normal momentum exceeds incoming normal momentum.");

        var construction = coupling.PostImpactConstruction;
        var translatingMass = construction.LocalEffectiveMassKg + construction.RigidEffectiveMassKg;
        var modalMass = construction.LocalEffectiveMassKg * construction.RigidEffectiveMassKg /
                        translatingMass;
        // X is the centre-of-mass translation and q = x_local - x_rigid. With
        // x_local = X + (m_r/M)q, a physical impulse at the retained mode produces
        // Q_q = (m_r/M) phi J. At phi=1 this reconstructs v_local=J/m_local,
        // v_rigid=0; at phi=0 the construction translates without flexural velocity.
        var modalImpulse = construction.RigidEffectiveMassKg / translatingMass *
                           coupling.ImpactModeFactor * impulse;
        var translationEnergy = impulse * impulse / (2.0 * translatingMass);
        var flexuralEnergy = modalImpulse * modalImpulse / (2.0 * modalMass);
        var seed = translationEnergy + flexuralEnergy;
        if (seed > deposited + energyTolerance)
        {
            return Failure(EvaluationStatus.InconsistentBudget,
                "The resolved impulse requires more coherent mechanical energy than the projectile deposited.");
        }
        if (seed > deposited) seed = deposited;

        var result = new Transfer
        {
            IsValid = true,
            Status = coupling.OutgoingInventoryComplete
                ? EvaluationStatus.Complete
                : EvaluationStatus.IncompleteOutgoingInventory,
            Readiness = coupling.OutgoingInventoryComplete ? readiness : BabtModel.Readiness.Unsupported,
            Reason = coupling.OutgoingInventoryComplete
                ? "Closed resolved-impact seed; remaining deposited energy is unresolved armour/projectile dissipation."
                : "Outgoing armour/projectile inventory is incomplete; deposited energy and impulse are upper bounds.",
            ProvenanceSummary = impact.Provenance + "; coupling: " + coupling.Provenance,
            Outcome = impact.Outcome,
            IncomingEnergyJ = incomingEnergy,
            OutgoingPrimaryEnergyJ = outgoingPrimaryEnergy,
            OutgoingSecondaryEnergyJ = secondaryEnergy,
            DepositedEnergyJ = deposited,
            IncomingNormalMomentumNs = incomingMomentum,
            OutgoingPrimaryNormalMomentumNs = outgoingPrimaryMomentum,
            OutgoingSecondaryNormalMomentumNs = secondaryMomentum,
            TransferredNormalImpulseNs = impulse,
            TranslationMechanicalEnergyJ = translationEnergy,
            FlexuralMechanicalEnergyJ = flexuralEnergy,
            MechanicalSeedEnergyJ = seed,
            UnresolvedArmorProjectileDissipationJ = deposited - seed,
            TranslationVelocityMps = impulse / translatingMass,
            FlexuralVelocityMps = modalImpulse / modalMass,
            ExistingBarrierWorkJ = impact.ExistingBarrierWorkJ,
            EmbeddedProjectileEnergyJ = impact.EmbeddedProjectileEnergyJ,
            FaceWearFraction = impact.FaceWearFraction,
            BackingWearFraction = impact.BackingWearFraction,
        };
        result.LocalInitialVelocityMps = result.TranslationVelocityMps +
                                         construction.RigidEffectiveMassKg / translatingMass *
                                         result.FlexuralVelocityMps;
        result.RigidInitialVelocityMps = result.TranslationVelocityMps -
                                         construction.LocalEffectiveMassKg / translatingMass *
                                         result.FlexuralVelocityMps;
        result.EnergyBalanceErrorJ = outgoingPrimaryEnergy + secondaryEnergy +
                                     result.MechanicalSeedEnergyJ +
                                     result.UnresolvedArmorProjectileDissipationJ - incomingEnergy;
        result.MomentumBalanceErrorNs = outgoingPrimaryMomentum + secondaryMomentum +
                                        result.TransferredNormalImpulseNs - incomingMomentum;
        return result;
    }

    public static Chain EvaluateChain(ResolvedImpact[] impacts, Coupling[] couplings)
    {
        if (impacts == null || couplings == null || impacts.Length == 0 ||
            impacts.Length != couplings.Length)
            return ChainFailure("A non-empty impact/coupling pair is required for every chain layer.");

        var layers = new Transfer[impacts.Length];
        for (var n = 0; n < impacts.Length; n++)
        {
            if (n > 0 && !SameState(impacts[n - 1].OutgoingPrimary, impacts[n].Incoming))
                return ChainFailure("Layer " + n + " incoming state does not equal the preceding layer output.");
            layers[n] = Evaluate(impacts[n], couplings[n]);
            if (!layers[n].IsValid)
                return ChainFailure("Layer " + n + " failed: " + layers[n].Reason);
        }

        var result = new Chain
        {
            IsValid = true,
            Status = EvaluationStatus.Complete,
            Reason = "Layer states are continuous and every loss is charged once.",
            Layers = layers,
            IncomingEnergyJ = layers[0].IncomingEnergyJ,
            FinalPrimaryEnergyJ = layers[layers.Length - 1].OutgoingPrimaryEnergyJ,
        };
        for (var n = 0; n < layers.Length; n++)
        {
            result.OutgoingSecondaryEnergyJ += layers[n].OutgoingSecondaryEnergyJ;
            result.DepositedEnergyJ += layers[n].DepositedEnergyJ;
            result.MechanicalSeedEnergyJ += layers[n].MechanicalSeedEnergyJ;
            result.UnresolvedArmorProjectileDissipationJ +=
                layers[n].UnresolvedArmorProjectileDissipationJ;
            if (layers[n].Status == EvaluationStatus.IncompleteOutgoingInventory)
                result.Status = EvaluationStatus.IncompleteOutgoingInventory;
        }
        result.EnergyBalanceErrorJ = result.FinalPrimaryEnergyJ +
                                     result.OutgoingSecondaryEnergyJ + result.DepositedEnergyJ -
                                     result.IncomingEnergyJ;
        return result;
    }

    /// <summary>
    /// Close one projectile ledger across an ordered stack and seed one composite
    /// post-impact construction. This is the only chain result intended for one body
    /// response; summing independent layer responses would charge shared support twice.
    /// </summary>
    public static Transfer EvaluateAggregate(ResolvedImpact[] impacts, Coupling compositeCoupling)
    {
        if (impacts == null || impacts.Length == 0 || compositeCoupling == null)
            return Failure(EvaluationStatus.InvalidChain,
                "A non-empty ordered impact chain and composite coupling are required.");

        var secondaryCount = 0;
        var existingWork = 0.0;
        var embeddedEnergy = 0.0;
        var faceWear = 0.0;
        var backingWear = 0.0;
        var readiness = BabtModel.Readiness.Provisional;
        var provenance = "aggregate chain";
        var unresolvedSecondaryDisposition = false;
        for (var n = 0; n < impacts.Length; n++)
        {
            var hit = impacts[n];
            if (hit == null)
                return Failure(EvaluationStatus.InvalidChain, "Impact chain contains a null layer.");
            if (hit.Incoming == null || hit.OutgoingPrimary == null)
                return Failure(EvaluationStatus.InvalidChain,
                    "Layer " + n + " has a null primary projectile state.");
            var impactError = ValidateImpact(hit);
            if (impactError != null)
                return Failure(EvaluationStatus.InvalidChain,
                    "Layer " + n + " is invalid: " + impactError);
            if (n > 0 && !SameState(impacts[n - 1].OutgoingPrimary, hit.Incoming))
                return Failure(EvaluationStatus.InvalidChain,
                    "Layer " + n + " incoming state does not equal the preceding layer output in the shared normal basis.");
            if (hit.OutgoingSecondaries == null)
                return Failure(EvaluationStatus.InvalidChain,
                    "Layer " + n + " has a null outgoing-secondary inventory.");

            var incoming = Energy(hit.Incoming.MassKg, hit.Incoming.SpeedMps);
            var outgoing = Energy(hit.OutgoingPrimary.MassKg, hit.OutgoingPrimary.SpeedMps);
            for (var s = 0; s < hit.OutgoingSecondaries.Length; s++)
            {
                if (hit.OutgoingSecondaries[s] == null)
                    return Failure(EvaluationStatus.InvalidChain,
                        "Layer " + n + " has a null outgoing secondary.");
                outgoing += Energy(hit.OutgoingSecondaries[s].MassKg,
                    hit.OutgoingSecondaries[s].SpeedMps);
            }
            if (outgoing > incoming + NumericalRelativeTolerance * Math.Max(1.0, incoming))
                return Failure(EvaluationStatus.InvalidChain,
                    "Layer " + n + " has more outgoing than incoming kinetic energy.");

            for (var s = 0; s < hit.OutgoingSecondaries.Length; s++)
            {
                var disposition = hit.OutgoingSecondaries[s].Disposition;
                if (disposition == AggregateDisposition.LeavesAggregateSystem)
                    secondaryCount++;
                else if (disposition == AggregateDisposition.Unknown)
                    unresolvedSecondaryDisposition = true;
            }
            existingWork += hit.ExistingBarrierWorkJ;
            embeddedEnergy += hit.EmbeddedProjectileEnergyJ;
            faceWear = Math.Max(faceWear, hit.FaceWearFraction);
            backingWear = Math.Max(backingWear, hit.BackingWearFraction);
            readiness = Min(readiness, hit.Readiness);
            provenance += "; " + hit.LayerId + ": " + hit.Provenance;
        }

        var secondaries = new OutgoingBody[secondaryCount];
        var index = 0;
        for (var n = 0; n < impacts.Length; n++)
            for (var s = 0; s < impacts[n].OutgoingSecondaries.Length; s++)
                if (impacts[n].OutgoingSecondaries[s].Disposition ==
                    AggregateDisposition.LeavesAggregateSystem)
                    secondaries[index++] = impacts[n].OutgoingSecondaries[s];

        var aggregateCoupling = new Coupling
        {
            PostImpactConstruction = compositeCoupling.PostImpactConstruction,
            ImpactModeFactor = compositeCoupling.ImpactModeFactor,
            OutgoingInventoryComplete = compositeCoupling.OutgoingInventoryComplete &&
                                        !unresolvedSecondaryDisposition,
            Provenance = compositeCoupling.Provenance +
                         (unresolvedSecondaryDisposition
                             ? "; unresolved intermediate-secondary fate"
                             : "; aggregate secondary dispositions resolved"),
            Readiness = compositeCoupling.Readiness,
        };
        return Evaluate(new ResolvedImpact
        {
            LayerId = "aggregate[" + impacts.Length + "]",
            Outcome = impacts[impacts.Length - 1].Outcome,
            Incoming = impacts[0].Incoming,
            OutgoingPrimary = impacts[impacts.Length - 1].OutgoingPrimary,
            OutgoingSecondaries = secondaries,
            ExistingBarrierWorkJ = existingWork,
            EmbeddedProjectileEnergyJ = embeddedEnergy,
            FaceWearFraction = faceWear,
            BackingWearFraction = backingWear,
            Provenance = provenance,
            Readiness = readiness,
        }, aggregateCoupling);
    }

    /// <summary>
    /// Replace the mechanical seed in the transfer ledger with the final/dissipated
    /// terms from BabtModel. BodyNetWork is reported but not added: it is an internal
    /// plate-to-body transfer already represented by final energy and dissipation.
    /// </summary>
    public static ClosedBudget Close(Transfer transfer, BabtModel.Response response)
    {
        if (transfer == null || response == null || !transfer.IsValid || !response.IsValid)
            return new ClosedBudget { Reason = "A valid transfer and mechanical response are required." };

        var mechanicalDissipation = response.ContactDissipationJ + response.BodyDissipationJ +
                                    response.StructuralDissipationJ + response.PlasticDissipationJ +
                                    response.BrittleFractureDissipationJ;
        var outgoing = transfer.OutgoingPrimaryEnergyJ + transfer.OutgoingSecondaryEnergyJ;
        var error = outgoing + transfer.UnresolvedArmorProjectileDissipationJ +
                    response.FinalMechanicalEnergyJ + mechanicalDissipation - transfer.IncomingEnergyJ;
        var energySeedMatches = response.UsesResolvedTransferSeed &&
                                Math.Abs(response.IncidentNormalEnergyJ -
                                         transfer.MechanicalSeedEnergyJ) <=
                                NumericalRelativeTolerance *
                                Math.Max(1.0, transfer.IncomingEnergyJ);
        var momentumSeedMatches = Math.Abs(response.IncidentNormalMomentumNs -
                                           transfer.TransferredNormalImpulseNs) <=
                                  NumericalRelativeTolerance *
                                  Math.Max(1.0, Math.Abs(transfer.IncomingNormalMomentumNs));
        var matches = energySeedMatches && momentumSeedMatches;
        return new ClosedBudget
        {
            IsValid = matches,
            Reason = matches
                ? "Body net work is an internal transfer and is not added twice to the global energy sum."
                : "The response was not seeded by this resolved transfer's energy and momentum.",
            IncomingEnergyJ = transfer.IncomingEnergyJ,
            OutgoingEnergyJ = outgoing,
            UnresolvedArmorProjectileDissipationJ = transfer.UnresolvedArmorProjectileDissipationJ,
            FinalMechanicalEnergyJ = response.FinalMechanicalEnergyJ,
            MechanicalDissipationJ = mechanicalDissipation,
            BodyNetWorkJ = response.BodyNetWorkJ,
            EnergyBalanceErrorJ = error,
        };
    }

    private static string Validate(ResolvedImpact hit, Coupling coupling)
    {
        var impactError = ValidateImpact(hit);
        if (impactError != null) return impactError;
        if (!Enum.IsDefined(typeof(BabtModel.Readiness), coupling.Readiness))
            return "Coupling readiness must be defined.";
        if (String.IsNullOrWhiteSpace(coupling.Provenance))
            return "Coupling provenance is required.";
        if (!PositiveFinite(coupling.PostImpactConstruction.LocalEffectiveMassKg) ||
            !PositiveFinite(coupling.PostImpactConstruction.RigidEffectiveMassKg) ||
            !UnitInterval(coupling.ImpactModeFactor))
            return "Post-impact effective masses and impact mode factor are invalid.";
        return null!;
    }

    private static string ValidateImpact(ResolvedImpact hit)
    {
        if (!Enum.IsDefined(typeof(Outcome), hit.Outcome) ||
            !Enum.IsDefined(typeof(BabtModel.Readiness), hit.Readiness))
            return "Outcome and impact readiness values must be defined.";
        if (String.IsNullOrWhiteSpace(hit.LayerId) || String.IsNullOrWhiteSpace(hit.Provenance))
            return "Layer identity and impact provenance are required.";
        var stateError = ValidateState(hit.Incoming, false, "incoming");
        if (stateError != null) return stateError;
        if (hit.Incoming.NormalVelocityMps < 0)
            return "Incoming normal velocity must use the positive inward convention.";
        stateError = ValidateState(hit.OutgoingPrimary, hit.Outcome == Outcome.Stop, "outgoing primary");
        if (stateError != null) return stateError;
        if (hit.Outcome == Outcome.Stop &&
            (hit.OutgoingPrimary.MassKg != 0 || hit.OutgoingPrimary.SpeedMps != 0 ||
             hit.OutgoingPrimary.NormalVelocityMps != 0))
            return "A stopped primary projectile must have a zero outgoing state.";
        if (hit.Outcome == Outcome.Pierce && hit.OutgoingPrimary.MassKg <= 0)
            return "A piercing event requires a non-zero outgoing primary projectile.";
        if (hit.OutgoingSecondaries == null) return "Outgoing secondaries cannot be null.";
        for (var n = 0; n < hit.OutgoingSecondaries.Length; n++)
        {
            var body = hit.OutgoingSecondaries[n];
            if (body == null || String.IsNullOrWhiteSpace(body.Name) ||
                String.IsNullOrWhiteSpace(body.Provenance) || !PositiveFinite(body.MassKg) ||
                !NonNegativeFinite(body.SpeedMps) || !Finite(body.NormalVelocityMps) ||
                Math.Abs(body.NormalVelocityMps) > body.SpeedMps ||
                !Enum.IsDefined(typeof(AggregateDisposition), body.Disposition) ||
                !Enum.IsDefined(typeof(BabtModel.Readiness), body.Readiness))
                return "Outgoing body " + n + " has invalid state or provenance.";
        }
        if (!NonNegativeFinite(hit.ExistingBarrierWorkJ) ||
            !NonNegativeFinite(hit.EmbeddedProjectileEnergyJ) ||
            !UnitInterval(hit.FaceWearFraction) || !UnitInterval(hit.BackingWearFraction))
            return "Audit energies and wear fractions must be finite and non-negative/in range.";
        return null!;
    }

    private static string ValidateState(ProjectileState state, bool allowZero, string name)
    {
        if (state == null || String.IsNullOrWhiteSpace(state.Provenance) ||
            !Enum.IsDefined(typeof(BabtModel.Readiness), state.Readiness))
            return name + " state and provenance are required.";
        if (allowZero && state.MassKg == 0 && state.SpeedMps == 0 && state.NormalVelocityMps == 0)
            return null!;
        if (!PositiveFinite(state.MassKg) || !NonNegativeFinite(state.SpeedMps) ||
            !Finite(state.NormalVelocityMps) || Math.Abs(state.NormalVelocityMps) > state.SpeedMps)
            return name + " projectile state is invalid.";
        return null!;
    }

    private static bool SameState(ProjectileState a, ProjectileState b)
    {
        if (a == null || b == null) return false;
        return Near(a.MassKg, b.MassKg) && Near(a.SpeedMps, b.SpeedMps) &&
               Near(a.NormalVelocityMps, b.NormalVelocityMps);
    }

    private static bool Near(double a, double b)
    {
        return Math.Abs(a - b) <= NumericalRelativeTolerance *
               Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
    }

    private static double Energy(double massKg, double speedMps)
    {
        return 0.5 * massKg * speedMps * speedMps;
    }

    private static Transfer Failure(EvaluationStatus status, string reason)
    {
        return new Transfer
        {
            Status = status,
            Readiness = BabtModel.Readiness.Unsupported,
            Reason = reason,
        };
    }

    private static Chain ChainFailure(string reason)
    {
        return new Chain { Status = EvaluationStatus.InvalidChain, Reason = reason };
    }

    private static BabtModel.Readiness Min(BabtModel.Readiness a, BabtModel.Readiness b)
    {
        return (BabtModel.Readiness)Math.Min((int)a, (int)b);
    }

    private static bool UnitInterval(double value)
    {
        return Finite(value) && value >= 0 && value <= 1;
    }

    private static bool PositiveFinite(double value)
    {
        return value > 0 && Finite(value);
    }

    private static bool NonNegativeFinite(double value)
    {
        return value >= 0 && Finite(value);
    }

    private static bool Finite(double value)
    {
        return !Double.IsNaN(value) && !Double.IsInfinity(value);
    }
}
