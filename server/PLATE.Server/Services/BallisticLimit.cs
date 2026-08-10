using System;

namespace PLATE.Server.Services;

/// <summary>
/// The velocity at which a projectile just gets through a barrier, and what is left of
/// it when it does.
///
/// This replaces a threshold and a price that were independent constants. A specific
/// energy against a class number cannot express what a plate does, and GOST says so in
/// its own table: Бр4 names two cartridges that one plate must stop, and in J/mm² they
/// are 2.7x apart. The 5.45 lands harder per unit area and is the easier of the two to
/// stop, because a light fast core has little behind it. That is a fact about the core's
/// mass and diameter, and E/A cannot see it.
///
/// **The laws.** A ductile alloy that localises shear plugs — the barrier is sheared
/// around the projectile's perimeter, through its own thickness:
///
///     W  = k · S · π · d · T²
///     v_bl = sqrt(2W / m) = T · sqrt(2 k S π d / m)
///
/// Linear in thickness, and falling with the square root of sectional density. The SHAPE
/// came off the rolled-armour ladder in ArmorStandardTests — six thicknesses from 6 to
/// 16 mm against 7.62 AP — where it holds to 3% on one constant, against 24% for a
/// T^0.75 power law of the Lambert-Jonas kind.
///
/// A ductile alloy that CANNOT localise — strain-hardening reserve left, see the
/// FailureMode constants — flows aside instead, and the work is the yield stress over
/// the hole's volume:
///
///     W = k_h · σ_y · (π d²/4) · T,   v_bl ∝ √T
///
/// Which law a plate obeys is the alloy's metallurgy, carried as data. It is never
/// decided by comparing the two works: the mild-steel ladder follows the flow law from
/// its thinnest point, where plugging would be cheaper, and a min() sat here for
/// exactly one calibration before that ladder refuted it.
///
/// **What S is** depends on how the barrier fails, which is what ArmorMaterialRef.Class
/// (and, within ductile, FailureMode) says: a plugging metal shears, a flowing one
/// yields, a ceramic crushes, a fibre stretches. Four mechanisms, four strengths, one
/// constant each.
///
/// **Hardness.** A core softer than the plate loses: it upsets on the face and stops
/// being a punch. This is the whole difference between a 7.62x39 PS at 40 HRC and a
/// 7N10 at 60 HRC out of plates of the same steel, and without it no single constant
/// fits both the armour-steel ladder and the GOST classes — they sit a factor of 3.8
/// apart in energy per mm² otherwise.
///
/// Sources: Recht &amp; Ipson 1963 for the residual velocity; Lambert &amp; Jonas 1976 for
/// the generalised form v_r = a(v^p − v_bl^p)^(1/p), of which Recht-Ipson is p = 2.
/// </summary>
public static class BallisticLimit
{
    /// <summary>
    /// How much of a sewn fabric package is actually fibre. A stitched aramid screen sits
    /// at 0.63 g/cm³ against the fibre's own 1.44, so 44% of it works and the rest is air
    /// between the layers — the same rule the face already obeys through PackedFraction.
    /// A pressed polyethylene laminate is essentially solid and does not use this.
    ///
    /// It exists because both the client and the fixture used to build a backing at
    /// packed = 1, crediting every vest screen with more than twice the fibre it has.
    /// </summary>
    public const double SewnPacked = 0.44;

    /// <summary>How the barrier gives way. Matches ArmorMaterialRef.Class.</summary>
    public const string Ductile = "Ductile";
    public const string Brittle = "Brittle";
    public const string Fibrous = "Fibrous";

    /// <summary>
    /// The two ways a DUCTILE metal gives way — a property of the alloy's metallurgy,
    /// never an outcome of arithmetic (Barrier.FailureMode).
    ///
    /// Plugging by adiabatic shear needs the deformation to LOCALISE, and what decides
    /// that is the alloy's strain-hardening reserve, read as UTS over yield. An alloy
    /// whose hardening is exhausted — by quenching, by ageing, by cold work — localises
    /// and shears a plug out: RHA at 1000/900, Ti-6Al-4V at 950/880, AR500 at
    /// 1650/1250, 6082-T651 at 290/260 all sit at 1.1-1.3, and the 6082 obliquity
    /// trial confirms the plug law's sec θ scaling to 1.5%. An alloy with reserve left
    /// hardens wherever shear tries to start, so no band forms and the material flows
    /// aside instead — structural mild steel at 450/250 = 1.8 is the fixture's one
    /// example, and its ladder follows the flow law from its thinnest point.
    /// </summary>
    public const string ShearPlugging = "ShearPlugging";
    public const string HoleExpansion = "HoleExpansion";

    /// <summary>
    /// Everything the law needs that is not the projectile. Kept as a struct so the
    /// client can fill it from the wire and the tests from the reference book.
    /// </summary>
    public struct Barrier
    {
        /// <summary>Ductile | Brittle | Fibrous.</summary>
        public string Class;

