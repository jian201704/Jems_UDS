using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TestCore
{
    /// <summary>
    /// Response decode engine. Direct port of EonTest Infrastructure ResponseDecoder.
    /// Extracts bytes at offset/length, converts per type/endian, applies formula.
    /// Protocol-agnostic: works on any payload (DoIP UDS, Modbus, CAN, serial, ...).
    /// </summary>
    public static class ResponseDecoder
    {
        public static DecodeResult Decode(byte[] payload, DecodeSpec spec)
        {
            var result = new DecodeResult { Payload = payload };
            var measurement = new DecodedMeasurement
            {
                Name = spec.Name,
                Unit = spec.Unit,
                Type = Normalize(spec.Type),
                Formula = spec.Formula
            };

            int offset = spec.Offset;
            int length = spec.Length < 0 ? payload.Length - offset : spec.Length;
            if (offset < 0 || length <= 0 || offset > payload.Length || length > payload.Length - offset)
            {
                measurement.ErrorMessage = "decode range is outside payload";
            }
            else
            {
                byte[] selected = new byte[length];
                Array.Copy(payload, offset, selected, 0, length);
                measurement.RawHex = HexUtils.ToHexString(selected);
                bool little = Normalize(spec.Endian) == "little";
                string type = measurement.Type;

                if (type == "hex")
                {
                    measurement.RawValue = measurement.Value = measurement.RawHex;
                }
                else if (type == "string")
                {
                    string decoded = DecodeString(selected, spec.Encoding);
                    measurement.RawValue = decoded;
                    measurement.Value = spec.Trim ? decoded.Trim() : decoded;
                }
                else if (type == "bits")
                {
                    if (spec.BitOffset < 0 || spec.BitLength <= 0 || spec.BitLength > 64 ||
                        spec.BitOffset + spec.BitLength > selected.Length * 8)
                    {
                        measurement.ErrorMessage = "invalid bit range";
                    }
                    else
                    {
                        ulong raw = UnsignedValue(selected, little);
                        ulong mask = spec.BitLength == 64
                            ? ulong.MaxValue
                            : (1UL << spec.BitLength) - 1;
                        ulong value = (raw >> spec.BitOffset) & mask;
                        measurement.RawValue = measurement.Value = value;
                    }
                }
                else
                {
                    string actualType = type;
                    if (actualType == "uint") actualType = "uint" + (length * 8);
                    if (actualType == "int") actualType = "int" + (length * 8);
                    if (actualType == "float") actualType = "float32";

                    if (actualType.StartsWith("uint", StringComparison.Ordinal))
                    {
                        int expected;
                        if (!TryBitsFromType(actualType, 4, out expected) || expected != length || expected > 8)
                            measurement.ErrorMessage = "invalid unsigned integer length";
                        else
                        {
                            ulong value = UnsignedValue(selected, little);
                            measurement.RawValue = measurement.Value = value;
                        }
                    }
                    else if (actualType.StartsWith("int", StringComparison.Ordinal))
                    {
                        int expected;
                        if (!TryBitsFromType(actualType, 3, out expected) || expected != length || expected > 8)
                            measurement.ErrorMessage = "invalid signed integer length";
                        else
                        {
                            long value = SignedValue(selected, little);
                            measurement.RawValue = measurement.Value = value;
                        }
                    }
                    else if (actualType == "float32" || actualType == "float64")
                    {
                        int expected = actualType == "float32" ? 4 : 8;
                        if (length != expected)
                            measurement.ErrorMessage = "invalid floating-point length";
                        else
                        {
                            // BitConverter uses the native (little-endian on x86) order.
                            // For big-endian wire bytes we must reverse first, so that
                            // e.g. "41 20 00 00" decodes to 10.0 (not 1.157E-41).
                            byte[] ordered = (byte[])selected.Clone();
                            if (!little) Array.Reverse(ordered);
                            double value;
                            if (expected == 4)
                                value = BitConverter.ToSingle(ordered, 0);
                            else
                                value = BitConverter.ToDouble(ordered, 0);
                            if (double.IsNaN(value) || double.IsInfinity(value))
                                measurement.ErrorMessage = "decoded floating-point value is not finite";
                            else
                                measurement.RawValue = measurement.Value = value;
                        }
                    }
                    else
                    {
                        measurement.ErrorMessage = "unsupported decode type: " + spec.Type;
                    }
                }
            }

            // extractRegex post-processing
            if (string.IsNullOrEmpty(measurement.ErrorMessage) && !string.IsNullOrEmpty(spec.ExtractRegex))
            {
                string source = measurement.Value == null ? "" : Convert.ToString(measurement.Value, System.Globalization.CultureInfo.InvariantCulture);
                Match match;
                try { match = Regex.Match(source, spec.ExtractRegex); }
                catch (ArgumentException) { measurement.ErrorMessage = "extractRegex is invalid"; match = null; }
                if (match != null && (!match.Success || match.Groups.Count < 2 || !match.Groups[1].Success))
                {
                    measurement.ErrorMessage = "extractRegex did not match";
                }
                else if (match != null)
                {
                    string captured = match.Groups[1].Value;
                    measurement.RawValue = measurement.Value = captured;
                    if (!string.IsNullOrEmpty(spec.PostType))
                    {
                        double value;
                        if (!double.TryParse(captured, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out value))
                            measurement.ErrorMessage = "postType conversion failed";
                        else if (Normalize(spec.PostType).StartsWith("int", StringComparison.Ordinal))
                            measurement.Value = (long)Math.Round(value);
                        else
                            measurement.Value = value;
                    }
                }
            }

            // formula
            if (string.IsNullOrEmpty(measurement.ErrorMessage) && !string.IsNullOrWhiteSpace(spec.Formula))
            {
                double input;
                double output;
                if (!ToDouble(measurement.Value, out input))
                    measurement.ErrorMessage = "formula input is not numeric";
                else if (!FormulaEvaluator.TryEvaluate(spec.Formula, input, out output, out string formulaError))
                    measurement.ErrorMessage = formulaError;
                else
                    measurement.Value = output;
            }

            measurement.Passed = string.IsNullOrEmpty(measurement.ErrorMessage);
            result.Measurements.Add(measurement);
            result.Success = measurement.Passed;
            if (!result.Success) result.ErrorMessage = measurement.ErrorMessage;
            return result;
        }

        public static DecodeResult DecodeMany(byte[] payload, List<DecodeSpec> specs)
        {
            var result = new DecodeResult { Payload = payload, Success = true };
            foreach (DecodeSpec spec in specs)
            {
                DecodeResult decoded = Decode(payload, spec);
                result.Measurements.AddRange(decoded.Measurements);
                if (!decoded.Success) result.Success = false;
            }
            if (!result.Success) result.ErrorMessage = "one or more decode items failed";
            return result;
        }

        // ---- helpers ----

        private static bool TryBitsFromType(string type, int prefixLength, out int bytes)
        {
            bytes = 0;
            string bitsText = type.Substring(prefixLength);
            int bits;
            if (!int.TryParse(bitsText, out bits) || bits <= 0) return false;
            bytes = bits / 8;
            return bits % 8 == 0;
        }

        private static ulong UnsignedValue(byte[] bytes, bool littleEndian)
        {
            ulong result = 0;
            if (littleEndian)
            {
                for (int i = bytes.Length - 1; i >= 0; i--)
                    result = (result << 8) | bytes[i];
            }
            else
            {
                foreach (byte b in bytes)
                    result = (result << 8) | b;
            }
            return result;
        }

        private static long SignedValue(byte[] bytes, bool littleEndian)
        {
            ulong raw = UnsignedValue(bytes, littleEndian);
            int bits = bytes.Length * 8;
            if (bits == 64) return unchecked((long)raw);
            ulong sign = 1UL << (bits - 1);
            ulong extended = (raw & sign) != 0 ? (raw | (~0UL << bits)) : raw;
            return unchecked((long)extended);
        }

        private static string DecodeString(byte[] bytes, string encoding)
        {
            string enc = Normalize(encoding);
            if (enc == "utf8" || enc == "utf-8")
                return Encoding.UTF8.GetString(bytes);
            if (enc == "utf16" || enc == "utf-16")
                return Encoding.Unicode.GetString(bytes);
            return Encoding.ASCII.GetString(bytes);
        }

        private static bool ToDouble(object value, out double result)
        {
            result = 0.0;
            if (value == null) return false;
            if (value is double) { result = (double)value; return !double.IsNaN(result) && !double.IsInfinity(result); }
            if (value is long) { result = (long)value; return true; }
            if (value is ulong) { result = (ulong)value; return true; }
            if (value is int) { result = (int)value; return true; }
            if (value is uint) { result = (uint)value; return true; }
            if (value is bool) { result = (bool)value ? 1.0 : 0.0; return true; }
            return double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
