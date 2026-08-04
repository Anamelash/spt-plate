using System; // the client compiles this file without implicit usings

namespace PLATE.Server.Services;

/// <summary>
/// What the channel is actually shaped like.
///
/// The model used to say a wound channel has one cross-section from end to end, and
/// that one number covered two different things at once: a hollow point physically
/// opening up, and a full metal jacket not opening at all but eventually lying sideways.
/// That is why a jacketed round's expansiveness index was never zero — 0.25 to 0.30 was
/// not "it expands a little", it was "sooner or later it goes broadside".
///
/// Split here. Expansion belongs to the projectile and works from the moment of entry.
/// Tumbling is a function of how far it has travelled: the channel is narrow for the
/// first stretch and wide after it. Where the turn happens is the most variable thing
/// in wound ballistics — in gelatin, one cartridge's neck length varies twofold — but
/// the width it turns INTO is geometry, and geometry is what is here. The randomness
/// lives on the client, where a shot has an identity to hang it on.
///
/// Shared source: this file is compiled into the client as well, so the damage a card
/// quotes and the damage a raid deals come out of the same arithmetic.
/// </summary>
public static class YawModel
{
    /// <summary>Constants of the broadside geometry, from the ammo normalizer config.</summary>
    public readonly struct Tuning
    {
        public Tuning(double expansionAreaFactor, double neckCalibres,
            double broadsideFraction, double densityGPerCm3, double formFactor)
        {
            ExpansionAreaFactor = expansionAreaFactor;
            NeckCalibres = neckCalibres;
            BroadsideFraction = broadsideFraction;
            DensityGPerCm3 = densityGPerCm3;
            FormFactor = formFactor;
        }

        /// <summary>How much expansion widens the nose: A·(1 + this·X).</summary>
        public double ExpansionAreaFactor { get; }

        /// <summary>Median travel before the turn, in calibres.</summary>
        public double NeckCalibres { get; }

        /// <summary>Share of full broadside a tumbling projectile presents on average.</summary>
        public double BroadsideFraction { get; }

        /// <summary>Mean density of a jacketed bullet, g/cm³ — lead core in a copper jacket.</summary>
        public double DensityGPerCm3 { get; }

        /// <summary>How much of its bounding cylinder a bullet fills: ogive nose, boat tail.</summary>
        public double FormFactor { get; }
    }

    /// <summary>Frontal area of the calibre, mm².</summary>
    public static double CalibreAreaMm2(double diaMm)
    {
        return Math.PI * diaMm * diaMm / 4.0;
    }

    /// <summary>
    /// The area the projectile cuts with before it turns — its own frontal area, opened
    /// up by however much of it can deform. At X = 1 this is 2.35 times the calibre, so
    /// 1.53 times the diameter, against the .55-.60 inch a real expanding .355 opens to.
    /// </summary>
    public static double NoseAreaMm2(double diaMm, double x, double expansionAreaFactor)
    {
        // no Math.Clamp: the client half of this file compiles against net471
        return CalibreAreaMm2(diaMm) * (1 + expansionAreaFactor * Math.Max(0, Math.Min(1, x)));
    }

    /// <summary>
    /// Length of the projectile, mm, from the one thing that is always known about it:
    /// how much mass sits behind its calibre. The equivalent cylinder is mass over
    /// density over frontal area, and a real bullet fills roughly two thirds of its
    /// bounding cylinder — the rest is ogive and boat tail. 7.62x51 M80 comes out at
    /// 28.8 mm against a measured 28.9 and 5.56x45 M855 at 23.0 against a measured 23.0.
    ///
    /// Known limit: one density for every bullet. A mild-steel core is lighter than lead
    /// for the same volume, so 5.45x39 7N6 reads 20.4 mm against a measured 24.8 and is
    /// that much narrower once it turns. Fixing it properly means a density per
    /// construction, which is a reference-book question rather than a geometry one.
    /// </summary>
    public static double LengthMm(double massG, double diaMm, in Tuning t)
    {
        var area = CalibreAreaMm2(diaMm);
        var densityGPerMm3 = t.DensityGPerCm3 / 1000.0;
        var denom = area * densityGPerMm3 * t.FormFactor;
        return denom > 1e-9 ? massG / denom : 0;
    }

    /// <summary>
    /// The area it cuts with once it has turned. Never less than the nose: a fully
    /// expanded hollow point is short and blunt and has nothing wider to turn into, and
    /// a round ball comes out the same area whichever way it faces — the geometry says
    /// so by itself, without a rule about shot.
    /// </summary>
    public static double SideAreaMm2(double massG, double diaMm, double x, in Tuning t)
    {
        var broadside = LengthMm(massG, diaMm, t) * diaMm * t.BroadsideFraction;
        return Math.Max(broadside, NoseAreaMm2(diaMm, x, t.ExpansionAreaFactor));
    }

    /// <summary>
    /// Median travel before the turn. Published gelatin necks run from about twelve
    /// calibres for 5.45x39 to well over thirty for 7.62x39, and one constant in the
    /// middle of that is as far as the model can honestly go without a neck length
    /// measured per cartridge.
    /// </summary>
    public static double MedianNeckMm(double diaMm, double neckCalibres)
    {
        return Math.Max(diaMm * neckCalibres, 0);
    }

    /// <summary>
    /// Volume of the permanent cavity over a path, mm³: narrow up to the turn, wide
    /// after it. This is where "the same round behaves differently in an arm and in a
    /// chest" comes from — not from a die, but from the channel being a different length.
    /// </summary>
    public static double CavityVolumeMm3(double noseAreaMm2, double sideAreaMm2,
        double neckMm, double pathMm)
    {
        if (pathMm <= 0)
        {
            return 0;
        }

        var straight = Math.Min(pathMm, Math.Max(neckMm, 0));
        return noseAreaMm2 * straight + sideAreaMm2 * (pathMm - straight);
    }
}
