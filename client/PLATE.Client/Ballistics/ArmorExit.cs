using UnityEngine;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// What is left of a projectile once it is through a plate.
    ///
    /// The armour meets the hard core, not the calibre, and it is the core that carries
    /// on: the jacket is stripped in the hole and stays there. Everything here follows
    /// from those two sentences, and it is kept out of the Harmony patch so the
    /// arithmetic can be checked without a game running.
    /// </summary>
    internal static class ArmorExit
    {
        internal struct Exit
        {
            /// <summary>Mass carrying on, g.</summary>
            public float MassG;

            /// <summary>Diameter of what carries on, mm.</summary>
            public float DiaMm;

            /// <summary>Velocity, m/s.</summary>
            public float V;

            /// <summary>Deformable fraction of what carries on.</summary>
            public float X;

            /// <summary>Energy the plate keeps beyond the penetration work, J — the shed jacket's.</summary>
            public float JacketEnergyJ;
        }

        /// <summary>
        /// Impact energy density on the plate, J/mm². The core's area, not the bullet's:
        /// a 5.5 mm carbide core in a 7.85 mm bullet arrives at twice the density the
        /// same energy spread over the full jacket would.
        /// </summary>
        public static float ImpactDensity(float energyJ, float diaMm, float coreAreaFrac)
        {
            var area = Mathf.PI * diaMm * diaMm / 4f;
            return energyJ / Mathf.Max(area * coreAreaFrac, 1e-4f);
        }

        /// <param name="massG">Mass of the whole bullet at impact, g.</param>
        /// <param name="diaMm">Calibre at impact, mm.</param>
        /// <param name="x">Deformable fraction of the whole bullet.</param>
        /// <param name="energyOutJ">Energy left after the penetration work, J.</param>
        /// <param name="coreAreaFrac">Core frontal area / bullet frontal area.</param>
        /// <param name="coreMassFrac">Core mass / bullet mass.</param>
        /// <param name="kFrag">Erosion by this barrier of whatever comes through, 0..1.</param>
        /// <param name="kDef">Blunting by this barrier of whatever deformable material is left.</param>
        public static Exit Compute(float massG, float diaMm, float x, float energyOutJ,
            float coreAreaFrac, float coreMassFrac, float kFrag, float kDef)
        {
            coreAreaFrac = Mathf.Clamp(coreAreaFrac, 0.05f, 1f);
            coreMassFrac = Mathf.Clamp(coreMassFrac, 0.05f, 1f);

            // the whole projectile decelerated as one piece, so the velocity is the whole
            // projectile's — the core does not speed up by shedding its jacket
            var v = Mathf.Sqrt(2f * Mathf.Max(energyOutJ, 0f) / (Mathf.Max(massG, 1e-4f) / 1000f));
            var mass = massG * coreMassFrac * (1f - Mathf.Clamp01(kFrag));

            // Deformable material goes with the jacket first, so a bullet that loses one
            // comes out harder than it went in. x is a fraction of the whole bullet;
            // rebase it onto what survived, then let the barrier blunt that.
            var xBase = coreMassFrac >= 1f
                ? x
                : Mathf.Clamp01((x - (1f - coreMassFrac)) / coreMassFrac);

            return new Exit
            {
                MassG = mass,
                DiaMm = diaMm * Mathf.Sqrt(coreAreaFrac),
                V = v,
                X = Mathf.Min(1f, xBase * (1f + kDef)),

                // energy the shed jacket was still carrying when it stopped in the hole
                JacketEnergyJ = Mathf.Max(energyOutJ, 0f) *
                                (1f - mass / Mathf.Max(massG, 1e-4f)),
            };
        }
    }
}
