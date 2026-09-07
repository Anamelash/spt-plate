using System;

namespace PLATE.Client.Ballistics
{
    /// <summary>
    /// Adapts net work on the body to the existing game's contact-bruise HP scale.
    /// This is a gameplay mapping, not a clinically calibrated injury criterion.
    /// Projectile energy lost in armor must not be supplied as body work.
    /// </summary>
    internal static class BabtInjuryModel
    {
        internal struct Result
        {
            public bool IsValid;
            public bool HasLoad;
            public double DamageHp;
            public double BodyWorkJ;
            public double BluntCriterion;
            public double Severity;
            public double EffectiveDiameterCm;
            public string Diagnostic;
        }

        /// <summary>
        /// Uses the same E / EnergyCapPerHp law as ClientWoundModel's sub-threshold
        /// contact branch. There is no permanent wound channel, 2 HP floor or 40 HP
        /// ceiling. Area affects the legacy effects criterion; the mechanical body
        /// calculation has already accounted for area when obtaining net body work.
        /// Net work excludes armor dissipation and energy returned to the plate.
        /// </summary>
        internal static Result Evaluate(double bodyWorkJ, double contactAreaM2,
            double bodyMassKg, double wallThicknessCm, double energyPerHp,
            double bc1, double bc2)
        {
            if (!Finite(bodyWorkJ) || bodyWorkJ < 0 ||
                !Positive(contactAreaM2) || !Positive(bodyMassKg) ||
                !Positive(wallThicknessCm) || !Positive(energyPerHp) ||
                !Finite(bc1) || !Finite(bc2) || bc2 <= bc1)
            {
                return new Result { Diagnostic = "invalid body-work or injury-scale input" };
            }

            var diameterCm = 200.0 * Math.Sqrt(contactAreaM2 / Math.PI);
            var damage = bodyWorkJ / energyPerHp;
            if (!Positive(diameterCm) || !Finite(damage))
            {
                return new Result { Diagnostic = "injury-scale input exceeds numeric range" };
            }

            // BC is undefined at zero work. HasLoad=false makes that explicit and
            // prevents an otherwise valid zero-energy event from producing effects.
            var bc = bodyWorkJ > 0
                ? Math.Log(bodyWorkJ) - Math.Log(bodyMassKg) / 3.0 -
                  Math.Log(wallThicknessCm) - Math.Log(diameterCm)
                : 0.0;
            var severity = bodyWorkJ > 0
                ? Math.Max(0.0, Math.Min(1.0, (bc - bc1) / (bc2 - bc1)))
                : 0.0;

            return new Result
            {
                IsValid = true,
                HasLoad = bodyWorkJ > 0,
                DamageHp = damage,
                BodyWorkJ = bodyWorkJ,
                BluntCriterion = bc,
                Severity = severity,
                EffectiveDiameterCm = diameterCm,
                Diagnostic = "estimated mechanics; existing contact-bruise HP scale; " +
                             "legacy BC effects are not a clinical BABT calibration",
            };
        }

        private static bool Positive(double value) => Finite(value) && value > 0;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
