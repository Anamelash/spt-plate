namespace PLATE.Server.Config;

/// <summary>
/// Server-side config. File: user/mods/PLATE/config.jsonc.
/// All formula constants live in the config file; the code only holds the
/// defaults used to generate it.
/// </summary>
public class PlateServerConfig
{
    public ModulesSection Modules { get; set; } = new();
    public AmmoNormalizerSection AmmoNormalizer { get; set; } = new();
    public BarrelSection BarrelNormalizer { get; set; } = new();
    public GrenadesSection Grenades { get; set; } = new();
    public ArmorSection Armor { get; set; } = new();
    public WoundsSection Wounds { get; set; } = new();
    public BloodSection Blood { get; set; } = new();

    public class ModulesSection
    {
        /// <summary>Ammo normalization (including mod-added rounds).</summary>
        public bool AmmoNormalizer { get; set; } = true;

        /// <summary>Grenade fragment physics from prototype specs (ammo-reference.jsonc):
        /// mass/velocity/damage from energy, optionally blast from explosive mass.</summary>
        public bool GrenadePhysics { get; set; } = true;

        /// <summary>Globals tweaks for the blood system (bleedings without HP damage, permanent Fresh Wound).
        /// Requires the client-side blood module to be installed (otherwise bleedings become harmless).</summary>
        public bool BloodGlobals { get; set; } = true;

        /// <summary>Barrel muzzle velocity from barrel length (including mod-added weapons).</summary>
        public bool BarrelNormalizer { get; set; } = true;

        /// <summary>
        /// Armour material, thickness and class from the real product it is modelled on.
        /// This is also where an item stamped with a class its material cannot hold —
        /// the sewn aramid packages the game rates 3 — is brought back to the class its
        /// construction earns; the separate GostArmor flag that used to promise that
        /// was never implemented and is gone.
        /// </summary>
        public bool ArmorNormalizer { get; set; } = true;
    }

    /// <summary>
    /// Muzzle velocity by barrel length: v = v∞·L/(L+C), Le Duc. Per-caliber reference
    /// barrels and C live in ammo-reference.jsonc; these are the guard rails.
    /// </summary>
    public class BarrelSection
    {
        /// <summary>Shortest length treated as a barrel, mm. A PM's is 93.5.</summary>
        public double MinLengthMm { get; set; } = 80;

        /// <summary>Longest, mm. An NSV's is 1100; past this the number is not a length.</summary>
        public double MaxLengthMm { get; set; } = 1200;

        /// <summary>
        /// Modifiers beyond this are not produced by the model, so one means the input
        /// was wrong: the item is left alone and listed in the report.
        /// </summary>
        public double MaxPercent { get; set; } = 45;

        /// <summary>
        /// Cap for muzzle devices and suppressors. A brake or a can shifts muzzle
        /// velocity by a hair, never by the double digits some of them claim.
        /// </summary>
        public double DeviceClampPercent { get; set; } = 2;
    }

    public class GrenadesSection
    {
        /// <summary>Recompute Strength (blast) from explosive mass by cube root
        /// relative to the reference book anchor (RGD-5: 110 g = Strength 100).</summary>
        public bool BlastFromTnt { get; set; } = true;

        /// <summary>Fragment expansiveness index for the penetration formula (torn steel ~0.3).</summary>
        public double FragmentX { get; set; } = 0.3;

        /// <summary>Fragment bleeding deltas (ragged wounds: they bleed almost always).</summary>
        public double FragLightDelta { get; set; } = 0.25;
        public double FragHeavyDelta { get; set; } = 0.15;
    }

    /// <summary>
    /// Physical armor model. Armor is a modifier of the projectile state:
    /// U penetration threshold (J/mm²) instead of a pen roll, an energy cut E_cost,
    /// deformation (K_def -> X) and break-up (K_frag -> mass) of the bullet.
    /// Data is served to the client as the "__armor" block in /plate/ammo-data.
    /// </summary>
    public class ArmorSection
    {
        /// <summary>false = vanilla penetration roll + GOST fragment gate (fallback).</summary>
        public bool PhysicalArmor { get; set; } = true;

        /// <summary>Probability band around the threshold (the material is not uniform):
        /// ±fraction of U_limit, linear penetration chance inside.</summary>
        public double ThresholdBand { get; set; } = 0.12;

        /// <summary>Minimum cosine of the impact angle: U_eff grows as 1/cos
        /// (slant thickness); below the cap the vanilla ricochet takes over.</summary>
        public double AngleMinCos { get; set; } = 0.34;

