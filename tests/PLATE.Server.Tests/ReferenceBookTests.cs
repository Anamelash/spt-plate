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

    [Fact]
    public void Integral_barrel_weapons_have_plausible_lengths()
    {
        foreach (var (name, w) in Shipped().Weapons)
        {
            Assert.InRange(w.LengthMm, 50, 600);
            Assert.False(string.IsNullOrWhiteSpace(w.Prototype), $"{name} has no prototype named");
        }
    }
}
