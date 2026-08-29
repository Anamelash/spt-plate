using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;

namespace PLATE.Server.Config;

/// <summary>
/// Loads (or creates) config.jsonc exactly once per server start, whichever entry
/// point asks first. There are two of them — PlateItemRegistration early and
/// PlateServerMod at PostLoad — and both need the same config, so the result is
/// cached statically and published to <see cref="Routes.PlateConfigHolder"/> for the
/// request handlers. OnLoad components run sequentially, so a flag is enough.
/// </summary>
[Injectable]
public class PlateConfigLoader(ISptLogger<PlateConfigLoader> logger)
{
    public const string ConfigFileName = "config.jsonc";

    private static PlateServerConfig? _loaded;

    public async Task<PlateServerConfig> LoadAsync(string modPath, CancellationToken ct)
    {
        if (_loaded != null)
        {
            return _loaded;
        }

        _loaded = await LoadOrCreateConfig(modPath, ct);
        Routes.PlateConfigHolder.Config = _loaded; // for request handlers (blood-get/set)
        return _loaded;
    }

    private async Task<PlateServerConfig> LoadOrCreateConfig(string modPath, CancellationToken ct)
    {
        var path = Path.Combine(modPath, ConfigFileName);
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, DefaultConfigJsonc, ct);
            logger.Debug($"[PLATE] Config not found, default written to {path}");
        }
        else
        {
            await MigrateConfigText(path, ct);
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            };
            return JsonSerializer.Deserialize<PlateServerConfig>(
                       await File.ReadAllTextAsync(path, ct), options)
                   ?? new PlateServerConfig();
        }
        // Shutdown is not a broken config: let the cancellation through instead of
        // reporting it as a parse failure and carrying on with defaults.
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error($"[PLATE] Failed to parse {ConfigFileName}, using defaults: {ex.Message}");
            return new PlateServerConfig();
        }
    }

    /// <summary>
    /// Retired defaults in an existing config. A value is only rewritten when it still
    /// holds the old default — a value the user picked is theirs. Surgical text edits
    /// rather than a rewrite: the file is hand-edited jsonc and the comments in it are
    /// the documentation.
    /// </summary>
    private async Task MigrateConfigText(string path, CancellationToken ct)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, ct);
            var before = text;

            // the card's reference shot got a definition: a perpendicular hit into the
            // chest of a gelatin manikin, 250 mm, instead of an unlabelled 350 mm
            text = text.Replace("\"BodyDepthMm\": 350", "\"BodyDepthMm\": 250");

            // the Br realignment: the threshold ladder is indexed by class 0..6 now
            // (class = Br), so the old six-element array — whose first entry was the
            // anti-fragment tier the game used to call class 1 — gains the Br6 rung
            text = text.Replace("\"ClassULimitJmm2\": [2.5, 5.2, 12, 40, 65, 90],",
                "\"ClassULimitJmm2\": [2.5, 5.2, 12, 40, 65, 90, 165],");

            if (text != before)
            {
                await File.WriteAllTextAsync(path, text, ct);
                logger.Debug($"[PLATE] {ConfigFileName}: retired defaults updated");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Warning($"[PLATE] Could not update {ConfigFileName}: {ex.Message}");
        }
    }

    /// <summary>Config template with a comment on every parameter.</summary>
    private const string DefaultConfigJsonc =
        """
        {
          "Modules": {
            // Ammo normalization (including mod-added rounds)
            "AmmoNormalizer": true,
            // Muzzle velocity recomputed from barrel length (Le Duc, calibrated against
            // published barrel-length ladders). Weapon packs and live-values backports
            // ship modifiers that are not physical, and they feed straight into damage.
            "BarrelNormalizer": true,
            // Armour material, thickness and class from the real product the item is
            // modelled on. Also brings an item stamped with a class its material cannot
            // hold — the sewn aramid packages the game rates 3 — back to the class its
            // construction earns.
            "ArmorNormalizer": true,
            // Grenade fragments/blast brought to prototype specs (ammo-reference.jsonc)
            "GrenadePhysics": true,
            // Globals tweaks for the blood system: bleedings no longer damage HP
            // (blood drains into the client module's BloodVolume), Fresh Wound lasts to raid end.
            // Requires the PLATE client — otherwise bleedings become harmless!
            "BloodGlobals": true
          },

          // ===== Barrels: v = v_inf*L/(L+C), Le Duc =====
          // Reference barrels and C per caliber live in ammo-reference.jsonc; these are
          // the guard rails. Anything the model cannot handle is left alone and listed
          // in plate-barrel-report.md.
          "BarrelNormalizer": {
            // Range of lengths treated as a barrel, mm (a PM is 93.5, an NSV is 1100)
            "MinLengthMm": 80,
            "MaxLengthMm": 1200,
            // The model does not produce modifiers past this; one means bad input
            "MaxPercent": 45,
            // Muzzle devices and suppressors: a brake shifts velocity by a hair
            "DeviceClampPercent": 2
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
            // Reference shot the card damage is quoted for: perpendicular hit into the
            // centre of the chest of a gelatin manikin at 5 m. 250 mm is the chest depth
            // of an adult male. In a raid the path is the real collider chord instead.
            "BodyDepthMm": 250,
            // Permanent cavity: mm³ of channel volume per 1 HP. Anchor: ~2.3 rifle
            // hits to the torso (85 HP) to incapacitation per combat-mortality
            // research, ~37 HP a hit — not the game's own damage numbers
            "WoundVolumePerHp": 381,
            // Temporary pulsating cavity: eff = 1/(1+exp(−(v−center)/width)) —
            // sigmoid at the high-velocity wound boundary (~600 m/s, Fackler)
            "TcVelocityCenter": 600,
            "TcVelocityWidth": 80,
            // J of deposited temporary-cavity energy per 1 HP. Same anchor as
            // WoundVolumePerHp - the two move together
            "TcEnergyPerHp": 74,
            // Fragmentation converts stretch into tearing: (1 + this·frag). frag is
            // DERIVED - a bullet breaks up where it turns, if it is still faster there
            // than the threshold below, and only its deformable non-core share breaks.
            // The vanilla FragmentationChance field takes no part in it (3.6)
            "TcFragBonus": 0.5,
            // Velocity at the tumble point above which a jacket lets go, m/s.
            // Published band 600-700 for thin-jacketed ball; the bottom of the band,
            // read at the tumble point, reproduces which cartridges fragment in
            // gelatin (M193/M855 yes, 7.62x39 PS and pistol ball no)
            "FragVelocityThreshold": 600,
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
            // Component weights of the expansiveness index X. The old third component
            // (vanilla FragmentationChance) is gone with 3.6: the model derives
            // fragmentation itself, and feeding the game's opinion into X was noise
            "WeightSpecificDamage": 0.45,       // percentile of specific damage (HP/J)
            "WeightSpecificPenetration": 0.45,  // percentile of specific penetration (negative)
            // Minimum rounds per caliber for percentiles; fewer — global regression
            "MinCaliberCohort": 4,
            // Penetration: pen units per J/mm² of cross-section. Anchors: M61 75->64, M995 64->53, PS 44.6->35
            "PenPerEnergyDensity": 0.85,
            // How small a core to assume for a cartridge ammo-reference.jsonc does not
            // name: 1.0 at the cohort median of specific penetration, down to (1 - this)
            // at the top of it. 0 = every unlisted bullet strikes as one piece
            "CoreFallbackDepth": 0.5,
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
            // U_limit per class 0..6 (J/mm², zero wear) — the INDEX is the class.
            // Game classes are GOST classes since the Br realignment: 0 — the
            // anti-fragment tier (spent shot/fragments only, does NOT stop a pistol
            // bullet); 1..6 — Br1..Br6, each at the specific energy of its own test
            // cartridge (Br1 = PM at 5.2; Br6 = 12.7 B-32, held by nothing worn)
            "ClassULimitJmm2": [2.5, 5.2, 12, 40, 65, 90, 165],
            // Wear is probabilistic, not a smooth multiplier (3.4): a worn plate is
            // intact where nothing hit it and broken where something did. Chance of
            // striking a damaged spot = missing durability; a struck spot keeps
            // 1 - x^WearExponentK of its thickness, with x from SpotDamageQ.
            // Per-material values live in the profiles below.
            // How far a deformable bullet spreads on the face of the panel before it has
            // finished loading it: impact area x (1 + this*X). A core concentrates the
            // energy, deformation spreads it. 0 = every bullet loads its own calibre
            "ExpansionOnArmor": 0.6,
            // The fraction of its thickness a hit must bite into before a ductile plate
            // (steel/titanium/aluminium) wears at all: below it a stopped bullet leaves
            // a dent and nothing else — no durability loss, no wear spot; above it the
            // price ramps to the full energy price at the ballistic limit. Anchors:
            // Armox 600T takes repeated 7.62 M61 AP on the same spot without losing
            // resistance (10.1016/j.jestch.2023.101337); MIL-STD-662F reads metal as
            // pristine two calibres from a crater. 0 = old behaviour, every stop pays
            "WearDepthFraction": 0.5,
            // Share of the energy price a fibre pack (UHMWPE/aramid) pays for a hit it
            // STOPPED. 0 = the published multi-hit data: Dyneema HB26 panels GAIN V50
            // during their own eight-shot test, and a soft pack shot at 75 mm spacing
            // outperforms the same pack at 150 (10.52202/080042-0031). Penetrations
            // pay in full — those fibres are cut
            "FibreBlockWearFraction": 0.0,
            // Material profiles: ULimitMult/ECostMult — threshold and energy-cost
            // multipliers (E_cost = ECostMult·U_eff·A_core); KDef — deformation
            // (X_out = X + KDef·X); KFrag — erosion of whatever comes through, a
            // property of the barrier (ceramic grinds a core down, aramid does not);
            // DAreaMm — radius of local damage around a hit; its ceiling has an
            // anchor: the standards space scored shots so damage zones do not
            // interact, NIJ 0101.06 at 51 mm, so no zone exceeds 51;
            // SpotDamageQ/WearExponentK — probabilistic wear (3.4): a hit spot
            // accumulates x = 1-(1-q)^n of damage and keeps 1-x^k of its thickness.
            // The q values are ASSUMPTIONS pending makers' multi-hit data; k from
            // the resolution of 3.4 (aramid 4, soft ductile 3, hard ductile 2,
            // brittle 1.5). SharpVulnMult — fiber vulnerability to sharp-nosed
            // bullets (U × (1 - this·clamp01((0.5-X)·2))); JPerDurability — J of
            // absorbed energy per 1 durability point.
            "Materials": {
              "Aramid":       { "ULimitMult": 0.85, "ECostMult": 0.50, "KDef": 0.05, "KFrag": 0.00, "DAreaMm": 51, "SpotDamageQ": 0.30, "WearExponentK": 4,   "SharpVulnMult": 0.25, "JPerDurability": 400 },
              "UHMWPE":       { "ULimitMult": 1.00, "ECostMult": 0.35, "KDef": 0.02, "KFrag": 0.00, "DAreaMm": 45, "SpotDamageQ": 0.40, "WearExponentK": 3,   "SharpVulnMult": 0.35, "JPerDurability": 450 },
              "ArmoredSteel": { "ULimitMult": 1.15, "ECostMult": 0.85, "KDef": 0.50, "KFrag": 0.10, "DAreaMm": 15, "SpotDamageQ": 0.50, "WearExponentK": 2,   "SharpVulnMult": 0.00, "JPerDurability": 700 },
              "Titan":        { "ULimitMult": 1.00, "ECostMult": 1.00, "KDef": 0.35, "KFrag": 0.05, "DAreaMm": 20, "SpotDamageQ": 0.50, "WearExponentK": 2,   "SharpVulnMult": 0.00, "JPerDurability": 500 },
              "Aluminium":    { "ULimitMult": 0.90, "ECostMult": 0.60, "KDef": 0.30, "KFrag": 0.05, "DAreaMm": 25, "SpotDamageQ": 0.40, "WearExponentK": 3,   "SharpVulnMult": 0.00, "JPerDurability": 350 },
              "Ceramic":      { "ULimitMult": 1.25, "ECostMult": 0.70, "KDef": 0.60, "KFrag": 0.35, "DAreaMm": 51, "SpotDamageQ": 0.90, "WearExponentK": 1.5, "SharpVulnMult": 0.00, "JPerDurability": 150 },
              "Glass":        { "ULimitMult": 0.80, "ECostMult": 0.50, "KDef": 0.40, "KFrag": 0.15, "DAreaMm": 51, "SpotDamageQ": 0.90, "WearExponentK": 1.5, "SharpVulnMult": 0.00, "JPerDurability": 100 },
              "Combined":     { "ULimitMult": 1.00, "ECostMult": 0.65, "KDef": 0.30, "KFrag": 0.10, "DAreaMm": 45, "SpotDamageQ": 0.60, "WearExponentK": 2,   "SharpVulnMult": 0.10, "JPerDurability": 300 }
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
