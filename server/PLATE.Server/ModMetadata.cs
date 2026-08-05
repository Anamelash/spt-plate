using SPTarkov.Server.Core.Models.Spt.Mod;

namespace PLATE.Server;

// The bundles.json next to this dll (the blood bag model) is picked up by the server on
// its own: 4.1 dropped the IsBundleMod flag and looks for the manifest instead.
public record PlateModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.anamelash.plate";
    public string Name { get; init; } = "P.L.A.T.E.";
    public string Author { get; init; } = "crow";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    // No prepatcher: PLATE extends no Core enum, everything it changes it changes at
    // runtime through the database and Harmony.
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "CC-BY-NC-SA-4.0";
}