        /// <summary>
        /// For a Ductile barrier: ShearPlugging or HoleExpansion — which of the two
        /// ductile laws this alloy obeys. A property of the material's metallurgy,
        /// carried as data from the reference book; empty (an older wire payload)
        /// reads as ShearPlugging, which every armour alloy in the book is. Ignored
        /// for Brittle and Fibrous, whose class already names their one mechanism.
        /// </summary>
        public string FailureMode;

        /// <summary>Thickness of the hard element, mm.</summary>
        public double ThicknessMm;

        /// <summary>Ductile, ShearPlugging: ultimate shear strength, MPa.</summary>
        public double ShearMPa;

        /// <summary>Ductile, HoleExpansion: yield strength, MPa — what radial flow costs.</summary>
        public double YieldMPa;

        /// <summary>Brittle: compressive strength, MPa.</summary>
        public double CompressiveMPa;

        /// <summary>Fibrous: fibre tensile strength, MPa.</summary>
        public double FibreTensileMPa;

        /// <summary>Fibrous: strain to failure.</summary>
        public double FailureStrain;

        /// <summary>Vickers hardness of the barrier; 0 leaves the hardness term out.</summary>
        public double HardnessHv;

        /// <summary>g/cm³ — for the mass of the plug the projectile drags with it.</summary>
        public double DensityGCm3;

        /// <summary>
        /// How much of a fibre barrier is actually fibre, by volume. A sewn package sits
        /// at 0.63 g/cm³ against aramid's own 1.44, so 44% of it is fibre and the rest is
        /// air between the layers; a pressed polyethylene plate is essentially all fibre.
        /// Only the fibre does any work, and without this the same constant cannot fit
        /// both a 7.6 mm vest package stopping a 9x18 and a 33 mm plate certified against
        /// M80 — they come out 2.4x apart, which is exactly the ratio of their packing.
        /// 1 = solid.
        /// </summary>
        public double PackedFraction;

        /// <summary>
        /// Fibre backing behind the face, mm. 0 = a single-layer barrier. A ceramic
        /// plate is a strike face bonded to a fibre panel and the two do different
        /// work: the face erodes and blunts the core, the backing catches what is
        /// left. Reading the whole product as ceramic made the one western plate whose
        /// full thickness was published come out three times stronger than its
        /// certificate.
        /// </summary>
        public double BackingMm;

        /// <summary>Fibre tensile strength of the backing, MPa.</summary>
        public double BackingTensileMPa;

        /// <summary>Strain to failure of the backing fibre.</summary>
        public double BackingStrain;

        /// <summary>How much of the backing is fibre; 1 = consolidated laminate.</summary>
        public double BackingPacked;
    }

    /// <summary>
    /// What is actually driving the penetration, from the bullet and its construction.
    ///
    /// The area fraction decides this, and it decides it for a reason the book already
    /// states. A fraction below 1 means a core hard enough to keep its shape: it punches,
    /// the jacket strips at the face, and the figure of merit is the core's own sectional
    /// density. A fraction of exactly 1 means there is no penetrator — the M855's tip is
    /// 40 HRC and the mass behind it is lead, which pushes rather than strips — so the
    /// bullet arrives as one piece at full calibre.
    ///
    /// Reading the M855 the other way, as its 0.65 g tip at 5.7 mm calibre, put its
    /// ballistic limit against a titanium plate at 2847 m/s. Nothing in the game leaves
    /// a barrel above 1220, so the round could not have defeated any titanium at all.
    ///
    /// **How much mass the core brings with it is measured, not chosen.** Forrestal et
    /// al. shot 20 mm of 6082-T651 with complete 7.62 APM2 bullets and, separately, with
    /// their stripped 5.3 g cores, at four angles. The limits came out within a few
    /// percent of each other — 501 m/s against 514 at normal incidence, 718 against 723
    /// at 45° — so half the bullet's mass changes almost nothing about whether it gets
    /// through. In the model's own terms that is (514/501)² = 1.05: the bullet behaves
    /// like its core plus five percent of everything else, and JacketCarry is that five
    /// percent.
    ///
    /// This replaces "the whole bullet, always", whose stated reason was that reading a
    /// 7N10 as its bare 1.7 g core made the round HARDER to stop than the same bullet
    /// with no core described. That is still true and it is still not a defence: it is an
    /// argument about the model's internal consistency against a measurement, and the
    /// measurement wins. What it costs is that every mode constant had to be derived
    /// again, because all of them were fitted with whole-bullet masses.
    /// </summary>
    public static Core Driving(double massG, double diaMm, double coreAreaFrac,
        double coreMassFrac, double hardnessHv, Tuning t)
    {
        var penetrator = coreAreaFrac > 0 && coreAreaFrac < 1;

        // A core mass of zero is not "no core" — it is "nobody published one", which is
        // the case for the 7N21 and every round the book describes by area alone. The
        // whole bullet is then the only honest reading.
        var known = penetrator && coreMassFrac > 0 && coreMassFrac < 1;
        var coreMassG = massG * coreMassFrac;

        return new Core
        {
            MassG = known ? coreMassG + t.JacketCarry * (massG - coreMassG) : massG,
            BulletMassG = massG,

            // The core is what the plate has to make room for, and only when it is hard
            // enough to keep its shape. Everything else arrives at its own calibre.
            DiaMm = penetrator ? diaMm * Math.Sqrt(coreAreaFrac) : diaMm,
            HardnessHv = hardnessHv,
        };
    }

