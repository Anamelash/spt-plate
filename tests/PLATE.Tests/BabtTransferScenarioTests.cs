using System;
using System.Collections.Generic;
using System.Diagnostics;
using PLATE.Client.Ballistics;
using PLATE.Server.Services;
using Xunit;
using Xunit.Abstractions;

namespace PLATE.Tests
{
    /// <summary>
    /// Crosses the production resolver reductions with the resolved-impact solver.
    /// Values mirror the shipped material/body references; outcomes are prescribed
    /// terminal-ballistics results and do not re-test penetration.
    /// </summary>
    public class BabtTransferScenarioTests
    {
        private readonly ITestOutputHelper _output;

        public BabtTransferScenarioTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Production_reductions_cover_stop_pierce_and_material_matrix()
        {
            var steel9 = Run(SteelPlate(6.35), Stop(0.008, 300),
                Intact(0.008), "Thorax");
            var steel762 = Run(SteelPlate(9.0), Stop(0.008, 720),
                Intact(0.008), "Thorax");
            var steel50 = Run(SteelPlate(12.0), Stop(0.048, 810),
                Intact(0.048), "Thorax");
            var steel50Pierce = Run(SteelPlate(12.0),
                Pierce(0.048, 810, 0.048, 700, 0.003, 700),
                Pierced(0.003, 0, 0.999), "Thorax");
            var uhmwpe = Run(UhmwpePlate(), Stop(0.008, 720),
                Intact(0.008), "Thorax");
            var aramid = Run(AramidPanel(), Stop(0.008, 300),
                Intact(0.008), "Thorax");
            var ceramicStop762Thorax = Run(CeramicBackedPlate(), Stop(0.010, 720),
                Intact(0.010), "Thorax");
            var ceramicStop762Abdomen = Run(CeramicBackedPlate(), Stop(0.010, 720),
                Intact(0.010), "Abdomen");
            var ceramicStop50Thorax = Run(CeramicBackedPlate(), Stop(0.048, 780),
                Intact(0.048), "Thorax");
            var ceramicStop50Abdomen = Run(CeramicBackedPlate(), Stop(0.048, 780),
                Intact(0.048), "Abdomen");
            var ceramicPierce = Run(CeramicBackedPlate(),
                Pierce(0.010, 830, 0.006, 620, 0.002, 620),
                Pierced(0.002, 0.004, 0, 0.90, true), "Thorax");
            var ceramicAbdomen50 = Run(CeramicBackedPlate(),
                Pierce(0.048, 810, 0.030, 600, 0.005, 600),
                Pierced(0.005, 0.018, 0, 0.85, true), "Abdomen");

            var all = new[] { steel9, steel762, steel50, steel50Pierce,
                uhmwpe, aramid, ceramicStop762Thorax, ceramicStop762Abdomen,
                ceramicStop50Thorax, ceramicStop50Abdomen,
                ceramicPierce, ceramicAbdomen50 };
            foreach (var scenario in all)
            {
                var state = scenario.Describe();
                _output.WriteLine(state);
                Assert.True(scenario.Transfer.IsValid, state);
                Assert.Equal(BabtTransferModel.EvaluationStatus.Complete,
                    scenario.Transfer.Status);
                Assert.True(scenario.Response.IsValid, state);
                Assert.True(BabtModel.EvaluationStatus.Complete == scenario.Response.Status,
                    state);
                Assert.True(scenario.Response.BodyWorkConverged, state);
                Assert.True(scenario.Response.BodyNetWorkJ >= 0, state);
                Assert.True(scenario.Response.BodyNetWorkJ <=
                            scenario.Transfer.MechanicalSeedEnergyJ * 1.011);
                Assert.True(scenario.Response.IntegrationSteps >= 1 &&
                            scenario.Response.IntegrationSteps <= 20000, state);
                Assert.Equal(0.05, scenario.Response.SimulatedDurationS, 9);
                Assert.True(scenario.Closed.IsValid, scenario.Closed.Reason);
                Assert.InRange(Math.Abs(scenario.Closed.EnergyBalanceErrorJ), 0,
                    scenario.Transfer.MechanicalSeedEnergyJ * 0.011);
            }

            Assert.True(steel9.Response.BodyNetWorkJ < steel762.Response.BodyNetWorkJ);
            Assert.True(steel762.Response.BodyNetWorkJ < steel50.Response.BodyNetWorkJ);
            Assert.True(steel9.Response.BodyNetWorkJ < aramid.Response.BodyNetWorkJ);
            Assert.True(ceramicPierce.Response.BodyNetWorkJ > 0);
            Assert.True(ceramicAbdomen50.Response.BodyNetWorkJ > 0);
            Assert.True(ceramicStop50Thorax.Response.ResolvedBrittleDropoutCount > 0);
            Assert.True(ceramicStop50Abdomen.Response.ResolvedBrittleDropoutCount > 0);
            Assert.True(ceramicStop50Thorax.Response.BodyNetWorkJ >
                        ceramicStop762Thorax.Response.BodyNetWorkJ);
            Assert.True(ceramicStop50Abdomen.Response.BodyNetWorkJ >
                        ceramicStop762Abdomen.Response.BodyNetWorkJ);
            Assert.True(steel50Pierce.Response.BodyNetWorkJ > 0);
        }

