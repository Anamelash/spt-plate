using System.Reflection;
using System.Text.Json;
using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The shipped reference book is a string constant in the source, so nothing parses it
/// until a server starts. These tests parse it here instead: a stray comma in a table
/// of two dozen calibers would otherwise silently disable the whole normalizer on a
/// user's machine, with one line in a log nobody reads.
/// </summary>
public class ReferenceBookTests
{
    private static ReferenceBook.AmmoReference Shipped()
    {
        var field = typeof(ReferenceBook).GetField("DefaultReferenceJsonc",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var json = (string)field!.GetRawConstantValue()!;
        var parsed = JsonSerializer.Deserialize<ReferenceBook.AmmoReference>(json,
            new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            });

        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public void The_shipped_reference_book_parses()
    {
        var r = Shipped();

        Assert.NotEmpty(r.Shotshells);
        Assert.NotEmpty(r.Grenades);
        Assert.NotEmpty(r.Barrels);
        Assert.NotEmpty(r.Weapons);
    }

    [Fact]
    public void Every_caliber_can_produce_a_velocity_curve()
    {
        foreach (var (caliber, b) in Shipped().Barrels)
        {
            Assert.True(b.RefMm > 50 && b.RefMm <= 1000,
                $"{caliber}: reference barrel {b.RefMm} mm is not a barrel length");

            var c = b.C > 0 ? b.C : BarrelModel.EstimateC(b.CaseMm3, b.BoreMm);
            Assert.True(c > 0, $"{caliber}: no measured C and nothing to derive one from");

            // relative to its own reference, since a pistol caliber's reference barrel
            // is shorter than the shortest carbine barrel of a rifle one
            var chopped = BarrelModel.VelocityPercent(b.RefMm * 0.4, b.RefMm, c);
            var stretched = BarrelModel.VelocityPercent(b.RefMm * 1.5, b.RefMm, c);
            Assert.True(chopped < 0 && stretched > 0,
                $"{caliber}: velocity does not follow barrel length ({chopped:N1}% / {stretched:N1}%)");
            Assert.InRange(chopped, -70, -3);
        }
    }

    /// <summary>
    /// The measured constants are the ones the fixture in BarrelModelTests validates;
    /// if a caliber quietly loses its C it falls back to an estimate worth ±35% and
    /// nothing says so.
    /// </summary>
    [Fact]
    public void The_calibers_with_measured_ladders_keep_their_measured_constant()
    {
        var barrels = Shipped().Barrels;
        var measured = new Dictionary<string, double>
        {
            ["Caliber762x51"] = 129,
            ["Caliber556x45NATO"] = 134,
            ["Caliber762x39"] = 68,
            ["Caliber762x35"] = 58,
            ["Caliber9x19PARA"] = 24,
            ["Caliber9x33R"] = 56,
        };

        foreach (var (caliber, c) in measured)
        {
            Assert.True(barrels.ContainsKey(caliber), $"{caliber} is missing from the reference book");
            Assert.Equal(c, barrels[caliber].C);
        }
    }

    /// <summary>
    /// Weapon packs bring real cartridges the base game does not have. An install
    /// without them just skips the entry, so carrying them costs nothing and their
    /// absence would silently leave those weapons on whatever numbers the pack chose.
    /// </summary>
    [Fact]
    public void Cartridges_added_by_weapon_packs_are_covered()
    {
        var barrels = Shipped().Barrels;
        string[] fromPacks =
        [
            "Caliber102x22",   // .40 S&W
            "Caliber11x33R",   // .44 Magnum
            "Caliber792x33",   // 7.92x33 Kurz
            "Caliber792x57",   // 7.92x57 Mauser
            "Caliber65x52",    // 6.5x52 Carcano
            "Caliber762x63",   // .30-06
            "Caliber762x67B",  // .300 Win Mag
            "Caliber6ARC",     // 6mm ARC
            "Caliber86x63",    // .338 Norma
            "Caliber93x64",    // 9.3x64 Brenneke
            "Caliber1036x77",  // .408 CheyTac
            "Caliber127x99",   // .50 BMG
            "Caliber127x108",  // 12.7x108
            "Caliber17.8×89",  // .700 Nitro Express - multiplication sign, not an x
        ];

        foreach (var caliber in fromPacks)
        {
            Assert.True(barrels.ContainsKey(caliber), $"{caliber} dropped out of the reference book");
        }
    }

    /// <summary>
    /// The file is written once and then only read. Without a fallback a section added
    /// in a later version reaches nobody who already ran the mod, and the feature behind
    /// it does nothing on every existing install — which is exactly how the barrel pass
    /// silently did nothing on its first run here.
    /// </summary>
    [Fact]
    public void A_reference_file_written_before_a_section_existed_still_gets_it()
    {
        // what an install that predates the barrel work has on disk
        var old = new ReferenceBook.AmmoReference
        {
            Shotshells = { ["patron_12x70_buckshot"] = new ReferenceBook.ShotshellRef() },
        };

        var filled = ReferenceBook.MergeShippedDefaults(old);

        Assert.Contains(filled, f => f.StartsWith("Barrels "));
        Assert.Contains(filled, f => f.StartsWith("Weapons "));
        Assert.Contains(filled, f => f.StartsWith("ArmorMaterials "));
        Assert.Contains(filled, f => f.StartsWith("ArmorPlates "));
        Assert.True(old.Barrels.ContainsKey("Caliber762x51"));
    }

    /// <summary>
    /// The same trap one level down: a table the file already has is not therefore the
    /// table this version ships. Armour products are added release by release, and a
    /// whole-section check means the four an early install has on disk are the four it
    /// keeps forever. Entries arrive one at a time — and never over one already written,
    /// because a figure in that file is a decision somebody made.
    /// </summary>
    [Fact]
    public void A_section_that_exists_still_gains_the_entries_added_since()
    {
        var mine = new ReferenceBook.ArmorPlateRef { Prototype = "mine", ThicknessMm = 1 };
        var old = new ReferenceBook.AmmoReference { ArmorPlates = { ["kora_kulon"] = mine } };

        ReferenceBook.MergeShippedDefaults(old);

        Assert.True(old.ArmorPlates.Count > 1, "nothing was added to a table that was not empty");
        Assert.Same(mine, old.ArmorPlates["kora_kulon"]);
    }

    /// <summary>
    /// Each material class is answered by a different penetration mechanism, so each has
    /// to carry the properties its own mechanism consumes. A ceramic listed with a shear
    /// strength and no compressive strength reads as zero resistance the moment the
    /// model starts using it.
    /// </summary>
    [Fact]
    public void Every_armour_material_carries_what_its_class_needs()
    {
        var materials = Shipped().ArmorMaterials;
        Assert.NotEmpty(materials);

        foreach (var (name, m) in materials)
        {
            Assert.InRange(m.DensityGCm3, 0.5, 20);

            switch (m.Class)
            {
                case "Ductile":
                    Assert.True(m.YieldMPa > 0 && m.ShearMPa > 0, $"{name}: no strength to punch through");
                    Assert.True(m.ShearMPa < m.YieldMPa, $"{name}: shear should be below yield");
                    break;
                case "Brittle":
                    Assert.True(m.CompressiveMPa > 0, $"{name}: a ceramic resists in compression");
                    break;
                case "Fibrous":
                    Assert.True(m.FibreTensileMPa > 0, $"{name}: a fibre resists in tension");
                    Assert.InRange(m.FailureStrain, 0.001, 0.5);
                    break;
                default:
                    Assert.Fail($"{name}: unknown material class '{m.Class}'");
                    break;
            }
        }
    }

    /// <summary>
    /// Thickness is the entire reason the plate table exists — the one physical number
    /// the game has nowhere — so an entry without it carries nothing.
    /// </summary>
    [Fact]
    public void Every_armour_product_states_a_thickness_and_a_source()
    {
        var materials = Shipped().ArmorMaterials;

        foreach (var (name, plate) in Shipped().ArmorPlates)
        {
            // a thickness, or failing that the rating the maker certifies — an entry
            // with neither says nothing the game did not already know
            if (plate.Rating > 0)
            {
                Assert.InRange(plate.Rating, 1, 6);
            }
            else
            {
                Assert.InRange(plate.ThicknessMm, 0.5, 60);
            }

            Assert.False(string.IsNullOrWhiteSpace(plate.Source), $"{name}: no source for the figures");

            if (plate.Material.Length > 0)
            {
                Assert.True(materials.ContainsKey(plate.Material),
                    $"{name}: overrides the material to '{plate.Material}', which is not in the table");
            }
        }
    }

    /// <summary>
    /// The stand-in plate for a rating has to exist for every rating the game actually
    /// ships, or the armour it invented falls through to its own mass — which any mod
    /// that scales weight quietly rewrites.
    /// </summary>
    [Fact]
    public void Every_rating_the_game_ships_has_a_reference_plate()
    {
        var byClass = Shipped().ArmorByClass;
        var materials = Shipped().ArmorMaterials;

        string[] shipped =
        [
            "ArmoredSteel/3", "ArmoredSteel/4", "ArmoredSteel/5", "ArmoredSteel/6",
            "Ceramic/4", "Ceramic/5", "Ceramic/6",
            "Combined/3", "Combined/4", "Combined/5", "Combined/6",
            "Titan/4", "Titan/5", "Titan/6",
            "UHMWPE/3", "UHMWPE/4", "UHMWPE/5", "UHMWPE/6",
            "Aluminium/4",
        ];

        foreach (var key in shipped)
        {
            Assert.True(byClass.ContainsKey(key), $"no reference plate for {key}");
            Assert.InRange(byClass[key].ThicknessMm, 1, 60);
            Assert.True(materials.ContainsKey(key.Split('/')[0]), $"{key}: unknown material");
        }
    }

    /// <summary>
    /// A higher rating in the same material is a thicker plate. If it is not, the class
    /// reference will make some armour weaker than the armour it outranks.
    /// </summary>
    [Fact]
    public void A_higher_class_of_the_same_material_is_thicker()
    {
        var byClass = Shipped().ArmorByClass;

        foreach (var group in byClass.GroupBy(kv => kv.Key.Split('/')[0]))
        {
            var ladder = group
                .Select(kv => (Class: int.Parse(kv.Key.Split('/')[1]), kv.Value.ThicknessMm))
                .OrderBy(x => x.Class)
                .ToList();

            for (var i = 1; i < ladder.Count; i++)
            {
                Assert.True(ladder[i].ThicknessMm > ladder[i - 1].ThicknessMm,
                    $"{group.Key}: class {ladder[i].Class} is not thicker than {ladder[i - 1].Class}");
            }
        }
    }

    /// <summary>
    /// A woven package is not a plate. Every rating the game gives soft armour and
    /// helmet shells needs an entry in their own table, or those items fall through to
    /// a plate's thickness — a class 3 polyethylene plate is 20 mm and a class 3
    /// polyethylene helmet shell is twelve, and handing the shell the plate's figure
    /// would make a helmet nearly twice the armour it is.
    /// </summary>
    [Fact]
    public void Soft_armour_and_helmet_shells_have_their_own_reference()
    {
        var soft = Shipped().SoftArmor;
        var shells = Shipped().HelmetShells;
        var plates = Shipped().ArmorByClass;

        // a sewn package is only ever fabric, and only ever reaches 2
        string[] sewn = ["Aramid/1", "Aramid/2", "UHMWPE/1", "UHMWPE/2"];

        string[] rigid =
        [
            // pressed laminate buys one rung over the sewn package and stops
            "Aramid/1", "Aramid/2", "Aramid/3",
            "UHMWPE/1", "UHMWPE/2", "UHMWPE/3",
            "Glass/1", "Glass/2",
            // metal and ceramic are not capped: a shell really is thicker on a heavier helmet
            "ArmoredSteel/2", "ArmoredSteel/3", "ArmoredSteel/4", "ArmoredSteel/5", "ArmoredSteel/6",
            "Titan/2", "Titan/3", "Titan/4", "Titan/5", "Titan/6",
            "Combined/3", "Combined/4", "Combined/5", "Combined/6",
            "Ceramic/4", "Ceramic/5", "Ceramic/6",
            "Aluminium/3", "Aluminium/4",
        ];

        foreach (var key in sewn)
        {
            Assert.True(soft.ContainsKey(key), $"no soft-armour reference for {key}");
            Assert.InRange(soft[key].ThicknessMm, 2, 20);
        }

        foreach (var key in rigid)
        {
            Assert.True(shells.ContainsKey(key), $"no helmet-shell reference for {key}");
            Assert.InRange(shells[key].ThicknessMm, 2, 20);
        }

        // and the three tables must actually differ, otherwise splitting them bought
        // nothing: a package is a fraction of a plate, and a pressed shell sits between
        Assert.True(soft["UHMWPE/2"].ThicknessMm < shells["UHMWPE/2"].ThicknessMm);
        Assert.True(shells["UHMWPE/3"].ThicknessMm < plates["UHMWPE/3"].ThicknessMm);
    }

    /// <summary>
    /// Fabric cannot be rated past 2 by being sewn thicker and a visor cannot be rated
    /// past 2 at all, so neither table may offer a rung above its ceiling — an entry
    /// there would be applied to something, and would mean a rating had lifted a
    /// ceiling it cannot lift.
    /// </summary>
    [Fact]
    public void No_table_offers_a_rung_its_material_cannot_reach()
    {
        foreach (var (key, _) in Shipped().SoftArmor)
        {
            var parts = key.Split('/');
            Assert.True(int.Parse(parts[1]) <= 2, $"{key}: sewn fabric stops where it stops");
        }

        foreach (var (key, _) in Shipped().HelmetShells)
        {
            var parts = key.Split('/');
            var ceiling = parts[0] switch
            {
                "Aramid" or "UHMWPE" => 3,
                "Glass" => 2,
                _ => 6,
            };

            Assert.True(int.Parse(parts[1]) <= ceiling,
                $"{key}: past {ceiling} a shell of this is no longer a shell of this");
        }
    }

    /// <summary>
    /// The whole point of the split: the same fibre at the same rating has to come out
    /// as two different objects depending on whether it was pressed or sewn. Nothing
    /// rigid ever reads off the sewn table — there is no such thing as a steel package.
    /// </summary>
    [Theory]
    [InlineData("ratnik_6b47_level3_helmet_armor_top", "Aramid", 3, 8.5)]
    [InlineData("6b43_6a_level3_soft_armor_front", "Aramid", 3, 7.0)]
    [InlineData("ulach_level4_helmet_armor_top", "UHMWPE", 4, 12.2)]
    [InlineData("item_equipment_facecover_welding_gorilla", "ArmoredSteel", 5, 4.5)]
    // a face mask is pressed like a helmet, whatever the game files it under
    [InlineData("item_equipment_facecover_ballistic_mask", "UHMWPE", 3, 12.2)]
    [InlineData("item_equipment_facecover_shatteredmask", "Aramid", 3, 8.5)]
    // and the few pieces of headgear that really are cloth stay cloth
    [InlineData("balaclava", "UHMWPE", 3, 7.0)]
    [InlineData("item_equipment_head_bomber", "Aramid", 1, 5.5)]
    public void Pressed_and_sewn_read_off_different_tables(
        string item, string material, int cls, double expected)
    {
        var reference = Shipped();
        ReferenceBook.MergeShippedDefaults(reference);

        Assert.Equal(expected, Resolve(reference, item, material, cls), 1);
    }

    /// <summary>
    /// Sometimes the maker publishes what a thing stops and nothing about what it is
    /// made of. That rating is still better than the game's — Fort certify the Kiver-M
    /// at 1+ and the game prints 3 — so the reference is read at theirs.
    /// </summary>
    [Fact]
    public void A_published_rating_is_used_where_a_construction_is_not_published()
    {
        var book = Shipped();

        var kiver = book.ArmorPlates["fort_kiver_m"];
        Assert.Equal(0, kiver.ThicknessMm);
        Assert.Equal(2, kiver.Rating);

        // read at 2 it is the PASGT shell; read at the game's 3 it would be the heaviest
        // aramid shell ever fielded
        Assert.True(book.HelmetShells["Aramid/2"].ThicknessMm
                    < book.HelmetShells["Aramid/3"].ThicknessMm);
    }

    /// <summary>
    /// The headstone list is a record of where somebody looked, so it is worth nothing
    /// if it names things that are not in the game or duplicates a product that already
    /// has its figures.
    /// </summary>
    [Fact]
    public void Nothing_is_both_documented_and_written_off()
    {
        var book = Shipped();

        foreach (var (key, why) in book.NoRealSpecs)
        {
            Assert.False(string.IsNullOrWhiteSpace(why), $"{key}: no reason given");

            // an entry that states a rating has not been written off — it gave us that
            if (book.ArmorPlates.TryGetValue(key, out var documented))
            {
                Assert.True(documented.ThicknessMm <= 0 && documented.Rating <= 0,
                    $"{key}: written off, but the product table has its construction");
            }
        }
    }

    /// <summary>Runs the normalizer's own lookup, which is private to it.</summary>
    private static double Resolve(ReferenceBook.AmmoReference reference,
        string item, string material, int cls)
    {
        var method = typeof(ArmorNormalizer).GetMethod("ClassReference",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var resolved = method!.Invoke(null, [reference, item, material, cls, cls]);
        Assert.NotNull(resolved);

        var plate = (ReferenceBook.ArmorPlateRef)resolved!.GetType()
            .GetProperty("Ref")!.GetValue(resolved)!;
        return plate.ThicknessMm;
    }


    /// <summary>A visor is never a plate, so the plate table must not offer one.</summary>
    [Fact]
    public void Glass_is_not_in_the_plate_table()
    {
        Assert.DoesNotContain(Shipped().ArmorByClass.Keys, k => k.StartsWith("Glass/"));
    }

    [Fact]
    public void Integral_barrel_weapons_have_plausible_lengths()
    {
        foreach (var (name, w) in Shipped().Weapons)
        {
            // an MP5K is 115 mm at one end and an NSV is 1100 mm at the other
            Assert.InRange(w.LengthMm, 50, 1200);
            Assert.False(string.IsNullOrWhiteSpace(w.Prototype), $"{name} has no prototype named");
        }
    }
}
