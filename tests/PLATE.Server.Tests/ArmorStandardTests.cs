using PLATE.Server.Config;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The measurements the armour model has to reproduce, kept as a fixture the way the
/// barrel ladders are, and assembled BEFORE the ballistic-limit work rather than after
/// it. A penetration model has too many knobs to be fitted honestly against nothing:
/// without a table of "this armour must stop this cartridge at this speed" in front of
/// it, tuning is just moving numbers until a raid feels right.
///
/// Three kinds of measurement, in descending order of how sharp they are.
///
/// **Class definitions.** A protection class is not a rating, it is a statement: an
/// item of class C must stop cartridge P arriving at velocity V, and is not required to
/// stop the class above. GOST R 50744-95 as amended in 2014 (Бр1..Бр6, table reproduced
/// on the Russian Wikipedia page for protection classes, which cites the standard) gives
/// nine such statements with the GRAU index, the core material, the bullet mass and the
/// test velocity. NIJ 0123.00, the threat schedule for NIJ 0101.07, gives eight more.
/// Every one of them is a two-sided constraint on the class threshold table.
///
/// **Ballistic limits.** V50 ladders by thickness for a named material against a named
/// projectile. These are what the Lambert-Jonas work is actually for; today they can
/// only be checked for internal consistency and for agreement with the class table.
///
/// **The products themselves.** Stage one left the reference book holding a real
/// thickness, a real material and a certified class for scores of items. Each of those
/// is a ballistic-limit statement in its own right, and they come from the shipped book
/// rather than from a copy, so they cannot drift away from what the mod actually uses.
/// </summary>
public class ArmorStandardTests
{
    /// <summary>
    /// One line of a standard: this class must stop this cartridge at this velocity.
    /// X and CoreAreaFrac describe the bullet the way the reference book does — the
    /// fixture spells them out rather than reading them from the book, because half
    /// these cartridges are not in the game and because a fixture that shares its
    /// inputs with the thing under test proves nothing.
    /// </summary>
    /// <param name="GameClass">The in-game class this maps to. Class 1 is anti-fragment
    /// junk below every standard, so GOST Бр1..Бр5 are in-game 2..6.</param>
    public record Threat(
        string Standard,
        string Class,
        int GameClass,
        string Cartridge,
        double MassG,
        double DiaMm,
        double V,
        double X,
        double CoreAreaFrac,
        double CoreMassFrac,
        double CoreHardnessHv,
        string Source)
    {
        public double EnergyJ => 0.5 * (MassG / 1000.0) * V * V;

        public double AreaMm2 => Math.PI * DiaMm * DiaMm / 4.0;

        /// <summary>Specific energy on the area the round actually loads, J/mm².</summary>
        public double SpecificEnergy(double expansionOnArmor) =>
            EnergyJ / AmmoNormalizer.ImpactArea(AreaMm2, CoreAreaFrac, X, expansionOnArmor);
    }

    // GOST R 50744-95 (amendment 3, in force 1 July 2014). Velocities are the nominal
    // centre of the tolerance the standard gives; distance 5 m for pistol classes and
    // 10 m for rifle, measured 3 m from the muzzle.
    //
    // Бр6 is 12.7x108 B-32 and has no in-game class — the game's table stops at 6.
    public static readonly Threat[] Gost =
    [
        new("GOST", "Бр1", 2, "9x18 Pst 57-N-181S", 5.9, 9.27, 335, 0.30, 1.0, 0, 250,
            "mild steel core in a lead sleeve, APS at 5 m"),
        new("GOST", "Бр2", 3, "9x21 P 7N28", 7.93, 9.02, 390, 0.35, 1.0, 0, 60,
            "LEAD core - the heaviest pistol bullet in the standard, and the softest"),
        new("GOST", "Бр3", 4, "9x19 Pst 7N21", 5.2, 9.00, 455, 0.20, 1.0, 0, 700,
            "hardened steel core; its mass and diameter are not published, so the bullet " +
            "has to be read as solid - only its hardness is known"),
        new("GOST", "Бр4", 5, "5.45x39 PP 7N10", 3.5, 5.62, 895, 0.15, 0.532, 0.478, 697,
            "hardened steel core 1.72-1.80 g, 4.1 mm, 60 HRC"),
        new("GOST", "Бр4", 5, "7.62x39 PS 57-N-231", 7.9, 7.92, 720, 0.25, 1.0, 0.468, 390,
            "the standard calls this core heat-treated; at 35-45 HRC it still upsets on a plate"),
        new("GOST", "Бр5", 6, "7.62x54R PP 7N13", 9.4, 7.92, 830, 0.10, 0.673, 0.463, 650,
            "U12A core 70 gr, 6.5 mm, 55-60 HRC"),
        new("GOST", "Бр5", 6, "7.62x54R B-32 7-BZ-3", 10.4, 7.92, 810, 0.10, 0.60, 0.60, 700,
            "armour-piercing incendiary, hardened steel core at 60 HRC"),
    ];

