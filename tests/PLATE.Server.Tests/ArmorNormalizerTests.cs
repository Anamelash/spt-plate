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
/// static helpers cannot see. The class ceiling is a property of the material, and
/// the reference book corrects materials the game got wrong — so the correction has
/// to land before the ceiling reads it. It once did not, for documented products
/// with a thickness: every aramid shell the game files under Combined kept its
/// vanilla class 4 while the same shell filed under Aramid was taken to 3.
/// ClassCeiling tests stayed green the whole time, which is why these run Run().
/// </summary>
public class ArmorNormalizerTests
{
    [Theory]
    // Ops-Core FAST MT: the game says Combined class 4, Gentex says aramid — and
    // an aramid shell holds 3. The book's correction must reach the ceiling.
    [InlineData("ops_core_fastMT_level4_helmet_armor_top", ArmorMaterial.Combined, 4,
        ArmorMaterial.Aramid, 3)]
    // the LShZ-2DTM aventail is aramid the game files under Combined at class 5;
    // it hangs off a helmet, so it caps as a shell does
    [InlineData("item_equipment_helmet_lshz2dtm_aventail", ArmorMaterial.Combined, 5,
        ArmorMaterial.Aramid, 3)]
    // a documented shell the game already calls by its fibre keeps being taken down
    [InlineData("ulach_level4_helmet_armor_top", ArmorMaterial.UHMWPE, 4,
        ArmorMaterial.UHMWPE, 3)]
    // the Vulkan-5 really is a ceramic screen on a composite shell: Combined is
    // correct, Combined is not capped, and the class stays where the game put it
    [InlineData("lshz5_vulkan5_level5_helmet_armor_top", ArmorMaterial.Combined, 5,
        ArmorMaterial.Combined, 5)]
    public void The_ceiling_reads_the_corrected_material(string itemName,
        ArmorMaterial gameMaterial, int gameClass,
        ArmorMaterial expectedMaterial, int expectedClass)
    {
        var (normalizer, item) = Fixture(itemName, gameMaterial, gameClass);

        normalizer.Run(new PlateServerConfig(), ModPath());

        Assert.Equal(expectedMaterial, item.Properties!.ArmorMaterial);
        Assert.Equal(expectedClass, (int)(item.Properties!.ArmorClass ?? 0));
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

        Assert.Equal(6.43, normalizer.ThicknessByTemplate[item.Id], 3);
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
