using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PLATE.Server.Services
{
    /// <summary>
    /// A length read out of an item's name or text — "barrel_ar15_370mm", "11.5 inch
    /// barrel". The server sets a barrel's velocity from it; E.F.E. carries an identical
    /// copy (its Ballistics/BarrelLength.cs) and lays the barrel out along the weapon for
    /// recoil with it, so the two must read the same number off the same name: a change to
    /// one is a change to the other, the same day.
    /// </summary>
    public static class BarrelLength
    {
        public const double MmPerMetre = 1000;

        private const double MmPerInch = 25.4;

        /// <summary>
        /// Length in millimetres. The lookbehind is what keeps the caliber out of it: a
        /// pack that writes "AR-15 5.56x45mm 11.5 inch barrel" offers "45mm" to anything
        /// reading left to right, and a 45 mm AR-15 barrel is not a thing. The unit ends at
        /// a letter rather than at a word boundary, because the base game writes
        /// "barrel_ar15_260mm_556x45" and an underscore is a word character.
        /// </summary>
        private static readonly Regex Millimetres =
            new Regex(@"(?<![\dxх×.,])(\d{2,4}(?:[.,]\d+)?)\s*(?:mm|мм)(?![a-zA-Zа-яА-Я])",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Length in inches, as every American pack spells it.</summary>
        private static readonly Regex Inches =
            new Regex(@"(?<![\dxх×.,])(\d{1,2}(?:[.,]\d+)?)\s*(?:inches|inch|in\b|""|″)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Length in millimetres from the first string that carries one. Millimetres are
        /// tried before inches so that "6.5 inch (165mm)" answers with the exact figure
        /// rather than 165.1; a name with no unit at all answers with nothing, which
        /// leaves the item alone rather than guessing that "12.5 Carbine" means 12.5 of
        /// something.
        /// </summary>
        public static double Parse(IEnumerable<string> texts)
        {
            foreach (var text in texts)
            {
                var mm = ParseMm(text);
                if (mm > 0)
                {
                    return mm;
                }
            }

            return 0;
        }

        /// <summary>The same for one string; 0 = no length in it.</summary>
        public static double ParseMm(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var mm = Millimetres.Match(text);
            if (mm.Success && TryNumber(mm.Groups[1].Value, out var millimetres))
            {
                return millimetres;
            }

            var inch = Inches.Match(text);
            if (inch.Success && TryNumber(inch.Groups[1].Value, out var inches))
            {
                return Math.Round(inches * MmPerInch, 1);
            }

            return 0;
        }

        /// <summary>A number as either half of the world writes it.</summary>
        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
