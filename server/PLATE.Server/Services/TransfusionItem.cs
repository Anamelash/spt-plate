using PLATE.Server.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;

namespace PLATE.Server.Services;

/// <summary>
/// Blood bag (transfusion) item — fast restoration of blood volume in raid.
/// Clone of the Emergency Water Ration (drinking-from-a-pouch animation, usable at
/// any time, multi-use via MaxResource); the effect is applied by the client module
/// matched by TemplateId. Sold by Therapist LL1, craftable at the medstation.
/// </summary>
[Injectable]
public class TransfusionItem(
    CustomItemService customItemService,
    DatabaseServer databaseServer,
    ISptLogger<TransfusionItem> logger)
{
    /// <summary>Stable item tpl (b100d = "blood"). Must not change — the client matches by it.</summary>
    public const string Tpl = "b100d0000000000000000001";

    private const string AssortId = "b100d0000000000000000002";
    private const string TherapistId = "54cb57776803fa99248b456e";
    private const string RoublesTpl = "5449016a4bdc2d6f028b456f";

    // --- Medstation craft: 1 saline + 1 bloodset -> 1 blood bag, 60 s ---
    private const string CraftId = "b100d0000000000000000003";
    private const string SalineTpl = "59e3606886f77417674759a5";   // Bottle of saline solution
    private const string BloodsetTpl = "5b4335ba86f7744d2837a264"; // Medical bloodset
    private const SPTarkov.Server.Core.Models.Enums.Hideout.HideoutAreas MedstationArea =
        SPTarkov.Server.Core.Models.Enums.Hideout.HideoutAreas.MedStation;
    private const double CraftTimeSeconds = 60;

    /// <summary>Custom bundle key (= manifest key in bundles.json and the item's Prefab.path).</summary>
    private const string CustomBundleKey = "plate/blood_bag.bundle";

    /// <summary>Vanilla bloodset — the fallback model until the custom bundle is built.</summary>
    private const string BloodsetPrefabPath =
        "assets/content/items/barter/item_barter_medical_bloodset/" +
        "item_barter_medical_bloodset.bundle";

    // Clone donor: Emergency Water Ration (a foil pouch with a straw).
    // Why this one: (1) the drinking-from-a-pouch animation is the best stand-in for
    // a transfusion; (2) the food/drink class is usable ALWAYS (no "missing HP
    // required" gate that broke the MedKit class); (3) vanilla tracks resource
    // consumption by itself, both on the client and in the profile. The use
    // animation is a matched PAIR of "class + UsePrefab container" that must not be
    // mixed (Drugs+Salewa hung MedsController — "the hands disappeared"), so
    // UsePrefab/sound are inherited from the ration wholesale.
    private const string WaterRationTpl = "60098b1705871270cd5352a1";

    public void Apply(PlateServerConfig cfg, string modPath)
    {
        var tables = databaseServer.GetTables();
        var items = tables.Templates?.Items;
        if (items == null)
        {
            return;
        }

        if (items.ContainsKey(new MongoId(Tpl)))
        {
            return; // idempotency in case of a repeated call
        }

        // base — Emergency Water Ration (see the comment at WaterRationTpl)
        if (!items.TryGetValue(new MongoId(WaterRationTpl), out var source))
        {
            logger.Error("[PLATE] TransfusionItem: water ration template not found, item skipped");
            return;
        }

        var sourceHandbook = tables.Templates?.Handbook?.Items?
            .FirstOrDefault(h => h.Id == source.Id);

        // model: the built Unity bundle (blood bag model) if deployed next to the
        // mod; otherwise the vanilla bloodset. The icon is rendered from the same prefab.
        var customBundle = System.IO.Path.Combine(modPath, "bundles", "plate", "blood_bag.bundle");
        var hasCustomModel = File.Exists(customBundle);
        var prefabPath = hasCustomModel ? CustomBundleKey : BloodsetPrefabPath;

        var result = customItemService.CreateItemFromClone(new NewItemFromCloneDetails
        {
            ItemTplToClone = source.Id,
            NewId = Tpl,
            ParentId = source.Parent, // Drink — the ration's native class, its drinking animation
            HandbookParentId = sourceHandbook?.ParentId.ToString(),
            HandbookPriceRoubles = cfg.Blood.TransfusionPriceRub,
            FleaPriceRoubles = cfg.Blood.TransfusionPriceRub,
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new()
                {
                    Name = "Blood transfusion kit",
                    ShortName = "Blood",
                    Description = "Packed red blood cells with a transfusion set. " +
                                  "Restores circulating blood volume.",
                },
                ["ru"] = new()
                {
                    Name = "Пакет крови (гемотрансфузия)",
                    ShortName = "Кровь",
                    Description = "Контейнер с эритроцитарной массой и системой для переливания. " +
                                  "Восстанавливает объём циркулирующей крови.",
                },
                ["ge"] = new()
                {
                    Name = "Bluttransfusionsset",
                    ShortName = "Blut",
                    Description = "Erythrozytenkonzentrat mit Transfusionsbesteck. " +
                                  "Stellt das zirkulierende Blutvolumen wieder her.",
                },
                ["fr"] = new()
                {
                    Name = "Kit de transfusion sanguine",
                    ShortName = "Sang",
                    Description = "Concentré de globules rouges avec nécessaire à transfusion. " +
                                  "Restaure le volume sanguin circulant.",
                },
                ["es"] = new()
                {
                    Name = "Kit de transfusión de sangre",
                    ShortName = "Sangre",
                    Description = "Concentrado de glóbulos rojos con equipo de transfusión. " +
                                  "Restaura el volumen de sangre circulante.",
                },
                ["es-mx"] = new()
                {
                    Name = "Kit de transfusión de sangre",
                    ShortName = "Sangre",
                    Description = "Concentrado de glóbulos rojos con equipo de transfusión. " +
                                  "Restaura el volumen de sangre circulante.",
                },
                ["pl"] = new()
                {
                    Name = "Zestaw do transfuzji krwi",
                    ShortName = "Krew",
                    Description = "Koncentrat krwinek czerwonych z zestawem do transfuzji. " +
                                  "Przywraca objętość krwi krążącej.",
                },
                ["cz"] = new()
                {
                    Name = "Transfuzní sada s krví",
                    ShortName = "Krev",
                    Description = "Erytrocytární koncentrát s transfuzní soupravou. " +
                                  "Obnovuje objem cirkulující krve.",
                },
                ["tu"] = new()
                {
                    Name = "Kan transfüzyon kiti",
                    ShortName = "Kan",
                    Description = "Transfüzyon setiyle birlikte eritrosit süspansiyonu. " +
                                  "Dolaşımdaki kan hacmini geri kazandırır.",
                },
                ["ch"] = new()
                {
                    Name = "输血套装",
                    ShortName = "血液",
                    Description = "红细胞悬液及输血器。恢复循环血量。",
                },
                ["jp"] = new()
                {
                    Name = "輸血キット",
                    ShortName = "血液",
                    Description = "赤血球製剤と輸血セット。循環血液量を回復する。",
                },
                ["kr"] = new()
                {
                    Name = "수혈 키트",
                    ShortName = "혈액",
                    Description = "적혈구 농축액과 수혈 세트. 순환 혈액량을 회복시킨다.",
                },
            },
            OverrideProperties = new TemplateItemProperties
            {
                // food/drink: resource = uses, vanilla deducts it by itself
                MaxResource = cfg.Blood.TransfusionUses,
                // inventory/world — our blood bag model; in hands — the ration's container
                // (UsePrefab and sound are inherited from the donor and must stay — animation)
                Prefab = new Prefab { Path = prefabPath, Rcid = "" },
            },
        });

        if (result?.Success != true)
        {
            logger.Error($"[PLATE] TransfusionItem: clone failed: {result?.Errors}");
            return;
        }

        AddTherapistAssort(tables, cfg);
        AddMedstationCraft(tables);

        logger.Success($"[PLATE] Transfusion item registered ({cfg.Blood.TransfusionUses} uses, " +
                       $"{cfg.Blood.TransfusionPriceRub:0} rub at Therapist LL1, " +
                       $"model={(hasCustomModel ? "custom bundle" : "vanilla bloodset")})");
    }

    /// <summary>Medstation recipe: 1 saline + 1 bloodset -> 1 blood bag (60 s).</summary>
    private void AddMedstationCraft(SPTarkov.Server.Core.Models.Spt.Server.DatabaseTables tables)
    {
        var recipes = tables.Hideout?.Production?.Recipes;
        if (recipes == null)
        {
            logger.Warning("[PLATE] TransfusionItem: hideout recipes unavailable, craft skipped");
            return;
        }

        var craftId = new MongoId(CraftId);
        if (recipes.Any(r => r.Id == craftId))
        {
            return; // idempotency
        }

        recipes.Add(new SPTarkov.Server.Core.Models.Eft.Hideout.HideoutProduction
        {
            Id = craftId,
            AreaType = MedstationArea,
            ProductionTime = CraftTimeSeconds,
            EndProduct = new MongoId(Tpl),
            Count = 1,
            Locked = false,
            Continuous = false,
            NeedFuelForAllProductionTime = false,
            IsEncoded = false,
            ProductionLimitCount = 0,
            Requirements =
            [
                new SPTarkov.Server.Core.Models.Eft.Hideout.Requirement
                {
                    AreaType = (int)MedstationArea, // Requirement takes int?, not the enum
                    RequiredLevel = 1,
                    Type = "Area",
                },
                new SPTarkov.Server.Core.Models.Eft.Hideout.Requirement
                {
                    TemplateId = new MongoId(SalineTpl),
                    Count = 1,
                    IsFunctional = false,
                    IsEncoded = false,
                    Type = "Item",
                },
                new SPTarkov.Server.Core.Models.Eft.Hideout.Requirement
                {
                    TemplateId = new MongoId(BloodsetTpl),
                    Count = 1,
                    IsFunctional = false,
                    IsEncoded = false,
                    Type = "Item",
                },
            ],
        });

        logger.Success("[PLATE] Medstation craft added: saline + bloodset -> blood bag (60s)");
    }

    private void AddTherapistAssort(
        SPTarkov.Server.Core.Models.Spt.Server.DatabaseTables tables, PlateServerConfig cfg)
    {
        if (tables.Traders == null ||
            !tables.Traders.TryGetValue(new MongoId(TherapistId), out var therapist) ||
            therapist.Assort == null)
        {
            logger.Warning("[PLATE] TransfusionItem: Therapist assort unavailable, item not sold");
            return;
        }

        therapist.Assort.Items?.Add(new Item
        {
            Id = new MongoId(AssortId),
            Template = new MongoId(Tpl),
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd { StackObjectsCount = 999999, UnlimitedCount = true },
        });
        therapist.Assort.BarterScheme![new MongoId(AssortId)] =
        [
            [new BarterScheme { Count = cfg.Blood.TransfusionPriceRub, Template = new MongoId(RoublesTpl) }],
        ];
        therapist.Assort.LoyalLevelItems![new MongoId(AssortId)] = 1;
    }
}
