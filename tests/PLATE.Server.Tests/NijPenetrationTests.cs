using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// NIJ 0101.06 and 0101.07, simulated the same way as GOST.
///
/// The other half of the world's armour, and the reason for testing it separately rather
/// than trusting a crosswalk: the two standards fire different things. GOST's rifle
/// classes are built around hardened and mild steel cores out of Soviet calibres, NIJ's
/// around lead-cored M80 and a single .30-06 AP. A model tuned on one and never shown the
/// other can be wrong in a way neither standard alone reveals — which is precisely what
/// happened, and what these tests exist to stop happening again.
///
/// Plates are real products with published certificates, listed in
/// ArmorStandardTests.Certified.
/// </summary>
public class NijPenetrationTests
{
    public static TheoryData<string> WesternPlates()
    {
        var data = new TheoryData<string>();
        foreach (var c in ArmorStandardTests.Certified.Where(c => c.Standard == "NIJ"))
        {
            data.Add(c.BookKey);
        }

        return data;
    }

    private static ArmorStandardTests.Certificate Cert(string bookKey) =>
        ArmorStandardTests.Certified.Single(c => c.BookKey == bookKey);

    [Theory]
    [MemberData(nameof(WesternPlates))]
    public void A_certified_western_plate_stops_what_its_level_is_tested_with(string bookKey)
    {
        var cert = Cert(bookKey);
        var (barrier, thickness) = ArmorFixture.ByProduct(bookKey);
        var threats = ArmorFixture.Threats("NIJ", cert.Class);

        Assert.NotEmpty(threats);
        var reaches = ArmorStandardTests.CertShortfalls.TryGetValue(bookKey, out var s)
            ? s.Reaches
            : 1.0;
        foreach (var t in threats)
        {
            var v50 = ArmorFixture.V50(barrier, t);
            var required = CertificationCriteria.RequiredV50("NIJ", cert.Class, t.V);
            Assert.True(v50 >= required * reaches,
                $"{bookKey} ({cert.Note}) is {thickness:N1} mm of {barrier.Class} and turns " +
                $"{t.Cartridge} back only up to {v50:N0} m/s, where NIJ {cert.Class} is " +
                $"certified at {t.V:N0}, its protocol demands {required:N0} and the " +
                $"recorded shortfall allows no less than {required * reaches:N0}");
        }
    }

    /// <summary>
    /// The other side of the certificate, coarse on purpose. A maker certifies at the
    /// level the plate reaches, not two levels under it: a plate reading more than
    /// twice its test velocity is not conservative rating, it is the model mistaking
    /// the product — which is exactly what happened when a composite's full thickness
    /// was read as ceramic and the one plate with a published total came out three
    /// times its certificate. The factor 2 is deliberately loose: real plates do beat
    /// their certificates, just not by a class of physics.
    /// </summary>
    [Theory]
    [MemberData(nameof(WesternPlates))]
    public void A_western_plate_is_not_read_wildly_stronger_than_its_certificate(
        string bookKey)
    {
        var cert = Cert(bookKey);
        var (barrier, thickness) = ArmorFixture.ByProduct(bookKey);

        foreach (var t in ArmorFixture.Threats("NIJ", cert.Class))
        {
            var v50 = ArmorFixture.V50(barrier, t);
            Assert.True(v50 <= t.V * 2,
                $"{bookKey} ({cert.Note}) is {thickness:N1} mm of {barrier.Class} and " +
                $"turns {t.Cartridge} back up to {v50:N0} m/s against a test velocity " +
                $"of {t.V:N0} — more than twice the certificate is not a strong plate, " +
                "it is a misread product");
        }
    }

    /// <summary>
    /// A Level III plate is not a Level IV plate. The one shot of .30-06 AP is the whole
    /// difference between them, and a model that lets polyethylene shrug it off has
    /// collapsed two certificates into one.
    /// </summary>
    [Theory]
    [MemberData(nameof(WesternPlates))]
    public void A_level_three_plate_is_beaten_by_the_armour_piercing_round(string bookKey)
    {
        var cert = Cert(bookKey);
        if (cert.Class != "III")
        {
            return; // IV and RF2 are the ones that are meant to hold it
        }

        var (barrier, thickness) = ArmorFixture.ByProduct(bookKey);
        var ap = ArmorFixture.Threats("NIJ", "IV").Single();
        var v50 = ArmorFixture.V50(barrier, ap);

        Assert.True(v50 < ap.V,
            $"{bookKey} is certified Level III and {thickness:N1} mm of {barrier.Class} " +
            $"turns {ap.Cartridge} back to {v50:N0} m/s against the {ap.V:N0} Level IV is " +
            "tested at — the two levels are not distinguishable");
    }

    /// <summary>
    /// The polyethylene the standard caught out. Level III was certified on M80 alone;
    /// 0101.07 added 5.56 M193 at 990 m/s, and pressed polyethylene that passes the first
    /// has been failing the second ever since — a small light bullet arriving fast is the
    /// wrong threat for a material that works by catching and stretching.
    ///
    /// Not an assertion that they fail, because some do pass. An assertion that the model
    /// finds M193 harder for fibre than M80 is, which is the physics that makes the
    /// standard have had to be rewritten.
    /// </summary>
    [Fact]
    public void Fibre_finds_the_small_fast_bullet_harder_than_the_big_slow_one()
    {
        var m80 = ArmorFixture.Threats("NIJ", "RF1").Single(t => t.Cartridge.Contains("M80"));
        var m193 = ArmorFixture.Threats("NIJ", "RF1").Single(t => t.Cartridge.Contains("M193"));

        foreach (var key in new[] { "SAPI_GAC_3s15m", "SAPI_Monoclete_PE", "SAPI_SPRTN_Elaphros" })
        {
            var (barrier, thickness) = ArmorFixture.ByProduct(key);
            var marginM80 = ArmorFixture.V50(barrier, m80) / m80.V;
            var marginM193 = ArmorFixture.V50(barrier, m193) / m193.V;

            Assert.True(marginM193 < marginM80,
                $"{key} at {thickness:N1} mm holds M193 by {marginM193:P0} of its test " +
                $"velocity and M80 by only {marginM80:P0} — the model has the harder of " +
                "the two threats backwards for fibre");
        }
    }
}