    /// <summary>The projectile as the barrier meets it: the core, not the cartridge.</summary>
    public struct Core
    {
        /// <summary>
        /// Mass carrying the penetration through a plate that is PUNCHED, g: the core
        /// plus the little of the jacket that goes with it.
        /// </summary>
        public double MassG;

        /// <summary>
        /// The whole bullet, g — the mass a barrier that does not get punched has to
        /// deal with. See MassAgainst for which barriers those are.
        /// </summary>
        public double BulletMassG;

        /// <summary>Diameter of what is doing the punching, mm.</summary>
        public double DiaMm;

        /// <summary>Vickers hardness of the core; 0 leaves the hardness term out.</summary>
        public double HardnessHv;
    }

    /// <summary>
    /// Free constants, one per failure MECHANISM plus the hardness clamp. Not one per
    /// material class: ductile metal has two ways to give way — shearing a plug out and
    /// being pushed aside — and each carries its own constant. The calibration rule is
    /// "strengths are published, one free constant per failure mechanism"; a second
    /// constant on the same mechanism would be a knob, a first constant on a second
    /// mechanism is physics.
    /// </summary>
    public struct Tuning
    {
        public double DuctileK;

        /// <summary>
        /// The ductile hole-expansion constant — the second ductile mechanism's own,
        /// on a par with DuctileK for plugging. Which mechanism a plate obeys is the
        /// alloy's FailureMode, decided by metallurgy and carried as data; there is no
        /// cost race between the two laws. It was min() once, and the mild-steel
        /// ladder refuted that from its thinnest point: the data follow the flow law
        /// even where plugging would be cheaper, because a high-hardening alloy
        /// cannot localise shear at any price.
        /// </summary>
        public double HoleGrowthK;

        public double BrittleK;
        public double FibrousK;

        /// <summary>
        /// How the fibre in a pack shares out the work, as a power of how densely it is
        /// packed. Not 1: a sewn package at 0.48 fibre by volume and a pressed laminate
        /// at 0.61 sit on the same published ladder against the same fragment, and the
        /// constant they each demand differs by exactly what this exponent absorbs. A
        /// loose package pulls a wider cone of yarns into the stretch, so halving the
        /// fibre in a given thickness does not halve the work it does.
        /// </summary>
        public double PackingExponent;

        /// <summary>
        /// How much of the non-core mass of a bullet the core carries through the plate.
        /// Measured — see Driving — and small: a jacket strips at the face rather than
        /// pushing its core onward.
        /// </summary>
        public double JacketCarry;

        /// <summary>Bounds on the plate-over-core hardness ratio.</summary>
        public double HardnessFloor;
        public double HardnessCeiling;

        /// <summary>How steeply the hardness ratio bites. 1 = the plain ratio.</summary>
        public double HardnessExponent;

        /// <summary>
        /// Dynamic yield strength per Vickers point, MPa. Tabor's constraint factor
        /// reads hardness as about three times the flow stress; 3.27 is where the
        /// 44S datasheet lands (613 HV against a published 2000-2100 MPa yield).
        /// </summary>
        public double HvToYieldMPa;

        /// <summary>
        /// The hardness above which a steel core arrives intact at any speed the game
        /// can fire it — quenched tool steel, not construction steel. See FateOf.
        /// </summary>
        public double DeformCoreMaxHv;

        /// <summary>
        /// How much of the plate's own strength joins the stagnation pressure in the
        /// contact stress that crushes a soft core. See FateOf.
        /// </summary>
        public double DeformPlateSupport;

        /// <summary>How the dead-core factor grows with the hardness ratio. See FateOf.</summary>
        public double DeformSpreadExponent;

        /// <summary>The least a core that dies on the face is worth to the plate.</summary>
        public double DeformFloor;

        /// <summary>Vickers hardness above which a core is a brittle solid, not a steel.</summary>
        public double BrittleCoreMinHv;

        /// <summary>
        /// Plate-over-core hardness ratio above which a brittle core cracks on the
        /// face instead of punching through it. See FateOf.
        /// </summary>
        public double ShatterRatio;

        /// <summary>Smallest cosine an oblique hit is read at, so a graze is not infinite.</summary>
        public double MinCos;

