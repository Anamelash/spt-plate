using System;

namespace PLATE.Server.Services;

/// <summary>
/// The velocity at which a projectile just gets through a barrier, and what is left of
/// it when it does.
///
/// This replaces a threshold and a price that were independent constants. A specific
/// energy against a class number cannot express what a plate does, and GOST says so in
/// its own table: Бр4 names two cartridges that one plate must stop, and in J/mm² they
/// are 2.7x apart. The 5.45 lands harder per unit area and is the easier of the two to
/// stop, because a light fast core has little behind it. That is a fact about the core's
/// mass and diameter, and E/A cannot see it.
///
/// **The law.** Punching a hole means shearing the barrier around the projectile's
/// perimeter, through the barrier's own thickness:
///
///     W  = k · S · π · d · T²
///     v_bl = sqrt(2W / m) = T · sqrt(2 k S π d / m)
///
/// Linear in thickness, and falling with the square root of sectional density. Checked
/// against the rolled-armour ladder in ArmorStandardTests — six thicknesses from 6 to
/// 16 mm against 7.62 AP — it lands within 3% at every point on one constant. A T^0.75
/// power law of the Lambert-Jonas kind was 24% out at the thin end of the same data.
///
/// **What S is** depends on how the barrier fails, which is what ArmorMaterialRef.Class
/// says: a ductile metal shears, a ceramic crushes, a fibre stretches. Three materials,
/// three strengths, one law.
///
/// **Hardness.** A core softer than the plate loses: it upsets on the face and stops
/// being a punch. This is the whole difference between a 7.62x39 PS at 40 HRC and a
/// 7N10 at 60 HRC out of plates of the same steel, and without it no single constant
/// fits both the armour-steel ladder and the GOST classes — they sit a factor of 3.8
/// apart in energy per mm² otherwise.
///
/// Sources: Recht &amp; Ipson 1963 for the residual velocity; Lambert &amp; Jonas 1976 for
/// the generalised form v_r = a(v^p − v_bl^p)^(1/p), of which Recht-Ipson is p = 2.
/// </summary>
public static class BallisticLimit
{
    /// <summary>How the barrier gives way. Matches ArmorMaterialRef.Class.</summary>
    public const string Ductile = "Ductile";
    public const string Brittle = "Brittle";
    public const string Fibrous = "Fibrous";

    /// <summary>
    /// Everything the law needs that is not the projectile. Kept as a struct so the
    /// client can fill it from the wire and the tests from the reference book.
    /// </summary>
    public struct Barrier
    {
        /// <summary>Ductile | Brittle | Fibrous.</summary>
        public string Class;

        /// <summary>Thickness of the hard element, mm.</summary>
        public double ThicknessMm;

        /// <summary>Ductile: ultimate shear strength, MPa.</summary>
        public double ShearMPa;

        /// <summary>Brittle: compressive strength, MPa.</summary>
        public double CompressiveMPa;

        /// <summary>Fibrous: fibre tensile strength, MPa.</summary>
        public double FibreTensileMPa;

        /// <summary>Fibrous: strain to failure.</summary>
        public double FailureStrain;

        /// <summary>Vickers hardness of the barrier; 0 leaves the hardness term out.</summary>
        public double HardnessHv;

        /// <summary>g/cm³ — for the mass of the plug the projectile drags with it.</summary>
        public double DensityGCm3;

        /// <summary>
        /// How much of a fibre barrier is actually fibre, by volume. A sewn package sits
        /// at 0.63 g/cm³ against aramid's own 1.44, so 44% of it is fibre and the rest is
        /// air between the layers; a pressed polyethylene plate is essentially all fibre.
        /// Only the fibre does any work, and without this the same constant cannot fit
        /// both a 7.6 mm vest package stopping a 9x18 and a 33 mm plate certified against
        /// M80 — they come out 2.4x apart, which is exactly the ratio of their packing.
        /// 1 = solid.
        /// </summary>
        public double PackedFraction;

        /// <summary>
        /// The strength that resists this projectile, MPa. A ductile plate shears, a
        /// ceramic crushes, and a fibre panel does neither: it stretches, and the work
        /// it can do is the area under its own stress-strain curve.
        /// </summary>
        public double Strength()
        {
            switch (Class)
            {
                case Brittle:
                    return CompressiveMPa;
                case Fibrous:
                    return FibreTensileMPa * FailureStrain;
                default:
                    return ShearMPa;
            }
        }
    }

    /// <summary>The projectile as the barrier meets it: the core, not the cartridge.</summary>
    public struct Core
    {
        /// <summary>Mass carrying the penetration, g.</summary>
        public double MassG;

        /// <summary>Diameter of what is doing the punching, mm.</summary>
        public double DiaMm;

        /// <summary>Vickers hardness of the core; 0 leaves the hardness term out.</summary>
        public double HardnessHv;
    }

    /// <summary>Free constants, one per failure mode plus the hardness clamp.</summary>
    public struct Tuning
    {
        public double DuctileK;
        public double BrittleK;
        public double FibrousK;

        /// <summary>Bounds on the plate-over-core hardness ratio.</summary>
        public double HardnessFloor;
        public double HardnessCeiling;

        /// <summary>Smallest cosine an oblique hit is read at, so a graze is not infinite.</summary>
        public double MinCos;

