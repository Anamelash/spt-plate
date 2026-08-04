namespace PLATE.Server.Tests;

/// <summary>
/// What a certificate actually demands of a V50.
///
/// "V50 ≥ V_test" is the wrong criterion at its root: V50 is the velocity of a coin
/// toss, and a plate whose V50 equals the test velocity fails a real protocol half
/// the time on the first shot. A certificate requires ZERO penetrations out of N
/// shots, so the plate's median limit has to sit above the test velocity by a margin
/// that depends on how many chances the protocol gives it to fail:
///
///     P_pen  = 1 − P_pass^(1/N)
///     V50_req = V_test · (1 + Z(P_pen) · CV)
///
/// Z is the standard normal quantile and CV the shot-to-shot variation of the limit.
///
/// Two numbers here are OURS, marked as assumptions the way the fixture marks every
/// assumption, and they are parameters of the criterion, not of any physics:
///
///  - P_pass = 0.95 — the probability a genuinely compliant plate survives the whole
///    protocol. The standards do not state one; certification labs live off retests.
///  - Zero penetrations out of five as the GOST reading. ГОСТ Р 55623-2013 defines
///    what counts as a зачётный выстрел (п. 4.3.1.2: at least five from rifled arms,
///    at least two from smoothbore) but never states an allowed number of
///    penetrations; the class decision belongs to ГОСТ Р 50744. Zero-of-five is our
///    working interpretation, not a quotation.
/// </summary>
public static class CertificationCriteria
{
    /// <summary>
    /// Shot-to-shot coefficient of variation of a plate's ballistic limit — material
    /// quality scatter, typical of published V50 statistics for hard armour.
    /// </summary>
    public const double BallisticCV = 0.04;

    /// <summary>Probability a compliant plate survives its whole protocol. Ours.</summary>
    public const double TargetPassProbability = 0.95;

    /// <summary>
    /// Shots the protocol fires at one plate. The sources, one per line:
    ///  - GOST rifled: 5 — ГОСТ Р 55623-2013 п. 4.3.1.2, не менее пяти зачётных
    ///    выстрелов из нарезного оружия. Multiplier 1.093.
    ///  - GOST smoothbore: 2 — same clause. Multiplier 1.078. (No smoothbore class is
    ///    in the fixture today; the row exists so nobody reinvents it wrong.)
    ///  - NIJ 0101.06 Level III: 6 shots standalone. Multiplier 1.096.
    ///  - NIJ 0101.06 Level IV: 1 shot. Multiplier 1.066.
    ///  - NIJ 0101.07 RF classes: read at the six-shot protocol, same as III.
    /// </summary>
    public static int ProtocolShots(string standard, string cls)
    {
        if (standard == "GOST")
        {
            return 5;
        }

        return cls == "IV" ? 1 : 6;
    }

    /// <summary>The multiplier a protocol demands of V50 over its test velocity.</summary>
    public static double RequiredV50Multiplier(int shots)
    {
        if (shots <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shots),
                "a protocol with no shots certifies nothing");
        }

        var pPen = 1.0 - Math.Pow(TargetPassProbability, 1.0 / shots);
        return 1.0 + ZScore(pPen) * BallisticCV;
    }

    public static double RequiredV50(string standard, string cls, double testVelocity)
        => testVelocity * RequiredV50Multiplier(ProtocolShots(standard, cls));

    /// <summary>
    /// Standard normal quantile of the UPPER tail probability, by the Hastings
    /// approximation (error under 4.5e-4 — far inside anything armour physics can
    /// resolve). Probabilities at or above one half need no margin at all.
    /// </summary>
    private static double ZScore(double probability)
    {
        if (probability is <= 0.0 or >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        if (probability >= 0.5)
        {
            return 0.0;
        }

        var t = Math.Sqrt(-2.0 * Math.Log(probability));
        var numerator = 2.515517 + 0.802853 * t + 0.010328 * t * t;
        var denominator = 1.0 + 1.432788 * t + 0.189269 * t * t + 0.001308 * t * t * t;
        return t - numerator / denominator;
    }
}
