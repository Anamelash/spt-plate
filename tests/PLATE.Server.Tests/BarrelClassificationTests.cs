using System.Runtime.CompilerServices;
using PLATE.Server.Config;
using PLATE.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// Telling a barrel from a muzzle device.
///
/// The pass used to do it by the naming convention alone, which holds for vanilla items
/// and for nothing a weapon pack registers: WTT's CustomItemService names a clone
/// "[Pack]_(whatever the locale calls it)", so an 8.5 inch .300 BLK barrel failed the
/// "starts with barrel_" test, fell through to the muzzle-device branch and was clamped
/// to -2% — the ballistics of a 16 inch barrel, with nothing on screen to say so. One
/// install had 89 barrels in that state.
///
/// The other half is the reverse mistake: the MP5SD upper receiver is not a barrel item
/// but has a ported 146 mm barrel inside it, and clamping it to -2% handed the SD
/// supersonic ammunition.
/// </summary>
public class BarrelClassificationTests
{
    // vanilla class nodes
    private static readonly MongoId BarrelClass = new("555ef6e44bdc2de9068b457e");
    private static readonly MongoId SilencerClass = new("550aa4cd4bdc2dd8348b456c");
    private static readonly MongoId FlashHiderClass = new("550aa4bf4bdc2dd6348b456b");
    private static readonly MongoId ReceiverClass = new("55818a304bdc2db5418b457d");
    private static readonly MongoId StockClass = new("55818a594bdc2db9688b456a");

    [Fact]
    public void A_pack_barrel_is_a_barrel_although_its_name_says_nothing()
    {
        var world = new World();

        Assert.Equal(PartRole.Barrel, world.RoleOf(World.PackBarrel));
        Assert.Equal("by class", world.EvidenceFor(World.PackBarrel));
    }

    [Fact]
    public void A_pack_barrel_is_normalized_by_length_and_never_clamped()
    {
        var world = new World();

        world.Run();

        // 8.5 inch = 215.9 mm against the .300 BLK reference barrel of 406 mm, C=58:
        // (215.9/273.9)/(406/464) - 1 = -9.9%. The round is famously insensitive to
        // barrel length, which is the whole point of it; the pack shipped -21.5% and
        // the clamp it used to get instead was -2%
        Assert.Equal(-9.91, world.VelocityOf(World.PackBarrel), 2);
    }

    [Fact]
    public void A_barrel_is_recognized_by_the_slot_it_sits_in()
    {
        var world = new World();
        var barrel = world.Items[World.PackBarrel];

        // strip the class: the slot alone has to carry the verdict
        barrel.Parent = default;

        Assert.Equal(PartRole.Barrel, world.RoleOf(World.PackBarrel));
        Assert.Equal("by slot", world.EvidenceFor(World.PackBarrel));
    }

    [Fact]
    public void A_barrel_is_recognized_by_the_properties_only_barrels_carry()
    {
        var world = new World();

        Assert.Equal(PartRole.Barrel, world.RoleOf(World.OrphanBarrel));
        Assert.Equal("by props", world.EvidenceFor(World.OrphanBarrel));
    }

    [Fact]
    public void A_muzzle_device_is_still_clamped()
    {
        var world = new World();

        world.Run();

        Assert.Equal(PartRole.MuzzleDevice, world.RoleOf(World.Brake));
        Assert.Equal(-2, world.VelocityOf(World.Brake), 2);
    }

    [Fact]
    public void The_mp5sd_receiver_carries_a_barrel_rather_than_a_muzzle_device()
    {
        var world = new World();

        Assert.Equal(PartRole.IntegratedBarrel, world.RoleOf(World.SdReceiver));
    }

    [Fact]
    public void The_mp5sd_receiver_is_given_what_the_weapon_does_not_already_carry()
    {
        var world = new World();

        world.Run();

        // the book asks for -23% at the muzzle; the MP5's own 225 mm barrel is already
        // worth +8.43% against the 120 mm pistol reference, and the game adds the two
        Assert.Equal(-31.43, world.VelocityOf(World.SdReceiver), 2);
    }

    [Fact]
    public void A_slide_whose_pistol_holds_the_barrel_separately_is_not_an_integrated_barrel()
    {
        var world = new World();

        // the Glock slide owns the muzzle slot exactly as the MP5SD receiver does; what
        // separates them is that the pistol has a barrel item of its own
        Assert.NotEqual(PartRole.IntegratedBarrel, world.RoleOf(World.GlockSlide));
    }

