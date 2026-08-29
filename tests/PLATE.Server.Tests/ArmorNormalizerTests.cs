using System.Runtime.CompilerServices;
using PLATE.Server.Config;
using PLATE.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The normalizer's pass over a live database, where order matters in a way the
/// static helpers cannot see. Since the Br realignment the game class IS the GOST
/// class, and the label is derived rather than trusted: a published certificate
/// (Rating) outranks everything including downward to 0, a documented construction
/// earns its class against the standard's own rounds, and everything else carries
/// the game's label shifted onto the Br scale under the form's ceiling. These run
/// Run() itself because the static ClassCeiling tests stayed green through a real
/// ordering bug once already.
/// </summary>
public class ArmorNormalizerTests
{
    [Theory]
    // the passport outranks the game's label: the Maska-1Sch is old GOST class 2 = Br2
    [InlineData("maska1sha_level4_helmet_armor_top", ArmorMaterial.ArmoredSteel, 4,
        ArmorMaterial.ArmoredSteel, 2)]
    // ...and it outranks the material correction path too — the aventail the game
    // files under Combined at 5 is certified with its Br2 family
    [InlineData("item_equipment_helmet_lshz2dtm_aventail", ArmorMaterial.Combined, 5,
        ArmorMaterial.Aramid, 2)]
    // ...and it can hold a class the ceiling never could: the Vulkan-5 really is Br4
    [InlineData("lshz5_vulkan5_level5_helmet_armor_top", ArmorMaterial.Combined, 5,
        ArmorMaterial.Combined, 4)]
    // ...and it outranks the model downward to zero: the SSh-68 passport says
    // fragments, not bullets, whatever the model thinks 1.8 mm of steel holds
    [InlineData("ssh68_level3_helmet_armor_top", ArmorMaterial.ArmoredSteel, 3,
        ArmorMaterial.ArmoredSteel, 0)]
    // no product, no construction: the game's label shifts onto the Br scale — the
    // airsoft FAST replica the game calls class 1 is the anti-fragment tier, class 0
    [InlineData("tac_kek_fast_mt_level1_helmet_armor_top", ArmorMaterial.UHMWPE, 1,
        ArmorMaterial.UHMWPE, 0)]
    // the shifted label still lands under the form's ceiling: a sewn aramid package
    // the game stamps 3 is Br1 at most, whatever 3 − 1 says
    [InlineData("thorcrv_level3_soft_armor_front", ArmorMaterial.Aramid, 3,
        ArmorMaterial.Aramid, 1)]
    // ...and only a passport lifts past the shift: the Zhuk-3 is certified Br3, its
    // vanilla label read Br2 after the shift, and without the book's Rating the
    // downward-only rule had no way back up
    [InlineData("item_equipment_plate_granit4_zhukBr3_3class_front", ArmorMaterial.UHMWPE, 3,
        ArmorMaterial.UHMWPE, 3)]
    public void The_class_is_derived_rather_than_trusted(string itemName,
        ArmorMaterial gameMaterial, int gameClass,
        ArmorMaterial expectedMaterial, int expectedClass)
    {
        var (normalizer, item) = Fixture(itemName, gameMaterial, gameClass);

        normalizer.Run(new PlateServerConfig(), ModPath());

        Assert.Equal(expectedMaterial, item.Properties!.ArmorMaterial);
        Assert.Equal(expectedClass, (int)(item.Properties!.ArmorClass ?? 0));
    }

    /// <summary>
    /// A documented construction with no certificate earns its class from the
    /// standard's own rounds: the Korund-VM is 6.3 mm of 44S with a published alloy
    /// grade, and it holds every Бр4 cartridge at test velocity while the Бр5 pair
    /// beats it — which is exactly what its (deliberately unstated here) certificate
    /// says it does.
    /// </summary>
    [Fact]
    public void A_documented_construction_earns_its_class()
    {
        var (normalizer, item) = Fixture(
            "korund_level5_soft_armor_front", ArmorMaterial.ArmoredSteel, 5);

        normalizer.Run(new PlateServerConfig(), ModPath());

        Assert.Equal(4, (int)(item.Properties!.ArmorClass ?? 0));
    }

