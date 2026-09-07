using System;
using System.Collections.Generic;

namespace PLATE.Server.Services;

/// <summary>
/// A passive reduced-order model of mechanical load behind an armour panel. Its legacy
/// autonomous contact entry point is limited to non-perforating impacts; the transferred
/// entry point accepts a surviving post-impact construction and a ballistic energy/
/// momentum seed already resolved by <see cref="BabtTransferModel"/>. It deliberately
/// stops before injury: the result is motion, force, impulse and work, never HP.
///
/// The model has four translating coordinates: projectile, the locally participating
/// part of the panel, the part translating behind that local deformation, and an
/// effective body mass. Projectile/panel and panel/body contact are unilateral springs
/// with compression-only dashpots. Flexural force joins the two panel masses. This
/// separation is what permits a stiff steel plate to translate and ring while its rear
/// face has little local deflection.
///
/// This is not a finite-element impact calculation. The effective masses, contact laws,
/// laminate ABD result and body impedance must come from measurements or a separately
/// validated model. Brittle fracture, delamination, fibre rupture and perforation are
/// not predicted here; the transferred path requires their surviving structure from the
/// resolved terminal-ballistics/data bridge rather than silently treating it as pristine. Consequently a
/// mechanically complete result from this class is at most <see cref="Readiness.Provisional"/>.
/// The numerical reduction can be verified for passivity and convergence; that does not
/// empirically validate a construction.
///
/// This source is also compiled by the net471 client. Keep it on System.Math and the
/// common runtime surface; do not introduce records, Span or newer BCL helpers.
/// </summary>
public static class BabtModel
{
    private static readonly bool[] NoFailedLayers = new bool[0];

    public enum LayerForm
    {
        MetalPlate,
        RigidLaminate,
        SoftFabric,
        BrittleFace,
    }

    public enum LayerState
    {
        Intact,
        Failed,
    }

    public enum Readiness
    {
        Unsupported = 0,
        Provisional = 1,
        Calibrated = 2,
    }

    public enum BoundaryCondition
    {
        SimplySupported,
    }

    public enum EvaluationStatus
    {
        Complete,
        IncompleteTimeHorizon,
        Unsupported,
        InvalidInput,
        NumericalFailure,
    }

    public sealed class Construction
    {
        /// <summary>Effective flexural span in the panel's first direction, m.</summary>
        public double WidthM { get; set; }

        /// <summary>Effective flexural span in the panel's second direction, m.</summary>
        public double HeightM { get; set; }

        /// <summary>
        /// Mass reached by the local impact/contact wave in this reduction, kg. It is a
        /// measured or calibrated modal quantity, not automatically the whole panel.
        /// </summary>
        public double LocalEffectiveMassKg { get; set; }

        /// <summary>
        /// Mass translating behind the local flexural coordinate, kg. Together with
        /// LocalEffectiveMassKg it defines the reduced panel momentum.
        /// </summary>
        public double RigidEffectiveMassKg { get; set; }

        /// <summary>Measured/reduced viscous damping joining the two panel masses.</summary>
        public double FlexuralDampingNsPerM { get; set; }

        public BoundaryCondition Boundary { get; set; }
        public Layer[] Layers { get; set; } = new Layer[0];
        public string Provenance { get; set; } = "";
        public Readiness Readiness { get; set; }
    }

    public sealed class Layer
    {
        public string Name { get; set; } = "";
        public LayerForm Form { get; set; }
        public LayerState State { get; set; }
        public double ThicknessM { get; set; }
        public double DensityKgM3 { get; set; }

        /// <summary>
        /// Fraction of this layer which remains coherent in the retained response mode
        /// after the resolved hit, such as a perforation net-section factor. It scales
        /// A, D and membrane stiffness without changing physical mass.
        /// </summary>
        public double CoherentFraction { get; set; } = 1.0;

        /// <summary>
        /// Retained effective structural thickness fraction after resolved wear. The
        /// reduced constitutive laws use A proportional to f, D proportional to f^3,
        /// and an isolated metal section's yield moment proportional to f^2. Authored
        /// layer centres and physical mass remain unchanged. This is a structural wear
        /// estimate, not a claim that the same fraction of material disappeared.
        /// </summary>
        public double EffectiveThicknessFraction { get; set; } = 1.0;

        /// <summary>Isotropic effective Young modulus, Pa; required for metal and brittle faces.</summary>
        public double YoungModulusPa { get; set; }
        public double PoissonRatio { get; set; }

        /// <summary>
        /// Laminate extensional stiffness A, N/m. For a rigid orthotropic laminate this
        /// must already be reduced in the impact direction from its measured ABD matrix.
        /// </summary>
        public double ExtensionalStiffnessNPerM { get; set; }

        /// <summary>
        /// Laminate bending stiffness D, N m. For an isotropic layer it is derived as
        /// E h^3 / (12 (1 - nu^2)) and this override is ignored.
        /// </summary>
        public double FlexuralRigidityNm { get; set; }

        /// <summary>Metal yield strength, Pa, for an elastic-perfectly-plastic reduction.</summary>
        public double YieldStrengthPa { get; set; }

        /// <summary>
        /// Constitutive-domain strain limit. Rigid laminate, soft fabric and intact
        /// brittle layers require it; crossing it makes the result unsupported because
        /// their failure mechanisms are outside this model.
        /// </summary>
        public double FailureStrain { get; set; }

        /// <summary>
        /// Effective equibiaxial membrane stiffness of a soft package, N/m. It must be a
        /// package measurement, not the modulus of a bare fibre.
        /// </summary>
        public double MembraneStiffnessNPerM { get; set; }

        /// <summary>
        /// True when this layer remains bonded to the preceding layer for this impact.
        /// The state must be resolved outside this class; debonding is not predicted.
        /// </summary>
        public bool BondedToPrevious { get; set; }

        public string Provenance { get; set; } = "";
        public Readiness Readiness { get; set; }
    }

    public sealed class Impact
    {
        public double ProjectileMassKg { get; set; }
        public double NormalVelocityMps { get; set; }
        public double ProjectileContactStiffnessNPerM { get; set; }
        public double ProjectileContactDampingNsPerM { get; set; }
        public double ContactAreaM2 { get; set; }
        public double TimeStepS { get; set; }
        public double SimulationDurationS { get; set; }

        /// <summary>Caller-owned per-impact workload ceiling; exceeding it fails before allocation/integration.</summary>
        public int MaximumIntegrationSteps { get; set; }

        /// <summary>Required temporal resolution of the shortest linearised period.</summary>
        public double MinimumStepsPerPeriod { get; set; }

        /// <summary>Maximum accepted |energy ledger error| / incident energy.</summary>
        public double MaximumRelativeEnergyError { get; set; }
        public double SettledVelocityToleranceMps { get; set; }
        public double SettledForceToleranceN { get; set; }

        /// <summary>
        /// Optional empirical applicability envelope of the supplied contact law, m/s.
        /// Zero means that no lower/upper empirical bound was supplied; it does not make
        /// the mechanics calibrated.
        /// </summary>
        public double ApplicabilityMinNormalVelocityMps { get; set; }
        public double ApplicabilityMaxNormalVelocityMps { get; set; }
        public bool Perforates { get; set; }
        public string Provenance { get; set; } = "";
        public Readiness Readiness { get; set; }
    }

    /// <summary>
    /// Caller-owned integration controls for a mechanical seed supplied by an already
    /// resolved ballistic event. These are numerical controls, not contact calibration.
    /// </summary>
    public sealed class Numerics
    {
        /// <summary>
        /// Maximum permitted time step. Transferred response selects an equal or
        /// smaller step from a conservative stiffness/seed-energy frequency bound.
        /// </summary>
        public double TimeStepS { get; set; }
        public double SimulationDurationS { get; set; }
        public int MaximumIntegrationSteps { get; set; }
        public double MinimumStepsPerPeriod { get; set; }
        public double MaximumRelativeEnergyError { get; set; }
        public double SettledVelocityToleranceMps { get; set; }
        public double SettledForceToleranceN { get; set; }
        public string Provenance { get; set; } = "";
        public Readiness Readiness { get; set; }
    }

    public sealed class BodyProfile
    {
        public double EffectiveMassKg { get; set; }
        public double ContactStiffnessNPerM { get; set; }
        public double ContactDampingNsPerM { get; set; }
        public double FoundationStiffnessNPerM { get; set; }
        public double FoundationDampingNsPerM { get; set; }
        public double InitialGapM { get; set; }
        /// <summary>
        /// Prescribed effective body-contact area copied from BodyProfile. The reduced
        /// model does not dynamically solve a pressure footprint.
        /// </summary>
        public double EffectiveContactAreaM2 { get; set; }
        public string Provenance { get; set; } = "";
        public Readiness Readiness { get; set; }
    }

    public sealed class Response
    {
        public bool IsValid { get; set; }
        public EvaluationStatus Status { get; set; }
        public Readiness Readiness { get; set; }
        public string Reason { get; set; } = "";
        public string ProvenanceSummary { get; set; } = "";
        public int IntegrationSteps { get; set; }
        public double IntegrationTimeStepS { get; set; }
        public double SimulatedDurationS { get; set; }
        public bool UsesResolvedTransferSeed { get; set; }

        public double RearDynamicDeflectionMm { get; set; }
        public double RearResidualDeflectionMm { get; set; }
        public double RigidTranslationMaxMm { get; set; }
        public double BodyCompressionMaxMm { get; set; }
        public double BodyCompressionVelocityMaxMps { get; set; }

        /// <summary>Maximum compression of the prescribed panel/body contact law.</summary>
        public double ContactCompressionMaxMm { get; set; }
        public double ContactCompressionVelocityMaxMps { get; set; }
        public double PeakBodyForceN { get; set; }
        public double BodyImpulseNs { get; set; }

