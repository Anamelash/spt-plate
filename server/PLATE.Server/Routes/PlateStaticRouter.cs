using PLATE.Server.Config;
using PLATE.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace PLATE.Server.Routes;

/// <summary>Normalizer data (X, E0, PDM for each cartridge) for the client module.</summary>
public static class PlateAmmoData
{
    public static string Json { get; set; } = "{}";
}

/// <summary>Config loaded in OnLoad — for the request handlers.</summary>
public static class PlateConfigHolder
{
    public static PlateServerConfig Config { get; set; } = new();
}

/// <summary>POST /plate/blood-set body. Explicit name contract — the client sends lowercase.</summary>
public record BloodSetRequest : IRequestData
{
    [System.Text.Json.Serialization.JsonPropertyName("cur")]
    public double Cur { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("max")]
    public double Max { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("died")]
    public bool Died { get; set; }
}

/// <summary>POST /plate/item-use body: deducting one blood bag use.</summary>
public record ItemUseRequest : IRequestData
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

[Injectable]
public class PlateStaticRouter(JsonUtil jsonUtil, BloodPersistence bloodPersistence) : StaticRouter(jsonUtil,
[
    new RouteAction<EmptyRequestData>(
        "/plate/ammo-data",
        async (url, info, sessionId, output) => await new ValueTask<string>(PlateAmmoData.Json)
    ),
    new RouteAction<EmptyRequestData>(
        "/plate/blood-get",
        async (url, info, sessionId, output) => await new ValueTask<string>(
            bloodPersistence.GetJson(sessionId, PlateConfigHolder.Config))
    ),
    new RouteAction<BloodSetRequest>(
        "/plate/blood-set",
        async (url, info, sessionId, output) => await new ValueTask<string>(
            bloodPersistence.SetFromClient(sessionId, info.Cur, info.Max, info.Died))
    ),
    new RouteAction<ItemUseRequest>(
        "/plate/item-use",
        async (url, info, sessionId, output) => await new ValueTask<string>(
            bloodPersistence.ConsumeItemUse(sessionId, info.Id,
                PlateConfigHolder.Config.Blood.TransfusionUses))
    ),
])
{
}
