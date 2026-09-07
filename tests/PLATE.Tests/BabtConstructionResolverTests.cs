using System.Collections.Generic;
using PLATE.Client.Ballistics;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Tests
{
    public class BabtConstructionResolverTests
    {
        [Fact]
        public void Missing_or_unknown_wire_schema_cannot_enter_the_babt_resolvers()
        {
            Assert.False(AmmoDataCache.SupportsBabtSchema(null));
            Assert.False(AmmoDataCache.SupportsBabtSchema(new AmmoDataCache.ArmorGeometry()));
            Assert.False(AmmoDataCache.SupportsBabtSchema(
                new AmmoDataCache.ArmorGeometry { BabtSchemaVersion = 2 }));
            Assert.True(AmmoDataCache.SupportsBabtSchema(
                new AmmoDataCache.ArmorGeometry { BabtSchemaVersion = 1 }));
        }

        [Fact]
        public void A_complete_metal_profile_maps_to_the_shared_solver_without_class_inference()
        {
            var profile = CompleteMetal();
            var envelope = new AmmoDataCache.BabtConstructionEnvelope
            {
                ProfileKey = "test-steel",
                Profile = profile,
            };

            var ok = BabtConstructionResolver.TryResolve(envelope, out var result, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.Equal(BabtModel.Readiness.Provisional, result.Readiness);
            Assert.Equal(0.00635, result.Layers[0].ThicknessM, 8);
            Assert.Equal(BabtModel.LayerForm.MetalPlate, result.Layers[0].Form);
            Assert.Contains("test-steel", result.Provenance);
        }

        [Fact]
        public void Missing_effective_mass_is_reported_instead_of_using_whole_plate_mass()
        {
            var profile = CompleteMetal();
            profile.LocalEffectiveMassKg = Missing("requires a contact-wave reduction");

            var ok = BabtConstructionResolver.TryResolve(
                new AmmoDataCache.BabtConstructionEnvelope { ProfileKey = "steel", Profile = profile },
                out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("local effective mass", diagnostic);
            Assert.Contains("contact-wave reduction", diagnostic);
        }

        [Fact]
        public void Contact_profile_is_rejected_outside_its_authored_projectile_envelope()
        {
            var profiles = new Dictionary<string, AmmoDataCache.BabtImpactProfile>
            {
                ["9mm-test"] = CompleteImpact(),
            };

            var ok = BabtConstructionResolver.TryResolveImpact(profiles, "test-steel", "ammo-9mm",
                0.008, 0.009, 700, 0.000064, false, out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("outside", diagnostic);
        }

        [Fact]
        public void Zero_contact_damping_is_valid_when_it_is_explicitly_sourced()
        {
            var p = CompleteImpact();
            p.ProjectileContactDampingNsPerM = Measured(0);
            var profiles = new Dictionary<string, AmmoDataCache.BabtImpactProfile> { ["dry"] = p };

            var ok = BabtConstructionResolver.TryResolveImpact(profiles, "test-steel", "ammo-9mm",
                0.008, 0.009, 350, 0.000064, false, out var impact, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.Equal(0, impact.ProjectileContactDampingNsPerM);
        }

        [Fact]
        public void Non_finite_applicability_bounds_are_rejected()
        {
            var p = CompleteImpact();
            p.MinProjectileMassG = Measured(double.NaN);
            var profiles = new Dictionary<string, AmmoDataCache.BabtImpactProfile> { ["bad"] = p };

            var ok = BabtConstructionResolver.TryResolveImpact(profiles, "test-steel", "ammo-9mm",
                0.008, 0.009, 350, 0.000064, false, out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("non-finite", diagnostic);
        }

        [Fact]
        public void Contact_profile_does_not_fall_back_across_ammunition()
        {
            var profiles = new Dictionary<string, AmmoDataCache.BabtImpactProfile>
                { ["9mm"] = CompleteImpact() };

            var ok = BabtConstructionResolver.TryResolveImpact(profiles, "test-steel", "ammo-50bmg",
                0.045, 0.0127, 850, 0.000127, false, out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("exact construction and ammunition", diagnostic);
        }

        [Fact]
        public void Body_profile_requires_the_exact_construction_and_pad_stack()
        {
            var profiles = new Dictionary<string, AmmoDataCache.BabtBodyProfile>
            {
                ["Thorax"] = CompleteBody(),
            };

            var wrong = BabtConstructionResolver.TryResolveBody(profiles, "Thorax", "test-steel",
                "different-pad", out _, out var diagnostic);
            Assert.False(wrong);
            Assert.Contains("pad/liner", diagnostic);

            var ok = BabtConstructionResolver.TryResolveBody(profiles, "Thorax", "test-steel",
                "test-pad", out var body, out diagnostic);
            Assert.True(ok, diagnostic);
            Assert.Equal(0.002, body.InitialGapM, 8);
        }

        [Fact]
        public void Material_bridge_builds_a_gong_capable_metal_plate_from_barrier_data()
        {
            var plate = SteelPlate();
            var materials = Materials();
            var damage = IntactDamage();

            var ok = BabtConstructionResolver.TryResolveCoupling(plate, null, materials,
                damage, out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            var construction = coupling.PostImpactConstruction;
            Assert.Equal(BabtModel.LayerForm.MetalPlate, construction.Layers[0].Form);
            Assert.Equal(0.00635, construction.Layers[0].ThicknessM, 8);
            Assert.Equal(210e9, construction.Layers[0].YoungModulusPa, 1);
            Assert.True(construction.FlexuralDampingNsPerM > 0);
            Assert.Equal(7850 * 0.00635 * 0.254 * 0.318,
                construction.LocalEffectiveMassKg + construction.RigidEffectiveMassKg, 8);
            Assert.Contains("M_participating/2", construction.Provenance);
        }

        [Fact]
        public void Pierce_keeps_physical_mass_while_net_section_reduces_stiffness_once()
        {
            var plate = SteelPlate();
            var materials = Materials();
            var intact = IntactDamage();
            Assert.True(BabtConstructionResolver.TryResolveCoupling(plate, null, materials,
                intact, out var before, out _));
            var pierced = IntactDamage();
            pierced.Perforated = true;
            pierced.HoleAreaM2 = 8e-5;
            pierced.FaceCoherentFraction = 0.8;
            pierced.OutgoingInventoryComplete = false;
            pierced.Provenance = "synthetic post-pierce net section";

            var ok = BabtConstructionResolver.TryResolveCoupling(plate, null, materials,
                pierced, out var after, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.Equal(0.00635, after.PostImpactConstruction.Layers[0].ThicknessM, 8);
            Assert.Equal(0.8, after.PostImpactConstruction.Layers[0].CoherentFraction, 8);
            Assert.Equal(
                before.PostImpactConstruction.LocalEffectiveMassKg +
                before.PostImpactConstruction.RigidEffectiveMassKg,
                after.PostImpactConstruction.LocalEffectiveMassKg +
                after.PostImpactConstruction.RigidEffectiveMassKg, 8);
            Assert.False(after.OutgoingInventoryComplete);
        }

        [Fact]
        public void Resolver_derives_post_pierce_net_section_from_its_own_geometry()
        {
            var damage = IntactDamage();
            damage.Perforated = true;
            damage.HoleAreaM2 = 0.01;
            damage.CoherentFractionsKnown = false;

            var ok = BabtConstructionResolver.TryResolveCoupling(SteelPlate(), null,
                Materials(), damage, out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.Equal(1 - 0.01 / (0.254 * 0.318),
                coupling.PostImpactConstruction.Layers[0].CoherentFraction, 8);
        }

        [Fact]
        public void Only_explicit_ejecta_and_retained_projectile_change_participating_mass()
        {
            var damage = IntactDamage();
            damage.KnownOutgoingEjectedMassKg = 0.04;
            damage.RetainedProjectileMassKg = 0.008;

            var ok = BabtConstructionResolver.TryResolveCoupling(SteelPlate(), null,
                Materials(), damage, out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            var pristine = 7850 * 0.00635 * 0.254 * 0.318;
            Assert.Equal(pristine - 0.04 + 0.008,
                coupling.PostImpactConstruction.LocalEffectiveMassKg +
                coupling.PostImpactConstruction.RigidEffectiveMassKg, 8);
        }

        [Fact]
        public void Stopped_worn_plate_keeps_mass_but_encodes_lower_bending_stiffness()
        {
            var pristineDamage = IntactDamage();
            Assert.True(BabtConstructionResolver.TryResolveCoupling(SteelPlate(), null,
                Materials(), pristineDamage, out var pristine, out _));
            var wornDamage = IntactDamage();
            wornDamage.FaceRetainedThicknessFraction = 0.5;
            wornDamage.Provenance = "synthetic local retained-thickness state";

            var ok = BabtConstructionResolver.TryResolveCoupling(SteelPlate(), null,
                Materials(), wornDamage, out var worn, out var diagnostic);

            Assert.True(ok, diagnostic);
            var pristineLayer = pristine.PostImpactConstruction.Layers[0];
            var wornLayer = worn.PostImpactConstruction.Layers[0];
            Assert.Equal(1, wornLayer.CoherentFraction, 8);
            Assert.Equal(0.5, wornLayer.EffectiveThicknessFraction, 8);
            Assert.Equal(0.125, EffectivePlateRigidity(wornLayer) /
                EffectivePlateRigidity(pristineLayer), 8);
            Assert.Equal(
                pristine.PostImpactConstruction.LocalEffectiveMassKg +
                pristine.PostImpactConstruction.RigidEffectiveMassKg,
                worn.PostImpactConstruction.LocalEffectiveMassKg +
                worn.PostImpactConstruction.RigidEffectiveMassKg, 8);
        }

        [Fact]
        public void Pierced_brittle_face_is_removed_but_its_intrinsic_backing_survives()
        {
            var plate = SteelPlate();
            plate.M = "Ceramic";
            plate.T = 10;
            plate.B = 12;
            plate.BM = "UHMWPE";
            plate.BP = 1;
            plate.BabtForm = "BrittleFace";
            plate.BabtBackingForm = "BondedLaminate";
            var damage = IntactDamage();
            damage.Perforated = true;
            damage.HoleAreaM2 = 6e-5;
            damage.FaceCoherentFraction = 0;
            damage.BackingCoherentFraction = 0.9;
            damage.BrittleFaceFailed = true;
            damage.Provenance = "synthetic fractured face and surviving backing";

            var ok = BabtConstructionResolver.TryResolveCoupling(plate, null, Materials(),
                damage, out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.Single(coupling.PostImpactConstruction.Layers);
            Assert.Equal(BabtModel.LayerForm.RigidLaminate,
                coupling.PostImpactConstruction.Layers[0].Form);
            Assert.Equal(0.9, coupling.PostImpactConstruction.Layers[0].CoherentFraction, 8);
        }

        [Fact]
        public void Integrated_soft_package_uses_strength_strain_and_packing_not_yarn_modulus()
        {
            var plate = SteelPlate();
            plate.M = "Aramid";
            plate.T = 7.6;
            plate.P = 0.44;
            plate.BabtForm = "SoftWoven";

            var ok = BabtConstructionResolver.TryResolveCoupling(plate, null, Materials(),
                IntactDamage(), out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            var layer = Assert.Single(coupling.PostImpactConstruction.Layers);
            Assert.Equal(BabtModel.LayerForm.SoftFabric, layer.Form);
            var expected = 2900e6 / 0.034 * 0.44 * 0.5 * 0.0076;
            Assert.Equal(expected, layer.MembraneStiffnessNPerM, 3);
            Assert.Contains("no yarn modulus", layer.Provenance);
        }

        [Fact]
        public void Exact_support_components_are_added_as_unbonded_shared_mode_layers()
        {
            var blocker = SteelPlate();
            blocker.B = 5;
            blocker.BM = "Aramid";
            blocker.BP = 0.44;
            blocker.BabtBackingForm = "SoftWoven";
            var support = SteelPlate();
            support.M = "Aramid";
            support.T = 7.6;
            support.P = 0.44;
            support.BabtForm = "SoftWoven";

            var ok = BabtConstructionResolver.TryResolveCoupling(blocker,
                new[]
                {
                    new BabtConstructionResolver.ResolvedSupportingComponent
                    {
                        Geometry = support,
                        Damage = IntactDamage(),
                    },
                }, Materials(), IntactDamage(),
                out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.Equal(3, coupling.PostImpactConstruction.Layers.Length);
            Assert.False(coupling.PostImpactConstruction.Layers[2].BondedToPrevious);
        }

        [Fact]
        public void Directly_hit_brittle_support_uses_its_own_resolved_failure_state()
        {
            var support = SteelPlate();
            support.M = "Ceramic";
            support.BabtForm = "BrittleFace";
            support.B = 7.6;
            support.BM = "Aramid";
            support.BP = 0.44;
            support.BabtBackingForm = "SoftWoven";
            var supportDamage = IntactDamage();
            supportDamage.Perforated = true;
            supportDamage.HoleAreaM2 = 5e-5;
            supportDamage.CoherentFractionsKnown = false;
            supportDamage.BrittleFaceFailed = true;

            var ok = BabtConstructionResolver.TryResolveCoupling(SteelPlate(),
                new[]
                {
                    new BabtConstructionResolver.ResolvedSupportingComponent
                        { Geometry = support, Damage = supportDamage },
                }, Materials(), IntactDamage(), out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.DoesNotContain(coupling.PostImpactConstruction.Layers,
                l => l.Form == BabtModel.LayerForm.BrittleFace);
            Assert.Contains(coupling.PostImpactConstruction.Layers,
                l => l.Name.Contains("backing Aramid"));
        }

        [Fact]
        public void Support_without_a_resolved_local_state_is_rejected()
        {
            var supportDamage = IntactDamage();
            supportDamage.LocalStateResolved = false;

            var ok = BabtConstructionResolver.TryResolveCoupling(SteelPlate(),
                new[]
                {
                    new BabtConstructionResolver.ResolvedSupportingComponent
                        { Geometry = SteelPlate(), Damage = supportDamage },
                }, Materials(), IntactDamage(), out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("no resolved local hit state", diagnostic);
        }

        [Fact]
        public void Resolved_support_damage_reduces_that_support_without_removing_its_mass()
        {
            var supportDamage = IntactDamage();
            supportDamage.FaceRetainedThicknessFraction = 0.5;
            var support = SteelPlate();
            support.M = "Aramid";
            support.T = 7.6;
            support.P = 0.44;
            support.BabtForm = "SoftWoven";

            var ok = BabtConstructionResolver.TryResolveCoupling(SteelPlate(),
                new[]
                {
                    new BabtConstructionResolver.ResolvedSupportingComponent
                        { Geometry = support, Damage = supportDamage },
                }, Materials(), IntactDamage(), out var coupling, out var diagnostic);

            Assert.True(ok, diagnostic);
            Assert.Equal(1, coupling.PostImpactConstruction.Layers[1].CoherentFraction, 8);
            Assert.Equal(0.5,
                coupling.PostImpactConstruction.Layers[1].EffectiveThicknessFraction, 8);
            var expectedMass = 7850 * 0.00635 * 0.254 * 0.318 +
                               1440 * 0.44 * 0.0076 * 0.254 * 0.318;
            Assert.Equal(expectedMass,
                coupling.PostImpactConstruction.LocalEffectiveMassKg +
                coupling.PostImpactConstruction.RigidEffectiveMassKg, 8);
        }

        [Fact]
        public void Numerical_policy_is_explicit_and_rejects_an_impossible_workload()
        {
            var profile = EngineeringNumerics();
            Assert.True(BabtConstructionResolver.TryResolveNumerics(profile,
                out var numerics, out var diagnostic), diagnostic);
            Assert.Equal(2500, numerics.SimulationDurationS / numerics.TimeStepS, 6);
            Assert.Equal(160, numerics.MinimumStepsPerPeriod);
            Assert.Equal(BabtModel.Readiness.Provisional, numerics.Readiness);

            profile.MaximumIntegrationSteps = Estimated(1000);
            Assert.False(BabtConstructionResolver.TryResolveNumerics(profile,
                out _, out diagnostic));
            Assert.Contains("workload ceiling", diagnostic);
        }

        [Fact]
        public void Malformed_mechanical_wire_is_rejected_without_affecting_barrier_fields()
        {
            var plate = SteelPlate();
            var materials = Materials();
            materials["ArmoredSteel"].YoungModulusGPa = null;

            var ok = BabtConstructionResolver.TryResolveCoupling(plate, null, materials,
                IntactDamage(), out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("elastic properties", diagnostic);
            Assert.Equal(6.35, plate.T);
            Assert.Equal(1250, materials["ArmoredSteel"].YieldMPa);
        }

        [Fact]
        public void Non_finite_retained_thickness_state_is_rejected()
        {
            var damage = IntactDamage();
            damage.FaceRetainedThicknessFraction = double.NaN;

            var ok = BabtConstructionResolver.TryResolveCoupling(SteelPlate(), null,
                Materials(), damage, out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("retained-thickness", diagnostic);
        }

        [Fact]
        public void Malformed_negative_construction_values_do_not_become_legacy_defaults()
        {
            var plate = SteelPlate();
            plate.P = -0.1;
            Assert.False(BabtConstructionResolver.TryResolveCoupling(plate, null,
                Materials(), IntactDamage(), out _, out var diagnostic));
            Assert.Contains("outside [0, 1]", diagnostic);

            plate = SteelPlate();
            plate.B = -1;
            Assert.False(BabtConstructionResolver.TryResolveCoupling(plate, null,
                Materials(), IntactDamage(), out _, out diagnostic));
            Assert.Contains("backing thickness", diagnostic);

            plate = SteelPlate();
            plate.B = 5;
            plate.BM = "Aramid";
            plate.BabtBackingForm = "SoftWoven";
            plate.BP = -0.1;
            Assert.False(BabtConstructionResolver.TryResolveCoupling(plate, null,
                Materials(), IntactDamage(), out _, out diagnostic));
            Assert.Contains("Backing packing fraction", diagnostic);

            plate = SteelPlate();
            plate.Y = double.NaN;
            Assert.False(BabtConstructionResolver.TryResolveCoupling(plate, null,
                Materials(), IntactDamage(), out _, out diagnostic));
            Assert.Contains("yield-strength override", diagnostic);
        }

        [Fact]
        public void Thorax_and_abdomen_use_distinct_sourced_mass_reductions()
        {
            var profiles = new Dictionary<string, AmmoDataCache.BabtBodyProfile>
            {
                ["Thorax"] = EngineeringThorax(),
                ["Abdomen"] = EngineeringAbdomen(),
            };

            Assert.True(BabtConstructionResolver.TryResolveBody(profiles, "Thorax",
                0.02, 0, out var thorax, out var diagnostic), diagnostic);
            Assert.Equal(0.45, thorax.EffectiveMassKg);
            Assert.Equal(26300, thorax.FoundationStiffnessNPerM);
            Assert.Equal(525, thorax.FoundationDampingNsPerM);

            Assert.True(BabtConstructionResolver.TryResolveBody(profiles, "Abdomen",
                0.02, 0.08, out var abdomen, out diagnostic), diagnostic);
            Assert.Equal(1.6, abdomen.EffectiveMassKg, 8);
            Assert.Equal(12900, abdomen.FoundationStiffnessNPerM);
            Assert.Contains("participating depth", abdomen.Provenance);
            Assert.Contains("caller-supplied gameplay/body-wall", abdomen.Provenance);
            Assert.Contains("not measured anatomy", abdomen.Provenance);
        }

        [Fact]
        public void Head_does_not_borrow_the_thorax_impedance()
        {
            var profiles = new Dictionary<string, AmmoDataCache.BabtBodyProfile>
                { ["Thorax"] = EngineeringThorax() };

            var ok = BabtConstructionResolver.TryResolveBody(profiles, "Head",
                0.02, 0.08, out _, out var diagnostic);

            Assert.False(ok);
            Assert.Contains("Head", diagnostic);
        }

        private static AmmoDataCache.BabtConstructionProfile CompleteMetal() => new()
        {
            Kind = "MetalPlate",
            Support = "SimplySupported",
            PadKey = "test-pad",
            WidthMm = Measured(254),
            HeightMm = Measured(305),
            CurvatureRadiusMm = Measured(0),
            LocalEffectiveMassKg = Measured(0.2),
            RigidEffectiveMassKg = Measured(2.8),
            FlexuralDampingNsPerM = Estimated(20),
            Layers = new List<AmmoDataCache.BabtLayerProfile>
            {
                new()
                {
                    Form = "IsotropicPlate",
                    Material = "ArmoredSteel",
                    ThicknessMm = Measured(6.35),
                    DensityKgM3 = Measured(7850),
                    YoungModulusGPa = Measured(200),
                    PoissonRatio = Measured(0.30),
                    YieldStrengthMPa = Measured(1250),
                    FailureStrain = Measured(0.08),
                    Source = "test layer",
                },
            },
            Source = "synthetic mechanical fixture",
        };

        private static AmmoDataCache.BabtImpactProfile CompleteImpact() => new()
        {
            ConstructionKeys = new[] { "test-steel" },
            AmmoKeys = new[] { "ammo-9mm" },
            MinProjectileMassG = Measured(7),
            MaxProjectileMassG = Measured(9),
            MinProjectileDiameterMm = Measured(8.5),
            MaxProjectileDiameterMm = Measured(9.5),
            MinNormalVelocityMps = Measured(300),
            MaxNormalVelocityMps = Measured(450),
            ProjectileContactStiffnessNPerM = Measured(1e7),
            ProjectileContactDampingNsPerM = Measured(100),
            TimeStepS = Measured(1e-6),
            SimulationDurationS = Measured(0.02),
            MaximumIntegrationSteps = Measured(30000),
            MinimumStepsPerPeriod = Measured(30),
            MaximumRelativeEnergyError = Measured(0.01),
            SettledVelocityToleranceMps = Measured(0.01),
            SettledForceToleranceN = Measured(1),
            Source = "synthetic mechanical fixture",
        };

        private static AmmoDataCache.BabtBodyProfile CompleteBody() => new()
        {
            ConstructionKeys = new[] { "test-steel" },
            PadKeys = new[] { "test-pad" },
            EffectiveMassKg = Measured(12),
            ContactStiffnessNPerM = Measured(2e5),
            ContactDampingNsPerM = Measured(500),
            FoundationStiffnessNPerM = Measured(1e4),
            FoundationDampingNsPerM = Measured(100),
            InitialGapMm = Measured(2),
            EffectiveContactAreaM2 = Measured(0.01),
            Source = "synthetic mechanical fixture",
        };

        private static AmmoDataCache.BabtParameter Measured(double value) => new()
            { Value = value, Status = "Measured", Source = "synthetic fixture" };

        private static AmmoDataCache.BabtParameter Estimated(double value) => new()
            { Value = value, Status = "Estimated", Source = "synthetic estimate" };

        private static AmmoDataCache.BabtParameter Missing(string reason) => new()
            { Status = "Missing", Reason = reason };

        private static AmmoDataCache.PlateGeometry SteelPlate() => new()
        {
            T = 6.35,
            M = "ArmoredSteel",
            P = 1,
            Y = 1250,
            BabtForm = "IsotropicPlate",
            BabtWidthMm = 254,
            BabtHeightMm = 318,
            BabtGeometryStatus = "Estimated",
            BabtGeometrySource = "synthetic plate footprint",
            ConstructionOrigin = "Product",
            ConstructionSource = "synthetic established penetration construction",
            ItemKind = "Plate",
        };

        private static Dictionary<string, AmmoDataCache.MaterialPhysics> Materials() => new()
        {
            ["ArmoredSteel"] = new AmmoDataCache.MaterialPhysics
            {
                Class = "Ductile", DensityGCm3 = 7.85, YieldMPa = 1250,
                FailureStrain = 0.08, Source = "synthetic steel",
                YoungModulusGPa = Estimated(210), PoissonRatio = Estimated(0.30),
                StructuralDampingRatio = Estimated(0.005),
            },
            ["Ceramic"] = new AmmoDataCache.MaterialPhysics
            {
                Class = "Brittle", DensityGCm3 = 3.9, FailureStrain = 0.00081,
                Source = "synthetic alumina", YoungModulusGPa = Estimated(370),
                PoissonRatio = Estimated(0.22), StructuralDampingRatio = Estimated(0.01),
            },
            ["Aramid"] = new AmmoDataCache.MaterialPhysics
            {
                Class = "Fibrous", DensityGCm3 = 1.44, FibreTensileMPa = 2900,
                FailureStrain = 0.034, Source = "synthetic aramid package",
                RigidLaminateModulusGPa = Estimated(10.06),
                RigidLaminatePoissonRatio = Estimated(0.25),
                StructuralDampingRatio = Estimated(0.02),
            },
            ["UHMWPE"] = new AmmoDataCache.MaterialPhysics
            {
                Class = "Fibrous", DensityGCm3 = 0.97, FibreTensileMPa = 3400,
                FailureStrain = 0.035, Source = "synthetic UHMWPE laminate",
                RigidLaminateModulusGPa = Estimated(45.2),
                RigidLaminatePoissonRatio = Estimated(0.013),
                StructuralDampingRatio = Estimated(0.02),
            },
        };

        private static BabtConstructionResolver.ResolvedImpactDamage IntactDamage() => new()
        {
            FaceCoherentFraction = 1,
            BackingCoherentFraction = 1,
            CoherentFractionsKnown = true,
            LocalStateResolved = true,
            OutgoingInventoryComplete = true,
            Provenance = "synthetic intact state",
        };

        private static double EffectivePlateRigidity(BabtModel.Layer layer)
        {
            var f = layer.EffectiveThicknessFraction;
            return layer.YoungModulusPa * layer.ThicknessM * layer.ThicknessM *
                   layer.ThicknessM * layer.CoherentFraction * f * f * f /
                   (12 * (1 - layer.PoissonRatio * layer.PoissonRatio));
        }

        private static AmmoDataCache.BabtBodyProfile EngineeringThorax() => new()
        {
            MassModel = "Fixed",
            EffectiveMassKg = Estimated(0.45),
            ContactStiffnessNPerM = Estimated(2630000),
            ContactDampingNsPerM = Estimated(0),
            FoundationStiffnessNPerM = Estimated(26300),
            FoundationDampingNsPerM = Estimated(525),
            InitialGapMm = Estimated(0),
            Source = "synthetic Lobdell reduction",
        };

        private static AmmoDataCache.BabtBodyProfile EngineeringAbdomen() => new()
        {
            MassModel = "TissueSlab",
            TissueDensityKgM3 = Estimated(1000),
            ContactStiffnessNPerM = Estimated(1290000),
            ContactDampingNsPerM = Estimated(0),
            FoundationStiffnessNPerM = Estimated(12900),
            FoundationDampingNsPerM = Estimated(765),
            InitialGapMm = Estimated(0),
            Source = "synthetic Trosseille reduction",
        };

        private static AmmoDataCache.BabtNumericsProfile EngineeringNumerics() => new()
        {
            TimeStepS = Estimated(2e-5),
            SimulationDurationS = Estimated(0.05),
            MaximumIntegrationSteps = Estimated(100000),
            MinimumStepsPerPeriod = Estimated(160),
            MaximumRelativeEnergyError = Estimated(0.01),
            SettledVelocityToleranceMps = Estimated(0.01),
            SettledForceToleranceN = Estimated(1),
            Source = "synthetic numerical verification policy",
        };
    }
}