        /// <summary>
        /// Signed net work done by the panel/body interface on the body. Elastic energy
        /// returned from the body is subtracted, so this is suitable for a single injury
        /// mapping after the integration rather than accumulation per oscillation.
        /// </summary>
        public double BodyNetWorkJ { get; set; }

        /// <summary>Compatibility alias for BodyNetWorkJ.</summary>
        public double BodyWorkJ { get; set; }
        public double BodyRetainedMechanicalEnergyJ { get; set; }
        public bool BodyWorkConverged { get; set; }
        public double BodyWorkStableDurationS { get; set; }
        public double FreePlateRecoilVelocityMps { get; set; }
        public double EffectiveContactAreaM2 { get; set; }

        public double IncidentNormalMomentumNs { get; set; }
        public double IncidentNormalEnergyJ { get; set; }
        public double ProjectileFinalVelocityMps { get; set; }
        public double FinalSystemMomentumNs { get; set; }
        public double FinalMechanicalEnergyJ { get; set; }
        public double ContactDissipationJ { get; set; }

        /// <summary>Panel/body interface dashpot dissipation.</summary>
        public double InterfaceDissipationJ { get; set; }

        /// <summary>Body-foundation dashpot dissipation.</summary>
        public double BodyFoundationDissipationJ { get; set; }

        /// <summary>Compatibility total: interface plus body-foundation dissipation.</summary>
        public double BodyDissipationJ { get; set; }
        public double StructuralDissipationJ { get; set; }
        public double PlasticDissipationJ { get; set; }

        /// <summary>
        /// Elastic energy removed when a supplied intact brittle layer crosses its
        /// strain limit and is dropped from the surviving stiffness model. This is a
        /// closed reduced-order fracture reservoir, not a heat or fragment prediction.
        /// </summary>
        public double BrittleFractureDissipationJ { get; set; }

        public int ResolvedBrittleDropoutCount { get; set; }
        public double FoundationImpulseNs { get; set; }
        public double MomentumBalanceErrorNs { get; set; }
        public double EnergyBalanceErrorJ { get; set; }

        /// <summary>Diagnostic stiffness of the linear plate branch at zero deflection.</summary>
        public double LinearFlexuralStiffnessNPerM { get; set; }

        /// <summary>Diagnostic coefficient in F = K3 q^3 for soft packages.</summary>
        public double MembraneCubicStiffnessNPerM3 { get; set; }

        /// <summary>Absolute flexural excursion used for constitutive-domain checks.</summary>
        public double MaximumAbsoluteFlexuralDeflectionMm { get; set; }

        internal double MaximumAbsoluteFlexuralDeflectionM { get; set; }
    }

    private sealed class LinearComponent
    {
        public int LayerIndex;
        public string Name = "";
        public LayerForm Form;
        public double Stiffness;
        public double YieldForce = double.PositiveInfinity;
        public double PlasticOffset;
        public double FailureDeflection = double.PositiveInfinity;
    }

    private sealed class Mechanics
    {
        public readonly List<LinearComponent> Components = new List<LinearComponent>();
        public bool HasBrittle;
        public double CubicStiffness;
        public double SoftFailureStrain = double.PositiveInfinity;
        public double EquivalentRadius;
        public double LinearStiffness;
    }

    private struct State
    {
        public double ProjectileX;
        public double ProjectileV;
        public double LocalX;
        public double LocalV;
        public double RigidX;
        public double RigidV;
        public double BodyX;
        public double BodyV;
    }

    private struct Forces
    {
        public double ProjectileContact;
        public double BodyContact;
        public double Foundation;
        public double Internal;
        public double ProjectileDampingPower;
        public double BodyContactDampingPower;
        public double FoundationDampingPower;
        public double StructuralDampingPower;
        public double PlasticIncrement;
    }

    /// <summary>Evaluate a non-perforating impact using SI inputs.</summary>
    public static Response Evaluate(Construction construction, Impact impact, BodyProfile body)
    {
        if (construction == null || impact == null || body == null)
        {
            return Failure(EvaluationStatus.InvalidInput, "Construction, impact and body are required.");
        }

        var provenance = JoinProvenance(construction, impact, body);
        if (impact.Perforates)
        {
            return Failure(EvaluationStatus.Unsupported,
                "Perforation requires a terminal-ballistics and residual-projectile solution before BABT can be evaluated.",
                provenance);
        }

        if ((impact.ApplicabilityMinNormalVelocityMps > 0 &&
             impact.NormalVelocityMps < impact.ApplicabilityMinNormalVelocityMps) ||
            (impact.ApplicabilityMaxNormalVelocityMps > 0 &&
             impact.NormalVelocityMps > impact.ApplicabilityMaxNormalVelocityMps))
        {
            return Failure(EvaluationStatus.Unsupported,
                "Impact velocity is outside the supplied empirical applicability envelope of the contact law.",
                provenance);
        }

        // Readiness is checked before numerical fields so a resolver can deliberately
        // return an unsupported, partially populated profile without it being mislabeled
        // as malformed input or paying any per-hit integration cost.
        if (construction.Readiness == Readiness.Unsupported || impact.Readiness == Readiness.Unsupported ||
            body.Readiness == Readiness.Unsupported)
        {
            return Failure(EvaluationStatus.Unsupported,
                "Required construction, projectile-contact or body constitutive data are marked unsupported.",
                provenance);
        }

        if (construction.Layers != null)
        {
            for (var i = 0; i < construction.Layers.Length; i++)
            {
                var layer = construction.Layers[i];
                if (layer == null) continue;
                if (layer.Readiness == Readiness.Unsupported)
                {
                    return Failure(EvaluationStatus.Unsupported,
                        "Layer '" + layer.Name + "' has no supported constitutive data.", provenance);
                }

                if (layer.State == LayerState.Failed)
                {
                    return Failure(EvaluationStatus.Unsupported,
                        "Failed layers require fracture, rupture or interface mechanics that this reduced model does not contain.",
                        provenance);
                }

                if (layer.Form == LayerForm.SoftFabric && layer.BondedToPrevious)
                {
                    return Failure(EvaluationStatus.Unsupported,
                        "A soft package bonded to a preceding layer requires measured coupled ABD/interface behaviour.",
                        provenance);
                }
            }
        }

        var invalid = Validate(construction, impact, body);
        if (invalid != null)
        {
            return Failure(EvaluationStatus.InvalidInput, invalid, provenance);
        }

        Mechanics mechanics;
        string mechanicsError;
        if (!TryBuildMechanics(construction, out mechanics, out mechanicsError))
        {
            return Failure(EvaluationStatus.Unsupported, mechanicsError, provenance);
        }

        var response = Integrate(construction, impact, body, mechanics, provenance);
        if (!response.IsValid)
        {
            return response;
        }

        var maxQ = response.MaximumAbsoluteFlexuralDeflectionM;
        for (var i = 0; i < mechanics.Components.Count; i++)
        {
            var component = mechanics.Components[i];
            if (maxQ > component.FailureDeflection)
            {
                return Failure(EvaluationStatus.Unsupported,
                    "Layer '" + component.Name + "' exceeded its supplied constitutive strain limit; failure is not modelled.",
                    provenance);
            }
        }

        if (mechanics.CubicStiffness > 0)
        {
            var softStrain = 2.0 * maxQ * maxQ /
                             (mechanics.EquivalentRadius * mechanics.EquivalentRadius);
            if (softStrain > mechanics.SoftFailureStrain)
            {
                return Failure(EvaluationStatus.Unsupported,
                    "The soft package exceeded its supplied tensile strain limit; fibre rupture is not modelled.",
                    provenance);
            }
        }

        return response;
    }