        public static Tuning Default => new Tuning
        {
            // the rolled-armour V50 ladder, six thicknesses, within 3% on this one number
            DuctileK = 2.50,
            // the ceramic ladder against the GOST rifle classes. The bare-alumina
            // depth-of-penetration point wants 0.68, three and a half times this, and
            // the difference is that a plate is a strike face on a backer while that
            // measurement was a tile in a fixture - the thickness in the book is the
            // whole plate, so the constant has to be the whole plate's too
            BrittleK = 0.20,
            // a 33 mm polyethylene plate certified to stop M80 at 847, and a 7.6 mm
            // aramid package stopping the 9x18 GOST fires at Бр1, once the package's
            // 44% packing is taken out
            FibrousK = 15.0,
            HardnessFloor = 0.5,
            HardnessCeiling = 2.5,
            MinCos = 0.34,
        };
    }

    /// <summary>
    /// How much harder the barrier is than the core, bounded. Above 1 the core is
    /// losing the argument and the barrier is worth more than its strength alone says;
    /// below 1 the core is cutting through and the barrier is worth less. Fibre panels
    /// have no hardness worth the name and sit at 1.
    /// </summary>
    public static double HardnessFactor(Barrier b, Core c, Tuning t)
    {
        if (b.Class == Fibrous || b.HardnessHv <= 0 || c.HardnessHv <= 0)
        {
            return 1;
        }

        var ratio = b.HardnessHv / c.HardnessHv;
        return ratio < t.HardnessFloor ? t.HardnessFloor
            : ratio > t.HardnessCeiling ? t.HardnessCeiling
            : ratio;
    }

    /// <summary>
    /// Work the barrier can do against this core before it is through, J.
    ///
    /// Two shapes, because a plate and a pack do not fail the same way. Punching a hole
    /// in a solid means shearing its perimeter through its own thickness, so the work
    /// goes as T²: twice the plate is four times the work, which is what the rolled
    /// armour ladder measures. A fibre pack has no hole to punch — each layer catches
    /// its share of the cone and hands the rest back, so the work goes as T. Reading a
    /// pack at T² makes a 33 mm polyethylene plate absorb three and a half times what
    /// its certificate says, because the constant that fits a 7.6 mm aramid pack is
    /// then being squared over four times the thickness.
    /// </summary>
    public static double WorkJ(Barrier b, Core c, double cos, Tuning t)
    {
        var k = b.Class == Brittle ? t.BrittleK : b.Class == Fibrous ? t.FibrousK : t.DuctileK;
        var strength = b.Strength();
        if (k <= 0 || strength <= 0 || b.ThicknessMm <= 0 || c.DiaMm <= 0)
        {
            return 0;
        }

        // an oblique hit presents more material along the path
        var slant = b.ThicknessMm / Math.Max(Math.Abs(cos), t.MinCos);

        // MPa·mm·mm² is N·mm, which is mJ
        var geometry = b.Class == Fibrous
            ? Math.PI * c.DiaMm * c.DiaMm / 4.0 * slant      // layer by layer over the core's face
            : Math.PI * c.DiaMm * slant * slant;             // shear round the perimeter, through the plate

        var packed = b.Class == Fibrous && b.PackedFraction > 0 ? b.PackedFraction : 1;

        return k * strength * geometry * packed * HardnessFactor(b, c, t) / 1000.0;
    }

    /// <summary>
    /// Ballistic limit, m/s: below this the barrier holds, above it the core is through.
    /// Returns 0 when there is nothing to compute with, which callers read as "no
    /// geometry for this item, fall back to the class threshold".
    /// </summary>
    public static double V50(Barrier b, Core c, double cos, Tuning t)
    {
        var w = WorkJ(b, c, cos, t);
        if (w <= 0 || c.MassG <= 0)
        {
            return 0;
        }

        return Math.Sqrt(2 * w / (c.MassG / 1000.0));
    }

    /// <summary>
    /// Mass of the plug the core punches out and drags along, g. A ductile plate loses a
    /// disc the size of the hole; a ceramic shatters into pieces that mostly go sideways;
    /// a fibre panel loses nothing at all — it stretches and tears.
    /// </summary>
    public static double PlugMassG(Barrier b, Core c, double cos, Tuning t)
    {
        if (b.Class == Fibrous || b.DensityGCm3 <= 0)
        {
            return 0;
        }

        var slant = b.ThicknessMm / Math.Max(Math.Abs(cos), t.MinCos);
        var volumeMm3 = Math.PI * c.DiaMm * c.DiaMm / 4.0 * slant;
        var grams = volumeMm3 * b.DensityGCm3 / 1000.0;

        // a ceramic does not hand over a disc; what comes off is rubble and dust
        return b.Class == Brittle ? grams * 0.25 : grams;
    }

    /// <summary>
    /// Recht-Ipson residual velocity, m/s. The plug the core dragged out of the plate is
    /// now travelling with it, so the pair is slower than energy conservation alone
    /// would say. Below the limit this is 0 and the barrier holds.
    ///
    /// The energy the barrier took is no longer a separate constant to tune: it is
    /// ½m(v² − v_r²), and it falls out of the limit velocity.
    /// </summary>
    public static double ResidualVelocity(double v, double v50, double coreMassG,
        double plugMassG)
    {
        if (v <= v50 || coreMassG <= 0)
        {
            return 0;
        }

        var shared = coreMassG / (coreMassG + Math.Max(plugMassG, 0));
        return shared * Math.Sqrt(v * v - v50 * v50);
    }
}