    [Fact]
    public void A_part_named_as_an_integral_barrel_is_never_clamped()
    {
        var world = new World();

        world.Run();

        Assert.Equal(PartRole.IntegratedBarrel, world.RoleOf(World.IntegralSuppressor));
        Assert.Equal(-25, world.VelocityOf(World.IntegralSuppressor), 2);
    }

    [Fact]
    public void A_part_that_takes_a_magazine_without_being_a_weapon_carries_a_barrel()
    {
        var world = new World();

        Assert.Equal(PartRole.IntegratedBarrel, world.RoleOf(World.MagazineCarrier));
    }

    [Fact]
    public void An_unclassified_part_keeps_its_modifier_and_is_reported()
    {
        var world = new World();

        world.Run();

        Assert.Equal(PartRole.Unknown, world.RoleOf(World.Handguard));
        Assert.Equal(15, world.VelocityOf(World.Handguard), 2);
        Assert.Contains("unclassified", world.Report());
    }

    [Theory]
    // the base game and the Russian packs
    [InlineData("barrel_ar15_260mm_556x45", 260)]
    [InlineData("[Pack]_(BM-59 467 mm Barrel)", 467)]
    [InlineData("[WTT]_(Ствол 370мм для AR-15)", 370)]
    // inches, as every American pack writes them
    [InlineData("[Pack]_(BRN-180 Gen.3 14 inch Barrel)", 355.6)]
    [InlineData("[Pack]_(AR-15 .300 blackout 8.5 inch barrel)", 215.9)]
    [InlineData("[Pack]_(AI AXMC .338 LM 20 inch barrel)", 508)]
    // millimetres win where a name carries both
    [InlineData("MPX-SD 9x19 6.5 inch (165mm) ported barrel", 165)]
    // the caliber is not a length: "5.56x45mm" offers 45 to anything reading left to right
    [InlineData("[Pack]_(AR-15 5.56x45mm 11.5 inch barrel)", 292.1)]
    // a number with no unit is not a length either, and guessing is worse than not
    [InlineData("[Pack]_(AR-15 .300 Blackout 12.5 Carbine barrel)", 0)]
    [InlineData("[Pack]_(MK-12 Mod 0 Barrel)", 0)]
    public void Lengths_are_read_off_the_name_in_whatever_unit_it_uses(string name, double expected) =>
        Assert.Equal(expected, BarrelNormalizer.ParseLength([name]), 1);

    [Theory]
    // a pack that rechambers a vanilla weapon rewrites the key the book is indexed by,
    // and leaves the prototype's name in plain sight
    [InlineData("[EpicsAIO]_(Kalashnikov AKS-74U .300 Blackout Assault Rifle)", 206.5)]
    [InlineData("[EpicsAIO]_(Kalashnikov AKS-74UN .300 Blackout Assault Rifle)", 206.5)]
    [InlineData("[EpicsAIO]_(Kalashnikov AK-102 .300 Blackout assault rifle)", 314)]
    [InlineData("[Eco]_(Kalashnikov AK-105 5.45x39 Kochevnik Bullpup Rifle)", 314)]
    [InlineData("[WTT]_(Saiga-12K 12ga automatic shotgun (Redline))", 430)]
    // an AK-12K is not an AK-12: a prefix must not count, and the book's own 290 mm
    // entry has to win over the 415 mm one it is spelled inside of
    [InlineData("[Eco]_(Kalashnikov AK-12K 5.45x39 assault rifle)", 290)]
    // a pack that backports content writes the internal name, hyphens and all missing
    [InlineData("[WTT-ContentBackport]_(weapon_izhmash_ak308_762x51)", 415)]
    // rechambering does not move a barrel: a .45 Thompson relined for 7.62x25 is still
    // a Thompson
    [InlineData("[Eco]_(M1921 Thompson 7.62x25 submachine gun)", 267)]
    // and a gun is what it is built as, not what the pack calls it: this one wears the
    // vanilla AKS-74U and is 206.5 mm, whatever the 12.25 inch original measures
    [InlineData("[EpicsAIO]_(Century Arms Draco 7.62x39 carbine)", 206.5)]
    [InlineData("[EpicsAIO]_(Modified Century Arms Draco 7.62x39 Assault Rifle)", 206.5)]
    // nor may a two-letter prototype find itself inside a caliber: "PM" is in "9x18PM"
    [InlineData("[Pack]_(Some 9x18PM machine pistol)", 0)]
    // a weapon the book has never heard of stays unknown rather than being guessed at
    [InlineData("[Eco]_(Kalashnikov AS-1 5.45x39 assault rifle)", 0)]
    public void A_renamed_clone_is_measured_by_the_prototype_it_is_named_after(string name, double expected)
    {
        var book = new ReferenceBook(new TestLogger<ReferenceBook>()).Load(World.ModPath());

        Assert.Equal(expected, BarrelNormalizer.LengthFromPrototype([name], book, out _), 1);
    }

