using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ExcelDecodeCore
{
    /// <summary>
    /// Step-level value validation rules (shared contract with EonTest).
    /// All specified checks must pass (AND). Applied to the first decoded value,
    /// or to the raw response text when the value is not decoded.
    ///
    /// G 列 (配置参数1) 校验键：
    ///   expectedValue        — 相等（数值可带 0x 前缀）
    ///   expectedContains     — 包含子串
    ///   expectedNotContains  — 不包含子串
    ///   expectedStartsWith   — 以...开头
    ///   expectedEndsWith     — 以...结尾
    ///   expectedRegex        — 正则匹配
    ///   expectedLength       — 字符串精确长度
    ///   expectedMinLength    — 字符串长度下限
    ///   expectedMaxLength    — 字符串长度上限
    ///   expectedMin          — 数值下限
    ///   expectedMax          — 数值上限
    ///   expectedMinDate      — ISO 日期下限（含）
    ///   expectedMaxDate      — ISO 日期上限（含）
    ///   expectedCaseSensitive — 大小写敏感（默认 true；影响 包含/前缀/后缀/正则）
    /// </summary>
    public class ValueValidation
    {
        public string ExpectedValue = "";
        public string Contains = "";
        public string NotContains = "";
        public string StartsWith = "";
        public string EndsWith = "";
        public string RegexPattern = "";
        public int? ExactLength;
        public int? MinLength;
        public int? MaxLength;
        public double? Min;
        public double? Max;
        public string MinDate = "";
        public string MaxDate = "";
        public bool CaseSensitive = true;
        public string ForbiddenDtcFile = "";
        public string ForbiddenDtcSheet = "";
        public string ForbiddenDtcColumn = "C";
        public string ForbiddenDtcError = "";
        public string ForbiddenDtcMatch = "";
        public string DtcStatusMatch = "";
        // -1 disables DTC status-byte checking; otherwise any selected bit fails.
        public int DtcStatusMask = -1;

        public void SetForbiddenDtcSource(string file, string sheet, string column)
        {
            ForbiddenDtcFile = file ?? "";
            ForbiddenDtcSheet = sheet ?? "";
            ForbiddenDtcColumn = string.IsNullOrEmpty(column) ? "C" : column.Trim();
        }

        public bool HasAny
        {
            get
            {
                return ExpectedValue.Length > 0 || Contains.Length > 0 ||
                       NotContains.Length > 0 || StartsWith.Length > 0 ||
                       EndsWith.Length > 0 || RegexPattern.Length > 0 ||
                       ExactLength.HasValue || MinLength.HasValue || MaxLength.HasValue ||
                       Min.HasValue || Max.HasValue || MinDate.Length > 0 || MaxDate.Length > 0 ||
                       ForbiddenDtcFile.Length > 0 ||
                       DtcStatusMask >= 0;
            }
        }

        public static ValueValidation FromPairs(IDictionary<string, string> pairs)
        {
            var v = new ValueValidation();
            v.ExpectedValue = ConfigPairParser.Get(pairs, "expectedValue", "");
            v.Contains = ConfigPairParser.Get(pairs, "expectedContains", "");
            v.NotContains = ConfigPairParser.Get(pairs, "expectedNotContains", "");
            v.StartsWith = ConfigPairParser.Get(pairs, "expectedStartsWith", "");
            v.EndsWith = ConfigPairParser.Get(pairs, "expectedEndsWith", "");
            v.RegexPattern = ConfigPairParser.Get(pairs, "expectedRegex", "");
            v.ExactLength = OptionalInt(pairs, "expectedLength");
            v.MinLength = OptionalInt(pairs, "expectedMinLength");
            v.MaxLength = OptionalInt(pairs, "expectedMaxLength");
            v.Min = OptionalDouble(pairs, "expectedMin", "lowerLimit");
            v.Max = OptionalDouble(pairs, "expectedMax", "upperLimit");
            v.MinDate = ConfigPairParser.Get(pairs, "expectedMinDate", "");
            v.MaxDate = ConfigPairParser.Get(pairs, "expectedMaxDate", "");
            v.CaseSensitive = ConfigPairParser.GetBool(pairs, "expectedCaseSensitive", true);
            v.ForbiddenDtcFile = ConfigPairParser.Get(pairs, "forbiddenDtcFile", "");
            v.ForbiddenDtcSheet = ConfigPairParser.Get(pairs, "forbiddenDtcSheet", "");
            v.ForbiddenDtcColumn = ConfigPairParser.Get(pairs, "forbiddenDtcColumn", "C");
            v.DtcStatusMask = ParseDtcStatusMask(
                ConfigPairParser.Get(pairs, "dtcStatusMask", ""),
                ConfigPairParser.Get(pairs, "dtcStatusBits", ""));
            return v;
        }

        /// <summary>Build validation rules from a decoded JSON object node (P 列每个测量项内嵌).</summary>
        public static ValueValidation FromJson(Dictionary<string, object> node)
        {
            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (node != null)
            {
                foreach (var kv in node)
                {
                    pairs[kv.Key] = kv.Value == null
                        ? ""
                        : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                }
            }
            return FromPairs(pairs);
        }

        /// <summary>
        /// Merge per-measurement verdicts (P 列内嵌校验键). Every measurement that has
        /// validation rules must pass. Returns "FAIL" / "PASS" / null (null = no per-item rules).
        /// </summary>
        public static string MultiVerdict(IList<DecodedMeasurement> measurements, IList<DecodeSpec> specs)
        {
            bool any = false;
            int count = measurements == null ? 0 : measurements.Count;
            for (int i = 0; i < count; i++)
            {
                var spec = (specs != null && i < specs.Count) ? specs[i] : null;
                if (spec == null || spec.Validation == null || !spec.Validation.HasAny) continue;
                any = true;
                var m = measurements[i];
                if (spec.Validation.Evaluate(m.Value, m.RawHex) == "FAIL") return "FAIL";
            }
            return any ? "PASS" : null;
        }

        /// <summary>
        /// Evaluate all rules.
        /// Returns "PASS" when every specified check passes, "FAIL" when any fails,
        /// null when there are no rules (caller falls back to J/K limits etc).
        /// </summary>
        public string Evaluate(object value, string rawText = null)
        {
            if (!HasAny) return null;

            DtcStatusMatch = "";
            if (ForbiddenDtcFile.Length > 0)
            {
                if (ContainsForbiddenDtc(value, rawText)) return "FAIL";
                if (ForbiddenDtcError.Length > 0) return "FAIL";
            }
            if (DtcStatusMask >= 0 && ForbiddenDtcFile.Length == 0 &&
                HasDtcStatusFault(rawText ?? Convert.ToString(value, CultureInfo.InvariantCulture)))
                return "FAIL";

            string text = value == null ? rawText : Convert.ToString(value, CultureInfo.InvariantCulture);
            var comp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            // 相等
            if (ExpectedValue.Length > 0)
            {
                if (value != null)
                {
                    if (!ValueCheck.MatchesExpected(value, ExpectedValue)) return "FAIL";
                }
                else
                {
                    if (text == null || !string.Equals(text.Trim(), ExpectedValue.Trim(), StringComparison.Ordinal))
                        return "FAIL";
                }
            }

            // 包含 / 不包含 / 前缀 / 后缀
            if (Contains.Length > 0 && (text == null || text.IndexOf(Contains, comp) < 0)) return "FAIL";
            if (NotContains.Length > 0 && text != null && text.IndexOf(NotContains, comp) >= 0) return "FAIL";
            if (StartsWith.Length > 0 && (text == null || !text.StartsWith(StartsWith, comp))) return "FAIL";
            if (EndsWith.Length > 0 && (text == null || !text.EndsWith(EndsWith, comp))) return "FAIL";

            // 正则
            if (RegexPattern.Length > 0 && (text == null || !RegexIsMatch(text, RegexPattern, CaseSensitive))) return "FAIL";

            // 字符串长度
            if (text != null)
            {
                if (ExactLength.HasValue && text.Length != ExactLength.Value) return "FAIL";
                if (MinLength.HasValue && text.Length < MinLength.Value) return "FAIL";
                if (MaxLength.HasValue && text.Length > MaxLength.Value) return "FAIL";
            }

            // 数值区间
            double num;
            bool isNum = ValueCheck.TryToDouble(value, out num);
            if (Min.HasValue) { if (!isNum || num < Min.Value) return "FAIL"; }
            if (Max.HasValue) { if (!isNum || num > Max.Value) return "FAIL"; }

            if (MinDate.Length > 0 || MaxDate.Length > 0)
            {
                DateTime date;
                if (text == null || !DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return "FAIL";
                DateTime bound;
                if (MinDate.Length > 0 && (!DateTime.TryParseExact(MinDate.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out bound) || date < bound)) return "FAIL";
                if (MaxDate.Length > 0 && (!DateTime.TryParseExact(MaxDate.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out bound) || date > bound)) return "FAIL";
            }

            return "PASS";
        }

        private bool ContainsForbiddenDtc(object value, string rawText)
        {
            ForbiddenDtcError = "";
            ForbiddenDtcMatch = "";
            string source = value == null ? rawText : Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(source)) return false;
            if (!File.Exists(ForbiddenDtcFile))
            {
                ForbiddenDtcError = "DTC forbidden list file was not found: " + ForbiddenDtcFile;
                return false;
            }
            string hex = Regex.Replace(source, "[^0-9A-Fa-f]", "").ToUpperInvariant();
            int start = DtcRecordsStart(hex);
            var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string line in ReadForbiddenDtcLines())
                {
                    Match match = Regex.Match(line, "(?i)(?:0x)?([0-9a-f]{6})");
                    if (match.Success) forbidden.Add(match.Groups[1].Value.ToUpperInvariant());
                }
            }
            catch (Exception ex)
            {
                ForbiddenDtcError = "DTC forbidden list could not be read: " + ex.Message;
                return false;
            }
            if (forbidden.Count == 0)
            {
                ForbiddenDtcError = "DTC forbidden list contains no valid six-digit DTC: " + ForbiddenDtcFile;
                return false;
            }
            for (int i = start; i + 8 <= hex.Length; i += 8)
            {
                string dtc = hex.Substring(i, 6);
                if (forbidden.Contains(dtc))
                {
                    if (DtcStatusMask < 0)
                    {
                        ForbiddenDtcMatch = dtc;
                        return true;
                    }

                    int status;
                    if (int.TryParse(hex.Substring(i + 6, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out status) && (status & DtcStatusMask) != 0)
                    {
                        ForbiddenDtcMatch = dtc;
                        DtcStatusMatch = dtc + " status=0x" + status.ToString("X2", CultureInfo.InvariantCulture) +
                            " mask=0x" + DtcStatusMask.ToString("X2", CultureInfo.InvariantCulture);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool HasDtcStatusFault(string source)
        {
            string hex = Regex.Replace(source ?? "", "[^0-9A-Fa-f]", "").ToUpperInvariant();
            int start = DtcRecordsStart(hex);
            for (int i = start; i + 8 <= hex.Length; i += 8)
            {
                int status;
                if (int.TryParse(hex.Substring(i + 6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out status) &&
                    (status & DtcStatusMask) != 0)
                {
                    DtcStatusMatch = hex.Substring(i, 6) + " status=0x" + status.ToString("X2", CultureInfo.InvariantCulture) +
                        " mask=0x" + DtcStatusMask.ToString("X2", CultureInfo.InvariantCulture);
                    return true;
                }
            }
            return false;
        }

        private static int DtcRecordsStart(string hex)
        {
            if (!hex.StartsWith("5902", StringComparison.Ordinal)) return 0;
            int payloadLength = hex.Length - 4;
            // Support both observed layouts: 5902 + records, and 5902 + availability mask + records.
            if (payloadLength >= 8 && payloadLength % 8 == 0) return 4;
            if (payloadLength >= 10 && (payloadLength - 2) % 8 == 0) return 6;
            return 4;
        }

        private static int ParseDtcStatusMask(string maskText, string bitsText)
        {
            int mask;
            string text = (maskText ?? "").Trim();
            if (text.Length > 0)
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
                if (int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask))
                    return mask & 0xFF;
            }

            mask = 0;
            foreach (string tokenValue in (bitsText ?? "").Split('|', ';', ' ', ','))
            {
                string token = tokenValue.Trim().ToLowerInvariant();
                if (token == "current" || token == "testfailed") { mask |= 0x01; continue; }
                if (token == "history" || token == "confirmed") { mask |= 0x08; continue; }
                if (token.StartsWith("bit")) token = token.Substring(3);
                int bit;
                if (int.TryParse(token, out bit))
                {
                    // bit1/bit3 follow the project's DTC terminology: 0x01/0x08.
                    if (bit == 1) mask |= 0x01;
                    else if (bit == 3) mask |= 0x08;
                    else if (bit >= 1 && bit <= 8) mask |= 1 << (bit - 1);
                }
            }
            return mask == 0 ? -1 : mask;
        }

        private IEnumerable<string> ReadForbiddenDtcLines()
        {
            if (string.Equals(Path.GetExtension(ForbiddenDtcFile), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using (var xlsx = new MiniXlsx(ForbiddenDtcFile))
                {
                    string sheet = string.IsNullOrEmpty(ForbiddenDtcSheet)
                        ? (xlsx.SheetNames.Count > 0 ? xlsx.SheetNames[0] : "")
                        : ForbiddenDtcSheet;
                    int column = ColumnNumber(ForbiddenDtcColumn);
                    var rows = xlsx.ReadSheet(sheet);
                    foreach (var row in rows)
                    {
                        if (column > 0 && column <= row.Count) yield return row[column - 1];
                    }
                }
                yield break;
            }

            foreach (string line in File.ReadAllLines(ForbiddenDtcFile)) yield return line;
        }

        private static int ColumnNumber(string column)
        {
            int result = 0;
            foreach (char ch in (column ?? "").Trim().ToUpperInvariant())
            {
                if (ch < 'A' || ch > 'Z') return 0;
                result = result * 26 + ch - 'A' + 1;
            }
            return result;
        }

        /// <summary>Human-readable description of all active rules (for the report/status line).</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            if (ExpectedValue.Length > 0) sb.Append("== " + ExpectedValue + " ");
            if (Contains.Length > 0) sb.Append("contains:" + Contains + " ");
            if (NotContains.Length > 0) sb.Append("not-contains:" + NotContains + " ");
            if (StartsWith.Length > 0) sb.Append("starts:" + StartsWith + " ");
            if (EndsWith.Length > 0) sb.Append("ends:" + EndsWith + " ");
            if (RegexPattern.Length > 0) sb.Append("regex:" + RegexPattern + " ");
            if (MinLength.HasValue) sb.Append("len>=" + MinLength.Value + " ");
            if (MaxLength.HasValue) sb.Append("len<=" + MaxLength.Value + " ");
            if (Min.HasValue) sb.Append("min=" + Min.Value.ToString(CultureInfo.InvariantCulture) + " ");
            if (Max.HasValue) sb.Append("max=" + Max.Value.ToString(CultureInfo.InvariantCulture) + " ");
            if (MinDate.Length > 0) sb.Append("date>=" + MinDate + " ");
            if (MaxDate.Length > 0) sb.Append("date<=" + MaxDate + " ");
            return sb.ToString().Trim();
        }

        private static bool RegexIsMatch(string text, string pattern, bool caseSensitive)
        {
            try
            {
                var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.IsMatch(text, pattern, options);
            }
            catch (ArgumentException)
            {
                return false; // 非法正则 → 不匹配
            }
        }

        private static int? OptionalInt(IDictionary<string, string> pairs, string key)
        {
            string s = ConfigPairParser.Get(pairs, key, null);
            int v;
            if (s != null && int.TryParse(s.Trim(), out v)) return v;
            return null;
        }

        private static double? OptionalDouble(IDictionary<string, string> pairs, string key, string alias = null)
        {
            string s = ConfigPairParser.Get(pairs, key, null);
            if (s == null && !string.IsNullOrEmpty(alias)) s = ConfigPairParser.Get(pairs, alias, null);
            double v;
            if (s != null && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return null;
        }
    }
}