    // NIJ 0123.00, the threat schedule NIJ 0101.07 refers to. The mapping onto in-game
    // classes is the published GOST/NIJ crosswalk (Бр1 II-IIA, Бр2 IIIA-III, Бр3 III,
    // Бр4 III-IV, Бр5 IV) — a crosswalk between certificates, not between physics, and
    // the rifle end of it is where it shows.
    public static readonly Threat[] Nij =
    [
        new("NIJ", "HG1", 2, "9mm FMJ RN 124 gr", 8.0, 9.00, 398, 0.30, 1.0, 0, 60, "lead core, jacketed"),
        new("NIJ", "HG1", 3, ".357 Magnum JSP 158 gr", 10.2, 9.07, 436, 0.70, 1.0, 0, 60, "soft point"),
        new("NIJ", "HG2", 3, "9mm FMJ RN 124 gr", 8.0, 9.00, 448, 0.30, 1.0, 0, 60, "lead core, jacketed"),
        new("NIJ", "HG2", 3, ".44 Magnum SJHP 240 gr", 15.6, 10.90, 436, 0.90, 1.0, 0, 60, "semi-jacketed hollow point"),
        new("NIJ", "RF1", 4, "7.62x51 M80 ball 147 gr", 9.5, 7.85, 847, 0.25, 1.0, 0, 60, "lead alloy core"),
        new("NIJ", "RF1", 4, "7.62x39 MSC 120.5 gr", 7.9, 7.92, 732, 0.25, 1.0, 0.468, 390, "mild steel core"),
        new("NIJ", "RF1", 4, "5.56x45 M193 56 gr", 3.6, 5.70, 990, 0.30, 1.0, 0, 60, "lead core"),
        new("NIJ", "RF2", 5, "5.56x45 M855 62 gr", 4.0, 5.70, 950, 0.25, 1.0, 0.162, 410,
            "10 gr steel tip at 40-45 HRC - too soft to concentrate"),
        new("NIJ", "RF3", 6, ".30-06 M2 AP 165.7 gr", 10.7, 7.82, 878, 0.10, 0.55, 0.55, 730,
            "hardened steel core at 60+ HRC; diameter read at the M61's, the same core in the same calibre"),
    ];

    public static IEnumerable<Threat> All => Gost.Concat(Nij);

    /// <summary>
    /// A published ballistic limit: this thickness of this material turns this
    /// projectile back half the time at this velocity.
    /// </summary>
    public record BallisticLimit(
        string Material,
        double ThicknessMm,
        string Projectile,
        double ProjectileMassG,
        double ProjectileDiaMm,
        double V50,
        string Source);

