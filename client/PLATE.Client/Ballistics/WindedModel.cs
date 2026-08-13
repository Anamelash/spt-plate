namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Winded — the breath knocked out of the torso by a heavy impact, blocked by
    /// armour or not. A transient diaphragm spasm, not an injury: the injury side is
    /// BABT's and the wound model's business, which is exactly why the thresholds here
    /// sit below theirs. Pure arithmetic, no Unity and no EFT types — tested the same
    /// way Anatomy and BloodReadout are (MODEL.md, Blood and trauma → Winded).
    /// </summary>
    internal static class WindedModel
    {
        internal struct Tuning
        {
            /// <summary>J into the torso below which nothing happens.</summary>
            public float OnsetJ;

            /// <summary>J at which the effect saturates.</summary>
            public float FullJ;

            /// <summary>Stamina-restoration lock at full severity, s.</summary>
            public float MaxLockSec;
        }

        /// <summary>
        /// Severity of the blow: 0 below the onset, 1 at saturation, linear between.
        /// A degenerate window (full at or below onset) reads as a hard threshold.
        /// </summary>
        public static float Severity(float joules, Tuning t)
        {
            if (joules <= t.OnsetJ)
            {
                return 0f;
            }

            var span = t.FullJ - t.OnsetJ;
            if (span <= 0f)
            {
                return 1f;
            }

            var s = (joules - t.OnsetJ) / span;
            return s > 1f ? 1f : s;
        }

        /// <summary>What is left of a stamina pool after a blow of severity t.</summary>
        public static float StaminaFactor(float t)
        {
            return 1f - Clamp01(t);
        }

        /// <summary>
        /// A volley lands over several calls in one frame, and the pool has already
        /// been drained for the severity known so far. Multiplying by this factor on
        /// top of that drain puts the pool exactly where a single blow of the combined
        /// severity would have: (1−t_prev)·Upgrade(t_prev, t_total) = 1−t_total.
        /// </summary>
        public static float UpgradeFactor(float appliedT, float totalT)
        {
            appliedT = Clamp01(appliedT);
            totalT = Clamp01(totalT);
            if (totalT <= appliedT)
            {
                return 1f;
            }

            var remaining = 1f - appliedT;
            return remaining <= 0f ? 0f : (1f - totalT) / remaining;
        }

        public static float LockSec(float t, Tuning tun)
        {
            return Clamp01(t) * tun.MaxLockSec;
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
