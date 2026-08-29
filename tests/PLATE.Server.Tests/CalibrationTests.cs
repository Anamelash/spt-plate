using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The shipped constants against their own derivations.
///
/// The calibration rule is: strengths are published, free constants come one per
/// failure MECHANISM, and each is derived from named data in a fixed order — DuctileK
/// from the RHA ladder, the hardness term from what separates hard plate from mild,
/// BrittleK from the bare-tile DOP point checked against the ceramic certificates,
/// FibrousK from the certificates it has to satisfy, now with a published aramid ladder
/// underneath it that says the certificates ask for too much. These tests make
/// the derivation repeatable: if the physics changes and the shipped number is not
/// re-derived, they go red — which is precisely the accident they exist to prevent,
/// a constant surviving the death of its own justification.
/// </summary>
public class CalibrationTests
{
    /// <summary>
    /// The certification multipliers, pinned to the numbers derived in the coherence
    /// work so a refactor of the quantile arithmetic cannot silently move the bar.
    /// </summary>
    [Fact]
    public void The_protocol_multipliers_are_the_derived_ones()
    {
        // ±0.002 absorbs the Hastings approximation without letting a protocol be
        // quietly re-counted: one shot more or fewer moves a multiplier by ~0.005
        Assert.InRange(CertificationCriteria.RequiredV50Multiplier(5), 1.091, 1.095);
        Assert.InRange(CertificationCriteria.RequiredV50Multiplier(2), 1.076, 1.080);
        Assert.InRange(CertificationCriteria.RequiredV50Multiplier(6), 1.094, 1.098);
        Assert.InRange(CertificationCriteria.RequiredV50Multiplier(1), 1.064, 1.068);
    }

    /// <summary>
    /// Every recorded certification shortfall must still be a shortfall. An allowance
    /// that is no longer needed is a hole a future regression walks through unseen:
    /// the moment a plate clears the strict bar on its own, its entry has to be
    /// DELETED, and this test is what forces that.
    /// </summary>
    [Fact]
    public void Every_recorded_shortfall_is_still_needed()
    {
        foreach (var (key, (reaches, cause)) in ArmorStandardTests.CertShortfalls)
        {
            BallisticLimit.Barrier barrier;
            string standard, cls;

            if (key.Contains('/'))
            {
                // a class rung standing on a real product without a book entry; the
                // rung index IS the Br number since the realignment
                var parts = key.Split('/');
                (barrier, _) = ArmorFixture.ByClass(parts[0], int.Parse(parts[1]));
                standard = "GOST";
                cls = $"Бр{parts[1]}";
            }
            else
            {
                var cert = ArmorStandardTests.Certified.Single(c => c.BookKey == key);
                (barrier, _) = ArmorFixture.ByProduct(key);
                standard = cert.Standard;
                cls = cert.Class;
            }

            var short_ = ArmorFixture.Threats(standard, cls).Any(t =>
                ArmorFixture.V50(barrier, t) <
                CertificationCriteria.RequiredV50(standard, cls, t.V));

            Assert.True(short_,
                $"{key} now clears the strict criterion on its own — delete its " +
                $"shortfall entry (\"{cause}\") instead of leaving the allowance armed");
            Assert.InRange(reaches, 0.5, 0.999);
        }
    }

    /// <summary>
    /// The fibre mode's two anchors, and the gap between them, pinned so neither can
    /// move without somebody noticing.
    ///
    /// The certificates give a FLOOR — a plate that stopped its threat says the limit is
    /// at least the test velocity — and the shipped constant is the smallest value that
    /// satisfies all of them. The para-aramid ladders give a two-sided measurement, and
    /// it lands BELOW that floor. Both numbers are held here because the pair is the
    /// finding: the model needs more work out of a thick fibre pack than a measured thin
    /// one allows, which is a statement about the thickness law.
    ///
    /// If a future law closes the gap, this test fails by being satisfied too easily and
    /// should be rewritten to derive FibrousK from the ladder, the way DuctileK is
    /// derived from RHA. That is the outcome to aim at.
    /// </summary>
    [Fact]
    public void The_fibre_ladder_sits_under_the_floor_the_certificates_demand()
    {
        var ladder = ArmorStandardTests.Limits
            .Where(l => ArmorStandardTests.LadderMaterials[l.Material].Class ==
                        BallisticLimit.Fibrous)
            .ToArray();
        Assert.Equal(10, ladder.Length);

        // one constant across both constructions: same fibre, same fragment, same
        // laboratory, same standard — the guard has nothing to object to
        var derived = LadderCalibrator.DeriveK(ladder);
        var shipped = BallisticLimit.Tuning.Default.FibrousK;

        Assert.InRange(derived, 22.5, 23.7);
        Assert.True(shipped > derived,
            $"the certificates' floor ({shipped}) has dropped to the ladder's " +
            $"measurement ({derived:0.0}) — the fibre law's thickness dependence has " +
            "been fixed, so derive FibrousK from the ladder and delete this test");

        // the gap, in the units it is felt in: velocity, not work
        Assert.InRange(Math.Sqrt(shipped / derived), 1.05, 1.12);
    }