    /// <summary>
    /// Evaluate plate/body motion from the coherent mechanical seed of an impact which
    /// the terminal-ballistics path has already resolved. No projectile contact or
    /// penetration work is solved again here.
    /// </summary>
    public static Response EvaluateTransferred(Construction construction,
        BabtTransferModel.Transfer transfer, BodyProfile body, Numerics numerics)
    {
        if (construction == null || transfer == null || body == null || numerics == null)
            return Failure(EvaluationStatus.InvalidInput,
                "Construction, resolved transfer, body and numerics are required.");

        var provenance = "construction: " + construction.Provenance +
                         "; resolved transfer: " + transfer.ProvenanceSummary +
                         "; body: " + body.Provenance + "; numerics: " + numerics.Provenance;
        if (!transfer.IsValid)
            return Failure(EvaluationStatus.InvalidInput,
                "The terminal-ballistics transfer is not a valid closed seed: " + transfer.Reason,
                provenance);
        if (construction.Readiness == Readiness.Unsupported || body.Readiness == Readiness.Unsupported ||
            numerics.Readiness == Readiness.Unsupported)
            return Failure(EvaluationStatus.Unsupported,
                "Required construction, body or numerical provenance is marked unsupported.", provenance);

        if (construction.Layers != null)
        {
            for (var n = 0; n < construction.Layers.Length; n++)
            {
                var layer = construction.Layers[n];
                if (layer == null) continue;
                if (layer.Readiness == Readiness.Unsupported)
                    return Failure(EvaluationStatus.Unsupported,
                        "Layer '" + layer.Name + "' has no supported constitutive data.", provenance);
                if (layer.State == LayerState.Failed)
                    return Failure(EvaluationStatus.Unsupported,
                        "A failed layer must be removed or replaced by a resolved surviving-structure estimate.",
                        provenance);
                if (layer.Form == LayerForm.SoftFabric && layer.BondedToPrevious)
                    return Failure(EvaluationStatus.Unsupported,
                        "A soft package bonded to a preceding layer requires measured coupled ABD/interface behaviour.",
                        provenance);
            }
        }

        var invalid = ValidateConstructionAndBody(construction, body);
        if (invalid == null) invalid = ValidateNumerics(numerics);
        if (invalid == null &&
            (!NonNegativeFinite(transfer.MechanicalSeedEnergyJ) ||
             !NonNegativeFinite(transfer.TransferredNormalImpulseNs) ||
             !Finite(transfer.TranslationVelocityMps) || !Finite(transfer.FlexuralVelocityMps)))
            invalid = "The resolved transfer contains non-finite or negative mechanical seed data.";
        if (invalid != null)
            return Failure(EvaluationStatus.InvalidInput, invalid, provenance);

        Mechanics mechanics;
        string mechanicsError;
        if (!TryBuildMechanics(construction, out mechanics, out mechanicsError))
            return Failure(EvaluationStatus.Unsupported, mechanicsError, provenance);

        var totalMass = construction.LocalEffectiveMassKg + construction.RigidEffectiveMassKg;
        var localV = transfer.TranslationVelocityMps +
                     construction.RigidEffectiveMassKg / totalMass * transfer.FlexuralVelocityMps;
        var rigidV = transfer.TranslationVelocityMps -
                     construction.LocalEffectiveMassKg / totalMass * transfer.FlexuralVelocityMps;
        var reconstructedSeed = 0.5 * construction.LocalEffectiveMassKg * localV * localV +
                                0.5 * construction.RigidEffectiveMassKg * rigidV * rigidV;
        var seedTolerance = 1e-9 * Math.Max(1.0, transfer.MechanicalSeedEnergyJ);
        if (Math.Abs(reconstructedSeed - transfer.MechanicalSeedEnergyJ) > seedTolerance)
            return Failure(EvaluationStatus.InvalidInput,
                "Transfer velocities do not reconstruct the declared coherent mechanical seed.", provenance);

        Mechanics finalMechanics;
        var response = IntegrateTransferred(construction, transfer, body, numerics, mechanics,
            provenance, localV, rigidV, out finalMechanics);
        if (!response.IsValid) return response;

        var maxQ = response.MaximumAbsoluteFlexuralDeflectionM;
        for (var n = 0; n < finalMechanics.Components.Count; n++)
        {
            var component = finalMechanics.Components[n];
            if (maxQ > component.FailureDeflection)
                return Failure(EvaluationStatus.Unsupported,
                    "Layer '" + component.Name +
                    "' exceeded its supplied constitutive strain limit; further failure is not modelled.",
                    provenance);
        }
        if (finalMechanics.CubicStiffness > 0)
        {
            var softStrain = 2.0 * maxQ * maxQ /
                             (finalMechanics.EquivalentRadius * finalMechanics.EquivalentRadius);
            if (softStrain > finalMechanics.SoftFailureStrain)
                return Failure(EvaluationStatus.Unsupported,
                    "The surviving soft package exceeded its tensile strain limit; further rupture is not modelled.",
                    provenance);
        }
        return response;
    }

    private static Construction WithoutFailedBrittle(Construction source, bool[] failed,
        string provenanceSuffix)
    {
        var layers = new List<Layer>();
        var previousRemoved = false;
        for (var n = 0; n < source.Layers.Length; n++)
        {
            if (failed[n])
            {
                previousRemoved = true;
                continue;
            }
            var original = source.Layers[n];
            layers.Add(new Layer
            {
                Name = original.Name,
                Form = original.Form,
                State = original.State,
                ThicknessM = original.ThicknessM,
                DensityKgM3 = original.DensityKgM3,
                CoherentFraction = original.CoherentFraction,
                EffectiveThicknessFraction = original.EffectiveThicknessFraction,
                YoungModulusPa = original.YoungModulusPa,
                PoissonRatio = original.PoissonRatio,
                ExtensionalStiffnessNPerM = original.ExtensionalStiffnessNPerM,
                FlexuralRigidityNm = original.FlexuralRigidityNm,
                YieldStrengthPa = original.YieldStrengthPa,
                FailureStrain = original.FailureStrain,
                MembraneStiffnessNPerM = original.MembraneStiffnessNPerM,
                BondedToPrevious = original.BondedToPrevious && !previousRemoved &&
                                   layers.Count > 0,
                Provenance = original.Provenance,
                Readiness = original.Readiness,
            });
            previousRemoved = false;
        }
        return new Construction
        {
            WidthM = source.WidthM,
            HeightM = source.HeightM,
            LocalEffectiveMassKg = source.LocalEffectiveMassKg,
            RigidEffectiveMassKg = source.RigidEffectiveMassKg,
            FlexuralDampingNsPerM = source.FlexuralDampingNsPerM,
            Boundary = source.Boundary,
            Layers = layers.ToArray(),
            Provenance = source.Provenance + provenanceSuffix,
            Readiness = source.Readiness,
        };
    }

    private static bool TryDropFailedBrittle(Construction construction,
        Mechanics mechanics, double q, out Construction surviving,
        out Mechanics survivingMechanics, out double removedEnergy,
        out int dropoutCount, out string error)
    {
        surviving = construction;
        survivingMechanics = mechanics;
        removedEnergy = 0.0;
        dropoutCount = 0;
        error = "";
        if (!mechanics.HasBrittle) return true;
        var failed = NoFailedLayers;
        for (var n = 0; n < mechanics.Components.Count; n++)
        {
            var component = mechanics.Components[n];
            if (component.Form != LayerForm.BrittleFace ||
                Math.Abs(q) + 1e-12 < component.FailureDeflection)
                continue;
            if (failed.Length == 0) failed = new bool[construction.Layers.Length];
            if (!failed[component.LayerIndex])
            {
                failed[component.LayerIndex] = true;
                dropoutCount++;
            }
        }
        if (dropoutCount == 0) return true;

        var before = FlexuralStoredPotential(q, mechanics);
        surviving = WithoutFailedBrittle(construction, failed,
            "; ideal-brittle dropout after supplied strain limit");
        var mechanicsError = "construction has no surviving layers";
        if (surviving.Layers.Length == 0 ||
            !TryBuildMechanics(surviving, out survivingMechanics, out mechanicsError))
        {
            error = "A brittle layer crossed its supplied strain limit but no supported " +
                    "surviving backing response remains: " + mechanicsError;
            return false;
        }
        CopyPlasticOffsets(mechanics, survivingMechanics);
        var after = FlexuralStoredPotential(q, survivingMechanics);
        var tolerance = 1e-10 * Math.Max(1.0, before);
        if (!Finite(before) || !Finite(after) || after > before + tolerance)
        {
            error = "Brittle stiffness dropout would add stored energy; the surviving bonded reduction is unsupported.";
            return false;
        }
        removedEnergy = Math.Max(0.0, before - after);
        return true;
    }

    private static double LimitStepAtBrittleThreshold(State state, Forces forces,
        Construction construction, BodyProfile body, Mechanics mechanics, double proposedStep)
    {
        if (!mechanics.HasBrittle) return proposedStep;
        var q0 = state.LocalX - state.RigidX;
        var relativeVelocity = state.LocalV - state.RigidV;
        var relativeAcceleration =
            (-forces.BodyContact - forces.Internal) / construction.LocalEffectiveMassKg -
            forces.Internal / construction.RigidEffectiveMassKg;
        var q1 = q0 + (relativeVelocity + relativeAcceleration * proposedStep) * proposedStep;
        var limited = proposedStep;
        for (var n = 0; n < mechanics.Components.Count; n++)
        {
            var component = mechanics.Components[n];
            if (component.Form != LayerForm.BrittleFace ||
                Math.Abs(q0) >= component.FailureDeflection ||
                Math.Abs(q1) < component.FailureDeflection)
                continue;

            var low = 0.0;
            var high = proposedStep;
            for (var iteration = 0; iteration < 50; iteration++)
            {
                var middle = 0.5 * (low + high);
                var q = q0 + (relativeVelocity + relativeAcceleration * middle) * middle;
                if (Math.Abs(q) >= component.FailureDeflection) high = middle;
                else low = middle;
            }
            limited = Math.Min(limited, high);
        }
        return limited;
    }

    private static void CopyPlasticOffsets(Mechanics source, Mechanics target)
    {
        var used = new bool[source.Components.Count];
        for (var t = 0; t < target.Components.Count; t++)
        {
            var next = target.Components[t];
            for (var s = 0; s < source.Components.Count; s++)
            {
                var prior = source.Components[s];
                if (used[s] || prior.Form != next.Form || prior.Name != next.Name) continue;
                next.PlasticOffset = prior.PlasticOffset;
                used[s] = true;
                break;
            }
        }
    }

    private static double FlexuralStoredPotential(double q, Mechanics mechanics)
    {
        var energy = 0.25 * mechanics.CubicStiffness * q * q * q * q;
        for (var n = 0; n < mechanics.Components.Count; n++)
        {
            var elastic = q - mechanics.Components[n].PlasticOffset;
            energy += 0.5 * mechanics.Components[n].Stiffness * elastic * elastic;
        }
        return energy;
    }

