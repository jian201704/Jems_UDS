using System;
using System.Globalization;

namespace ExcelDecodeCore
{
    /// <summary>
    /// Single-value (string / status / numeric) validation against an expected literal.
    /// Used when a response has ONE correct value instead of a numeric lower/upper range.
    /// Protocol-agnostic, reused by all hardware-protocol libraries.
    /// </summary>
    public static class ValueCheck
    {
        /// <summary>
        /// True when <paramref name="value"/> equals <paramref name="expected"/>.
        /// - numeric vs numeric: compares numerically (0x00 == 0, "3000" == 3000)
        /// - expected with 0x prefix: parsed as hex and compared numerically
        /// - otherwise: exact string equality (trimmed, case-sensitive)
        /// </summary>
        public static bool MatchesExpected(object value, string expected)
        {
            if (value == null || string.IsNullOrEmpty(expected)) return false;

            double v;
            if (TryToDouble(value, out v))
            {
                string exp = expected.Trim();
                if (exp.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    int hex;
                    if (int.TryParse(exp.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex))
                        return v == hex;
                }
                else
                {
                    double e;
                    if (double.TryParse(exp, NumberStyles.Float, CultureInfo.InvariantCulture, out e))
                        return v == e;
                }
            }

            string valueText = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.Equals(valueText.Trim(), expected.Trim(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Verdict text for a single-value check: "PASS"/"FAIL".
        /// </summary>
        public static string ExpectedVerdict(object value, string expected)
        {
            return MatchesExpected(value, expected) ? "PASS" : "FAIL";
        }

        public static bool TryToDouble(object value, out double result)
        {
            result = 0.0;
            if (value == null) return false;
            if (value is double) { result = (double)value; return true; }
            if (value is long) { result = (long)value; return true; }
            if (value is ulong) { result = (ulong)value; return true; }
            if (value is int) { result = (int)value; return true; }
            if (value is uint) { result = (uint)value; return true; }
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}
