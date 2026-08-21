using System.Reflection;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace PLATE.Server.Services;

/// <summary>
/// Whether the server still accepts new item templates.
///
/// Since 4.1.3 the item database has a cutoff. At the start of SaveCallbacks the server
/// sets <c>CustomItemService.ProfilesLoaded</c> and snapshots the keys of the item table
/// (<c>DatabaseIntegrityService.TakeItemSnapshot</c>); once every IOnLoad has run, a key
/// that was not in the snapshot kills the process — an item registered after the profiles
/// were read is an item those profiles cannot resolve. Adding items late used to merely
/// corrupt profiles quietly, which is how PLATE's blood bag ended up in
/// <see cref="PlateItemRegistration"/> in the first place; 4.1.3 made the same mistake
/// fatal and extended it to writes that bypass CustomItemService.
///
/// PLATE registers at <c>SaveCallbacks - 1000</c>: the last moment before the cutoff, so
/// that the grenade pass still sees grenades added by other content mods. That position
/// is a compile-time constant baked into our attribute, so the day SPT moves the line,
/// "just before it" quietly stops being just before it. This asks the server where the
/// line is instead of assuming: a pass that arrives too late skips its items and says so
/// in the log, rather than taking the whole server down with it.
///
/// The property does not exist before 4.1.3 and PLATE is built against 4.1.1 to keep one
/// binary for all of 4.1.x — hence reflection. No property means no cutoff, which is
/// exactly what those versions did.
/// </summary>
internal static class ItemRegistrationWindow
{
    private static readonly PropertyInfo? ProfilesLoaded = typeof(CustomItemService)
        .GetProperty("ProfilesLoaded", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>True while items may still be added to the database.</summary>
    public static bool IsOpen(CustomItemService customItemService) =>
        ProfilesLoaded?.GetValue(customItemService) as bool? != true;
}