        [Fact]
        public void Near_threshold_transition_is_finite_and_continuous()
        {
            var plate = SteelPlate(9.0);
            var stopped = Run(plate, Stop(0.008, 500), Intact(0.008), "Thorax");
            var barelyThrough = Run(plate, Pierce(0.008, 500, 0.008, 0.01),
                Pierced(0, 0, 0.999), "Thorax");

            Assert.True(stopped.Response.IsValid, stopped.Response.Reason);
            Assert.True(barelyThrough.Response.IsValid, barelyThrough.Response.Reason);
            Assert.InRange(Math.Abs(stopped.Transfer.DepositedEnergyJ -
                                    barelyThrough.Transfer.DepositedEnergyJ), 0, 1e-6);
            Assert.InRange(Math.Abs(stopped.Response.BodyNetWorkJ -
                                    barelyThrough.Response.BodyNetWorkJ), 0,
                0.01 * Math.Max(1e-9, stopped.Response.BodyNetWorkJ));
        }

        [Fact]
        public void Adaptive_policy_converges_and_bounds_burst_workload()
        {
            var plate = SteelPlate(6.35);
            var hit = Stop(0.008, 300);
            var damage = Intact(0.008);
            var normal = Run(plate, hit, damage, "Thorax", Numerics(2e-5));
            Assert.True(normal.Response.IsValid, normal.Response.Reason);
            var halfStep = Run(plate, hit, damage, "Thorax",
                Numerics(normal.Response.IntegrationTimeStepS / 2.0));

            Assert.True(halfStep.Response.IsValid, halfStep.Response.Reason);
            Assert.True(normal.Response.IntegrationTimeStepS <= 2e-5);
            Assert.True(halfStep.Response.IntegrationTimeStepS <=
                        normal.Response.IntegrationTimeStepS / 2.0 * (1.0 + 1e-12));
            Assert.InRange(Math.Abs(normal.Response.BodyNetWorkJ - halfStep.Response.BodyNetWorkJ),
                0, 0.01 * Math.Max(1e-9, halfStep.Response.BodyNetWorkJ));
            _output.WriteLine("steel coarse: " + normal.Describe());
            _output.WriteLine("steel fine: " + halfStep.Describe());

            var ceramicHit = Stop(0.048, 780);
            var ceramicDamage = Intact(0.048);
            var ceramicNormal = Run(CeramicBackedPlate(), ceramicHit, ceramicDamage,
                "Thorax", Numerics(2e-5));
            Assert.True(ceramicNormal.Response.IsValid, ceramicNormal.Describe());
            var ceramicFine = Run(CeramicBackedPlate(), ceramicHit, ceramicDamage,
                "Thorax", Numerics(ceramicNormal.Response.IntegrationTimeStepS / 2.0));
            Assert.True(ceramicFine.Response.IsValid, ceramicFine.Describe());
            Assert.Equal(BabtModel.EvaluationStatus.Complete,
                ceramicNormal.Response.Status);
            Assert.Equal(BabtModel.EvaluationStatus.Complete,
                ceramicFine.Response.Status);
            Assert.Equal(1, ceramicNormal.Response.ResolvedBrittleDropoutCount);
            Assert.Equal(1, ceramicFine.Response.ResolvedBrittleDropoutCount);
            Assert.True(ceramicFine.Response.IntegrationTimeStepS <=
                        ceramicNormal.Response.IntegrationTimeStepS / 2.0 * (1.0 + 1e-12));
            Assert.InRange(Math.Abs(ceramicNormal.Response.BodyNetWorkJ -
                                    ceramicFine.Response.BodyNetWorkJ), 0,
                0.01 * ceramicFine.Response.BodyNetWorkJ);
            Assert.InRange(Math.Abs(ceramicNormal.Response.BrittleFractureDissipationJ -
                                    ceramicFine.Response.BrittleFractureDissipationJ), 0,
                0.01 * ceramicFine.Response.BrittleFractureDissipationJ);
            _output.WriteLine("ceramic coarse: " + ceramicNormal.Describe());
            _output.WriteLine("ceramic fine: " + ceramicFine.Describe());

            var stopwatch = Stopwatch.StartNew();
            var steps = 0;
            for (var n = 0; n < 100; n++)
            {
                var scenario = n % 2 == 0
                    ? Run(plate, hit, damage, "Thorax", Numerics(2e-5))
                    : Run(CeramicBackedPlate(), Stop(0.048, 780), Intact(0.048),
                        "Abdomen", Numerics(2e-5));
                steps += scenario.Response.IntegrationSteps;
            }
            stopwatch.Stop();
            _output.WriteLine("100-hit mixed steel/ceramic workload: " + steps +
                              " steps, " + stopwatch.Elapsed);
            Assert.InRange(steps, 100, 2000000);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                "100-hit smoke workload took " + stopwatch.Elapsed);
        }

