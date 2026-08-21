using System.Reflection;
using PLATE.Server.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace PLATE.Server;

/// <summary>
/// The early half of the entry point: registers PLATE's own item templates — the blood
/// bag and one fragment per grenade — while the server still accepts them. Everything
/// else stays in <see cref="PlateServerMod"/> at PostLoad + 9000, where the normalizers
/// can see the content of every other mod.
///
/// The database closes at SaveCallbacks (see <see cref="Services.ItemRegistrationWindow"/>):
/// profiles are validated against the item table, so since 4.1.2 an item registered
/// afterwards marks every profile that carries one invalid, and since 4.1.3 the server
/// refuses to start at all. SaveCallbacks - 1000 is the last position before that line,
/// which is what makes the grenade pass here rather than at Preload: content mods add
/// their grenades all over the earlier priorities, and this way it sees all of them.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.SaveCallbacks - 1000)]
public class PlateItemRegistration(
    ModHelper modHelper,
    PlateConfigLoader configLoader,
    CustomItemService customItemService,
    Services.TransfusionItem transfusionItem,
    Services.GrenadePhysics grenadePhysics,
    ISptLogger<PlateItemRegistration> logger) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config = await configLoader.LoadAsync(modPath, cancellationToken);

        var wantsBloodBag = config.Modules.BloodGlobals && config.Blood.TransfusionItem;
        if (!wantsBloodBag && !config.Modules.GrenadePhysics)
        {
            return;
        }

        if (!Services.ItemRegistrationWindow.IsOpen(customItemService))
        {
            // Only reachable if a server release moves the cutoff ahead of this priority.
            // Losing the blood bag and the grenade fragments is bad; taking the server
            // down with them is worse, so PLATE says what happened and carries on.
            logger.Error("[PLATE] the server closed the item database before PLATE could register " +
                         "its items: the blood bag and the grenade fragments are missing this run. " +
                         "The mod needs an update for this server version");
            return;
        }

        if (wantsBloodBag)
        {
            transfusionItem.Apply(config, modPath, canAddItems: true);
        }

        if (config.Modules.GrenadePhysics)
        {
            grenadePhysics.Apply(config, modPath, canAddItems: true);
        }
    }
}
