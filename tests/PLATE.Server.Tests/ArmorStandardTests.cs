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
    /// <param name="GameClass">The in-game class this maps to. Since the Br realignment
    /// the game class IS the GOST class — Бр1..Бр6 are in-game 1..6, and the
    /// anti-fragment tier below every standard is class 0.</param>
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
    // Бр6 is 12.7x108 B-32, in-game class 6 — a rung nothing wearable earns, and the
    // top of the scale is deliberately empty for it.
    public static readonly Threat[] Gost =
    [
        new("GOST", "Бр1", 1, "9x18 Pst 57-N-181S", 5.9, 9.27, 335, 0.30, 1.0, 0, 250,
            "mild steel core in a lead sleeve, APS at 5 m"),
        new("GOST", "Бр2", 2, "9x21 P 7N28", 7.93, 9.02, 390, 0.35, 1.0, 0, 60,
            "LEAD core - the heaviest pistol bullet in the standard, and the softest"),
        new("GOST", "Бр3", 3, "9x19 Pst 7N21", 5.2, 9.00, 455, 0.20, 1.0, 0, 700,
            "hardened steel core; its mass and diameter are not published, so the bullet " +
            "has to be read as solid - only its hardness is known"),
        new("GOST", "Бр4", 4, "5.45x39 PP 7N10", 3.5, 5.62, 895, 0.15, 0.532, 0.478, 697,
            "hardened steel core 1.72-1.80 g, 4.1 mm, 60 HRC"),
        new("GOST", "Бр4", 4, "7.62x39 PS 57-N-231", 7.9, 7.92, 720, 0.25, 0.50, 0.468, 697,
            "65G core, heat-treated since 1989 under an unchanged index - and the 6B23 " +
            "certificate names the heat-hardened AKM core by that index, so this is what " +
            "Бр4 is actually shot with"),
        new("GOST", "Бр5", 5, "7.62x54R PP 7N13", 9.4, 7.92, 830, 0.10, 0.673, 0.463, 650,
            "U12A core 70 gr, 6.5 mm, 55-60 HRC"),
        new("GOST", "Бр5", 5, "7.62x54R B-32 7-BZ-3", 10.4, 7.92, 810, 0.10, 0.60, 0.60, 700,
            "armour-piercing incendiary, hardened steel core at 60 HRC"),
        new("GOST", "Бр6", 6, "12.7x108 B-32 57-BZ-542", 48.2, 12.98, 810, 0.10, 0.69, 0.61, 700,
            "armour-piercing incendiary, hardened U12A-grade core ~29.5 g; nothing " +
            "man-portable holds it, which is the point of keeping the rung"),
    ];

    // NIJ 0123.00, the threat schedule NIJ 0101.07 refers to. The mapping onto in-game
    // classes is the published GOST/NIJ crosswalk (Бр1 II-IIA, Бр2 IIIA-III, Бр3 III,
    // Бр4 III-IV, Бр5 IV) — a crosswalk between certificates, not between physics, and
    // the rifle end of it is where it shows.
    public static readonly Threat[] Nij =
    [
        new("NIJ", "HG1", 1, "9mm FMJ RN 124 gr", 8.0, 9.00, 398, 0.30, 1.0, 0, 60, "lead core, jacketed"),
        new("NIJ", "HG1", 2, ".357 Magnum JSP 158 gr", 10.2, 9.07, 436, 0.70, 1.0, 0, 60, "soft point"),
        new("NIJ", "HG2", 2, "9mm FMJ RN 124 gr", 8.0, 9.00, 448, 0.30, 1.0, 0, 60, "lead core, jacketed"),
        new("NIJ", "HG2", 2, ".44 Magnum SJHP 240 gr", 15.6, 10.90, 436, 0.90, 1.0, 0, 60, "semi-jacketed hollow point"),
        new("NIJ", "RF1", 3, "7.62x51 M80 ball 147 gr", 9.5, 7.85, 847, 0.25, 1.0, 0, 60, "lead alloy core"),
        new("NIJ", "RF1", 3, "7.62x39 MSC 120.5 gr", 7.9, 7.92, 732, 0.25, 1.0, 0.468, 390, "mild steel core"),
        new("NIJ", "RF1", 3, "5.56x45 M193 56 gr", 3.6, 5.70, 990, 0.30, 1.0, 0, 60, "lead core"),
        new("NIJ", "RF2", 4, "5.56x45 M855 62 gr", 4.0, 5.70, 950, 0.25, 1.0, 0.162, 410,
            "10 gr steel tip at 40-45 HRC - too soft to concentrate"),
        new("NIJ", "RF3", 5, ".30-06 M2 AP 165.7 gr", 10.7, 7.82, 878, 0.10, 0.55, 0.55, 730,
            "hardened steel core at 60+ HRC; diameter read at the M61's, the same core in the same calibre"),

        // NIJ 0101.06, the standard nearly every plate on the market is still certified
        // to. Kept apart from the 0101.07 classes above because they are not the same
        // promise: RF1 added 5.56 M193 at 990 and 7.62x39 MSC to what Level III had to
        // stop. A plate is tested against what it was tested against.
        new("NIJ", "III", 3, "7.62x51 M80 ball 147 gr", 9.5, 7.85, 847, 0.25, 1.0, 0, 60,
            "0101.06 Level III: M80 ball, six shots, standalone"),
        new("NIJ", "IV", 5, ".30-06 M2 AP 165.7 gr", 10.7, 7.82, 878, 0.10, 0.55, 0.55, 730,
            "0101.06 Level IV: one shot of .30-06 AP over a Level III backing"),
    ];

    public static IEnumerable<Threat> All => Gost.Concat(Nij);

    /// <summary>
    /// A product whose certificate is published, pointing at the entry the reference book
    /// holds it under.
    ///
    /// ArmorByClass is a derived thing — what thickness of a material the model believes a
    /// class needs. These are the other direction: real plates with a real thickness and a
    /// real certificate, and the only place where the model's belief and a manufacturer's
    /// promise can be made to disagree out loud. Every class here is stated by the book's
    /// own entry, in the prototype name or the source note; none is inferred from the
    /// class the game gives the item.
    /// </summary>
    public record Certificate(string BookKey, string Standard, string Class, string Note);

    public static readonly Certificate[] Certified =
    [
        // --- Russian, GOST R 50744-95 ---
        new("kora_kulon", "GOST", "Бр3", "Kora-Kulon, 4.3 mm steel, stated Br3"),
        new("granit4_zhukBr3_3class_front", "GOST", "Бр3", "Zhuk-3, 23 mm polyethylene"),
        new("granitBr4", "GOST", "Бр4", "Granit Br4 / Granit-5A, 6 mm ceramic"),
        new("korund", "GOST", "Бр4", "Korund-VM steel panel, 6.3 mm, stated Br4"),
        new("korund_vm", "GOST", "Бр4", "the same panel under the other item name"),
        new("granitBr5", "GOST", "Бр5", "Granit Br5, 6.8 mm ceramic at 4.1 g/cm²"),
        new("granit", "GOST", "Бр5", "Granit Br5 first execution, the heavy 7.7 mm"),
        new("granit4rs", "GOST", "Бр5", "Granit-4RS, the line's 4.1 g/cm²"),
        new("granit4_5class_front", "GOST", "Бр5", "Granit-4, the Br5 of the line"),
        new("granit4_5class_back", "GOST", "Бр5", "the same plate on the back"),

        // Panels built into a vest rather than dropped into a carrier. Worth having
        // because they are the older, thinner end of Russian armour and the model has
        // never been asked about them: a 6.3 mm steel back panel is a very different
        // object from a 6.8 mm ceramic plate, and both are sold as rifle protection.
        new("korund_back_6b23_2", "GOST", "Бр4",
            "6B23 back panel, 6.3 mm of 44S steel — ARMOR-TABLE gives it as Бр4"),

        // --- Western, NIJ 0101.06 ---
        new("SAPI_GAC_3s15m", "NIJ", "III", "HighCom Guardian 3s15m, NIJ III standalone"),
        new("SAPI_Monoclete_PE", "NIJ", "III", "Paraclete 10260, NIJ III standalone"),
        new("SAPI_SPRTN_Elaphros", "NIJ", "III", "Spartan Elaphros, NIJ III certified"),
        new("SAPI_GAC_4sss2", "NIJ", "IV", "HighCom Guardian 4sss2, NIJ IV to 0101.04"),
        new("SAPI_NESCO_4400", "NIJ", "IV",
            "HESCO 4400SA-MC, NIJ IV — the 4401 is the listed variant, this one is not"),

        // Adept publish this one as III+ against their own 'RF2', which is a 0101.07
        // class rather than a 0101.06 one
        new("SAPI_Cult_Locust", "NIJ", "RF2", "Adept Mantis, 17.8 mm titanium on a PE backer"),
    ];

    /// <summary>
    /// Certified products the model reads short of the STRICT criterion, each with its
    /// measured shortfall and its cause — physics, not a softened bar.
    ///
    /// The strict criterion (CertificationCriteria) demands V50 about 7-10% over the
    /// test velocity, and the model puts most certified plates just above their test
    /// velocity instead. Each entry is the fraction of the requirement the plate
    /// actually reaches, measured, plus a hair of head-room (0.002) against float
    /// noise — NOT rounded up generously, so a regression cannot hide inside an
    /// allowance. A guard test asserts every entry is still needed; a fixed plate must
    /// leave this table, not linger in it.
    /// </summary>
    public static readonly Dictionary<string, (double Reaches, string Cause)> CertShortfalls = new()
    {
        // Re-measured after the projectile was re-read: a bullet is now its measured
        // core plus five percent of the jacket, and the core is 570 HV rather than an
        // assumed 730. Every mode constant moved with it, and so did every entry here.
        // Three plates left this table because they now clear the bar on their own
        // (the Бр4 Granit, the Paraclete, and the polyethylene Бр4 rung); several class
        // rungs joined it, and the pattern in who joined is the finding — see the
        // ceramic and titanium notes below.

        // The steel trio is GONE, and it went the way this note said it would: "the
        // resolution is a published 44S strength, not a bigger constant". It was 750 MPa
        // shear underselling 44S — the figure belonged to AR500, the one steel the game's
        // material enum could name, and every steel plate in the game inherited it. 44S is
        // NII Stali's ultra-high-strength grade at 2250-2350 UTS and 2000-2100 yield, so
        // its shear is 1035, and the panel now clears zero-of-five on its own with no
        // allowance at all. The constant was never the problem.
        ["ArmoredSteel/5"] = (0.898,
            "the Бр5 steel rung against the B-32, 10% under; the same 44S arithmetic " +
            "one class up, where the hardest core in the standard arrives"),

        // The ceramic line sits under the strict bar with BrittleK at the BOTTOM of
        // the tile's band on purpose (the smallest constant the enforced certificates
        // allow, the Бр4 Granit binding). The binding round is the B-32 —
        // armour-piercing incendiary with a 60 HRC core — and that is no accident:
        // since the hardness term left the brittle mode (3.2), the model cannot let a
        // ceramic treat a hardened core differently, and the granits pay for it
        // exactly where the hardest core in the standard arrives. The measurement
        // that closes 3.2 would close most of this gap.
        //
        // Twice these entries have gone stale in the loose direction and been
        // re-measured. First the backing: the book never said what the backing was
        // MADE of, so every ceramic plate defaulted to aramid when the line is
        // alumina on UHMWPE, and every backing was built at packed = 1 — fixing both
        // made the assemblies stronger and left up to 13% of hidden head-room inside
        // these allowances, room a real regression could have crossed unseen. Then
        // BrittleK itself was re-derived (1.04 → 0.98) under the criterion these
        // very tests enforce. Every value below is the measurement at the shipped
        // constants minus the 0.002 float-noise hair, nothing more.
        ["granitBr5"] = (0.938, "B-32 binds at 6% under zero-of-five"),
        ["granit4_5class_front"] = (0.938, "the same plate"),
        ["granit4_5class_back"] = (0.938, "the same plate on the back"),
        ["granit4rs"] = (0.924, "the lightest execution of the line, B-32 7.3% under"),
        ["UHMWPE/4"] = (0.943,
            "a real 1.3-in Level III standalone PE plate wearing the Бр4 rung; the " +
            "5.45 binds, and fibre is the mode where the ladder and the products " +
            "disagree (3.1)"),
        ["Combined/4"] = (0.932,
            "the boron-carbide Бр4 rung, bound by the 7.62x39 PS because a brittle " +
            "barrier is not allowed to notice how hard a core is (3.2). It read " +
            "0.777 once, and most of that was never 3.2: the unnamed backing " +
            "material and the laminate-packed screen were carrying the difference, " +
            "and the erosion term's true cost is the 7% that remains"),
        ["Combined/5"] = (0.964, "the same rung one class up, B-32 binding"),
        ["Ceramic/5"] = (0.938, "the Бр5 alumina rung, which is the Granit's own reading"),

        // Titanium: the hardness exponent is derived from a rolled-armour ladder at
        // 320 HV and a 580 HV AR500 plate, and titanium sits at 350 — near the ladder,
        // far from the certificate, and with no ladder of its own beyond a single
        // point. What binds both rungs is a hardened core, which is exactly where an
        // exponent fitted at the two ends of the steel range has the least to say.
        ["Titan/4"] = (0.913,
            "the Бр4 titanium rung, 9% under - bound by the 7.62x39 PS since its core " +
            "was corrected to the hardened one, where it used to be bound by the 7N10"),
        ["Titan/5"] = (0.828, "the Бр5 rung against the B-32, 17% under"),

        // Western
        ["SAPI_Cult_Locust"] = (0.896,
            "titanium face over a PE backer, split derived from densities; its own " +
            "maker certifies III+/'RF2', read here at the six-shot protocol, and it " +
            "carries the titanium gap above as well. It read 0.849 while the M855's " +
            "410 HV tip was read as a rigid punch; the core-fate rework has it die " +
            "on the face, which is the physics its own certificate was owed"),
        ["SAPI_GAC_3s15m"] = (0.921,
            "fibre, where the ladder and the products disagree (3.1) — the constant " +
            "behind this plate is the model's weakest and it shows here first"),
    };

    /// <summary>
    /// Built-in vest panels whose class our own references do NOT pin down, and why.
    ///
    /// Not a to-do list — a boundary. ARMOR-TABLE carries a class column and writes it in
    /// the Бр scale wherever the modern scale is what the source meant; for these it
    /// holds the protection LAYOUT instead ("круговой", "диффер.") or a bare number from
    /// the old 1995 scale, whose crosswalk onto Бр1..Бр5 is not in any of our references.
    /// A bare "class 2" from 1995 is not Бр2, and guessing which it is would put an
    /// invented certificate into the one fixture whose whole job is to be right.
    ///
    /// What would close it: the crosswalk between the 1995 numbering and the 2014
    /// amendment, with its test cartridges — the same shape as the tables in Gost above.
    /// </summary>
    public static readonly (string BookKey, string Product, string WhatWeHave)[] Unpinned =
    [
        ("6b5-15", "6Б5-15 «Улей»", "13 mm boron carbide; ARMOR-TABLE says 'круговой', no class"),
        ("6b5-16", "6Б5-16 «Улей»", "6.5 mm titanium front; 'диффер.', no class"),
        ("6b3TM", "6Б3ТМ-01", "6.5 mm VT-23 titanium; 'диффер.', no class"),
        ("6b2", "6Б2 / Ж-81", "1.25 mm VT-14; 'класс 2' on the 1995 scale, not Бр2"),
        ("korund_6b12", "6Б12", "6 mm steel; '3 класс перёд' on the 1995 scale"),
        ("6b23-1", "6Б23-1 package", "30 layers TSVM-2; 'класс II' against TT and PMM, " +
                                     "neither of which is in the GOST table above"),
    ];

    /// <summary>
    /// A published ballistic limit: this thickness of this material turns this
    /// projectile back half the time at this velocity.
    /// </summary>
    /// <param name="Core">
    /// What the row assumed about the projectile's core — a strict contract, not four
    /// loose fields. There is no default: a row cannot be written without naming its
    /// assumption, and the calibrator refuses to derive one constant across rows whose
    /// assumptions differ (see CoreAssumption for why). Rows that share a construction
    /// share the same named static because their sources say so — not because it was
    /// convenient.
    /// </param>
    /// <param name="Band">
    /// How far the model may sit from this point before the row counts as missed. Not one
    /// number for all fifteen: the DRDO rows are finite-element results validated against
    /// depth-of-penetration trials and deserve a tight band, a depth-of-penetration
    /// figure converted into a limit deserves a loose one.
    /// </param>
    /// <param name="ArealDensityKgM2">
    /// Mass per unit area of the sample, for the fibre rows where it is the second half
    /// of the measurement. A fibre pack's thickness says nothing on its own — the same
    /// 7 mm is a sewn pack at half fibre or a pressed laminate at three quarters — so
    /// the packing fraction the model needs comes from here (kg/m² over mm is g/cm³
    /// exactly) rather than from a number somebody chose. 0 for the solid materials,
    /// where thickness IS the measurement.
    /// </param>
    public record BallisticLimit(
        string Material,
        double ThicknessMm,
        string Projectile,
        double ProjectileMassG,
        double ProjectileDiaMm,
        double V50,
        CoreAssumption Core,
        double Band,
        string Source,
        double ArealDensityKgM2 = 0)
    {
        /// <summary>This row's projectile, as the model takes it.</summary>
        public Threat Threat => new("ladder", "AP", 0, Projectile, ProjectileMassG,
            ProjectileDiaMm, V50, 0.10, Core.AreaFracOf(ProjectileDiaMm),
            Core.MassFracOf(ProjectileMassG), Core.HardnessHv, Core.SourceName);
    }

    /// <summary>
    /// The plate a ladder was actually shot at, in the model's own terms.
    ///
    /// These are NOT the game's materials and must not be confused with them: the RHA
    /// ladder is rolled homogeneous armour at about 300 HB, where the book's
    /// "ArmoredSteel" is AR500 at 500 HV — a different steel by half again in hardness.
    /// Firing the model's AR500 at the RHA ladder's velocities and calling the difference
    /// a calibration error is a mistake that has already been made once here.
    /// </summary>
    /// <param name="YieldMPa">
    /// Yield strength for the ductile hole-growth term; 0 for anything not ductile.
    /// Sourced like every strength in the book: a derivation rule or a named document,
    /// never a fitted number.
    /// </param>
    /// <param name="FailureStrain">
    /// Strain to failure for the fibrous term; 0 for anything not fibrous. StrengthMPa
    /// carries the fibre's tensile strength for those, the way it carries shear for a
    /// ductile and compressive for a brittle one.
    /// </param>
    /// <param name="Speaks">
    /// The game material this ladder is evidence about, or blank when it is evidence
    /// about nothing the mod ships. RHA speaks for the game's ArmoredSteel with a
    /// caveat; structural mild steel and 6082-T651 speak for nothing — they are the
    /// bottom of the ductile range and a rolled aerospace alloy, and neither is armour.
    /// The fixture's "what can this fixture not speak for" test reads this field rather
    /// than matching names, because a ladder's material name is the paper's, not ours.
    /// </param>
    /// <param name="FailureMode">
    /// For ductile alloys: ShearPlugging or HoleExpansion, decided by the alloy's
    /// strain-hardening reserve (UTS over yield — exhausted hardening localises and
    /// plugs, reserve flows; see the constants in BallisticLimit). Empty reads as
    /// ShearPlugging, which every armour alloy here is; structural mild steel at
    /// 450/250 = 1.8 is the fixture's one flowing metal.
    /// </param>
    public record LadderMaterial(string Class, double StrengthMPa, double YieldMPa,
        double HardnessHv, double DensityGCm3, string Source, double FailureStrain = 0,
        string Speaks = "", string FailureMode = "");

    public static readonly Dictionary<string, LadderMaterial> LadderMaterials = new()
    {
        // the book's own ArmoredSteel entry already carries these, in the note warning
        // that the ladder is RHA and not the game's armour steel: "300 HB, 450 MPa shear,
        // 320 HV". Taken from there rather than picked, so the fixture holds no number of
        // its own invention.
        ["ArmoredSteel"] = new("Ductile", 450, 900, 320, 7.85,
            "RHA at 300 HB — the book's own figure for it, not the game's 580 HV AR500; " +
            "yield ~900 MPa = 0.9·UTS(1000), the armour-steel yield ratio (MIL-DTL-12560 class)",
            Speaks: "ArmoredSteel"),
        ["MildSteel"] = new("Ductile", 270, 250, 158, 7.85,
            "structural mild steel at ~150 HB, UTS ~450 MPa; the bottom of the ductile " +
            "range. Yield 250 MPa per the structural grades (S235/A36 handbooks). " +
            "UTS/yield = 1.8: hardening reserve everywhere, so shear cannot localise " +
            "and the plate flows — the one HoleExpansion metal in the fixture, against " +
            "RHA at 1.11, Ti64 at 1.08 and 6082-T651 at 1.12, all of which plug",
            FailureMode: Services.BallisticLimit.HoleExpansion),
        ["Titan"] = new("Ductile", 550, 880, 350, 4.43,
            "Ti-6Al-4V, as the book has it: yield 880 MPa per ASM handbook / MMPDS",
            Speaks: "Titan"),
        ["Ceramic"] = new("Brittle", 2500, 0, 1500, 3.90, "95% alumina, as the book has it",
            Speaks: "Ceramic"),

        // The two aramid ladders are one fibre in two constructions, and they are held
        // apart on purpose: a woven pack and a unidirectional laminate of the same
        // Twaron are different objects at the same thickness, and mixing them would put
        // a 3.6 mm woven point next to a 3.5 mm laminate point of higher V50 and call
        // the pair a ladder that falls with thickness. What each row shares with its own
        // ladder is the construction; what all ten share is the fibre and the fragment.
        //
        // The fibre figures are the book's own aramid entry — Kevlar 29 at 2920 MPa and
        // 3.6% break elongation, read at 3.4% because a pack fails at the weave rather
        // than at the filament. Twaron 1000 and Kevlar 29 are the same generation of
        // standard-modulus para-aramid and our references publish the datasheet for one
        // of them; using the published one and saying so is the honest half-step, and it
        // is the same half-step the RHA row above takes.
        ["AramidWoven"] = new("Fibrous", 2900, 0, 0, 1.44,
            "Twaron CT612 WRT woven fabric, sewn into a soft pack; fibre read at the " +
            "book's Kevlar 29 figures (DuPont datasheet 2920 MPa, 3.6% break) because " +
            "Twaron 1000 is the same generation of para-aramid",
            FailureStrain: 0.034, Speaks: "Aramid"),
        ["AramidUD"] = new("Fibrous", 2900, 0, 0, 1.44,
            "Twaron UD42 unidirectional laminate — the same fibre pressed into cross-ply " +
            "sheets rather than woven, and the same datasheet figures",
            FailureStrain: 0.034, Speaks: "Aramid"),

        // The obliquity series' plate. Structural-aerospace 6082-T651, NOT the game's
        // armour aluminium (5083-H131 at 300 MPa yield and 120 HV): softer, weaker and
        // rolled for a different purpose. It is here because the only published set of
        // ballistic limits at four angles on one plate happens to be shot into it, and
        // an angle law is a ratio — see ObliquityTests for why that makes the alloy's
        // difference from the game's harmless.
        ["Al6082T651"] = new("Ductile", 174, 260, 90, 2.70,
            "AA6082-T651 as the REL V50 database records it from the trial: yield 260 " +
            "MPa, UTS 290, 90 HV, 2.70 g/cm³; shear 174 = 0.6·UTS, the aluminium rule " +
            "the book already uses for its own 5083"),
    };


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
        new("ArmoredSteel", 6, "7.62 AP", 10.0, 7.62, 320, CoreAssumption.M2Ap, 0.10,
            "RHA, FE validated against DOP trials"),
        new("ArmoredSteel", 8, "7.62 AP", 10.0, 7.62, 425, CoreAssumption.M2Ap, 0.10, "RHA"),
        new("ArmoredSteel", 10, "7.62 AP", 10.0, 7.62, 515, CoreAssumption.M2Ap, 0.10, "RHA"),
        new("ArmoredSteel", 12, "7.62 AP", 10.0, 7.62, 620, CoreAssumption.M2Ap, 0.10, "RHA"),
        new("ArmoredSteel", 14, "7.62 AP", 10.0, 7.62, 720, CoreAssumption.M2Ap, 0.10, "RHA"),
        new("ArmoredSteel", 16, "7.62 AP", 10.0, 7.62, 830, CoreAssumption.M2Ap, 0.10,
            "RHA; 18 mm only bulged at 854, the STANAG ceiling"),

        // Senthil et al., quoted in the same review. Mild steel, not armour steel —
        // the bottom of the ductile range, and useful precisely for that: any model
        // that cannot separate 300 HB from 600 HB will fit one and miss the other.
        //
        // Same projectile as the rows above, and that is the review's statement rather
        // than an assumption of convenience: both ladders are quoted side by side as
        // "7.62 AP" at 10.0 g. Live-fire rather than finite element, so a wider band.
        new("MildSteel", 4.7, "7.62 AP", 10.0, 7.62, 274, CoreAssumption.M2Ap, 0.15,
            "mild steel, normal incidence"),
        new("MildSteel", 6, "7.62 AP", 10.0, 7.62, 304.5, CoreAssumption.M2Ap, 0.15, "mild steel"),
        new("MildSteel", 10, "7.62 AP", 10.0, 7.62, 400.5, CoreAssumption.M2Ap, 0.15, "mild steel"),
        new("MildSteel", 12, "7.62 AP", 10.0, 7.62, 447.5, CoreAssumption.M2Ap, 0.15, "mild steel"),
        new("MildSteel", 16, "7.62 AP", 10.0, 7.62, 533, CoreAssumption.M2Ap, 0.15, "mild steel"),
        new("MildSteel", 20, "7.62 AP", 10.0, 7.62, 682.5, CoreAssumption.M2Ap, 0.15, "mild steel"),
        new("MildSteel", 25, "7.62 AP", 10.0, 7.62, 791, CoreAssumption.M2Ap, 0.15, "mild steel"),

        // Ti-6Al-4V. TIMETAL 6-4 was shot from 6 to 50 mm against .30 AP M2, .50 AP M2
        // and 14.5 B32, and the paper reports an excellent linear fit of V50 against
        // thickness (R2 = 0.997) without tabulating it — only the conclusion that the
        // alpha-beta alloys buy 30-40% of areal density over RHA. The single point
        // below is from a separate trial and is worth more than the summary: it puts
        // 14 mm of Ti64 at the ballistic limit for 7.62x51 AP at nominal velocity,
        // which is 16 mm of RHA on the ladder above, or half the areal density.
        // A different bullet from the two ladders above, and the source says so — which
        // is why this row carries its own named assumption. The calibrator will refuse
        // to fold this point into an M2Ap derivation without an explicit compensation,
        // and that refusal is working from day one: it is not a guard waiting for a
        // hypothetical mistake, it is the mistake the fixture nearly made once.
        new("Titan", 14, "7.62x51 AP flat-nose hardened core", 9.7, 7.85, 830,
            CoreAssumption.Ap8FlatNose, 0.15,
            "Ti-6Al-4V rolled plate, at the limit for nominal-velocity ammunition"),

        // Alumina over a metal backing, depth-of-penetration method, 7.62 AP at
        // 600-820 m/s: 7.2 mm left 1.1 mm of residual penetration and 9.1 mm left none.
        //
        // The widest band of the four, and not because the trial was poor: a depth of
        // penetration of nil is not a ballistic limit, it is a statement that the limit is
        // somewhere at or above the velocity fired. Reading it as V50 = 820 is already an
        // interpretation.
        new("Ceramic", 9.1, "7.62 AP", 10.0, 7.62, 820, CoreAssumption.M2Ap, 0.20,
            "95% alumina tile on a backing, nil residual DOP; 7.2 mm left 1.1 mm"),

        // --- Fibre, at last ---
        //
        // Para-aramid packs shot to STANAG 2920 with the .22 FSP. Kośla, Kubiak,
        // Łandwijt, Urbaniak and Kucharska-Jastrzabek, "Fragment-Resistant Property
        // Optimization within Ballistic Inserts Obtained on the Basis of Para-Aramid
        // Materials", Materials 2022, 15(6), 2314 (doi 10.3390/ma15062314), Table 5. Ten
        // points across two constructions of one fibre, every one of them carrying its
        // own areal density as well as its thickness.
        //
        // This closes the hole the fixture has been naming since it was written: fibre
        // was the one failure mode with no ladder at all, so its constant came off two
        // certificates and nothing measured whether the LAW was right. Now something
        // does, and what it says is not comfortable. The woven ladder's shape holds
        // (spread 1.09 across a 2.8x range of thickness); the laminate's does not; and
        // the constant the ten rows derive is 23.1 against the 27.5 the certificates
        // demand as a floor. A floor above a measurement is a contradiction, and it says
        // the T-linear fibre law under-rates thick packs — see the FibrousK comment in
        // BallisticLimit for why the constant stays where the certificates put it and
        // the miss is carried here instead.
        //
        // The band is the mild ladder's: live fire, a real laboratory, V50 by the
        // standard's own method rather than a residual-velocity fit. Two of the ten rows
        // are outside it and stay outside it — the laminate's thick end, exactly where
        // the ladder and the certificates pull hardest against each other.
        new("AramidWoven", 3.6, ".22 FSP", 1.10, 5.46, 438, CoreAssumption.Fsp22, 0.15,
            "Twaron CT612 WRT, 21 layers, STANAG 2920", ArealDensityKgM2: 2.5),
        new("AramidWoven", 6.5, ".22 FSP", 1.10, 5.46, 585, CoreAssumption.Fsp22, 0.15,
            "Twaron CT612 WRT, 38 layers", ArealDensityKgM2: 4.5),
        new("AramidWoven", 7.1, ".22 FSP", 1.10, 5.46, 620, CoreAssumption.Fsp22, 0.15,
            "Twaron CT612 WRT, 42 layers", ArealDensityKgM2: 5.0),
        new("AramidWoven", 8.2, ".22 FSP", 1.10, 5.46, 645, CoreAssumption.Fsp22, 0.15,
            "Twaron CT612 WRT, 48 layers", ArealDensityKgM2: 6.1),
        new("AramidWoven", 10.0, ".22 FSP", 1.10, 5.46, 700, CoreAssumption.Fsp22, 0.15,
            "Twaron CT612 WRT, 62 layers", ArealDensityKgM2: 7.5),

        // The laminate ladder, same paper and same table. Its last point is the one
        // that does not fit anything: 7.0 kg/m² in 6.8 mm is 1.03 g/cm³ where the other
        // four sit at 0.87-0.89, a 17% jump in packing for a 19% jump in thickness. The
        // row is kept as published — a fixture that drops the awkward point is not a
        // fixture — and the shape test says out loud that this is where the laminate
        // ladder breaks.
        new("AramidUD", 2.2, ".22 FSP", 1.10, 5.46, 368, CoreAssumption.Fsp22, 0.15,
            "Twaron UD42 unidirectional, 8 layers", ArealDensityKgM2: 1.9),
        new("AramidUD", 3.5, ".22 FSP", 1.10, 5.46, 455, CoreAssumption.Fsp22, 0.15,
            "Twaron UD42, 13 layers", ArealDensityKgM2: 3.1),
        new("AramidUD", 4.1, ".22 FSP", 1.10, 5.46, 495, CoreAssumption.Fsp22, 0.15,
            "Twaron UD42, 15 layers", ArealDensityKgM2: 3.6),
        new("AramidUD", 5.7, ".22 FSP", 1.10, 5.46, 540, CoreAssumption.Fsp22, 0.15,
            "Twaron UD42, 21 layers", ArealDensityKgM2: 5.0),
        new("AramidUD", 6.8, ".22 FSP", 1.10, 5.46, 600, CoreAssumption.Fsp22, 0.15,
            "Twaron UD42, 30 layers", ArealDensityKgM2: 7.0),
    ];

    /// <summary>
    /// What obliquity does to a ballistic limit, measured rather than assumed.
    ///
    /// The model has always lengthened the path by 1/cos θ and left it there. That is a
    /// real claim with a real consequence — for a plate that fails by plugging it says
    /// V50 rises exactly as sec θ, for a fibre pack (work linear in path) as the square
    /// root of it — and until now nothing in the fixture tested it, while a raid log
    /// showed one vest reading V50 anywhere from 767 to 1528 m/s on neighbouring hits
    /// with the angle carrying the whole spread. The angle moves outcomes harder than
    /// any constant here, and it was the only input with nothing behind it.
    /// </summary>
    /// <param name="AngleDeg">Obliquity from the plate normal, degrees.</param>
    public record ObliqueLimit(
        string Material,
        double ThicknessMm,
        string Projectile,
        double ProjectileMassG,
        double ProjectileDiaMm,
        int AngleDeg,
        double V50,
        CoreAssumption Core,
        string Source)
    {
        public Threat Threat => new("oblique", "AP", 0, Projectile, ProjectileMassG,
            ProjectileDiaMm, V50, 0.10, Core.AreaFracOf(ProjectileDiaMm),
            Core.MassFracOf(ProjectileMassG), Core.HardnessHv, Core.SourceName);

        public double Cos => Math.Cos(AngleDeg * Math.PI / 180.0);
    }

    /// <summary>
    /// One plate, one projectile, four angles — the only published series of that shape
    /// we could find, and it is a good one: Forrestal, Børvik, Warren and Chen,
    /// "Perforation of 6082-T651 Aluminum Plates with 7.62 mm APM2 Bullets at Normal and
    /// Oblique Impacts", Experimental Mechanics 54: 471-481 (2014), as recorded in the
    /// REL ballistic-limit database (Ryan et al., Defence Technology 2023; Mendeley
    /// doi 10.17632/4f92y6jzzh.2, CC BY 4.0).
    ///
    /// Both halves of the trial are kept. The paper fired complete APM2 bullets and, in
    /// a second series, the bare hardened cores; the model reads a bullet as its core
    /// dragging the rest of itself along, so having the same plate shot both ways is a
    /// check on that reading as well as on the angle law.
    /// </summary>
    public static readonly ObliqueLimit[] Obliquities =
    [
        new("Al6082T651", 20, "7.62 APM2 bullet", 10.7, 7.62, 0, 501,
            CoreAssumption.M2Ap, "Forrestal et al. 2014, complete bullet"),
        new("Al6082T651", 20, "7.62 APM2 bullet", 10.7, 7.62, 15, 516,
            CoreAssumption.M2Ap, "Forrestal et al. 2014"),
        new("Al6082T651", 20, "7.62 APM2 bullet", 10.7, 7.62, 30, 580,
            CoreAssumption.M2Ap, "Forrestal et al. 2014"),
        new("Al6082T651", 20, "7.62 APM2 bullet", 10.7, 7.62, 45, 718,
            CoreAssumption.M2Ap, "Forrestal et al. 2014"),

        new("Al6082T651", 20, "7.62 APM2 core", 5.3, 6.2484, 0, 514,
            CoreAssumption.M2Ap, "Forrestal et al. 2014, bare core"),
        new("Al6082T651", 20, "7.62 APM2 core", 5.3, 6.2484, 15, 535,
            CoreAssumption.M2Ap, "Forrestal et al. 2014, bare core"),
        new("Al6082T651", 20, "7.62 APM2 core", 5.3, 6.2484, 30, 597,
            CoreAssumption.M2Ap, "Forrestal et al. 2014, bare core"),
        new("Al6082T651", 20, "7.62 APM2 core", 5.3, 6.2484, 45, 723,
            CoreAssumption.M2Ap, "Forrestal et al. 2014, bare core"),
    ];

    /// <summary>
    /// The same question asked once more, of fourteen other plates: what one plate's
    /// V50 at 30° is over its own V50 at 0°.
    ///
    /// These are pairs from the same REL database and the same trials — one alloy or
    /// steel, one thickness, one projectile, shot at both angles — and they exist here
    /// because the model makes a claim the Forrestal series alone cannot test. In the
    /// plugging regime the obliquity factor cancels the material out entirely: work goes
    /// as the square of the path, so V50(θ)/V50(0) is sec θ for RHA, for aluminium and
    /// for a titanium plate alike. Fourteen materials from four laboratories either
    /// scatter around one number or they do not.
    /// </summary>
    public static readonly (string Material, double ThicknessMm, string Projectile,
        double V50At0, double V50At30, string Source)[] ObliquityPairs =
    [
        ("AA2060-T8", 21.6, "CAL30APM2", 642, 707, "Gallardy, ARL-MR-0930, 2016"),
        ("AA6055-T651", 19.0, "CAL30APM2", 543, 576, "Gallardy, ARL-MR-0904, 2015"),
        ("AA6055-T651", 19.1, "CAL30APM2", 532, 592, "Gallardy, ARL-MR-0904, 2015"),
        ("AA6082-T6", 20.0, "CAL30APM2", 501, 580, "Forrestal et al. 2014"),
        ("AA6082-T6 (core)", 20.0, "CAL30APM2 core", 514, 597, "Forrestal et al. 2014"),
        ("AA7017-T6", 19.3, "CAL30APM2", 564, 641, "Jones and Placzankis, ARL-TR-7727, 2016"),
        ("AA7017-T7", 20.0, "CAL30APM2", 569, 633, "Jones and Placzankis, ARL-TR-7727, 2016"),
        ("AA7085-T711", 18.5, "CAL30APM2", 598, 652, "Gallardy, ARL-TR-5952, 2012"),
        ("AA7085-T711", 18.6, "CAL30APM2", 598, 668, "Gallardy, ARL-TR-5952, 2012"),
        ("AA7085-T721", 18.5, "CAL30APM2", 567, 614, "Gallardy, ARL-TR-5952, 2012"),
        ("BIS UHH steel", 9.8, "CAL30APM2", 865, 965, "Ryan et al., IJIE 94:60-73, 2016"),
        ("BIS UHH steel", 12.2, "CAL50APM2", 686, 796, "Ryan et al., IJIE 94:60-73, 2016"),
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
        var limit = a.ClassULimitJmm2[t.GameClass];
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
    /// The 6B23's own certificate, cartridge by cartridge — the densest anchor in the
    /// corpus, because it is one plate of known thickness and known alloy against six
    /// named rounds. Everywhere else a certificate gives a class and the class gives one
    /// or two cartridges; here the maker lists the schedule.
    ///
    /// Velocities are muzzle, where the certificate names a range of 10 to 50 m. That is
    /// deliberate and it is the safe direction: a round arrives at 50 m slower than it
    /// leaves, so a plate that holds at muzzle holds at the certified range with room to
    /// spare. Reading it the other way would need a drag law per cartridge, which is one
    /// more model between the evidence and the test.
    /// </summary>
    public static readonly Threat[] Vest6B23 =
    [
        new("6B23", "cert", 0, "57-N-231 PS", 7.9, 7.92, 720, 0.25, 0.50, 0.468, 697,
            "the heat-hardened AKM core, at 10 m"),
        new("6B23", "cert", 0, "7N22 BP", 3.65, 5.62, 890, 0.08, 0.507, 0.477, 765,
            "U12A tool steel core, at 25 m"),
        new("6B23", "cert", 0, "M193", 3.75, 5.7, 957, 0.30, 1.0, 1.0, 60,
            "no hard element, at 25 m"),
        new("6B23", "cert", 0, "M855", 4.0, 5.7, 922, 0.25, 1.0, 0.162, 410,
            "steel tip over lead, at 25 m"),
        new("6B23", "cert", 0, "7N24 BS", 3.67, 5.62, 830, 0.05, 0.507, 0.512, 1300,
            "VK-8 tungsten carbide, at 50 m"),
        new("6B23", "cert", 0, "57-N-323S LPS", 9.6, 7.92, 865, 0.25, 1.0, 1.0, 60,
            "mild steel core, at 50 m"),
    ];

    /// <summary>
    /// A vest as it is actually certified: the hard element AND the fabric screen it sits
    /// in, against a cartridge its maker names. Two-sided where the maker is two-sided —
    /// a passport that says "the SVD goes through the chest" is worth more than one that
    /// only lists what is stopped, because a model can pass every positive gate by being
    /// uniformly too strong.
    ///
    /// ScreenMm is the fabric behind the plate; where the game already models it as its
    /// own item the plate entry carries none, so the assembly has to be stated here.
    /// </summary>
    public readonly record struct VestGate(
        string Vest, string PlateKey, double ScreenMm, Threat Round, bool MustHold, string Source);

    public static readonly VestGate[] VestGates =
    [
        // 6B23: 44S panel inside a 30-layer TSVM-2 screen. Six rounds, all "holds".
        .. Vest6B23.Select(r => new VestGate("6B23", "korund_back_6b23_2", 7.6, r, true,
            $"maker's schedule: {r.Source}")),

        // 6B3TM: thirteen 6.5 mm VT-23 tiles over 30 layers of TSVM-DZh. Its passport is
        // explicit about which era of core it was rated against — "стальными
        // нетермоупрочненными сердечниками" — which is the pre-1989 mild PS, not the one
        // the same index carries today.
        new("6B3TM", "6b3TM", 7.6,
            new Threat("6B3TM", "cert", 0, "57-N-231 PS mild", 7.9, 7.92, 720, 0.25, 1.0, 0.468, 390,
                "non-heat-treated core, at 10 m"),
            true, "круговая противопульная защита от пуль ПС 7,62x39 с дистанции 10 м"),

        // The other half of the same passport, and the rarer kind of statement: the SVD
        // goes through the chest section. A model that stops this is wrong in the
        // direction no positive gate can catch.
        new("6B3TM", "6b3TM", 7.6,
            new Threat("6B3TM", "cert", 0, "57-N-323S LPS", 9.6, 7.92, 865, 0.25, 1.0, 1.0, 60,
                "SVD through the chest"),
            false, "пуля из СВД могла пробить грудную секцию"),
    ];

    /// <summary>
    /// What each of those six is allowed to fall short by, and why. Empty on purpose
    /// and kept: a future shortfall belongs here, recorded, not in a loosened test.
    ///
    /// The 7N24 entry this table was created for (0.71 — "tungsten carbide on the
    /// hardness floor, 29% under") is gone the way such entries are supposed to go:
    /// the core-fate rework gave the term the decision it was missing. A VK-8 core at
    /// 1300 HV against 613 HV of 44S is a brittle solid against a face hard enough to
    /// crack it, and a cracked core is spread mass, not a punch — the certificate was
    /// the anchor that pinned ShatterRatio, and the panel now clears the strict
    /// criterion with no allowance at all.
    /// </summary>
    public static readonly Dictionary<string, (double Reaches, string Why)> Vest6B23Shortfalls =
        new();

    /// <summary>
    /// The finding this whole fixture was assembled to produce, and the reason stage
    /// three is a rewrite rather than a recalibration.
    ///
    /// GOST Бр4 names two cartridges. Both must be stopped by the same plate, and in
    /// specific energy they are not the same threat, so no single J/mm² threshold stops
    /// one without being wasteful about the other. Real armour manages it because a
    /// light fast core has little sectional density and erodes on the way in, which is a
    /// fact about the core's mass and diameter, not about its energy over its area. E/A
    /// cannot see it. v_bl(thickness, material, core mass, core diameter) can.
    ///
    /// **The margin shrank, and honestly so.** This read "more than twice as hard" while
    /// the 7.62x39 PS was modelled as a full-calibre soft slug. It is not one: the core
    /// has been heat-treated since 1989 under an unchanged index, so it meets a plate on
    /// its own 5.6 mm and the two cartridges now sit 1.35x apart rather than 2.2x. The
    /// argument survives — 35% is not nothing, and one plate still has to stop both — but
    /// it no longer carries itself, and the weight moves to the evidence that does: the
    /// RHA-over-mild ladder pair, the hardness separation, and titanium buying its class
    /// at half the areal density. A threshold below 1.25 would put the spread inside the
    /// uncertainty of the cartridge data itself, and then this test would be measuring
    /// nothing.
    /// </summary>
    [Fact]
    public void One_specific_energy_cannot_express_a_class_with_two_test_cartridges()
    {
        var a = Armor();
        var pair = Gost.Where(t => t.Class == "Бр4").ToArray();
        Assert.Equal(2, pair.Length);

        var high = pair.Max(t => t.SpecificEnergy(a.ExpansionOnArmor));
        var low = pair.Min(t => t.SpecificEnergy(a.ExpansionOnArmor));

        Assert.True(high / low > 1.25,
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
            var limit = a.ClassULimitJmm2[t.GameClass];
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
            .Where(t => t.SpecificEnergy(a.ExpansionOnArmor) > a.ClassULimitJmm2[t.GameClass])
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
    /// still does not have one for all of them. Naming the hole is the point.
    ///
    /// Aramid left this list when the para-aramid ladders arrived, and it left it on the
    /// ladder's terms rather than by renaming: a ladder speaks for a game material only
    /// where the fibre or the alloy really is the same thing, which is why the mild
    /// steel and 6082-T651 ladders speak for nothing. UHMWPE is still uncovered and that
    /// matters more than it looks — it is the pressed plates the certificates are built
    /// on, and the aramid ladder is what disagrees with them.
    /// </summary>
    [Fact]
    public void The_fixture_knows_which_materials_it_cannot_speak_for()
    {
        var covered = Limits
            .Select(l => LadderMaterials[l.Material].Speaks)
            .Where(s => s.Length > 0)
            .Distinct()
            .ToHashSet();
        var profiled = Armor().Materials.Keys.ToHashSet();

        Assert.Subset(profiled, covered);
        Assert.Equal(
            new[] { "Aluminium", "Combined", "Glass", "UHMWPE" },
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
