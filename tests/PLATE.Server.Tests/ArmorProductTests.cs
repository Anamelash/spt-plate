using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// Resolving an item to the product it belongs to. Pure string work, and the first
/// version of it matched nothing at all: the pattern ended the class marker with a
/// word boundary, but what follows a marker is an underscore, which is a word
/// character, so the boundary never existed and the name was never cut. Every product
/// then looked unknown and the reference book appeared to cover zero of them.
/// </summary>
public class ArmorProductTests
{
    [Theory]
    // vests and helmets: "_levelN"
    [InlineData("6b5-16_level3_soft_armor_front", "6b5-16")]
    [InlineData("6b5-15_level4_soft_armor_groin", "6b5-15")]
    [InlineData("6b3TM_level4_soft_armor_front", "6b3TM")]
    [InlineData("kora_kulon_level3_soft_armor_back", "kora_kulon")]
    [InlineData("altin_level5_helmet_armor_ears", "altin")]
    [InlineData("6b13_killa_level3_soft_armor_collar", "6b13_killa")]
    // plates: the prefix goes, then "_Nclass"
    [InlineData("item_equipment_plate_granit4_5class_back", "granit4")]
    [InlineData("item_equipment_plate_korund_vm_k_6class_back", "korund_vm_k")]
    // some of them spell it with a space
    [InlineData("item_equipment_plate_granitBr5_6 class_frontback", "granitBr5")]
    public void An_item_resolves_to_its_product(string item, string expected)
    {
        Assert.Equal(expected, ArmorNormalizer.Product(item));
    }

    /// <summary>Every zone of a vest is the same plate and must land on one entry.</summary>
    [Fact]
    public void All_zones_of_a_product_collapse_to_one_key()
    {
        string[] zones =
        [
            "6b43_6a_level3_soft_armor_front",
            "6b43_6a_level3_soft_armor_back",
            "6b43_6a_level3_soft_armor_left_side",
            "6b43_6a_level3_soft_armor_groin_front",
            "6b43_6a_level3_soft_armor_collar",
        ];

        Assert.Single(zones.Select(ArmorNormalizer.Product).Distinct());
    }

    /// <summary>A name with no marker is its own product rather than an empty key.</summary>
    [Fact]
    public void A_name_without_a_marker_survives_intact()
    {
        Assert.Equal("some_modded_vest", ArmorNormalizer.Product("some_modded_vest"));
    }

    [Theory]
    [InlineData("item_equipment_plate_granit4_5class_back", ArmorNormalizer.Kind.Plate)]
    [InlineData("ratnik_6b47_level3_helmet_armor_top", ArmorNormalizer.Kind.Helmet)]
    [InlineData("helmet_ops_core_fast_visor", ArmorNormalizer.Kind.Helmet)]
    [InlineData("item_equipment_helmet_vulkan_shield", ArmorNormalizer.Kind.Helmet)]
    [InlineData("6b43_6a_level3_soft_armor_front", ArmorNormalizer.Kind.VestComponent)]
    [InlineData("item_equipment_facecover_ballistic_mask", ArmorNormalizer.Kind.Other)]
    [InlineData("item_equipment_glasses_npp", ArmorNormalizer.Kind.Other)]
    [InlineData("balaclava", ArmorNormalizer.Kind.Other)]
    public void An_item_lands_in_the_right_section(string item, ArmorNormalizer.Kind expected)
    {
        Assert.Equal(expected, ArmorNormalizer.Classify(item));
    }

    /// <summary>
    /// UNTAR is a helmet and a vest under one name, differing only in case. Whichever
    /// way the classifier reads the name, the zone suffix has to decide.
    /// </summary>
    [Fact]
    public void One_name_shared_by_a_helmet_and_a_vest_still_splits()
    {
        Assert.Equal(ArmorNormalizer.Kind.Helmet,
            ArmorNormalizer.Classify("Untar_level3_helmet_armor_top"));
        Assert.Equal(ArmorNormalizer.Kind.VestComponent,
            ArmorNormalizer.Classify("untar_level3_soft_armor_front"));
    }

