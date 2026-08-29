using UnityEngine;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// What a projectile that crossed a person is launched with — the body-side twin of
    /// <see cref="ArmorExit"/> and of <see cref="ObstacleModel.Launch"/>.
    ///
    /// Two projectiles come out of a body: the one that went through a part and out the
    /// far side, and the fragments a bullet that broke up there splits into. Both are
    /// already priced by the wound channel — the drag law that ends the channel also
    /// says at what speed the projectile leaves before the channel ends — so nothing new
    /// is decided here. What this type exists for is that the answer has to be available
    /// BEFORE the engine builds the child, and the code that used to compute it ran
    /// after, so the arithmetic is lifted out of the Harmony patch and can be checked
    /// without a game running.
    /// </summary>
    internal static class BodyExit
    {
        /// <summary>
        /// What a projectile with nothing left to give is launched at, m/s.
        ///
        /// Not zero: a spawn with no speed has no direction either, and the engine
        /// builds its whole trajectory from direction × speed. A projectile at this
        /// speed drops where it was born, which is what "it did not come out" looks
        /// like. The same floor the barrier side uses, for the same reason.
        /// </summary>
        public const float InertSpeedMs = 0.1f;

        /// <summary>
        /// Exit speed after T mm of tissue: v·exp(−T/λ), λ = L/ln(v/v_stop).
        /// If T ≥ L (or on a contact impact, L=0) the projectile does not exit — 0.
        /// </summary>
        public static float ExitSpeed(float v, float lMm, float tMm, float vStop)
        {
            if (lMm <= 0f || lMm <= tMm)
            {
                return 0f;
            }

            var lambda = lMm / Mathf.Log(v / Mathf.Max(vStop, 1f)); // L>0 ⇒ v>v_stop
            return v * Mathf.Exp(-tMm / lambda);
        }

        /// <summary>
        /// What one of `count` fragments is made of. They split the parent's MASS rather
        /// than a damage budget, the diameter follows from the cube root of that mass so
        /// that density is preserved, and every fragment of a batch is therefore the
        /// same fragment — which is what lets the whole batch be priced once.
        /// </summary>
        /// <param name="share">Share of the parent's mass that leaves as fragments.</param>
        public static void FragmentSplit(float parentMassG, float parentDiaMm, float share,
            int count, out float massG, out float diaMm)
        {
            var per = Mathf.Max(share / Mathf.Max(count, 1), MinMassShare);
            massG = parentMassG * per;
            diaMm = parentDiaMm * Mathf.Pow(per, 1f / 3f);
        }

        /// <summary>Floor on the mass share, so a large batch cannot divide a fragment
        /// down to nothing and take its diameter with it.</summary>
        private const float MinMassShare = 1e-3f;

        /// <summary>
        /// The speed the child leaves with, m/s.
        ///
        /// `pathMm` is the tissue between it and daylight: the whole chord for a
        /// projectile that crossed the part, half of it for a fragment, because where in
        /// the part the bullet broke up is not known and the midpoint is the only
        /// answer that does not invent one.
        ///
        /// `minMassG` is the mass below which a fragment is inert — its energy has
        /// already been deposited in the part as the wound model's fragmentation bonus,
        /// and letting it fly on would spend that energy twice. The projectile that
        /// merely overpenetrated has no such floor and passes 0.
        /// </summary>
        public static float LaunchSpeed(float massG, float diaMm, float v, float x,
            float pathMm, float tissueScale, float minMassG, AmmoDataCache.WoundParams p)
        {
            if (massG < minMassG)
            {
                return InertSpeedMs;
            }

            var l = ClientWoundModel.ChannelMm(massG, diaMm, v, x, p, tissueScale);
            var vOut = ExitSpeed(v, l, pathMm, (float)p.GelStopVelocity);
            return Mathf.Max(vOut, InertSpeedMs);
        }
    }
}
