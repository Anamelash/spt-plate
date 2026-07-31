using UnityEngine;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Client-side wound channel model.
    /// Damage is a pure function of the projectile state at the moment of impact
    /// (m, d, v, X, frag) and of the path through the body part (collider chord).
    /// Template Damage takes no part in the calculation. Constants come from the
    /// server (/plate/ammo-data, the "__wound" block) — the formulas must match the
    /// server-side WoundModel.cs (which bakes the display Damage at V0).
    /// </summary>
    internal static class ClientWoundModel
    {
        internal struct Deposit
        {
            /// <summary>Body part damage, HP.</summary>
            public float DamageHp;

            /// <summary>Full channel length L(v), mm (0 = contact impact).</summary>
            public float ChannelMm;

            public float Pc;
            public float Tc;

            /// <summary>Contact branch (v ≤ v_stop): a bruise without a channel.</summary>
            public bool Contact;
        }

        /// <summary>
        /// Channel length L(v), mm. 0 — velocity at or below v_stop (tissue is not cut,
        /// contact deposition). Also used by the overpenetration decision (L > chord).
        /// </summary>
        public static float ChannelMm(float massG, float diaMm, float v, float x,
            AmmoDataCache.WoundParams p)
        {
            var vStop = Mathf.Max((float)p.GelStopVelocity, 1f);
            if (v <= vStop)
            {
                return 0f;
            }

            var area = Mathf.PI * diaMm * diaMm / 4f;
            var sd = massG / Mathf.Max(area, 1e-3f);
            return Mathf.Max(
                (float)p.GelDepthK * sd * Mathf.Log(v / vStop) *
                (1f - (float)p.ExpansionDepthFactor * x), 1f);
        }

        /// <summary>
        /// Energy deposition in a body part. pathMm — the path inside the part
        /// (collider chord); pathMm ≤ 0 — the projectile stayed in the part
        /// (bone/stuck): full deposition over L, the chord does not limit it.
        /// </summary>
        public static Deposit Compute(float massG, float diaMm, float v, float x,
            float frag, float pathMm, AmmoDataCache.WoundParams p)
        {
            var e = 0.5f * (massG / 1000f) * v * v;
            var budget = e / Mathf.Max((float)p.EnergyCapPerHp, 0.1f);

            var l = ChannelMm(massG, diaMm, v, x, p);
            if (l <= 0f)
            {
                // v ≤ v_stop: no channel, all remaining energy becomes a contact bruise
                return new Deposit { DamageHp = budget, Contact = true };
            }

            var path = pathMm > 0f ? Mathf.Min(pathMm, l) : l;
            var phi = path / l; // share of the path (≈ energy) left in the part

            var area = Mathf.PI * diaMm * diaMm / 4f;
            var areaEff = area * (1f + (float)p.ExpansionAreaFactor * x);
            var pc = areaEff * path / (float)p.WoundVolumePerHp;

            var eff = 1f / (1f + Mathf.Exp(
                -(v - (float)p.TcVelocityCenter) / (float)p.TcVelocityWidth));
            var tc = eff * e * phi * (1f + (float)p.TcFragBonus * frag) /
                     (float)p.TcEnergyPerHp;

            return new Deposit
            {
                DamageHp = Mathf.Min(pc + tc, budget),
                ChannelMm = l,
                Pc = pc,
                Tc = tc,
            };
        }
    }
}