    /// <summary>
    /// A product name is not always one plate. "granit4" covers a class 5 ceramic front,
    /// a class 4 steel one and a class 3 polyethylene insert, so the item's own name has
    /// to be able to overrule the product it belongs to.
    /// </summary>
    [Fact]
    public void The_items_own_name_beats_its_product()
    {
        var reference = new ReferenceBook.AmmoReference
        {
            ArmorPlates =
            {
                ["granit4"] = new ReferenceBook.ArmorPlateRef { Prototype = "the whole family" },
                ["granit4_5class_front"] = new ReferenceBook.ArmorPlateRef { Prototype = "this one plate" },
            },
        };

        const string front = "item_equipment_plate_granit4_5class_front";
        var spec = ArmorNormalizer.ProductSpec(reference, front, ArmorNormalizer.Product(front), out var key);

        Assert.Equal("this one plate", spec?.Prototype);
        Assert.Equal("granit4_5class_front", key);
    }

    /// <summary>
    /// A class is what a construction earns. The game hands out ratings its materials
    /// cannot reach — 125 of the aramid packages sewn into vests are stamped class 3,
    /// which would take around 200 mm of aramid — and the ceiling is what takes them
    /// back to what the fabric does.
    /// </summary>
    [Theory]
    // the sewn package: fabric stops at 2 whatever the carrier is sold as
    [InlineData("thorcrv_level3_soft_armor_front", "Aramid", 2)]
    [InlineData("iotv_gen4_f_level3_soft_armor_front", "Aramid", 2)]
    [InlineData("defender2_level3_soft_armor_back", "Aramid", 2)]
    [InlineData("crye_avs_level3_soft_armor_front", "Aramid", 2)]
    // a pressed shell is one rung better than the fabric it is made of, and no more
    [InlineData("ratnik_6b47_level3_helmet_armor_top", "Aramid", 3)]
    [InlineData("item_equipment_facecover_ballistic_mask", "UHMWPE", 3)]
    // a visor is polycarbonate whatever it is bolted to
    [InlineData("item_equipment_helmet_vulkan_shield", "Glass", 2)]
    // metal and ceramic are not capped: a heavier helmet really is a thicker shell
    [InlineData("kora_kulon_level3_soft_armor_back", "ArmoredSteel", int.MaxValue)]
    [InlineData("6b5-15_level4_soft_armor_front", "Ceramic", int.MaxValue)]
    // and neither is a plate: the rifle protection lives there, and a 23 mm pressed
    // polyethylene Zhuk really is certified Br3
    [InlineData("item_equipment_plate_granit4_zhukBr3_3class_front", "UHMWPE", int.MaxValue)]
    [InlineData("item_equipment_plate_granit4_5class_back", "Ceramic", int.MaxValue)]
    public void A_material_can_only_hold_so_much(string item, string material, int expected)
    {
        Assert.Equal(expected, ArmorNormalizer.ClassCeiling(item, material));
    }

    /// <summary>
    /// A balaclava is fabric the game rates armour, and one of them ships at class 10.
    /// Whatever number the game prints, the ceiling is a property of the material.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(6)]
    [InlineData(3)]
    public void The_ceiling_does_not_care_what_the_item_claims(int declared)
    {
        var ceiling = ArmorNormalizer.ClassCeiling("balaclava", "UHMWPE");

        Assert.Equal(2, ceiling);
        Assert.True(declared > ceiling);
    }

    /// <summary>And with no entry of its own it still falls back to the product.</summary>
    [Fact]
    public void Without_one_it_falls_back_to_the_product()
    {
        var reference = new ReferenceBook.AmmoReference
        {
            ArmorPlates = { ["granit4"] = new ReferenceBook.ArmorPlateRef { Prototype = "the whole family" } },
        };

        const string back = "item_equipment_plate_granit4_5class_back";
        var spec = ArmorNormalizer.ProductSpec(reference, back, ArmorNormalizer.Product(back), out var key);

        Assert.Equal("the whole family", spec?.Prototype);
        Assert.Equal("granit4", key);
    }
}