        [Fact]
        public void Unknown_intermediate_plug_fate_blocks_active_aggregate()
        {
            var construction = ResolveConstruction(SteelPlate(9.0), Intact(0));
            var first = Pierce(0.008, 720, 0.008, 600, 0.001, 600,
                BabtTransferModel.AggregateDisposition.Unknown);
            var second = Pierce(0.008, 600, 0.008, 500);
            var aggregate = BabtTransferModel.EvaluateAggregate(new[] { first, second },
                Coupling(construction));

            Assert.True(aggregate.IsValid, aggregate.Reason);
            Assert.Equal(BabtTransferModel.EvaluationStatus.IncompleteOutgoingInventory,
                aggregate.Status);
            Assert.Equal(BabtModel.Readiness.Unsupported, aggregate.Readiness);
        }

        private sealed class Scenario
        {
            public string Name;
            public BabtTransferModel.Transfer Transfer;
            public BabtModel.Response Response;
            public BabtTransferModel.ClosedBudget Closed;

            public string Describe()
            {
                return Name + ": transfer=" + Transfer.Status + "/" + Transfer.Reason +
                       "; response=" + Response.Status + "/" + Response.Reason +
                       "; bodyWork=" + Response.BodyNetWorkJ.ToString("G6") + " J" +
                       "; seed=" + Transfer.MechanicalSeedEnergyJ.ToString("G6") + " J" +
                       "; steps=" + Response.IntegrationSteps +
                       "; dt=" + Response.IntegrationTimeStepS.ToString("G6") + " s" +
                       "; stable=" + Response.BodyWorkStableDurationS.ToString("G6") + " s" +
                       "; recoil=" + Response.FreePlateRecoilVelocityMps.ToString("G6") + " m/s" +
                       "; brittle=" + Response.ResolvedBrittleDropoutCount + "/" +
                       Response.BrittleFractureDissipationJ.ToString("G6") + " J";
            }
        }