    /// <summary>
    /// Every item in the book whose class the ballistic limit has to keep: the certified
    /// products, and the class rungs that stand in for a material nobody built a
    /// certified plate of. The rungs matter because they are the only evidence the
    /// fixture has at the pistol end, where no ladder reaches.
    /// </summary>
    private static IEnumerable<(BallisticLimit.Barrier Barrier, string Standard,
        string Class, string Name)> Items()
    {
        foreach (var c in ArmorStandardTests.Certified)
        {
            var (barrier, _) = ArmorFixture.ByProduct(c.BookKey);
            yield return (barrier, c.Standard, c.Class, c.BookKey);
        }

        foreach (var material in new[]
                 {
                     "Aramid", "UHMWPE", "ArmoredSteel", "Ceramic", "Titan", "Combined",
                     "Aluminium",
                 })
        {
            // a sewn aramid package is sold as Бр1 or Бр2 and nothing above; the rung
            // table clamps, so asking for class 6 hands back the Бр2 package and then
            // demands it stop a B-32
            var top = material == "Aramid" ? 3 : 6;
            for (var gameClass = 2; gameClass <= top; gameClass++)
            {
                if (!ArmorFixture.ClassExists(material, gameClass))
                {
                    continue;
                }

                var (barrier, _) = ArmorFixture.ByClass(material, gameClass);
                var cls = gameClass switch
                {
                    2 => "Бр1", 3 => "Бр2", 4 => "Бр3", 5 => "Бр4", _ => "Бр5",
                };
                yield return (barrier, "GOST", cls, $"{material}/{gameClass}");
            }
        }
    }

    /// <summary>How far short of its own certificate an item reads, 1 = clears it.</summary>
    private static (double Reaches, string Threat) Reaches(
        BallisticLimit.Barrier barrier, string standard, string cls,
        BallisticLimit.Tuning t)
    {
        var reaches = 1.0;
        var worst = "";
        foreach (var threat in ArmorFixture.Threats(standard, cls))
        {
            var need = CertificationCriteria.RequiredV50(standard, cls, threat.V);
            var got = BallisticLimit.V50(barrier, ArmorFixture.CoreOf(threat), 1.0, threat.V, t);
            if (need > 0 && got / need < reaches)
            {
                reaches = got / need;
                worst = threat.Cartridge;
            }
        }

        return (reaches, worst);
    }

    /// <summary>
    /// The hardness ceiling, and the one measurement that pins it.
    ///
    /// Every published ladder in the fixture is an armour-piercing core, so no ladder
    /// says anything about the end of the term where a LEAD bullet meets a HARD plate —
    /// which is the end the ceiling is. The two steel pistol rungs used to stand in for
    /// that evidence and could not: they are computed, solved from their own class's
    /// cartridge AT this clamp, so any ceiling produces a thickness that clears the
    /// certificate and the certificate confirms nothing.
    ///
    /// <see cref="Ar500LevelIii"/> is the real thing — a plate somebody built, sold and
    /// had shot six times. It is one certificate, and a certificate is one-sided: it
    /// bounds the limit from below, so what it hands back is the smallest ceiling at
    /// which the plate still holds. The shipped value sits on that floor, the way
    /// FibrousK sits on the floor its own certificates demand.
    /// </summary>
    [Fact]
    public void The_hardness_ceiling_is_what_the_one_soft_core_certificate_demands()
    {
        var shipped = BallisticLimit.Tuning.Default;

        var holds = Reaches(Ar500LevelIii, "NIJ", "III", shipped);
        Assert.True(holds.Reaches >= 1.0,
            $"the AR500 Level III reads {holds.Reaches:P0} of its own certificate " +
            $"against {holds.Threat} — the ceiling no longer covers the soft-core end");

        // and it sits ON the floor rather than comfortably above it: a hair under the
        // shipped value the plate stops holding, which is what "derived" means here
        var under = shipped;
        under.HardnessCeiling = shipped.HardnessCeiling * 0.97;
        Assert.True(Reaches(Ar500LevelIii, "NIJ", "III", under).Reaches < 1.0,
            $"the plate still clears its certificate 3% below the shipped ceiling of " +
            $"{shipped.HardnessCeiling}, so the ceiling is above what anything measured " +
            "demands and should be derived down to it");
    }

