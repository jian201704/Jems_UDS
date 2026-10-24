using System;
using System.Globalization;
using System.Text;

namespace EonSharedCore
{
    /// <summary>
    /// Hex parsing / formatting helpers. Faithful port of EonTest's hexPayload / toHex helpers.
    /// </summary>
    public static class HexUtils
    {
        /// <summary>
        /// Parse a hex display string into bytes.
        /// Accepts: "22 F1 05", "22F105", "0x22 0xF1", "22,f1,05", "22-F1-05".
        /// Returns null when the input is invalid / not an even number of hex digits.
        /// </summary>
        public static byte[] TryParseHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var cleaned = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch) || ch == ',' || ch == '_' || ch == '-' || ch == ':')
                    continue;
                cleaned.Append(ch);
            }
            string hex = cleaned.ToString().Replace("0x", "").Replace("0X", "");
            if (hex.Length == 0 || (hex.Length % 2) != 0) return null;

            byte[] data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                string token = hex.Substring(i * 2, 2);
                int value;
                if (!int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                    return null;
                data[i] = (byte)value;
            }
            return data;
        }

        public static byte[] ParseHex(string text)
        {
            byte[] data = TryParseHex(text);
            if (data == null)
                throw new FormatException("Invalid hex string: '" + text + "'");
            return data;
        }

        /// <summary>Format bytes as uppercase hex display, e.g. "62 F1 05".</summary>
        public static string ToHexString(byte[] data, string separator = " ")
        {
            if (data == null || data.Length == 0) return "";
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0 && !string.IsNullOrEmpty(separator)) sb.Append(separator);
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>Normalize a hex display string to uppercase single-space form; "" on invalid input.</summary>
        public static string Normalize(string text)
        {
            byte[] data = TryParseHex(text);
            return data == null ? "" : ToHexString(data);
        }

        /// <summary>
        /// Parse an address value: accepts "0x102D", "102D", "0x1008", decimal "4141".
        /// Returns <paramref name="fallback"/> when unparsable or out of range.
        /// </summary>
        public static ushort TryParseAddress(string text, ushort fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string token = text.Trim();
            bool isHex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (isHex) token = token.Substring(2);
            int value;
            NumberStyles style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (int.TryParse(token, style, CultureInfo.InvariantCulture, out value) &&
                value >= 0 && value <= 0xFFFF)
                return (ushort)value;
            return fallback;
        }

        /// <summary>Parse a byte (0..255) from "0x00" / "00" / decimal. Returns fallback on failure.</summary>
        public static byte TryParseByte(string text, byte fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string token = text.Trim();
            bool isHex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (isHex) token = token.Substring(2);
            int value;
            NumberStyles style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (int.TryParse(token, style, CultureInfo.InvariantCulture, out value) &&
                value >= 0 && value <= 0xFF)
                return (byte)value;
            return fallback;
        }
    }
}
