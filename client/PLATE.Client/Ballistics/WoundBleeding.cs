using EFT;
using UnityEngine;

namespace PLATE.Client.Ballistics
{
    /// <summary>Where on the body a wound is, in the terms the mortality data is kept in.</summary>
    internal enum BleedRegion
    {
        Torso = 0,

        /// <summary>Neck, groin and shoulder — where a tourniquet cannot reach and a vessel still runs.</summary>
        Junction = 1,

        Limb = 2,
        Head = 3,
    }

    /// <summary>
    /// Whether a wound bleeds badly, decided by what the channel crossed.
    ///
    /// It used to be decided by the cartridge: the server wrote a bleed chance per ammo
    /// template out of calibre and expansiveness, and the same round then bled the same
    /// whether it went through a thigh or through the mediastinum. That is backwards.
    /// A projectile does not carry a bleeding rate around with it — it cuts what happens
    /// to be in front of it, and what is in front of it is anatomy.
    ///
    /// So the chance is a geometric one. A channel of diameter d over a length L sweeps a
    /// plane of d·L through the tissue, and the vessels it cuts are the ones that crossed
    /// that plane. For vessels of a given length per unit volume that is a Poisson
    /// process, which gives 1 - exp(-density·swept): more channel, more cut; a wider
    /// channel cuts more than a narrow one; and the density is the only thing that has to
    /// know any anatomy.
    ///
    /// The cartridge still matters, but through the channel it actually cuts rather than
    /// through a number somebody typed next to its name.
    /// </summary>
    internal static class WoundBleeding
    {
        internal struct Tuning
        {
            /// <summary>Major vessels per mm² of swept channel, general torso.</summary>
            public float VesselsTorso;

            /// <summary>Same for neck, groin and shoulder.</summary>
            public float VesselsJunction;

            /// <summary>Same for arms and legs away from the bundles.</summary>
            public float VesselsLimb;

            /// <summary>Same for the head.</summary>
            public float VesselsHead;

            /// <summary>Nothing is ever certain.</summary>
            public float MaxChance;
        }

        /// <summary>
        /// Which region a hitbox belongs to. The junctional set is the one the combat
        /// mortality data keeps separate for a reason: neck, groin and shoulder are where
        /// a major vessel runs and a tourniquet has nothing to squeeze against.
        ///
        /// Known bias, and it matters because these regions are what the raid tally is
        /// checked against: the thigh and upper arm hitboxes are counted whole, while the
        /// mortality taxonomy calls only their proximal ends junctional and the rest
        /// extremity. The game gives no way to split them, so the junctional share the
        /// journal prints reads high against the 19.2% it is compared with.
        /// </summary>
        public static BleedRegion Region(EBodyPartColliderType collider)
        {
            switch (collider)
            {
                case EBodyPartColliderType.NeckFront:
                case EBodyPartColliderType.NeckBack:
                case EBodyPartColliderType.Pelvis:
                case EBodyPartColliderType.PelvisBack:
                case EBodyPartColliderType.LeftThigh:
                case EBodyPartColliderType.RightThigh:
                case EBodyPartColliderType.LeftUpperArm:
                case EBodyPartColliderType.RightUpperArm:
                    return BleedRegion.Junction;

                case EBodyPartColliderType.LeftCalf:
                case EBodyPartColliderType.RightCalf:
                case EBodyPartColliderType.LeftForearm:
                case EBodyPartColliderType.RightForearm:
                    return BleedRegion.Limb;

                case EBodyPartColliderType.Eyes:
                case EBodyPartColliderType.Jaw:
                case EBodyPartColliderType.HeadCommon:
                case EBodyPartColliderType.ParietalHead:
                case EBodyPartColliderType.BackHead:
                case EBodyPartColliderType.Ears:
                    return BleedRegion.Head;

                default:
                    return BleedRegion.Torso;
            }
        }

        /// <summary>
        /// The plane the channel swept, mm². Diameter times length, taken through the
        /// average cross-section so a channel that widened when the projectile turned
        /// counts as the wider one it became: sqrt(4·V·L/pi).
        /// </summary>
        public static float SweptMm2(float cavityVolumeMm3, float pathMm)
        {
            if (cavityVolumeMm3 <= 0f || pathMm <= 0f)
            {
                return 0f;
            }

            return Mathf.Sqrt(4f * cavityVolumeMm3 * pathMm / Mathf.PI);
        }

        public static float Density(BleedRegion region, in Tuning t)
        {
            switch (region)
            {
                case BleedRegion.Junction: return t.VesselsJunction;
                case BleedRegion.Limb: return t.VesselsLimb;
                case BleedRegion.Head: return t.VesselsHead;
                default: return t.VesselsTorso;
            }
        }

        /// <summary>
        /// Chance that this channel opened something a bandage has to deal with.
        ///
        /// Deliberately no term for the great vessels. The aorta and the vena cava are in
        /// the mediastinum, but a wound to either of them is not something anyone
        /// bandages — it is handled where it belongs, as an internal organ bleed nothing
        /// in a field kit reaches. Giving the mediastinum a boost here as well would have
        /// been the same vessels counted twice, once in a form that a bandage closes.
        /// </summary>
        /// <param name="sweptMm2">Plane swept through the body part.</param>
        public static float HeavyChance(BleedRegion region, float sweptMm2, in Tuning t)
        {
            var expected = Density(region, t) * Mathf.Max(sweptMm2, 0f);
            if (expected <= 0f)
            {
                return 0f;
            }

            return Mathf.Min(1f - Mathf.Exp(-expected), Mathf.Clamp01(t.MaxChance));
        }
    }
}