        private static Scenario Run(AmmoDataCache.PlateGeometry plate,
            BabtTransferModel.ResolvedImpact hit,
            BabtConstructionResolver.ResolvedImpactDamage damage, string region,
            BabtModel.Numerics numerics = null)
        {
            var construction = ResolveConstruction(plate, damage, out var coupling);
            var area = construction.WidthM * construction.HeightM;
            Assert.True(BabtConstructionResolver.TryResolveBody(BodyProfiles(), region,
                area, 0.08, out var body, out var bodyDiagnostic), bodyDiagnostic);
            var transfer = BabtTransferModel.Evaluate(hit, coupling);
            var response = BabtModel.EvaluateTransferred(construction, transfer, body,
                numerics ?? Numerics(2e-5));
            return new Scenario
            {
                Name = plate.M + " " + plate.T.ToString("G4") + " mm " +
                       hit.Outcome + " " + region,
                Transfer = transfer,
                Response = response,
                Closed = BabtTransferModel.Close(transfer, response),
            };
        }

        private static BabtModel.Construction ResolveConstruction(
            AmmoDataCache.PlateGeometry plate,
            BabtConstructionResolver.ResolvedImpactDamage damage)
        {
            return ResolveConstruction(plate, damage, out _);
        }

        private static BabtModel.Construction ResolveConstruction(
            AmmoDataCache.PlateGeometry plate,
            BabtConstructionResolver.ResolvedImpactDamage damage,
            out BabtTransferModel.Coupling coupling)
        {
            Assert.True(BabtConstructionResolver.TryResolveCoupling(plate, null, Materials(),
                damage, out coupling, out var diagnostic), diagnostic);
            return coupling.PostImpactConstruction;
        }

        private static BabtTransferModel.Coupling Coupling(BabtModel.Construction construction)
        {
            return new BabtTransferModel.Coupling
            {
                PostImpactConstruction = construction,
                ImpactModeFactor = 1,
                OutgoingInventoryComplete = true,
                Provenance = "production-value aggregate smoke coupling",
                Readiness = BabtModel.Readiness.Provisional,
            };
        }

        private static BabtTransferModel.ResolvedImpact Stop(double mass, double speed)
        {
            return new BabtTransferModel.ResolvedImpact
            {
                LayerId = "production-value resolved stop",
                Outcome = BabtTransferModel.Outcome.Stop,
                Incoming = Projectile(mass, speed),
                OutgoingPrimary = Projectile(0, 0, true),
                ExistingBarrierWorkJ = 0.5 * mass * speed * speed,
                Provenance = "prescribed terminal-ballistics stop",
                Readiness = BabtModel.Readiness.Provisional,
            };
        }

        private static BabtTransferModel.ResolvedImpact Pierce(double incomingMass,
            double incomingSpeed, double outgoingMass, double outgoingSpeed,
            double plugMass = 0, double plugSpeed = 0,
            BabtTransferModel.AggregateDisposition plugDisposition =
                BabtTransferModel.AggregateDisposition.LeavesAggregateSystem)
        {
            var secondaries = plugMass > 0
                ? new[]
                {
                    new BabtTransferModel.OutgoingBody
                    {
                        Name = "existing-model plug",
                        MassKg = plugMass,
                        SpeedMps = plugSpeed,
                        NormalVelocityMps = plugSpeed,
                        Disposition = plugDisposition,
                        Provenance = "prescribed ArmorExit plug",
                        Readiness = BabtModel.Readiness.Provisional,
                    },
                }
                : new BabtTransferModel.OutgoingBody[0];
            return new BabtTransferModel.ResolvedImpact
            {
                LayerId = "production-value resolved pierce",
                Outcome = BabtTransferModel.Outcome.Pierce,
                Incoming = Projectile(incomingMass, incomingSpeed),
                OutgoingPrimary = Projectile(outgoingMass, outgoingSpeed),
                OutgoingSecondaries = secondaries,
                ExistingBarrierWorkJ = 0,
                Provenance = "prescribed terminal-ballistics pierce",
                Readiness = BabtModel.Readiness.Provisional,
            };
        }