        /// <summary>U_limit per in-game class 1..6 at zero wear, J/mm².
        /// Class 1 — anti-fragmentation junk (construction helmets: spent shot/
        /// fragments only, does NOT stop a pistol bullet); class 2 = GOST Br1
        /// (test cartridge PM, 5.2 J/mm²); above — Br2..Br5, estimated.</summary>
        public double[] ClassULimitJmm2 { get; set; } = { 2.5, 5.2, 12, 40, 65, 90 };

        // Wear is no longer a smooth multiplier (the old DurabilityFloor/DegradeFloor
        // pair). A worn plate is not uniformly thinner — it is intact where nothing
        // hit it and broken where something did — so wear is probabilistic: the
        // chance of striking a damaged spot equals the missing durability, and a
        // struck spot loses thickness by 1 − x^k with per-material k and q
        // (MaterialProfile below). See MODEL.md, "Local damage and wear".

        /// <summary>
        /// How far a fully deformable bullet spreads against the face of a panel before
        /// it has finished loading it: impact area × (1 + this·X). The other half of
        /// what the core fraction says — a core concentrates the energy, deformation
        /// spreads it, and a hollow point is poor against armour for the second reason
        /// whatever energy it carries. 0 = every bullet loads its own calibre.
        /// </summary>
        public double ExpansionOnArmor { get; set; } = 0.6;

        /// <summary>Armor material profiles (key — EFT MaterialType).</summary>
        public Dictionary<string, MaterialProfile> Materials { get; set; } = new()
        {
            // soft fabric: catches pistol rounds/fragments, barely touches a penetrating
            // bullet; sharp noses push the fibers apart (SharpVuln)
            ["Aramid"] = new()
            {
                ULimitMult = 0.85, ECostMult = 0.50, KDef = 0.05, KFrag = 0.00,
                DAreaMm = 51, SpotDamageQ = 0.30, WearExponentK = 4, SharpVulnMult = 0.25, JPerDurability = 400,
            },
            // UHMWPE: fibers work in tension; sharp noses pierce the pack, a penetrating bullet stays intact
            ["UHMWPE"] = new()
            {
                ULimitMult = 1.00, ECostMult = 0.35, KDef = 0.02, KFrag = 0.00,
                DAreaMm = 45, SpotDamageQ = 0.40, WearExponentK = 3, SharpVulnMult = 0.35, JPerDurability = 450,
            },
            // steel: ductile, penetration is expensive, lead gets flattened; the hole is local — a "gong"
            ["ArmoredSteel"] = new()
            {
                ULimitMult = 1.15, ECostMult = 0.85, KDef = 0.50, KFrag = 0.10,
                DAreaMm = 15, SpotDamageQ = 0.50, WearExponentK = 2, SharpVulnMult = 0.00, JPerDurability = 700,
            },
            // titanium: the bullet "bogs down" — extreme energy absorption
            ["Titan"] = new()
            {
                ULimitMult = 1.00, ECostMult = 1.00, KDef = 0.35, KFrag = 0.05,
                DAreaMm = 20, SpotDamageQ = 0.50, WearExponentK = 2, SharpVulnMult = 0.00, JPerDurability = 500,
            },
            ["Aluminium"] = new()
            {
                ULimitMult = 0.90, ECostMult = 0.60, KDef = 0.30, KFrag = 0.05,
                DAreaMm = 25, SpotDamageQ = 0.40, WearExponentK = 3, SharpVulnMult = 0.00, JPerDurability = 350,
            },
            // ceramic: highest threshold, shatters cores, but cracks tile by tile —
            // a repeat hit on the segment meets rubble
            ["Ceramic"] = new()
            {
                ULimitMult = 1.25, ECostMult = 0.70, KDef = 0.60, KFrag = 0.35,
                DAreaMm = 51, SpotDamageQ = 0.90, WearExponentK = 1.5, SharpVulnMult = 0.00, JPerDurability = 150,
            },
            ["Glass"] = new()
            {
                ULimitMult = 0.80, ECostMult = 0.50, KDef = 0.40, KFrag = 0.15,
                DAreaMm = 51, SpotDamageQ = 0.90, WearExponentK = 1.5, SharpVulnMult = 0.00, JPerDurability = 100,
            },
            ["Combined"] = new()
            {
                ULimitMult = 1.00, ECostMult = 0.65, KDef = 0.30, KFrag = 0.10,
                DAreaMm = 45, SpotDamageQ = 0.60, WearExponentK = 2, SharpVulnMult = 0.10, JPerDurability = 300,
            },
        };

