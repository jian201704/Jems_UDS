using System;
using System.IO;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using Jabil.Uds.Core;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_ExcelStep — 直接读共享 Excel 测试工步表的某一行（按工步号），
    /// 取 G 列（配置参数1）+ P 列（解码配置）执行 UDS 步骤。
    /// 这是"同一张测试工步表，JEMSts 与 EonTest 共用"的入口。
    ///
    /// 脚本示例:
    ///   ($result,$testItem,$req,$resp,$value,$unit,$limits,$testType) =
    ///       DoIP_UDS_ExcelStep("D:\\Tests\\real-device-acceptance.xlsx", "测试工步表", "5")
    /// </summary>
    internal class DoIP_UDS_ExcelStep : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_ExcelStep(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string excelPath = ArgString(0, "Excel Path", "");
            string sheetName = ArgString(1, "Sheet Name", ExcelStepReader.DefaultSheetName);
            string stepNo = ArgString(2, "工步号 (Step No)", "");
            string sessionKeyOverride = ArgString(3, "ECU/Session Key Override", "");
            string defaultHost = ArgString(4, "Default Host (fallback)", "");

            if (string.IsNullOrEmpty(excelPath) || !File.Exists(excelPath))
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelStep", 2, "DoIP_UDS_ExcelStep: Excel file not found: '" + excelPath + "'");
                return testResult;
            }
            if (string.IsNullOrEmpty(sheetName)) sheetName = ExcelStepReader.DefaultSheetName;
            if (string.IsNullOrEmpty(stepNo))
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelStep", 2, "DoIP_UDS_ExcelStep: missing 工步号 (Argument2).");
                return testResult;
            }

            ExcelStepRow row;
            try
            {
                row = ExcelStepReader.ReadRowByStepNo(excelPath, stepNo, sheetName);
            }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelStep", 2, "DoIP_UDS_ExcelStep: failed to read Excel: " + ex.Message);
                return testResult;
            }

            if (row == null)
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelStep", 2, "DoIP_UDS_ExcelStep: 工步号 '" + stepNo + "' not found in sheet '" + sheetName + "'.");
                return testResult;
            }

            if (!row.ShouldRun())
            {
                ClearReturns();
                StepRunner.SavePersistentSimple(this, stepNo, true, "", "SKIP", row);
                UpdateStatusKey("DoIP_UDS_ExcelStep: SKIP disabled 工步号 " + stepNo);
                testResult.Status = TestStatus.Pass;
                return testResult;
            }

            string excelDir = Path.GetDirectoryName(Path.GetFullPath(excelPath));
            // 设备通道映射表（Sheet2）可选：有则按设备名回填 host/port/targetAddress，没有也不影响
            DeviceInfo device = null;
            try
            {
                var deviceMap = ExcelStepReader.ReadDeviceMap(excelPath);
                if (!string.IsNullOrEmpty(row.DeviceName) && deviceMap.TryGetValue(row.DeviceName, out device)) { }
            }
            catch (Exception) { device = null; }

            row.Config1 = ExpandVariables(row.Config1);
            row.Config2 = ExpandVariables(row.Config2);
            row.Config3 = ExpandVariables(row.Config3);
            row.DecodeConfig = ExpandVariables(row.DecodeConfig);
            UdsStepConfig cfg = row.ToStepConfig(excelDir, device, excelPath);
            string sessionKey = string.IsNullOrEmpty(sessionKeyOverride) ? cfg.SessionKey : sessionKeyOverride;

            // 站点兜底：G 列和 Sheet2 都没有 host 时，用默认 host（每站一个 DoIP 网关 IP）
            if (string.IsNullOrEmpty(cfg.Host) && !string.IsNullOrEmpty(defaultHost))
                cfg.Host = defaultHost.Trim();

            if (row.IsUdsStep())
            {
                UdsStepResult result = UdsRequestExecutor.ExecuteStep(cfg, sessionKey);
                StepRunner.Publish(this, "DoIP_UDS_ExcelStep", result, cfg, row);
            }
            else if (StepInterop.IsDelayRow(row))
            {
                int ms = StepInterop.DelayMsFromConfig(row.Config1, 1000);
                System.Threading.Thread.Sleep(ms);
                StepRunner.SavePersistentSimple(this, stepNo, true, ms.ToString(), "PASS", row);
                ClearReturns();   // 清掉上一步（如 10 01）残留的 RV，避免显示旧数据
                UpdateStatusKey("DoIP_UDS_ExcelStep: DELAY " + ms + "ms 工步号 " + stepNo);
            }
            else if (StepInterop.IsMessageRow(row))
            {
                var kv = ConfigPairParser.Parse(row.Config1);
                string msg = "请点击确定继续";
                string ttl = "JEMSts 提示";
                string btns = "ok";
                string v;
                if (kv.TryGetValue("message", out v) && v.Length > 0) msg = v;
                if (kv.TryGetValue("title", out v) && v.Length > 0) ttl = v;
                if (kv.TryGetValue("buttons", out v) && v.Length > 0) btns = v;
                bool okCancel = btns.IndexOf("okcancel", StringComparison.OrdinalIgnoreCase) >= 0
                             || btns.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0;
                ClearReturns();   // 清掉上一步残留的 RV，只保留本次点击选项
                string clicked;
                bool ok = StepInterop.ShowMessageBox(msg, ttl, okCancel, out clicked);
                SetReturn(0, ok ? 0 : 1);
                SetReturn(1, clicked);
                StepRunner.SavePersistentSimple(this, stepNo, ok, clicked, ok ? "PASS" : "FAIL", row);
                UpdateStatusKey("DoIP_UDS_ExcelStep: MESSAGE " + clicked + " 工步号 " + stepNo);
            }
            else
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelStep", 2,
                    "DoIP_UDS_ExcelStep: 工步号 '" + stepNo + "' is not a supported step (UDS / delay / message).");
                return testResult;
            }

            // ZLG 风格：函数不设 Fail，成败状态放返回值（$result / ReturnValue0），由脚本自行判断，失败不终止。
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
