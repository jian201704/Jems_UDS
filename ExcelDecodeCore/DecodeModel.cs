using System;
using System.Collections.Generic;

namespace ExcelDecodeCore
{
    /// <summary>
    /// One decode rule. Faithful port of EonTest's DecodeSpec (ResponseDecoder.h).
    /// </summary>
    public class DecodeSpec
    {
        public string Name = "";
        public int Offset = 0;
        public int Length = -1;              // -1 = rest of the payload from offset
        public string Type = "hex";          // hex/uint8..64/int8..64/float32/float64/string/bits/uint/int/float
        public string Endian = "big";        // big / little
        public string Encoding = "ascii";
        public string DateEncoding = "bcd";  // bcd / ascii
        public string DateFormat = "yyyyMMdd";
        public int BitOffset = 0;
        public int BitLength = 0;
        public string Formula = "";
        public string Unit = "";
        public string ExtractRegex = "";
        public string PostType = "";
        public bool Trim = true;
        public bool RemoveSpaces = false;
        public string Padding = "";
        public string HexSeparator = " ";
        public string ForbiddenDtcFile = "";
        public string ForbiddenDtcSheet = "";
        public string ForbiddenDtcColumn = "C";

        // per-measurement value validation (P 列内嵌 expectedXxx 键，与 G 列同套契约)
        public ValueValidation Validation = new ValueValidation();
        public bool HasValidation { get { return Validation != null && Validation.HasAny; } }

        public static DecodeSpec FromPairs(IDictionary<string, string> pairs)
        {
            var spec = new DecodeSpec();
            spec.Name = ConfigPairParser.Get(pairs, "name",
                       ConfigPairParser.Get(pairs, "measurementName", ""));
            spec.Offset = ConfigPairParser.GetInt(pairs, "valueOffset",
                          ConfigPairParser.GetInt(pairs, "byteOffset",
                          ConfigPairParser.GetInt(pairs, "offset", 0)));
            spec.Length = ConfigPairParser.GetInt(pairs, "valueLength",
                          ConfigPairParser.GetInt(pairs, "byteLength",
                          ConfigPairParser.GetInt(pairs, "length", -1)));
            spec.Type = ConfigPairParser.Get(pairs, "valueType",
                        ConfigPairParser.Get(pairs, "dataType",
                        ConfigPairParser.Get(pairs, "type", "hex")));
            spec.Endian = ConfigPairParser.Get(pairs, "endian",
                        ConfigPairParser.Get(pairs, "byteOrder", "big"));
            spec.Encoding = ConfigPairParser.Get(pairs, "encoding", "ascii");
            spec.DateEncoding = ConfigPairParser.Get(pairs, "dateEncoding", "bcd");
            spec.DateFormat = ConfigPairParser.Get(pairs, "dateFormat", "yyyyMMdd");
            spec.BitOffset = ConfigPairParser.GetInt(pairs, "bitOffset", 0);
            spec.BitLength = ConfigPairParser.GetInt(pairs, "bitLength", 0);
            spec.Formula = ConfigPairParser.Get(pairs, "formula", "");
            spec.Unit = ConfigPairParser.Get(pairs, "unit",
                        ConfigPairParser.Get(pairs, "measuredUnit", ""));
            spec.ExtractRegex = ConfigPairParser.Get(pairs, "extractRegex", "");
            spec.PostType = ConfigPairParser.Get(pairs, "postType", "");
            spec.Trim = ConfigPairParser.GetBool(pairs, "trim", true);
            spec.RemoveSpaces = ConfigPairParser.GetBool(pairs, "removeSpaces", false);
            spec.Padding = ConfigPairParser.Get(pairs, "padding", "");
            spec.HexSeparator = ConfigPairParser.Get(pairs, "hexSeparator", " ");
            spec.ForbiddenDtcFile = ConfigPairParser.Get(pairs, "forbiddenDtcFile", "");
            spec.ForbiddenDtcSheet = ConfigPairParser.Get(pairs, "forbiddenDtcSheet", "");
            spec.ForbiddenDtcColumn = ConfigPairParser.Get(pairs, "forbiddenDtcColumn", "C");
            spec.Validation = ValueValidation.FromPairs(pairs);
            spec.Validation.SetForbiddenDtcSource(spec.ForbiddenDtcFile, spec.ForbiddenDtcSheet, spec.ForbiddenDtcColumn);
            return spec;
        }