    private static string Validate(Construction c, Impact i, BodyProfile b)
    {
        var shared = ValidateConstructionAndBody(c, b);
        if (shared != null) return shared;
        if (!Enum.IsDefined(typeof(Readiness), i.Readiness))
            return "Impact readiness must be defined.";
        if (String.IsNullOrWhiteSpace(i.Provenance))
            return "Impact provenance is required.";
        if (!PositiveFinite(i.ProjectileMassKg) || !Finite(i.NormalVelocityMps) || i.NormalVelocityMps < 0)
            return "Projectile mass must be positive and normal velocity non-negative.";
        if (!NonNegativeFinite(i.ApplicabilityMinNormalVelocityMps) ||
            !NonNegativeFinite(i.ApplicabilityMaxNormalVelocityMps) ||
            (i.ApplicabilityMaxNormalVelocityMps > 0 &&
             i.ApplicabilityMaxNormalVelocityMps < i.ApplicabilityMinNormalVelocityMps))
            return "The optional contact-law velocity envelope is invalid.";
        if (!PositiveFinite(i.ProjectileContactStiffnessNPerM) ||
            !NonNegativeFinite(i.ProjectileContactDampingNsPerM) || !PositiveFinite(i.ContactAreaM2))
            return "Projectile contact stiffness, damping and contact area must be supplied.";
        if (!PositiveFinite(i.TimeStepS) || !PositiveFinite(i.SimulationDurationS))
            return "A finite positive integration step and duration are required.";
        if (i.MaximumIntegrationSteps <= 0 || !PositiveFinite(i.MinimumStepsPerPeriod) ||
            !PositiveFinite(i.MaximumRelativeEnergyError) ||
            !NonNegativeFinite(i.SettledVelocityToleranceMps) ||
            !NonNegativeFinite(i.SettledForceToleranceN))
            return "Explicit integration workload, temporal resolution and energy-error limits are required.";
        if (Math.Ceiling(i.SimulationDurationS / i.TimeStepS) > i.MaximumIntegrationSteps)
            return "The requested integration exceeds its caller-supplied workload ceiling.";
        return null!;
    }

    private static string ValidateConstructionAndBody(Construction c, BodyProfile b)
    {
        if (!PositiveFinite(c.WidthM) || !PositiveFinite(c.HeightM))
            return "Construction width and height must be finite and positive.";
        if (!PositiveFinite(c.LocalEffectiveMassKg) || !PositiveFinite(c.RigidEffectiveMassKg))
            return "Local and rigid effective panel masses must be finite, positive and externally resolved.";
        if (!NonNegativeFinite(c.FlexuralDampingNsPerM))
            return "Panel flexural damping must be finite and non-negative.";
        if (!Enum.IsDefined(typeof(BoundaryCondition), c.Boundary) ||
            c.Boundary != BoundaryCondition.SimplySupported)
            return "Only the explicitly selected simply-supported reduction is implemented.";
        if (!Enum.IsDefined(typeof(Readiness), c.Readiness) ||
            !Enum.IsDefined(typeof(Readiness), b.Readiness))
            return "Construction and body readiness values must be defined.";
        if (c.Layers == null || c.Layers.Length == 0)
            return "At least one construction layer is required.";
        if (String.IsNullOrWhiteSpace(c.Provenance) || String.IsNullOrWhiteSpace(b.Provenance))
            return "Construction and body provenance are required.";

        for (var n = 0; n < c.Layers.Length; n++)
        {
            var l = c.Layers[n];
            if (l == null) return "Construction layers cannot contain null entries.";
            if (String.IsNullOrWhiteSpace(l.Name) || String.IsNullOrWhiteSpace(l.Provenance))
                return "Every layer requires a name and provenance.";
            if (!Enum.IsDefined(typeof(LayerForm), l.Form) ||
                !Enum.IsDefined(typeof(LayerState), l.State) ||
                !Enum.IsDefined(typeof(Readiness), l.Readiness))
                return "Layer '" + l.Name + "' contains an undefined form, state or readiness value.";
            if (!PositiveFinite(l.ThicknessM) || !PositiveFinite(l.DensityKgM3) ||
                !Finite(l.CoherentFraction) || l.CoherentFraction <= 0 || l.CoherentFraction > 1 ||
                !Finite(l.EffectiveThicknessFraction) || l.EffectiveThicknessFraction <= 0 ||
                l.EffectiveThicknessFraction > 1)
                return "Layer '" + l.Name +
                       "' requires finite positive thickness/density and coherent/effective-thickness fractions in (0,1].";

            if (l.Form == LayerForm.MetalPlate || l.Form == LayerForm.BrittleFace)
            {
                if (!PositiveFinite(l.YoungModulusPa) || !Finite(l.PoissonRatio) ||
                    l.PoissonRatio <= -1.0 || l.PoissonRatio >= 0.5)
                    return "Layer '" + l.Name + "' requires valid isotropic E and Poisson ratio.";
                if (l.Form == LayerForm.MetalPlate &&
                    (!PositiveFinite(l.YieldStrengthPa) || !PositiveFinite(l.FailureStrain) ||
                     l.FailureStrain <= l.YieldStrengthPa / l.YoungModulusPa))
                    return "Metal layer '" + l.Name +
                           " requires yield strength and a larger constitutive failure-strain limit.";
                if (l.Form == LayerForm.BrittleFace && !PositiveFinite(l.FailureStrain))
                    return "Brittle layer '" + l.Name + "' requires an elastic-domain strain limit.";
            }
            else if (l.Form == LayerForm.RigidLaminate)
            {
                if (!PositiveFinite(l.ExtensionalStiffnessNPerM) || !PositiveFinite(l.FlexuralRigidityNm) ||
                    !PositiveFinite(l.FailureStrain))
                    return "Rigid laminate '" + l.Name + "' requires measured A, D and a strain limit.";
            }
            else if (l.Form == LayerForm.SoftFabric)
            {
                if (!PositiveFinite(l.MembraneStiffnessNPerM) || !PositiveFinite(l.FailureStrain))
                    return "Soft package '" + l.Name + "' requires measured membrane stiffness and failure strain.";
            }
        }

        if (!PositiveFinite(b.EffectiveMassKg) || !PositiveFinite(b.ContactStiffnessNPerM) ||
            !NonNegativeFinite(b.ContactDampingNsPerM) || !PositiveFinite(b.FoundationStiffnessNPerM) ||
            !NonNegativeFinite(b.FoundationDampingNsPerM) || !NonNegativeFinite(b.InitialGapM) ||
            !PositiveFinite(b.EffectiveContactAreaM2))
            return "Body effective mass, contact/foundation laws, gap and contact area must be supplied.";

        return null!;
    }

    private static string ValidateNumerics(Numerics n)
    {
        if (!Enum.IsDefined(typeof(Readiness), n.Readiness))
            return "Numerical-control readiness must be defined.";
        if (String.IsNullOrWhiteSpace(n.Provenance))
            return "Numerical-control provenance is required.";
        if (!PositiveFinite(n.TimeStepS) || !PositiveFinite(n.SimulationDurationS))
            return "A finite positive integration step and duration are required.";
        if (n.MaximumIntegrationSteps <= 0 || !PositiveFinite(n.MinimumStepsPerPeriod) ||
            !PositiveFinite(n.MaximumRelativeEnergyError) ||
            !NonNegativeFinite(n.SettledVelocityToleranceMps) ||
            !NonNegativeFinite(n.SettledForceToleranceN))
            return "Explicit integration workload, temporal resolution and energy-error limits are required.";
        if (Math.Ceiling(n.SimulationDurationS / n.TimeStepS) > n.MaximumIntegrationSteps)
            return "The requested integration exceeds its caller-supplied workload ceiling.";
        return null!;
    }

    private static bool TryBuildMechanics(Construction construction, out Mechanics mechanics,
        out string error)
    {
        mechanics = new Mechanics();
        error = "";
        var area = construction.WidthM * construction.HeightM;
        mechanics.EquivalentRadius = Math.Sqrt(area / Math.PI);
        var curvatureFactor = Math.PI * Math.PI *
                              Math.Max(1.0 / (construction.WidthM * construction.WidthM),
                                  1.0 / (construction.HeightM * construction.HeightM));
        var lambda = Math.PI * Math.PI / (construction.WidthM * construction.WidthM) +
                     Math.PI * Math.PI / (construction.HeightM * construction.HeightM);
        var modalFactor = area * lambda * lambda / 4.0;

        var first = 0;
        while (first < construction.Layers.Length)
        {
            var end = first + 1;
            while (end < construction.Layers.Length && construction.Layers[end].BondedToPrevious)
                end++;

            var hasSoft = false;
            for (var j = first; j < end; j++)
                hasSoft |= construction.Layers[j].Form == LayerForm.SoftFabric;
            if (hasSoft && end - first > 1)
            {
                error = "Soft and rigid layers need an interface/contact model; a bonded mixed group is unsupported.";
                return false;
            }

            if (hasSoft)
            {
                var soft = construction.Layers[first];
                // Equal-area circular membrane with w=q(1-r^2/R^2):
                // epsilon_r = .5(dw/dr)^2 and U = (2*pi/3) A q^4/R^2,
                // hence F = dU/dq = (8*pi/3) A q^3/R^2. This is a declared
                // one-mode reduction; the supplied package A carries weave/crimp.
                mechanics.CubicStiffness += 8.0 * Math.PI * soft.MembraneStiffnessNPerM *
                                              soft.CoherentFraction *
                                              soft.EffectiveThicknessFraction /
                                              (3.0 * mechanics.EquivalentRadius * mechanics.EquivalentRadius);
                mechanics.SoftFailureStrain = Math.Min(mechanics.SoftFailureStrain, soft.FailureStrain);
                first = end;
                continue;
            }

            var totalThickness = 0.0;
            var sumA = 0.0;
            var sumAz = 0.0;
            for (var j = first; j < end; j++)
            {
                var layer = construction.Layers[j];
                var a = ExtensionalStiffness(layer);
                var centre = totalThickness + layer.ThicknessM / 2.0;
                totalThickness += layer.ThicknessM;
                sumA += a;
                sumAz += a * centre;
            }

            if (!(sumA > 0) || !Finite(sumA))
            {
                error = "A bonded rigid group has no finite extensional stiffness.";
                return false;
            }

            var neutral = sumAz / sumA;
            var z = 0.0;
            for (var j = first; j < end; j++)
            {
                var layer = construction.Layers[j];
                var centre = z + layer.ThicknessM / 2.0;
                var a = ExtensionalStiffness(layer);
                // Classical laminate parallel-axis assembly: D = sum(D_i+A_i dz^2).
                var d = IntrinsicFlexuralRigidity(layer) + a * (centre - neutral) * (centre - neutral);
                // Simply-supported rectangular mode sin(pi*x/a)sin(pi*y/b):
                // K = D*a*b/4 * ((pi/a)^2+(pi/b)^2)^2.
                var stiffness = d * modalFactor;
                var component = new LinearComponent
                {
                    LayerIndex = j,
                    Name = layer.Name,
                    Form = layer.Form,
                    Stiffness = stiffness,
                };

                // Keep the authored layer centre within a bonded stack while using the
                // retained structural thickness for the two constitutive surfaces.
                // For an isolated plate this makes q_y proportional to 1/f; together
                // with D proportional to f^3, its yield force/moment scales as f^2.
                var effectiveHalfThickness = layer.ThicknessM *
                                             layer.EffectiveThicknessFraction / 2.0;
                var farthest = Math.Abs(centre - neutral) + effectiveHalfThickness;
                if (layer.Form == LayerForm.MetalPlate)
                {
                    var yieldStrain = layer.YieldStrengthPa / layer.YoungModulusPa;
                    var yieldQ = yieldStrain / (curvatureFactor * farthest);
                    component.YieldForce = stiffness * yieldQ;
                    if (layer.FailureStrain > 0)
                        component.FailureDeflection = layer.FailureStrain /
                                                     (curvatureFactor * farthest);
                }
                else
                {
                    component.FailureDeflection = layer.FailureStrain /
                                                 (curvatureFactor * farthest);
                }

                mechanics.Components.Add(component);
                mechanics.HasBrittle |= layer.Form == LayerForm.BrittleFace;
                mechanics.LinearStiffness += stiffness;
                z += layer.ThicknessM;
            }

            first = end;
        }

        if (!(mechanics.LinearStiffness > 0 || mechanics.CubicStiffness > 0))
        {
            error = "The construction has no supported flexural or membrane resistance.";
            return false;
        }

        return true;
    }