    [Fact]
    public void The_longest_prototype_name_wins()
    {
        var book = new ReferenceBook(new TestLogger<ReferenceBook>()).Load(World.ModPath());

        // "Uzi Pro Pistol" contains "Uzi", and the two are 114 mm and 260 mm apart
        BarrelNormalizer.LengthFromPrototype(["[Pack]_(IWI Uzi Pro Pistol 9x19)"], book, out var prototype);

        Assert.Equal("Uzi Pro Pistol", prototype);
    }

    [Theory]
    // the dimensions, with the dots a pack writes and the base game does not
    [InlineData("barrel_ar15_260mm_556x45", "Caliber556x45NATO")]
    [InlineData("[Pack]_(AC-TX 7.62x51 22 inch stainless steel threaded barrel)", "Caliber762x51")]
    // a Russian pack writes the x of "5.56х45" in Cyrillic, in an otherwise Latin name
    [InlineData("[WTT]_(Ствол 370мм для AR-15 и совместимых 5.56х45)", "Caliber556x45NATO")]
    // the trade name, which is all a pack ever writes for these
    [InlineData("[Pack]_(AR-15 .300 blackout 8.5 inch barrel)", "Caliber762x35")]
    [InlineData("[Pack]_(AI AXMC .338 LM 20 inch barrel)", "Caliber86x70")]
    [InlineData("[Pack]_(M700 .277 Sig Fury 26 inch barrel)", "Caliber68x51")]
    // two calibers in one name decide nothing; the slot graph is asked instead
    [InlineData("[Pack]_(\"Honey Badger\" 10 inch 5.56x45 Blackout barrel)", null)]
    public void Calibers_are_read_off_the_name_by_dimension_or_trade_name(string name, string? expected)
    {
        var book = new ReferenceBook(new TestLogger<ReferenceBook>()).Load(World.ModPath());

        Assert.Equal(expected, BarrelNormalizer.CaliberFromText([name], book));
    }

    /// <summary>
    /// A miniature item database: an MP5 whose barrel is inside its receiver, a Glock
    /// whose barrel is an item, an AR-15 wearing a pack's barrel, and the odds and ends
    /// that used to be swept into the muzzle-device branch.
    /// </summary>
    private sealed class World
    {
        public static readonly MongoId Mp5 = new("aa0000000000000000000001");
        public static readonly MongoId SdReceiver = new("aa0000000000000000000002");
        public static readonly MongoId Brake = new("aa0000000000000000000003");
        public static readonly MongoId Glock = new("aa0000000000000000000004");
        public static readonly MongoId GlockSlide = new("aa0000000000000000000005");
        public static readonly MongoId GlockBarrel = new("aa0000000000000000000006");
        public static readonly MongoId Ar15 = new("aa0000000000000000000007");
        public static readonly MongoId Ar15Receiver = new("aa0000000000000000000008");
        public static readonly MongoId PackBarrel = new("aa0000000000000000000009");
        public static readonly MongoId Handguard = new("aa000000000000000000000a");
        public static readonly MongoId BufferTube = new("aa000000000000000000000b");
        public static readonly MongoId OrphanBarrel = new("aa000000000000000000000c");
        public static readonly MongoId IntegralSuppressor = new("aa000000000000000000000d");
        public static readonly MongoId MagazineCarrier = new("aa000000000000000000000e");

        public Dictionary<MongoId, TemplateItem> Items { get; }

        private readonly BarrelNormalizer _normalizer;
        private readonly Dictionary<string, string> _locale;

