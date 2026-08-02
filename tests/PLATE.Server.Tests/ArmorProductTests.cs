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
}