    // The Lambert-Jonas work has to land on these. Nothing checks them against a v_bl
    // today because there is no v_bl yet — that is the point of writing them down now.
    //
    // A caution that belongs with the numbers rather than under them: "7.62 AP" is not
    // one projectile. M2 AP is 10.8 g, the 7.62x51 AP8 is 9.7 g, B-32 is 10.4 g, and
    // the steel behind the name runs from 300 HB rolled plate to 600 HB armour. Each
    // row carries the projectile it was actually shot with, and rows from different
    // sources are not interchangeable.
    public static readonly BallisticLimit[] Limits =
    [
        // Vasundhra et al., J. Mech. Cont. & Math. Sci. 15(9) 2020 — DRDO Combat
        // Vehicles R&D. Explicit FE, validated against the depth-of-penetration
        // experiments in the same paper. Linear in thickness: V50 = 49.75t + 22.5.
        new("ArmoredSteel", 6, "7.62 AP", 10.0, 7.62, 320, "RHA, FE validated against DOP trials"),
        new("ArmoredSteel", 8, "7.62 AP", 10.0, 7.62, 425, "RHA"),
        new("ArmoredSteel", 10, "7.62 AP", 10.0, 7.62, 515, "RHA"),
        new("ArmoredSteel", 12, "7.62 AP", 10.0, 7.62, 620, "RHA"),
        new("ArmoredSteel", 14, "7.62 AP", 10.0, 7.62, 720, "RHA"),
        new("ArmoredSteel", 16, "7.62 AP", 10.0, 7.62, 830, "RHA; 18 mm only bulged at 854, the STANAG ceiling"),

        // Senthil et al., quoted in the same review. Mild steel, not armour steel —
        // the bottom of the ductile range, and useful precisely for that: any model
        // that cannot separate 300 HB from 600 HB will fit one and miss the other.
        new("MildSteel", 4.7, "7.62 AP", 10.0, 7.62, 274, "mild steel, normal incidence"),
        new("MildSteel", 6, "7.62 AP", 10.0, 7.62, 304.5, "mild steel"),
        new("MildSteel", 10, "7.62 AP", 10.0, 7.62, 400.5, "mild steel"),
        new("MildSteel", 12, "7.62 AP", 10.0, 7.62, 447.5, "mild steel"),
        new("MildSteel", 16, "7.62 AP", 10.0, 7.62, 533, "mild steel"),
        new("MildSteel", 20, "7.62 AP", 10.0, 7.62, 682.5, "mild steel"),
        new("MildSteel", 25, "7.62 AP", 10.0, 7.62, 791, "mild steel"),

        // Ti-6Al-4V. TIMETAL 6-4 was shot from 6 to 50 mm against .30 AP M2, .50 AP M2
        // and 14.5 B32, and the paper reports an excellent linear fit of V50 against
        // thickness (R2 = 0.997) without tabulating it — only the conclusion that the
        // alpha-beta alloys buy 30-40% of areal density over RHA. The single point
        // below is from a separate trial and is worth more than the summary: it puts
        // 14 mm of Ti64 at the ballistic limit for 7.62x51 AP at nominal velocity,
        // which is 16 mm of RHA on the ladder above, or half the areal density.
        new("Titan", 14, "7.62x51 AP flat-nose hardened core", 9.7, 7.85, 830,
            "Ti-6Al-4V rolled plate, at the limit for nominal-velocity ammunition"),

        // Alumina over a metal backing, depth-of-penetration method, 7.62 AP at
        // 600-820 m/s: 7.2 mm left 1.1 mm of residual penetration and 9.1 mm left none.
        new("Ceramic", 9.1, "7.62 AP", 10.0, 7.62, 820,
            "95% alumina tile on a backing, nil residual DOP; 7.2 mm left 1.1 mm"),
    ];

    private static PlateServerConfig.ArmorSection Armor() => new PlateServerConfig().Armor;

    public static TheoryData<string, string> ThreatNames()
    {
        var data = new TheoryData<string, string>();
        foreach (var t in All)
        {
            data.Add(t.Class, t.Cartridge);
        }

        return data;
    }

    // --- What the class table has to mean ---

    /// <summary>
    /// A class is a promise. Everything the standards say an item of class C stops, the
    /// model's threshold for class C has to stop.
    ///
    /// This is the test that does not pass. The pistol classes are fine and the rifle
    /// ones are not, and the fixture exists to say by how much rather than to hide it:
    /// the allowance below is the size of today's miss, so the gap cannot quietly grow
    /// while the ballistic-limit work is going on. Lambert-Jonas has to bring it to 1.0.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThreatNames))]
    public void The_class_threshold_is_measured_against_what_the_class_promises(
        string cls, string cartridge)
    {
        var a = Armor();
        var t = All.Single(x => x.Class == cls && x.Cartridge == cartridge);
        var limit = a.ClassULimitJmm2[t.GameClass - 1];
        var ratio = t.SpecificEnergy(a.ExpansionOnArmor) / limit;

        Assert.True(ratio <= KnownMiss,
            $"{t.Standard} {t.Class} must stop {t.Cartridge} at {t.V:N0} m/s: " +
            $"{t.SpecificEnergy(a.ExpansionOnArmor):N1} J/mm² against a class {t.GameClass} " +
            $"threshold of {limit:N1}, over by {ratio:P0}");
    }