        private static BabtTransferModel.ProjectileState Projectile(double mass,
            double speed, bool stopped = false)
        {
            return new BabtTransferModel.ProjectileState
            {
                MassKg = mass,
                SpeedMps = speed,
                NormalVelocityMps = speed,
                Provenance = stopped ? "resolved stopped state" : "resolved projectile state",
                Readiness = BabtModel.Readiness.Provisional,
            };
        }

        private static BabtConstructionResolver.ResolvedImpactDamage Intact(double retainedMass)
        {
            return new BabtConstructionResolver.ResolvedImpactDamage
            {
                CoherentFractionsKnown = true,
                FaceCoherentFraction = 1,
                BackingCoherentFraction = 1,
                OutgoingInventoryComplete = true,
                RetainedProjectileMassKg = retainedMass,
                Provenance = "resolved intact stop with retained projectile",
            };
        }

        private static BabtConstructionResolver.ResolvedImpactDamage Pierced(
            double ejectedMass, double retainedMass, double faceFraction,
            double backingFraction = 1, bool brittleFailed = false)
        {
            return new BabtConstructionResolver.ResolvedImpactDamage
            {
                Perforated = true,
                HoleAreaM2 = 8e-5,
                CoherentFractionsKnown = true,
                FaceCoherentFraction = faceFraction,
                BackingCoherentFraction = backingFraction,
                BrittleFaceFailed = brittleFailed,
                OutgoingInventoryComplete = true,
                KnownOutgoingEjectedMassKg = ejectedMass,
                RetainedProjectileMassKg = retainedMass,
                Provenance = "resolved post-pierce coherent state and mass inventory",
            };
        }

        private static AmmoDataCache.PlateGeometry SteelPlate(double thicknessMm)
        {
            return Plate("ArmoredSteel", "IsotropicPlate", thicknessMm, 1, 2050);
        }

        private static AmmoDataCache.PlateGeometry UhmwpePlate()
        {
            return Plate("UHMWPE", "BondedLaminate", 23, 1, 0);
        }

        private static AmmoDataCache.PlateGeometry AramidPanel()
        {
            return Plate("Aramid", "SoftWoven", 7.6, 0.44, 0);
        }

        private static AmmoDataCache.PlateGeometry CeramicBackedPlate()
        {
            var plate = Plate("Ceramic", "BrittleFace", 10, 2.52 / 3.90, 0);
            plate.B = 12;
            plate.BM = "UHMWPE";
            plate.BP = 1;
            plate.BabtBackingForm = "BondedLaminate";
            return plate;
        }

        private static AmmoDataCache.PlateGeometry Plate(string material, string form,
            double thicknessMm, double packing, double yieldMpa)
        {
            return new AmmoDataCache.PlateGeometry
            {
                T = thicknessMm,
                M = material,
                P = packing,
                Y = yieldMpa,
                BabtForm = form,
                BabtWidthMm = 254,
                BabtHeightMm = 318,
                BabtGeometryStatus = "Estimated",
                BabtGeometrySource = "production plate footprint reduction",
                ConstructionOrigin = "Product",
                ConstructionSource = "production-value scenario fixture",
                ItemKind = "Plate",
            };
        }