    /// <summary>
    /// Nothing else in the corpus can speak for the ceiling, and this is what says so.
    /// If some future entry starts moving with it, that entry is better evidence than
    /// one certificate and the constant should be derived against it instead.
    /// </summary>
    [Fact]
    public void No_certified_product_but_the_one_anchor_moves_with_the_ceiling()
    {
        var shipped = BallisticLimit.Tuning.Default;
        var doubled = shipped;
        doubled.HardnessCeiling = shipped.HardnessCeiling * 2;

        foreach (var cert in ArmorStandardTests.Certified)
        {
            var (barrier, _) = ArmorFixture.ByProduct(cert.BookKey);
            var now = Reaches(barrier, cert.Standard, cert.Class, shipped).Reaches;
            var loose = Reaches(barrier, cert.Standard, cert.Class, doubled).Reaches;

            Assert.True(Math.Abs(now - loose) < 0.01,
                $"{cert.BookKey} moves from {now:0.00} to {loose:0.00} when the hardness " +
                "ceiling doubles — it is evidence for the clamp and belongs in its " +
                "derivation");
        }
    }

    /// <summary>
    /// A 0.25-inch AR500 plate: the commonest steel Level III on the market, and the
    /// only place in the corpus where a soft core meets a hard plate under a published
    /// certificate. NIJ 0101.06 Level III is six shots of M80 ball — 9.5 g of lead
    /// alloy at 847 m/s — standalone, into 580 HV steel. The steel's own figures come
    /// from the book's ArmoredSteel entry; only the thickness is the product's.
    /// </summary>
    private static BallisticLimit.Barrier Ar500LevelIii => new()
    {
        Class = BallisticLimit.Ductile,
        FailureMode = BallisticLimit.ShearPlugging,
        ThicknessMm = 6.35,
        ShearMPa = 750,
        YieldMPa = 1250,
        HardnessHv = 580,
        DensityGCm3 = 7.85,
        PackedFraction = 1,
    };

    /// <summary>
    /// The floor, pinned between the certificate that needs it and the absurdity that
    /// bounds it: a plate softer than the core must never earn more from the hardness
    /// term than a harder plate meeting the same core, which is where the term inverts.
    /// The RHA ladder's own factor is that ceiling on the floor.
    /// </summary>
    [Fact]
    public void The_hardness_floor_stays_under_the_ladder_it_would_otherwise_invert()
    {
        var t = BallisticLimit.Tuning.Default;
        var rha = ArmorStandardTests.Limits.First(l => l.Material == "ArmoredSteel");
        var m = ArmorStandardTests.LadderMaterials["ArmoredSteel"];
        var factor = BallisticLimit.HardnessFactor(
            LadderCalibrator.LadderBarrier(rha, m), ArmorFixture.CoreOf(rha.Threat),
            rha.V50, t);

        Assert.True(t.HardnessFloor < factor,
            $"the floor {t.HardnessFloor} is at or above the {factor:0.000} the RHA " +
            "ladder itself earns against its core — a softer plate would now be worth " +
            "more than a harder one against the same bullet");
    }

    /// <summary>A vest gate's plate with its fabric screen, as the gate test builds it.</summary>
    private static BallisticLimit.Barrier VestAssembly(int gateIndex)
    {
        var g = ArmorStandardTests.VestGates[gateIndex];
        var (barrier, _) = ArmorFixture.ByProduct(g.PlateKey);
        var aramid = ReferenceBookTests.ShippedBook().ArmorMaterials["Aramid"];
        barrier.BackingMm = g.ScreenMm;
        barrier.BackingTensileMPa = aramid.FibreTensileMPa;
        barrier.BackingStrain = aramid.FailureStrain;
        barrier.BackingPacked = BallisticLimit.SewnPacked;
        return barrier;
    }

