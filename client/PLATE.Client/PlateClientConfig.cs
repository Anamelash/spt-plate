using System.Collections.Generic;
using BepInEx.Configuration;
using EFT.InventoryLogic;
using UnityEngine;

namespace PLATE.Client
{
    /// <summary>
    /// Duck-typed attributes for BepInEx ConfigurationManager (F12): read via reflection
    /// by field names. IsAdvanced settings are only visible with the "Advanced" checkbox.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
        public bool? Browsable;
        public int? Order;
    }

    /// <summary>
    /// Client-side config. Normal F12 mode exposes gameplay tuning only;
    /// formula constants and debug options sit behind the Advanced checkbox
    /// (every formula constant lives in the config, but they are kept out of
    /// the regular user's face).
    /// </summary>
    public static class PlateClientConfig
    {
        // --- Modules ---
        public static ConfigEntry<bool> BallisticsEnabled;
        public static ConfigEntry<bool> BloodEnabled;
        public static ConfigEntry<bool> OverlayEnabled;

        // --- Ballistics ---
        public static ConfigEntry<float> FleshRetentionAp;
        public static ConfigEntry<float> FleshRetentionHp;
        public static ConfigEntry<float> ArmorMitigationK;
        public static ConfigEntry<float> ArmorMitigationMin;
        public static ConfigEntry<float> ArmorResistPerClass;
        public static ConfigEntry<float> ArmorDurabilityFloor;
        public static ConfigEntry<bool> BabtEnabled;
        public static ConfigEntry<float> BabtBc1;
        public static ConfigEntry<float> BabtBc2;
        public static ConfigEntry<float> BabtPlateauDamage;
        public static ConfigEntry<float> BabtMaxDamage;
        public static ConfigEntry<float> BabtBodyMassKg;
        public static ConfigEntry<float> BabtWallCm;
        public static ConfigEntry<float> BabtEnergyScale;
        public static ConfigEntry<float> BabtInternalBleedRate;
        public static ConfigEntry<bool> FragRescale;
        public static ConfigEntry<bool> PhysDamageModel;
        public static ConfigEntry<bool> PhysArmorModel;
        public static ConfigEntry<float> LegacyDamageScale;
        public static ConfigEntry<float> DamageScalePlayer;
        public static ConfigEntry<float> DamageScalePmc;
        public static ConfigEntry<float> DamageScaleScav;
        public static ConfigEntry<bool> ArmorLocalDegradation;
        public static ConfigEntry<bool> OrganZones;
        public static ConfigEntry<float> OrganTissueStrengthMPa;
        public static ConfigEntry<float> OrganKHeart;
        public static ConfigEntry<float> OrganKLiver;
        public static ConfigEntry<float> OrganKSpine;
        public static ConfigEntry<float> OrganArrestChance;
        public static ConfigEntry<float> OrganAvulsionChance;
        public static ConfigEntry<float> OrganLiverRadiusMm;
        public static ConfigEntry<float> OrganBleedMlSec;
        public static ConfigEntry<float> BleedVesselsTorso;
        public static ConfigEntry<float> BleedVesselsJunction;
        public static ConfigEntry<float> BleedVesselsLimb;
        public static ConfigEntry<float> BleedVesselsHead;
        public static ConfigEntry<float> BleedHeavyMaxChance;
        public static ConfigEntry<float> YawSpreadSigma;
        public static ConfigEntry<float> TissueSpread;
        public static ConfigEntry<float> ZoneShiftMm;
        public static ConfigEntry<float> VitalBrainMult;
        public static ConfigEntry<float> VitalJawMult;
        public static ConfigEntry<float> VitalNeckMult;
        public static ConfigEntry<float> FragEnergyShare;
        public static ConfigEntry<float> GrenadeFragmentRange;
        public static ConfigEntry<bool> FragmentsStoppedByArmor;
        public static ConfigEntry<float> FragBlockEnergyJ;
        public static ConfigEntry<float> FragBlockClassFactor;
        public static ConfigEntry<float> LargeFragShare;
        public static ConfigEntry<float> LargeFragEnergyMult;
        public static ConfigEntry<bool> BlastBarotrauma;
        public static ConfigEntry<float> BlastInternalMinDamage;
        public static ConfigEntry<float> BlastInternalFullDamage;
        public static ConfigEntry<float> BlastInternalMlSec;

        // --- Blood & trauma ---
        public static ConfigEntry<float> BloodMaxMl;
        public static ConfigEntry<float> BleedHeavyTorso;
        public static ConfigEntry<float> BleedHeavyLeg;
        public static ConfigEntry<float> BleedHeavyArm;
        public static ConfigEntry<float> BleedLight;
        public static ConfigEntry<float> SelfLimitBeta;
        public static ConfigEntry<float> FemoralChance;
        public static ConfigEntry<float> FemoralBleedMlSec;
        public static ConfigEntry<float> CardiacOutputMlSec;
        public static ConfigEntry<float> StomachDestroyedBleed;
        // "Leg/Arm destroyed bleed" retired: a destroyed limb now opens a heavy external
        // bleed, whose rate comes from BleedHeavyLeg/BleedHeavyArm (or the femoral branch)
        public static ConfigEntry<bool> LimbHitsCanKill;
        public static ConfigEntry<bool> CrippleEnabled;
        public static ConfigEntry<float> CrippleStaminaCoeff;
        public static ConfigEntry<float> CrippleSpeedLimit;
        public static ConfigEntry<bool> FractureCollapsePlayer;
        public static ConfigEntry<bool> FractureCollapsePmc;
        public static ConfigEntry<bool> FractureCollapseScav;
        public static ConfigEntry<float> FractureFallDelay;
        public static ConfigEntry<float> FractureEnergyMin;
        public static ConfigEntry<float> FractureEnergyFull;
        public static ConfigEntry<float> BoneChanceThigh;
        public static ConfigEntry<float> BoneChanceCalf;
        public static ConfigEntry<float> BoneChanceUpperArm;
        public static ConfigEntry<float> BoneChanceForearm;
        public static ConfigEntry<float> GuaranteedBleedMinDamage;
        public static ConfigEntry<float> ThresholdTier1;
        public static ConfigEntry<float> ThresholdTier2;
        public static ConfigEntry<float> ThresholdTier3;
        public static ConfigEntry<float> DeathThreshold;
        public static ConfigEntry<bool> DeathForPlayer;
        public static ConfigEntry<bool> DeathForPmc;
        public static ConfigEntry<bool> DeathForScav;
        public static ConfigEntry<bool> SurgeryStopsBleeding;
        public static ConfigEntry<bool> InternalBleedPlayer;
        public static ConfigEntry<bool> InternalBleedPmc;
        public static ConfigEntry<bool> InternalBleedScav;
        public static ConfigEntry<float> BleedRatePlayer;
        public static ConfigEntry<float> BleedRatePmc;
        public static ConfigEntry<float> BleedRateScav;
        public static ConfigEntry<float> PassiveRegenMlMin;
        public static ConfigEntry<bool> BloodHudVisible;
        public static ConfigEntry<bool> HeartbeatAtTier2;
        public static ConfigEntry<bool> FatigueAtTier2;
        public static ConfigEntry<bool> Tier3MovementBan;
        public static ConfigEntry<float> ContusionTier3Strength;
        public static ConfigEntry<float> TransfusionMlPerUse;

        // --- Overlay ---
        public static ConfigEntry<bool> OverlayFloatingText;
        public static ConfigEntry<bool> OverlayPanelVisible;
        public static ConfigEntry<KeyboardShortcut> OverlayPanelKey;
        public static ConfigEntry<int> OverlayPanelMaxLines;
        public static ConfigEntry<float> OverlayFloatSeconds;
        public static ConfigEntry<bool> OverlayOnlyMyFights;
        public static ConfigEntry<bool> OverlayLogHits;
        public static ConfigEntry<float> OverlayMaxFloatDistance;

        // --- Player survivability (section 7) ---
        // The overrides that depart from the model on purpose; the ": Player" halves of
        // the per-side groups are declared with their siblings above but bound here.
        public static ConfigEntry<bool> PreventPlayerDeath;
        public static ConfigEntry<float> PlayerBleedChance;
        public static ConfigEntry<bool> PlayerOrganCrits;

        // Retired keys, kept bound so the v4 migration can read what the player had under
        // the section these four used to live in. See MigrateDefaults.
        public static ConfigEntry<bool> LegacyDeathForPlayer;
        public static ConfigEntry<bool> LegacyInternalBleedPlayer;
        public static ConfigEntry<float> LegacyBleedRatePlayer;
        public static ConfigEntry<bool> LegacyFractureCollapsePlayer;
        public static ConfigEntry<float> LegacyDamageScalePlayer;

        // --- Debug ---
        public static ConfigEntry<bool> TrackSelfHits;
        public static ConfigEntry<bool> SelfTestOnLoad;
        public static ConfigEntry<bool> VerboseLog;
        public static ConfigEntry<bool> AnatomyDebug;
        public static ConfigEntry<bool> PerfTrace;
        public static ConfigEntry<int> ConfigVersion;

        /// <summary>Bump on every change to an existing setting's default.</summary>
        private const int CurrentConfigVersion = 5;

        /// <summary>Shown on a key that only exists to be read once by a migration.</summary>
        private const string RetiredNote =
            "Moved to \"7. Player Survivability\". Read once on update to carry your " +
            "value across; editing it now does nothing.";

        // --- Armor material profiles ---
        public class MaterialProfile
        {
            public ConfigEntry<float> DuraBlockMult;
            public ConfigEntry<float> DuraPenMult;
            public ConfigEntry<float> SpreadCm;
        }

        public static readonly Dictionary<EArmorMaterial, MaterialProfile> Materials =
            new Dictionary<EArmorMaterial, MaterialProfile>();

        private static ConfigFile _cfg;
        private static int _order = 5000;

        private static ConfigEntry<T> Bind<T>(string section, string key, T def, string desc,
            AcceptableValueBase range = null, bool advanced = false)
        {
            // Fine-tuning (formula constants, armor material profiles) is file-only:
            // edit BepInEx/config/*.cfg. Overlay/debug sections (5/6) stay visible
            // in F12 behind the Advanced toggle.
            var fileOnly = section.StartsWith("4") ||
                           (advanced && !section.StartsWith("5") && !section.StartsWith("6"));
            var attrs = new ConfigurationManagerAttributes
            {
                IsAdvanced = advanced,
                Browsable = fileOnly ? false : (bool?)null,
                Order = _order--,
            };
            return _cfg.Bind(section, key, def, new ConfigDescription(desc, range, attrs));
        }

        public static void Bind(ConfigFile config)
        {
            _cfg = config;

            const string sMod = "1. Modules";
            const string sBal = "2. Ballistics";
            const string sBlood = "3. Blood & trauma";
            const string sMat = "4. Armor materials";
            const string sOverlay = "5. Hit overlay (debug)";
            const string sDebug = "6. Debug";
            const string sSurv = "7. Player Survivability";

            // ===== 1. Modules =====
            BallisticsEnabled = Bind(sMod, "Ballistics", true,
                "Terminal ballistics: damage from deposited energy, fixes for vanilla " +
                "zero-damage cases (AVOID, plate + overpenetration), damage mitigation " +
                "on penetration, behind-armor blunt trauma.");
            BloodEnabled = Bind(sMod, "Blood system", true,
                "Blood system: volume, bleedings, thresholds, cripple effects, fractures, " +
                "death from blood loss. Works on the player and bots.");

            // ===== 2. Ballistics (regular) =====
            BabtEnabled = Bind(sBal, "BABT enabled", true,
                "Behind-armor blunt trauma per the Sturdivan Blunt Criterion instead of " +
                "vanilla blunt damage: a stopped bullet hurts the body through the armor " +
                "depending on energy and material.");
            FragRescale = Bind(sBal, "Fragment energy budget", true,
                "Bullet fragments split a real share of the energy (total damage never " +
                "exceeds the bullet's budget) instead of vanilla's bonus damage out of thin air.");
            PhysDamageModel = Bind(sBal, "Physical damage model", true,
                "Damage is a pure function of projectile physics at the moment of impact " +
                "(mass, diameter, impact velocity, expansiveness) and of the path through " +
                "the body part (collider chord: a grazing hit is a scratch). Overpenetration " +
                "is decided by channel depth and bone, not PenetrationLevel. Template Damage " +
                "is display-only. Requires the PLATE server component.");
            PhysArmorModel = Bind(sBal, "Physical armor model", true,
                "Armor as a modifier of the projectile's state: penetration threshold by " +
                "specific energy (J/mm², GOST anchors per class, material, wear, angle); " +
                "a penetrating bullet pays with energy, deforms and loses mass — a weakened " +
                "projectile enters the body. Replaces the vanilla pen roll. Material " +
                "profiles live in the server config. Requires Physical damage model.");
            // the single "Damage scale" knob was split per category; its saved value is
            // carried into the three by the v3 migration so an existing tuning is not
            // reset (the key has to stay verbatim — reading the old one is the point)
            LegacyDamageScale = Bind(sBal, "Damage scale", 1.0f,
                "Superseded by the per-category damage scales below. Read once on update " +
                "to seed them; editing it now does nothing.",
                new AcceptableValueRange<float>(0.1f, 10f), true);
            // "Damage scale: Player" is bound in section 7 with the rest of what keeps
            // you alive; the retired pair it moved from is bound further down so the v5
            // migration can read it
            DamageScalePmc = Bind(sBal, "Damage scale: PMC", 1.0f,
                "Same for PMC bots (USEC/BEAR).",
                new AcceptableValueRange<float>(0.1f, 10f));
            DamageScaleScav = Bind(sBal, "Damage scale: Scav", 1.0f,
                "Same for all Savage-side NPCs: scavs, bosses, raiders, cultists, etc.",
                new AcceptableValueRange<float>(0.1f, 10f));
            ArmorLocalDegradation = Bind(sBal, "Armor local degradation", true,
                "Per-location hit memory: the penetration threshold drops locally around " +
                "impact points — ceramic cracks in tile-like segments (a repeat hit on the " +
                "same segment meets rubble), 'gong' steel only degrades at the edges. " +
                "Radii and coefficients live in the server config material profiles.");
            OrganZones = Bind(sBal, "Organ zones", true,
                "Heart, liver and spinal cord as thirds of the torso hitboxes. A channel " +
                "that runs deeper than half the zone through the heart or the cord is " +
                "fatal — dealt as damage equal to what the body part had left, never as a " +
                "scripted kill, so the kill feed, statistics and other mods see an " +
                "ordinary hit. Also switches bleeding over to anatomy: whether a wound " +
                "opens an artery is decided by the plane the channel swept and by the " +
                "vessels where it swept it, instead of by a number attached to the " +
                "cartridge. Requires Physical damage model.");
            GrenadeFragmentRange = Bind(sBal, "Grenade fragment range, m", 25f,
                "Maximum grenade fragment kill range. Vanilla hard-caps it at 5-8 m; " +
                "real lethal fragments travel tens of meters. Does not stretch blast " +
                "damage or concussion — their radii are computed separately.",
                new AcceptableValueRange<float>(5f, 100f));
            FragmentsStoppedByArmor = Bind(sBal, "Fragments stopped by armor", true,
                "Fragments with impact energy below the GOST threshold cannot penetrate " +
                "class 1+ armor (Br1 threshold = 400 J; higher class — higher threshold). " +
                "An energetic large fragment near the epicenter still gets an honest roll. " +
                "Broken armor does not protect.");

            // ===== 2. Ballistics (advanced) =====
            VitalBrainMult = Bind(sBal, "Vital multiplier: brain", 3.0f,
                "Damage multiplier for brain zones (skull/face/temple/eyes): the volumetric " +
                "model is calibrated on torso muscle; the brain is more sensitive per mm³ destroyed.",
                new AcceptableValueRange<float>(1f, 10f), true);
            VitalJawMult = Bind(sBal, "Vital multiplier: jaw", 1.5f,
                "Jaw zone multiplier (grievous, but it is not the brain — Garand Thumb).",
                new AcceptableValueRange<float>(1f, 10f), true);
            VitalNeckMult = Bind(sBal, "Vital multiplier: neck", 2.0f,
                "Neck multiplier (major vessels; the blood loss itself comes from the blood system).",
                new AcceptableValueRange<float>(1f, 10f), true);
            OrganTissueStrengthMPa = Bind(sBal, "Organ: tissue radial strength, MPa", 1.0f,
                "The one constant behind the temporary cavity radius: dE/dx = pi r^2 sigma. " +
                "Calibrated on published cavity diameters — 7.62x51 comes out near 60 mm of " +
                "radius, 9x19 near 30. Lower it and cavities reach further.",
                new AcceptableValueRange<float>(0.1f, 10f), true);
            OrganKHeart = Bind(sBal, "Organ: heart severity", 2.8f,
                "Damage multiplier for a cavity that reaches the heart and mediastinum, at " +
                "full overlap. From the ratio of AIS squares against one lobe of lung (25/9). " +
                "A channel straight through the heart does not use this — it is fatal.",
                new AcceptableValueRange<float>(1f, 10f), true);
            OrganKLiver = Bind(sBal, "Organ: liver severity", 1.8f,
                "Same for the liver (AIS 4, 16/9). Applies in full to a channel through it " +
                "that did not avulse it, and by overlap to one that only stretched it.",
                new AcceptableValueRange<float>(1f, 10f), true);
            OrganKSpine = Bind(sBal, "Organ: spinal cord severity", 2.3f,
                "Same for the cord (AIS 4-5). Only for a cavity beside it — a channel " +
                "through the plate severs it.",
                new AcceptableValueRange<float>(1f, 10f), true);
            OrganArrestChance = Bind(sBal, "Organ: cardiac arrest chance", 0.15f,
                "Ceiling on traumatic cardiac arrest from a cavity that passed beside the " +
                "heart. Scaled by how much of the zone the cavity reached and by the " +
                "high-velocity sigmoid, so pistols practically never do it.",
                new AcceptableValueRange<float>(0f, 1f), true);
            OrganAvulsionChance = Bind(sBal, "Organ: liver avulsion chance", 0.35f,
                "Ceiling on tearing the liver loose — the injury the combat autopsy series " +
                "lists as unsurvivable. Scaled by the high-velocity sigmoid and by the " +
                "cavity radius against the organ's own.",
                new AcceptableValueRange<float>(0f, 1f), true);
            OrganLiverRadiusMm = Bind(sBal, "Organ: liver radius, mm", 70f,
                "Half the liver's span. A cavity this wide can tear the whole organ off its " +
                "ligaments; a narrower one is scaled down in proportion.",
                new AcceptableValueRange<float>(10f, 200f), true);
            OrganBleedMlSec = Bind(sBal, "Organ: internal bleed, ml per s", 80f,
                "Bleed rate from an opened liver — the same rate a destroyed abdomen gets, " +
                "and for the same reason: the vena cava. Internal, so no bandage, tourniquet " +
                "or hemostatic reaches it. A cavity that only grazed the organ opens a share " +
                "of this.",
                new AcceptableValueRange<float>(0f, 300f), true);
            BleedVesselsTorso = Bind(sBal, "Bleed vessels: torso, per mm²", 6e-5f,
                "Major vessels per mm² of the plane a wound channel sweeps, in general " +
                "torso. Chance of an arterial bleed = 1 - exp(-density × swept), so a " +
                "longer or wider channel cuts more. Calibrated so a rifle round across a " +
                "chest lands near where the old per-cartridge chance was.",
                new AcceptableValueRange<float>(0f, 0.01f), true);
            BleedVesselsJunction = Bind(sBal, "Bleed vessels: junction, per mm²", 1.8e-4f,
                "Same for neck, groin and shoulder — where a major vessel runs and a " +
                "tourniquet has nothing to squeeze against. Three times the torso.",
                new AcceptableValueRange<float>(0f, 0.01f), true);
            BleedVesselsLimb = Bind(sBal, "Bleed vessels: limb, per mm²", 2.4e-5f,
                "Same for calves and forearms away from the bundles. The femoral and " +
                "brachial cases are handled separately by their own chance.",
                new AcceptableValueRange<float>(0f, 0.01f), true);
            BleedVesselsHead = Bind(sBal, "Bleed vessels: head, per mm²", 2.4e-5f,
                "Same for the head. What kills there is not volume loss.",
                new AcceptableValueRange<float>(0f, 0.01f), true);
            BleedHeavyMaxChance = Bind(sBal, "Bleed heavy max chance", 0.95f,
                "Ceiling on the arterial bleed chance. Nothing is ever certain.",
                new AcceptableValueRange<float>(0f, 1f), true);
            YawSpreadSigma = Bind(sBal, "Spread: yaw neck sigma", 0.35f,
                "How much the travel before a projectile goes broadside varies, as the " +
                "sigma of a log-normal about the cartridge's median. The single most " +
                "variable quantity in wound ballistics — one cartridge's neck length " +
                "varies twofold in gelatin. 0 makes every shot the median one. Drawn once " +
                "per shot: this is where the difference between two identical hits comes " +
                "from, instead of a die rolled on the damage.",
                new AcceptableValueRange<float>(0f, 1f), true);
            TissueSpread = Bind(sBal, "Spread: tissue density sigma", 0.15f,
                "How much the tissue along a channel varies from the calibrated average — " +
                "ribs, cartilage, diaphragm. Moves the channel length, and with it how " +
                "much energy is left behind.",
                new AcceptableValueRange<float>(0f, 0.5f), true);
            ZoneShiftMm = Bind(sBal, "Spread: organ position sigma, mm", 10f,
                "The game has one skeleton and people do not. Shifts the organ zones " +
                "sideways per shot, which turns 'hit the heart' from a step at the edge " +
                "of a box into a gradient.",
                new AcceptableValueRange<float>(0f, 50f), true);
            FleshRetentionAp = Bind(sBal, "Flesh retention for AP (X 0)", 0.60f,
                "FALLBACK (only when Physical damage model is off): share of damage/energy " +
                "a solid (X~0, AP) bullet retains when passing through a body part. " +
                "The physical model computes the exit via its own energy balance.",
                new AcceptableValueRange<float>(0f, 0.95f), true);
            FleshRetentionHp = Bind(sBal, "Flesh retention for HP (X 1)", 0.05f,
                "FALLBACK: same for a fully expanding bullet (X~1, HP/RIP).",
                new AcceptableValueRange<float>(0f, 0.95f), true);
            ArmorMitigationK = Bind(sBal, "Armor mitigation K", 0.4f,
                "Damage mitigation on PENETRATION: m = pen/(pen + K*resist). 0 = vanilla behavior.",
                new AcceptableValueRange<float>(0f, 2f), true);
            ArmorMitigationMin = Bind(sBal, "Armor mitigation min", 0.30f,
                "Lower bound of the mitigation on penetration.",
                new AcceptableValueRange<float>(0f, 1f), true);
            ArmorResistPerClass = Bind(sBal, "Armor resist per class", 10f,
                "Resistance estimate: armor class * this value.",
                new AcceptableValueRange<float>(5f, 20f), true);
            ArmorDurabilityFloor = Bind(sBal, "Armor durability floor", 0.5f,
                "Share of resistance left at zero armor durability.",
                new AcceptableValueRange<float>(0f, 1f), true);
            BabtBc1 = Bind(sBal, "BABT BC1 plateau end", 1.8f,
                "Below this BC — plateau: small fixed damage, Pain + a short concussion, " +
                "no internal bleeding.", new AcceptableValueRange<float>(0f, 5f), true);
            BabtBc2 = Bind(sBal, "BABT BC2 severe", 3.4f,
                "From this BC on — severe BABT: max damage, guaranteed internal bleeding, " +
                "disrupted breathing.", new AcceptableValueRange<float>(1f, 6f), true);
            BabtPlateauDamage = Bind(sBal, "BABT plateau damage", 2f,
                "Body part damage on the plateau (a bruise under the plate).",
                new AcceptableValueRange<float>(0f, 15f), true);
            BabtMaxDamage = Bind(sBal, "BABT max damage", 40f,
                "Body part damage at BC2+ (broken ribs, organ contusion).",
                new AcceptableValueRange<float>(5f, 120f), true);
            BabtBodyMassKg = Bind(sBal, "BABT body mass, kg", 80f,
                "W in the Sturdivan formula.", new AcceptableValueRange<float>(50f, 120f), true);
            BabtWallCm = Bind(sBal, "BABT body wall, cm", 3.5f,
                "T in the Sturdivan formula (chest wall thickness).",
                new AcceptableValueRange<float>(1f, 6f), true);
            BabtEnergyScale = Bind(sBal, "BABT energy scale", 1f,
                "Behind-armor energy multiplier: E_bfd = impact energy * BluntThroughput * this.",
                new AcceptableValueRange<float>(0.1f, 3f), true);
            BabtInternalBleedRate = Bind(sBal, "BABT internal bleed, ml per s", 2.5f,
                "Internal bleed rate from BABT; probability grows from BC1 to BC2.",
                new AcceptableValueRange<float>(0f, 20f), true);
            FragEnergyShare = Bind(sBal, "Fragment share", 0.4f,
                "Bullet fragmentation: with the physical model — the share of MASS that " +
                "goes into fragments (split evenly; fragment damage/penetration are " +
                "computed from their mass and velocity); with it off — the share of " +
                "energy/damage (fallback).",
                new AcceptableValueRange<float>(0f, 0.9f), true);
            FragBlockEnergyJ = Bind(sBal, "Frag block energy, J", 400f,
                "Fragment block threshold for class 1 armor: energy of the GOST Br1 test " +
                "bullet (5.9 g x 335 m/s = 331 J) +20% for fragment shape. Below it — guaranteed block.",
                new AcceptableValueRange<float>(100f, 1500f), true);
            FragBlockClassFactor = Bind(sBal, "Frag block class factor", 1.45f,
                "Threshold multiplier per armor class above 1 (GOST Br1->Br2 step: 331->485 J).",
                new AcceptableValueRange<float>(1f, 3f), true);
            LargeFragShare = Bind(sBal, "Large fragment share (fallback)", 0.02f,
                "Fallback share of large fragments (base plate/fuze) when the server did " +
                "not report the exact one (1/grenade fragment count). Only a large fragment " +
                "is allowed an honest penetration roll — class 1+ armor always stops a medium one.",
                new AcceptableValueRange<float>(0f, 1f), true);
            LargeFragEnergyMult = Bind(sBal, "Large fragment energy mult", 4f,
                "How many times more energetic a large fragment is than a medium one " +
                "(F-1 base plate ~6 g vs 1.5 g).",
                new AcceptableValueRange<float>(1f, 10f), true);

            // ===== 3. Blood & trauma (regular — gameplay tuning) =====
            // The ": Player" halves of the groups below live in section 7 instead: what
            // keeps YOU alive is worth having in one place, even at the cost of a group
            // being read across two sections.
            DeathForPmc = Bind(sBlood, "Death from bleeding: PMC", true,
                "Death from blood loss for PMC bots (USEC/BEAR).");
            DeathForScav = Bind(sBlood, "Death from bleeding: Scav", true,
                "Death from blood loss for all Savage-side NPCs: scavs, bosses, " +
                "raiders, cultists, etc.");
            SurgeryStopsBleeding = Bind(sBlood, "Surgical kit stops bleeding", true,
                "A surgical kit (CMS, Surv12) also closes the bleedings on the limb it " +
                "repairs. Vanilla leaves them running, which under PLATE keeps draining " +
                "blood out of a limb that was just operated on.");
            InternalBleedPmc = Bind(sBlood, "Internal bleeding: PMC", true,
                "Same for PMC bots (USEC/BEAR).");
            InternalBleedScav = Bind(sBlood, "Internal bleeding: Scav", true,
                "Same for all Savage-side NPCs.");
            BleedRatePmc = Bind(sBlood, "Bleed rate: PMC", 1.0f,
                "Same for PMC bots (USEC/BEAR).",
                new AcceptableValueRange<float>(0f, 10f));
            BleedRateScav = Bind(sBlood, "Bleed rate: Scav", 1.0f,
                "Same for all Savage-side NPCs.",
                new AcceptableValueRange<float>(0f, 10f));
            FractureCollapsePmc = Bind(sBlood, "Fracture collapse: PMC", true,
                "Same for PMC bots (USEC/BEAR).");
            FractureCollapseScav = Bind(sBlood, "Fracture collapse: Scav", true,
                "Same for all Savage-side NPCs.");
            // not a player switch — it changes how a destroyed limb behaves for everyone,
            // which is why it sits with the rest of the trauma rules rather than in 7
            LimbHitsCanKill = Bind(sBlood, "Limb hits can kill", true,
                "Vanilla behaviour, and it applies to everyone — you and bots alike. " +
                "Destroying an arm or a leg spills the excess damage over the surviving " +
                "parts, which is how shot-off legs kill. Turn off and arms and legs stop " +
                "being a route to death: they still take damage, black out, bleed and " +
                "cripple, but nothing carries over from them into the torso or the head.");
            CrippleEnabled = Bind(sBlood, "Cripple on destroyed part", true,
                "A destroyed body part = sprint ban, Endurance/Strength bonus rollback " +
                "and a speed limit — until surgery.");
            HeartbeatAtTier2 = Bind(sBlood, "Heartbeat at tier 2", true,
                "On severe blood loss — heartbeat sound and screen desaturation " +
                "(the vanilla critical-state package). You only.");
            FatigueAtTier2 = Bind(sBlood, "Fatigue at tier 2", true,
                "On severe blood loss — the Fatigue debuff (disrupted breathing).");
            Tier3MovementBan = Bind(sBlood, "Tier 3 sprint/jump ban", true,
                "Hypovolemic shock: at tier 3 (ATLS class IV) sprinting and jumping are " +
                "impossible until blood volume recovers above the threshold.");
            BloodHudVisible = Bind(sBlood, "Blood HUD", true,
                "BP (blood pressure) indicator at the bottom left of the screen.");
            BlastBarotrauma = Bind(sBlood, "Blast barotrauma", true,
                "A close grenade blast gives a chance of internal bleeding from barotrauma " +
                "(lungs/GI tract). Probability grows with blast wave damage.");
            FractureFallDelay = Bind(sBlood, "Fracture fall delay, s", 0.8f,
                "Seconds of moving on a broken leg before collapsing.",
                new AcceptableValueRange<float>(0.1f, 5f));
            TransfusionMlPerUse = Bind(sBlood, "Transfusion ml per use", 500f,
                "How many ml one use of a blood pack restores (sold by Therapist).",
                new AcceptableValueRange<float>(100f, 2000f));

            // ===== 3. Blood & trauma (advanced — medical anchors) =====
            BloodMaxMl = Bind(sBlood, "Blood volume max, ml", 5000f,
                "Total blood volume (~70 ml/kg).", new AcceptableValueRange<float>(3000f, 8000f), true);
            BleedHeavyTorso = Bind(sBlood, "Heavy bleed torso, ml per s", 16f,
                "Arterial bleeding: torso/head (~1 L/min).",
                new AcceptableValueRange<float>(1f, 90f), true);
            BleedHeavyLeg = Bind(sBlood, "Heavy bleed leg, ml per s", 16f,
                "Arterial bleeding: leg (without femoral artery involvement).",
                new AcceptableValueRange<float>(1f, 90f), true);
            BleedHeavyArm = Bind(sBlood, "Heavy bleed arm, ml per s", 8f,
                "Arterial bleeding: arm (brachial ~0.5 L/min).",
                new AcceptableValueRange<float>(1f, 90f), true);
            BleedLight = Bind(sBlood, "Light bleed, ml per s", 0.8f,
                "Venous/soft-tissue bleeding.",
                new AcceptableValueRange<float>(0.1f, 5f), true);
            SelfLimitBeta = Bind(sBlood, "Self-limiting beta", 1.5f,
                "Flow self-limiting via hypotension: Q = Q0*(V/Vmax)^beta.",
                new AcceptableValueRange<float>(0f, 4f), true);
            FemoralChance = Bind(sBlood, "Femoral artery chance", 0.4f,
                "Chance of femoral/iliac artery involvement when an arterial bleed comes " +
                "from a thigh or pelvis hit (Thigh/Pelvis hitboxes).",
                new AcceptableValueRange<float>(0f, 1f), true);
            FemoralBleedMlSec = Bind(sBlood, "Femoral bleed, ml per s", 75f,
                "Flow rate of a transected femoral artery (unconsciousness in 30-60 s IRL).",
                new AcceptableValueRange<float>(20f, 150f), true);
            CardiacOutputMlSec = Bind(sBlood, "Cardiac output cap, ml per s", 85f,
                "Physical cap on total blood loss (~5 L/min).",
                new AcceptableValueRange<float>(20f, 200f), true);
            StomachDestroyedBleed = Bind(sBlood, "Stomach destroyed bleed, ml per s", 80f,
                "Internal bleeding with a destroyed stomach (aorta/vena cava). " +
                "Bleeds into the abdominal cavity — no dressing, tourniquet or hemostatic " +
                "reaches it.", new AcceptableValueRange<float>(0f, 200f), true);
            CrippleStaminaCoeff = Bind(sBlood, "Cripple stamina coeff", 0.2f,
                "Stamina multiplier with a destroyed body part.",
                new AcceptableValueRange<float>(0.05f, 1f), true);
            CrippleSpeedLimit = Bind(sBlood, "Cripple speed limit", 0.4f,
                "Movement speed limit with a destroyed part.",
                new AcceptableValueRange<float>(0.1f, 1f), true);
            FractureEnergyMin = Bind(sBlood, "Bone fracture energy min, J", 200f,
                "Below this energy a bullet cannot break bone.",
                new AcceptableValueRange<float>(0f, 1000f), true);
            FractureEnergyFull = Bind(sBlood, "Bone fracture energy full, J", 900f,
                "From this energy on, a bone hit breaks it guaranteed.",
                new AcceptableValueRange<float>(200f, 5000f), true);
            BoneChanceThigh = Bind(sBlood, "Bone chance thigh", 0.35f,
                "Probability of clipping the femur on a thigh hit.",
                new AcceptableValueRange<float>(0f, 1f), true);
            BoneChanceCalf = Bind(sBlood, "Bone chance calf", 0.45f,
                "Calf: the tibia sits near the surface.",
                new AcceptableValueRange<float>(0f, 1f), true);
            BoneChanceUpperArm = Bind(sBlood, "Bone chance upper arm", 0.35f,
                "Upper arm (humerus).", new AcceptableValueRange<float>(0f, 1f), true);
            BoneChanceForearm = Bind(sBlood, "Bone chance forearm", 0.5f,
                "Forearm: two bones in a small cross-section.",
                new AcceptableValueRange<float>(0f, 1f), true);
            GuaranteedBleedMinDamage = Bind(sBlood, "Guaranteed bleed min damage", 3f,
                "Any penetrating wound with damage above this threshold is guaranteed to bleed.",
                new AcceptableValueRange<float>(0f, 20f), true);
            ThresholdTier1 = Bind(sBlood, "Tier 1 threshold", 0.85f,
                "Remaining blood (fraction); below it — ATLS class II.",
                new AcceptableValueRange<float>(0.5f, 1f), true);
            ThresholdTier2 = Bind(sBlood, "Tier 2 threshold", 0.70f,
                "Below — ATLS class III: tremor, tunnel vision, fatigue, heartbeat.",
                new AcceptableValueRange<float>(0.4f, 1f), true);
            ThresholdTier3 = Bind(sBlood, "Tier 3 threshold", 0.60f,
                "Below — ATLS class IV: continuous concussion.",
                new AcceptableValueRange<float>(0.3f, 1f), true);
            DeathThreshold = Bind(sBlood, "Death threshold", 0.50f,
                "Remaining blood at which death occurs (~50% of total blood volume).",
                new AcceptableValueRange<float>(0.2f, 0.9f), true);
            PassiveRegenMlMin = Bind(sBlood, "Passive regen, ml per min", 6f,
                "Volume recovery while all bleedings are stopped.",
                new AcceptableValueRange<float>(0f, 60f), true);
            ContusionTier3Strength = Bind(sBlood, "Concussion strength tier 3", 1.2f,
                "Strength of the continuous concussion at tier 3. 0 = disable.",
                new AcceptableValueRange<float>(0f, 3f), true);
            BlastInternalMinDamage = Bind(sBlood, "Blast internal min damage", 15f,
                "Blast wave damage at which a barotrauma chance appears.",
                new AcceptableValueRange<float>(0f, 100f), true);
            BlastInternalFullDamage = Bind(sBlood, "Blast internal full damage", 60f,
                "Blast wave damage at which barotrauma is guaranteed.",
                new AcceptableValueRange<float>(10f, 200f), true);
            BlastInternalMlSec = Bind(sBlood, "Blast internal bleed, ml per s", 3f,
                "Internal bleed rate from barotrauma.",
                new AcceptableValueRange<float>(0f, 20f), true);

            // ===== 4. Armor materials (all advanced) =====
            BindMaterialProfiles(sMat);

            // ===== 5. Overlay (debug tool — all behind Advanced, off by default) =====
            OverlayEnabled = Bind(sOverlay, "Hit debug overlay", false,
                "Debug hit overlay for playtesting: floating damage above bots and a log " +
                "panel. A regular player does not need it. REQUIRES A GAME RESTART.", null, true);
            OverlayFloatingText = Bind(sOverlay, "Floating text", true,
                "Floating damage/event text above the bot that was hit.", null, true);
            OverlayPanelKey = Bind(sOverlay, "Panel toggle key", new KeyboardShortcut(KeyCode.F10),
                "Hotkey to show/hide the hit log panel.", null, true);
            OverlayPanelVisible = Bind(sOverlay, "Panel visible", false,
                "Current log panel state (toggled by the hotkey).", null, true);
            OverlayPanelMaxLines = Bind(sOverlay, "Panel max lines", 22,
                "How many recent events to keep in the panel.",
                new AcceptableValueRange<int>(5, 60), true);
            OverlayFloatSeconds = Bind(sOverlay, "Float duration, s", 2.5f,
                "Floating text lifetime.",
                new AcceptableValueRange<float>(0.5f, 8f), true);
            OverlayOnlyMyFights = Bind(sOverlay, "Only my fights", true,
                "Show only events caused by your own shots.", null, true);
            OverlayMaxFloatDistance = Bind(sOverlay, "Max float distance, m", 300f,
                "Floating text is not drawn beyond this distance.",
                new AcceptableValueRange<float>(50f, 1000f), true);

            // ===== 6. Debug (all advanced, off by default) =====
            // The journal is deliberately NOT under the overlay section: it used to be,
            // and since the overlay is off by default that made the file bug reports are
            // built from silently non-existent for everyone.
            OverlayLogHits = Bind(sDebug, "Log events to file", true,
                "Write the event journal to events.log next to the plugin (buffered, " +
                "rotated at 500 KB). Works with the overlay off. Attach this to bug reports.",
                null, true);
            TrackSelfHits = Bind(sDebug, "Track hits on you", false,
                "Instrument hits ON YOU (noisy during development).", null, true);
            SelfTestOnLoad = Bind(sDebug, "Patch targets self-test on load", true,
                "Resolve all patch targets on startup and log the result " +
                "(catches name drift after an SPT update — a single log line).", null, true);
            VerboseLog = Bind(sDebug, "Verbose logging", false,
                "Verbose PLATE logging (including chronic damage ticks).", null, true);
            AnatomyDebug = Bind(sDebug, "Log hitbox geometry", false,
                "One line per hit naming the box that was hit, how its width/height/depth " +
                "axes resolved against the character, and where in it the bullet entered " +
                "(signed fractions, +width being the target's own right). Turn on to check " +
                "the organ zones sit where the anatomy says; noisy otherwise.", null, true);
            PerfTrace = Bind(sDebug, "Perf trace", false,
                "Profile PLATE subsystems: [PLATE-PERF] lines every 5 s.", null, true);
            ConfigVersion = Bind(sDebug, "Config version (internal)", 1,
                "Internal default-migration field — do not edit by hand.", null, true);

            // ===== 7. Player Survivability =====
            // Everything that decides how long YOU last, in one place: the overrides that
            // deliberately depart from the physical model, and the ": Player" halves of the
            // per-side groups that used to sit with their PMC and Scav siblings in section
            // 3. The defaults change nothing — the model as calibrated.
            PreventPlayerDeath = Bind(sSurv, "Prevent death", false,
                "Hits can no longer kill YOU: head and thorax never black out, each keeps " +
                "at least 1 HP, and damage that would spill into them from a destroyed " +
                "limb is trimmed to the same floor. Everything else stays — parts still " +
                "take damage, limbs still black out, bleedings and fractures still happen. " +
                "Death from blood loss is NOT covered by this: that is \"Death from " +
                "bleeding: Player\" below, so turn that off too to be fully unkillable.");
            DamageScalePlayer = Bind(sSurv, "Damage scale: Player", 1.0f,
                "Multiplier for flesh damage YOU take, computed by the physical model. " +
                "1.0 = realism as calibrated; below — bullet-sponge mode, above — for " +
                "maniacs. The PMC and Scav halves of this group are in section 2.",
                new AcceptableValueRange<float>(0.1f, 10f));
            DeathForPlayer = Bind(sSurv, "Death from bleeding: Player", true,
                "Death from blood loss for YOU. When off: blood pressure drops to 0% and " +
                "hangs at the threshold with all the debuffs, but death never occurs. " +
                "The PMC and Scav halves of this group are in section 3.");
            InternalBleedPlayer = Bind(sSurv, "Internal bleeding: Player", true,
                "Internal bleeding for YOU: from a destroyed abdomen, from behind-armor " +
                "trauma to the torso or head, and from blast barotrauma. All three bleed " +
                "into a cavity, so there is no wound icon and no field medicine closes " +
                "them — dressings, tourniquets, hemostatics and surgery all fall short. " +
                "A destroyed limb is NOT one of these: it bleeds externally and is " +
                "treatable. Turn off if you would rather every bleed be treatable.");
            BleedRatePlayer = Bind(sSurv, "Bleed rate: Player", 1.0f,
                "Multiplier for how fast blood leaves YOUR body — every bleed, external " +
                "and internal, plus the cardiac output cap, so 2.0 really is twice as fast. " +
                "0 = you never lose blood, bleedings become cosmetic. Applies immediately, " +
                "including to bleedings already running.",
                new AcceptableValueRange<float>(0f, 10f));
            FractureCollapsePlayer = Bind(sSurv, "Fracture collapse: Player", true,
                "Leg fracture without a splint: collapse to prone when trying to walk, " +
                "and a jump ban (also with a destroyed stomach/leg). A splint lifts the " +
                "restrictions.");
            PlayerBleedChance = Bind(sSurv, "Bleeding chance on hits to you", 1f,
                "Multiplier on the chance that a hit ON YOU starts a bleeding — heavy, " +
                "guaranteed light, and the internal ones from opened organs, destroyed " +
                "parts and blast alike. 1 = the model's own chance, 0 = you never bleed " +
                "from being shot. Does not touch bleedings already running, bots, or the " +
                "blood you have already lost.",
                new AcceptableValueRange<float>(0f, 1f));
            // The four above moved here out of "3. Blood & trauma" in v4. BepInEx keys are
            // (section, key) pairs, so to the cfg file that is a delete plus an add and a
            // tuned value would silently revert to the default. Binding the old pair reads
            // it back; the v4 migration copies it over. Same trick as the retired
            // "Damage scale". The keys have to stay verbatim — reading them is the point.
            LegacyDeathForPlayer = Bind(sBlood, "Death from bleeding: Player", true,
                RetiredNote, null, true);
            LegacyInternalBleedPlayer = Bind(sBlood, "Internal bleeding: Player", true,
                RetiredNote, null, true);
            LegacyBleedRatePlayer = Bind(sBlood, "Bleed rate: Player", 1.0f,
                RetiredNote, new AcceptableValueRange<float>(0f, 10f), true);
            LegacyFractureCollapsePlayer = Bind(sBlood, "Fracture collapse: Player", true,
                RetiredNote, null, true);
            LegacyDamageScalePlayer = Bind(sBal, "Damage scale: Player", 1.0f,
                RetiredNote, new AcceptableValueRange<float>(0.1f, 10f), true);

            PlayerOrganCrits = Bind(sSurv, "Critical organ hits on you", true,
                "On (the model as designed): a channel through YOUR heart, liver or spinal " +
                "cord is lethal or nearly so, and brain, jaw and neck carry their vital " +
                "multipliers. Off: those hits deposit ordinary flesh damage and nothing " +
                "else — no lethality, no multiplier. Organs still bleed when opened; that " +
                "is the bleeding chance above, not this.");

            MigrateDefaults();
        }

        /// <summary>
        /// Armor material profiles: (durability wear multiplier on BLOCK, on PENETRATION,
        /// BABT energy spread diameter in cm).
        /// Physics: steel does not deform — the whole plate plus the carrier's soft panel
        /// spreads the energy, stopped bullets cause almost no wear ("gong"); ceramic
        /// crumbles in cones from every hit; aramid is plastic — the backface deformation
        /// is deep and localized.
        /// </summary>
        private static void BindMaterialProfiles(string section)
        {
            var defaults = new Dictionary<EArmorMaterial, (float Block, float Pen, float Spread, string Note)>
            {
                [EArmorMaterial.ArmoredSteel] = (0.05f, 1f, 10f, "gong: no wear without penetration"),
                [EArmorMaterial.Titan] = (0.30f, 1f, 7f, "elastic-plastic"),
                [EArmorMaterial.Aluminium] = (0.50f, 1f, 6f, "soft metal"),
                [EArmorMaterial.Combined] = (0.90f, 1f, 5f, "ceramic + backing"),
                [EArmorMaterial.Ceramic] = (1.50f, 1f, 5.5f, "crumbles from any hit"),
                [EArmorMaterial.UHMWPE] = (0.60f, 1f, 4.5f, "UHMWPE: elastic, localized heating"),
                [EArmorMaterial.Aramid] = (1.00f, 1f, 2.5f, "soft pack: deep localized deformation"),
                [EArmorMaterial.Glass] = (1.80f, 1f, 3f, "cracks"),
            };

            foreach (var kv in defaults)
            {
                Materials[kv.Key] = new MaterialProfile
                {
                    DuraBlockMult = Bind(section, $"{kv.Key} durability mult on block",
                        kv.Value.Block,
                        $"Durability wear multiplier on a NON-penetrating hit ({kv.Value.Note}). 1 = vanilla.",
                        new AcceptableValueRange<float>(0f, 3f), true),
                    DuraPenMult = Bind(section, $"{kv.Key} durability mult on pen",
                        kv.Value.Pen, "Durability wear multiplier on penetration. 1 = vanilla.",
                        new AcceptableValueRange<float>(0f, 3f), true),
                    SpreadCm = Bind(section, $"{kv.Key} BABT spread, cm",
                        kv.Value.Spread,
                        "Effective behind-armor energy distribution diameter (D in the Sturdivan formula).",
                        new AcceptableValueRange<float>(1f, 20f), true),
                };
            }
        }

        /// <summary>
        /// Everything the user moved away from its default, one line, for the journal
        /// header. Reading a bug report without knowing the settings behind it wastes a
        /// round trip on every side.
        /// </summary>
        public static string ChangedSettings()
        {
            if (_cfg == null)
            {
                return "unavailable";
            }

            var changed = new List<string>();
            foreach (var key in _cfg.Keys)
            {
                var entry = _cfg[key];
                if (entry != null && !Equals(entry.BoxedValue, entry.DefaultValue))
                {
                    changed.Add($"{key.Key}={entry.BoxedValue}");
                }
            }

            return changed.Count == 0 ? "all default" : string.Join(", ", changed);
        }

        /// <summary>
        /// BepInEx: the value saved in the cfg always wins over a new default from code.
        /// Migration updates a setting to the new default ONLY if the user never changed
        /// it (current value == old default). Custom values are left alone.
        /// </summary>
        private static void MigrateDefaults()
        {
            if (ConfigVersion.Value >= CurrentConfigVersion)
            {
                return;
            }

            // v2 (2026-07-29): modules enabled by default + medical bleed-rate anchors
            if (ConfigVersion.Value < 2)
            {
                Migrate(BallisticsEnabled, false, true);
                Migrate(BloodEnabled, false, true);
                Migrate(BleedHeavyTorso, 9f, 16f);
                Migrate(BleedHeavyLeg, 13f, 16f);
                Migrate(BleedHeavyArm, 7f, 8f);
                Migrate(StomachDestroyedBleed, 35f, 80f);
            }

            // v3: "Damage scale" became one knob per side — carry the old value over
            if (ConfigVersion.Value < 3)
            {
                var legacy = LegacyDamageScale.Value;
                Migrate(DamageScalePlayer, 1f, legacy);
                Migrate(DamageScalePmc, 1f, legacy);
                Migrate(DamageScaleScav, 1f, legacy);
            }

            // v4: the four ": Player" knobs moved from "3. Blood & trauma" into
            // "7. Player Survivability". Moving a key between sections loses its saved
            // value, so what the player had under the old section is carried across.
            if (ConfigVersion.Value < 4)
            {
                Migrate(DeathForPlayer, true, LegacyDeathForPlayer.Value);
                Migrate(InternalBleedPlayer, true, LegacyInternalBleedPlayer.Value);
                Migrate(BleedRatePlayer, 1f, LegacyBleedRatePlayer.Value);
                Migrate(FractureCollapsePlayer, true, LegacyFractureCollapsePlayer.Value);
            }

            // v5: "Damage scale: Player" followed them out of "2. Ballistics". Its own
            // block rather than folded into v4 — a cfg that already reached 4 would skip
            // the move and quietly reset the knob.
            //
            // Runs after v3, which seeds this same entry from the retired single "Damage
            // scale". No conflict: Migrate only writes over a value still sitting on the
            // old default, so whichever of the two had something to say keeps it.
            if (ConfigVersion.Value < 5)
            {
                Migrate(DamageScalePlayer, 1f, LegacyDamageScalePlayer.Value);
            }

            ConfigVersion.Value = CurrentConfigVersion;
            Plugin.Log.LogInfo($"[PLATE] Config migrated to v{CurrentConfigVersion}");
        }

        private static void Migrate<T>(ConfigEntry<T> entry, T oldDefault, T newDefault)
            where T : System.IEquatable<T>
        {
            if (entry != null && entry.Value.Equals(oldDefault))
            {
                entry.Value = newDefault;
            }
        }
    }
}