        private static Dictionary<string, AmmoDataCache.MaterialPhysics> Materials()
        {
            return new Dictionary<string, AmmoDataCache.MaterialPhysics>
            {
                ["ArmoredSteel"] = new AmmoDataCache.MaterialPhysics
                {
                    Class = "Ductile", DensityGCm3 = 7.85, YieldMPa = 1250,
                    FailureStrain = 0.08, Source = "shipped ArmoredSteel reference",
                    YoungModulusGPa = Estimated(210), PoissonRatio = Estimated(0.30),
                    StructuralDampingRatio = Estimated(0.005),
                },
                ["Ceramic"] = new AmmoDataCache.MaterialPhysics
                {
                    Class = "Brittle", DensityGCm3 = 3.90, FailureStrain = 0.00081,
                    Source = "shipped alumina reference", YoungModulusGPa = Estimated(370),
                    PoissonRatio = Estimated(0.22), StructuralDampingRatio = Estimated(0.01),
                },
                ["Aramid"] = new AmmoDataCache.MaterialPhysics
                {
                    Class = "Fibrous", DensityGCm3 = 1.44, FibreTensileMPa = 2900,
                    FailureStrain = 0.034, Source = "shipped aramid reference",
                    RigidLaminateModulusGPa = Estimated(10.06),
                    RigidLaminatePoissonRatio = Estimated(0.25),
                    StructuralDampingRatio = Estimated(0.02),
                },
                ["UHMWPE"] = new AmmoDataCache.MaterialPhysics
                {
                    Class = "Fibrous", DensityGCm3 = 0.97, FibreTensileMPa = 3400,
                    FailureStrain = 0.035, Source = "shipped UHMWPE reference",
                    RigidLaminateModulusGPa = Estimated(45.2),
                    RigidLaminatePoissonRatio = Estimated(0.013),
                    StructuralDampingRatio = Estimated(0.02),
                },
            };
        }

        private static Dictionary<string, AmmoDataCache.BabtBodyProfile> BodyProfiles()
        {
            return new Dictionary<string, AmmoDataCache.BabtBodyProfile>
            {
                ["Thorax"] = new AmmoDataCache.BabtBodyProfile
                {
                    MassModel = "Fixed",
                    EffectiveMassKg = Estimated(0.45),
                    ContactStiffnessNPerM = Estimated(2630000),
                    ContactDampingNsPerM = Estimated(0),
                    FoundationStiffnessNPerM = Estimated(26300),
                    FoundationDampingNsPerM = Estimated(525),
                    InitialGapMm = Estimated(0),
                    Source = "shipped provisional Lobdell reduction",
                },
                ["Abdomen"] = new AmmoDataCache.BabtBodyProfile
                {
                    MassModel = "TissueSlab",
                    TissueDensityKgM3 = Estimated(1000),
                    ContactStiffnessNPerM = Estimated(1290000),
                    ContactDampingNsPerM = Estimated(0),
                    FoundationStiffnessNPerM = Estimated(12900),
                    FoundationDampingNsPerM = Estimated(765),
                    InitialGapMm = Estimated(0),
                    Source = "shipped provisional Trosseille reduction",
                },
            };
        }

        private static BabtModel.Numerics Numerics(double maximumStep)
        {
            var profile = new AmmoDataCache.BabtNumericsProfile
            {
                TimeStepS = Estimated(maximumStep),
                SimulationDurationS = Estimated(0.05),
                MaximumIntegrationSteps = Estimated(100000),
                MinimumStepsPerPeriod = Estimated(160),
                MaximumRelativeEnergyError = Estimated(0.01),
                SettledVelocityToleranceMps = Estimated(0.01),
                SettledForceToleranceN = Estimated(1),
                Source = "shipped production numerical verification policy",
            };
            Assert.True(BabtConstructionResolver.TryResolveNumerics(profile,
                out var numerics, out var diagnostic), diagnostic);
            return numerics;
        }

        private static AmmoDataCache.BabtParameter Estimated(double value)
        {
            return new AmmoDataCache.BabtParameter
            {
                Value = value,
                Status = "Estimated",
                Source = "shipped/reference-backed estimate",
            };
        }
    }
}
