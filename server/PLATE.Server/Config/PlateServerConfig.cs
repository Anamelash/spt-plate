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

        /// <summary>GOST armor class normalization (disabled by default).</summary>
        public bool GostArmor { get; set; } = false;
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

        /// <summary>Threshold degradation with wear: U_eff = U·(floor + (1−floor)·durability%).</summary>
        public double DurabilityFloor { get; set; } = 0.4;

        /// <summary>Lower bound of local U_limit degradation in the hit zone
        /// (a shattered ceramic segment still holds at least this fraction of the threshold).</summary>
        public double DegradeFloor { get; set; } = 0.15;

        /// <summary>Armor material profiles (key — EFT MaterialType).</summary>
        public Dictionary<string, MaterialProfile> Materials { get; set; } = new()
        {
            // soft fabric: catches pistol rounds/fragments, barely touches a penetrating
            // bullet; sharp noses push the fibers apart (SharpVuln)
            ["Aramid"] = new()
            {
                ULimitMult = 0.85, ECostMult = 0.50, KDef = 0.05, KFrag = 0.00,
                DAreaMm = 60, DegradeMult = 0.80, SharpVulnMult = 0.25, JPerDurability = 400,
            },
            // UHMWPE: fibers work in tension; sharp noses pierce the pack, a penetrating bullet stays intact
            ["UHMWPE"] = new()
            {
                ULimitMult = 1.00, ECostMult = 0.35, KDef = 0.02, KFrag = 0.00,
                DAreaMm = 50, DegradeMult = 0.85, SharpVulnMult = 0.35, JPerDurability = 450,
            },
            // steel: ductile, penetration is expensive, lead gets flattened; the hole is local — a "gong"
            ["ArmoredSteel"] = new()
            {
                ULimitMult = 1.15, ECostMult = 0.85, KDef = 0.50, KFrag = 0.10,
                DAreaMm = 15, DegradeMult = 0.90, SharpVulnMult = 0.00, JPerDurability = 700,
            },
            // titanium: the bullet "bogs down" — extreme energy absorption
            ["Titan"] = new()
            {
                ULimitMult = 1.00, ECostMult = 1.00, KDef = 0.35, KFrag = 0.05,
                DAreaMm = 20, DegradeMult = 0.85, SharpVulnMult = 0.00, JPerDurability = 500,
            },
            ["Aluminium"] = new()
            {
                ULimitMult = 0.90, ECostMult = 0.60, KDef = 0.30, KFrag = 0.05,
                DAreaMm = 25, DegradeMult = 0.80, SharpVulnMult = 0.00, JPerDurability = 350,
            },
            // ceramic: highest threshold, shatters cores, but cracks tile by tile —
            // a repeat hit on the segment meets rubble
            ["Ceramic"] = new()
            {
                ULimitMult = 1.25, ECostMult = 0.70, KDef = 0.60, KFrag = 0.35,
                DAreaMm = 80, DegradeMult = 0.25, SharpVulnMult = 0.00, JPerDurability = 150,
            },
            ["Glass"] = new()
            {
                ULimitMult = 0.80, ECostMult = 0.50, KDef = 0.40, KFrag = 0.15,
                DAreaMm = 100, DegradeMult = 0.20, SharpVulnMult = 0.00, JPerDurability = 100,
            },
            ["Combined"] = new()
            {
                ULimitMult = 1.00, ECostMult = 0.65, KDef = 0.30, KFrag = 0.10,
                DAreaMm = 40, DegradeMult = 0.60, SharpVulnMult = 0.10, JPerDurability = 300,
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

            /// <summary>Bullet break-up: mass loss K_frag·(1−0.5X)
            /// (a hard core shatters against the barrier more than a soft one).</summary>
            public double KFrag { get; set; }

            /// <summary>Radius of local degradation around the hit, mm
            /// (ceramic — a whole "tile" segment, steel — only the hole rim).</summary>
            public double DAreaMm { get; set; } = 30;

            /// <summary>Fraction of U_limit remaining in the zone after each hit
            /// (multiplies; the floor is Armor.DegradeFloor).</summary>
            public double DegradeMult { get; set; } = 0.8;

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

        /// <summary>How much expansion/tumbling widens the channel: cross-section A·(1 + this·X).</summary>
        public double ExpansionAreaFactor { get; set; } = 1.35;

        /// <summary>Body (torso) thickness, mm — the channel deposits nothing beyond it.</summary>
        public double BodyDepthMm { get; set; } = 350;

        /// <summary>mm³ of permanent cavity volume per 1 HP of damage.
        /// Anchor: 9x19 PST -> ~54 (vanilla).</summary>
        public double WoundVolumePerHp { get; set; } = 710;

        // --- Temporary pulsating cavity (stretch) ---

        /// <summary>Center of the TC efficiency sigmoid, m/s — the "high-velocity wound"
        /// boundary (tissue is elastic: it survives slow stretch, fast stretch tears it).</summary>
        public double TcVelocityCenter { get; set; } = 600;

        /// <summary>Sigmoid width, m/s: eff = 1/(1+exp(−(v−center)/width)).</summary>
        public double TcVelocityWidth { get; set; } = 80;

        /// <summary>J of deposited TC energy per 1 HP.
        /// Anchor: 7.62x39 PS -> ~57 (vanilla).</summary>
        public double TcEnergyPerHp { get; set; } = 28;

        /// <summary>TC bonus for fragmentation: multiplier (1 + this·FragmentationChance) —
        /// fragments turn stretching into tearing.</summary>
        public double TcFragBonus { get; set; } = 0.5;

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

        /// <summary>Weights of the expansiveness index X.</summary>
        public double WeightSpecificDamage { get; set; } = 0.45;
        public double WeightSpecificPenetration { get; set; } = 0.45;
        public double WeightFragmentation { get; set; } = 0.25;

        /// <summary>FragmentationChance normalization into X: clamp01(FragChance / this divisor).</summary>
        public double FragChanceNormalizer { get; set; } = 0.30;

        /// <summary>Minimum caliber cohort size; fewer — global regression.</summary>
        public int MinCaliberCohort { get; set; } = 4;

        /// <summary>Penetration: pen units per J/mm² of cross-section. Anchors: M61 (75 J/mm² -> 64 pen),
        /// M995 (64 -> 53), PS 7.62x39 (44.6 -> 35).</summary>
        public double PenPerEnergyDensity { get; set; } = 0.85;

        /// <summary>Bullet construction effect on penetration: multiplier (1 + this*(0.5-X)).
        /// AP (X=0) concentrates energy in the core, HP (X=1) spreads it out.</summary>
        public double PenConstructionFactor { get; set; } = 0.6;

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