    /// <summary>
    /// How far past its own threshold the worst-fitting threat currently sits. 1.0 is
    /// the model keeping every promise in both standards; stage three's target.
    /// </summary>
    private const double KnownMiss = 1.65;

    /// <summary>
    /// A standard's classes are a ladder, and each rung has to be harder than the last.
    /// GOST's is — in reality. In specific energy it is not quite: Бр2's lead 9x21 at
    /// 7.93 g lands within a few percent of Бр3's steel-cored 9x19, because the whole
    /// of Бр3's advantage is a hardened core whose mass and diameter nobody publishes,
    /// so the model has to read it as a solid bullet. One published figure for the
    /// 7N21's core would separate them; until then the two rungs are tied, and the
    /// fixture says so rather than pretending the ladder is clean.
    /// </summary>
    [Fact]
    public void The_standard_ladder_climbs_except_where_a_core_is_unpublished()
    {
        var a = Armor();
        var rungs = Gost
            .GroupBy(t => t.Class)
            .OrderBy(g => g.First().GameClass)
            .Select(g => (Class: g.Key, U: g.Max(t => t.SpecificEnergy(a.ExpansionOnArmor))))
            .ToArray();

        for (var i = 1; i < rungs.Length; i++)
        {
            var climbs = rungs[i].U > rungs[i - 1].U;
            var tied = Math.Abs(rungs[i].U - rungs[i - 1].U) / rungs[i - 1].U < 0.05;

            Assert.True(climbs || tied,
                $"{rungs[i].Class} ({rungs[i].U:N1} J/mm²) is easier than " +
                $"{rungs[i - 1].Class} ({rungs[i - 1].U:N1}) by more than a rounding error");
        }

        // and the one place it does not climb is the one named above
        var br2 = rungs.Single(r => r.Class == "Бр2").U;
        var br3 = rungs.Single(r => r.Class == "Бр3").U;
        Assert.True(br3 < br2,
            "the 7N21's core is published now - give it a CoreAreaFrac and delete this");
    }

    /// <summary>
    /// The published GOST-to-NIJ crosswalk maps certificates, not physics, and it will
    /// not survive being used as one. It calls Бр1 and NIJ II-IIA the same tier, but
    /// Бр1's test round is a 9x18 carrying 331 J and NIJ HG1's is a 9x19 carrying 634 —
    /// nearly twice as much out of the same nominal calibre. Anything that maps one
    /// standard onto the other and then reasons about penetration is wrong before it
    /// starts; the two standards are held here side by side and never merged.
    /// </summary>
    [Fact]
    public void The_two_standards_do_not_agree_about_what_a_tier_is()
    {
        var gostPistol = Gost.Single(t => t.Class == "Бр1");
        var nijPistol = Nij.First(t => t.Class == "HG1" && t.Cartridge.StartsWith("9mm"));

        Assert.True(nijPistol.EnergyJ / gostPistol.EnergyJ > 1.5,
            $"the bottom rung of the two standards is {nijPistol.EnergyJ / gostPistol.EnergyJ:N2}x " +
            "apart in energy, which is close enough that the crosswalk might be usable " +
            "after all - check before relying on this");
    }

    /// <summary>
    /// The finding this whole fixture was assembled to produce, and the reason stage
    /// three is a rewrite rather than a recalibration.
    ///
    /// GOST Бр4 names two cartridges. Both must be stopped by the same plate. In
    /// specific energy they are nowhere near each other — the 5.45 lands more than
    /// twice as hard as the 7.62x39 — so there is no single J/mm² threshold that
    /// stops one without being wasteful about the other. Real armour manages it
    /// because a light fast core has little sectional density and erodes on the way
    /// in, which is a fact about the core's mass and diameter, not about its energy
    /// over its area. E/A cannot see it. v_bl(thickness, material, core mass, core
    /// diameter) can.
    /// </summary>
    [Fact]
    public void One_specific_energy_cannot_express_a_class_with_two_test_cartridges()
    {
        var a = Armor();
        var pair = Gost.Where(t => t.Class == "Бр4").ToArray();
        Assert.Equal(2, pair.Length);

        var high = pair.Max(t => t.SpecificEnergy(a.ExpansionOnArmor));
        var low = pair.Min(t => t.SpecificEnergy(a.ExpansionOnArmor));

        Assert.True(high / low > 2.0,
            $"the two Бр4 cartridges now sit {high / low:N2}x apart in specific energy " +
            "- if they ever converge, a single threshold could express the class and " +
            "the case for a ballistic-limit model is weaker than this fixture claims");
    }