    private static bool GateHolds(int gateIndex, BallisticLimit.Tuning t)
    {
        var g = ArmorStandardTests.VestGates[gateIndex];
        var v50 = BallisticLimit.V50(VestAssembly(gateIndex),
            ArmorFixture.CoreOf(g.Round), 1.0, g.Round.V, t);
        return v50 >= g.Round.V;
    }

    /// <summary>
    /// The deform floor sits on the one measurement that demands a factor above 1:
    /// the 6B3TM holding the mild PS core, a passport sentence no rigid reading can
    /// satisfy (the ratio is below 1 there). One-sided, so the value sits just over
    /// the demand — at 1.0, the physical floor, the gate must fail, or the floor has
    /// stopped being load-bearing and should be derived down.
    /// </summary>
    [Fact]
    public void The_deform_floor_sits_on_the_6B3TM_mild_core_passport()
    {
        var shipped = BallisticLimit.Tuning.Default;
        Assert.True(GateHolds(6, shipped), "the anchor gate itself is failing");

        var bare = shipped;
        bare.DeformFloor = 1.0;
        Assert.False(GateHolds(6, bare),
            "the 6B3TM holds the mild PS with no help from the deform floor — the " +
            "floor is above what anything measured demands and should be derived down");

        // and it is a floor over MORE than the calibre, never less
        Assert.True(shipped.DeformFloor >= 1.0);
    }

    /// <summary>
    /// The spread exponent's window, both walls. Below it the M80 cannot drive the
    /// AR500 certificate up to the ceiling that certificate pins; above it the 6B3TM
    /// chest section starts stopping the SVD its own passport says goes through. If
    /// either probe stops failing, the corpus has loosened and the value should be
    /// re-derived against whatever moved.
    /// </summary>
    [Fact]
    public void The_spread_exponent_sits_between_the_AR500_and_the_6B3TM_passport()
    {
        var shipped = BallisticLimit.Tuning.Default;

        var shallow = shipped;
        shallow.DeformSpreadExponent = 0.31;
        Assert.True(Reaches(Ar500LevelIii, "NIJ", "III", shallow).Reaches < 1.0,
            "the AR500 clears its certificate below the exponent's derived floor — " +
            "the floor is not where this test thinks it is; re-derive");

        var steep = shipped;
        steep.DeformSpreadExponent = 0.41;
        Assert.True(GateHolds(7, steep),
            "at 0.41 the SVD still pierces the 6B3TM, so the ceiling of the window " +
            "is not where this test thinks it is; re-derive");
        Assert.False(GateHolds(7, shipped),
            "the SVD fails to pierce the 6B3TM at the shipped exponent");

        Assert.True(Reaches(Ar500LevelIii, "NIJ", "III", shipped).Reaches >= 1.0);
    }

    /// <summary>
    /// The deform threshold's window, both walls: the 390 HV pre-1989 PS core must
    /// die on titanium or the 6B3TM passport gate fails, and the 570 HV M2 AP must
    /// stay rigid or the RHA ladder stops deriving one constant — its top rows would
    /// arrive above the Taylor stress and flip branches mid-ladder, and the per-row
    /// solutions tear apart by the ratio of the two factors.
    /// </summary>
    [Fact]
    public void The_deform_threshold_sits_between_the_PS_core_and_the_M2_AP()
    {
        var shipped = BallisticLimit.Tuning.Default;
        Assert.InRange(shipped.DeformCoreMaxHv, 391, 569);

        var below = shipped;
        below.DeformCoreMaxHv = 390;
        Assert.False(GateHolds(6, below),
            "at a threshold of 390 the mild PS core stays rigid and the 6B3TM gate " +
            "still passes — the lower wall has moved; re-derive");

        var above = shipped;
        above.DeformCoreMaxHv = 580;
        var rha = ArmorStandardTests.Limits.Where(l => l.Material == "ArmoredSteel").ToArray();
        var perRow = rha.Select(r => LadderCalibrator.SolveK(r, above)).ToArray();
        Assert.True(perRow.Max() / perRow.Min() > 1.5,
            "with the M2 AP allowed to deform, the RHA ladder still solves to one " +
            "constant — the upper wall has moved; re-derive");
        var perRowShipped = rha.Select(r => LadderCalibrator.SolveK(r, shipped)).ToArray();
        Assert.True(perRowShipped.Max() / perRowShipped.Min() < 1.25,
            "the ladder no longer agrees with itself even at the shipped threshold");
    }

