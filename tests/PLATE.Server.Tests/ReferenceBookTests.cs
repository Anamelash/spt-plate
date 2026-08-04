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
    /// <summary>The shipped book, for fixtures in other files that measure against it.</summary>
    public static ReferenceBook.AmmoReference ShippedBook() => Shipped();

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
        Assert.NotEmpty(r.Bullets);
        Assert.NotEmpty(r.Grenades);
        Assert.NotEmpty(r.Barrels);
        Assert.NotEmpty(r.Weapons);
    }

    [Fact]
    public void Every_bullet_is_a_fraction_of_itself()
    {
        foreach (var (name, b) in Shipped().Bullets)
        {
            Assert.True(b.X is >= 0 and <= 1,
                $"{name}: X={b.X} is not a fraction of the bullet");
            Assert.True(b.CoreAreaFrac is >= 0 and <= 1,
                $"{name}: core area {b.CoreAreaFrac} is not a fraction of the bullet");
            Assert.True(b.CoreMassFrac is >= 0 and <= 1,
                $"{name}: core mass {b.CoreMassFrac} is not a fraction of the bullet");
            Assert.False(string.IsNullOrWhiteSpace(b.Prototype),
                $"{name}: an entry with no prototype says nothing the statistic would not");
        }
    }

    /// <summary>
    /// A core is a claim about the inside of a bullet, so it has to be sourced. The rule
    /// the book states is stricter than "write something down": an area fraction below 1
    /// means the core is hard enough to keep its shape against a plate, and that is the
    /// line between the M855 and the M855A1.
    /// </summary>
    [Fact]
    public void Every_published_core_says_where_it_came_from()
    {
        var cored = 0;
        foreach (var (name, b) in Shipped().Bullets)
        {
            if (b.CoreAreaFrac <= 0 && b.CoreMassFrac <= 0)
            {
                continue;
            }

            cored++;
            Assert.False(string.IsNullOrWhiteSpace(b.Source),
                $"{name}: a core fraction with no source behind it");
            Assert.True(b.X < 0.5,
                $"{name}: X={b.X} says the bullet is soft, the core fractions say it is not");
        }

        Assert.True(cored >= 15, $"only {cored} bullets carry a published construction");
    }

    /// <summary>
    /// The two tungsten-carbide cores whose geometry is published by different makers in
    /// different calibres — M993 and 7N37 — should land on the same fraction of their
    /// bullet's face. They do, and that is the only reason to trust the ones derived
    /// from a mass and a length.
    /// </summary>
    [Fact]
    public void The_two_carbide_cores_agree_with_each_other()
    {
        var bullets = Shipped().Bullets;
        var m993 = bullets["patron_762x51_m993"].CoreAreaFrac;
        var r7n37 = bullets["patron_762x54r_7n37"].CoreAreaFrac;

        Assert.True(Math.Abs(m993 - r7n37) < 0.1,
            $"the carbide cores disagree: M993 {m993:0.00} against 7N37 {r7n37:0.00}");
    }

    /// <summary>
    /// The M855 and the M855A1 are the same cartridge, the same bullet weight and the
    /// same calibre; the difference between them is that one core is 40 HRC and the
    /// other 58. If the book ever stops saying so, the model has lost the distinction
    /// that the whole core mechanic exists to carry.
    /// </summary>
    [Fact]
    public void A_soft_penetrator_gets_no_concentration()
    {
        var bullets = Shipped().Bullets;

        Assert.Equal(1.0, bullets["patron_556x45_M855"].CoreAreaFrac);
        Assert.True(bullets["patron_556x45_M855"].CoreMassFrac > 0,
            "the M855 still loses everything but its tip going through a plate");

        Assert.True(bullets["patron_556x45_M855A1"].CoreAreaFrac is > 0.4 and < 0.7,
            "the M855A1's hardened tip concentrates and the book should say by how much");
    }

    /// <summary>
    /// Both fractions or neither, spelled out. The first run of this table left the area
    /// off the soft-cored rounds meaning "no concentration", the loader read a missing
    /// area as "same as the mass", and the M855 came out of the normalizer with a
    /// sixfold concentration and the highest penetration in the game.
    /// </summary>
    [Fact]
    public void A_core_mass_without_an_area_beside_it_is_an_invitation_to_guess()
    {
        foreach (var (name, b) in Shipped().Bullets)
        {
            if (b.CoreMassFrac <= 0)
            {
                continue;
            }

            Assert.True(b.CoreAreaFrac > 0,
                $"{name}: a core mass with no area fraction next to it - say 1.0 if the " +
                "core is too soft to concentrate, but say it");
        }
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
        Assert.Contains(filled, f => f.StartsWith("Bullets "));
        Assert.True(old.Barrels.ContainsKey("Caliber762x51"));
        Assert.True(old.Bullets.ContainsKey("patron_762x51_m993"));
    }

    /// <summary>
    /// The merge deliberately never overwrites, so a figure that was wrong when it
    /// shipped would stay wrong on every machine that had already run the mod once.
    /// The version field is the way out, and it only works if the shipped book actually
    /// declares one.
    /// </summary>
    [Fact]
    public void The_shipped_book_declares_its_version()
    {
        Assert.True(Shipped().Version > 0, "no Version in the shipped reference book");
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
    /// A strength without a provenance is indistinguishable from a fitted one. "Source
    /// is non-empty" does not guard that — the old book passed it with "450 MPa shear",
    /// which restates the number without saying where it came from. What this test
    /// demands is the origin itself: either a derivation rule written with the
    /// convention's "·" (0.45·UTS and its kin) or the name of a document from the fixed
    /// list below. The list is the convention: extending it is a reviewed change, not a
    /// way past the test.
    /// </summary>
    [Fact]
    public void Every_armour_material_strength_names_its_origin()
    {
        // documents the book is allowed to cite; each name is a real, findable source
        string[] documents =
        [
            "SSAB", "ASM", "MMPDS", "MIL-DTL", "CoorsTek", "Saint-Gobain", "Ashby",
            "DuPont", "Dyneema", "GOST", "STANAG",
        ];

        foreach (var (name, m) in Shipped().ArmorMaterials)
        {
            var derived = m.Source.Contains('·');
            var cited = documents.Any(m.Source.Contains);

            Assert.True(derived || cited,
                $"{name}: Source says \"{m.Source}\" — a number, maybe, but not where " +
                "it came from. State the derivation rule (with '·') or name the document");
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
            // a thickness, or failing that the rating the maker certifies, or failing
            // that what it is made of — an entry with none of the three says nothing
            // the game did not already know
            if (plate.ThicknessMm > 0)
            {
                Assert.InRange(plate.ThicknessMm, 0.5, 60);
            }
            else
            {
                Assert.True(plate.Rating > 0 || plate.Material.Length > 0,
                    $"{name}: no thickness, no rating and no material");
            }

            if (plate.Rating > 0)
            {
                Assert.InRange(plate.Rating, 1, 6);
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
    /// Thickness times density is areal density, and that is the quantity a penetration
    /// model actually spends. It is also the check that catches a figure entered under
    /// the wrong convention: the ESAPI is 10 mm, but of boron carbide at 2.52, and read
    /// as the alumina the material table holds it would weigh half again what it does.
    /// Nothing wearable is under 0.3 g/cm² or over 5.
    /// </summary>
    [Fact]
    public void Every_documented_construction_weighs_something_a_person_could_wear()
    {
        var materials = Shipped().ArmorMaterials;

        foreach (var (name, plate) in Shipped().ArmorPlates)
        {
            if (plate.ThicknessMm <= 0)
            {
                continue;
            }

            // an entry that gives a thickness has to say what the thickness is of, or
            // this check cannot run on it and the figure goes unwatched
            Assert.True(plate.Material.Length > 0, $"{name}: a thickness and no material");

            var density = plate.DensityGCm3 > 0
                ? plate.DensityGCm3
                : materials[plate.Material].DensityGCm3;

            // mm * g/cm³ -> g/cm² is a tenth. The top of the range is the Korund-VM
            // steel panel at 4.95, which is about as much as anyone has ever agreed to
            // carry over one dm² of themselves
            var arealGCm2 = plate.ThicknessMm * density / 10.0;
            Assert.InRange(arealGCm2, 0.3, 5.0);
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

        var book = Shipped();
        foreach (var key in shipped)
        {
            Assert.True(byClass.ContainsKey(key), $"no reference plate for {key}");

            // through the resolver: a represented rung's thickness is its product's
            var resolved = book.ResolveByClass(key);
            Assert.InRange(resolved.ThicknessMm, 1, 60);
            Assert.True(materials.ContainsKey(key.Split('/')[0]), $"{key}: unknown material");

            // a rung that names a product must name one that exists and matches
            if (byClass[key].SameAs.Length > 0)
            {
                Assert.True(book.ArmorPlates.ContainsKey(byClass[key].SameAs),
                    $"{key} borrows from '{byClass[key].SameAs}', which is not in the book");
                Assert.Equal(key.Split('/')[0], resolved.Material);
            }
        }
    }

    /// <summary>
    /// A higher rating in the same material is a thicker plate. If it is not, the class
    /// reference will make some armour weaker than the armour it outranks.
    /// </summary>
    [Fact]
    public void A_higher_class_of_the_same_material_is_thicker()
    {
        var book = Shipped();
        var byClass = book.ArmorByClass;

        foreach (var group in byClass.GroupBy(kv => kv.Key.Split('/')[0]))
        {
            var ladder = group
                .Select(kv => (Class: int.Parse(kv.Key.Split('/')[1]),
                    book.ResolveByClass(kv.Key).ThicknessMm))
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

        // The three tables must actually differ, otherwise splitting them bought
        // nothing. Compare them by AREAL DENSITY, not by thickness: a sewn package is
        // loose plies and can be thicker than a pressed shell while weighing a third of
        // it, which is exactly what happens at UHMWPE/2 — 7.0 mm of package against
        // 6.0 mm of shell, and the package is the lighter object by far.
        static double Areal(ReferenceBook.ArmorPlateRef p, double fallback) =>
            p.ThicknessMm * (p.DensityGCm3 > 0 ? p.DensityGCm3 : fallback);

        var sewnPack = Areal(soft["UHMWPE/2"], 0.97);
        var pressed = Areal(shells["UHMWPE/2"], 0.97);
        var monolith = Areal(plates["UHMWPE/3"], 0.97);

        Assert.True(sewnPack < pressed, $"a sewn package ({sewnPack:N1}) must be lighter than a shell ({pressed:N1})");
        Assert.True(pressed < monolith, $"a shell ({pressed:N1}) must be lighter than a plate ({monolith:N1})");
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
    [InlineData("ratnik_6b47_level3_helmet_armor_top", "Aramid", 3, 8.6)]
    [InlineData("6b43_6a_level3_soft_armor_front", "Aramid", 3, 7.6)]
    [InlineData("ulach_level4_helmet_armor_top", "UHMWPE", 4, 7.3)]
    [InlineData("item_equipment_facecover_welding_gorilla", "ArmoredSteel", 5, 4.5)]
    // a face mask is pressed like a helmet, whatever the game files it under
    [InlineData("item_equipment_facecover_ballistic_mask", "UHMWPE", 3, 7.3)]
    [InlineData("item_equipment_facecover_shatteredmask", "Aramid", 3, 8.6)]
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
    /// A class ceiling is a statement about a form — plies stitched together, prepreg
    /// pressed into a shell — and a hard element is not one of them. The Velocity SLAAP
    /// bolts onto a helmet and is rated for rifle fire, so the shell ceiling has nothing
    /// to say about the class it is sold at, and the book has to say so in data rather
    /// than in a comment.
    /// </summary>
    [Fact]
    public void A_hard_element_in_a_helmet_slot_is_not_read_as_a_shell()
    {
        var book = Shipped();
        var slaap = book.ArmorPlates["item_equipment_helmet_gentex_slaap_gray"];

        Assert.True(slaap.Plate);
        Assert.True(slaap.ThicknessMm > book.HelmetShells["UHMWPE/3"].ThicknessMm,
            "an applique the ceiling does not reach has to be visibly not a shell");

        // and it is a construction, not a lever: the flag lifts a rating, so an entry
        // that carries it has to have the thickness to answer for the one it keeps
        foreach (var (key, entry) in book.ArmorPlates)
        {
            Assert.True(!entry.Plate || entry.ThicknessMm > 0,
                $"{key}: called a plate with no thickness to show for it");
        }
    }

    /// <summary>
    /// Sometimes the maker publishes what a thing stops and nothing about what it is
    /// made of. That rating is still better than the game's — Fort certify the Kiver-M
    /// at 1+ and the game prints 3 — so the reference is read at theirs.
    /// </summary>
    [Fact]
    public void A_published_rating_is_used_where_a_construction_is_not_published()
    {
        var reference = Shipped();

        // Nothing in the shipped book needs this today — every entry that once carried a
        // bare rating has since had its construction found. The path still has to work,
        // because the next maker who publishes what a thing stops and nothing else will
        // land on it.
        reference.ArmorPlates["a_maker_who_only_says_what_it_stops"] =
            new ReferenceBook.ArmorPlateRef { Prototype = "stated at Br2", Rating = 2 };

        const string item = "a_maker_who_only_says_what_it_stops_level5_helmet_armor_top";
        var spec = ArmorNormalizer.ProductSpec(
            reference, item, ArmorNormalizer.Product(item), out _);

        Assert.Equal(0, spec!.ThicknessMm);
        Assert.Equal(2, spec.Rating);

        // read at their 2 it is the PASGT shell; read at the game's 5 it would be capped
        // to 3 and come out as the heaviest aramid shell ever fielded
        Assert.True(reference.HelmetShells["Aramid/2"].ThicknessMm
                    < reference.HelmetShells["Aramid/3"].ThicknessMm);
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

            // an entry that gave up a rating or a material has not been written off
            if (book.ArmorPlates.TryGetValue(key, out var documented))
            {
                Assert.Fail($"{key}: written off, but the product table has {documented.Prototype}");
            }
        }
    }

    /// <summary>
    /// Runs the normalizer's own lookup, which is private to it — through the ceiling,
    /// as the normalizer does: the lookup is handed a rating the material can hold, and
    /// the item is re-rated to the same figure.
    /// </summary>
    private static double Resolve(ReferenceBook.AmmoReference reference,
        string item, string material, int cls)
    {
        var method = typeof(ArmorNormalizer).GetMethod("ClassReference",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var rating = Math.Min(cls, ArmorNormalizer.ClassCeiling(item, material));
        var resolved = method!.Invoke(null, [reference, item, material, rating]);
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
