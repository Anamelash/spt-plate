using System;

namespace PLATE.Server.Services
{
    /// <summary>
    /// What an item's card weight is worth as a mass. The model reads mass off the card —
    /// a weapon's body for recoil, a plate's hard element for thickness — and other mods
    /// rescale every card in the database for carrying comfort (a weight multiplier of
    /// 0.5 halves a rifle and its cartridges alike). Two cards whose vanilla weight is
    /// known stand as anchors: what they weigh now against what they weighed says how the
    /// install has rescaled the rest. E.F.E. carries an identical copy for the recoil
    /// (its Ballistics/MassAnchor.cs): a change to one is a change to the other, the same
    /// day.
    /// </summary>
    public static class MassAnchor
    {
        /// <summary>weapon_izhmeh_pm_9x18pm — the anchor for weapons, parts and gear.</summary>
        public const string GearTpl = "5448bd6b4bdc2dfc2f8b4569";

        /// <summary>The PM's own card weight (no magazine), EFT 0.16.9 items.json, kg.</summary>
        public const double GearKg = 0.333;

        /// <summary>patron_9x18pm_PST_gzh — the anchor for cartridges.</summary>
        public const string AmmoTpl = "5737201124597760fc4431f1";

        /// <summary>The 9x18 Pst gzh card weight, EFT 0.16.9 items.json, kg.</summary>
        public const double AmmoKg = 0.010;

        /// <summary>
        /// Card weight over real mass: 1 on an untouched database, 0.5 under a halving
        /// multiplier. An anchor that is missing or weightless says nothing, and nothing
        /// is 1 — a zeroed card must not turn every mass into infinity.
        /// </summary>
        public static double Scale(double currentKg, double referenceKg)
        {
            if (!(currentKg > 0) || !(referenceKg > 0) || double.IsInfinity(currentKg))
            {
                return 1;
            }

            return currentKg / referenceKg;
        }

        /// <summary>The mass behind a card weight under the given scale.</summary>
        public static double Real(double cardKg, double scale)
        {
            return scale > 0 ? cardKg / scale : cardKg;
        }
    }
}
