using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// GOST R 50744-95, simulated shot by shot.
///
/// A protection class is a two-sided statement and both sides have to hold. An item of
/// class C stops the cartridge class C is certified against — and it is only class C,
/// not class C+1, because the plate one rung down lets that same cartridge through. A
/// model that satisfies only the first half can be made to satisfy it by turning every
/// threshold up until nothing penetrates anything.
///
/// The plate under fire is the reference plate the model itself would reach for: the
/// entry in ArmorByClass or SoftArmor for that material and that class, read from the
/// shipped book. So this is not a test of an idealised plate — it is a test of the
/// numbers a player's armour will actually be resolved to.
///
/// Ammunition comes from ArmorStandardTests, where the standard's own table lives.
/// </summary>
public class GostPenetrationTests
{
    /// <summary>
    /// In-game class 1 is anti-fragment junk below every standard, so GOST Бр1..Бр5 are
    /// in-game 2..6 and the rung below Бр1 is in-game 1.
    /// </summary>
    private static int GameClass(string cls) => cls switch
    {
        "Бр1" => 2,
        "Бр2" => 3,
        "Бр3" => 4,
        "Бр4" => 5,
        "Бр5" => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(cls), cls, "not a GOST class"),
    };

    /// <summary>
    /// Materials a plate of a given class is actually made of. Steel spans the whole
    /// ladder and is the only material with a published V50 ladder behind it, so it is
    /// the one every class is fired at. The others join where real armour uses them:
    /// nobody makes a Бр5 aramid pack or a Бр1 ceramic plate.
    /// </summary>
    public static TheoryData<string, string> ClassAndMaterial()
    {
        var data = new TheoryData<string, string>();
        foreach (var cls in new[] { "Бр1", "Бр2", "Бр3", "Бр4", "Бр5" })
        {
            data.Add(cls, "ArmoredSteel");
        }

        // no rifle plate is sold against a pistol class, and firing a 9x19 at a ceramic
        // one measures nothing except that ceramic is very good at stopping pistols
        foreach (var cls in new[] { "Бр4", "Бр5" })
        {
            data.Add(cls, "Ceramic");
            data.Add(cls, "Titan");
            data.Add(cls, "Combined");
            data.Add(cls, "UHMWPE");
        }

        return data;
    }

    /// <summary>
    /// The plate the model resolves an item of this material and class to, and the
    /// physics of what it is made of. Fibre reads out of SoftArmor, which is what a
    /// vest package is; everything rigid reads out of ArmorByClass.
    /// </summary>
    private static (BallisticLimit.Barrier Barrier, double ThicknessMm) Plate(
        string material, int gameClass)
    {
        var book = ReferenceBookTests.ShippedBook();
        var physics = book.ArmorMaterials[material];

        // Fibre is the one material that comes both ways, so the item decides and not
        // the material: an aramid vest package reads out of SoftArmor and is sold as Бр1
        // or Бр2, a pressed polyethylene plate reads out of ArmorByClass like any other
        // plate. Everything rigid reads out of ArmorByClass.
        var sewn = material == "Aramid";
        var table = sewn ? book.SoftArmor : book.ArmorByClass;
        var rung = sewn ? Math.Min(gameClass, 2) : gameClass;
        var entry = table[$"{material}/{rung}"];

        // only the fibre in a package does any work, and a sewn one is mostly air
        var density = entry.DensityGCm3 > 0 ? entry.DensityGCm3 : physics.DensityGCm3;

        return (new BallisticLimit.Barrier
        {
            Class = physics.Class,
            ThicknessMm = entry.ThicknessMm,
            ShearMPa = physics.ShearMPa,
            CompressiveMPa = physics.CompressiveMPa,
            FibreTensileMPa = physics.FibreTensileMPa,
            FailureStrain = physics.FailureStrain,
            HardnessHv = physics.HardnessHv,
            DensityGCm3 = density,
            PackedFraction = physics.DensityGCm3 > 0 ? density / physics.DensityGCm3 : 1,
        }, entry.ThicknessMm);
    }

    private static BallisticLimit.Core CoreOf(ArmorStandardTests.Threat t)
    {
        return BallisticLimit.Driving(t.MassG, t.DiaMm, t.CoreAreaFrac, t.CoreMassFrac,
            t.CoreHardnessHv);
    }

    /// <summary>Perpendicular hit, undamaged plate — the conditions the standard tests at.</summary>
    private static double V50(string material, int gameClass, ArmorStandardTests.Threat t)
    {
        var (barrier, _) = Plate(material, gameClass);
        return BallisticLimit.V50(barrier, CoreOf(t), 1.0, BallisticLimit.Tuning.Default);
    }

    [Theory]
    [MemberData(nameof(ClassAndMaterial))]
    public void A_plate_of_the_class_stops_what_the_class_is_certified_against(
        string cls, string material)
    {
        var gameClass = GameClass(cls);
        foreach (var t in ArmorStandardTests.Gost.Where(t => t.Class == cls))
        {
            var v50 = V50(material, gameClass, t);
            var (_, thickness) = Plate(material, gameClass);

            Assert.True(v50 >= t.V,
                $"{cls} {material} {thickness:N1} mm turns {t.Cartridge} back only up to " +
                $"{v50:N0} m/s, and the standard fires it at {t.V:N0}");
        }
    }

    /// <summary>
    /// The other half of the promise, and the half that stops the ladder from being
    /// fixed by making every rung thicker until nothing gets through anything.
    ///
    /// Failing a class means failing ANY of its cartridges, which is how certification
    /// works and not a softening of the test: a class with two test rounds is not passed
    /// on average. It matters here because it is true of real armour too. Twenty-five
    /// millimetres of polyethylene shrugs off the 7.62x39 that GOST fires at Бр4 — a
    /// mild steel core is the wrong tool against fibre — and is beaten by the 5.45 in
    /// the same class, whose small hard core is the right one. Requiring both would be
    /// requiring the model to be wrong about polyethylene.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClassAndMaterial))]
    public void A_plate_one_class_down_lets_it_through(string cls, string material)
    {
        var below = GameClass(cls) - 1;
        var threats = ArmorStandardTests.Gost.Where(t => t.Class == cls).ToArray();

        if (!Exists(material, below))
        {
            // nothing of that class is made at all, which is a stronger statement than
            // the one being tested — GOST Бр1 is the bottom of the ladder
            Assert.Equal(1, below);
            return;
        }

        var (_, thickness) = Plate(material, below);
        var through = threats
            .Select(t => (t, V50: V50(material, below, t)))
            .Where(x => x.V50 < x.t.V)
            .ToArray();

        Assert.True(through.Length > 0,
            $"{cls} {material}: the class below it is {thickness:N1} mm and stops every " +
            "cartridge the class above is tested with — " +
            string.Join(", ", threats.Select(t =>
                $"{t.Cartridge} to {V50(material, below, t):N0} against {t.V:N0}")) +
            " — so the two classes are not distinguishable");
    }

    private static bool Exists(string material, int gameClass)
    {
        var book = ReferenceBookTests.ShippedBook();
        var sewn = material == "Aramid";
        var rung = sewn ? Math.Min(gameClass, 2) : gameClass;
        return (sewn ? book.SoftArmor : book.ArmorByClass).ContainsKey($"{material}/{rung}");
    }

    /// <summary>
    /// What is left of the round on the far side. Recht-Ipson, and the energy the plate
    /// took is not a constant any more: it is whatever ½m(v² − v_r²) comes to.
    /// </summary>
    [Fact]
    public void A_round_well_over_the_limit_comes_through_slower_and_the_plate_keeps_the_difference()
    {
        var t = ArmorStandardTests.Gost.Single(x => x.Cartridge.Contains("7N10"));
        var (barrier, _) = Plate("ArmoredSteel", GameClass("Бр3")); // one class under it
        var core = CoreOf(t);
        var tuning = BallisticLimit.Tuning.Default;

        var v50 = BallisticLimit.V50(barrier, core, 1.0, tuning);
        var plug = BallisticLimit.PlugMassG(barrier, core, 1.0, tuning);
        var vr = BallisticLimit.ResidualVelocity(t.V, v50, core.MassG, plug);

        Assert.True(v50 < t.V, "the setup wants a plate this round beats");
        Assert.True(vr > 0 && vr < t.V, $"residual {vr:N0} m/s out of {t.V:N0}");

        var before = 0.5 * (core.MassG / 1000) * t.V * t.V;
        var after = 0.5 * (core.MassG / 1000) * vr * vr;
        Assert.True(after < before * 0.9, "a plate it barely beats should still cost it dearly");
    }

    /// <summary>
    /// Writing down what a bullet is made of must never make it worse at getting through
    /// a plate than knowing nothing about it. A construction is information; if adding it
    /// costs the round penetration, the construction is being read wrong.
    ///
    /// This is the raid check GOST cannot make. Every cartridge in the standard has a
    /// full-length core, so reading the driving mass as the surviving mass was invisible
    /// to all seven of them. An M855, whose "core" is a 0.65 g tip riding on a lead body,
    /// came out of that reading as a 0.65 g projectile at full 5.7 mm calibre and met a
    /// titanium plate with a ballistic limit of 2847 m/s. Nothing in the game leaves a
    /// barrel above 1220, so it was not armour, it was a wall.
    /// </summary>
    [Theory]
    [InlineData("ArmoredSteel", 5)]
    [InlineData("Ceramic", 5)]
    [InlineData("Titan", 5)]
    [InlineData("UHMWPE", 5)]
    [InlineData("Combined", 5)]
    public void Knowing_a_bullets_construction_never_makes_it_weaker(string material, int gameClass)
    {
        var tuning = BallisticLimit.Tuning.Default;
        var (barrier, thickness) = Plate(material, gameClass);

        foreach (var t in ArmorStandardTests.All)
        {
            var known = BallisticLimit.V50(barrier, CoreOf(t), 1.0, tuning);
            var blind = BallisticLimit.V50(barrier,
                BallisticLimit.Driving(t.MassG, t.DiaMm, 1, 1, t.CoreHardnessHv), 1.0, tuning);

            Assert.True(known <= blind * 1.02,
                $"{material} at {thickness:N1} mm stops {t.Cartridge} up to {known:N0} m/s " +
                $"once its construction is known and only {blind:N0} without it — the " +
                "construction is being read as a handicap");
        }
    }

    [Fact]
    public void Below_the_limit_nothing_comes_through_at_all()
    {
        var t = ArmorStandardTests.Gost.Single(x => x.Cartridge.Contains("7N10"));
        var core = CoreOf(t);

        Assert.Equal(0, BallisticLimit.ResidualVelocity(t.V, t.V + 1, core.MassG, 1.0));
        Assert.Equal(0, BallisticLimit.ResidualVelocity(t.V, t.V, core.MassG, 1.0));
    }

    /// <summary>
    /// The plug is the reason a plate costs more than the hole in it: the disc punched
    /// out of a steel plate leaves with the core and has to be accelerated too. A fibre
    /// pack has no disc to give.
    /// </summary>
    [Fact]
    public void A_steel_plate_hands_over_a_plug_and_a_fibre_pack_does_not()
    {
        var t = ArmorStandardTests.Gost.Single(x => x.Cartridge.Contains("7N10"));
        var core = CoreOf(t);
        var tuning = BallisticLimit.Tuning.Default;

        var (steel, _) = Plate("ArmoredSteel", 5);
        var (fibre, _) = Plate("Aramid", 2);

        Assert.True(BallisticLimit.PlugMassG(steel, core, 1.0, tuning) > 0.3);
        Assert.Equal(0, BallisticLimit.PlugMassG(fibre, core, 1.0, tuning));
    }

    /// <summary>
    /// An oblique hit crosses more material. At the shallowest angle the model reads,
    /// a plate is worth about three times what it is worth head-on.
    /// </summary>
    [Fact]
    public void A_slanted_plate_is_a_thicker_plate()
    {
        var t = ArmorStandardTests.Gost.Single(x => x.Cartridge.Contains("7N10"));
        var (barrier, _) = Plate("ArmoredSteel", 5);
        var core = CoreOf(t);
        var tuning = BallisticLimit.Tuning.Default;

        var square = BallisticLimit.V50(barrier, core, 1.0, tuning);
        var glancing = BallisticLimit.V50(barrier, core, 0.4, tuning);

        Assert.True(glancing > square * 2, $"{square:N0} head-on against {glancing:N0} at 66°");
    }
}
