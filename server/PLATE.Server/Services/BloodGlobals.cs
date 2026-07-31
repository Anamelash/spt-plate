using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace PLATE.Server.Services;

/// <summary>
/// Server-side globals tweaks for the blood system.
/// 1. Bleedings stop damaging limb HP — the blood drains into BloodVolume
///    (client module). Dehydration/energy stay vanilla.
/// 2. Fresh Wound lasts until the end of the raid (vanilla: 480 sec).
/// </summary>
[Injectable]
public class BloodGlobals(DatabaseServer databaseServer, ISptLogger<BloodGlobals> logger)
{
    public void Apply(PlateServerConfig cfg)
    {
        var effects = databaseServer.GetTables().Globals?.Configuration?.Health?.Effects;
        if (effects == null)
        {
            logger.Error("[PLATE] BloodGlobals: globals Health.Effects unavailable");
            return;
        }

        if (cfg.Wounds.DisableBleedingHpDamage)
        {
            effects.LightBleeding.DamageHealth = 0;
            effects.LightBleeding.DamageHealthDehydrated = 0;
            effects.HeavyBleeding.DamageHealth = 0;
            effects.HeavyBleeding.DamageHealthDehydrated = 0;
        }

        // Offline mode (SPT) gives bleedings a finite lifetime (600/900 s) —
        // they "healed on their own", devaluing the blood system and hemostatics.
        // A bleeding lives until it is stopped or the raid ends.
        effects.LightBleeding.OfflineDurationMin = cfg.Wounds.BleedingLifetimeSec;
        effects.LightBleeding.OfflineDurationMax = cfg.Wounds.BleedingLifetimeSec;
        effects.HeavyBleeding.OfflineDurationMin = cfg.Wounds.BleedingLifetimeSec;
        effects.HeavyBleeding.OfflineDurationMax = cfg.Wounds.BleedingLifetimeSec;

        // Elite Vitality (level 51) self-stops bleedings after 22/33 s — on a
        // leveled profile they just "blinked". Also extended to raid end.
        effects.LightBleeding.EliteVitalityDuration = cfg.Wounds.BleedingLifetimeSec;
        effects.HeavyBleeding.EliteVitalityDuration = cfg.Wounds.BleedingLifetimeSec;

        effects.Wound.WorkingTime = cfg.Wounds.FreshWoundWorkingTime;

        if (cfg.Wounds.DisableVanillaBulletFractures)
        {
            // the client rolls fractures itself (bone per collider × bullet energy);
            // FallingProbability (fall fractures) is left alone
            effects.Fracture.BulletHitProbability.K = 0;
            effects.Fracture.BulletHitProbability.B = 0;
            effects.BreakPart.BulletHitProbability.K = 0;
            effects.BreakPart.BulletHitProbability.B = 0;
        }

        logger.Success($"[PLATE] BloodGlobals applied: bleedHpDamage=" +
                       $"{(cfg.Wounds.DisableBleedingHpDamage ? "off" : "vanilla")}, " +
                       $"freshWoundTime={cfg.Wounds.FreshWoundWorkingTime}s, " +
                       $"vanillaBulletFractures={(cfg.Wounds.DisableVanillaBulletFractures ? "off" : "on")}");
    }
}