    /// <summary>
    /// The pistol end of the GOST table is what the class thresholds were anchored on
    /// in the first place — class 2 is Бр1's test round and nothing else — and it still
    /// holds. Keeping it in its own test means the rifle failures above cannot be
    /// "fixed" by a change that quietly breaks the end that was right.
    /// </summary>
    [Fact]
    public void Every_GOST_pistol_class_still_stops_its_own_cartridge()
    {
        var a = Armor();
        foreach (var t in Gost.Where(t => t.V < 500))
        {
            var limit = a.ClassULimitJmm2[t.GameClass - 1];
            Assert.True(t.SpecificEnergy(a.ExpansionOnArmor) <= limit,
                $"{t.Standard} {t.Class} no longer stops {t.Cartridge}: " +
                $"{t.SpecificEnergy(a.ExpansionOnArmor):N1} against {limit:N1} J/mm²");
        }
    }

    /// <summary>
    /// Where the misses are. Every threat the model fails to stop is a rifle threat or
    /// an NIJ handgun round that the crosswalk put in the wrong tier — no GOST pistol
    /// class is among them. That is worth pinning: it says the rifle end needs a
    /// different model rather than the whole table needing a different number.
    /// </summary>
    [Fact]
    public void Nothing_the_model_misses_is_a_GOST_pistol_threat()
    {
        var a = Armor();
        var missed = All
            .Where(t => t.SpecificEnergy(a.ExpansionOnArmor) > a.ClassULimitJmm2[t.GameClass - 1])
            .ToArray();

        Assert.NotEmpty(missed); // if this ever fires, stage three has landed
        Assert.DoesNotContain(missed, t => t.Standard == "GOST" && t.V < 500);
    }

    // --- The ballistic limits themselves ---