    /// <summary>
    /// The plate-support share, both walls, each pinned by a named round staying on
    /// its own side of the fate decision: the mild PS core must die on the 6B3TM's
    /// titanium at 720 m/s (stagnation alone is 109 MPa short there — the term is
    /// load-bearing), and the 9x18's 250 HV core must survive a Бр1 steel panel at
    /// 335, or the Бр1 and Бр2 rungs stop being distinguishable.
    /// </summary>
    [Fact]
    public void The_plate_support_sits_between_the_6B3TM_and_the_9x18()
    {
        var shipped = BallisticLimit.Tuning.Default;

        var titanium = VestAssembly(6);
        var ps = ArmorFixture.CoreOf(ArmorStandardTests.VestGates[6].Round);
        Assert.Equal(BallisticLimit.CoreFate.Deformed,
            BallisticLimit.FateOf(titanium, ps, 720, shipped));
        var weak = shipped;
        weak.DeformPlateSupport = 0.09;
        Assert.Equal(BallisticLimit.CoreFate.Rigid,
            BallisticLimit.FateOf(titanium, ps, 720, weak));

        var (br1, _) = ArmorFixture.ByClass("ArmoredSteel", 2);
        var pst = ArmorFixture.CoreOf(
            ArmorStandardTests.Gost.Single(t => t.Cartridge.StartsWith("9x18")));
        Assert.Equal(BallisticLimit.CoreFate.Rigid,
            BallisticLimit.FateOf(br1, pst, 335, shipped));
        var strong = shipped;
        strong.DeformPlateSupport = 0.20;
        Assert.Equal(BallisticLimit.CoreFate.Deformed,
            BallisticLimit.FateOf(br1, pst, 335, strong));
    }

    /// <summary>
    /// The shatter ratio sits a hair inside its one anchor — the 6B23 certificate,
    /// where 613 HV of 44S turns back the 7N24's 1300 HV carbide. A certificate is
    /// one-sided: it says this ratio suffices and nothing about softer plates, so the
    /// value may not exceed the documented point, and just past it the gate must fail
    /// or the anchor is not doing the pinning.
    /// </summary>
    [Fact]
    public void The_shatter_ratio_sits_on_the_6B23_anchor()
    {
        var shipped = BallisticLimit.Tuning.Default;
        Assert.True(shipped.ShatterRatio <= 613.0 / 1300.0,
            "the ratio is past the one documented shatter — nothing measured supports it");
        Assert.True(GateHolds(4, shipped), "the anchor gate itself is failing");

        var past = shipped;
        past.ShatterRatio = 0.48;
        Assert.False(GateHolds(4, past),
            "the 6B23 holds the 7N24 even with the shatter out of reach — the gate " +
            "is being carried by something else and the anchor story is stale");

        // and no steel is brittle: the boundary lives above every steel in the corpus
        Assert.True(shipped.BrittleCoreMinHv > 800);
        Assert.True(shipped.BrittleCoreMinHv <= 1300);
    }

