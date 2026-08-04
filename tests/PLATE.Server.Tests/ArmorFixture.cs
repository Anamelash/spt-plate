using PLATE.Server.Services;

namespace PLATE.Server.Tests;

/// <summary>
/// Turning the reference book into something the ballistic limit can be fired at.
///
/// Two ways in, and the difference between them is the whole point of having both. A
/// class rung is what the model BELIEVES a class needs, and it is the fallback for an
/// item whose construction nobody published. A product is what a manufacturer actually
/// built and certified. Testing only the first measures the model against itself.
/// </summary>
public static class ArmorFixture
{
    private static ReferenceBook.AmmoReference Book => ReferenceBookTests.ShippedBook();

    /// <summary>The plate the model resolves an item of this material and class to.</summary>
    public static (BallisticLimit.Barrier Barrier, double ThicknessMm) ByClass(
        string material, int gameClass)
    {
        // Fibre is the one material that comes both ways, so the item decides and not the
        // material: an aramid vest package reads out of SoftArmor and is sold as Бр1 or
        // Бр2, a pressed polyethylene plate reads out of ArmorByClass like any other.
        var sewn = material == "Aramid";
        var rung = sewn ? System.Math.Min(gameClass, 2) : gameClass;
        var entry = sewn
            ? Book.SoftArmor[$"{material}/{rung}"]
            : Book.ResolveByClass($"{material}/{rung}");
        return Build(material, entry);
    }

    /// <summary>
    /// The book key of the real product a class rung borrows, or null for a rung that
    /// carries its own computed figures. The strict certification tests need this: a
    /// represented rung inherits its product's recorded shortfall.
    /// </summary>
    public static string ClassRepresentative(string material, int gameClass)
    {
        if (material == "Aramid")
        {
            return null;
        }

        return Book.ArmorByClass.TryGetValue($"{material}/{gameClass}", out var rung) &&
               rung.SameAs.Length > 0
            ? rung.SameAs
            : null;
    }

    public static bool ClassExists(string material, int gameClass)
    {
        var sewn = material == "Aramid";
        var rung = sewn ? System.Math.Min(gameClass, 2) : gameClass;
        return (sewn ? Book.SoftArmor : Book.ArmorByClass).ContainsKey($"{material}/{rung}");
    }

    /// <summary>A real product, by the key the book holds it under.</summary>
    public static (BallisticLimit.Barrier Barrier, double ThicknessMm) ByProduct(string bookKey)
    {
        var entry = Book.ArmorPlates[bookKey];
        if (string.IsNullOrEmpty(entry.Material))
        {
            throw new InvalidOperationException(
                $"{bookKey} has no material of its own in the book, so the game's would " +
                "have to supply it and a test cannot see the game");
        }

        return Build(entry.Material, entry);
    }

    private static (BallisticLimit.Barrier, double) Build(string material,
        ReferenceBook.ArmorPlateRef entry)
    {
        var physics = Book.ArmorMaterials[material];

        // only the fibre in a package does any work, and a sewn one is mostly air; a
        // product may also carry its own density when the material table is wrong for it
        // (boron carbide against the table's alumina)
        var density = entry.DensityGCm3 > 0 ? entry.DensityGCm3 : physics.DensityGCm3;

        var barrier = new BallisticLimit.Barrier
        {
            Class = physics.Class,
            FailureMode = physics.FailureMode,
            ThicknessMm = entry.ThicknessMm,
            ShearMPa = physics.ShearMPa,
            YieldMPa = physics.YieldMPa,
            CompressiveMPa = physics.CompressiveMPa,
            FibreTensileMPa = physics.FibreTensileMPa,
            FailureStrain = physics.FailureStrain,
            HardnessHv = physics.HardnessHv,
            DensityGCm3 = density,
            PackedFraction = physics.DensityGCm3 > 0 ? density / physics.DensityGCm3 : 1,
        };

        // the fibre panel behind the face is its own layer, exactly as the client
        // resolves it from the wire
        if (entry.BackingMm > 0)
        {
            var backingKey = string.IsNullOrEmpty(entry.BackingMaterial)
                ? "Aramid"
                : entry.BackingMaterial;
            var backing = Book.ArmorMaterials[backingKey];
            barrier.BackingMm = entry.BackingMm;
            barrier.BackingTensileMPa = backing.FibreTensileMPa;
            barrier.BackingStrain = backing.FailureStrain;
            barrier.BackingPacked = 1;
        }

        return (barrier, entry.ThicknessMm);
    }

    public static BallisticLimit.Core CoreOf(ArmorStandardTests.Threat t)
    {
        return BallisticLimit.Driving(t.MassG, t.DiaMm, t.CoreAreaFrac, t.CoreMassFrac,
            t.CoreHardnessHv, BallisticLimit.Tuning.Default);
    }

    /// <summary>Perpendicular hit on an undamaged plate — the conditions a standard tests at.</summary>
    public static double V50(BallisticLimit.Barrier barrier, ArmorStandardTests.Threat t)
    {
        return BallisticLimit.V50(barrier, CoreOf(t), 1.0, BallisticLimit.Tuning.Default);
    }

    /// <summary>Every cartridge a standard fires at this class.</summary>
    public static ArmorStandardTests.Threat[] Threats(string standard, string cls)
    {
        var table = standard == "GOST" ? ArmorStandardTests.Gost : ArmorStandardTests.Nij;
        return table.Where(t => t.Class == cls).ToArray();
    }
}
