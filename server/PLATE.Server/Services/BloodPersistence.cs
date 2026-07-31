using System.Text.Json;
using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace PLATE.Server.Services;

/// <summary>
/// Blood volume persistence between raids.
/// The value lives in profile.characters.pmc.Health via JsonExtensionData —
/// the server models preserve unknown keys on round-trip, so no changes to the
/// SPT models are needed. Out of raid the volume slowly regenerates in real
/// time (computed on read).
/// </summary>
[Injectable]
public class BloodPersistence(SaveServer saveServer, ISptLogger<BloodPersistence> logger)
{
    public const string ExtensionKey = "PlateBlood";

    public class PlateBloodRecord
    {
        public double Cur { get; set; }
        public double Max { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>Current value including out-of-raid regeneration. Null-safe.</summary>
    public string GetJson(MongoId sessionId, PlateServerConfig cfg)
    {
        try
        {
            var health = saveServer.GetProfile(sessionId)?.CharacterData?.PmcData?.Health;
            if (health == null)
            {
                return "{}";
            }

            var rec = ReadRecord(health.ExtensionData);
            if (rec == null)
            {
                // first run: full volume (the client sets the maximum, the server stores it as-is)
                return "{}";
            }

            var hours = Math.Max(0, (DateTime.UtcNow - rec.UpdatedUtc).TotalHours);
            rec.Cur = Math.Min(rec.Max, rec.Cur + hours * cfg.Blood.OutOfRaidRegenMlPerHour);
            rec.UpdatedUtc = DateTime.UtcNow;
            WriteRecord(health, rec);

            return JsonSerializer.Serialize(rec);
        }
        catch (Exception ex)
        {
            logger.Error($"[PLATE] BloodPersistence.Get failed: {ex.Message}");
            return "{}";
        }
    }

    public string SetFromClient(MongoId sessionId, double cur, double max, bool died)
    {
        try
        {
            var health = saveServer.GetProfile(sessionId)?.CharacterData?.PmcData?.Health;
            if (health == null)
            {
                return "{}";
            }

            var rec = new PlateBloodRecord
            {
                // death = a "new body", like the vanilla post-death health reset
                Cur = died ? max : Math.Clamp(cur, 0, max),
                Max = max,
                UpdatedUtc = DateTime.UtcNow,
            };
            WriteRecord(health, rec);
            logger.Info($"[PLATE] Blood saved for {sessionId}: {rec.Cur:0}/{rec.Max:0} ml" +
                        (died ? " (died -> reset)" : ""));
            return JsonSerializer.Serialize(rec);
        }
        catch (Exception ex)
        {
            logger.Error($"[PLATE] BloodPersistence.Set failed: {ex.Message}");
            return "{}";
        }
    }

    /// <summary>
    /// Deducts one blood bag use in the profile (the client sends this after
    /// applying). Vanilla does not touch the resource (HpResourceRate=0) — the
    /// deduction happens only here and in the client-side item instance, otherwise
    /// the bag would have "refilled" after the raid.
    /// </summary>
    public string ConsumeItemUse(MongoId sessionId, string itemId, int defaultUses)
    {
        try
        {
            var items = saveServer.GetProfile(sessionId)?.CharacterData?.PmcData?.Inventory?.Items;
            var item = items?.FirstOrDefault(i => i.Id.ToString() == itemId);
            if (item == null)
            {
                return "{\"ok\":false}"; // item not in the PMC profile (died/dropped) — not a problem
            }

            item.Upd ??= new();
            item.Upd.MedKit ??= new() { HpResource = defaultUses };
            var left = Math.Max(0, (item.Upd.MedKit.HpResource ?? defaultUses) - 1);
            item.Upd.MedKit.HpResource = left;
            logger.Info($"[PLATE] Transfusion use consumed: {itemId} -> {left} left");
            return $"{{\"ok\":true,\"left\":{left}}}";
        }
        catch (Exception ex)
        {
            logger.Error($"[PLATE] BloodPersistence.ConsumeItemUse failed: {ex.Message}");
            return "{\"ok\":false}";
        }
    }

    private static PlateBloodRecord ReadRecord(Dictionary<string, object>? ext)
    {
        if (ext == null || !ext.TryGetValue(ExtensionKey, out var raw))
        {
            return null;
        }

        return raw switch
        {
            PlateBloodRecord rec => rec,
            JsonElement je => je.Deserialize<PlateBloodRecord>(),
            _ => null,
        };
    }

    private static void WriteRecord(SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBaseHealth health,
        PlateBloodRecord rec)
    {
        health.ExtensionData ??= new Dictionary<string, object>();
        health.ExtensionData[ExtensionKey] = rec;
    }
}