    private static double ExtensionalStiffness(Layer layer)
    {
        if (layer.Form == LayerForm.RigidLaminate)
            return layer.ExtensionalStiffnessNPerM * layer.CoherentFraction *
                   layer.EffectiveThicknessFraction;
        return layer.YoungModulusPa * layer.ThicknessM * layer.CoherentFraction *
               layer.EffectiveThicknessFraction /
               (1.0 - layer.PoissonRatio * layer.PoissonRatio);
    }

    private static double IntrinsicFlexuralRigidity(Layer layer)
    {
        var retainedCubed = layer.EffectiveThicknessFraction *
                            layer.EffectiveThicknessFraction *
                            layer.EffectiveThicknessFraction;
        if (layer.Form == LayerForm.RigidLaminate)
            return layer.FlexuralRigidityNm * layer.CoherentFraction * retainedCubed;
        return layer.YoungModulusPa * layer.ThicknessM * layer.ThicknessM * layer.ThicknessM /
               (12.0 * (1.0 - layer.PoissonRatio * layer.PoissonRatio)) *
               layer.CoherentFraction * retainedCubed;
    }

    private static Response Integrate(Construction c, Impact i, BodyProfile b,
        Mechanics mechanics, string provenance)
    {
        var response = new Response
        {
            IsValid = true,
            Status = EvaluationStatus.Complete,
            Readiness = MinimumReadiness(c, i, b),
            Reason = "Mechanically complete reduced-order result; empirical calibration is still required.",
            ProvenanceSummary = provenance,
            EffectiveContactAreaM2 = b.EffectiveContactAreaM2,
            IncidentNormalMomentumNs = i.ProjectileMassKg * i.NormalVelocityMps,
            IncidentNormalEnergyJ = 0.5 * i.ProjectileMassKg * i.NormalVelocityMps * i.NormalVelocityMps,
            LinearFlexuralStiffnessNPerM = mechanics.LinearStiffness,
            MembraneCubicStiffnessNPerM3 = mechanics.CubicStiffness,
        };

        var state = new State { ProjectileV = i.NormalVelocityMps };

        var linearOmega = MaximumLinearisedAngularFrequency(c, i, b, mechanics, 0.0);
        if (i.TimeStepS * linearOmega * i.MinimumStepsPerPeriod > 2.0 * Math.PI)
        {
            return Failure(EvaluationStatus.NumericalFailure,
                "The supplied time step does not resolve the shortest linearised period.", provenance);
        }
        if (i.TimeStepS * MaximumDampingRate(c, i, b) >= 2.0)
        {
            return Failure(EvaluationStatus.NumericalFailure,
                "The supplied time step is outside the explicit damping stability bound.", provenance);
        }
        var contactDissipation = 0.0;
        var interfaceDissipation = 0.0;
        var foundationDissipation = 0.0;
        var structuralDissipation = 0.0;
        var plasticDissipation = 0.0;
        var foundationImpulse = 0.0;
        var bodyImpulse = 0.0;
        var bodyWork = 0.0;
        var maxQ = 0.0;
        var maxRigid = 0.0;
        var maxBodyCompression = 0.0;
        var maxBodyCompressionVelocity = 0.0;
        var maxContactCompression = 0.0;
        var maxContactCompressionVelocity = 0.0;
        var maxAbsQ = 0.0;
        var maxBodyForce = 0.0;

        var requestedSteps = (int)Math.Ceiling(i.SimulationDurationS / i.TimeStepS);
        var elapsed = 0.0;
        for (var step = 0; step < requestedSteps; step++)
        {
            var dt = Math.Min(i.TimeStepS, i.SimulationDurationS - elapsed);
            var forces = CalculateForces(state, c, i, b, mechanics, true);

            var projectileA = -forces.ProjectileContact / i.ProjectileMassKg;
            var localA = (forces.ProjectileContact - forces.BodyContact - forces.Internal) /
                         c.LocalEffectiveMassKg;
            var rigidA = forces.Internal / c.RigidEffectiveMassKg;
            var bodyA = (forces.BodyContact + forces.Foundation) / b.EffectiveMassKg;

            var oldBodyV = state.BodyV;
            state.ProjectileV += projectileA * dt;
            state.LocalV += localA * dt;
            state.RigidV += rigidA * dt;
            state.BodyV += bodyA * dt;

            state.ProjectileX += state.ProjectileV * dt;
            state.LocalX += state.LocalV * dt;
            state.RigidX += state.RigidV * dt;
            state.BodyX += state.BodyV * dt;

            // Explicit integration advances q before its next force evaluation. Apply
            // the return map now so the last step cannot leave uncounted plastic work.
            var postStepPlastic = CalculateForces(state, c, i, b, mechanics, true);

            contactDissipation += forces.ProjectileDampingPower * dt;
            interfaceDissipation += forces.BodyContactDampingPower * dt;
            foundationDissipation += forces.FoundationDampingPower * dt;
            structuralDissipation += forces.StructuralDampingPower * dt;
            plasticDissipation += forces.PlasticIncrement + postStepPlastic.PlasticIncrement;
            foundationImpulse += forces.Foundation * dt;
            bodyImpulse += forces.BodyContact * dt;
            bodyWork += forces.BodyContact * (oldBodyV + state.BodyV) * 0.5 * dt;

            var q = state.LocalX - state.RigidX;
            var rigidTranslation = (c.LocalEffectiveMassKg * state.LocalX +
                                    c.RigidEffectiveMassKg * state.RigidX) /
                                   (c.LocalEffectiveMassKg + c.RigidEffectiveMassKg);
            var contactCompression = Math.Max(0.0, state.LocalX - state.BodyX - b.InitialGapM);
            var contactCompressionVelocity = Math.Max(0.0, state.LocalV - state.BodyV);
            var bodyCompression = Math.Max(0.0, state.BodyX);
            var bodyCompressionVelocity = Math.Max(0.0, state.BodyV);
            maxQ = Math.Max(maxQ, q);
            maxAbsQ = Math.Max(maxAbsQ, Math.Abs(q));
            maxRigid = Math.Max(maxRigid, rigidTranslation);
            maxBodyCompression = Math.Max(maxBodyCompression, bodyCompression);
            maxBodyCompressionVelocity = Math.Max(maxBodyCompressionVelocity, bodyCompressionVelocity);
            maxContactCompression = Math.Max(maxContactCompression, contactCompression);
            maxContactCompressionVelocity = Math.Max(maxContactCompressionVelocity,
                contactCompressionVelocity);
            maxBodyForce = Math.Max(maxBodyForce, forces.BodyContact);

            var currentOmega = MaximumLinearisedAngularFrequency(c, i, b, mechanics, q);
            if (dt * currentOmega * i.MinimumStepsPerPeriod > 2.0 * Math.PI)
            {
                return Failure(EvaluationStatus.NumericalFailure,
                    "Membrane tangent stiffness moved beyond the supplied time-step resolution.", provenance);
            }

            if (!FiniteState(state) || !Finite(contactDissipation) ||
                !Finite(interfaceDissipation) || !Finite(foundationDissipation) ||
                !Finite(structuralDissipation) || !Finite(plasticDissipation))
            {
                return Failure(EvaluationStatus.NumericalFailure,
                    "The reduced integration became non-finite; reduce the supplied time step or revise the profile.",
                    provenance);
            }

            elapsed += dt;
            response.IntegrationSteps = step + 1;
        }

        var finalForces = CalculateForces(state, c, i, b, mechanics, false);
        var finalEnergy = MechanicalEnergy(state, c, i, b, mechanics);
        var finalMomentum = i.ProjectileMassKg * state.ProjectileV +
                            c.LocalEffectiveMassKg * state.LocalV +
                            c.RigidEffectiveMassKg * state.RigidV +
                            b.EffectiveMassKg * state.BodyV;
        var initialMomentum = response.IncidentNormalMomentumNs;
        var initialEnergy = response.IncidentNormalEnergyJ;

        response.RearDynamicDeflectionMm = maxQ * 1000.0;
        response.RearResidualDeflectionMm = Math.Max(0.0, ResidualDeflection(mechanics)) * 1000.0;
        response.RigidTranslationMaxMm = maxRigid * 1000.0;
        response.BodyCompressionMaxMm = maxBodyCompression * 1000.0;
        response.BodyCompressionVelocityMaxMps = maxBodyCompressionVelocity;
        response.ContactCompressionMaxMm = maxContactCompression * 1000.0;
        response.ContactCompressionVelocityMaxMps = maxContactCompressionVelocity;
        response.PeakBodyForceN = Math.Max(maxBodyForce, finalForces.BodyContact);
        response.BodyImpulseNs = bodyImpulse;
        response.BodyNetWorkJ = bodyWork;
        response.BodyWorkJ = bodyWork;
        response.BodyRetainedMechanicalEnergyJ =
            0.5 * b.EffectiveMassKg * state.BodyV * state.BodyV +
            0.5 * b.FoundationStiffnessNPerM * state.BodyX * state.BodyX;
        response.ProjectileFinalVelocityMps = state.ProjectileV;
        response.FinalSystemMomentumNs = finalMomentum;
        response.FinalMechanicalEnergyJ = finalEnergy;
        response.ContactDissipationJ = contactDissipation;
        response.InterfaceDissipationJ = interfaceDissipation;
        response.BodyFoundationDissipationJ = foundationDissipation;
        response.BodyDissipationJ = interfaceDissipation + foundationDissipation;
        response.StructuralDissipationJ = structuralDissipation;
        response.PlasticDissipationJ = plasticDissipation;
        response.FoundationImpulseNs = foundationImpulse;
        response.MomentumBalanceErrorNs = finalMomentum - initialMomentum - foundationImpulse;
        response.EnergyBalanceErrorJ = finalEnergy + contactDissipation +
                                       interfaceDissipation + foundationDissipation +
                                       structuralDissipation + plasticDissipation - initialEnergy;

        response.MaximumAbsoluteFlexuralDeflectionM = maxAbsQ;
        response.MaximumAbsoluteFlexuralDeflectionMm = maxAbsQ * 1000.0;
        if (initialEnergy > 0 &&
            Math.Abs(response.EnergyBalanceErrorJ) / initialEnergy > i.MaximumRelativeEnergyError)
        {
            return Failure(EvaluationStatus.NumericalFailure,
                "The completed integration exceeded its caller-supplied energy-ledger error limit.", provenance);
        }
        var maximumSpeed = Math.Max(Math.Abs(state.LocalV),
            Math.Max(Math.Abs(state.RigidV), Math.Abs(state.BodyV)));
        if (maximumSpeed > i.SettledVelocityToleranceMps ||
            finalForces.ProjectileContact > i.SettledForceToleranceN ||
            finalForces.BodyContact > i.SettledForceToleranceN ||
            Math.Abs(finalForces.Internal) > i.SettledForceToleranceN)
        {
            response.Status = EvaluationStatus.IncompleteTimeHorizon;
            response.Reason = "The supplied simulation horizon ended before the reduced system met its supplied settling tolerances.";
        }
        return response;
    }