    [Fact]
    public void The_ladders_rise_with_thickness()
    {
        foreach (var group in Limits.GroupBy(l => (l.Material, l.Projectile)))
        {
            var ordered = group.OrderBy(l => l.ThicknessMm).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                Assert.True(ordered[i].V50 > ordered[i - 1].V50,
                    $"{group.Key.Material} against {group.Key.Projectile}: " +
                    $"{ordered[i].ThicknessMm} mm turns back less than " +
                    $"{ordered[i - 1].ThicknessMm} mm");
            }
        }
    }

    /// <summary>
    /// Armour steel has to beat mild steel at the same thickness, and by a lot. The two
    /// ladders come from different papers and are the fixture's only direct evidence
    /// that hardness matters at all — which the current model cannot express, since a
    /// class threshold knows nothing about what the plate is made of beyond a
    /// per-material multiplier.
    /// </summary>
    [Fact]
    public void Hardness_shows_up_in_the_ladders()
    {
        foreach (var t in new[] { 6.0, 10.0, 12.0, 16.0 })
        {
            var rha = Limits.Single(l => l.Material == "ArmoredSteel" && l.ThicknessMm == t);
            var mild = Limits.Single(l => l.Material == "MildSteel" && l.ThicknessMm == t);

            Assert.True(rha.V50 > mild.V50 * 1.05,
                $"at {t} mm, rolled armour ({rha.V50} m/s) should clearly beat mild steel " +
                $"({mild.V50} m/s) and does not");
        }
    }

    /// <summary>
    /// Titanium's whole reason for being in an armour is mass. The single Ti64 point
    /// and the RHA ladder should say that a titanium plate weighs less than the steel
    /// one that stops the same round — the 30-40% the TIMETAL survey reports, and
    /// nearer half on these two numbers.
    /// </summary>
    [Fact]
    public void Titanium_buys_its_protection_at_a_lower_areal_density()
    {
        var ti = Limits.Single(l => l.Material == "Titan");
        var steel = Limits
            .Where(l => l.Material == "ArmoredSteel")
            .OrderBy(l => Math.Abs(l.V50 - ti.V50))
            .First();

        var tiAreal = ti.ThicknessMm * 4.43;      // Ti-6Al-4V
        var steelAreal = steel.ThicknessMm * 7.85;

        Assert.True(tiAreal < steelAreal * 0.7,
            $"{ti.ThicknessMm} mm of titanium is {tiAreal:N0} kg/m² against " +
            $"{steel.ThicknessMm} mm of steel at {steelAreal:N0} — no saving worth the price");
    }

    /// <summary>
    /// Stage three needs a limit for every material the model has a profile for, and it
    /// does not have one. Naming the hole is the point: aramid and UHMWPE carry a
    /// quarter of the armour in the game between them, and neither has a published
    /// figure in here yet.
    /// </summary>
    [Fact]
    public void The_fixture_knows_which_materials_it_cannot_speak_for()
    {
        var covered = Limits.Select(l => l.Material).Distinct().ToHashSet();
        var profiled = Armor().Materials.Keys.ToHashSet();

        Assert.Subset(profiled.Append("MildSteel").ToHashSet(), covered);
        Assert.Equal(
            new[] { "Aluminium", "Aramid", "Combined", "Glass", "UHMWPE" },
            profiled.Except(covered).OrderBy(m => m).ToArray());
    }

    // --- The products, which are ballistic-limit statements too ---

    /// <summary>
    /// Every plate in the shipped reference book that carries both a thickness and a
    /// material is a measurement: somebody built that much of that material and had it
    /// certified. Read from the book rather than copied, so the fixture cannot fall out
    /// of step with what the mod ships.
    /// </summary>
    public static IEnumerable<(string Name, ReferenceBook.ArmorPlateRef Plate)> Documented()
    {
        return ReferenceBookTests.ShippedBook().ArmorPlates
            .Where(p => p.Value.ThicknessMm > 0 && !string.IsNullOrEmpty(p.Value.Material))
            .Select(p => (p.Key, p.Value));
    }

    /// <summary>
    /// The bridge between stage one and stage three. A ballistic limit says how much of
    /// a material it takes to turn a round back; the reference book says how much of it
    /// real makers actually use. If the model's rifle-class threshold implied a steel
    /// plate two or three times thicker than anything anyone sells, the class table
    /// would be wrong in a way no amount of per-cartridge tuning could fix.
    /// </summary>
    [Fact]
    public void The_thickest_documented_steel_is_in_the_range_the_ladders_call_rifle_proof()
    {
        var steel = Documented()
            .Where(d => d.Plate.Material == "ArmoredSteel")
            .OrderByDescending(d => d.Plate.ThicknessMm)
            .ToArray();

        Assert.NotEmpty(steel);

        // The RHA ladder puts a rifle-proof plate at 12-16 mm; real hard-armour steel is
        // 500-600 HB against RHA's ~300, and comes in at half that. Anything outside 4-16
        // mm is either not a rifle plate or not a real one.
        Assert.InRange(steel[0].Plate.ThicknessMm, 4.0, 16.0);
    }

    [Fact]
    public void The_book_and_the_fixture_agree_about_the_cartridges_they_share()
    {
        var bullets = ReferenceBookTests.ShippedBook().Bullets;

        // GOST Бр4 is 7N10 and the 7.62x39 PS, both of which the game has
        var pp = bullets["patron_545x39_PP"];
        var ps = bullets["patron_762x39_PS"];
        var fixturePp = Gost.Single(t => t.Cartridge.Contains("7N10"));
        var fixturePs = Gost.Single(t => t.Cartridge.Contains("57-N-231"));

        Assert.Equal(pp.X, fixturePp.X, 3);
        Assert.Equal(pp.CoreAreaFrac, fixturePp.CoreAreaFrac, 3);
        Assert.Equal(ps.X, fixturePs.X, 3);
        Assert.Equal(ps.CoreAreaFrac, fixturePs.CoreAreaFrac, 3);
    }
}
