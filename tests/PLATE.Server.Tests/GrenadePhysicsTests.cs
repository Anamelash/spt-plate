using System.Runtime.CompilerServices;
using PLATE.Server.Config;
using PLATE.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The grenade pass is the one place in the mod that puts new templates into the item
/// database, and since SPT 4.1.3 doing that at the wrong moment is fatal: the server
/// snapshots the item ids before it loads profiles and kills the process if anything
/// appeared afterwards (DatabaseIntegrityService). PLATE therefore registers its
/// fragments early and runs the same pass again late, where it may configure but not
/// create. These tests hold that line — a "create it if it is missing" that creeps back
/// into the late pass takes down every server running the mod.
/// </summary>
public class GrenadePhysicsTests
{
    private const string GrenadeName = "weapon_grenade_f1";
    private static readonly MongoId GrenadeId = new("5710c24ad2720bc3458b45a3");
    private static readonly MongoId ShrapnelId = new("5996f6d686f77467977ba6cc");

    [Fact]
    public void The_late_pass_adds_nothing_to_the_item_database()
    {
        var (physics, items, _) = Fixture();
        var before = items.Keys.ToHashSet();

        physics.Apply(new PlateServerConfig(), ModPath(), canAddItems: false);

        Assert.Equal(before, items.Keys.ToHashSet());
        Assert.Equal(ShrapnelId, new MongoId(items[GrenadeId].Properties!.FragmentType!));
    }

    [Fact]
    public void The_registration_pass_adds_one_fragment_per_grenade_and_the_late_pass_keeps_it()
    {
        var (physics, items, cfg) = Fixture();

        physics.Apply(cfg, ModPath(), canAddItems: true);

        var fragments = items.Keys.Where(GrenadePhysics.IsFragmentTemplate).ToList();
        Assert.Single(fragments);

        var fragment = items[fragments[0]];
        var props = fragment.Properties!;
        Assert.Equal(GrenadePhysics.FragmentTemplateId(1), fragments[0].ToString());
        Assert.Equal(fragments[0], new MongoId(items[GrenadeId].Properties!.FragmentType!));
        // F-1: 1.5 g at 730 m/s, nothing like the 0.09 g at 90 m/s vanilla shrapnel
        Assert.Equal(1.5, props.BulletMassGram!.Value, 3);
        Assert.Equal(730, props.InitialSpeed!.Value, 3);

        // the late pass must find its own template and re-apply the numbers to it,
        // whatever another mod did to them in between
        props.Damage = 1;
        props.BulletMassGram = 0.09;
        var afterRegistration = items.Keys.ToHashSet();

        physics.Apply(cfg, ModPath(), canAddItems: false);

        Assert.Equal(afterRegistration, items.Keys.ToHashSet());
        Assert.Equal(1.5, props.BulletMassGram!.Value, 3);
        Assert.True(props.Damage > 1);
    }

    [Fact]
    public void A_grenade_that_appears_after_registration_keeps_its_vanilla_fragments()
    {
        var (physics, items, cfg) = Fixture();

        // the registration pass never saw this grenade: it is in the database, its
        // fragment template is not, and the database is closed
        physics.Apply(cfg, ModPath(), canAddItems: false);

        Assert.DoesNotContain(items.Keys, GrenadePhysics.IsFragmentTemplate);
        Assert.Equal(ShrapnelId, new MongoId(items[GrenadeId].Properties!.FragmentType!));
    }

    [Fact]
    public void A_fragment_id_taken_by_another_mod_is_left_alone()
    {
        var (physics, items, cfg) = Fixture();
        var squatted = new MongoId(GrenadePhysics.FragmentTemplateId(1));
        items[squatted] = new TemplateItem
        {
            Id = squatted,
            Name = "someone_elses_item",
            Properties = new TemplateItemProperties(),
        };

        physics.Apply(cfg, ModPath(), canAddItems: true);

        // repointing the grenade at a stranger's template would be worse than vanilla
        Assert.Equal("someone_elses_item", items[squatted].Name);
        Assert.Equal(ShrapnelId, new MongoId(items[GrenadeId].Properties!.FragmentType!));
    }

    /// <summary>
    /// A database with one vanilla grenade and the shrapnel template it points at — the
    /// reference book ships F-1 first, so it is the fragment with index 1.
    /// </summary>
    private static (GrenadePhysics Physics, Dictionary<MongoId, TemplateItem> Items, PlateServerConfig Config) Fixture()
    {
        var items = new Dictionary<MongoId, TemplateItem>
        {
            [GrenadeId] = new()
            {
                Id = GrenadeId,
                Name = GrenadeName,
                Properties = new TemplateItemProperties
                {
                    FragmentType = ShrapnelId.ToString(),
                    FragmentsCount = 70,
                    Strength = 100,
                },
            },
            [ShrapnelId] = new()
            {
                Id = ShrapnelId,
                Name = "shrapnel_F1",
                Properties = new TemplateItemProperties
                {
                    BulletMassGram = 0.09,
                    BulletDiameterMilimeters = 7,
                    InitialSpeed = 90,
                    Damage = 55,
                    PenetrationPower = 10,
                    Caliber = "Caliber9x18PM",
                    AmmoType = "bullet",
                },
            },
        };

        var templateTable = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templateTable, items);

        // Global is left null; GrenadePhysics checks for that and skips the locale entries
        var localeTable = (LocaleTable)RuntimeHelpers.GetUninitializedObject(typeof(LocaleTable));

        var physics = new GrenadePhysics(
            templateTable,
            localeTable,
            new ReferenceBook(new TestLogger<ReferenceBook>()),
            new JsonUtil([]),
            new TestLogger<GrenadePhysics>());

        return (physics, items, new PlateServerConfig());
    }

    /// <summary>Somewhere for the shipped reference book to be written to and read back.</summary>
    private static string ModPath()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plate-tests", "grenade-physics");
        Directory.CreateDirectory(path);
        return path;
    }
}