    private static Response IntegrateTransferred(Construction c,
        BabtTransferModel.Transfer transfer, BodyProfile b, Numerics n, Mechanics mechanics,
        string provenance, double localInitialVelocity, double rigidInitialVelocity,
        out Mechanics finalMechanics)
    {
        finalMechanics = mechanics;
        var brittleFractureDissipation = 0.0;
        var brittleDropoutCount = 0;
        var response = new Response
        {
            IsValid = true,
            Status = EvaluationStatus.Complete,
            Readiness = MinimumReadiness(c, transfer, b, n),
            Reason = "Mechanically complete response to a resolved ballistic transfer; empirical calibration is still required.",
            ProvenanceSummary = provenance,
            UsesResolvedTransferSeed = true,
            EffectiveContactAreaM2 = b.EffectiveContactAreaM2,
            IncidentNormalMomentumNs = transfer.TransferredNormalImpulseNs,
            IncidentNormalEnergyJ = transfer.MechanicalSeedEnergyJ,
            LinearFlexuralStiffnessNPerM = mechanics.LinearStiffness,
            MembraneCubicStiffnessNPerM3 = mechanics.CubicStiffness,
        };
        if (transfer.MechanicalSeedEnergyJ == 0)
        {
            response.IntegrationTimeStepS = n.TimeStepS;
            response.BodyWorkConverged = true;
            return response;
        }

        var state = new State
        {
            LocalV = localInitialVelocity,
            RigidV = rigidInitialVelocity,
        };
        var boundedOmega = MaximumTransferredAngularFrequencyBound(c, b, mechanics,
            transfer.MechanicalSeedEnergyJ);
        var integrationStep = Math.Min(n.TimeStepS,
            2.0 * Math.PI / (boundedOmega * n.MinimumStepsPerPeriod));
        var dampingRate = MaximumTransferredDampingRate(c, b);
        if (dampingRate > 0) integrationStep = Math.Min(integrationStep, 1.0 / dampingRate);
        if (!PositiveFinite(integrationStep) ||
            Math.Ceiling(n.SimulationDurationS / integrationStep) > n.MaximumIntegrationSteps)
            return Failure(EvaluationStatus.NumericalFailure,
                "The conservative transferred-response step exceeds the workload ceiling.", provenance);
        response.IntegrationTimeStepS = integrationStep;

        var interfaceDissipation = 0.0;
        var foundationDissipation = 0.0;
        var structuralDissipation = 0.0;
        var plasticDissipation = 0.0;
        var foundationImpulse = 0.0;
        var bodyImpulse = 0.0;
        var bodyWork = 0.0;
        var maxQ = 0.0;
        var maxRigid = 0.0;
        var maxBodyCompression = 0.0;
        var maxBodyCompressionVelocity = 0.0;
        var maxContactCompression = 0.0;
        var maxContactCompressionVelocity = 0.0;
        var maxAbsQ = 0.0;
        var maxBodyForce = 0.0;
        var lastBodyContactTime = 0.0;

        var elapsed = 0.0;
        for (var step = 0;
             step < n.MaximumIntegrationSteps && elapsed < n.SimulationDurationS;
             step++)
        {
            var dt = Math.Min(integrationStep, n.SimulationDurationS - elapsed);
            var trialForces = CalculatePlateBodyForces(state, c, b, mechanics, false);
            dt = LimitStepAtBrittleThreshold(state, trialForces, c, b, mechanics, dt);
            var forces = CalculatePlateBodyForces(state, c, b, mechanics, true);
            var localA = (-forces.BodyContact - forces.Internal) / c.LocalEffectiveMassKg;
            var rigidA = forces.Internal / c.RigidEffectiveMassKg;
            var bodyA = (forces.BodyContact + forces.Foundation) / b.EffectiveMassKg;

            var oldBodyV = state.BodyV;
            state.LocalV += localA * dt;
            state.RigidV += rigidA * dt;
            state.BodyV += bodyA * dt;
            state.LocalX += state.LocalV * dt;
            state.RigidX += state.RigidV * dt;
            state.BodyX += state.BodyV * dt;

            var postStepPlastic = CalculatePlateBodyForces(state, c, b, mechanics, true);
            interfaceDissipation += forces.BodyContactDampingPower * dt;
            foundationDissipation += forces.FoundationDampingPower * dt;
            structuralDissipation += forces.StructuralDampingPower * dt;
            plasticDissipation += forces.PlasticIncrement + postStepPlastic.PlasticIncrement;
            foundationImpulse += forces.Foundation * dt;
            bodyImpulse += forces.BodyContact * dt;
            bodyWork += forces.BodyContact * (oldBodyV + state.BodyV) * 0.5 * dt;
            if (forces.BodyContact > n.SettledForceToleranceN)
                lastBodyContactTime = elapsed + dt;

            var q = state.LocalX - state.RigidX;
            Construction survivingConstruction;
            Mechanics survivingMechanics;
            double fractureIncrement;
            int dropoutIncrement;
            string dropoutError;
            if (!TryDropFailedBrittle(c, mechanics, q, out survivingConstruction,
                    out survivingMechanics, out fractureIncrement, out dropoutIncrement,
                    out dropoutError))
                return Failure(EvaluationStatus.Unsupported, dropoutError, provenance);
            if (dropoutIncrement > 0)
            {
                c = survivingConstruction;
                mechanics = survivingMechanics;
                finalMechanics = mechanics;
                brittleFractureDissipation += fractureIncrement;
                brittleDropoutCount += dropoutIncrement;
                response.LinearFlexuralStiffnessNPerM = mechanics.LinearStiffness;
                response.MembraneCubicStiffnessNPerM3 = mechanics.CubicStiffness;
                response.ProvenanceSummary +=
                    "; ideal-brittle stiffness dropout at the supplied strain limit; fragment mass retained";
            }
            var rigidTranslation = (c.LocalEffectiveMassKg * state.LocalX +
                                    c.RigidEffectiveMassKg * state.RigidX) /
                                   (c.LocalEffectiveMassKg + c.RigidEffectiveMassKg);
            var contactCompression = Math.Max(0.0, state.LocalX - state.BodyX - b.InitialGapM);
            var contactCompressionVelocity = Math.Max(0.0, state.LocalV - state.BodyV);
            maxQ = Math.Max(maxQ, q);
            maxAbsQ = Math.Max(maxAbsQ, Math.Abs(q));
            maxRigid = Math.Max(maxRigid, rigidTranslation);
            maxBodyCompression = Math.Max(maxBodyCompression, Math.Max(0.0, state.BodyX));
            maxBodyCompressionVelocity = Math.Max(maxBodyCompressionVelocity,
                Math.Max(0.0, state.BodyV));
            maxContactCompression = Math.Max(maxContactCompression, contactCompression);
            maxContactCompressionVelocity = Math.Max(maxContactCompressionVelocity,
                contactCompressionVelocity);
            maxBodyForce = Math.Max(maxBodyForce, forces.BodyContact);

            var currentOmega = MaximumTransferredAngularFrequency(c, b, mechanics, q);
            if (dt * currentOmega * n.MinimumStepsPerPeriod >
                2.0 * Math.PI * (1.0 + 1e-12))
                return Failure(EvaluationStatus.NumericalFailure,
                    "Membrane tangent stiffness moved beyond the transferred-response time-step resolution.",
                    provenance);
            if (!FiniteState(state) || !Finite(interfaceDissipation) ||
                !Finite(foundationDissipation) || !Finite(structuralDissipation) ||
                !Finite(plasticDissipation) || !Finite(bodyWork))
                return Failure(EvaluationStatus.NumericalFailure,
                    "The transferred response became non-finite; reduce the time step or revise the profile.",
                    provenance);

            elapsed += dt;
            response.IntegrationSteps = step + 1;
        }

        if (elapsed < n.SimulationDurationS)
            return Failure(EvaluationStatus.NumericalFailure,
                "Brittle event subdivision exhausted the caller-supplied workload ceiling.",
                provenance);

        var finalForces = CalculatePlateBodyForces(state, c, b, mechanics, false);
        var finalEnergy = MechanicalEnergyTransferred(state, c, b, mechanics);
        var finalMomentum = c.LocalEffectiveMassKg * state.LocalV +
                            c.RigidEffectiveMassKg * state.RigidV +
                            b.EffectiveMassKg * state.BodyV;
        response.RearDynamicDeflectionMm = maxQ * 1000.0;
        response.RearResidualDeflectionMm = Math.Max(0.0, ResidualDeflection(mechanics)) * 1000.0;
        response.RigidTranslationMaxMm = maxRigid * 1000.0;
        response.BodyCompressionMaxMm = maxBodyCompression * 1000.0;
        response.BodyCompressionVelocityMaxMps = maxBodyCompressionVelocity;
        response.ContactCompressionMaxMm = maxContactCompression * 1000.0;
        response.ContactCompressionVelocityMaxMps = maxContactCompressionVelocity;
        response.PeakBodyForceN = Math.Max(maxBodyForce, finalForces.BodyContact);
        response.BodyImpulseNs = bodyImpulse;
        response.BodyNetWorkJ = bodyWork;
        response.BodyWorkJ = bodyWork;
        response.BodyRetainedMechanicalEnergyJ =
            0.5 * b.EffectiveMassKg * state.BodyV * state.BodyV +
            0.5 * b.FoundationStiffnessNPerM * state.BodyX * state.BodyX;
        response.BodyWorkStableDurationS = Math.Max(0.0, elapsed - lastBodyContactTime);
        response.FreePlateRecoilVelocityMps =
            (c.LocalEffectiveMassKg * state.LocalV +
             c.RigidEffectiveMassKg * state.RigidV) /
            (c.LocalEffectiveMassKg + c.RigidEffectiveMassKg);
        response.FinalSystemMomentumNs = finalMomentum;
        response.FinalMechanicalEnergyJ = finalEnergy;
        response.InterfaceDissipationJ = interfaceDissipation;
        response.BodyFoundationDissipationJ = foundationDissipation;
        response.BodyDissipationJ = interfaceDissipation + foundationDissipation;
        response.StructuralDissipationJ = structuralDissipation;
        response.PlasticDissipationJ = plasticDissipation;
        response.BrittleFractureDissipationJ = brittleFractureDissipation;
        response.ResolvedBrittleDropoutCount = brittleDropoutCount;
        response.FoundationImpulseNs = foundationImpulse;
        response.MomentumBalanceErrorNs = finalMomentum - transfer.TransferredNormalImpulseNs -
                                          foundationImpulse;
        response.EnergyBalanceErrorJ = finalEnergy + interfaceDissipation +
                                       foundationDissipation + structuralDissipation +
                                       plasticDissipation + brittleFractureDissipation -
                                       transfer.MechanicalSeedEnergyJ;
        response.MaximumAbsoluteFlexuralDeflectionM = maxAbsQ;
        response.MaximumAbsoluteFlexuralDeflectionMm = maxAbsQ * 1000.0;
        response.SimulatedDurationS = elapsed;

        if (Math.Abs(response.EnergyBalanceErrorJ) /
            Math.Max(1e-12, transfer.MechanicalSeedEnergyJ) > n.MaximumRelativeEnergyError)
            return Failure(EvaluationStatus.NumericalFailure,
                "The transferred integration exceeded its caller-supplied energy-ledger error limit " +
                "(error=" + response.EnergyBalanceErrorJ.ToString("G6") + " J, seed=" +
                transfer.MechanicalSeedEnergyJ.ToString("G6") + " J).",
                provenance);
        var flexuralTangent = mechanics.LinearStiffness +
                              3.0 * mechanics.CubicStiffness * maxAbsQ * maxAbsQ;
        var flexuralOmega = Math.Sqrt(flexuralTangent *
            (1.0 / c.LocalEffectiveMassKg + 1.0 / c.RigidEffectiveMassKg));
        // A free panel need not settle in the laboratory-frame sense: its centre of
        // mass may recoil indefinitely. Require separation over four complete retained
        // flexural cycles while the panel is receding from the body. The doubled-horizon
        // convergence tests bound the remaining change in net body work.
        var noContactGuard = flexuralOmega > 0 ? 8.0 * Math.PI / flexuralOmega : elapsed;
        var plateReceding = response.FreePlateRecoilVelocityMps - state.BodyV <=
                            n.SettledVelocityToleranceMps;
        if (finalForces.BodyContact > n.SettledForceToleranceN || !plateReceding ||
            response.BodyWorkStableDurationS < noContactGuard)
        {
            response.Status = EvaluationStatus.IncompleteTimeHorizon;
            response.Reason = "The horizon ended before panel/body work was stable for four flexural periods with the free panel receding.";
        }
        else
        {
            response.BodyWorkConverged = true;
            response.Reason = brittleDropoutCount > 0
                ? "Panel/body work is stable after ideal-brittle face dropout; fracture energy and free plate recoil remain in the closed ledger."
                : "Panel/body work is stable after separation; free plate recoil remains in the closed mechanical-energy ledger.";
        }
        return response;
    }

