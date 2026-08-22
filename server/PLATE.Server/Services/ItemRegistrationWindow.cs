using System.Reflection;
using SPTarkov.Server.Core.Services.Mod;

namespace PLATE.Server.Services;

/// <summary>
/// Whether the server still accepts new item templates.
///
/// 4.0.13 accepts them for as long as mods run, so on this server the answer is always
/// yes. Later releases do not: from 4.1.3 the server sets
/// <c>CustomItemService.ProfilesLoaded</c> and snapshots the keys of the item table at
/// the start of SaveCallbacks, and once every IOnLoad has run, a key that was not in the
/// snapshot kills the process — an item registered after the profiles were read is an
/// item those profiles cannot resolve. 4.1.2 merely corrupted such profiles quietly,
/// which is how PLATE's blood bag ended up registered early in the first place.
///
/// PLATE registers at <c>PostDBModLoader + 8990</c>, late enough to see the grenades
/// other content mods add. That position is a compile-time constant baked into our
/// attribute, so the day a server draws a line in front of it, "in time" quietly stops
/// being in time. This asks the server where the line is instead of assuming: a pass
/// that arrives too late skips its items and says so in the log, rather than taking the
/// whole server down with it.
///
/// The property does not exist in 4.0.13 — hence reflection. No property means no
/// cutoff, which is exactly what this version does.
/// </summary>
internal static class ItemRegistrationWindow
{
    private static readonly PropertyInfo? ProfilesLoaded = typeof(CustomItemService)
        .GetProperty("ProfilesLoaded", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>True while items may still be added to the database.</summary>
    public static bool IsOpen(CustomItemService customItemService) =>
        ProfilesLoaded?.GetValue(customItemService) as bool? != true;
}
