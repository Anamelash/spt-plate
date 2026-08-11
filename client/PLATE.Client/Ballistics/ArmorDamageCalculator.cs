using PLATE.Server.Services;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// What a hit costs the plate — durability and the wear-spot record — decided by
    /// how the material fails, not by one price for everything.
    ///
    /// The old rule charged every material the same way: absorbed energy over
    /// JPerDurability, and a spot recorded for every hit. Two measurements refuted the
    /// halves of it that concern metal and fibre. Armox 600T took repeated 7.62 M61 AP
    /// hits ON THE SAME SPOT without losing resistance — the craters do not deepen
    /// cumulatively, the dent floor work-hardens (Göde et al., Eng. Sci. Tech. 38,
    /// 2023, 10.1016/j.jestch.2023.101337) — and MIL-STD-662F reads a metal plate as
    /// pristine two calibres from a crater. Dyneema HB26 panels shot eight times gained
    /// V50 during the test rather than losing it, and a hybrid soft pack shot at 75 mm
    /// spacing outperformed the same pack at 150 (van Es; van der Jagt-Deutekom &amp;
    /// Broos, PASS 2024, 10.52202/080042-0031). A ceramic, by contrast, genuinely
    /// crumbles by the energy it eats — the certification budgets (ESAPI's three shots
    /// per threat, NIJ IV's single AP, GOST's five) land where the energy price already
    /// puts it — so the brittle rule stays exactly what it was.
    ///
    /// **Metal** wears only when the hit bites past WearDepthFraction of the plate:
    /// partial-penetration depth follows from the failure law's own work integral —
    /// plugging work grows as T², so p/T = v/v50; hole-expansion flow grows as T, so
    /// p/T = (v/v50)². Below the fraction: no durability, no spot. Above it the price
    /// ramps linearly to the full energy price at the limit, because a step at the
    /// threshold would make 0.49·v50 free and 0.51·v50 full.
    ///
    /// A projectile that DIED on the face (crushed or cracked — CoreFate) reads its
    /// depth at (v/v50)² whatever the plate's own law, because the linear reading
    /// belongs to a rigid punch boring its own calibre: a mushroomed slug spreads over
    /// several calibres and shoves metal aside instead of shearing a plug, which is
    /// the flow reading. This is what a steel gong is: lead never wears it below the
    /// limit, and a magazine of soft-point 7.62x39 point-blank dents a Бр3 panel
    /// instead of eating it — while ball arriving near the limit still pays, which is
    /// what keeps the AR500 Level III certificate's six M80 an honest test.
    ///
    /// **Fibre** pays durability only for penetration — the evidence above — times
    /// FibreBlockWearFraction (0 = the evidence) for a blocked hit. The SPOT is still
    /// recorded: a bullet caught in the pack has cut the fibres of its own channel, and
    /// the measurements clearing the fibre of wear are spaced shots, not repeats into
    /// one crater.
    ///
    /// A barrier with no computed limit (v50 = 0: an item the server has no geometry
    /// for) pays the old full price — there is no thickness to take half of.
    ///
    /// Pure arithmetic: no Unity, no EFT types, tested without the game.
    /// </summary>
    public static class ArmorDamageCalculator
    {
        public struct Verdict
        {
            /// <summary>Durability points this hit costs the item.</summary>
            public float DurabilityLoss;

            /// <summary>Whether the hit leaves a mark in the plate's hit memory.</summary>
            public bool RecordSpot;
        }

        /// <param name="materialClass">Ductile | Brittle | Fibrous, from the wire.</param>
        /// <param name="failureMode">ShearPlugging | HoleExpansion — decides how depth
        /// grows with velocity for a ductile plate. Ignored elsewhere.</param>
        /// <param name="penetrated">Whether the round went through.</param>
        /// <param name="v">Impact speed, m/s.</param>
        /// <param name="v50">The limit for this hit's geometry and angle; 0 = unknown.</param>
        /// <param name="absorbedJ">Energy the plate kept: all of it on a block, the
        /// cost of the hole plus the shed jacket on a penetration.</param>
        /// <param name="jPerDurability">The material's energy price of one durability
        /// point; 0 disables durability loss (never the spot record).</param>
        /// <param name="wearDepthFraction">The fraction of thickness a hit must bite
        /// into before a ductile plate wears at all.</param>
        /// <param name="fibreBlockWearFraction">The share of the energy price a fibre
        /// pack pays for a hit it stopped.</param>
        /// <param name="coreDead">Whether the projectile died on the face — crushed or
        /// cracked (CoreFate other than Rigid). A dead projectile is spread mass and
        /// reads its depth at the flow law's square.</param>
        public static Verdict Assess(string materialClass, string failureMode,
            bool penetrated, float v, float v50, float absorbedJ, float jPerDurability,
            float wearDepthFraction, float fibreBlockWearFraction, bool coreDead = false)
        {
            var basePrice = jPerDurability > 0f && absorbedJ > 0f
                ? absorbedJ / jPerDurability
                : 0f;

            if (penetrated || v50 <= 0f)
            {
                return new Verdict { DurabilityLoss = basePrice, RecordSpot = true };
            }

            if (materialClass == BallisticLimit.Fibrous)
            {
                return new Verdict
                {
                    DurabilityLoss = basePrice * Clamp01(fibreBlockWearFraction),
                    RecordSpot = true,
                };
            }

            if (materialClass != BallisticLimit.Ductile)
            {
                // brittle, and anything the wire did not name: the old price
                return new Verdict { DurabilityLoss = basePrice, RecordSpot = true };
            }

            // partial-penetration depth from the failure law's work integral; a dead
            // projectile is spread mass and digs by the flow reading whatever the
            // plate's own law says
            var ratio = Clamp01(v / v50);
            var depth = coreDead || failureMode == BallisticLimit.HoleExpansion
                ? ratio * ratio
                : ratio;

            var floor = Clamp01(wearDepthFraction);
            if (depth < floor)
            {
                return new Verdict { DurabilityLoss = 0f, RecordSpot = false };
            }

            var ramp = floor >= 1f ? 1f : (depth - floor) / (1f - floor);
            return new Verdict { DurabilityLoss = basePrice * ramp, RecordSpot = true };
        }

        private static float Clamp01(float x)
        {
            return x < 0f ? 0f : x > 1f ? 1f : x;
        }
    }
}