    /// <summary>
    /// The packing exponent, derived against every piece of fibre evidence at once
    /// rather than against the pair that would move it furthest.
    ///
    /// Packing enters as packed^p. The two aramid ladders on their own solve p = 0.38,
    /// which makes them agree with each other exactly — and the same value moves the
    /// pressed plates, which are packed 1 and cannot move, from 12% away to 32% away.
    /// Fitted against ladders and products together the disagreement is smallest at 1,
    /// and this test is what stops the tempting number being taken.
    /// </summary>
    [Fact]
    public void The_packing_exponent_is_where_the_fibre_evidence_disagrees_least()
    {
        var ladder = ArmorStandardTests.Limits
            .Where(l => ArmorStandardTests.LadderMaterials[l.Material].Class ==
                        BallisticLimit.Fibrous)
            .ToArray();

        double Gap(double p)
        {
            var t = BallisticLimit.Tuning.Default;
            t.PackingExponent = p;

            var demanded = 0.0;
            foreach (var (barrier, standard, cls, _) in Items())
            {
                if (barrier.Class != BallisticLimit.Fibrous && barrier.BackingMm <= 0)
                {
                    continue;
                }

                foreach (var threat in ArmorFixture.Threats(standard, cls))
                {
                    var core = ArmorFixture.CoreOf(threat);
                    var unit = t;
                    unit.FibrousK = 1;
                    var perK = BallisticLimit.WorkJ(barrier, core, 1.0, threat.V, unit);
                    var need = 0.5 * (BallisticLimit.MassAgainst(barrier, core) / 1000.0) * threat.V * threat.V;
                    if (perK > 0)
                    {
                        demanded = Math.Max(demanded, need / perK);
                    }
                }
            }

            return Math.Sqrt(demanded / LadderCalibrator.DeriveK(ladder, t));
        }

        var shipped = BallisticLimit.Tuning.Default.PackingExponent;
        foreach (var p in new[] { 0.38, 0.8, 0.9, 1.1, 1.2 })
        {
            Assert.True(Gap(shipped) <= Gap(p),
                $"packing exponent {p} now reconciles the fibre evidence better than " +
                $"the shipped {shipped} ({Gap(p):0.000} against {Gap(shipped):0.000}) " +
                "— derive it again");
        }
    }

    /// <summary>
    /// BrittleK against both of its anchors at once: the bare tile is a floor, the
    /// certified ceramics are the requirement, and the shipped value is the smallest
    /// that meets the second without leaving the first's band.
    ///
    /// Two rules of the derivation, both learned the hard way. The requirement is
    /// read at the criterion the certificate tests actually enforce — zero-of-five,
    /// about +9% in velocity — not at the bare test velocity: this test derived at
    /// bare velocity once, agreed with the enforcement by coincidence, and the
    /// coincidence died the day the backing data was fixed. And only real products
    /// WITHOUT a recorded shortfall may demand anything: a computed rung confirms
    /// nothing (the ceiling's old circular anchor), and a recorded miss is a
    /// documented gap, not a requirement — letting it bid would push the constant up
    /// to hide the very physics its entry documents.
    /// </summary>
    [Fact]
    public void BrittleK_is_the_smallest_the_ceramic_certificates_allow()
    {
        var t = BallisticLimit.Tuning.Default;
        var need = 0.0;
        foreach (var c in ArmorStandardTests.Certified)
        {
            if (ArmorStandardTests.CertShortfalls.ContainsKey(c.BookKey))
            {
                continue;
            }

            var (barrier, _) = ArmorFixture.ByProduct(c.BookKey);
            if (barrier.Class != BallisticLimit.Brittle)
            {
                continue;
            }

            foreach (var threat in ArmorFixture.Threats(c.Standard, c.Class))
            {
                var core = ArmorFixture.CoreOf(threat);
                var unit = t;
                unit.BrittleK = 1;
                var face = barrier;
                face.BackingMm = 0;
                var perK = BallisticLimit.WorkJ(face, core, 1.0, threat.V, unit);
                var backing = BallisticLimit.WorkJ(barrier, core, 1.0, threat.V, unit) - perK;
                var required = CertificationCriteria.RequiredV50(c.Standard, c.Class, threat.V);
                var demanded = 0.5 * (BallisticLimit.MassAgainst(barrier, core) / 1000.0)
                               * required * required;
                if (perK > 0)
                {
                    need = Math.Max(need, (demanded - backing) / perK);
                }
            }
        }

        Assert.InRange(t.BrittleK / need, 0.99, 1.02);
    }

    [Fact]
    public void DuctileK_is_what_the_RHA_ladder_derives()
    {
        var rha = ArmorStandardTests.Limits
            .Where(l => l.Material == "ArmoredSteel")
            .ToArray();
        var derived = LadderCalibrator.DeriveK(rha);

        Assert.InRange(BallisticLimit.Tuning.Default.DuctileK / derived, 0.99, 1.01);
    }

