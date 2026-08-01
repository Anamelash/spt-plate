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
            Assert.True(b.RefMm > 50 && b.RefMm < 900,
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