        public World()
        {
            _locale = new Dictionary<string, string>
            {
                [$"{IntegralSuppressor} Name"] = "VSS 9x39 integral barrel-suppressor",
            };

            Items = new Dictionary<MongoId, TemplateItem>
            {
                // --- MP5: the barrel lives inside the upper receiver ---
                [Mp5] = Weapon(Mp5, "weapon_hk_mp5_navy3_9x19", "Caliber9x19PARA", 13,
                    Slot("mod_reciever", SdReceiver), Slot("mod_magazine")),
                [SdReceiver] = Part(SdReceiver, "reciever_mp5_hk_sd", ReceiverClass, -33,
                    Slot("mod_muzzle", Brake)),
                [Brake] = Part(Brake, "muzzle_mp5_brake", FlashHiderClass, -12),

                // --- Glock: the slide owns the muzzle, but the barrel is an item ---
                [Glock] = Weapon(Glock, "weapon_glock_glock_17_gen3_9x19", "Caliber9x19PARA", 0,
                    Slot("mod_barrel", GlockBarrel), Slot("mod_reciever", GlockSlide)),
                [GlockSlide] = Part(GlockSlide, "reciever_glock_glock_17_std", ReceiverClass, 0,
                    Slot("mod_muzzle", Brake)),
                [GlockBarrel] = Barrel(GlockBarrel, "barrel_glock_114mm_9x19_std", -2),

                // --- AR-15 wearing a pack's barrel, named as the pack names it ---
                [Ar15] = Weapon(Ar15, "weapon_colt_m4a1_556x45", "Caliber556x45NATO", 0,
                    Slot("mod_reciever", Ar15Receiver)),
                [Ar15Receiver] = Part(Ar15Receiver, "reciever_ar15_colt_m4a1_std", ReceiverClass, 0,
                    Slot("mod_barrel", PackBarrel), Slot("mod_handguard", Handguard)),
                [PackBarrel] = Barrel(PackBarrel, "[EpicsAIO]_(AR-15 .300 blackout 8.5 inch barrel)", -21.5),

                // --- the odds and ends ---
                [Handguard] = Part(Handguard, "handguard_ar15_pack", default, 15),
                [BufferTube] = Part(BufferTube, "stock_ar15_receiver_extension", StockClass, 2),
                [OrphanBarrel] = new TemplateItem
                {
                    Id = OrphanBarrel,
                    Name = "something_a_pack_invented_386mm_762x51",
                    Properties = new TemplateItemProperties
                    {
                        Velocity = -8, CenterOfImpact = 0.05, ShotgunDispersion = 1,
                    },
                },
                [IntegralSuppressor] = Part(IntegralSuppressor, "silencer_vss_pack", SilencerClass, -25),
                [MagazineCarrier] = Part(MagazineCarrier, "chassis_with_a_barrel_in_it", default, -18,
                    Slot("mod_magazine")),
            };

            var templateTable = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
            typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templateTable, Items);

            var localeTable = (LocaleTable)RuntimeHelpers.GetUninitializedObject(typeof(LocaleTable));
            var global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>>
            {
                ["en"] = new(() =>
                {
                    var dictionary = new GlobalLocaleDictionary();
                    foreach (var (key, value) in _locale)
                    {
                        dictionary[key] = value;
                    }

                    return dictionary;
                }),
            };
            typeof(LocaleTable).GetProperty(nameof(LocaleTable.Global))!.SetValue(localeTable, global);

            _normalizer = new BarrelNormalizer(
                templateTable,
                new ReferenceBook(new TestLogger<ReferenceBook>()),
                localeTable,
                new TestLogger<BarrelNormalizer>());
        }

        public void Run() => _normalizer.Run(new PlateServerConfig(), ModPath());

        public PartRole RoleOf(MongoId id) => Classifier().RoleOf(Items[id]);

        public string EvidenceFor(MongoId id) => Classifier().BarrelEvidence(Items[id]);

        public double VelocityOf(MongoId id) => Items[id].Properties!.Velocity!.Value;

        public string Report() =>
            File.ReadAllText(System.IO.Path.Combine(ModPath(), "plate-barrel-report.md"));

        private PartClassifier Classifier() => new(Items, _locale);

        public static string ModPath()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plate-tests", "barrel-classification");
            Directory.CreateDirectory(path);
            return path;
        }

        private static TemplateItem Weapon(MongoId id, string name, string caliber, double velocity,
            params Slot[] slots) =>
            new()
            {
                Id = id,
                Name = name,
                Properties = new TemplateItemProperties
                {
                    AmmoCaliber = caliber, Velocity = velocity, Slots = slots,
                },
            };

        private static TemplateItem Barrel(MongoId id, string name, double velocity, params Slot[] slots) =>
            new()
            {
                Id = id,
                Name = name,
                Parent = BarrelClass,
                Properties = new TemplateItemProperties
                {
                    Velocity = velocity, CenterOfImpact = 0.05, ShotgunDispersion = 1, Slots = slots,
                },
            };

        private static TemplateItem Part(MongoId id, string name, MongoId parent, double velocity,
            params Slot[] slots) =>
            new()
            {
                Id = id,
                Name = name,
                Parent = parent,
                Properties = new TemplateItemProperties { Velocity = velocity, Slots = slots },
            };

        private static Slot Slot(string name, params MongoId[] children) =>
            new()
            {
                Name = name,
                Properties = new SlotProperties
                {
                    Filters = [new SlotFilter { Filter = children.ToHashSet() }],
                },
            };
    }
}
