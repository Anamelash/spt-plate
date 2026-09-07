using System;
using System.Collections.Generic;
using PLATE.Server.Services;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Converts the versioned armour-data DTO into the pure BABT solver input. It is
    /// intentionally strict: protection class never supplies stiffness, while the
    /// established thickness/density/backing chain is reused with separately sourced
    /// material mechanics, form and span. The reduced model has no hit
    /// coordinates, so a resolved construction is a centred effective-mode response;
    /// it cannot predict the spatial change in BFD near an edge or off centre.
    /// </summary>
    public static class BabtConstructionResolver
    {
        public sealed class ResolvedImpactDamage
        {
            public bool Perforated { get; set; }
            public double HoleAreaM2 { get; set; }
            public double FaceCoherentFraction { get; set; }
            public double BackingCoherentFraction { get; set; }
            public bool CoherentFractionsKnown { get; set; }

            /// <summary>
            /// True when this component had an actual local armour decision or an
            /// equivalent exact local-state lookup. Global durability alone is false.
            /// </summary>
            public bool LocalStateResolved { get; set; }

            /// <summary>
            /// Existing ArmorWear retained effective-thickness state. This affects
            /// structural A/D/yield laws but never subtracts physical mass.
            /// </summary>
            public double FaceRetainedThicknessFraction { get; set; } = 1.0;
            public double BackingRetainedThicknessFraction { get; set; } = 1.0;
            public bool BrittleFaceFailed { get; set; }
            public bool OutgoingInventoryComplete { get; set; }
            public double KnownOutgoingEjectedMassKg { get; set; }
            public double RetainedProjectileMassKg { get; set; }
            public string Provenance { get; set; }
        }

        public sealed class SupportingComponent
        {
            public string ArmorTemplateId { get; set; }
            public ResolvedImpactDamage Damage { get; set; }
        }

        internal sealed class ResolvedSupportingComponent
        {
            public AmmoDataCache.PlateGeometry Geometry { get; set; }
            public ResolvedImpactDamage Damage { get; set; }
        }

        internal static bool TryResolveCoupling(AmmoDataCache.PlateGeometry blocker,
            IList<ResolvedSupportingComponent> supports,
            IDictionary<string, AmmoDataCache.MaterialPhysics> materials,
            ResolvedImpactDamage damage, out BabtTransferModel.Coupling coupling,
            out string diagnostic)
        {
            coupling = null;
            if (blocker == null || materials == null || damage == null)
            {
                diagnostic = "BABT construction, material table and resolved damage state are required.";
                return false;
            }
            if (!Finite(damage.HoleAreaM2) || damage.HoleAreaM2 < 0 ||
                (damage.CoherentFractionsKnown &&
                 (!UnitFraction(damage.FaceCoherentFraction) ||
                  !UnitFraction(damage.BackingCoherentFraction))) ||
                !UnitFraction(damage.FaceRetainedThicknessFraction) ||
                !UnitFraction(damage.BackingRetainedThicknessFraction) ||
                !Finite(damage.KnownOutgoingEjectedMassKg) ||
                damage.KnownOutgoingEjectedMassKg < 0 ||
                !Finite(damage.RetainedProjectileMassKg) ||
                damage.RetainedProjectileMassKg < 0)
            {
                diagnostic = "Resolved hole area, coherence and retained-thickness fractions must be finite and in range.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(damage.Provenance))
            {
                diagnostic = "Resolved post-impact damage state has no provenance.";
                return false;
            }

            var widthM = blocker.BabtWidthMm / 1000.0;
            var heightM = blocker.BabtHeightMm / 1000.0;
            if (!PositiveFinite(widthM) || !PositiveFinite(heightM) ||
                string.IsNullOrWhiteSpace(blocker.BabtGeometrySource) ||
                !KnownEvidence(blocker.BabtGeometryStatus))
            {
                diagnostic = "The blocker has no finite, sourced BABT flexural span.";
                return false;
            }
            if (damage.HoleAreaM2 > widthM * heightM)
            {
                diagnostic = "The resolved perforation area exceeds the construction span area.";
                return false;
            }

            var layers = new List<BabtModel.Layer>();
            var provenance = new List<string>();
            var dampingMassSum = 0.0;
            var dampingWeighted = 0.0;
            var physicalMass = 0.0;
            ResolveCoherence(damage, widthM * heightM,
                out var faceFraction, out var backingFraction);
            var netSection = Math.Max(0, 1.0 - damage.HoleAreaM2 / (widthM * heightM));
            if (damage.Perforated && blocker.BabtForm != "BrittleFace" &&
                faceFraction > netSection + 1e-9)
            {
                diagnostic = "Post-pierce face coherence does not include the resolved hole-area net-section loss.";
                return false;
            }
            if (damage.Perforated && blocker.B > 0 &&
                backingFraction > netSection + 1e-9)
            {
                diagnostic = "Post-pierce backing coherence does not include the resolved hole-area net-section loss.";
                return false;
            }
            if (!TryAppendComponent(blocker, materials, faceFraction,
                    backingFraction, damage.FaceRetainedThicknessFraction,
                    damage.BackingRetainedThicknessFraction, damage.BrittleFaceFailed,
                    layers, provenance, ref physicalMass,
                    ref dampingMassSum, ref dampingWeighted,
                    out diagnostic))
            {
                return false;
            }

            if (supports != null)
            {
                for (var i = 0; i < supports.Count; i++)
                {
                    var support = supports[i];
                    if (support?.Geometry == null || support.Damage == null)
                    {
                        diagnostic = $"Supporting armour component {i} is missing from the payload.";
                        return false;
                    }
                    if (!support.Damage.LocalStateResolved)
                    {
                        diagnostic = $"Supporting armour component {i} has no resolved local hit state; global durability is not a stiffness fraction.";
                        return false;
                    }
                    if (!ValidateDamage(support.Damage, out diagnostic))
                    {
                        diagnostic = $"Supporting armour component {i}: {diagnostic}";
                        return false;
                    }
                    if (!TryComponentArea(support.Geometry, out var supportArea))
                    {
                        diagnostic = $"Supporting armour component {i} has no physical area.";
                        return false;
                    }
                    if (support.Damage.HoleAreaM2 > supportArea)
                    {
                        diagnostic = $"Supporting armour component {i}: resolved perforation area exceeds its construction area.";
                        return false;
                    }
                    ResolveCoherence(support.Damage, supportArea,
                        out var supportFace, out var supportBacking);
                    var supportNetSection = Math.Max(0,
                        1.0 - support.Damage.HoleAreaM2 / supportArea);
                    if (support.Damage.Perforated &&
                        support.Geometry.BabtForm != "BrittleFace" &&
                        supportFace > supportNetSection + 1e-9)
                    {
                        diagnostic = $"Supporting armour component {i}: post-pierce face coherence omits the resolved hole-area net-section loss.";
                        return false;
                    }
                    if (support.Damage.Perforated && support.Geometry.B > 0 &&
                        supportBacking > supportNetSection + 1e-9)
                    {
                        diagnostic = $"Supporting armour component {i}: post-pierce backing coherence omits the resolved hole-area net-section loss.";
                        return false;
                    }
                    if (!TryAppendComponent(support.Geometry, materials, supportFace,
                            supportBacking, support.Damage.FaceRetainedThicknessFraction,
                            support.Damage.BackingRetainedThicknessFraction,
                            support.Damage.BrittleFaceFailed,
                            layers, provenance, ref physicalMass, ref dampingMassSum,
                            ref dampingWeighted, out diagnostic))
                    {
                        diagnostic = $"Supporting armour component {i}: {diagnostic}";
                        return false;
                    }
                }
            }

            var knownEjectedMass = damage.KnownOutgoingEjectedMassKg;
            var retainedProjectileMass = damage.RetainedProjectileMassKg;
            var outgoingComplete = damage.OutgoingInventoryComplete;
            if (supports != null)
            {
                for (var i = 0; i < supports.Count; i++)
                {
                    knownEjectedMass += supports[i].Damage.KnownOutgoingEjectedMassKg;
                    retainedProjectileMass += supports[i].Damage.RetainedProjectileMassKg;
                    outgoingComplete &= supports[i].Damage.OutgoingInventoryComplete;
                }
            }
            var participatingMass = physicalMass - knownEjectedMass + retainedProjectileMass;
            if (!PositiveFinite(participatingMass) ||
                knownEjectedMass > physicalMass)
            {
                diagnostic = "Known ejecta exceeds pristine assembly mass or leaves no participating mass.";
                return false;
            }
            if (layers.Count == 0)
            {
                diagnostic = "The post-impact assembly retains mass but has no coherent stiffness branch.";
                return false;
            }

            var modalHalf = participatingMass / 2.0;
            var stiffness = ReducedLinearStiffness(layers, widthM, heightM);
            if (!Finite(stiffness) || stiffness < 0)
            {
                diagnostic = "The reduced construction stiffness calculation is non-finite.";
                return false;
            }
            var zeta = dampingMassSum > 0 ? dampingWeighted / dampingMassSum : 0;
            var damping = stiffness > 0 && zeta > 0
                ? 2.0 * zeta * Math.Sqrt(stiffness * participatingMass / 4.0)
                : 0;
            if (!Finite(damping))
            {
                diagnostic = "The reduced construction damping calculation is non-finite.";
                return false;
            }

            var constructionProvenance = string.Join("; ", provenance) +
                $"; span: {blocker.BabtGeometryStatus}, {blocker.BabtGeometrySource}; " +
                "m_local=m_rigid=M_participating/2 gives M/4 relative inertia as a two-mass engineering reduction; " +
                "it is not an exact simultaneous translation/modal decomposition; simply-supported shared-mode and centre/antinode impact are engineering estimates" +
                (!outgoingComplete
                    ? "; unknown ejecta makes retained mass participation an upper-bound estimate"
                    : "");
            var construction = new BabtModel.Construction
            {
                WidthM = widthM,
                HeightM = heightM,
                LocalEffectiveMassKg = modalHalf,
                RigidEffectiveMassKg = modalHalf,
                FlexuralDampingNsPerM = damping,
                Boundary = BabtModel.BoundaryCondition.SimplySupported,
                Layers = layers.ToArray(),
                Provenance = constructionProvenance,
                Readiness = BabtModel.Readiness.Provisional,
            };
            coupling = new BabtTransferModel.Coupling
            {
                PostImpactConstruction = construction,
                ImpactModeFactor = 1,
                OutgoingInventoryComplete = outgoingComplete,
                Provenance = constructionProvenance + "; damage: " + damage.Provenance,
                Readiness = BabtModel.Readiness.Provisional,
            };
            diagnostic = supports != null && supports.Count > 0
                ? "BABT coupling resolved from the penetration construction plus exact support components; shared-mode kinematics and centre impact are provisional."
                : "BABT coupling resolved from the established penetration construction; centre impact and support are provisional.";
            return true;
        }

        private static bool TryAppendComponent(AmmoDataCache.PlateGeometry plate,
            IDictionary<string, AmmoDataCache.MaterialPhysics> materials,
            double faceFraction, double backingFraction,
            double faceRetainedThickness, double backingRetainedThickness,
            bool brittleFaceFailed,
            ICollection<BabtModel.Layer> layers,
            ICollection<string> provenance, ref double physicalMass,
            ref double dampingMassSum,
            ref double dampingWeighted, out string diagnostic)
        {
            diagnostic = null;
            if (!PositiveFinite(plate.T) || string.IsNullOrWhiteSpace(plate.M) ||
                !materials.TryGetValue(plate.M, out var material) || material == null)
            {
                diagnostic = "Construction thickness or material is missing.";
                return false;
            }
            if (!PositiveFinite(material.DensityGCm3))
            {
                diagnostic = $"Material '{plate.M}' has no finite positive density.";
                return false;
            }
            if (!Finite(plate.P) || plate.P < 0 || plate.P > 1)
            {
                diagnostic = "Construction density ratio/packing is non-finite or outside [0, 1].";
                return false;
            }
            if (!Finite(plate.B) || plate.B < 0)
            {
                diagnostic = "Construction backing thickness is non-finite or negative.";
                return false;
            }
            if (!Finite(plate.Y) || plate.Y < 0)
            {
                diagnostic = "Construction yield-strength override is non-finite or negative.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plate.ConstructionSource))
            {
                diagnostic = "Construction thickness/backing has no provenance.";
                return false;
            }

            var packed = PositiveFinite(plate.P) ? Math.Min(plate.P, 1) : 1;
            if (!TryComponentArea(plate, out var componentArea))
            {
                diagnostic = "The component has no finite physical area for mass participation.";
                return false;
            }
            physicalMass += material.DensityGCm3 * 1000.0 * packed *
                            plate.T / 1000.0 * componentArea;
            if (!TryBuildLayer(plate.M, plate.BabtForm, plate.T, packed,
                    plate.Y > 0 ? plate.Y : material.YieldMPa,
                    material, faceFraction, faceRetainedThickness, brittleFaceFailed,
                    false, out var face, out var dampingRatio, out diagnostic))
            {
                return false;
            }
            if (face != null)
            {
                face.BondedToPrevious = false;
                layers.Add(face);
                AddDampingWeight(face, dampingRatio, ref dampingMassSum, ref dampingWeighted);
                provenance.Add($"{plate.M} face: {plate.ConstructionOrigin}, {plate.ConstructionSource}; " +
                               $"form={plate.BabtForm}, density-ratio/packing={packed:0.####}, " +
                               $"coherent-net-section={faceFraction:0.####}, retained-thickness={faceRetainedThickness:0.####}; stiffness uses A proportional to f, D proportional to f^3 and metal yield moment proportional to f^2; damage mapping is provisional" +
                               (packed < 0.999 && (plate.BabtForm == "IsotropicPlate" ||
                                                  plate.BabtForm == "BrittleFace")
                                   ? "; item density override is reused for physical mass while elastic constants remain the generic material estimate"
                                   : ""));
            }

            if (plate.B > 0)
            {
                if (!Finite(plate.BP) || plate.BP < 0 || plate.BP > 1)
                {
                    diagnostic = "Backing packing fraction is non-finite or outside [0, 1].";
                    return false;
                }
                var backingKey = string.IsNullOrWhiteSpace(plate.BM) ? "Aramid" : plate.BM;
                if (!materials.TryGetValue(backingKey, out var backingMaterial) || backingMaterial == null)
                {
                    diagnostic = $"Backing material '{backingKey}' is absent from the material table.";
                    return false;
                }
                if (!PositiveFinite(backingMaterial.DensityGCm3))
                {
                    diagnostic = $"Backing material '{backingKey}' has no finite positive density.";
                    return false;
                }
                var backingPacked = PositiveFinite(plate.BP)
                    ? Math.Min(plate.BP, 1)
                    : backingKey.Equals("Aramid", StringComparison.OrdinalIgnoreCase)
                        ? BallisticLimit.SewnPacked
                        : 1;
                physicalMass += backingMaterial.DensityGCm3 * 1000.0 * backingPacked *
                                plate.B / 1000.0 * componentArea;
                if (!TryBuildLayer(backingKey, plate.BabtBackingForm, plate.B,
                        backingPacked, backingMaterial.YieldMPa, backingMaterial,
                        backingFraction, backingRetainedThickness, false, true, out var backing,
                        out var backingDamping, out diagnostic))
                {
                    return false;
                }
                if (backing != null)
                {
                    backing.BondedToPrevious = plate.BabtBackingForm == "BondedLaminate" &&
                                               face != null;
                    layers.Add(backing);
                    AddDampingWeight(backing, backingDamping,
                        ref dampingMassSum, ref dampingWeighted);
                    provenance.Add($"{backingKey} intrinsic backing: {plate.ConstructionSource}; " +
                                   $"form={plate.BabtBackingForm}, packing={backingPacked:0.####}, " +
                                   $"coherent-net-section={backingFraction:0.####}, retained-thickness={backingRetainedThickness:0.####}; stiffness uses the form-specific retained-thickness law; damage mapping is provisional");
                }
            }

            if (face == null && plate.B <= 0)
            {
                diagnostic = "The component has no coherent post-impact face or backing layer.";
                return false;
            }
            return true;
        }

        private static bool TryBuildLayer(string name, string form, double thicknessMm,
            double packed, double yieldMpa, AmmoDataCache.MaterialPhysics material,
            double coherentFraction, double effectiveThicknessFraction,
            bool excludeBrittle, bool backing,
            out BabtModel.Layer layer, out double dampingRatio, out string diagnostic)
        {
            layer = null;
            dampingRatio = 0;
            diagnostic = null;
            if (!PositiveFinite(coherentFraction) || effectiveThicknessFraction <= 0)
            {
                return true;
            }
            if (form == "BrittleFace" && excludeBrittle)
            {
                return true;
            }
            if (!TryEvidence(material.StructuralDampingRatio, true,
                    out dampingRatio, out var dampingSource))
            {
                diagnostic = $"Material '{name}' has no sourced structural damping ratio.";
                return false;
            }
            if (!PositiveFinite(material.DensityGCm3) || !PositiveFinite(packed) || packed > 1 ||
                !PositiveFinite(material.FailureStrain))
            {
                diagnostic = $"Material '{name}' has invalid density, packing or failure strain.";
                return false;
            }

            var layerForm = BabtModel.LayerForm.MetalPlate;
            var young = 0.0;
            var poisson = 0.0;
            var extensional = 0.0;
            var flexural = 0.0;
            var membrane = 0.0;
            var mechanicalEvidence = "";
            if (form == "IsotropicPlate" || form == "BrittleFace")
            {
                layerForm = form == "BrittleFace"
                    ? BabtModel.LayerForm.BrittleFace
                    : BabtModel.LayerForm.MetalPlate;
                if (!TryEvidence(material.YoungModulusGPa, false, out var eGpa,
                        out var eSource) ||
                    !TryEvidence(material.PoissonRatio, true, out poisson,
                        out var nuSource))
                {
                    diagnostic = $"Material '{name}' lacks sourced isotropic elastic properties.";
                    return false;
                }
                if (poisson <= -1 || poisson >= 0.5)
                {
                    diagnostic = $"Material '{name}' has a Poisson ratio outside (-1, 0.5).";
                    return false;
                }
                young = eGpa * 1e9;
                mechanicalEvidence = "; E: " + eSource + "; nu: " + nuSource;
                if (layerForm == BabtModel.LayerForm.MetalPlate &&
                    (!PositiveFinite(yieldMpa) || material.FailureStrain <= yieldMpa * 1e6 / young))
                {
                    diagnostic = $"Metal '{name}' lacks a valid yield/failure-strain domain.";
                    return false;
                }
            }
            else if (form == "BondedLaminate")
            {
                layerForm = BabtModel.LayerForm.RigidLaminate;
                if (!TryEvidence(material.RigidLaminateModulusGPa, false,
                        out var laminateGpa, out var laminateSource) ||
                    !TryEvidence(material.RigidLaminatePoissonRatio, true,
                        out poisson, out var laminateNuSource))
                {
                    diagnostic = $"Material '{name}' lacks a sourced effective laminate modulus.";
                    return false;
                }
                mechanicalEvidence = "; effective package E: " + laminateSource +
                                     "; effective package nu: " + laminateNuSource;
                if (poisson <= -1 || poisson >= 0.5)
                {
                    diagnostic = $"Material '{name}' has a laminate Poisson ratio outside (-1, 0.5).";
                    return false;
                }
                var h = thicknessMm / 1000.0;
                var effectiveE = laminateGpa * 1e9 * packed;
                var denominator = 1.0 - poisson * poisson;
                extensional = effectiveE * h / denominator;
                flexural = effectiveE * h * h * h / (12.0 * denominator);
            }
            else if (form == "SoftWoven" || form == "SoftUd")
            {
                layerForm = BabtModel.LayerForm.SoftFabric;
                if (!PositiveFinite(material.FibreTensileMPa))
                {
                    diagnostic = $"Soft package '{name}' lacks fibre tensile strength.";
                    return false;
                }
                var secant = material.FibreTensileMPa * 1e6 / material.FailureStrain;
                // Both woven cloth and a balanced 0/90 UD package place about half
                // of their fibre in either principal direction. A single UD ply has
                // no isotropic radial membrane reduction and is outside this form.
                var orientationShare = 0.5;
                membrane = secant * packed * orientationShare * thicknessMm / 1000.0;
            }
            else
            {
                diagnostic = $"BABT form '{form ?? "missing"}' is unsupported.";
                return false;
            }

            layer = new BabtModel.Layer
            {
                Name = (backing ? "backing " : "face ") + name,
                Form = layerForm,
                State = BabtModel.LayerState.Intact,
                ThicknessM = thicknessMm / 1000.0,
                DensityKgM3 = material.DensityGCm3 * 1000.0 * packed,
                YoungModulusPa = young,
                PoissonRatio = poisson,
                ExtensionalStiffnessNPerM = extensional,
                FlexuralRigidityNm = flexural,
                YieldStrengthPa = yieldMpa * 1e6,
                FailureStrain = material.FailureStrain,
                MembraneStiffnessNPerM = membrane,
                CoherentFraction = coherentFraction,
                EffectiveThicknessFraction = effectiveThicknessFraction,
                Provenance = (material.Class ?? "") + " material; " +
                             (MaterialSource(material) ?? "material-table properties") +
                             mechanicalEvidence +
                             "; damping: " + dampingSource +
                             (layerForm == BabtModel.LayerForm.SoftFabric
                                 ? "; membrane A estimated as (fibre tensile/failure strain)*packing*0.5*h for woven or balanced 0/90 package; no yarn modulus is used, and layup/crimp/high-rate package response remains provisional"
                                 : layerForm == BabtModel.LayerForm.RigidLaminate
                                     ? "; A and D reduced from sourced effective package modulus, packing and physical thickness"
                                     : ""),
                Readiness = BabtModel.Readiness.Provisional,
            };
            return true;
        }

        private static string MaterialSource(AmmoDataCache.MaterialPhysics material)
        {
            // Legacy material wire did not carry its broad penetration source. The
            // mechanical parameter objects above carry the required exact citations.
            if (!string.IsNullOrWhiteSpace(material?.Source)) return material.Source;
            if (!string.IsNullOrWhiteSpace(material?.YoungModulusGPa?.Source))
                return material.YoungModulusGPa.Source;
            return material?.RigidLaminateModulusGPa?.Source;
        }

        private static void AddDampingWeight(BabtModel.Layer layer, double ratio,
            ref double massSum, ref double weighted)
        {
            var arealMass = layer.DensityKgM3 * layer.ThicknessM * layer.CoherentFraction;
            massSum += arealMass;
            weighted += arealMass * ratio;
        }

        private static bool TryComponentArea(AmmoDataCache.PlateGeometry plate,
            out double areaM2)
        {
            areaM2 = plate.BabtWidthMm * plate.BabtHeightMm / 1e6;
            return PositiveFinite(areaM2) && KnownEvidence(plate.BabtGeometryStatus) &&
                   !string.IsNullOrWhiteSpace(plate.BabtGeometrySource);
        }

        private static double ReducedLinearStiffness(IList<BabtModel.Layer> layers,
            double widthM, double heightM)
        {
            var area = widthM * heightM;
            var lambda = Math.PI * Math.PI / (widthM * widthM) +
                         Math.PI * Math.PI / (heightM * heightM);
            var modalFactor = area * lambda * lambda / 4.0;
            var stiffness = 0.0;
            var first = 0;
            while (first < layers.Count)
            {
                var end = first + 1;
                while (end < layers.Count && layers[end].BondedToPrevious) end++;
                if (layers[first].Form == BabtModel.LayerForm.SoftFabric)
                {
                    first = end;
                    continue;
                }

                var thickness = 0.0;
                var sumA = 0.0;
                var sumAz = 0.0;
                for (var i = first; i < end; i++)
                {
                    var layer = layers[i];
                    var a = ExtensionalStiffness(layer);
                    var centre = thickness + layer.ThicknessM / 2.0;
                    thickness += layer.ThicknessM;
                    sumA += a;
                    sumAz += a * centre;
                }
                if (!(sumA > 0) || !Finite(sumA)) return double.NaN;
                var neutral = sumAz / sumA;
                var z = 0.0;
                for (var i = first; i < end; i++)
                {
                    var layer = layers[i];
                    var centre = z + layer.ThicknessM / 2.0;
                    var a = ExtensionalStiffness(layer);
                    stiffness += (IntrinsicFlexuralRigidity(layer) +
                                  a * (centre - neutral) * (centre - neutral)) * modalFactor;
                    z += layer.ThicknessM;
                }
                first = end;
            }
            return stiffness;
        }

        private static double ExtensionalStiffness(BabtModel.Layer layer)
        {
            if (layer.Form == BabtModel.LayerForm.RigidLaminate)
                return layer.ExtensionalStiffnessNPerM * layer.CoherentFraction *
                       layer.EffectiveThicknessFraction;
            return layer.YoungModulusPa * layer.ThicknessM * layer.CoherentFraction *
                   layer.EffectiveThicknessFraction /
                   (1.0 - layer.PoissonRatio * layer.PoissonRatio);
        }

        private static double IntrinsicFlexuralRigidity(BabtModel.Layer layer)
        {
            var f = layer.EffectiveThicknessFraction;
            if (layer.Form == BabtModel.LayerForm.RigidLaminate)
                return layer.FlexuralRigidityNm * layer.CoherentFraction * f * f * f;
            return layer.YoungModulusPa * layer.ThicknessM * layer.ThicknessM *
                   layer.ThicknessM * layer.CoherentFraction * f * f * f /
                   (12.0 * (1.0 - layer.PoissonRatio * layer.PoissonRatio));
        }

        private static bool TryEvidence(AmmoDataCache.BabtParameter parameter,
            bool allowZero, out double value, out string source)
        {
            value = parameter?.Value ?? double.NaN;
            source = parameter?.Source;
            return Finite(value) && (allowZero ? value >= 0 : value > 0) &&
                   !string.IsNullOrWhiteSpace(source) && KnownEvidence(parameter.Status);
        }

        private static bool KnownEvidence(string status)
        {
            return string.Equals(status, "Measured", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Estimated", StringComparison.OrdinalIgnoreCase);
        }

        private static bool UnitFraction(double value)
        {
            return Finite(value) && value >= 0 && value <= 1;
        }

        private static bool ValidateDamage(ResolvedImpactDamage damage,
            out string diagnostic)
        {
            if (damage == null || !Finite(damage.HoleAreaM2) || damage.HoleAreaM2 < 0 ||
                (damage.CoherentFractionsKnown &&
                 (!UnitFraction(damage.FaceCoherentFraction) ||
                  !UnitFraction(damage.BackingCoherentFraction))) ||
                !UnitFraction(damage.FaceRetainedThicknessFraction) ||
                !UnitFraction(damage.BackingRetainedThicknessFraction) ||
                !Finite(damage.KnownOutgoingEjectedMassKg) ||
                damage.KnownOutgoingEjectedMassKg < 0 ||
                !Finite(damage.RetainedProjectileMassKg) ||
                damage.RetainedProjectileMassKg < 0 ||
                string.IsNullOrWhiteSpace(damage.Provenance))
            {
                diagnostic = "Resolved component damage is missing, non-finite, out of range or unsourced.";
                return false;
            }
            diagnostic = null;
            return true;
        }

        private static void ResolveCoherence(ResolvedImpactDamage damage, double areaM2,
            out double face, out double backing)
        {
            if (damage.CoherentFractionsKnown)
            {
                face = damage.FaceCoherentFraction;
                backing = damage.BackingCoherentFraction;
                return;
            }

            var netSection = damage.Perforated
                ? Math.Max(0, 1.0 - damage.HoleAreaM2 / areaM2)
                : 1.0;
            face = netSection;
            backing = netSection;
        }

        private static bool PositiveFinite(double value)
        {
            return Finite(value) && value > 0;
        }

        internal static bool TryResolve(AmmoDataCache.BabtConstructionEnvelope envelope,
            out BabtModel.Construction construction, out string diagnostic)
        {
            construction = null;
            var p = envelope?.Profile;
            if (p == null)
            {
                diagnostic = "BABT construction payload is null.";
                return false;
            }

            var gaps = new List<string>();
            var readiness = BabtModel.Readiness.Calibrated;
            if (string.IsNullOrWhiteSpace(p.Source)) gaps.Add("construction composition has no source");
            var width = Required(p.WidthMm, "construction width", gaps, ref readiness);
            var height = Required(p.HeightMm, "construction height", gaps, ref readiness);
            var localMass = Required(p.LocalEffectiveMassKg, "local effective mass", gaps, ref readiness);
            var rigidMass = Required(p.RigidEffectiveMassKg, "rigid effective mass", gaps, ref readiness);
            var flexuralDamping = RequiredNonnegative(p.FlexuralDampingNsPerM, "flexural damping", gaps,
                ref readiness);
            var curvature = RequiredNonnegative(p.CurvatureRadiusMm, "construction curvature", gaps,
                ref readiness);
            if (curvature > 0)
            {
                gaps.Add("curved shells are outside the flat simply-supported reduction");
            }
            if (HasPositive(p.EdgeStiffnessNPerM) || HasPositive(p.EdgeDampingNsPerM))
            {
                gaps.Add("elastic carrier edge support is not represented by SimplySupported");
            }
            if (HasPositive(p.PadThicknessMm) || HasPositive(p.PadModulusMPa))
            {
                gaps.Add("construction-local pad data must be resolved through an applicable body profile");
            }

            if (!string.Equals(p.Support, "SimplySupported", StringComparison.OrdinalIgnoreCase))
            {
                gaps.Add(string.IsNullOrWhiteSpace(p.Support)
                    ? "boundary condition is missing"
                    : $"boundary condition '{p.Support}' is unsupported");
            }

            var layers = new List<BabtModel.Layer>();
            if (p.Layers == null || p.Layers.Count == 0)
            {
                gaps.Add("construction has no physical layers");
            }
            else
            {
                for (var i = 0; i < p.Layers.Count; i++)
                {
                    var layer = ResolveLayer(p.Layers[i], i, gaps, ref readiness);
                    if (layer != null)
                    {
                        layers.Add(layer);
                    }
                }
            }
            ValidateKind(p.Kind, layers, gaps);

            if (gaps.Count > 0)
            {
                diagnostic = string.Join("; ", gaps);
                return false;
            }

            construction = new BabtModel.Construction
            {
                WidthM = width / 1000.0,
                HeightM = height / 1000.0,
                LocalEffectiveMassKg = localMass,
                RigidEffectiveMassKg = rigidMass,
                FlexuralDampingNsPerM = flexuralDamping,
                Boundary = BabtModel.BoundaryCondition.SimplySupported,
                Layers = layers.ToArray(),
                Provenance = Provenance(envelope.ProfileKey, p.Source, p.Notes),
                Readiness = readiness,
            };
            diagnostic = readiness == BabtModel.Readiness.Calibrated
                ? "BABT construction resolved from measured inputs for a centred effective-mode response; off-centre/edge BFD is unsupported."
                : "BABT construction resolved with explicitly identified estimates for a centred effective-mode response; off-centre/edge BFD is unsupported.";
            return true;
        }

        private static BabtModel.Layer ResolveLayer(AmmoDataCache.BabtLayerProfile p, int index,
            ICollection<string> gaps, ref BabtModel.Readiness readiness)
        {
            if (p == null)
            {
                gaps.Add($"layer {index}: payload is null");
                return null;
            }

            var prefix = $"layer {index} ({p.Material ?? "unnamed"})";
            if (string.IsNullOrWhiteSpace(p.Source)) gaps.Add(prefix + ": layer composition has no source");
            if (!TryForm(p.Form, out var form))
            {
                gaps.Add($"{prefix}: form '{p.Form ?? "missing"}' is unsupported");
                return null;
            }

            var h = Required(p.ThicknessMm, prefix + " thickness", gaps, ref readiness);
            var rho = Required(p.DensityKgM3, prefix + " density", gaps, ref readiness);
            var e = 0.0;
            var nu = 0.0;
            var a = 0.0;
            var d = 0.0;
            var membrane = 0.0;
            var yield = 0.0;
            var failure = Required(p.FailureStrain, prefix + " failure strain", gaps, ref readiness);

            switch (form)
            {
                case BabtModel.LayerForm.MetalPlate:
                    e = Required(p.YoungModulusGPa, prefix + " Young modulus", gaps, ref readiness) * 1e9;
                    nu = RequiredPoisson(p.PoissonRatio, prefix + " Poisson ratio", gaps, ref readiness);
                    yield = Required(p.YieldStrengthMPa, prefix + " yield strength", gaps, ref readiness) * 1e6;
                    if (e > 0 && yield > 0 && failure <= yield / e)
                    {
                        gaps.Add(prefix + " failure strain must exceed elastic yield strain");
                    }
                    break;
                case BabtModel.LayerForm.BrittleFace:
                    e = Required(p.YoungModulusGPa, prefix + " Young modulus", gaps, ref readiness) * 1e9;
                    nu = RequiredPoisson(p.PoissonRatio, prefix + " Poisson ratio", gaps, ref readiness);
                    break;
                case BabtModel.LayerForm.RigidLaminate:
                    a = Required(p.ExtensionalStiffnessNPerM, prefix + " extensional stiffness", gaps,
                        ref readiness);
                    d = Required(p.FlexuralRigidityNm, prefix + " flexural rigidity", gaps, ref readiness);
                    break;
                case BabtModel.LayerForm.SoftFabric:
                    membrane = Required(p.MembraneStiffnessNPerM, prefix + " membrane stiffness", gaps,
                        ref readiness);
                    break;
            }

            return new BabtModel.Layer
            {
                Name = string.IsNullOrWhiteSpace(p.Material) ? $"layer {index}" : p.Material,
                Form = form,
                State = BabtModel.LayerState.Intact,
                ThicknessM = h / 1000.0,
                DensityKgM3 = rho,
                YoungModulusPa = e,
                PoissonRatio = nu,
                ExtensionalStiffnessNPerM = a,
                FlexuralRigidityNm = d,
                YieldStrengthPa = yield,
                MembraneStiffnessNPerM = membrane,
                FailureStrain = failure,
                BondedToPrevious = p.BondedToPrevious,
                Provenance = Provenance(p.Material, p.Source, null),
                Readiness = readiness,
            };
        }

        private static bool TryForm(string form, out BabtModel.LayerForm value)
        {
            if (string.Equals(form, "IsotropicPlate", StringComparison.OrdinalIgnoreCase))
            {
                value = BabtModel.LayerForm.MetalPlate;
                return true;
            }
            if (string.Equals(form, "BondedLaminate", StringComparison.OrdinalIgnoreCase))
            {
                value = BabtModel.LayerForm.RigidLaminate;
                return true;
            }
            if (string.Equals(form, "SoftWoven", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(form, "SoftUd", StringComparison.OrdinalIgnoreCase))
            {
                value = BabtModel.LayerForm.SoftFabric;
                return true;
            }
            if (string.Equals(form, "BrittleFace", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(form, "GlassPly", StringComparison.OrdinalIgnoreCase))
            {
                value = BabtModel.LayerForm.BrittleFace;
                return true;
            }

            value = default;
            return false;
        }

        private static void ValidateKind(string kind, IList<BabtModel.Layer> layers,
            ICollection<string> gaps)
        {
            bool All(BabtModel.LayerForm form)
            {
                if (layers.Count == 0) return false;
                for (var i = 0; i < layers.Count; i++) if (layers[i].Form != form) return false;
                return true;
            }

            switch (kind)
            {
                case "MetalPlate":
                    if (!All(BabtModel.LayerForm.MetalPlate)) gaps.Add("MetalPlate requires only metal plate layers");
                    break;
                case "BondedFibreLaminate":
                    if (!All(BabtModel.LayerForm.RigidLaminate)) gaps.Add("BondedFibreLaminate requires measured rigid-laminate layers");
                    break;
                case "SoftIntegrated":
                    if (!All(BabtModel.LayerForm.SoftFabric)) gaps.Add("SoftIntegrated requires soft-fabric package layers");
                    break;
                case "CeramicComposite":
                    if (layers.Count < 2 || layers[0].Form != BabtModel.LayerForm.BrittleFace)
                        gaps.Add("CeramicComposite requires a brittle face and an explicit backing layer");
                    break;
                case "LayerStack":
                    if (layers.Count == 0) gaps.Add("LayerStack requires an ordered physical layer stack");
                    break;
                case "GlassLaminate":
                    if (layers.Count < 2 || layers[0].Form != BabtModel.LayerForm.BrittleFace)
                        gaps.Add("GlassLaminate requires explicit glass plies and interlayers");
                    break;
                default:
                    gaps.Add($"construction kind '{kind ?? "missing"}' is unsupported");
                    break;
            }
        }

        private static double Required(AmmoDataCache.BabtParameter p, string name,
            ICollection<string> gaps, ref BabtModel.Readiness readiness)
        {
            if (p?.Value == null || !Finite(p.Value.Value) || p.Value <= 0 ||
                !Evidence(p, ref readiness))
            {
                gaps.Add(Gap(name, p));
                return 0;
            }
            return p.Value.Value;
        }

        private static double RequiredPoisson(AmmoDataCache.BabtParameter p, string name,
            ICollection<string> gaps, ref BabtModel.Readiness readiness)
        {
            if (p?.Value == null || !Finite(p.Value.Value) || !Evidence(p, ref readiness))
            {
                gaps.Add(Gap(name, p));
                return 0;
            }
            var value = p.Value.Value;
            if (value <= -1 || value >= 0.5)
            {
                gaps.Add(name + " must be greater than -1 and below 0.5");
                return 0;
            }
            return value;
        }

        internal static bool TryResolveNumerics(AmmoDataCache.BabtNumericsProfile profile,
            out BabtModel.Numerics numerics, out string diagnostic)
        {
            numerics = null;
            if (profile == null)
            {
                diagnostic = "No BABT numerical integration policy is authored.";
                return false;
            }

            var gaps = new List<string>();
            var readiness = BabtModel.Readiness.Calibrated;
            if (string.IsNullOrWhiteSpace(profile.Source))
            {
                gaps.Add("BABT numerical policy has no provenance");
            }
            var dt = Required(profile.TimeStepS, "integration time step", gaps, ref readiness);
            var duration = Required(profile.SimulationDurationS, "simulation duration", gaps, ref readiness);
            var maxStepsRaw = Required(profile.MaximumIntegrationSteps,
                "maximum integration steps", gaps, ref readiness);
            var stepsPerPeriod = Required(profile.MinimumStepsPerPeriod,
                "minimum steps per period", gaps, ref readiness);
            var energyError = Required(profile.MaximumRelativeEnergyError,
                "maximum relative energy error", gaps, ref readiness);
            var settledVelocity = RequiredNonnegative(profile.SettledVelocityToleranceMps,
                "settled velocity tolerance", gaps, ref readiness);
            var settledForce = RequiredNonnegative(profile.SettledForceToleranceN,
                "settled force tolerance", gaps, ref readiness);
            if (maxStepsRaw > int.MaxValue ||
                Math.Abs(maxStepsRaw - Math.Round(maxStepsRaw)) > 1e-9)
            {
                gaps.Add("maximum integration steps must be a positive integer in Int32 range");
            }
            if (dt > 0 && duration > 0 && maxStepsRaw > 0 &&
                Math.Ceiling(duration / dt) > maxStepsRaw)
            {
                gaps.Add("simulation duration divided by time step exceeds the workload ceiling");
            }
            if (gaps.Count > 0)
            {
                diagnostic = string.Join("; ", gaps);
                return false;
            }

            numerics = new BabtModel.Numerics
            {
                TimeStepS = dt,
                SimulationDurationS = duration,
                MaximumIntegrationSteps = (int)Math.Round(maxStepsRaw),
                MinimumStepsPerPeriod = stepsPerPeriod,
                MaximumRelativeEnergyError = energyError,
                SettledVelocityToleranceMps = settledVelocity,
                SettledForceToleranceN = settledForce,
                Provenance = profile.Source + "; " +
                    "dt: " + EvidenceSummary(profile.TimeStepS) + "; horizon: " +
                    EvidenceSummary(profile.SimulationDurationS) + "; max steps: " +
                    EvidenceSummary(profile.MaximumIntegrationSteps) + "; steps/period: " +
                    EvidenceSummary(profile.MinimumStepsPerPeriod) + "; energy error: " +
                    EvidenceSummary(profile.MaximumRelativeEnergyError) + "; settled velocity: " +
                    EvidenceSummary(profile.SettledVelocityToleranceMps) + "; settled force: " +
                    EvidenceSummary(profile.SettledForceToleranceN),
                Readiness = readiness,
            };
            diagnostic = "BABT numerical verification policy resolved.";
            return true;
        }

        private static string EvidenceSummary(AmmoDataCache.BabtParameter parameter)
        {
            var source = !string.IsNullOrWhiteSpace(parameter?.Source)
                ? parameter.Source
                : !string.IsNullOrWhiteSpace(parameter?.Reason)
                    ? parameter.Reason
                    : "no provenance";
            return (parameter?.Status ?? "Missing") + ", " +
                   source;
        }

        internal static bool TryResolveImpact(
            IDictionary<string, AmmoDataCache.BabtImpactProfile> profiles,
            string constructionKey, string ammoTemplateId, double projectileMassKg,
            double projectileDiameterM, double normalVelocityMps, double contactAreaM2,
            bool perforates, out BabtModel.Impact impact, out string diagnostic)
        {
            impact = null;
            if (profiles == null || profiles.Count == 0)
            {
                diagnostic = "No validated BABT projectile-contact profiles are authored.";
                return false;
            }

            var matches = new List<KeyValuePair<string, AmmoDataCache.BabtImpactProfile>>();
            foreach (var pair in profiles)
            {
                var p = pair.Value;
                if (p != null && Contains(p.ConstructionKeys, constructionKey) &&
                    Contains(p.AmmoKeys, ammoTemplateId))
                {
                    matches.Add(pair);
                }
            }
            if (matches.Count == 0)
            {
                diagnostic = "No BABT contact profile applies to this exact construction and ammunition.";
                return false;
            }
            var applicable = new List<KeyValuePair<string, AmmoDataCache.BabtImpactProfile>>();
            var exclusions = new List<string>();
            for (var i = 0; i < matches.Count; i++)
            {
                if (EnvelopeContains(matches[i].Value, projectileMassKg, projectileDiameterM,
                        normalVelocityMps, out var why))
                {
                    applicable.Add(matches[i]);
                }
                else
                {
                    exclusions.Add(matches[i].Key + ": " + why);
                }
            }
            if (applicable.Count == 0)
            {
                diagnostic = "No matching BABT contact profile contains this runtime impact: " +
                             string.Join("; ", exclusions);
                return false;
            }
            if (applicable.Count > 1)
            {
                diagnostic = "Overlapping BABT contact profiles claim this construction, ammunition and impact envelope.";
                return false;
            }

            var selected = applicable[0];
            var gaps = new List<string>();
            var readiness = BabtModel.Readiness.Calibrated;
            var p2 = selected.Value;
            if (string.IsNullOrWhiteSpace(p2.Source)) gaps.Add("impact/contact profile has no source");
            var minMass = Required(p2.MinProjectileMassG, "minimum projectile mass", gaps, ref readiness) / 1000.0;
            var maxMass = Required(p2.MaxProjectileMassG, "maximum projectile mass", gaps, ref readiness) / 1000.0;
            var minDia = Required(p2.MinProjectileDiameterMm, "minimum projectile diameter", gaps, ref readiness) / 1000.0;
            var maxDia = Required(p2.MaxProjectileDiameterMm, "maximum projectile diameter", gaps, ref readiness) / 1000.0;
            var minVelocity = Required(p2.MinNormalVelocityMps, "minimum normal velocity", gaps, ref readiness);
            var maxVelocity = Required(p2.MaxNormalVelocityMps, "maximum normal velocity", gaps, ref readiness);
            var stiffness = Required(p2.ProjectileContactStiffnessNPerM, "projectile contact stiffness", gaps, ref readiness);
            var damping = RequiredNonnegative(p2.ProjectileContactDampingNsPerM,
                "projectile contact damping", gaps, ref readiness);
            var dt = Required(p2.TimeStepS, "integration time step", gaps, ref readiness);
            var duration = Required(p2.SimulationDurationS, "simulation duration", gaps, ref readiness);
            var maxStepsRaw = Required(p2.MaximumIntegrationSteps, "maximum integration steps", gaps, ref readiness);
            var stepsPerPeriod = Required(p2.MinimumStepsPerPeriod, "minimum steps per period", gaps, ref readiness);
            var energyError = Required(p2.MaximumRelativeEnergyError, "maximum relative energy error", gaps,
                ref readiness);
            var settledVelocity = RequiredNonnegative(p2.SettledVelocityToleranceMps,
                "settled velocity tolerance", gaps, ref readiness);
            var settledForce = RequiredNonnegative(p2.SettledForceToleranceN,
                "settled force tolerance", gaps, ref readiness);

            if (maxMass < minMass || maxDia < minDia || maxVelocity < minVelocity)
            {
                gaps.Add("impact applicability envelope is inverted");
            }
            if (Math.Abs(maxStepsRaw - Math.Round(maxStepsRaw)) > 1e-9 || maxStepsRaw > int.MaxValue)
            {
                gaps.Add("maximum integration steps must be a positive integer");
            }
            if (projectileMassKg < minMass || projectileMassKg > maxMass ||
                projectileDiameterM < minDia || projectileDiameterM > maxDia ||
                normalVelocityMps < minVelocity || normalVelocityMps > maxVelocity)
            {
                gaps.Add("runtime impact lies outside the authored mass/diameter/normal-speed envelope");
            }
            if (!Finite(projectileMassKg) || !Finite(projectileDiameterM) ||
                !Finite(normalVelocityMps) || !Finite(contactAreaM2) ||
                projectileMassKg <= 0 || projectileDiameterM <= 0 ||
                normalVelocityMps <= 0 || contactAreaM2 <= 0)
            {
                gaps.Add("runtime projectile mass, diameter, normal velocity and contact area must be positive");
            }
            if (gaps.Count > 0)
            {
                diagnostic = string.Join("; ", gaps);
                return false;
            }

            impact = new BabtModel.Impact
            {
                ProjectileMassKg = projectileMassKg,
                NormalVelocityMps = normalVelocityMps,
                ProjectileContactStiffnessNPerM = stiffness,
                ProjectileContactDampingNsPerM = damping,
                ContactAreaM2 = contactAreaM2,
                TimeStepS = dt,
                SimulationDurationS = duration,
                MaximumIntegrationSteps = (int)Math.Round(maxStepsRaw),
                MinimumStepsPerPeriod = stepsPerPeriod,
                MaximumRelativeEnergyError = energyError,
                SettledVelocityToleranceMps = settledVelocity,
                SettledForceToleranceN = settledForce,
                ApplicabilityMinNormalVelocityMps = minVelocity,
                ApplicabilityMaxNormalVelocityMps = maxVelocity,
                Perforates = perforates,
                Provenance = selected.Key + ": " + (p2.Source ?? "") +
                             "; contact K/C: " +
                             EvidenceSummary(p2.ProjectileContactStiffnessNPerM) +
                             " / " + EvidenceSummary(p2.ProjectileContactDampingNsPerM) +
                             "; numerical controls: " + EvidenceSummary(p2.TimeStepS) +
                             " / " + EvidenceSummary(p2.SimulationDurationS),
                Readiness = readiness,
            };
            diagnostic = "BABT impact/contact profile resolved inside its authored envelope.";
            return true;
        }

        internal static bool TryResolveBody(
            IDictionary<string, AmmoDataCache.BabtBodyProfile> profiles,
            string region, double effectiveAreaM2, double participatingDepthM,
            out BabtModel.BodyProfile body, out string diagnostic)
        {
            body = null;
            if (profiles == null || string.IsNullOrWhiteSpace(region) ||
                !profiles.TryGetValue(region, out var p) || p == null)
            {
                diagnostic = $"No BABT body profile is authored for region '{region ?? "missing"}'.";
                return false;
            }
            if (!PositiveFinite(effectiveAreaM2))
            {
                diagnostic = "Resolved construction contact area must be finite and positive.";
                return false;
            }

            var gaps = new List<string>();
            var readiness = BabtModel.Readiness.Provisional;
            if (string.IsNullOrWhiteSpace(p.Source)) gaps.Add("body/contact profile has no source");
            double mass;
            if (string.Equals(p.MassModel, "TissueSlab", StringComparison.OrdinalIgnoreCase))
            {
                var density = Required(p.TissueDensityKgM3, "tissue density", gaps, ref readiness);
                if (!PositiveFinite(participatingDepthM))
                    gaps.Add("tissue-slab participating depth is missing or non-finite");
                mass = density * effectiveAreaM2 * participatingDepthM;
            }
            else if (string.Equals(p.MassModel, "Fixed", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(p.MassModel))
            {
                mass = Required(p.EffectiveMassKg, "body effective mass", gaps, ref readiness);
            }
            else
            {
                mass = 0;
                gaps.Add($"body mass model '{p.MassModel}' is unsupported");
            }
            var contactK = Required(p.ContactStiffnessNPerM,
                "body contact stiffness", gaps, ref readiness);
            var contactC = RequiredNonnegative(p.ContactDampingNsPerM,
                "body contact damping", gaps, ref readiness);
            var foundationK = Required(p.FoundationStiffnessNPerM,
                "body foundation stiffness", gaps, ref readiness);
            var foundationC = RequiredNonnegative(p.FoundationDampingNsPerM,
                "body foundation damping", gaps, ref readiness);
            var gapMm = RequiredNonnegative(p.InitialGapMm,
                "initial plate/body gap", gaps, ref readiness);
            if (!PositiveFinite(mass)) gaps.Add("resolved body effective mass is not positive");
            if (gaps.Count > 0)
            {
                diagnostic = string.Join("; ", gaps);
                return false;
            }

            body = new BabtModel.BodyProfile
            {
                EffectiveMassKg = mass,
                ContactStiffnessNPerM = contactK,
                ContactDampingNsPerM = contactC,
                FoundationStiffnessNPerM = foundationK,
                FoundationDampingNsPerM = foundationC,
                InitialGapM = gapMm / 1000.0,
                EffectiveContactAreaM2 = effectiveAreaM2,
                Provenance = region + ": " + p.Source +
                             "; mass: " +
                             EvidenceSummary(string.Equals(p.MassModel, "TissueSlab",
                                 StringComparison.OrdinalIgnoreCase)
                                 ? p.TissueDensityKgM3
                                 : p.EffectiveMassKg) +
                             "; contact K/C: " + EvidenceSummary(p.ContactStiffnessNPerM) +
                             " / " + EvidenceSummary(p.ContactDampingNsPerM) +
                             "; foundation K/C: " + EvidenceSummary(p.FoundationStiffnessNPerM) +
                             " / " + EvidenceSummary(p.FoundationDampingNsPerM) +
                             "; initial gap: " + EvidenceSummary(p.InitialGapMm) +
                             $"; contact area={effectiveAreaM2:0.######} m2 from resolved construction" +
                             (string.Equals(p.MassModel, "TissueSlab", StringComparison.OrdinalIgnoreCase)
                                 ? $"; participating depth={participatingDepthM:0.####} m from the caller-supplied gameplay/body-wall profile, not measured anatomy"
                                 : ""),
                Readiness = BabtModel.Readiness.Provisional,
            };
            diagnostic = "BABT body profile resolved as an explicitly sourced provisional engineering reduction.";
            return true;
        }

        internal static bool TryResolveBody(
            IDictionary<string, AmmoDataCache.BabtBodyProfile> profiles,
            string region, string constructionKey, string padKey,
            out BabtModel.BodyProfile body, out string diagnostic)
        {
            body = null;
            if (profiles == null || string.IsNullOrWhiteSpace(region) ||
                !profiles.TryGetValue(region, out var p) || p == null)
            {
                diagnostic = $"No BABT body profile is authored for region '{region ?? "missing"}'.";
                return false;
            }
            if (!Contains(p.ConstructionKeys, constructionKey))
            {
                diagnostic = "The body profile was not validated with this exact armour construction.";
                return false;
            }
            if (!Contains(p.PadKeys, padKey))
            {
                diagnostic = "The body profile was not validated with this exact pad/liner stack.";
                return false;
            }

            var gaps = new List<string>();
            var readiness = BabtModel.Readiness.Calibrated;
            if (string.IsNullOrWhiteSpace(p.Source)) gaps.Add("body/contact profile has no source");
            var mass = Required(p.EffectiveMassKg, "body effective mass", gaps, ref readiness);
            var contactK = Required(p.ContactStiffnessNPerM, "body contact stiffness", gaps, ref readiness);
            var contactC = RequiredNonnegative(p.ContactDampingNsPerM,
                "body contact damping", gaps, ref readiness);
            var foundationK = Required(p.FoundationStiffnessNPerM, "body foundation stiffness", gaps, ref readiness);
            var foundationC = RequiredNonnegative(p.FoundationDampingNsPerM,
                "body foundation damping", gaps, ref readiness);
            var gapMm = RequiredNonnegative(p.InitialGapMm, "initial plate/body gap", gaps, ref readiness);
            var area = Required(p.EffectiveContactAreaM2, "effective body contact area", gaps, ref readiness);
            if (gaps.Count > 0)
            {
                diagnostic = string.Join("; ", gaps);
                return false;
            }

            body = new BabtModel.BodyProfile
            {
                EffectiveMassKg = mass,
                ContactStiffnessNPerM = contactK,
                ContactDampingNsPerM = contactC,
                FoundationStiffnessNPerM = foundationK,
                FoundationDampingNsPerM = foundationC,
                InitialGapM = gapMm / 1000.0,
                EffectiveContactAreaM2 = area,
                Provenance = region + ": " + (p.Source ?? ""),
                Readiness = readiness,
            };
            diagnostic = "BABT body/contact profile resolved for the exact construction and pad stack.";
            return true;
        }

        private static bool Contains(string[] values, string sought)
        {
            if (values == null || string.IsNullOrWhiteSpace(sought)) return false;
            for (var i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], sought, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool EnvelopeContains(AmmoDataCache.BabtImpactProfile p,
            double massKg, double diameterM, double velocityMps, out string diagnostic)
        {
            if (!PlainPositive(p.MinProjectileMassG, out var minMass) ||
                !PlainPositive(p.MaxProjectileMassG, out var maxMass) ||
                !PlainPositive(p.MinProjectileDiameterMm, out var minDia) ||
                !PlainPositive(p.MaxProjectileDiameterMm, out var maxDia) ||
                !PlainPositive(p.MinNormalVelocityMps, out var minVelocity) ||
                !PlainPositive(p.MaxNormalVelocityMps, out var maxVelocity))
            {
                diagnostic = "applicability envelope has a missing, non-finite or unsourced bound";
                return false;
            }
            if (maxMass < minMass || maxDia < minDia || maxVelocity < minVelocity)
            {
                diagnostic = "applicability envelope is inverted";
                return false;
            }
            var inside = massKg >= minMass / 1000.0 && massKg <= maxMass / 1000.0 &&
                         diameterM >= minDia / 1000.0 && diameterM <= maxDia / 1000.0 &&
                         velocityMps >= minVelocity && velocityMps <= maxVelocity;
            diagnostic = inside ? "inside" : "outside mass/diameter/normal-speed bounds";
            return inside;
        }

        private static bool PlainPositive(AmmoDataCache.BabtParameter p, out double value)
        {
            value = p?.Value ?? 0;
            if (!Finite(value) || value <= 0 || string.IsNullOrWhiteSpace(p?.Source)) return false;
            return string.Equals(p.Status, "Measured", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(p.Status, "Estimated", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPositive(AmmoDataCache.BabtParameter p) => p?.Value is > 0;

        private static double RequiredNonnegative(AmmoDataCache.BabtParameter p, string name,
            ICollection<string> gaps, ref BabtModel.Readiness readiness)
        {
            if (p?.Value == null || !Finite(p.Value.Value) || p.Value < 0 ||
                !Evidence(p, ref readiness))
            {
                gaps.Add(Gap(name, p));
                return 0;
            }
            return p.Value.Value;
        }

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool Evidence(AmmoDataCache.BabtParameter p,
            ref BabtModel.Readiness readiness)
        {
            if (string.Equals(p.Status, "Measured", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(p.Source);
            }
            if (string.Equals(p.Status, "Estimated", StringComparison.OrdinalIgnoreCase))
            {
                readiness = BabtModel.Readiness.Provisional;
                return !string.IsNullOrWhiteSpace(p.Source);
            }
            return false;
        }

        private static string Gap(string name, AmmoDataCache.BabtParameter p)
        {
            if (!string.IsNullOrWhiteSpace(p?.Reason))
            {
                return name + ": " + p.Reason;
            }
            if (p?.Value != null && string.IsNullOrWhiteSpace(p.Source))
            {
                return name + ": value has no source";
            }
            return name + ": missing measured or estimated value";
        }

        private static string Provenance(string name, string source, string notes)
        {
            var result = string.IsNullOrWhiteSpace(name) ? "unnamed profile" : name;
            if (!string.IsNullOrWhiteSpace(source)) result += ": " + source;
            if (!string.IsNullOrWhiteSpace(notes)) result += " (" + notes + ")";
            return result;
        }
    }
}