        public static Tuning Default => new Tuning
        {
            // Derived from the RHA V50 ladder alone — six thicknesses against 7.62 AP,
            // geometric mean of the per-row solutions — and then checked, not fitted,
            // against everything else. That agreement is the number's whole
            // justification: independent published datasets, measured different ways in
            // different decades, satisfied by one constant.
            //
            // It was 4.69 while the ladders were read at a 10.0 g whole-bullet mass and
            // an assumed 730 HV core. Both were wrong — the trials measure 5.3 g at
            // 570 HV, and a jacket carries almost nothing through — and the constant
            // follows: roughly half the mass and a third more hardness factor.
            DuctileK = 2.64,
            // Derived from the mild-steel ladder alone — all seven points, geometric
            // mean of the per-row solutions, no hardness factor in the branch — now
            // that the flow law owns that ladder outright instead of waiting for a
            // min() that never fired. Every point lands inside its own band (1.07 at
            // the thin end to 0.86 at 25 mm), and the RHA/mild pair comparison comes
            // out 0.90-0.99 of the published ratios for free — two mechanisms, two
            // ladders, no shared constant between them.
            //
            // The value itself has a physical address: cavity-expansion theory prices
            // opening a hole at 3-5 times the yield stress over its volume, and 6.6
            // sits at that scale once the little the jacket carries is counted. The
            // per-row solutions also say where the law ends: they hold at 5.6-6.4
            // from 4.7 to 16 mm and rise to 8.4-9.0 at 20-25 mm (T/d beyond 2.6),
            // which is confinement — deep cavity expansion costs more than thin-plate
            // flow — recorded as the shape test's remaining miss, not fitted away.
            HoleGrowthK = 6.60,
            // Anchored on the bare-alumina depth-of-penetration point, now that the
            // backing is a layer of its own and the hardness term no longer doubles
            // ceramic strength. The tile solves to 0.827, but a nil-residual DOP is a
            // one-sided statement — "the limit is AT OR ABOVE the velocity fired" — so
            // the certified plates act as the check that pins the value inside the
            // band: 0.98 is the smallest constant at which every certified ceramic
            // product without a recorded shortfall holds its class at the criterion the
            // tests actually enforce (zero-of-five, about +9% in velocity), with the
            // Бр4 Granit against the 7N10 binding at 0.976, and the tile is then read
            // at +9% of the trial velocity, inside the +20% its method earns.
            //
            // It was 1.04, derived at the BARE test velocity over a mixed set of
            // products and computed rungs — a reading no enforcement test uses — and
            // the two derivations agreed only until the backing data was fixed: the
            // named UHMWPE backings and the sewn-screen packing made every ceramic
            // assembly stronger, the bare-velocity reading drifted to 0.94, and the
            // shipped value survived its own justification by 6% until this was
            // re-derived under the criterion the certificates are actually held to.
            //
            // It barely moved when the projectile was re-read, and that is the point of
            // MassAgainst: a tile meets the whole bullet, so the measured jacket-carry —
            // which is a metal-plate result — does not reach it.
            BrittleK = 0.98,
            // A 33 mm polyethylene plate certified to stop M80 at 847, and a 7.6 mm
            // aramid package stopping the 9x18 GOST fires at Бр1, once the package's
            // 44% packing is taken out. The value is the FLOOR those certificates
            // demand: a certificate says the plate stopped the round, so it bounds the
            // limit from below and never from above.
            //
            // Fibre now has a published ladder too — ten para-aramid points against the
            // .22 FSP, in ArmorStandardTests — and it does not agree with the floor. The
            // ladder derives 23.1, which is 12% BELOW the floor in velocity, and that
            // direction is the whole finding: the smallest constant at which real armour
            // holds its certificates is larger than the largest one a measurement allows.
            // The T-linear fibre law is under-rating thick packs and the constant has
            // been quietly making up the difference — the ladder's own rows fall from
            // 26.4 at 3.6 mm to 17.6 at 6.8 mm while the products ask for 28.8. Moving
            // the constant onto the ladder was tried and it puts a dozen certified plates
            // below their own test velocity, which is not a recalibration but a model
            // that says real armour does not work.
            //
            // So the constant stays on the certificates, the ladder rows carry the miss
            // in the open, and what closes it is a thickness law that fits both — not a
            // number between the two. MODEL.md, "The fibre mode".
            FibrousK = 28.8,
            // One, and derived rather than assumed — which is the point of it being a
            // constant at all. Packing enters the fibre law as packed^p, and p was
            // fitted against every piece of fibre evidence at once: ten ladder rows at
            // 0.48-0.72 packing, the certified pressed plates at 1.0, and the sewn Бр1
            // and Бр2 packages at 0.44. The disagreement between ladder and products is
            // smallest at p = 1.00 and grows on both sides of it — 1.12 in velocity at
            // 1.00 against 1.32 at 0.38 and 1.13 at 1.10.
            //
            // 0.38 is what the two aramid ladders say ON THEIR OWN, and it is tempting
            // because it makes them agree exactly. It is also confounded: the two
            // ladders differ in weave (woven cloth against unidirectional laminate) as
            // well as in packing, so attributing the whole 15% between them to packing
            // is a fit, not a measurement — and it costs agreement with every pressed
            // plate, which is the evidence with the most bullets behind it. What would
            // settle it: one construction at three packings, or two constructions at the
            // same packing.
            PackingExponent = 1.0,
            // (514/501)² − 1, over the mass that is not core: the five percent of a
            // jacket that goes through with the core rather than staying on the face.
            JacketCarry = 0.05,
            // The bounds are where a soft bullet against a hard plate, and a hard core
            // against a soft one, are decided — and they are the two ends the ladders
            // cannot speak for, because every published ladder in the fixture is an AP
            // core. They used to be inherited numbers; with the core re-read they had to
            // be derived, and their anchors are the certificates that bind them.
            //
            // Floor 0.30: a plate much softer than the core still does something. The
            // binding cases are a 7N21 against aluminium and a B-32 against titanium.
            // It cannot go higher than the RHA ladder's own factor (0.32) without a
            // softer plate out-earning a harder one against the same core, which is the
            // term inverting — so the floor is pinned between a certificate and an
            // absurdity, and sits just under the absurdity.
            HardnessFloor = 0.30,
            // Ceiling 2.08: past this, more plate hardness stops buying anything against
            // a core that is already losing.
            //
            // It was 4.5, anchored on the two steel pistol rungs — and that anchor was
            // CIRCULAR. Both rungs are computed: their thickness is solved from the
            // class's own test cartridge under these very constants, so whatever the
            // ceiling is, the solver picks the thickness that clears the certificate and
            // the certificate then appears to confirm the ceiling. One equation, two
            // unknowns. Sweeping the ceiling from 4.5 to 1.0 moves not one certified
            // product in the corpus: they are ceramic (no hardness term), fibre (none
            // either), or met by an AP core the clamp never reaches. The clamp was
            // pinned by nothing that was measured.
            //
            // What pins it is the one certificate in the corpus where a SOFT core meets
            // a HARD plate, which is the case the clamp exists for: a 0.25-inch AR500
            // plate, the commonest steel Level III on the market, against six shots of
            // M80 ball — 9.5 g of lead alloy at 847 m/s into 580 HV steel. The plate
            // holds its certificate down to 2.077 and fails below it, so 2.08 is the
            // FLOOR that certificate demands, and — a certificate being one-sided, it
            // says the plate stopped the round and never how much more it could have
            // stopped — the value sits on that floor, exactly as FibrousK sits on the
            // floor its own certificates demand. At the old 4.5 the same plate reads
            // 47% over its certificate.
            //
            // What sent us looking: a raid log where 6.5 mm of titanium — a Бр3 plate,
            // certified against 2.0 kJ — stopped a .50 BMG carrying 15.1 kJ. Nothing
            // about that hit was exotic; the clamp was simply worth 4.5x the plate's
            // shear strength against a lead core, and a clamp derived from pistol
            // bullets was being spent on a round seven times outside its evidence.
            HardnessCeiling = 2.08,
            // The exponent that puts the RHA ladder and the certified Russian steel
            // plates on one DuctileK, derived exactly as 1.32 was and landing higher
            // for a reason: with the core read at its measured 570 HV instead of an
            // assumed 730, RHA meets a core it is 0.56 as hard as where it used to meet
            // one at 0.44, and the term has to work harder to keep that plate and a
            // 580 HV AR500 meeting a 697 HV core apart. Two independent datasets, one
            // constant, to within a percent — the same agreement the old pair had.
            HardnessExponent = 1.96,
            // Tabor's constraint factor, pinned where a published pair exists to pin
            // it: 44S is 613 HV against a 2000-2100 MPa yield, and 3.27 is that
            // arithmetic. It only ever appears multiplied by a hardness, on both
            // sides of the same inequality, so its absolute scale nearly cancels —
            // what it prices is the STAGNATION term against the hardness terms.
            HvToYieldMPa = 3.27,
            // The boundary between a core that upsets at rifle-impact contact stress
            // and one that arrives intact. The corpus pins the window and nothing in
            // it names the point: the 390 HV pre-1989 PS core deforms on a titanium
            // vest (the 6B3TM passport holds it, and no rigid reading can - the
            // factor it needs is above 1 at a ratio below 1), while the 570 HV M2 AP
            // is rigid through the whole RHA ladder, including its 818 m/s top row.
            // Midpoint of (390, 570); the measurement that would name it properly is
            // a hardness ladder of one core geometry - Anderson, Hohler, Walker &
            // Stilp 1999, which is exactly that experiment, remains unread (no
            // access); its published conclusion (performance rises monotonically
            // with hardness over 200-750 HV) is consistent with a threshold in this
            // window but does not place it.
            DeformCoreMaxHv = 480,
            // What fraction of the plate's own strength joins the stagnation
            // pressure in the contact stress. Window [0.096, 0.199): below 0.096
            // the 6B3TM titanium cannot crush the mild PS core at 720 m/s
            // (0.5*rho*v^2 alone is 1166 MPa against the core's 1275) and its
            // passport gate fails; at 0.199 the 9x18 Pst's 250 HV core would die on
            // a Бр1 steel panel at 335 m/s, and the Бр1/Бр2 rungs stop being
            // distinguishable. Near the midpoint.
            DeformPlateSupport = 0.14,
            // How the dead-core factor grows with the ratio. Window [0.323, 0.405]:
            // the smallest value at which M80 ball still drives the one soft-core
            // certificate (AR500 Level III) up to the ceiling that certificate
            // demands, and 0.405 is where the 6B3TM chest section would start
            // stopping the SVD its passport says goes through. Sits just over its
            // own floor, the way the ceiling sits on the AR500's.
            DeformSpreadExponent = 0.33,
            // A core that dies on the face loads the plate over MORE than its own
            // calibre, never less - 1.0 is the physical floor. The 6B3TM passport
            // demands 1.035 of it (mild PS at 720 m/s, held); it sits just above.
            DeformFloor = 1.05,
            // No steel reaches 1000 HV - hardened tool steels stop at about 800,
            // and the space above belongs to carbides and ceramics, which fail by
            // fracture rather than flow. A material bound, not a corpus fit.
            BrittleCoreMinHv = 1000,
            // The 6B23 certificate is the one place a brittle core meets a plate
            // hard enough: 44S at 613 HV shatters the 7N24's VK-8 at 1300 HV, ratio
            // 0.4715, and the certificate is one-sided - it says this ratio is
            // enough and nothing about softer plates. The value sits a hair inside
            // the documented point; titanium at 0.27 stays under it, which keeps
            // the 7N24 the titanium-killer nothing in the corpus contradicts.
            ShatterRatio = 0.47,
            MinCos = 0.34,
        };
    }

