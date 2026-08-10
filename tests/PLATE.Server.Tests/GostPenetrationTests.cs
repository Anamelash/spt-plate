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

    private static (BallisticLimit.Barrier Barrier, double ThicknessMm) Plate(
        string material, int gameClass) => ArmorFixture.ByClass(material, gameClass);

    private static BallisticLimit.Core CoreOf(ArmorStandardTests.Threat t) =>
        ArmorFixture.CoreOf(t);

    /// <summary>Perpendicular hit, undamaged plate — the conditions the standard tests at.</summary>
    private static double V50(string material, int gameClass, ArmorStandardTests.Threat t)
    {
        var (barrier, _) = Plate(material, gameClass);
        return ArmorFixture.V50(barrier, t);
    }

    /// <summary>
    /// Strict, the way a certificate is strict: zero penetrations out of five shots,
    /// which puts the required V50 about 9% above the test velocity rather than at it
    /// — a plate whose V50 equals the test velocity fails a real protocol half the
    /// time on the first shot. See CertificationCriteria for the derivation and for
    /// which parts of it are our assumptions.
    ///
    /// Since the class rungs stopped being thicknesses solved from the class, this
    /// test stopped being a tautology. For a rung that names a representative product
    /// it now measures that real plate against the standard — and inherits the
    /// product's recorded shortfall, because the rung IS the product. Rungs still
    /// computed as a last resort are solved under this very criterion, so for them
    /// the test only guards drift; their Source says so.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClassAndMaterial))]
    public void A_plate_of_the_class_stops_what_the_class_is_certified_against(
        string cls, string material)
    {
        var gameClass = GameClass(cls);
        var shortfallKey = ArmorFixture.ClassRepresentative(material, gameClass)
                           ?? $"{material}/{gameClass}";
        var reaches = ArmorStandardTests.CertShortfalls.TryGetValue(shortfallKey, out var s)
            ? s.Reaches
            : 1.0;

        foreach (var t in ArmorStandardTests.Gost.Where(t => t.Class == cls))
        {
            var v50 = V50(material, gameClass, t);
            var (_, thickness) = Plate(material, gameClass);
            var required = CertificationCriteria.RequiredV50("GOST", cls, t.V);

            Assert.True(v50 >= required * reaches,
                $"{cls} {material} {thickness:N1} mm turns {t.Cartridge} back only up to " +
                $"{v50:N0} m/s; the standard fires it at {t.V:N0}, zero-of-five " +
                $"demands {required:N0} and the recorded shortfall allows no less " +
                $"than {required * reaches:N0}. " +
                "If this rung's Source says it was COMPUTED, the answer is to re-solve " +
                $"its thickness — {thickness * required * reaches / v50:N2} mm closes it — " +
                "because the number was solved under this very criterion and an input to " +
                "it has moved. Do NOT reach for the material's strength or the model's " +
                "constants: a computed rung falling short is a stale number, not weak " +
                "steel. For a rung that names a real product, the opposite holds — the " +
                "thickness is published and it is the physics that has to answer for it");
        }
    }

    public static TheoryData<int> VestGateIndices()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < ArmorStandardTests.VestGates.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    /// <summary>
    /// The vest as a whole — plate plus the fabric it sits in — against what its maker
    /// says happens. The bare-plate tests above ask a question no certificate answers;
    /// this one asks the certificate's own.
    ///
    /// Both directions are asserted. "Holds" uses the firing velocity rather than the
    /// zero-of-five margin, because a passport sentence is a statement about the round
    /// being stopped, not about a five-shot protocol; the strict criterion has its own
    /// tests. "Pierces" is the half that keeps the model honest in the other direction,
    /// and there is no margin to give it: if the maker says the SVD goes through, a V50
    /// above the firing velocity is simply wrong.
    /// </summary>
    [Theory]
    [MemberData(nameof(VestGateIndices))]
    public void A_vest_does_what_its_passport_says(int index)
    {
        var g = ArmorStandardTests.VestGates[index];
        var (barrier, thickness) = ArmorFixture.ByProduct(g.PlateKey);

        // the vest's fabric screen, stated here because the game models it as its own item
        // and so the plate entry carries none. Sewn, therefore mostly air — the same rule
        // ArmorFixture applies to a backing the book does declare.
        var aramid = ReferenceBookTests.ShippedBook().ArmorMaterials["Aramid"];
        barrier.BackingMm = g.ScreenMm;
        barrier.BackingTensileMPa = aramid.FibreTensileMPa;
        barrier.BackingStrain = aramid.FailureStrain;
        barrier.BackingPacked = BallisticLimit.SewnPacked;

        var v50 = ArmorFixture.V50(barrier, g.Round);
        var held = v50 >= g.Round.V;

        Assert.True(held == g.MustHold,
            $"{g.Vest}: {thickness:N1} mm plate + {g.ScreenMm:N1} mm screen versus " +
            $"{g.Round.Cartridge} at {g.Round.V:N0} m/s reads V50 {v50:N0}, so the model " +
            $"says it {(held ? "holds" : "is pierced")} — the maker says it " +
            $"{(g.MustHold ? "holds" : "is pierced")}. Source: {g.Source}");
    }

    public static TheoryData<string> Vest6B23Rounds()
    {
        var data = new TheoryData<string>();
        foreach (var t in ArmorStandardTests.Vest6B23)
        {
            data.Add(t.Cartridge);
        }

        return data;
    }

    /// <summary>
    /// The 6B23 steel panel against the six rounds its maker names, one test per round so
    /// that a failure says which one. This is the corpus's densest anchor — one plate, one
    /// alloy, one thickness, six cartridges — and it is the only place where the model is
    /// asked the same question the certificate answers, rather than a class-shaped
    /// paraphrase of it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Vest6B23Rounds))]
    public void The_6B23_panel_stops_what_its_maker_says_it_stops(string cartridge)
    {
        var t = ArmorStandardTests.Vest6B23.Single(x => x.Cartridge == cartridge);
        var (barrier, thickness) = ArmorFixture.ByProduct("korund_back_6b23_2");

        var reaches = ArmorStandardTests.Vest6B23Shortfalls.TryGetValue(cartridge, out var s)
            ? s.Reaches
            : 1.0;

        var v50 = ArmorFixture.V50(barrier, t);
        var required = CertificationCriteria.RequiredV50("GOST", "Бр4", t.V);

        Assert.True(v50 >= required * reaches,
            $"{thickness:N1} mm of 44S turns {cartridge} ({t.Source}) back only up to " +
            $"{v50:N0} m/s; it is fired at {t.V:N0}, zero-of-five demands {required:N0} " +
            $"and the recorded shortfall allows no less than {required * reaches:N0}. " +
            "The panel's thickness and alloy are both published, so this is the physics " +
            "answering for itself — not a number to be re-solved");
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

    private static bool Exists(string material, int gameClass) =>
        ArmorFixture.ClassExists(material, gameClass);

    // --- The plates that actually exist, rather than the ladder's idea of them ---

    public static TheoryData<string> RussianPlates()
    {
        var data = new TheoryData<string>();
        foreach (var c in ArmorStandardTests.Certified.Where(c => c.Standard == "GOST"))
        {
            data.Add(c.BookKey);
        }

        return data;
    }

    private static ArmorStandardTests.Certificate Cert(string bookKey) =>
        ArmorStandardTests.Certified.Single(c => c.BookKey == bookKey);

    /// <summary>
    /// Every Russian plate in the book with a published class, against every cartridge
    /// GOST fires at that class.
    ///
    /// The ladder test above asks whether the model's own idea of a class holds together.
    /// This one asks the harder question: a real plate of a real thickness, certified by
    /// the people who made it — does the model let it do what it is sold to do. A class
    /// rung can always be thickened until it passes; 6 mm of Granit ceramic cannot.
    /// </summary>
    [Theory]
    [MemberData(nameof(RussianPlates))]
    public void A_certified_russian_plate_stops_its_own_class(string bookKey)
    {
        var cert = Cert(bookKey);
        var (barrier, thickness) = ArmorFixture.ByProduct(bookKey);
        var threats = ArmorFixture.Threats("GOST", cert.Class);

        Assert.NotEmpty(threats);
        var reaches = ArmorStandardTests.CertShortfalls.TryGetValue(bookKey, out var s)
            ? s.Reaches
            : 1.0;
        foreach (var t in threats)
        {
            var v50 = ArmorFixture.V50(barrier, t);
            var required = CertificationCriteria.RequiredV50("GOST", cert.Class, t.V);
            Assert.True(v50 >= required * reaches,
                $"{bookKey} ({cert.Note}) is {thickness:N1} mm of {barrier.Class} and turns " +
                $"{t.Cartridge} back only up to {v50:N0} m/s, where {cert.Class} is " +
                $"certified at {t.V:N0}, zero-of-five demands {required:N0} and the " +
                $"recorded shortfall allows no less than {required * reaches:N0}");
        }
    }

    /// <summary>
    /// And is beaten by the class above it, or its certificate is worth nothing — a plate
    /// that stops everything is not a Бр4 plate, it is a bug.
    /// </summary>
    [Theory]
    [MemberData(nameof(RussianPlates))]
    public void A_certified_russian_plate_is_beaten_by_the_class_above(string bookKey)
    {
        var cert = Cert(bookKey);
        if (cert.Class == "Бр5")
        {
            return; // the top of the ladder the game has a class for
        }

        var above = Above(cert.Class);
        var (barrier, thickness) = ArmorFixture.ByProduct(bookKey);
        var threats = ArmorFixture.Threats("GOST", above);

        var through = threats.Where(t => ArmorFixture.V50(barrier, t) < t.V).ToArray();
        Assert.True(through.Length > 0,
            $"{bookKey} is sold as {cert.Class} and stops every cartridge of {above} too — " +
            string.Join(", ", threats.Select(t =>
                $"{t.Cartridge} to {ArmorFixture.V50(barrier, t):N0} against {t.V:N0}")) +
            $" — so {thickness:N1} mm of {barrier.Class} is being read a class too strong");
    }

    /// <summary>
    /// The boundary of the certified list, asserted rather than left in a comment.
    ///
    /// Every panel named here exists in the book with a real thickness and a real
    /// material, and is deliberately NOT being fired at, because our references give its
    /// protection layout or a bare number from the 1995 scale instead of a class in the
    /// terms this fixture tests in. If one of them ever gains a class, this goes red and
    /// the panel moves up into Certified where it belongs.
    /// </summary>
    [Fact]
    public void The_panels_we_cannot_class_are_named_and_still_in_the_book()
    {
        var book = ReferenceBookTests.ShippedBook();
        var certified = ArmorStandardTests.Certified.Select(c => c.BookKey).ToHashSet();

        foreach (var (key, product, what) in ArmorStandardTests.Unpinned)
        {
            Assert.True(book.ArmorPlates.ContainsKey(key),
                $"{product} is listed as unclassed but is not in the book at all");
            Assert.DoesNotContain(key, certified);
            Assert.False(string.IsNullOrWhiteSpace(what));
        }
    }

    private static string Above(string cls) => cls switch
    {
        "Бр1" => "Бр2",
        "Бр2" => "Бр3",
        "Бр3" => "Бр4",
        "Бр4" => "Бр5",
        _ => throw new ArgumentOutOfRangeException(nameof(cls), cls, "nothing above it"),
    };

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

        var v50 = BallisticLimit.V50(barrier, core, 1.0, t.V, tuning);
        var plug = BallisticLimit.PlugMassG(barrier, core, 1.0, tuning);
        var vr = BallisticLimit.ResidualVelocity(t.V, v50, core.MassG, plug);

        Assert.True(v50 < t.V, "the setup wants a plate this round beats");
        Assert.True(vr > 0 && vr < t.V, $"residual {vr:N0} m/s out of {t.V:N0}");

        var before = 0.5 * (core.MassG / 1000) * t.V * t.V;
        var after = 0.5 * (core.MassG / 1000) * vr * vr;
        Assert.True(after < before * 0.9, "a plate it barely beats should still cost it dearly");
    }

    /// <summary>
    /// Writing down what a bullet is made of must never turn it into something the game
    /// cannot fire. A construction is information, and the guard against reading it
    /// wrong is that the described round stays in the same world as the undescribed
    /// one — not that it always comes out stronger.
    ///
    /// **That second half used to be the test, and a measurement took it away.** It read
    /// "knowing a construction must never cost a round penetration", which held only
    /// because the model gave a hardened core the whole bullet's mass to carry. Forrestal
    /// shot 20 mm of aluminium with complete APM2 bullets and with their stripped cores
    /// and got the same limit either way, so the jacket does not carry the core through
    /// a metal plate — and once the model says so, describing a 7N10 as its 1.7 g core
    /// DOES make it easier to stop than reading it as 3.5 g of full-calibre lead, by
    /// about a fifth. That is not the construction being read as a handicap; it is the
    /// undescribed reading over-crediting a bullet with mass its core never delivers.
    ///
    /// What survives is the check that caught the real bug: an M855, whose "core" is a
    /// 0.65 g tip riding on a lead body, once came out as a 0.65 g projectile at full
    /// calibre and met a titanium plate at a ballistic limit of 2847 m/s. Nothing in the
    /// game leaves a barrel above 1220, so that plate was not armour, it was a wall. A
    /// factor of two either way is physics; a factor of three is a category error.
    /// </summary>
    [Theory]
    [InlineData("ArmoredSteel", 5)]
    [InlineData("Ceramic", 5)]
    [InlineData("Titan", 5)]
    [InlineData("UHMWPE", 5)]
    [InlineData("Combined", 5)]
    public void Knowing_a_bullets_construction_keeps_it_in_the_same_world(string material,
        int gameClass)
    {
        var tuning = BallisticLimit.Tuning.Default;
        var (barrier, thickness) = Plate(material, gameClass);

        foreach (var t in ArmorStandardTests.All)
        {
            var known = BallisticLimit.V50(barrier, CoreOf(t), 1.0, t.V, tuning);
            var blind = BallisticLimit.V50(barrier,
                BallisticLimit.Driving(t.MassG, t.DiaMm, 1, 1, t.CoreHardnessHv, tuning),
                1.0, t.V, tuning);

            Assert.InRange(known / blind, 0.5, 2.0);
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

        var square = BallisticLimit.V50(barrier, core, 1.0, t.V, tuning);
        var glancing = BallisticLimit.V50(barrier, core, 0.4, t.V, tuning);

        Assert.True(glancing > square * 2, $"{square:N0} head-on against {glancing:N0} at 66°");
    }
}
