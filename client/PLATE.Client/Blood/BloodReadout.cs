using System.Globalization;

namespace PLATE.Client.Blood
{
    /// <summary>How the volume is written out.</summary>
    public enum BloodUnits
    {
        /// <summary>Absolute volume: "4500/5000 ml".</summary>
        Milliliters,

        /// <summary>Share of the range: "90%".</summary>
        Percent,
    }

    /// <summary>What the readout's zero means.</summary>
    public enum BloodRange
    {
        /// <summary>
        /// The whole body. Zero is an empty body — a state that never arrives, because
        /// death comes at the threshold around half of it.
        /// </summary>
        FullVolume,

        /// <summary>
        /// Only the part that can be lost. Zero is the death point, so the reading is
        /// what is left to lose rather than what is in there.
        /// </summary>
        UsableVolume,
    }

    /// <summary>
    /// What the blood panel says, worked out from numbers alone.
    ///
    /// No Unity types and no EFT types on purpose — same reason as
    /// <see cref="Ballistics.Anatomy"/>. Everything here is decidable at a desk: whether
    /// a rate counts as bleeding, how long is left at that rate, how the volume reads.
    /// The view below it only positions the strings this returns and picks the colours,
    /// so a wrong countdown is a failing test rather than something noticed in a raid.
    ///
    /// Formatting is invariant-culture throughout. The game runs on whatever locale the
    /// machine has, and a decimal comma in "3,2 ml/s" would be a locale-dependent HUD.
    /// </summary>
    internal static class BloodReadout
    {
        /// <summary>
        /// Below this the rate would print as "0 ml/s", so there is nothing to say: no
        /// rate, no countdown. Tied to the one-decimal format used for it.
        /// </summary>
        public const float RateEpsilon = 0.05f;

        /// <summary>Where the readout stands, as fractions of maximum volume.</summary>
        internal struct Thresholds
        {
            /// <summary>Below this the volume reads as a warning.</summary>
            public float Warning;

            /// <summary>The death point — the floor of the usable range.</summary>
            public float Death;

            /// <summary>The next boundary below the current tier — what the countdown runs to.</summary>
            public float Next;

            /// <summary>What that boundary is called: "T1".."T3", or "OUT" for the death point.</summary>
            public string NextLabel;
        }

        /// <summary>How the player asked for it to be written.</summary>
        internal struct Format
        {
            public BloodUnits Units;
            public BloodRange Range;
        }

        /// <summary>
        /// One frame of the panel, in the pieces it is printed in. Kept apart rather than
        /// pre-joined because the view colours them differently — the volume carries the
        /// warning colour, the rest is muted behind it.
        /// </summary>
        internal struct Lines
        {
            /// <summary>The reading itself: "4500" or "90%".</summary>
            public string Volume;

            /// <summary>What it is out of: "5000 ml". Empty when the units are a share already.</summary>
            public string Capacity;

            /// <summary>The ATLS tier: "T2".</summary>
            public string Tag;

            /// <summary>Loss rate: "3.2 ml/s". Empty when nothing is draining.</summary>
            public string Rate;

            /// <summary>Countdown: "T3 in 41s". Empty when there is nothing to count to.</summary>
            public string Estimate;

            /// <summary>Blood is measurably leaving the body.</summary>
            public bool Bleeding;

            /// <summary>Volume is under the warning threshold.</summary>
            public bool Warning;
        }

        public static Lines Build(float cur, float max, int tier, float drainMlSec,
            Thresholds thresholds, Format format)
        {
            var lines = new Lines
            {
                Tag = "T" + tier.ToString(CultureInfo.InvariantCulture),
                Rate = string.Empty,
                Estimate = string.Empty,
                Capacity = string.Empty,
            };

            // A zero or negative capacity is not a state to render around: no fraction of
            // it means anything, so the panel says what it knows and claims nothing else.
            if (max <= 0f)
            {
                lines.Volume = format.Units == BloodUnits.Percent ? "0%" : "0";
                return lines;
            }

            // The usable range starts at the death point rather than at an empty body:
            // the blood below that threshold is never available to lose, and counting it
            // makes a lethal reading look like half a tank.
            var death = Clamp01(thresholds.Death);
            var floor = format.Range == BloodRange.UsableVolume ? max * death : 0f;
            var shownMax = max - floor;
            var shownCur = cur - floor;
            if (shownCur < 0f)
            {
                shownCur = 0f;
            }

            if (format.Units == BloodUnits.Percent)
            {
                var share = shownMax > 0f ? shownCur / shownMax * 100f : 0f;
                lines.Volume = Whole(share) + "%";
            }
            else
            {
                lines.Volume = Whole(shownCur);
                lines.Capacity = Whole(shownMax) + " ml";
            }

            lines.Warning = cur <= max * thresholds.Warning;
            lines.Bleeding = drainMlSec >= RateEpsilon;

            if (!lines.Bleeding)
            {
                return lines;
            }

            lines.Rate = drainMlSec.ToString("0.#", CultureInfo.InvariantCulture) + " ml/s";

            // Already past the boundary we would be counting to — which happens for a
            // frame or two before the tier catches up. A countdown to something behind
            // you is worse than none.
            var remaining = cur - max * thresholds.Next;
            if (remaining > 0f)
            {
                lines.Estimate = thresholds.NextLabel + " in " + Clock(remaining / drainMlSec);
            }

            return lines;
        }

        /// <summary>Seconds as a countdown: "41s" close in, "2:15" further out.</summary>
        public static string Clock(float seconds)
        {
            if (seconds < 0f)
            {
                seconds = 0f;
            }

            if (seconds < 60f)
            {
                return Whole(seconds) + "s";
            }

            var whole = (int)seconds;
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                   (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        private static string Whole(float value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
