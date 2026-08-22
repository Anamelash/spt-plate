using System.Reflection;
using PLATE.Server.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services.Mod;

namespace PLATE.Server;

/// <summary>
/// The registering half of the entry point: PLATE's own item templates — the blood bag
/// and one fragment per grenade — are created here, one step ahead of the numbers pass
/// in <see cref="PlateServerMod"/>. Both run late enough (PostDBModLoader + 8990 and
/// + 9000) to see the content of every other mod.
///
/// The split exists because later SPT releases close the item database partway through
/// loading: from 4.1.2 an item registered after the profiles are validated marks every
/// profile carrying it invalid, and 4.1.3 refuses to start at all. 4.0.13 has no such
/// cutoff — nothing here is load-bearing on this server — but the two halves are kept
/// in step with the 4.1 line so the code stays one shape,
/// and <see cref="Services.ItemRegistrationWindow"/> keeps asking the server whether
/// the door is still open rather than assuming it.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 8990)]
public class PlateItemRegistration(
    ModHelper modHelper,
    PlateConfigLoader configLoader,
    CustomItemService customItemService,
    Services.TransfusionItem transfusionItem,
    Services.GrenadePhysics grenadePhysics,
    ISptLogger<PlateItemRegistration> logger) : IOnLoad
{
    public Task OnLoad()
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config = configLoader.Load(modPath);

        var wantsBloodBag = config.Modules.BloodGlobals && config.Blood.TransfusionItem;
        if (!wantsBloodBag && !config.Modules.GrenadePhysics)
        {
            return Task.CompletedTask;
        }

        if (!Services.ItemRegistrationWindow.IsOpen(customItemService))
        {
            // Only reachable if a server release moves the cutoff ahead of this priority.
            // Losing the blood bag and the grenade fragments is bad; taking the server
            // down with them is worse, so PLATE says what happened and carries on.
            logger.Error("[PLATE] the server closed the item database before PLATE could register " +
                         "its items: the blood bag and the grenade fragments are missing this run. " +
                         "The mod needs an update for this server version");
            return Task.CompletedTask;
        }

        if (wantsBloodBag)
        {
            transfusionItem.Apply(config, modPath, canAddItems: true);
        }

        if (config.Modules.GrenadePhysics)
        {
            grenadePhysics.Apply(config, modPath, canAddItems: true);
        }

        return Task.CompletedTask;
    }
}
