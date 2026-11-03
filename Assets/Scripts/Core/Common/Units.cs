using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TradingEmpire.Core
{
    /// <summary>
    /// Price: integer "points" (smallest quoted unit) for an instrument, e.g. EUR/USD with 5 digits:
    /// 1.10432 = 110432. Lots: integer hundredths of a lot (0.01 lot = 1). Both are plain <c>long</c>.
    /// </summary>
    public static class Units
    {
        /// <summary>Parse a positive decimal price exactly (extra digits are rounded half-even).</summary>
        public static long ParsePrice(string text, int digits)
        {
            var m = Regex.Match(text.Trim(), @"^(\d+)(?:\.(\d+))?$");
            if (!m.Success) throw new FormatException("Invalid price: \"" + text + "\"");
            string frac = m.Groups[2].Success ? m.Groups[2].Value : "";
            string kept = frac.Length > digits ? frac.Substring(0, digits) : frac;
            long joined = long.Parse(m.Groups[1].Value + kept.PadRight(digits, '0'), CultureInfo.InvariantCulture);
            string extra = frac.Length > digits ? frac.Substring(digits) : "";
            if (extra.Length == 0) return joined;
            bool isTie = Regex.IsMatch(extra, "^50*$");
            bool above = !isTie && double.Parse("0." + extra, CultureInfo.InvariantCulture) > 0.5;
            return joined + (above || (isTie && joined % 2 == 1) ? 1 : 0);
        }

        public static double PriceToNumber(long price, int digits) { return price / Math.Pow(10, digits); }

        public static string FormatPrice(long price, int digits)
        {
            long scale = (long)Math.Pow(10, digits);
            long abs = Math.Abs(price);
            long whole = abs / scale;
            string sign = price < 0 ? "-" : "";
            if (digits == 0) return sign + whole.ToString(CultureInfo.InvariantCulture);
            return sign + whole.ToString(CultureInfo.InvariantCulture) + "." + (abs - whole * scale).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        /// <summary>Parse lots like "0.25" into hundredths; null if invalid or more than 2 decimals.</summary>
        public static long? ParseLots(string text)
        {
            var m = Regex.Match(text.Trim(), @"^(\d+)(?:\.(\d{1,2}))?$");
            if (!m.Success) return null;
            long whole = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            long frac = m.Groups[2].Success ? long.Parse(m.Groups[2].Value.PadRight(2, '0'), CultureInfo.InvariantCulture) : 0;
            return whole * 100 + frac;
        }

        public static string FormatLots(long lots) { return (lots / 100.0).ToString("0.00", CultureInfo.InvariantCulture); }
    }
}
