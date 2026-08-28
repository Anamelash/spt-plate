using System.Globalization;

namespace PLATE.Client.Overlay
{
    /// <summary>
    /// The journal line for one projectile meeting one obstacle, and nothing else.
    ///
    /// Pure text assembly — no Unity types, no EFT types — so it can be checked without
    /// a game running, which matters more here than it looks: this is the line raid
    /// evidence is grepped out of, and a decimal comma from a ru locale would break
    /// every filter written against it. Everything numeric goes through
    /// InvariantCulture, deliberately and in one place.
    ///
    /// It is the ENGINE's account of the collision, next to the model's own (which
    /// ObstaclePatches writes from inside the gate): what the collider actually carried,
    /// what the projectile actually arrived and left at, and what the game decided
    /// happened to it. The two together are what tells "the model said no and the engine
    /// agreed" apart from "the model was never asked".
    /// </summary>
    internal static class WallJournal
    {
        /// <summary>Nothing happened to the projectile.</summary>
        public const string EffectIntact = "intact";

        /// <summary>A barrier killed the core — only the obstacle model can say this.</summary>
        public const string EffectDeformed = "deformed";

        /// <summary>It came apart into fragments.</summary>
        public const string EffectDestroyed = "destroyed";

        /// <summary>It ended here.</summary>
        public const string EffectStopped = "stopped";

        /// <summary>It bounced.</summary>
        public const string EffectRicochet = "ricochet";

        /// <summary>
        /// What the collision did to the projectile, from the state the game left it in.
        ///
        /// <paramref name="deformed"/> overrides a plain pass-through: vanilla has no
        /// concept of a bullet being changed by a wall, so "intact" there means only
        /// "the game did not replace it", and the obstacle model is the one thing that
        /// can say otherwise.
        /// </summary>
        /// <param name="bulletState">Name of the engine's EBulletState after the collision.</param>
        public static string EffectOf(string bulletState, bool deformed)
        {
            switch (bulletState)
            {
                case "RicochetHit":
                    return EffectRicochet;
                case "StopHit":
                    return EffectStopped;
                case "FragmentationHit":
                    return EffectDestroyed;
                case "DeviationHit":
                    return deformed ? EffectDeformed : EffectIntact;
                default:
                    // "Flying" is reachable: a projectile that got through but had spent
                    // its deviation budget spawns no child and the engine leaves the
                    // state alone. Reported as itself rather than dressed up as
                    // something the game did not say.
                    return (bulletState ?? "?").ToLowerInvariant();
            }
        }

        /// <summary>
        /// One line. The collider's own numbers rather than its preset's: the whole
        /// reason for this journal is that a scene object may carry any values its
        /// designer liked, and the GameObject name is what identifies which door on
        /// which map it was.
        /// </summary>
        /// <param name="vOut">Speed of what carried on; null when nothing did.</param>
        /// <param name="devDeg">Angle between what arrived and what left; null when
        /// nothing left.</param>
        /// <param name="parents">The collider's ancestry, "parent/grandparent" — half the
        /// maps call the collider itself nothing but "metal" and the prop's real name
        /// lives a transform or two up. Null omits the field.</param>
        /// <param name="shotId">Which projectile chain this was, so two lines can be told
        /// apart when automatic fire makes their numbers identical. Null omits it.</param>
        /// <param name="freeExit">This collision was the far face of a crossing already
        /// paid for on the way in. Said out loud because the line otherwise looks exactly
        /// like a second, charged crossing of the same wall.</param>
        public static string Line(string material, string objectName, float penLevel,
            float penChance, float ricochetChance, float fragChance, string ammo,
            float vIn, float? vOut, float? devDeg, string effect, bool penetrated,
            string parents = null, string shotId = null, bool freeExit = false)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Concat(
                "wall ", material ?? "?", "(", objectName ?? "?", ")",
                parents == null ? "" : " par=" + parents,
                " pl=", penLevel.ToString("0.#", c),
                " pc=", penChance.ToString("0.##", c),
                " rc=", ricochetChance.ToString("0.##", c),
                " fc=", fragChance.ToString("0.##", c),
                " | ", ammo ?? "?",
                " v_in=", vIn.ToString("0", c),
                " -> v_out=", vOut.HasValue ? vOut.Value.ToString("0", c) : "-",
                " dev=", devDeg.HasValue ? devDeg.Value.ToString("0.0", c) : "-",
                " | effect=", effect ?? "?",
                " PEN:", penetrated ? "T" : "F",
                shotId == null ? "" : " shot=" + shotId,
                freeExit ? " EXIT" : "");
        }
    }
}
