using System;
using System.Collections.Generic;
using UdsCore;
using Jabil.Uds.Core;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// Shared logic to run a UDS step and publish results into the variable space.
    /// Used by DoIP_UDS_Step (config-driven) and DoIP_UDS_ExcelStep (Excel-driven).
    /// </summary>
    internal static class StepRunner
    {
        public static UdsStepResult Run(UdsStepConfig cfg, string sessionKey)
        {
            return UdsRequestExecutor.ExecuteStep(cfg, sessionKey);
        }

        /// <summary>
        /// Publish a step result as ReturnValue0..16 (ZLG ZUDS style: the function never fails
        /// the script; the real status is exposed through return values so the script decides).
        ///   ReturnValue0 = status (0=success, 1=UDS step failed)
        ///   ReturnValue1 = combined response hex
        ///   ReturnValue2 = first measurement name
        ///   ReturnValue3 = first engineering value
        ///   ReturnValue4 = first raw value
        ///   ReturnValue5 = unit
        ///   ReturnValue6 = all responses joined ' | '
        ///   ReturnValue7 = request commands joined ' | '
        ///   ReturnValue8 = detailed report text
        ///   ReturnValue9 = PASS / FAIL text
        ///   ReturnValue10 = error message ("" on success)
        ///   ReturnValue11 = Excel 测试项名称 (E column)          [Excel-driven only]
        ///   ReturnValue12 = Excel 下限值 (J column)
        ///   ReturnValue13 = Excel 上限值 (K column)
        ///   ReturnValue14 = Excel 单位   (L column)
        ///   ReturnValue15 = Excel 超时ms (M column)
        ///   ReturnValue16 = 值校验判定 PASS / FAIL / N/A
        ///                   (优先 expectedValue 字符串/状态/数值唯一正确值；否则按 J/K 数值上下限)
        /// Key line (verdict) goes through UpdateStatusTextFromUpdateStatus; full report is verbose.
        /// </summary>
        /// <summary>
        /// Publish a step result as ReturnValue0..6 (fixed, script-friendly layout):
        ///   ReturnValue0 = 结果：布尔 true=PASS，false=FAIL/ERROR（UDS 收发 + 值校验综合）
        ///   ReturnValue1 = 名称：单测量项=E 列测试项名（无则测量名/G 列 testItem）；多测量项=各测量项名称 '|' 分隔
        ///   ReturnValue2 = UDS 请求 HEX（多命令 ' | ' 分隔）
        ///   ReturnValue3 = UDS 响应 HEX（dataFrom 选中；mismatch 时保留实际收到的响应）
        ///   ReturnValue4 = 测试值（解码工程值；多测量项 '|' 分隔，与 5/6 按位置对齐）
        ///   ReturnValue5 = 单位（多测量项 '|' 分隔）
        ///   ReturnValue6 = 上下限（"下限~上限"；多测量项 '|' 分隔；per-item 校验键优先）
        ///   ReturnValue7 = 测试类型码：0=无判定，1=报文匹配，2=完全相等，3=包含，
        ///                  4=不包含，5=长度，6=正则，7=数值范围，8=DTC校验，9=日期校验
        /// 失败原因通过关键信息通道（UpdateStatusTextFromUpdateStatus）打印，不进返回值。
        /// </summary>
        public static void Publish(UdsTestBase test, string prefix, UdsStepResult result,
                                   UdsStepConfig cfg = null, ExcelStepRow row = null)
        {
            // RV0 结果：布尔 true=PASS，false=FAIL（综合 UDS 收发结果 + 值校验）
            string verdict = ComputeVerdict(result, cfg, row);
            bool passed = result.Success && verdict != "FAIL";
            string overall = passed ? "PASS" : "FAIL";
            test.SetReturn(0, BuildResultReturn(result, passed));

            // RV1 名称：多测量项返回测量项名列表，单测量项返回 E 列测试项名
            test.SetReturn(1, BuildItemName(result, cfg, row));

            // RV2 请求 / RV3 响应；请求项中的 {pad:...,retry:...,delay:...}
            // 是传输控制参数，不属于返回给脚本的报文内容。
            string requestText = DisplayRequestHex(result);
            test.SetReturn(2, requestText);
            test.SetReturn(3, result.CombinedResponseHex);

            // RV4/5/6 测试值 / 单位 / 上下限（多测量项 '|' 分隔）
            string values, units, limits;
            BuildMeasurements(result, cfg, row, out values, out units, out limits);
            test.SetReturn(4, values);
            test.SetReturn(5, units);
            test.SetReturn(6, limits);
            test.SetReturn(7, BuildTestTypeReturn(result, cfg, row));
            test.SetReturn(8, result.ToReportText());
            test.SetReturn(9, overall);
            test.SetReturn(10, result.Error ?? "");

            string stepName = row != null ? row.StepNo : "";
            SavePersistentResult(test, stepName, passed, result, values, units, limits, cfg, row);

            // 关键信息：步骤判定 + 测量摘要 —— 走 UpdateStatusTextFromUpdateStatus
            test.UpdateStatusKey(KeyLine(prefix, result, overall));
            // 失败时把收发 trace 也打进关键通道（DebugView 可见），定位发了什么/回了什么/为什么超时
            if (!result.Success && !string.IsNullOrEmpty(result.Trace))
                test.UpdateStatusKey(prefix + " trace: " + result.Trace);
            // 详细报告（多行）—— verbose 通道（仅日志，不进返回值）
            test.UpdateStatus(result.ToReportText());
        }

        private static object BuildResultReturn(UdsStepResult result, bool passed)
        {
            if (result == null || result.Decode == null || result.Decode.Measurements.Count <= 1)
                return passed;

            var statuses = new List<string>();
            foreach (DecodedMeasurement measurement in result.Decode.Measurements)
                statuses.Add(measurement.Passed ? "true" : "false");
            return string.Join("|", statuses);
        }

        internal static void SavePersistentResult(UdsTestBase test, string stepNo, bool passed,
                                                   UdsStepResult result, string values, string units,
                                                   string limits, UdsStepConfig cfg, ExcelStepRow row)
        {
            string stepToken = (stepNo ?? "").Trim();
            string prefix = StepPrefix(stepToken);
            string request = DisplayRequestHex(result);
            string response = result == null ? "" : result.CombinedResponseHex;
            object valueObject = result == null ? null : result.FirstValue;
            string value = valueObject == null ? ""
                : Convert.ToString(valueObject, System.Globalization.CultureInfo.InvariantCulture);
            string name = result == null ? "" : (result.FirstName ?? "");
            string rawUnit = result == null ? "" : (result.FirstUnit ?? "");
            string unit = units ?? "";
            string error = result == null ? "" : (result.Error ?? "");
            test.SetPersistent(prefix + "_Passed", passed);
            test.SetPersistent(prefix + "_Status", passed ? "PASS" : "FAIL");
            test.SetPersistent(prefix + "_Request", request);
            test.SetPersistent(prefix + "_Response", response);
            test.SetPersistent(prefix + "_Value", valueObject ?? "");
            test.SetPersistent(prefix + "_Name", name);
            test.SetPersistent(prefix + "_Unit", unit);
            test.SetPersistent(prefix + "_RawUnit", rawUnit);
            string rawValue = result != null && result.Decode != null && result.Decode.Measurements.Count > 0
                ? (result.Decode.Measurements[0].RawValue ?? "").ToString() : "";
            string rawHex = result != null && result.Decode != null && result.Decode.Measurements.Count > 0
                ? (result.Decode.Measurements[0].RawHex ?? "") : "";
            test.SetPersistent(prefix + "_RawValue", rawValue);
            test.SetPersistent(prefix + "_RawHex", rawHex);
            test.SetPersistent(prefix + "_Values", values ?? "");
            test.SetPersistent(prefix + "_Units", units ?? "");
            test.SetPersistent(prefix + "_Limits", limits ?? "");
            test.SetPersistent(prefix + "_Error", error);
            test.SetPersistent(prefix + "_Trace", result == null ? "" : (result.Trace ?? ""));
            test.SetPersistent(prefix + "_FailureStage", FailureStage(result, passed, cfg));
            test.SetPersistent(prefix + "_expectedContext", ExpectedContextText(cfg));
            SaveCommandVariables(test, prefix, result);
            SaveMeasurementVariables(test, prefix, result, cfg);

            string alias = AliasFrom(row == null ? null : row.Config1, cfg);
            if (alias.Length == 0) return;
            test.SetPersistent(alias, valueObject ?? response);
            test.SetPersistent(alias + "_Passed", passed);
            test.SetPersistent(alias + "_Status", passed ? "PASS" : "FAIL");
            test.SetPersistent(alias + "_Request", request);
            test.SetPersistent(alias + "_Response", response);
            test.SetPersistent(alias + "_Value", valueObject ?? "");
            test.SetPersistent(alias + "_Name", name);
            test.SetPersistent(alias + "_Unit", unit);
            test.SetPersistent(alias + "_RawUnit", rawUnit);
            test.SetPersistent(alias + "_RawValue", rawValue);
            test.SetPersistent(alias + "_RawHex", rawHex);
            test.SetPersistent(alias + "_Values", values ?? "");
            test.SetPersistent(alias + "_Units", units ?? "");
            test.SetPersistent(alias + "_Limits", limits ?? "");
            test.SetPersistent(alias + "_expectedContext", ExpectedContextText(cfg));
            SaveMeasurementVariables(test, alias, result, cfg);
            SaveCommandVariables(test, alias, result);
        }

        private static void SaveCommandVariables(UdsTestBase test, string prefix, UdsStepResult result)
        {
            if (result == null) return;
            int requestCount = result.ActualRequestHex == null ? 0 : result.ActualRequestHex.Count;
            int responseCount = result.ResponseHexList == null ? 0 : result.ResponseHexList.Count;
            int count = Math.Max(requestCount, responseCount);
            test.SetPersistent(prefix + "_CommandCount", count);
            for (int i = 0; i < count; ++i)
            {
                string request = i < requestCount ? result.ActualRequestHex[i] : "";
                string response = i < responseCount ? result.ResponseHexList[i] : "";
                test.SetPersistent(prefix + "_Command_" + i + "_Request", request);
                test.SetPersistent(prefix + "_Command_" + i + "_Response", response);
                test.SetPersistent(prefix + "_Command_" + i + "_Sent", request.Length > 0);
            }
        }

        private static void SaveMeasurementVariables(UdsTestBase test, string prefix, UdsStepResult result,
                                                     UdsStepConfig cfg)
        {
            if (result == null || result.Decode == null) return;
            for (int i = 0; i < result.Decode.Measurements.Count; ++i)
            {
                DecodedMeasurement measurement = result.Decode.Measurements[i];
                string itemPrefix = prefix + "_" + i;
                test.SetPersistent(itemPrefix, measurement.Value ?? "");
                test.SetPersistent(itemPrefix + "_Value", measurement.Value ?? "");
                test.SetPersistent(itemPrefix + "_Name", measurement.Name ?? "");
                DecodeSpec spec = (cfg != null && i < cfg.DecodeSpecs.Count)
                    ? cfg.DecodeSpecs[i] : null;
                string criteria = CriteriaText(spec, i == 0 && cfg != null ? cfg.Validation : null,
                                               measurement.Unit ?? "");
                test.SetPersistent(itemPrefix + "_Unit", criteria);
                test.SetPersistent(itemPrefix + "_RawUnit", measurement.Unit ?? "");
                test.SetPersistent(itemPrefix + "_RawValue", measurement.RawValue ?? "");
                test.SetPersistent(itemPrefix + "_RawHex", measurement.RawHex ?? "");
                test.SetPersistent(itemPrefix + "_Passed", measurement.Passed);
                test.SetPersistent(itemPrefix + "_TestType", TestTypeCode(spec));
                test.SetPersistent(itemPrefix + "_Criteria", criteria);
            }
        }

        private static string DisplayRequestHex(UdsStepResult result)
        {
            if (result == null) return "";
            List<string> source = result.ActualRequestHex != null && result.ActualRequestHex.Count > 0
                ? result.ActualRequestHex : result.CommandHex;
            if (source == null) return "";
            var commands = new List<string>();
            foreach (string command in source)
            {
                string text = command ?? "";
                int annotationStart = text.IndexOf('{');
                if (annotationStart >= 0) text = text.Substring(0, annotationStart);
                commands.Add(text.Trim());
            }
            return string.Join(" | ", commands);
        }

        internal static void SavePersistentSimple(UdsTestBase test, string stepNo, bool passed,
                                                    string value, string status, ExcelStepRow row)
        {
            string prefix = StepPrefix(stepNo);
            if (prefix.EndsWith("_", StringComparison.Ordinal)) return;
            test.SetPersistent(prefix + "_Passed", passed);
            test.SetPersistent(prefix + "_Status", status ?? (passed ? "PASS" : "FAIL"));
            test.SetPersistent(prefix + "_Value", value ?? "");
            test.SetPersistent(prefix + "_Response", value ?? "");

            string alias = AliasFrom(row == null ? null : row.Config1);
            if (alias.Length == 0) return;
            test.SetPersistent(alias, value ?? "");
            test.SetPersistent(alias + "_Passed", passed);
            test.SetPersistent(alias + "_Status", status ?? (passed ? "PASS" : "FAIL"));
            test.SetPersistent(alias + "_Value", value ?? "");
            test.SetPersistent(alias + "_Response", value ?? "");
        }

        private static string AliasFrom(string config)
        {
            return AliasFrom(config, null);
        }

        private static string AliasFrom(string config, UdsStepConfig cfg)
        {
            if (!string.IsNullOrWhiteSpace(config))
            {
                var pairs = ConfigPairParser.Parse(config);
                string alias;
                if (!pairs.TryGetValue("saveAs", out alias)) pairs.TryGetValue("resultName", out alias);
                if (!string.IsNullOrWhiteSpace(alias)) return alias.Trim();
            }
            return cfg == null || string.IsNullOrWhiteSpace(cfg.SaveAs) ? "" : cfg.SaveAs.Trim();
        }

        private static string StepPrefix(string stepToken)
        {
            string normalized = (stepToken ?? "").Trim().Replace('.', '_');
            return normalized.Length == 0 ? "Result_step" : "Result_step_" + normalized;
        }

        /// <summary>
        /// RV1：多测量项时返回各测量项名称（'|' 分隔），脚本可与 RV4/5/6 按位置对齐；
        /// 单测量项时返回 Excel E 列测试项名（无则解码测量名 / G 列 testItem）。
        /// </summary>
        private static string BuildItemName(UdsStepResult result, UdsStepConfig cfg, ExcelStepRow row)
        {
            if (result.Decode != null && result.Decode.Measurements.Count > 1)
            {
                var names = new List<string>();
                foreach (DecodedMeasurement m in result.Decode.Measurements)
                    names.Add(m.Name ?? "");
                return string.Join("|", names);
            }
            if (row != null && !string.IsNullOrEmpty(row.TestItem)) return row.TestItem;
            if (!string.IsNullOrEmpty(result.FirstName)) return result.FirstName;
            if (cfg != null) return cfg.TestItem;
            return "";
        }

        /// <summary>
        /// Build RV4/5/6 (values/units/limits). Multi-measurement rows are joined with '|'
        /// so each field aligns positionally (value[i], unit[i], limit[i] belong together).
        /// Limits come from the per-measurement spec validation keys (P 列 expectedMin/Max)
        /// first, otherwise the step-level J/K (or G 列 expectedMin/expectedMax) for item 0.
        /// </summary>
        internal static void BuildMeasurements(UdsStepResult result, UdsStepConfig cfg, ExcelStepRow row,
                                              out string values, out string units, out string limits)
        {
            var v = new List<string>();
            var u = new List<string>();
            var l = new List<string>();

            if (result.Decode != null && result.Decode.Measurements.Count > 0)
            {
                for (int i = 0; i < result.Decode.Measurements.Count; i++)
                {
                    DecodedMeasurement m = result.Decode.Measurements[i];
                    v.Add(m.Value == null ? ""
                        : Convert.ToString(m.Value, System.Globalization.CultureInfo.InvariantCulture));
                    DecodeSpec spec = (cfg != null && i < cfg.DecodeSpecs.Count) ? cfg.DecodeSpecs[i] : null;
                    string measurementUnit = m.Unit;
                    if (i == 0 && cfg != null && !string.IsNullOrWhiteSpace(cfg.Unit))
                        measurementUnit = cfg.Unit;
                    u.Add(CriteriaText(spec, i == 0 && cfg != null ? cfg.Validation : null, measurementUnit));

                    string minText = "", maxText = "";
                    if (spec != null && spec.Validation != null &&
                        (spec.Validation.Min.HasValue || spec.Validation.Max.HasValue))
                    {
                        if (spec.Validation.Min.HasValue)
                            minText = spec.Validation.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (spec.Validation.Max.HasValue)
                            maxText = spec.Validation.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else if (i == 0)
                    {
                        if (row != null) { minText = row.LowerLimit; maxText = row.UpperLimit; }
                        else if (cfg != null) { minText = cfg.LowerLimit; maxText = cfg.UpperLimit; }
                    }
                    l.Add(LimitPair(minText, maxText));
                }
            }
            else
            {
                // 无解码：测试值空（响应 HEX 在 RV3），unit 返回报文匹配条件
                v.Add("");
                u.Add(cfg != null && !string.IsNullOrWhiteSpace(cfg.Unit)
                    ? cfg.Unit.Trim() : ExpectedContextText(cfg));
                string minText = row != null ? row.LowerLimit : (cfg != null ? cfg.LowerLimit : "");
                string maxText = row != null ? row.UpperLimit : (cfg != null ? cfg.UpperLimit : "");
                l.Add(LimitPair(minText, maxText));
            }

            values = string.Join("|", v);
            units = string.Join("|", u);
            limits = string.Join("|", l);
        }

        internal static string ExpectedContextText(UdsStepConfig cfg)
        {
            if (cfg == null) return "";
            var prefixes = SplitPipe(cfg.ExpectedPrefix);
            var responses = SplitPipe(cfg.ExpectedResponse);
            var suffixes = SplitPipe(cfg.ExpectedSuffix);
            var offsets = SplitPipe(cfg.ExpectedByteOffset);
            var byteValues = SplitPipe(cfg.ExpectedByteValue);
            int index = cfg.HasExplicitDataFrom && cfg.DataFrom >= 0 ? cfg.DataFrom : -1;
            int defaultCount = Math.Max(prefixes.Count,
                Math.Max(responses.Count, Math.Max(suffixes.Count, Math.Max(offsets.Count, byteValues.Count))));
            if (index < 0) index = Math.Max(0, defaultCount - 1);
            var parts = new List<string>();
            string value;
            if (TryGetAt(prefixes, index, out value)) parts.Add(value);
            if (TryGetAt(responses, index, out value)) parts.Add(value);
            if (TryGetAt(suffixes, index, out value)) parts.Add(value);
            string offset, byteValue;
            if (TryGetAt(offsets, index, out offset))
            {
                string byteText = TryGetAt(byteValues, index, out byteValue) ? byteValue : "";
                parts.Add(offset + (byteText.Length > 0 ? ":" + byteText : ""));
            }
            return string.Join(";", parts);
        }

        private static bool TryGetAt(List<string> values, int index, out string value)
        {
            value = index >= 0 && index < values.Count ? values[index].Trim() : "";
            return value.Length > 0;
        }

        private static List<string> SplitPipe(string text)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return values;
            foreach (string value in text.Split('|')) values.Add(value.Trim());
            return values;
        }

        private static object BuildTestTypeReturn(UdsStepResult result, UdsStepConfig cfg, ExcelStepRow row)
        {
            var codes = new List<string>();
            string responseCode = ResponseMatchTypeCode(cfg);
            if (result != null && result.Decode != null && result.Decode.Measurements.Count > 0)
            {
                for (int i = 0; i < result.Decode.Measurements.Count; ++i)
                {
                    DecodeSpec spec = cfg != null && i < cfg.DecodeSpecs.Count ? cfg.DecodeSpecs[i] : null;
                    string code = TestTypeCodes(spec);
                    if (i == 0 && cfg != null) code = JoinCodes(code, TestTypeCodes(cfg.Validation));
                    if (i == 0 && code.Length == 0 && HasNumericLimits(cfg, row)) code = "7";
                    // 报文匹配是收发合法性约束；若当前测量项已有值/长度/范围等
                    // 测试判定，只返回该测试判定类型，避免出现 1|5 这类组合码。
                    if (i == 0 && code.Length == 0) code = responseCode;
                    codes.Add(code.Length == 0 ? "0" : code);
                }
            }
            else
            {
                codes.Add(HasNumericLimits(cfg, row) ? "7" : (responseCode.Length == 0 ? "0" : responseCode));
            }
            if (codes.Count == 1 && !codes[0].Contains("|")) return int.Parse(codes[0]);
            return string.Join("|", codes);
        }

        private static bool HasNumericLimits(UdsStepConfig cfg, ExcelStepRow row)
        {
            string lower = row != null ? row.LowerLimit : (cfg == null ? "" : cfg.LowerLimit);
            string upper = row != null ? row.UpperLimit : (cfg == null ? "" : cfg.UpperLimit);
            double ignored;
            return double.TryParse(lower, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out ignored) ||
                   double.TryParse(upper, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out ignored);
        }

        internal static int TestTypeCode(DecodeSpec spec)
        {
            string codes = TestTypeCodes(spec);
            return codes.Length == 0 ? 0 : int.Parse(codes.Split('|')[0]);
        }

        internal static int TestTypeCode(ValueValidation v)
        {
            string codes = TestTypeCodes(v);
            return codes.Length == 0 ? 0 : int.Parse(codes.Split('|')[0]);
        }

        private static string TestTypeCodes(DecodeSpec spec)
        {
            if (spec == null) return "";
            string codes = TestTypeCodes(spec.Validation);
            if (codes.Length > 0) return codes;
            return string.Equals(spec.Type == null ? "" : spec.Type.Trim(), "date",
                StringComparison.OrdinalIgnoreCase) ? "9" : "";
        }

        private static string TestTypeCodes(ValueValidation v)
        {
            if (v == null || !v.HasAny) return "";
            if (v.ExpectedValue.Length > 0) return "2";
            if (v.StartsWith.Length > 0 || v.EndsWith.Length > 0 || v.Contains.Length > 0) return "3";
            if (v.NotContains.Length > 0) return "4";
            if (v.ExactLength.HasValue || v.MinLength.HasValue || v.MaxLength.HasValue) return "5";
            if (v.RegexPattern.Length > 0) return "6";
            if (v.ForbiddenDtcFile.Length > 0 || v.DtcStatusMask >= 0) return "8";
            if (v.MinDate.Length > 0 || v.MaxDate.Length > 0) return "9";
            if (v.Min.HasValue || v.Max.HasValue) return "7";
            return "";
        }

        private static string ResponseMatchTypeCode(UdsStepConfig cfg)
        {
            if (cfg == null) return "";
            return string.IsNullOrWhiteSpace(cfg.ExpectedPrefix) &&
                   string.IsNullOrWhiteSpace(cfg.ExpectedResponse) &&
                   string.IsNullOrWhiteSpace(cfg.ExpectedSuffix) &&
                   string.IsNullOrWhiteSpace(cfg.ExpectedByteOffset) ? "" : "1";
        }

        private static string JoinCodes(string first, string second)
        {
            if (string.IsNullOrEmpty(first)) return second;
            if (string.IsNullOrEmpty(second)) return first;
            return first + "|" + second;
        }

        internal static string CriteriaText(DecodeSpec spec, string fallback = "")
        {
            return CriteriaText(spec, null, fallback);
        }

        internal static string CriteriaText(DecodeSpec spec, ValueValidation fallbackValidation,
                                           string fallback = "")
        {
            ValueValidation v = spec != null && spec.Validation != null && spec.Validation.HasAny
                ? spec.Validation : fallbackValidation;
            if (v == null || !v.HasAny) return fallback ?? "";
            var parts = new List<string>();
            if (v.ExpectedValue.Length > 0) parts.Add(v.ExpectedValue);
            if (v.Contains.Length > 0) parts.Add("contains:" + v.Contains);
            if (v.NotContains.Length > 0) parts.Add("not-contains:" + v.NotContains);
            if (v.StartsWith.Length > 0) parts.Add("starts:" + v.StartsWith);
            if (v.EndsWith.Length > 0) parts.Add("ends:" + v.EndsWith);
            if (v.RegexPattern.Length > 0) parts.Add("regex:" + v.RegexPattern);
            if (v.ExactLength.HasValue) parts.Add("len=" + v.ExactLength.Value);
            if (v.MinLength.HasValue) parts.Add("len>=" + v.MinLength.Value);
            if (v.MaxLength.HasValue) parts.Add("len<=" + v.MaxLength.Value);
            if (v.Min.HasValue) parts.Add("min=" + v.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (v.Max.HasValue) parts.Add("max=" + v.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (v.ForbiddenDtcFile.Length > 0) parts.Add("forbidden-dtc-list");
            string criteria = string.Join(";", parts);
            if (string.IsNullOrWhiteSpace(fallback)) return criteria;
            return fallback.Trim() + " [" + criteria + "]";
        }

        private static string LimitPair(string minText, string maxText)
        {
            bool hasMin = !string.IsNullOrWhiteSpace(minText);
            bool hasMax = !string.IsNullOrWhiteSpace(maxText);
            if (!hasMin && !hasMax) return "";
            if (hasMin && hasMax) return minText.Trim() + "~" + maxText.Trim();
            return hasMin ? minText.Trim() + "~" : "~" + maxText.Trim();
        }

        /// <summary>
        /// Value-level verdict (AND across all specified rules):
        /// 1) P 列 per-measurement validation (每个测量项内嵌 expectedXxx，全部 PASS 才 PASS);
        /// 2) G 列 global ValueValidation (contains/regex/length/numeric-range/equality, on first value);
        /// 3) legacy expectedValue (equality on first value);
        /// 4) numeric J/K limits;
        /// 5) N/A.
        /// </summary>
        private static string ComputeVerdict(UdsStepResult result, UdsStepConfig cfg, ExcelStepRow row)
        {
            // 1) P 列 per-measurement + G 列全局（AND）
            string perItem = null, global = null;
            if (cfg != null && result.Decode != null && result.Decode.Measurements.Count > 0)
            {
                foreach (DecodedMeasurement measurement in result.Decode.Measurements)
                    if (!measurement.Passed) return "FAIL";
                perItem = ValueValidation.MultiVerdict(result.Decode.Measurements, cfg.DecodeSpecs);
            }
            if (cfg != null && cfg.HasValidation)
                global = cfg.Validation.Evaluate(result.FirstValue, result.CombinedResponseHex);
            if (perItem == "FAIL" || global == "FAIL") return "FAIL";
            if (perItem == "PASS" || global == "PASS") return "PASS";

            // 2) 单值相等（解码后第一个值）
            if (cfg != null && cfg.HasExpectedValue)
                return ValueCheck.ExpectedVerdict(result.FirstValue, cfg.ExpectedValue);
            // 3) Excel J/K 数值上下限
            if (row != null)
                return LimitVerdict(result.FirstValue, row.LowerLimit, row.UpperLimit);
            return "N/A";
        }

        /// <summary>
        /// Compare the measured value against Excel J/K limits.
        /// Returns PASS/FAIL when both limits parse and value is numeric; N/A otherwise.
        /// </summary>
        private static string LimitVerdict(object value, string lowerText, string upperText)
        {
            double v;
            if (!TryToDouble(value, out v)) return "N/A";
            bool hasLower = double.TryParse(lowerText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double lower);
            bool hasUpper = double.TryParse(upperText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double upper);
            if (!hasLower && !hasUpper) return "N/A";
            if (hasLower && v < lower) return "FAIL";
            if (hasUpper && v > upper) return "FAIL";
            return "PASS";
        }

        private static bool TryToDouble(object value, out double result)
        {
            result = 0.0;
            if (value == null) return false;
            if (value is double) { result = (double)value; return true; }
            if (value is long) { result = (long)value; return true; }
            if (value is ulong) { result = (ulong)value; return true; }
            if (value is int) { result = (int)value; return true; }
            return double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        /// <summary>One-line verdict for the key channel.</summary>
        private static string KeyLine(string prefix, UdsStepResult result, string overall)
        {
            if (result.Success)
            {
                string summary = "";
                if (result.Decode != null && result.Decode.Measurements.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (DecodedMeasurement m in result.Decode.Measurements)
                    {
                        string mv = m.Value == null ? ""
                            : Convert.ToString(m.Value, System.Globalization.CultureInfo.InvariantCulture);
                        parts.Add(m.Name + "=" + mv + m.Unit);
                    }
                    summary = string.Join(", ", parts);
                }
                else if (result.FirstValue != null)
                {
                    summary = Convert.ToString(result.FirstValue, System.Globalization.CultureInfo.InvariantCulture);
                }
                if (string.IsNullOrEmpty(summary)) summary = result.ResultText;
                return prefix + ": " + overall + "  " + summary;
            }
            return prefix + ": FAIL  - " + result.Error;
        }

        /// <summary>
        /// Publish a usage/config error WITHOUT failing the script (ZLG style).
        /// ReturnValue0 = false（布尔：ERROR 视为失败），其他字段空（固定布局同 Publish）。
        /// </summary>
        public static void PublishError(UdsTestBase test, string prefix, int status, string message)
        {
            test.SetReturn(0, false);
            test.SetReturn(1, "");
            test.SetReturn(2, "");
            test.SetReturn(3, "");
            test.SetReturn(4, "");
            test.SetReturn(5, "");
            test.SetReturn(6, "");
            test.SetReturn(7, "");
            test.SetReturn(8, "");
            test.SetReturn(9, "FAIL");
            test.SetReturn(10, message ?? "");
            test.SetPersistent("Result_step_Passed", false);
            test.SetPersistent("Result_step_Status", "FAIL");
            test.SetPersistent("Result_step_Request", "");
            test.SetPersistent("Result_step_Response", "");
            test.SetPersistent("Result_step_Value", "");
            test.SetPersistent("Result_step_Error", message ?? "");
            test.SetPersistent("Result_step_Trace", "");
            test.SetPersistent("Result_step_FailureStage", "CONFIG");
            test.SetPersistent("Result_step_expectedContext", "");
            test.UpdateStatusKey(prefix + ": ERROR - " + message);
        }

        private static string FailureStage(UdsStepResult result, bool passed, UdsStepConfig cfg)
        {
            if (passed) return "PASS";
            if (result == null) return "EXECUTION";

            string error = result.Error ?? "";
            if (error.IndexOf("mismatch", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RESPONSE_MISMATCH";
            if (result.Decode != null && result.Decode.Payload != null &&
                result.Decode.Measurements.Count > 0)
                return "LIMIT";
            if (error.IndexOf("decode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (result.Decode != null && !result.Decode.Success))
                return "DECODE";
            if (error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TIMEOUT";
            if (error.IndexOf("connect failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "CONNECT";
            if (error.IndexOf("routing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("activation", StringComparison.OrdinalIgnoreCase) >= 0)
                return "ROUTING";
            if (error.IndexOf("send", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("write failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("socket", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("远程主机强迫关闭", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("NACK", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SEND";
            return "EXECUTION";
        }
    }
}
