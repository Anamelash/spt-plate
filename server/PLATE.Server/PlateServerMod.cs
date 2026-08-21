using System.Reflection;
using PLATE.Server.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;

namespace PLATE.Server;

/// <summary>
/// PLATE.Server entry point. PostLoad is the last stage the server runs and a higher
/// priority runs later, so PostLoad + 9000 puts us after content mods have finished
/// adding items to the DB — the normalizer must see everything. The one thing that
/// cannot wait this long — registering our own item templates before the profiles
/// are loaded and validated — lives in <see cref="PlateItemRegistration"/>.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 9000)]
public class PlateServerMod(
    ModHelper modHelper,
    PlateConfigLoader configLoader,
    Services.AmmoNormalizer ammoNormalizer,
    Services.BarrelNormalizer barrelNormalizer,
    Services.ArmorNormalizer armorNormalizer,
    Services.GrenadePhysics grenadePhysics,
    Services.BloodGlobals bloodGlobals,
    Services.TransfusionItem transfusionItem,
    ISptLogger<PlateServerMod> logger) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config = await configLoader.LoadAsync(modPath, cancellationToken);

        if (config.Modules.AmmoNormalizer)
        {
            ammoNormalizer.Run(config, modPath); // ammo normalization (incl. mod-added rounds)
        }

        if (config.Modules.BarrelNormalizer)
        {
            barrelNormalizer.Run(config, modPath); // muzzle velocity from barrel length
        }

        if (config.Modules.ArmorNormalizer)
        {
            armorNormalizer.Run(config, modPath); // armour construction from real products
        }

        if (config.Modules.GrenadePhysics)
        {
            // fragments/blast from prototype specs; the fragment templates themselves were
            // registered by PlateItemRegistration — nothing may be added to the item
            // database this late (see Services/ItemRegistrationWindow.cs)
            grenadePhysics.Apply(config, modPath, canAddItems: false);
        }

        if (config.Modules.BloodGlobals)
        {
            bloodGlobals.Apply(config); // globals tweaks for the blood system

            if (config.Blood.TransfusionItem)
            {
                // registered by PlateItemRegistration before profile validation;
                // this pass only reports it into the summary below
                transfusionItem.Apply(config, modPath, canAddItems: false);
            }
        }

        // One line on success; anything that went wrong has already logged itself as a
        // warning or an error with the full detail. Per-module specifics are Debug.
        var applied = new[]
            {
                ammoNormalizer.Summary,
                barrelNormalizer.Summary,
                armorNormalizer.Summary,
                grenadePhysics.Summary,
                bloodGlobals.Summary,
                transfusionItem.Summary,
            }
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        var version = new PlateModMetadata().Version;
        logger.Success(applied.Count > 0
            ? $"[PLATE] {version} loaded: {string.Join(", ", applied)}"
            : $"[PLATE] {version} loaded: all modules disabled in config.jsonc");
    }
}