    /// <summary>
    /// What is left of the core's authority once it has met the face. Rigid, deformed
    /// or shattered — and which one is decided here, because the hardness term's whole
    /// job depends on it.
    ///
    /// One clamped power of the hardness ratio used to do all three jobs, and the three
    /// vest-passport gates refuted it with a sign pattern no ratio curve can produce:
    /// the same titanium vest was under-credited against a mild steel core (the passport
    /// holds it; the model needed a factor above 1 at a ratio below 1, which a power of
    /// the ratio cannot say) and over-credited against lead (the passport lets the SVD
    /// through), while a 44S panel was under-credited against tungsten carbide — a
    /// factor of 0.41 demanded at a ratio where the RHA ladder pins 0.32, so the term
    /// cannot even be monotone in the ratio. It is not a curve that was wrong but a
    /// missing decision: what happened to the core.
    ///
    /// **Rigid** — a quenched core (above DeformCoreMaxHv) or any core the plate cannot
    /// stress to its yield. The contest is whether the plate's shear band or the core
    /// gives way first, and the clamped power of the ratio keeps that job unchanged,
    /// floor, ceiling, exponent and the RHA-ladder derivation with it.
    ///
    /// **Deformed** — a soft core dies on the face when the contact stress reaches its
    /// own yield: the Taylor rigidity criterion, two-material form. The stress is the
    /// stagnation pressure ½ρ_plate·v² plus the share of the plate's own strength
    /// DeformPlateSupport says, against HvToYieldMPa times the core's hardness. The
    /// velocity matters and the corpus shows it: the same construction-steel family is
    /// rigid at 335 m/s out of a PM and dead at 720 out of an AKM. A dead core loads
    /// the plate as spread mass over more than its calibre — worth DeformFloor at
    /// least, growing shallowly with the ratio (DeformSpreadExponent), capped by the
    /// same ceiling the AR500 certificate pins.
    ///
    /// **Shattered** — a brittle core (above BrittleCoreMinHv: carbides and ceramics,
    /// no steel) cracks on a face hard enough (ShatterRatio), and rubble is spread
    /// mass exactly as a mushroomed slug is: the dead-core factor covers both deaths.
    /// Velocity is deliberately absent here — the real phenomenon has a shatter gap
    /// (a slow enough carbide survives) — but below the rigid limit both readings
    /// block, so the gap cannot show inside the game's velocity range. Recorded in
    /// MODEL.md under what is deliberately not modelled.
    /// </summary>
    public enum CoreFate
    {
        Rigid,
        Deformed,
        Shattered,
    }