        public class MaterialProfile
        {
            /// <summary>Multiplier of the class U_limit.</summary>
            public double ULimitMult { get; set; } = 1.0;

            /// <summary>Energy cost: E_cost = this · U_eff · A_bullet (work ∝
            /// strength × hole area × slant thickness).</summary>
            public double ECostMult { get; set; } = 0.6;

            /// <summary>Bullet deformation: X_out = X + K_def·X (soft bullets get
            /// squashed, a hard core keeps its shape).</summary>
            public double KDef { get; set; }

            /// <summary>
            /// Erosion of what comes through: m_out = m·CoreMassFrac·(1−K_frag). A
            /// property of the barrier, not of the bullet — ceramic is in the business
            /// of grinding a core down, aramid is not. It used to carry a (1−0.5X)
            /// factor reading "a hard core shatters more", which is the wrong way round;
            /// the jacket coming off is CoreMassFrac's job now.
            /// </summary>
            public double KFrag { get; set; }

            /// <summary>
            /// Radius of local degradation around the hit, mm (ceramic — a whole
            /// "tile" segment, steel — only the hole rim). The ceiling of the scale
            /// has an anchor: both certification standards space their scored shots
            /// so that damage zones do not interact — NIJ 0101.06 demands 51 mm
            /// between hits — so no material's zone may exceed 51. That is one
            /// spacing for all products, i.e. an upper bound set by the worst
            /// material; the ladder below it is still chosen, and says so.
            /// </summary>
            public double DAreaMm { get; set; } = 30;

            /// <summary>
            /// Damage one hit does to the SPOT it lands on, 0..1 — not to the plate.
            /// Seen damage accumulates geometrically per hit in the same spot:
            /// x = 1 − (1−q)^n; unseen damage (worn plate entering the raid, or hit
            /// memory overflow) is rolled with p = missing durability and reads
            /// x = max(missing, q), because a rolled "you hit a damaged spot" means
            /// at least one hit landed there. An ASSUMPTION, like every q: the data
            /// that would replace it is makers' multi-hit ratings, and those are for
            /// SPACED hits, not one spot. Marked accordingly.
            /// </summary>
            public double SpotDamageQ { get; set; } = 0.4;

            /// <summary>
            /// How local the damage stays: effective thickness at a damaged spot is
            /// 1 − x^k. High k (aramid 4) — cut fibres in the spot, neighbours
            /// intact; low k (ceramic 1.5) — the crack web spreads, a struck tile is
            /// rubble. From the resolution of 3.4.
            /// </summary>
            public double WearExponentK { get; set; } = 2;

            /// <summary>Vulnerability to sharp-nosed bullets (fibers get pushed apart):
            /// U_limit × (1 − this·clamp01((0.5−X)·2)).</summary>
            public double SharpVulnMult { get; set; }

            /// <summary>J of absorbed energy per 1 durability point
            /// (ceramic crumbles fast, "gong" steel takes dozens of hits).</summary>
            public double JPerDurability { get; set; } = 400;
        }
    }

    public class AmmoNormalizerSection
    {
        /// <summary>Wound channel model (PC + TC) instead of linear energy.
        /// false = legacy formula Damage = E0/EnergyPerHp.</summary>
        public bool WoundChannelModel { get; set; } = true;

        // --- Permanent cavity (crush): channel depth × cross-section ---

        /// <summary>Channel depth: K·(m/A)·ln(v/vstop)·(1−cX·X), mm per (g/mm²).
        /// Log model of quadratic drag; calibration: 9mm FMJ ~50 cm of gelatin.</summary>
        public double GelDepthK { get; set; } = 2700;

        /// <summary>Velocity below which tissue stops the projectile elastically, m/s.</summary>
        public double GelStopVelocity { get; set; } = 50;

        /// <summary>How much expansion shortens the channel: multiplier (1 − this·X).</summary>
        public double ExpansionDepthFactor { get; set; } = 0.4;

        /// <summary>How much expansion widens the channel before the projectile turns:
        /// cross-section A·(1 + this·X). At X=1 that is 1.53 calibres of diameter,
        /// against the 1.55-1.7 a real expanding bullet opens to. Tumbling is no longer
        /// in here — it is a separate area that switches on after the neck.</summary>
        public double ExpansionAreaFactor { get; set; } = 1.35;

        /// <summary>Median travel before the projectile goes broadside, in calibres.
        /// Published gelatin necks run from ~12 calibres for 5.45x39 to over 30 for
        /// 7.62x39; without a measured neck per cartridge, one constant in the middle.</summary>
        public double YawNeckCalibres { get; set; } = 20;

