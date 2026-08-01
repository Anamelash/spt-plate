using SPTarkov.Server.Core.Models.Spt.Mod;

namespace PLATE.Server;

public record PlateModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.anamelash.plate";
    public override string Name { get; init; } = "P.L.A.T.E.";
    public override string Author { get; init; } = "crow";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("0.9.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    // true: the server reads bundles.json and serves the custom bundles to the
    // client (the blood bag model). Without this flag the mod's bundle manifest
    // is silently ignored.
    public override bool? IsBundleMod { get; init; } = true;
    public override string License { get; init; } = "CC-BY-NC-SA-4.0";
}
