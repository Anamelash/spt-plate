using System.Reflection;
using System.Text.Json;
using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace PLATE.Server;

/// <summary>
/// PLATE.Server entry point. PostDBModLoader + 9000: we start after content mods
/// have finished adding items to the DB — the normalizer must see everything.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 9000)]
public class PlateServerMod(
    DatabaseServer databaseServer,
    ModHelper modHelper,
    Services.AmmoNormalizer ammoNormalizer,
    Services.GrenadePhysics grenadePhysics,
    Services.BloodGlobals bloodGlobals,
    Services.TransfusionItem transfusionItem,
    ISptLogger<PlateServerMod> logger) : IOnLoad
{
    public const string ConfigFileName = "config.jsonc";

    public Task OnLoad()
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config = LoadOrCreateConfig(modPath);
        Routes.PlateConfigHolder.Config = config; // for request handlers (blood-get/set)

        var tables = databaseServer.GetTables();
        var ammoCount = tables.Templates?.Items?.Values
            .Count(i => i.Properties?.Caliber is not null) ?? 0;

        var version = new PlateModMetadata().Version;
        logger.Success($"[PLATE] Server {version} loaded. DB: {ammoCount} ammo templates visible " +
                       $"(modules: ammoNorm={config.Modules.AmmoNormalizer}, " +
                       $"bloodGlobals={config.Modules.BloodGlobals}, gost={config.Modules.GostArmor})");

        if (config.Modules.AmmoNormalizer)
        {
            ammoNormalizer.Run(config, modPath); // ammo normalization (incl. mod-added rounds)
        }

        if (config.Modules.GrenadePhysics)
        {
            grenadePhysics.Apply(config, modPath); // fragments/blast from prototype specs
        }

        if (config.Modules.BloodGlobals)
        {
            bloodGlobals.Apply(config); // globals tweaks for the blood system

            if (config.Blood.TransfusionItem)
            {
                transfusionItem.Apply(config, modPath); // blood bag item at the Therapist
            }
        }

        // TODO: GostArmor.Apply(tables, ...)

        return Task.CompletedTask;
    }

    private PlateServerConfig LoadOrCreateConfig(string modPath)
    {
        var path = Path.Combine(modPath, ConfigFileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, DefaultConfigJsonc);
            logger.Info($"[PLATE] Config not found, default written to {path}");
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            };
            return JsonSerializer.Deserialize<PlateServerConfig>(File.ReadAllText(path), options)
                   ?? new PlateServerConfig();
        }
        catch (Exception ex)
        {
            logger.Error($"[PLATE] Failed to parse {ConfigFileName}, using defaults: {ex.Message}");
            return new PlateServerConfig();
        }
    }

    /// <summary>Config template with a comment on every parameter.</summary>
    private const string DefaultConfigJsonc =
        """
        {
          "Modules": {
            // Ammo normalization (including mod-added rounds)
            "AmmoNormalizer": true,
            // Grenade fragments/blast brought to prototype specs (ammo-reference.jsonc)
            "GrenadePhysics": true,
            // Globals tweaks for the blood system: bleedings no longer damage HP
            // (blood drains into the client module's BloodVolume), Fresh Wound lasts to raid end.
            // Requires the PLATE client — otherwise bleedings become harmless!
            "BloodGlobals": true,
            // GOST armor classes (helmets >=4 -> 3). Low priority, disabled.
            "GostArmor": false
          },

          "AmmoNormalizer": {
            // ===== Wound channel model: Damage = PC + TC, capped by the E0/EnergyCapPerHp budget =====
            // false = legacy linear model Damage = E0/EnergyPerHp.
            "WoundChannelModel": true,
            // Channel depth (mm): K·(m/A)·ln(v/GelStopVelocity)·(1−ExpansionDepthFactor·X).
            // Log model of quadratic drag; calibration: 9mm FMJ ~50 cm of gelatin.
            "GelDepthK": 2700,
            "GelStopVelocity": 50,
            // Expansion: shortens the channel (1−cX·X) and widens the cross-section A·(1+eX·X)
            "ExpansionDepthFactor": 0.4,
            "ExpansionAreaFactor": 1.35,
            // Body (torso) thickness, mm — the channel deposits nothing beyond it
            "BodyDepthMm": 350,
            // Permanent cavity: mm³ of channel volume per 1 HP. Anchor: 9x19 PST -> ~54
            "WoundVolumePerHp": 710,
            // Temporary pulsating cavity: eff = 1/(1+exp(−(v−center)/width)) —
            // sigmoid at the high-velocity wound boundary (~600 m/s, Fackler)
            "TcVelocityCenter": 600,
            "TcVelocityWidth": 80,
            // J of deposited temporary-cavity energy per 1 HP. Anchor: 7.62x39 PS -> ~57
            "TcEnergyPerHp": 28,
            // Fragmentation converts stretch into tearing: (1 + this·FragmentationChance)
            "TcFragBonus": 0.5,
            // Energy budget: damage no higher than E0/this. Trims slow buckshot and light birdshot
            "EnergyCapPerHp": 7,
            // J per 1 HP of damage (only for WoundChannelModel=false). Anchor: PS 2036 J -> 57
            "EnergyPerHp": 35.7,
            // true: Damage of every round is recomputed by the model.
            // false: only fill missing fields, Damage is left untouched.
            "RescaleDamageFromEnergy": true,
            // Penetration: 0 = vanilla, 1 = fully from energy over cross-section area. Blend.
            "PenetrationBlend": 0.5,
            // Maximum PenetrationDamageMod for pure AP (expansiveness index X=0).
            "PdmMax": 0.35,
            // Component weights of the expansiveness index X
            "WeightSpecificDamage": 0.45,       // percentile of specific damage (HP/J)
            "WeightSpecificPenetration": 0.45,  // percentile of specific penetration (negative)
            "WeightFragmentation": 0.25,        // fragmentation chance contribution
            // Fragmentation normalization: clamp01(FragmentationChance / this value)
            "FragChanceNormalizer": 0.30,
            // Minimum rounds per caliber for percentiles; fewer — global regression
            "MinCaliberCohort": 4,
            // Penetration: pen units per J/mm² of cross-section. Anchors: M61 75->64, M995 64->53, PS 44.6->35
            "PenPerEnergyDensity": 0.85,
            // Bullet construction effect on penetration: multiplier (1 + this*(0.5-X))
            "PenConstructionFactor": 0.6,
            // Technical damage ceiling after the rescale
            "DamageCap": 999,
            // Buckshot: pellet masses from the spec reference book (ammo-reference.jsonc),
            // per-pellet Damage/Pen recomputed from energy
            "NormalizeBuckshot": true,
            // Damage floor of a single pellet/fragment (3, so small shot keeps its gradation)
            "MinPelletDamage": 3,
            // X for buckshot without a reference entry (lead deforms)
            "XBuckshotDefault": 0.7,
            // Write the normalization report (plate-ammo-report.md in the mod folder)
            "WriteReport": true,
            // Bleeding deltas from channel geometry (light: base+perMm*diameter;
            // heavy: perMm*diameter*(0.5+0.5X), pellets get the PelletHeavyFactor multiplier)
            "NormalizeBleedDeltas": true,
            "BleedLightBase": 0.05,
            "BleedLightPerMm": 0.02,
            "LightDeltaMax": 0.6,
            "BleedHeavyPerMm": 0.016,
            "HeavyDeltaMax": 0.5,
            "PelletHeavyFactor": 0.5
          },

          "Armor": {
            // ===== Physical armor — a modifier of the projectile state =====
            // U penetration threshold (J/mm²) instead of a pen roll; a penetrating bullet
            // pays energy (E_cost), deforms (K_def -> X) and loses mass (K_frag).
            // false = vanilla roll + GOST fragment gate.
            "PhysicalArmor": true,
            // Probability band around the threshold: ±fraction of U_limit, linear chance inside
            "ThresholdBand": 0.12,
            // Slant thickness: U_eff ~ 1/cos of the impact angle, capped at this cosine
            "AngleMinCos": 0.34,
            // U_limit per class 1..6 (J/mm², zero wear). Class 1 —
            // anti-fragmentation junk (construction helmets: spent shot/fragments only,
            // does NOT stop a pistol bullet); class 2 = GOST Br1 (PM, 5.2); above — Br2..Br5
            "ClassULimitJmm2": [2.5, 5.2, 12, 40, 65, 90],
            // Wear: U_eff = U·(floor + (1-floor)·durability%)
            "DurabilityFloor": 0.4,
            // Floor of local degradation (a shattered segment holds at least this fraction)
            "DegradeFloor": 0.15,
            // Material profiles: ULimitMult/ECostMult — threshold and energy-cost
            // multipliers (E_cost = ECostMult·U_eff·A_bullet); KDef — deformation
            // (X_out = X + KDef·X); KFrag — mass loss (·(1-0.5X): a hard core
            // shatters harder); DAreaMm/DegradeMult — local degradation
            // around the hit (ceramic cracks tile by tile, "gong" steel stays local);
            // SharpVulnMult — fiber vulnerability to sharp-nosed bullets
            // (U × (1 - this·clamp01((0.5-X)·2))); JPerDurability — J of absorbed
            // energy per 1 durability point (wear from energy, not from ArmorDamage).
            "Materials": {
              "Aramid":       { "ULimitMult": 0.85, "ECostMult": 0.50, "KDef": 0.05, "KFrag": 0.00, "DAreaMm": 60,  "DegradeMult": 0.80, "SharpVulnMult": 0.25, "JPerDurability": 400 },
              "UHMWPE":       { "ULimitMult": 1.00, "ECostMult": 0.35, "KDef": 0.02, "KFrag": 0.00, "DAreaMm": 50,  "DegradeMult": 0.85, "SharpVulnMult": 0.35, "JPerDurability": 450 },
              "ArmoredSteel": { "ULimitMult": 1.15, "ECostMult": 0.85, "KDef": 0.50, "KFrag": 0.10, "DAreaMm": 15,  "DegradeMult": 0.90, "SharpVulnMult": 0.00, "JPerDurability": 700 },
              "Titan":        { "ULimitMult": 1.00, "ECostMult": 1.00, "KDef": 0.35, "KFrag": 0.05, "DAreaMm": 20,  "DegradeMult": 0.85, "SharpVulnMult": 0.00, "JPerDurability": 500 },
              "Aluminium":    { "ULimitMult": 0.90, "ECostMult": 0.60, "KDef": 0.30, "KFrag": 0.05, "DAreaMm": 25,  "DegradeMult": 0.80, "SharpVulnMult": 0.00, "JPerDurability": 350 },
              "Ceramic":      { "ULimitMult": 1.25, "ECostMult": 0.70, "KDef": 0.60, "KFrag": 0.35, "DAreaMm": 80,  "DegradeMult": 0.25, "SharpVulnMult": 0.00, "JPerDurability": 150 },
              "Glass":        { "ULimitMult": 0.80, "ECostMult": 0.50, "KDef": 0.40, "KFrag": 0.15, "DAreaMm": 100, "DegradeMult": 0.20, "SharpVulnMult": 0.00, "JPerDurability": 100 },
              "Combined":     { "ULimitMult": 1.00, "ECostMult": 0.65, "KDef": 0.30, "KFrag": 0.10, "DAreaMm": 40,  "DegradeMult": 0.60, "SharpVulnMult": 0.10, "JPerDurability": 300 }
            }
          },

          "Grenades": {
            // Blast (Strength) from explosive mass by cube root; anchor in the reference book (RGD-5 110 g = 100)
            "BlastFromTnt": true,
            // Fragment expansiveness index for the penetration formula (torn steel)
            "FragmentX": 0.3,
            // Fragment bleeding deltas (ragged wounds)
            "FragLightDelta": 0.25,
            "FragHeavyDelta": 0.15
          },

          "Blood": {
            // Out-of-raid blood regeneration, ml per hour of real time
            "OutOfRaidRegenMlPerHour": 1200,
            // Blood bag (transfusion) item at Therapist LL1
            "TransfusionItem": true,
            "TransfusionPriceRub": 24000,
            // Uses per bag (volume per use is set by the client config)
            "TransfusionUses": 3
          },

          "Wounds": {
            // Light/HeavyBleeding lifetime in offline raids, sec. SPT vanilla: 600/900 —
            // bleedings "healed on their own". 999999 = until stopped or raid end.
            "BleedingLifetimeSec": 999999,
            // Fresh Wound: lifetime in seconds. Vanilla 480; 999999 = until raid end.
            "FreshWoundWorkingTime": 999999,
            // Zero out limb HP damage from bleedings (blood drains into BloodVolume).
            // Applied only when Modules.BloodGlobals = true.
            "DisableBleedingHpDamage": true,
            // Zero out the vanilla bullet fracture roll: the client rolls it itself
            // (bone chance per hitbox * bullet energy). Fall fractures remain.
            "DisableVanillaBulletFractures": true
          }
        }
        """;
}
