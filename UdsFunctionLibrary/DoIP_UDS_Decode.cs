using System;
using System.Collections.Generic;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_Decode — 用 P 列解码配置（JSON 或 key=value）解析一条原始 UDS 响应 HEX。
    /// 不依赖任何会话，可独立使用（也方便先在 JEMSts 里验证解码规则）。
    ///
    /// 脚本示例:
    ///   ($value,$name,$unit,$report,$criteria,$limits,$testType) = DoIP_UDS_Decode(
    ///       "62 F1 05 0B B8",
    ///       "valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4,expectedMin=0,expectedMax=5,unit=V")
    /// </summary>
    internal class DoIP_UDS_Decode : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_Decode(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string responseHex = ArgString(0, "Response Hex", "");
            string decodeConfig = ArgString(1, "Decode Config (P column)", "");
            string testItem = ArgString(2, "Test Item Name", "UDS Decode");

            byte[] response = HexUtils.TryParseHex(responseHex);
            if (response == null || response.Length == 0)
            {
                StepRunner.PublishError(this, "DoIP_UDS_Decode", 2, "DoIP_UDS_Decode: invalid response hex: '" + responseHex + "'");
                return testResult;
            }

            // Build a throwaway config to reuse decode-column parsing
            var cfg = UdsStepConfig.FromColumns("", decodeConfig);
            if (string.IsNullOrEmpty(cfg.DecodeProfile))
            {
                // nothing? still allow empty -> hex passthrough
            }
            List<DecodeSpec> specs = cfg.DecodeSpecs;
            if (specs.Count == 0)
            {
                // passthrough as hex
                var passthrough = new DecodeSpec { Name = testItem, Type = "hex", Offset = 0, Length = -1 };
                specs = new List<DecodeSpec> { passthrough };
            }

            DecodeResult decoded = ResponseDecoder.DecodeMany(response, specs);
            DecodedMeasurement first = decoded.Measurements.Count > 0 ? decoded.Measurements[0] : null;

            var criteria = new System.Collections.Generic.List<string>();
            var limits = new System.Collections.Generic.List<string>();
            var testTypes = new System.Collections.Generic.List<string>();
            for (int i = 0; i < decoded.Measurements.Count; ++i)
            {
                DecodeSpec spec = i < specs.Count ? specs[i] : null;
                DecodedMeasurement measurement = decoded.Measurements[i];
                criteria.Add(StepRunner.CriteriaText(spec, null, measurement.Unit));
                string min = "", max = "";
                if (spec != null && spec.Validation != null)
                {
                    if (spec.Validation.Min.HasValue)
                        min = spec.Validation.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (spec.Validation.Max.HasValue)
                        max = spec.Validation.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                limits.Add((min.Length == 0 && max.Length == 0) ? ""
                    : (min.Length > 0 ? min : "") + "~" + (max.Length > 0 ? max : ""));
                testTypes.Add(StepRunner.TestTypeCode(spec).ToString());
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("Response: " + HexUtils.ToHexString(response));
            foreach (DecodedMeasurement m in decoded.Measurements)
            {
                string v = m.Value == null ? "" : Convert.ToString(m.Value, System.Globalization.CultureInfo.InvariantCulture);
                report.AppendLine(string.Format("{0} = {1} {2}  [raw: {3}] {4}",
                    m.Name, v, m.Unit, m.RawHex, m.Passed ? "PASS" : "FAIL"));
            }

            SetReturn(0, first != null ? first.Value : null);
            SetReturn(1, first != null ? first.Name : "");
            SetReturn(2, first != null ? first.Unit : "");
            SetReturn(3, decoded.Success ? 0 : 1);
            SetReturn(4, report.ToString().TrimEnd());
            SetReturn(5, string.Join("|", criteria));
            SetReturn(6, string.Join("|", limits));
            SetReturn(7, testTypes.Count == 1 && !testTypes[0].Contains("|")
                ? (object)int.Parse(testTypes[0]) : string.Join("|", testTypes));

            UpdateStatusKey(report.ToString().TrimEnd());

            // ZLG 风格：函数不设 Fail，解码成败放 $status（ReturnValue3，0=成功/1=失败），由脚本自行判断。
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