    /// <summary>Which of the three ends this core meets on this face at this speed.</summary>
    public static CoreFate FateOf(Barrier b, Core c, double v, Tuning t)
    {
        if (b.HardnessHv <= 0 || c.HardnessHv <= 0)
        {
            return CoreFate.Rigid;
        }

        if (c.HardnessHv >= t.BrittleCoreMinHv)
        {
            return b.HardnessHv / c.HardnessHv >= t.ShatterRatio
                ? CoreFate.Shattered
                : CoreFate.Rigid;
        }

        if (c.HardnessHv >= t.DeformCoreMaxHv)
        {
            return CoreFate.Rigid;
        }

        // Taylor, two-material form, in MPa: stagnation plus the plate's supported
        // strength against the core's dynamic yield
        var contact = 0.5 * b.DensityGCm3 * 1000.0 * v * v / 1e6
                      + t.DeformPlateSupport * t.HvToYieldMPa * b.HardnessHv;
        return contact >= t.HvToYieldMPa * c.HardnessHv
            ? CoreFate.Deformed
            : CoreFate.Rigid;
    }

    /// <summary>
    /// How much the hardness argument between core and face is worth, and it is two
    /// different arguments depending on the core's fate (see CoreFate).
    ///
    /// For a rigid core: the clamped power of the plate-over-core ratio. Above 1 the
    /// core is losing and the barrier is worth more than its strength alone says;
    /// below 1 the core is cutting through and the barrier is worth less. The exponent
    /// is what reconciled the RHA ladder with the certified Russian plates — two
    /// datasets, one constant, to within a percent.
    ///
    /// For a dead core — mushroomed or shattered — the plate meets spread mass, never
    /// worth less than DeformFloor and growing shallowly with the ratio. Fibre panels
    /// have no hardness worth the name and sit at 1.
    /// </summary>
    public static double HardnessFactor(Barrier b, Core c, double v, Tuning t)
    {
        // Fibre has no hardness worth the name. Brittle is excluded for a different
        // reason: at 1500 HV a ceramic sits above every core in the game — even the
        // hardest, at 730, pins the ratio to the ceiling — so the term stopped
        // distinguishing cores and became a flat 2.5x on the strength, a constant
        // wearing physics' clothes. What is genuinely lost by returning 1 (alumina
        // shatters lead but loses to a hardened core) is recorded in MODEL.md under
        // "What is deliberately not modelled", with the measurement that would bring
        // it back: one ceramic tile shot with cores of two hardnesses.
        if (b.Class == Fibrous || b.Class == Brittle || b.HardnessHv <= 0 || c.HardnessHv <= 0)
        {
            return 1;
        }

        // A flowing alloy is excluded too: the hardness contest is about whether the
        // CORE or the PLUG's shear band gives way first, and a hole-expansion plate
        // forms no band — its resistance is its yield stress, already in the work
        // term. Its constant is derived without a hardness factor and stays clean of
        // the hardness re-derivations that move the plugging family.
        if (b.Class == Ductile && b.FailureMode == HoleExpansion)
        {
            return 1;
        }

        if (FateOf(b, c, v, t) != CoreFate.Rigid)
        {
            var spread = Math.Pow(b.HardnessHv / c.HardnessHv, t.DeformSpreadExponent);
            return spread < t.DeformFloor ? t.DeformFloor
                : spread > t.HardnessCeiling ? t.HardnessCeiling
                : spread;
        }

        var ratio = Math.Pow(b.HardnessHv / c.HardnessHv, t.HardnessExponent);
        return ratio < t.HardnessFloor ? t.HardnessFloor
            : ratio > t.HardnessCeiling ? t.HardnessCeiling
            : ratio;
    }

