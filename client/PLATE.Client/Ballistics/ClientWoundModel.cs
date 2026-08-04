using PLATE.Server.Services; // YawModel, compiled into both halves from one file
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

            /// <summary>Derived fragmentation degree, 0..1 — what broke up at the turn.</summary>
            public float Frag;

            /// <summary>Share of the projectile's energy left in this part, 0..1.</summary>
            public float DepositFrac;

            /// <summary>Contact branch (v ≤ v_stop): a bruise without a channel.</summary>
            public bool Contact;
        }

        /// <summary>
        /// Channel length L(v), mm. 0 — velocity at or below v_stop (tissue is not cut,
        /// contact deposition). Also used by the overpenetration decision (L > chord).
        /// </summary>
        /// <param name="tissueScale">What this channel's tissue is like against the
        /// calibrated average — ribs, cartilage and diaphragm are not gelatin.</param>
        public static float ChannelMm(float massG, float diaMm, float v, float x,
            AmmoDataCache.WoundParams p, float tissueScale = 1f)
        {
            var vStop = Mathf.Max((float)p.GelStopVelocity, 1f);
            if (v <= vStop)
            {
                return 0f;
            }

            var area = Mathf.PI * diaMm * diaMm / 4f;
            var sd = massG / Mathf.Max(area, 1e-3f);
            return Mathf.Max(
                (float)p.GelDepthK * Mathf.Max(tissueScale, 0.01f) * sd * Mathf.Log(v / vStop) *
                (1f - (float)p.ExpansionDepthFactor * x), 1f);
        }

        /// <summary>The broadside constants as the server sent them.</summary>
        public static YawModel.Tuning Yaw(AmmoDataCache.WoundParams p)
        {
            return new YawModel.Tuning(p.ExpansionAreaFactor, p.YawNeckCalibres,
                p.YawBroadsideFraction, p.BulletDensityGPerCm3, p.BulletFormFactor);
        }

        /// <summary>
        /// Energy deposition in a body part. pathMm — the path available inside the
        /// part (collider chord). Whether the projectile leaves the part is not asked:
        /// the drag law answers it. A channel that ends inside the part deposits
        /// everything; one that runs past it leaves only what the tissue took.
        /// </summary>
        /// <param name="coreMassFrac">Mass share of the hard core, which never breaks
        /// up. Fragmentation is derived here, not read from the vanilla field: the
        /// envelope fails where the bullet turns, if it is still fast enough there,
        /// and only the deformable non-core share breaks (3.6).</param>
        /// <param name="neckMm">Travel before this projectile goes broadside — the shot's
        /// own draw, not the cartridge's median.</param>
        /// <param name="tissueScale">Density of the tissue along this channel, 1 = calibrated.</param>
        public static Deposit Compute(float massG, float diaMm, float v, float x,
            float coreMassFrac, float pathMm, AmmoDataCache.WoundParams p,
            float neckMm = float.MaxValue, float tissueScale = 1f)
        {
            var e = 0.5f * (massG / 1000f) * v * v;
            var budget = e / Mathf.Max((float)p.EnergyCapPerHp, 0.1f);

            var l = ChannelMm(massG, diaMm, v, x, p, tissueScale);
            if (l <= 0f)
            {
                // v ≤ v_stop: no channel, all remaining energy becomes a contact bruise
                return new Deposit { DamageHp = budget, Contact = true };
            }

            var area = Mathf.PI * diaMm * diaMm / 4f;

            // the wound channel ends where the body ends: a bullet stopped by bone
            // does not carve a metre of cavity through a 250 mm chest
            var path = pathMm > 0f ? Mathf.Min(pathMm, l) : l;

            // energy left behind: quadratic drag gives v(s) = v·exp(-s/lambda), so the
            // tissue keeps 1-(v_out/v)² of the energy. No separate "it stopped" branch —
            // when the channel ends inside the part, path is the whole channel and this
            // already comes out at ~1. Asking the game whether a child bullet was
            // spawned instead handed full muzzle energy to a part that was only clipped.
            var lambda = Mathf.Max((float)p.GelDepthK * Mathf.Max(tissueScale, 0.01f) *
                                   (massG / Mathf.Max(area, 1e-3f)) *
                                   (1f - (float)p.ExpansionDepthFactor * x), 1e-3f);
            var phi = 1f - Mathf.Exp(-2f * path / lambda);

            // narrow while the projectile is still nose-first, wide once it has turned
            var yaw = Yaw(p);
            var pc = (float)YawModel.CavityVolumeMm3(
                YawModel.NoseAreaMm2(diaMm, x, p.ExpansionAreaFactor),
                YawModel.SideAreaMm2(massG, diaMm, x, yaw),
                neckMm, path) / (float)p.WoundVolumePerHp;

            // fragmentation: the envelope fails where this shot's own turn came, if
            // the projectile was still fast enough there; a hard core never breaks
            var vNeck = v * Mathf.Exp(-neckMm / lambda);
            var frag = neckMm <= path && vNeck > (float)p.FragVelocityThreshold
                ? x * (1f - Mathf.Clamp01(coreMassFrac))
                : 0f;

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
                Frag = frag,
                DepositFrac = phi,
            };
        }
    }
}
