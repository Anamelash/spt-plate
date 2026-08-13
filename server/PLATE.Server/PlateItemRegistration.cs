using System.Reflection;
using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;

namespace PLATE.Server;

/// <summary>
/// The early half of the entry point: registers PLATE's custom items before the
/// server loads profiles. Since SPT 4.1.2 every profile is validated at startup
/// (SaveCallbacks, priority 600000: SaveServer.LoadAsync →
/// ProfileValidatorHelper.CheckForOrphanedModdedData), and an inventory item whose
/// template is missing from the DB marks the whole profile invalid — a blood bag
/// stored in the stash bricked the profile while the item was registered at
/// PostLoad. Everything else stays in PlateServerMod at PostLoad + 9000: the
/// normalizers must run after all content mods, and nothing they touch is checked
/// against profiles.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.SaveCallbacks - 1000)]
public class PlateItemRegistration(
    ModHelper modHelper,
    PlateConfigLoader configLoader,
    Services.TransfusionItem transfusionItem) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config = await configLoader.LoadAsync(modPath, cancellationToken);

        if (config.Modules.BloodGlobals && config.Blood.TransfusionItem)
        {
            transfusionItem.Apply(config, modPath);
        }
    }
}
