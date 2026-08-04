using PLATE.Server.Services;
using Xunit;

namespace PLATE.Server.Tests;

/// <summary>
/// The one comparison in the whole fixture that varies the core while holding the
/// plate fixed — which is what makes the hardness term identifiable at all.
///
/// This file used to hold the cross-standard tests, which fired each standard's
/// cartridges at the other standard's plates through the published GOST/NIJ crosswalk.
/// They are gone on purpose: the standards test with different ammunition, and the
/// crosswalk maps certificates, not physics. A green test built on a false premise is a
/// deferred red one. Each standard is now checked only with its own documented rounds
/// (ArmorStandardTests, GostPenetrationTests, NijPenetrationTests).
///
/// This test survived the deletion because it never used the crosswalk: it takes the
/// NIJ III and IV threats directly and asks a question both standards agree on.
/// </summary>
public class HardnessSeparationTests
{
    public static TheoryData<string> Certified()
    {
        var data = new TheoryData<string>();
        foreach (var c in ArmorStandardTests.Certified)
        {
            data.Add(c.BookKey);
        }

        return data;
    }

    /// <summary>
    /// Both standards agree that a hardened core is the harder thing to stop. If the
    /// model has that backwards on a real plate, no amount of per-class thickness
    /// fitting will save it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Certified))]
    public void A_hardened_core_is_harder_to_stop_than_a_lead_one(string bookKey)
    {
        var (barrier, thickness) = ArmorFixture.ByProduct(bookKey);

        var lead = ArmorFixture.Threats("NIJ", "III").Single();        // M80, lead core
        var hardened = ArmorFixture.Threats("NIJ", "IV").Single();     // .30-06 AP

        var vLead = ArmorFixture.V50(barrier, lead);
        var vHard = ArmorFixture.V50(barrier, hardened);

        Assert.True(vHard < vLead,
            $"{bookKey} at {thickness:N1} mm of {barrier.Class} turns the hardened core " +
            $"back to {vHard:N0} m/s and the lead one to only {vLead:N0} — the model has " +
            "which core is the threat backwards");
    }
}
