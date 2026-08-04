using UnityEngine;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Probabilistic armour wear (3.4). One curve, two inputs.
    ///
    /// The old model made a worn plate uniformly thinner: at 50% durability it was
    /// half a plate everywhere. A real plate wears LOCALLY — it is intact where
    /// nothing hit it and broken where something did — so the global durability
    /// number is a statement about how much of the plate is damaged, not about how
    /// thin all of it is.
    ///
    /// The curve: a spot carrying damage x (0..1) keeps `1 − x^k` of its thickness.
    /// k is how local the material keeps its damage: aramid at 4 loses fibres in the
    /// spot and nothing beside it, ceramic at 1.5 spreads a crack web and a struck
    /// tile is rubble.
    ///
    /// The two inputs:
    ///  - SEEN damage — the hit landed within a recorded previous hit's radius.
    ///    Nothing to roll, geometry already answered: x = 1 − (1−q)^n over the n
    ///    hits recorded there, q being what one hit does to a spot.
    ///  - UNSEEN damage — the item entered the raid worn, or the hit memory
    ///    overflowed. Rolled: the chance of striking a damaged spot is the missing
    ///    durability, and a struck spot reads x = max(missing, q) — max, because a
    ///    roll that says "you hit a damaged spot" means at least one hit landed
    ///    there, and a spot cannot be less damaged than one hit leaves it. With the
    ///    max the two paths meet at the boundary instead of telling two stories
    ///    about the same plate.
    ///
    /// The q values are assumptions pending manufacturers' multi-hit data (which
    /// exists, but for SPACED hits); k comes from the resolution of 3.4. Both live
    /// in the per-material profiles the server sends.
    /// </summary>
    internal static class ArmorWear
    {
        /// <summary>Damage of a spot that took n recorded hits, 0..1.</summary>
        public static float SeenSpotDamage(int hits, float q)
        {
            if (hits <= 0 || q <= 0f)
            {
                return 0f;
            }

            return 1f - Mathf.Pow(1f - Mathf.Clamp01(q), hits);
        }

        /// <summary>
        /// Damage of the spot this hit found, 0..1. Seen hits win over the roll:
        /// where geometry already answered, there is nothing left to draw.
        /// </summary>
        /// <param name="hitsNearby">Recorded previous hits within the material's
        /// damage radius of this hit.</param>
        /// <param name="missingFrac">Missing durability, 0..1 — the plate-level
        /// statement of how much of it is damaged.</param>
        /// <param name="roll">One uniform draw for this hit, 0..1.</param>
        public static float SpotDamage(int hitsNearby, float missingFrac, float q, float roll)
        {
            if (hitsNearby > 0)
            {
                return SeenSpotDamage(hitsNearby, q);
            }

            missingFrac = Mathf.Clamp01(missingFrac);
            return roll < missingFrac ? Mathf.Max(missingFrac, Mathf.Clamp01(q)) : 0f;
        }

        /// <summary>Thickness fraction a spot with damage x still presents: 1 − x^k.</summary>
        public static float ThicknessFraction(float x, float k)
        {
            x = Mathf.Clamp01(x);
            if (x <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(1f - Mathf.Pow(x, Mathf.Max(k, 1f)));
        }

        /// <summary>The whole rule in one call, per layer: q and k are the LAYER's.</summary>
        public static float WornFraction(int hitsNearby, float missingFrac, float q,
            float k, float roll)
        {
            return ThicknessFraction(SpotDamage(hitsNearby, missingFrac, q, roll), k);
        }
    }
}