        /// <summary>Share of full broadside a tumbling projectile presents on average —
        /// it turns through the whole circle rather than staying side-on. 0.75 puts
        /// 7.62x51 M80 at 3.5 times its calibre area once it has turned.</summary>
        public double YawBroadsideFraction { get; set; } = 0.75;

        /// <summary>Mean density of a jacketed bullet, g/cm³ (lead core, copper jacket) —
        /// what turns mass and calibre into a length.</summary>
        public double BulletDensityGPerCm3 { get; set; } = 10.5;

        /// <summary>How much of its bounding cylinder a bullet fills, once the ogive nose
        /// and boat tail are taken out. 0.65 puts M80 at 28.8 mm against a measured 28.9.</summary>
        public double BulletFormFactor { get; set; } = 0.65;

        /// <summary>
        /// Tissue depth of the reference shot the card damage is quoted for, mm.
        /// The protocol: perpendicular hit into the centre of the chest of a gelatin
        /// manikin at 5 m. 250 mm is the anteroposterior chest depth of an adult male;
        /// 5 m means muzzle velocity, and perpendicular means no oblique lengthening.
        /// In a raid the path comes from the actual collider chord, not from this.
        /// </summary>
        public double BodyDepthMm { get; set; } = 250;

        /// <summary>mm³ of permanent cavity volume per 1 HP of damage.
        /// Anchor: the combat-mortality research figure of ~2.3 rifle hits to the
        /// torso (85 HP) to incapacitation, ~37 HP per hit — replacing the old
        /// vanilla-damage anchor (9x19 PST -> ~54), which calibrated the model to the
        /// game's own invented numbers.</summary>
        public double WoundVolumePerHp { get; set; } = 381;

        // --- Temporary pulsating cavity (stretch) ---

        /// <summary>Center of the TC efficiency sigmoid, m/s — the "high-velocity wound"
        /// boundary (tissue is elastic: it survives slow stretch, fast stretch tears it).</summary>
        public double TcVelocityCenter { get; set; } = 600;

        /// <summary>Sigmoid width, m/s: eff = 1/(1+exp(−(v−center)/width)).</summary>
        public double TcVelocityWidth { get; set; } = 80;

        /// <summary>J of deposited TC energy per 1 HP.
        /// Anchor: the same 2.3-hits-to-incapacitation figure as WoundVolumePerHp —
        /// the two are one calibration and move together. The old anchor was
        /// 7.62x39 PS -> ~57 (vanilla).</summary>
        public double TcEnergyPerHp { get; set; } = 74;

        /// <summary>TC bonus for fragmentation: multiplier (1 + this·frag) — fragments
        /// turn stretching into tearing. frag is DERIVED from construction, not read
        /// from the vanilla FragmentationChance field: a bullet breaks up where it
        /// turns, if it is still fast enough there, and only its deformable,
        /// non-core share breaks.</summary>
        public double TcFragBonus { get; set; } = 0.5;

        /// <summary>Velocity at the tumble point above which a jacketed bullet's
        /// envelope fails and it fragments, m/s. Published threshold band for
        /// thin-jacketed ball is 600–700; the bottom of the band, read at the tumble
        /// point rather than at impact, reproduces which cartridges actually
        /// fragment in gelatin (M193/M855 yes, 7.62x39 PS and pistol ball no).</summary>
        public double FragVelocityThreshold { get; set; } = 600;

        /// <summary>Energy budget: damage no higher than E0 / this (J per HP at full
        /// deposition). Trims slow fat projectiles and light birdshot.</summary>
        public double EnergyCapPerHp { get; set; } = 7;

        /// <summary>J per unit of HP damage (legacy linear model, WoundChannelModel=false).
        /// Anchor: 7.62x39 PS, 2036 J -> 57 dmg.</summary>
        public double EnergyPerHp { get; set; } = 35.7;

        /// <summary>Recompute Damage strictly from energy. false = only fill missing fields.</summary>
        public bool RescaleDamageFromEnergy { get; set; } = true;

        /// <summary>Penetration blend towards vanilla: 0 = leave PenetrationPower alone, 1 = fully from E/A.</summary>
        public double PenetrationBlend { get; set; } = 0.5;

        /// <summary>Maximum PenetrationDamageMod for pure AP (X=0).</summary>
        public double PdmMax { get; set; } = 0.35;