        /// <summary>Build a DecodeSpec from a JSON object node (used by decodeProfile/decodeSpecs).</summary>
        public static DecodeSpec FromJson(Dictionary<string, object> node)
        {
            var spec = new DecodeSpec();
            spec.Name = JsonStr(node, "name", JsonStr(node, "measurementName", ""));
            spec.Offset = JsonInt(node, "valueOffset", JsonInt(node, "offset", 0));
            spec.Length = JsonInt(node, "valueLength", JsonInt(node, "length", -1));
            spec.Type = JsonStr(node, "valueType", JsonStr(node, "type", "hex"));
            spec.Endian = JsonStr(node, "endian", JsonStr(node, "byteOrder", "big"));
            spec.Encoding = JsonStr(node, "encoding", "ascii");
            spec.DateEncoding = JsonStr(node, "dateEncoding", "bcd");
            spec.DateFormat = JsonStr(node, "dateFormat", "yyyyMMdd");
            spec.BitOffset = JsonInt(node, "bitOffset", 0);
            spec.BitLength = JsonInt(node, "bitLength", 0);
            spec.Formula = JsonStr(node, "formula", "");
            spec.Unit = JsonStr(node, "unit", JsonStr(node, "measuredUnit", ""));
            spec.ExtractRegex = JsonStr(node, "extractRegex", "");
            spec.PostType = JsonStr(node, "postType", "");
            spec.Trim = JsonBool(node, "trim", true);
            spec.RemoveSpaces = JsonBool(node, "removeSpaces", false);
            spec.Padding = JsonStr(node, "padding", "");
            spec.HexSeparator = JsonStr(node, "hexSeparator", " ");
            spec.ForbiddenDtcFile = JsonStr(node, "forbiddenDtcFile", "");
            spec.ForbiddenDtcSheet = JsonStr(node, "forbiddenDtcSheet", "");
            spec.ForbiddenDtcColumn = JsonStr(node, "forbiddenDtcColumn", "C");
            spec.Validation = ValueValidation.FromJson(node);
            spec.Validation.SetForbiddenDtcSource(spec.ForbiddenDtcFile, spec.ForbiddenDtcSheet, spec.ForbiddenDtcColumn);
            return spec;
        }

        public bool HasExplicitDecode()
        {
            string t = Normalize(Type);
                        return !string.IsNullOrEmpty(Name) || Offset != 0 || Length >= 0 || t != "hex" ||
                   !string.IsNullOrEmpty(Formula) || !string.IsNullOrEmpty(ExtractRegex) ||
                                         !string.IsNullOrEmpty(Unit) || BitLength > 0 || HexSeparator != " " || HasValidation;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
        }

        private static string JsonStr(Dictionary<string, object> node, string key, string fallback)
        {
            object value;
            if (node.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return fallback;
        }

        private static int JsonInt(Dictionary<string, object> node, string key, int fallback)
        {
            object value;
            if (node.TryGetValue(key, out value) && value != null)
            {
                if (value is long) return (int)(long)value;
                if (value is double) return (int)(double)value;
                int parsed;
                if (int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out parsed))
                    return parsed;
            }
            return fallback;
        }

        private static bool JsonBool(Dictionary<string, object> node, string key, bool fallback)
        {
            object value;
            if (node.TryGetValue(key, out value) && value != null)
            {
                if (value is bool) return (bool)value;
                if (value is long) return (long)value != 0;
                bool parsed;
                if (bool.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out parsed))
                    return parsed;
            }
            return fallback;
        }
    }

    /// <summary>One decoded measurement. Port of EonTest DecodedMeasurement.</summary>
    public class DecodedMeasurement
    {
        public string Name = "";
        public object RawValue;
        public object Value;
        public string RawHex = "";
        public string Unit = "";
        public string Type = "";
        public string Formula = "";
        public bool Passed = true;
        public string ErrorMessage = "";

        public Dictionary<string, object> ToMap()
        {
            return new Dictionary<string, object>
            {
                { "name", Name },
                { "rawValue", RawValue },
                { "value", Value },
                { "rawHex", RawHex },
                { "unit", Unit },
                { "type", Type },
                { "formula", Formula },
                { "passed", Passed },
                { "error", ErrorMessage }
            };
        }
    }

    /// <summary>Result of decoding one response. Port of EonTest DecodeResult.</summary>
    public class DecodeResult
    {
        public byte[] Payload;
        public readonly List<DecodedMeasurement> Measurements = new List<DecodedMeasurement>();
        public string ErrorMessage = "";
        public bool Success = false;

        public string RawHex
        {
            get { return Payload == null ? "" : HexUtils.ToHexString(Payload); }
        }
    }
}
