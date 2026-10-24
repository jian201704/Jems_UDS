using System;
using System.Collections.Generic;

namespace TestCore
{
    /// <summary>
    /// Parses EonTest G-column style "key=value,key=value" config strings.
    /// Example: "udsPayload=22 F1 05,expectedPrefix=62 F1 05,connectionMode=session".
    /// Tolerates ':' as key/value separator and empty entries (mirrors EonTest parseConfigPairs).
    /// Keys are matched case-insensitively.
    /// </summary>
    public static class ConfigPairParser
    {
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string rawPart in text.Split(','))
            {
                string trimmed = rawPart.Trim();
                if (trimmed.Length == 0) continue;
                int sep = trimmed.IndexOf('=');
                if (sep < 0) sep = trimmed.IndexOf(':');
                if (sep < 0)
                {
                    result[trimmed] = "";
                    continue;
                }
                string key = trimmed.Substring(0, sep).Trim();
                string value = trimmed.Substring(sep + 1).Trim();
                if (key.Length == 0) continue;
                result[key] = value;
            }
            return result;
        }

        /// <summary>Case-insensitive lookup. Returns fallback when the key is absent.</summary>
        public static string Get(IDictionary<string, string> pairs, string key, string fallback)
        {
            if (pairs == null) return fallback;
            string value;
            if (pairs.TryGetValue(key, out value)) return value;
            return fallback;
        }

        public static bool GetBool(IDictionary<string, string> pairs, string key, bool fallback)
        {
            string value = Get(pairs, key, null);
            if (value == null) return fallback;
            string token = value.Trim().ToLowerInvariant();
            if (token == "true" || token == "1" || token == "yes" || token == "on") return true;
            if (token == "false" || token == "0" || token == "no" || token == "off") return false;
            bool parsed;
            if (bool.TryParse(value.Trim(), out parsed)) return parsed;
            return fallback;
        }

        public static int GetInt(IDictionary<string, string> pairs, string key, int fallback)
        {
            string value = Get(pairs, key, null);
            if (value == null) return fallback;
            int parsed;
            if (int.TryParse(value.Trim(), out parsed)) return parsed;
            return fallback;
        }

        public static double GetDouble(IDictionary<string, string> pairs, string key, double fallback)
        {
            string value = Get(pairs, key, null);
            if (value == null) return fallback;
            double parsed;
            if (double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return fallback;
        }
    }
}