    /// <summary>
    /// How much of a fibre pack's work its packing is worth. A consolidated laminate
    /// (1) is unchanged by definition; everything looser gets more than its volume
    /// fraction, because the yarns a projectile pulls into the stretch are not confined
    /// to the volume directly under it.
    /// </summary>
    private static double Packing(double packedFraction, Tuning t)
    {
        var packed = packedFraction > 0 ? packedFraction : 1;
        return t.PackingExponent > 0 && t.PackingExponent != 1
            ? Math.Pow(packed, t.PackingExponent)
            : packed;
    }

    /// <summary>
    /// Work the barrier can do against this core before it is through, J.
    ///
    /// Four shapes, and which one applies is a property of the MATERIAL, never a
    /// cost race inside this function. A ductile alloy that localises shear punches
    /// out a plug — perimeter times its own thickness, work rising as T². A ductile
    /// alloy with strain-hardening left cannot localise at any price: it flows aside,
    /// and the work is the yield stress over the hole's volume, rising as T. The
    /// mild-steel ladder is what settled this — it follows the flow law from its
    /// thinnest point, including where the plug law would be cheaper, so choosing by
    /// min() was answering a metallurgical question with arithmetic. A ceramic
    /// crushes and erodes; a fibre pack has no hole to punch — each layer catches its
    /// share of the cone and hands the rest back, so its work goes as T too. Reading
    /// a pack at T² makes a 33 mm polyethylene plate absorb three and a half times
    /// what its certificate says.
    /// </summary>
    public static double WorkJ(Barrier b, Core c, double cos, double v, Tuning t)
    {
        if (b.ThicknessMm <= 0 || c.DiaMm <= 0)
        {
            return 0;
        }

        // an oblique hit presents more material along the path
        var slant = b.ThicknessMm / Math.Max(Math.Abs(cos), t.MinCos);

        // MPa·mm·mm² is N·mm, which is mJ
        double work;
        switch (b.Class)
        {
            case Brittle:
                work = t.BrittleK * b.CompressiveMPa * Math.PI * c.DiaMm * slant * slant;
                break;

            case Fibrous:
                work = t.FibrousK * b.FibreTensileMPa * b.FailureStrain
                       * Math.PI * c.DiaMm * c.DiaMm / 4.0 * slant * Packing(b.PackedFraction, t);
                break;

            case Ductile when b.FailureMode == HoleExpansion:
                // radial flow: yield stress over the hole's volume
                work = t.HoleGrowthK * b.YieldMPa * Math.PI * c.DiaMm * c.DiaMm / 4.0 * slant;
                break;

            default:
                // adiabatic shear: a plug cut round the perimeter, through the plate
                work = t.DuctileK * b.ShearMPa * Math.PI * c.DiaMm * slant * slant;
                break;
        }

        if (work <= 0)
        {
            return 0;
        }

        // the hardness argument is between the core and the FACE; the backing is fibre
        // and fibre has no hardness worth the name, so its work is added outside
        work *= HardnessFactor(b, c, v, t);

        // the backing catches what is left of the core layer by layer, exactly as a
        // free-standing fibre panel would
        if (b.BackingMm > 0 && b.BackingTensileMPa > 0 && b.BackingStrain > 0)
        {
            var backingSlant = b.BackingMm / Math.Max(Math.Abs(cos), t.MinCos);
            work += t.FibrousK * b.BackingTensileMPa * b.BackingStrain
                    * Math.PI * c.DiaMm * c.DiaMm / 4.0 * backingSlant
                    * Packing(b.BackingPacked, t);
        }

        return work / 1000.0;
    }