    /// <summary>
    /// The flow law's own constant, from the flow law's own ladder — all seven mild
    /// points, no hardness factor in the branch, no other material in the derivation.
    /// The plugging family cannot move it and it cannot move them: that independence
    /// is the whole point of failure mode being a material property rather than a
    /// min(), and this test is what keeps the two calibrations from growing back
    /// together.
    /// </summary>
    [Fact]
    public void HoleGrowthK_is_what_the_mild_ladder_derives()
    {
        var mild = ArmorStandardTests.Limits
            .Where(l => l.Material == "MildSteel")
            .ToArray();
        var derived = LadderCalibrator.DeriveK(mild);

        Assert.InRange(BallisticLimit.Tuning.Default.HoleGrowthK / derived, 0.99, 1.01);
    }

    /// <summary>
    /// The tile is the anchor and the certificates are the check, and the shipped
    /// value has to respect both. A nil-residual DOP is one-sided — the limit is at
    /// or above the velocity fired — so the tile's own solution (≈0.85) is a FLOOR
    /// for BrittleK, not a target; the ceramic certificates pin where inside the
    /// tile's +20% band the constant actually sits. Drifting below the floor would
    /// mean the tile outperforms the model's ceiling for it; drifting past the band
    /// edge would mean the certificates are being bought with a tile nobody measured.
    /// </summary>
    [Fact]
    public void BrittleK_sits_on_the_tile_anchor_read_one_sided()
    {
        var tile = ArmorStandardTests.Limits.Single(l => l.Material == "Ceramic");
        var floor = LadderCalibrator.SolveK(tile);
        var shipped = BallisticLimit.Tuning.Default.BrittleK;

        Assert.True(shipped >= floor,
            $"BrittleK {shipped} is below {floor:0.000}, the work the bare tile is " +
            "known to do — the DOP point is a lower bound and the constant sits under it");

        // the model's reading of the tile must stay inside the band its method earns
        var m = ArmorStandardTests.LadderMaterials["Ceramic"];
        var v = BallisticLimit.V50(LadderCalibrator.LadderBarrier(tile, m),
            ArmorFixture.CoreOf(tile.Threat), 1.0, tile.V50, BallisticLimit.Tuning.Default);
        Assert.InRange(v / tile.V50, 1.0 - tile.Band, 1.0 + tile.Band);
    }

    /// <summary>
    /// The finding phase six existed to produce, pinned so it cannot silently rot:
    /// with the backing split out and the hardness term gone from ceramics, the bare
    /// tile and the certified plates agree on what alumina is worth. Before the
    /// split they disagreed by 2.5x in work and the constant quietly carried the
    /// difference. If this drifts apart again, something has re-broken the layer
    /// arithmetic — do not fix it by moving BrittleK.
    /// </summary>
    [Fact]
    public void The_tile_and_the_certificates_tell_one_story_about_alumina()
    {
        var tile = ArmorStandardTests.Limits.Single(l => l.Material == "Ceramic");
        var tileK = LadderCalibrator.SolveK(tile);

        // the smallest constant that satisfies every ceramic certificate, found the
        // same way the shipped value was
        var t = BallisticLimit.Tuning.Default;
        var need = 0.0;
        foreach (var c in ArmorStandardTests.Certified)
        {
            var (barrier, _) = ArmorFixture.ByProduct(c.BookKey);
            if (barrier.Class != BallisticLimit.Brittle)
            {
                continue;
            }

            foreach (var threat in ArmorFixture.Threats(c.Standard, c.Class))
            {
                // work demanded at the test velocity, minus what the backing does,
                // over the ceramic geometry at unit constant
                var core = ArmorFixture.CoreOf(threat);
                var unit = t;
                unit.BrittleK = 1;
                var noBacking = barrier;
                noBacking.BackingMm = 0;
                var perK = BallisticLimit.WorkJ(noBacking, core, 1.0, threat.V, unit);
                var backing = BallisticLimit.WorkJ(barrier, core, 1.0, threat.V, unit) - perK;
                var demanded = 0.5 * (BallisticLimit.MassAgainst(barrier, core) / 1000.0) * threat.V * threat.V;
                var k = (demanded - backing) / perK;
                need = Math.Max(need, k);
            }
        }

        // one story: the certificates' requirement and the tile's floor are the same
        // number to within the tile's own band. The band is stated in velocity and a
        // constant is work, so the comparison happens in velocity space: k ~ v².
        Assert.InRange(Math.Sqrt(need / tileK), 1.0 - tile.Band, 1.0 + tile.Band);
    }
}