    private static double MaximumLinearisedAngularFrequency(Construction c, Impact i,
        BodyProfile b, Mechanics mechanics, double q)
    {
        var tangent = mechanics.LinearStiffness + 3.0 * mechanics.CubicStiffness * q * q;
        // For a spring between two masses, the relative mode contributes
        // k(1/m1 + 1/m2). The sum is a conservative bound for this spring network.
        var projectile = i.ProjectileContactStiffnessNPerM *
                         (1.0 / i.ProjectileMassKg + 1.0 / c.LocalEffectiveMassKg);
        var flexure = tangent *
                      (1.0 / c.LocalEffectiveMassKg + 1.0 / c.RigidEffectiveMassKg);
        var bodyContact = b.ContactStiffnessNPerM *
                          (1.0 / c.LocalEffectiveMassKg + 1.0 / b.EffectiveMassKg);
        var foundation = b.FoundationStiffnessNPerM / b.EffectiveMassKg;
        return Math.Sqrt(projectile + flexure + bodyContact + foundation);
    }

    private static double MaximumDampingRate(Construction c, Impact i, BodyProfile b)
    {
        var projectile = i.ProjectileContactDampingNsPerM *
                         (1.0 / i.ProjectileMassKg + 1.0 / c.LocalEffectiveMassKg);
        var flexure = c.FlexuralDampingNsPerM *
                      (1.0 / c.LocalEffectiveMassKg + 1.0 / c.RigidEffectiveMassKg);
        var bodyContact = b.ContactDampingNsPerM *
                          (1.0 / c.LocalEffectiveMassKg + 1.0 / b.EffectiveMassKg);
        var foundation = b.FoundationDampingNsPerM / b.EffectiveMassKg;
        return projectile + flexure + bodyContact + foundation;
    }

    private static double MaximumTransferredAngularFrequency(Construction c, BodyProfile b,
        Mechanics mechanics, double q)
    {
        var tangent = mechanics.LinearStiffness + 3.0 * mechanics.CubicStiffness * q * q;
        var flexure = tangent *
                      (1.0 / c.LocalEffectiveMassKg + 1.0 / c.RigidEffectiveMassKg);
        var bodyContact = b.ContactStiffnessNPerM *
                          (1.0 / c.LocalEffectiveMassKg + 1.0 / b.EffectiveMassKg);
        var foundation = b.FoundationStiffnessNPerM / b.EffectiveMassKg;
        return Math.Sqrt(flexure + bodyContact + foundation);
    }