    /// <summary>
    /// And it earns only DOWNWARD: a lift is a certificate's to make, never the
    /// model's. The 6B2's titanium panels are 1.25 mm the model reads far too
    /// optimistically — left symmetric, the engine handed them a rifle class the real
    /// vest never had.
    /// </summary>
    [Fact]
    public void The_model_never_lifts_a_class_on_its_own()
    {
        var (normalizer, item) = Fixture(
            "6b2_level2_soft_armor_front", ArmorMaterial.Titan, 2);

        normalizer.Run(new PlateServerConfig(), ModPath());

        Assert.True((int)(item.Properties!.ArmorClass ?? 0) <= 1,
            "a vanilla class-2 panel may keep Br1 or fall, but the model must not lift it");
    }

    /// <summary>
    /// The re-rating must not cost the item its construction: the documented
    /// thickness still comes through for the ballistic limit.
    /// </summary>
    [Fact]
    public void A_re_rated_helmet_keeps_its_documented_thickness()
    {
        var (normalizer, item) = Fixture(
            "ops_core_fastMT_level4_helmet_armor_top", ArmorMaterial.Combined, 4);

        normalizer.Run(new PlateServerConfig(), ModPath());

        Assert.Equal(ArmorMaterial.Aramid, item.Properties!.ArmorMaterial);
        Assert.Equal(6.43, normalizer.ThicknessByTemplate[item.Id], 3);
    }

    /// <summary>
    /// Fence's plate-existence table ships rungs 3..6 and FenceService indexes it by
    /// the plate's class directly — a Бр2 plate crashed the trader refresh. The
    /// extension gives every class 0..6 a rung, each missing one reading the nearest
    /// shipped rung's value, and touches nothing that exists.
    /// </summary>
    [Fact]
    public void The_fence_table_gains_a_rung_for_every_class_the_scale_knows()
    {
        var chances = new Dictionary<string, double>
        {
            ["3"] = 100, ["4"] = 87, ["5"] = 60, ["6"] = 15,
        };

        var added = ArmorNormalizer.ExtendFencePlateChances(chances);

        Assert.Equal(["0", "1", "2"], added);
        Assert.Equal(100, chances["0"]);
        Assert.Equal(100, chances["1"]);
        Assert.Equal(100, chances["2"]);
        Assert.Equal(87, chances["4"]);
        Assert.Equal(7, chances.Count);

        // and an empty table stays empty: there is no edge to extend
        var empty = new Dictionary<string, double>();
        Assert.Empty(ArmorNormalizer.ExtendFencePlateChances(empty));
        Assert.Empty(empty);
    }

    /// <summary>
    /// Running the pass twice must answer the same as running it once: the fallback
    /// shifts the game's label, and an unpinned second pass would shift the shifted.
    /// </summary>
    [Fact]
    public void A_second_pass_changes_nothing()
    {
        var (normalizer, item) = Fixture(
            "tac_kek_fast_mt_level1_helmet_armor_top", ArmorMaterial.UHMWPE, 1);

        normalizer.Run(new PlateServerConfig(), ModPath());
        var after = (int)(item.Properties!.ArmorClass ?? 0);
        normalizer.Run(new PlateServerConfig(), ModPath());

        Assert.Equal(after, (int)(item.Properties!.ArmorClass ?? 0));
    }

    private static readonly MongoId ItemId = new("6f0000000000000000000001");

    /// <summary>A database with a single armour zone item wearing the game's numbers.</summary>
    private static (ArmorNormalizer Normalizer, TemplateItem Item) Fixture(
        string itemName, ArmorMaterial material, int cls)
    {
        var item = new TemplateItem
        {
            Id = ItemId,
            Name = itemName,
            Properties = new TemplateItemProperties
            {
                ArmorMaterial = material,
                ArmorClass = cls,
            },
        };

        var items = new Dictionary<MongoId, TemplateItem> { [ItemId] = item };
        var templateTable = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templateTable, items);

        var normalizer = new ArmorNormalizer(
            templateTable,
            new ReferenceBook(new TestLogger<ReferenceBook>()),
            new TestLogger<ArmorNormalizer>());

        return (normalizer, item);
    }

    /// <summary>Somewhere for the shipped reference book to be written to and read back.</summary>
    private static string ModPath()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plate-tests", "armor-normalizer");
        Directory.CreateDirectory(path);
        return path;
    }
}