    /// <summary>
    /// Ballistic limit, m/s: below this the barrier holds, above it the core is through.
    /// Returns 0 when there is nothing to compute with, which callers read as "no
    /// geometry for this item, fall back to the class threshold".
    ///
    /// v is the speed the round actually arrives at, and it is an input to the LIMIT
    /// because the limit depends on the state the round arrives in: a soft core that
    /// dies on the face at full speed would have arrived intact at half of it. The
    /// number handed back is the limit for a round in the state v puts it in, and the
    /// caller's question stays the same — is v above it.
    /// </summary>
    public static double V50(Barrier b, Core c, double cos, double v, Tuning t)
    {
        var w = WorkJ(b, c, cos, v, t);
        var m = MassAgainst(b, c);
        if (w <= 0 || m <= 0)
        {
            return 0;
        }

        return Math.Sqrt(2 * w / (m / 1000.0));
    }

    /// <summary>
    /// Which mass the limit is computed against, and the answer depends on how the
    /// barrier fails — because that is what the measurement behind it covers.
    ///
    /// Forrestal's trial is a metal plate: complete APM2 bullets and their stripped
    /// cores gave the same limit, so on something that gets PUNCHED the jacket stays at
    /// the face and only the core (plus five percent) goes on. It is a ductile trial and
    /// nothing in it says a word about tiles or fibre.
    ///
    /// A ceramic does not get punched — it shatters, and it shatters the whole
    /// projectile with it; a fibre pack does not get punched either — it catches what
    /// arrives and stretches. Neither lets the jacket walk away, and reading them at the
    /// core's mass alone produces answers that the standards themselves refute: a Level
    /// IV tile stops the .30-06 AP more easily than M80 ball, which is backwards, and
    /// the ceramic class rungs fall under their own certificates. So the measured
    /// jacket-carry stays where it was measured, and the whole bullet arrives everywhere
    /// else. What would extend it: Forrestal's experiment against a tile and against a
    /// pack.
    /// </summary>
    public static double MassAgainst(Barrier b, Core c)
    {
        if (b.Class == Ductile || b.Class == null)
        {
            return c.MassG;
        }

        return c.BulletMassG > 0 ? c.BulletMassG : c.MassG;
    }

    /// <summary>
    /// Mass of the plug the core punches out and drags along, g. A ductile plate loses a
    /// disc the size of the hole; a ceramic shatters into pieces that mostly go sideways;
    /// a fibre panel loses nothing at all — it stretches and tears.
    /// </summary>
    public static double PlugMassG(Barrier b, Core c, double cos, Tuning t)
    {
        if (b.Class == Fibrous || b.DensityGCm3 <= 0)
        {
            return 0;
        }

        var slant = b.ThicknessMm / Math.Max(Math.Abs(cos), t.MinCos);
        var volumeMm3 = Math.PI * c.DiaMm * c.DiaMm / 4.0 * slant;
        var grams = volumeMm3 * b.DensityGCm3 / 1000.0;

        // a ceramic does not hand over a disc; what comes off is rubble and dust
        return b.Class == Brittle ? grams * 0.25 : grams;
    }

    /// <summary>
    /// Recht-Ipson residual velocity, m/s. The plug the core dragged out of the plate is
    /// now travelling with it, so the pair is slower than energy conservation alone
    /// would say. Below the limit this is 0 and the barrier holds.
    ///
    /// The energy the barrier took is no longer a separate constant to tune: it is
    /// ½m(v² − v_r²), and it falls out of the limit velocity.
    /// </summary>
    public static double ResidualVelocity(double v, double v50, double coreMassG,
        double plugMassG)
    {
        if (v <= v50 || coreMassG <= 0)
        {
            return 0;
        }

        var shared = coreMassG / (coreMassG + Math.Max(plugMassG, 0));
        return shared * Math.Sqrt(v * v - v50 * v50);
    }
}