    private static double MaximumTransferredAngularFrequencyBound(Construction c,
        BodyProfile b, Mechanics mechanics, double seedEnergyJ)
    {
        // U_membrane=K3*q^4/4 <= E gives q^2 <= 2*sqrt(E/K3).
        // Therefore dF/dq=Klinear+3*K3*q^2 is bounded by
        // Klinear+6*sqrt(K3*E). Other springs are already linear.
        var tangentBound = mechanics.LinearStiffness;
        if (mechanics.CubicStiffness > 0 && seedEnergyJ > 0)
            tangentBound += 6.0 * Math.Sqrt(mechanics.CubicStiffness * seedEnergyJ);
        var flexure = tangentBound *
                      (1.0 / c.LocalEffectiveMassKg + 1.0 / c.RigidEffectiveMassKg);
        var bodyContact = b.ContactStiffnessNPerM *
                          (1.0 / c.LocalEffectiveMassKg + 1.0 / b.EffectiveMassKg);
        var foundation = b.FoundationStiffnessNPerM / b.EffectiveMassKg;
        return Math.Sqrt(flexure + bodyContact + foundation);
    }

    private static double MaximumTransferredDampingRate(Construction c, BodyProfile b)
    {
        var flexure = c.FlexuralDampingNsPerM *
                      (1.0 / c.LocalEffectiveMassKg + 1.0 / c.RigidEffectiveMassKg);
        var bodyContact = b.ContactDampingNsPerM *
                          (1.0 / c.LocalEffectiveMassKg + 1.0 / b.EffectiveMassKg);
        var foundation = b.FoundationDampingNsPerM / b.EffectiveMassKg;
        return flexure + bodyContact + foundation;
    }

    private static Forces CalculateForces(State s, Construction c, Impact i, BodyProfile b,
        Mechanics mechanics, bool updatePlastic)
    {
        var forces = CalculatePlateBodyForces(s, c, b, mechanics, updatePlastic);
        var projectileCompression = s.ProjectileX - s.LocalX;
        var projectileClosing = s.ProjectileV - s.LocalV;
        if (projectileCompression > 0)
        {
            var closing = Math.Max(0.0, projectileClosing);
            forces.ProjectileContact = i.ProjectileContactStiffnessNPerM * projectileCompression +
                                       i.ProjectileContactDampingNsPerM * closing;
            forces.ProjectileDampingPower = i.ProjectileContactDampingNsPerM * closing * closing;
        }

        return forces;
    }

    private static Forces CalculatePlateBodyForces(State s, Construction c, BodyProfile b,
        Mechanics mechanics, bool updatePlastic)
    {
        var forces = new Forces();
        var bodyCompression = s.LocalX - s.BodyX - b.InitialGapM;
        var bodyClosing = s.LocalV - s.BodyV;
        if (bodyCompression > 0)
        {
            var closing = Math.Max(0.0, bodyClosing);
            forces.BodyContact = b.ContactStiffnessNPerM * bodyCompression +
                                 b.ContactDampingNsPerM * closing;
            forces.BodyContactDampingPower = b.ContactDampingNsPerM * closing * closing;
        }

        forces.Foundation = -b.FoundationStiffnessNPerM * s.BodyX -
                            b.FoundationDampingNsPerM * s.BodyV;
        forces.FoundationDampingPower = b.FoundationDampingNsPerM * s.BodyV * s.BodyV;

        var q = s.LocalX - s.RigidX;
        for (var n = 0; n < mechanics.Components.Count; n++)
        {
            var component = mechanics.Components[n];
            var trial = component.Stiffness * (q - component.PlasticOffset);
            if (Math.Abs(trial) > component.YieldForce)
            {
                var sign = trial < 0 ? -1.0 : 1.0;
                var nextOffset = q - sign * component.YieldForce / component.Stiffness;
                if (updatePlastic)
                {
                    forces.PlasticIncrement += component.YieldForce *
                                               Math.Abs(nextOffset - component.PlasticOffset);
                    component.PlasticOffset = nextOffset;
                }
                trial = sign * component.YieldForce;
            }
            forces.Internal += trial;
        }
        forces.Internal += mechanics.CubicStiffness * q * q * q;
        var flexuralVelocity = s.LocalV - s.RigidV;
        forces.Internal += c.FlexuralDampingNsPerM * flexuralVelocity;
        forces.StructuralDampingPower = c.FlexuralDampingNsPerM *
                                        flexuralVelocity * flexuralVelocity;
        return forces;
    }

    private static double MechanicalEnergy(State s, Construction c, Impact i, BodyProfile b,
        Mechanics mechanics)
    {
        var energy = 0.5 * i.ProjectileMassKg * s.ProjectileV * s.ProjectileV +
                     0.5 * c.LocalEffectiveMassKg * s.LocalV * s.LocalV +
                     0.5 * c.RigidEffectiveMassKg * s.RigidV * s.RigidV +
                     0.5 * b.EffectiveMassKg * s.BodyV * s.BodyV;
        return energy + StoredPotential(s, i, b, mechanics);
    }

    private static double MechanicalEnergyTransferred(State s, Construction c, BodyProfile b,
        Mechanics mechanics)
    {
        var energy = 0.5 * c.LocalEffectiveMassKg * s.LocalV * s.LocalV +
                     0.5 * c.RigidEffectiveMassKg * s.RigidV * s.RigidV +
                     0.5 * b.EffectiveMassKg * s.BodyV * s.BodyV;
        var bodyCompression = Math.Max(0.0, s.LocalX - s.BodyX - b.InitialGapM);
        var q = s.LocalX - s.RigidX;
        energy += 0.5 * b.ContactStiffnessNPerM * bodyCompression * bodyCompression;
        energy += 0.5 * b.FoundationStiffnessNPerM * s.BodyX * s.BodyX;
        for (var component = 0; component < mechanics.Components.Count; component++)
        {
            var elastic = q - mechanics.Components[component].PlasticOffset;
            energy += 0.5 * mechanics.Components[component].Stiffness * elastic * elastic;
        }
        energy += 0.25 * mechanics.CubicStiffness * q * q * q * q;
        return energy;
    }

    private static double StoredPotential(State s, Impact i, BodyProfile b, Mechanics mechanics)
    {
        var energy = 0.0;
        var projectileCompression = Math.Max(0.0, s.ProjectileX - s.LocalX);
        var bodyCompression = Math.Max(0.0, s.LocalX - s.BodyX - b.InitialGapM);
        var q = s.LocalX - s.RigidX;
        energy += 0.5 * i.ProjectileContactStiffnessNPerM * projectileCompression * projectileCompression;
        energy += 0.5 * b.ContactStiffnessNPerM * bodyCompression * bodyCompression;
        energy += 0.5 * b.FoundationStiffnessNPerM * s.BodyX * s.BodyX;
        for (var n = 0; n < mechanics.Components.Count; n++)
        {
            var elastic = q - mechanics.Components[n].PlasticOffset;
            energy += 0.5 * mechanics.Components[n].Stiffness * elastic * elastic;
        }
        energy += 0.25 * mechanics.CubicStiffness * q * q * q * q;
        return energy;
    }

    private static double ResidualDeflection(Mechanics mechanics)
    {
        var low = 0.0;
        var high = 0.0;
        for (var n = 0; n < mechanics.Components.Count; n++)
        {
            low = Math.Min(low, mechanics.Components[n].PlasticOffset);
            high = Math.Max(high, mechanics.Components[n].PlasticOffset);
        }
        if (low == high) return low;

        for (var iteration = 0; iteration < 80; iteration++)
        {
            var middle = 0.5 * (low + high);
            var force = mechanics.CubicStiffness * middle * middle * middle;
            for (var n = 0; n < mechanics.Components.Count; n++)
                force += mechanics.Components[n].Stiffness *
                         (middle - mechanics.Components[n].PlasticOffset);
            if (force > 0) high = middle;
            else low = middle;
        }
        return 0.5 * (low + high);
    }

    private static Readiness MinimumReadiness(Construction c, Impact i, BodyProfile b)
    {
        var result = Readiness.Provisional;
        result = Min(result, c.Readiness);
        result = Min(result, i.Readiness);
        result = Min(result, b.Readiness);
        for (var n = 0; n < c.Layers.Length; n++) result = Min(result, c.Layers[n].Readiness);
        return result;
    }

    private static Readiness MinimumReadiness(Construction c,
        BabtTransferModel.Transfer transfer, BodyProfile b, Numerics n)
    {
        var result = Readiness.Provisional;
        result = Min(result, c.Readiness);
        result = Min(result, transfer.Readiness);
        result = Min(result, b.Readiness);
        result = Min(result, n.Readiness);
        for (var layer = 0; layer < c.Layers.Length; layer++)
            result = Min(result, c.Layers[layer].Readiness);
        return result;
    }

    private static Readiness Min(Readiness a, Readiness b)
    {
        return (Readiness)Math.Min((int)a, (int)b);
    }

    private static string JoinProvenance(Construction c, Impact i, BodyProfile b)
    {
        var text = "construction: " + c.Provenance + "; impact/contact: " + i.Provenance +
                   "; body: " + b.Provenance;
        if (c.Layers != null)
        {
            for (var n = 0; n < c.Layers.Length; n++)
            {
                var layer = c.Layers[n];
                if (layer != null)
                    text += "; layer " + layer.Name + ": " + layer.Provenance;
            }
        }
        return text;
    }

    private static Response Failure(EvaluationStatus status, string reason, string provenance = "")
    {
        return new Response
        {
            IsValid = false,
            Status = status,
            Readiness = Readiness.Unsupported,
            Reason = reason,
            ProvenanceSummary = provenance,
        };
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

    private static bool FiniteState(State state)
    {
        return Finite(state.ProjectileX) && Finite(state.ProjectileV) &&
               Finite(state.LocalX) && Finite(state.LocalV) &&
               Finite(state.RigidX) && Finite(state.RigidV) &&
               Finite(state.BodyX) && Finite(state.BodyV);
    }
}
