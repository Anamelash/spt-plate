using System;
using System.Globalization;

namespace PLATE.Client.Overlay
{
    /// <summary>One synchronous ApplyShot scope; nested shots restore their parent.</summary>
    internal sealed class HitMarkerDamage
    {
        [ThreadStatic] private static HitMarkerDamage _current;
        private readonly HitMarkerDamage _previous;
        private readonly object _victim;
        private readonly object _collider;
        private float _armorLoss;

        private HitMarkerDamage(object victim, object collider)
        {
            _previous = _current;
            _victim = victim;
            _collider = collider;
        }

        internal static HitMarkerDamage Begin(object victim, object collider)
        {
            var scope = new HitMarkerDamage(victim, collider);
            _current = scope;
            return scope;
        }

        internal void End() => _current = _previous;

        internal static void RecordArmor(object collider, float before, float after)
        {
            if (_current == null || !ReferenceEquals(_current._collider, collider)) return;
            _current._armorLoss += Positive(before - after);
        }

        internal static float ConsumeArmor(object victim, object collider)
        {
            if (_current == null || !ReferenceEquals(_current._victim, victim) ||
                !ReferenceEquals(_current._collider, collider)) return 0;
            var loss = _current._armorLoss;
            _current._armorLoss = 0;
            return loss;
        }

        /// <summary>
        /// Health.ApplyDamage returns actual total HP loss, including overflow.
        /// Allocate that total proportionally between the wound/BABT inputs; this
        /// displays the health result rather than uncapped requested damage.
        /// Armor durability points are independent of HP and are never added to it.
        /// </summary>
        internal static string Label(float applied, bool blocked, bool hasBreakdown,
            float wound, float babt, float armor)
        {
            applied = Positive(applied);
            var total = (double)Positive(wound) + Positive(babt);
            var blunt = hasBreakdown && total > 0
                ? (float)(applied * (Positive(babt) / total))
                : blocked ? applied : 0;
            return string.Format(CultureInfo.InvariantCulture, "F:{0:0.#} B:{1:0.#} A:{2:0.#}",
                applied - blunt, blunt, Positive(armor));
        }

        private static float Positive(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, value);
    }
}