        /// <summary>
        /// Weights of the expansiveness index X. There used to be a third component —
        /// the vanilla FragmentationChance — removed with 3.6: that field is the
        /// game's opinion, not physics, and the model now derives fragmentation from
        /// construction, which would make feeding it back into X circular. The two
        /// remaining weights are symmetric, so the cohort median still reads 0.5.
        /// </summary>
        public double WeightSpecificDamage { get; set; } = 0.45;
        public double WeightSpecificPenetration { get; set; } = 0.45;

        /// <summary>Minimum caliber cohort size; fewer — global regression.</summary>
        public int MinCaliberCohort { get; set; } = 4;

        /// <summary>Penetration: pen units per J/mm² of cross-section. Anchors: M61 (75 J/mm² -> 64 pen),
        /// M995 (64 -> 53), PS 7.62x39 (44.6 -> 35).</summary>
        public double PenPerEnergyDensity { get; set; } = 0.85;

        /// <summary>
        /// How small a core to assume for a cartridge the reference book does not name.
        /// The book carries the real geometry; for anything else the only evidence of a
        /// penetrator is that the round out-penetrates what its energy density explains,
        /// so the core area is read off that residual: 1.0 at the cohort median, down to
        /// (1 − this) at the top of it. 0 assumes every unlisted bullet is monolithic.
        /// </summary>
        public double CoreFallbackDepth { get; set; } = 0.5;

        /// <summary>Upper damage limit after the rescale (technical cap).</summary>
        public double DamageCap { get; set; } = 999;

        /// <summary>Normalize buckshot/birdshot: pellet masses from the spec reference book
        /// (ammo-reference.jsonc), per-pellet Damage/Pen from energy.</summary>
        public bool NormalizeBuckshot { get; set; } = true;

        /// <summary>Damage floor of a single pellet/fragment after the rescale
        /// (3, not 5 — otherwise the floor eats the gradation of small shot).</summary>
        public double MinPelletDamage { get; set; } = 3;

        /// <summary>Expansiveness index X for buckshot without a reference entry
        /// (a lead ball deforms — closer to an expanding bullet).</summary>
        public double XBuckshotDefault { get; set; } = 0.7;

        /// <summary>Write the normalization report (report.md next to the config).</summary>
        public bool WriteReport { get; set; } = true;

        /// <summary>Align bleeding deltas with channel diameter and X
        /// (part of vanilla has zeros/arbitrary values — e.g. 20x70 buckshot never bled at all).</summary>
        public bool NormalizeBleedDeltas { get; set; } = true;

        /// <summary>Light delta: base + perMm * diameter (mm).</summary>
        public double BleedLightBase { get; set; } = 0.05;
        public double BleedLightPerMm { get; set; } = 0.02;
        public double LightDeltaMax { get; set; } = 0.6;

        /// <summary>Heavy delta: perMm * diameter * (0.5 + 0.5X) — a large expanding
        /// channel tears vessels more often.</summary>
        public double BleedHeavyPerMm { get; set; } = 0.016;
        public double HeavyDeltaMax { get; set; } = 0.5;

        /// <summary>Heavy delta multiplier for a single pellet (small channels).</summary>
        public double PelletHeavyFactor { get; set; } = 0.5;
    }

    public class BloodSection
    {
        /// <summary>Out-of-raid blood regeneration, ml per hour of real time (plasma replacement).</summary>
        public double OutOfRaidRegenMlPerHour { get; set; } = 1200;

        /// <summary>Add the blood bag (transfusion) item to the Therapist.</summary>
        public bool TransfusionItem { get; set; } = true;

        /// <summary>Blood bag price at the Therapist, RUB.</summary>
        public double TransfusionPriceRub { get; set; } = 24000;

        /// <summary>Uses per bag.</summary>
        public int TransfusionUses { get; set; } = 3;
    }

    public class WoundsSection
    {
        /// <summary>Fresh Wound: lifetime, sec (999999 = until the end of any raid). Vanilla: 480.</summary>
        public double FreshWoundWorkingTime { get; set; } = 999999;

        /// <summary>Light/HeavyBleeding lifetime in offline raids, sec. SPT vanilla: 600/900 —
        /// bleedings "healed on their own". 999999 = until stopped or raid end.</summary>
        public double BleedingLifetimeSec { get; set; } = 999999;

        /// <summary>Zero out HP damage from bleedings (blood pours into BloodVolume, not into limbs).</summary>
        public bool DisableBleedingHpDamage { get; set; } = true;

        /// <summary>Zero out the vanilla bullet fracture roll (the client rolls it itself:
        /// bone probability per collider × bullet energy). Fall fractures are left alone.</summary>
        public bool DisableVanillaBulletFractures { get; set; } = true;
    }
}
