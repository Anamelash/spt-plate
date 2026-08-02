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

        Assert.Contains("Barrels", filled);
        Assert.Contains("Weapons", filled);
        Assert.Contains("ArmorMaterials", filled);
        Assert.Contains("ArmorPlates", filled);
        Assert.True(old.Barrels.ContainsKey("Caliber762x51"));

        // and the section it did have is left exactly as the user had it
        Assert.Single(old.Shotshells);
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
            Assert.InRange(plate.ThicknessMm, 0.5, 60);
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
            "Glass/1", "Glass/2", "Glass/3", "Glass/4",
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
    /// A visor is polycarbonate and triplex, and that material stops pistol rounds and
    /// fragments — no class number changes what it is made of. The heaviest one in the
    /// game is 1.8 kg, about 14 mm of laminate over a face; a rifle-proof visor would be
    /// four or five times that and nobody has ever worn one. If this table ever hands
    /// glass a plate-like thickness, the game gets a face shield that eats rifle rounds
    /// because a number said so.
    /// </summary>
    [Fact]
    public void Ballistic_glass_never_gets_a_rifle_plate_thickness()
    {
        foreach (var (key, plate) in Shipped().ArmorByClass.Where(kv => kv.Key.StartsWith("Glass/")))
        {
            Assert.InRange(plate.ThicknessMm, 2, 15);
            Assert.True(int.Parse(key.Split('/')[1]) <= 4,
                $"{key}: the game has no glass above class 4, and reality has none above 2");
        }
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
