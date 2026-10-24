using System;
using System.IO;
using System.Collections.Generic;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using Jabil.Uds.Core;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_ExcelRunAll — 一键跑完整张共享 Excel 里的所有 UDS 步骤（按行顺序），
    /// 自动复用同一个 DoIP 会话（connectionMode=session）。这是"最终用 Excel 驱动"的终极入口。
    ///
    /// 脚本示例:
    ///   DoIP_UDS_ExcelRunAll("D:\\Tests\\real-device-acceptance.xlsx", "测试工步表")
    ///   ($pass,$fail,$total) = DoIP_UDS_ExcelRunAll("D:\\Tests\\real-device-acceptance.xlsx", "测试工步表")
    /// </summary>
    internal class DoIP_UDS_ExcelRunAll : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_ExcelRunAll(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string excelPath = ArgString(0, "Excel Path", "");
            string sheetName = ArgString(1, "Sheet Name", ExcelStepReader.DefaultSheetName);
            int maxSteps = ArgInt(2, "Max Steps (0=all)", 0);
            string sessionKeyOverride = ArgString(3, "ECU/Session Key Override", "");
            string defaultHost = ArgString(4, "Default Host (fallback)", "");

            if (string.IsNullOrEmpty(excelPath) || !File.Exists(excelPath))
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelRunAll", 2, "DoIP_UDS_ExcelRunAll: Excel file not found: '" + excelPath + "'");
                return testResult;
            }
            if (string.IsNullOrEmpty(sheetName)) sheetName = ExcelStepReader.DefaultSheetName;

            List<ExcelStepRow> rows;
            try
            {
                rows = ExcelStepReader.ReadAllRows(excelPath, sheetName);
            }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelRunAll", 2, "DoIP_UDS_ExcelRunAll: failed to read Excel: " + ex.Message);
                return testResult;
            }

            if (rows.Count == 0)
            {
                StepRunner.PublishError(this, "DoIP_UDS_ExcelRunAll", 2, "DoIP_UDS_ExcelRunAll: no steps found in sheet '" + sheetName + "'.");
                return testResult;
            }

            string excelDir = Path.GetDirectoryName(Path.GetFullPath(excelPath));
            // 设备通道映射表（Sheet2）可选：有则按设备名回填 host/port/targetAddress，没有也不影响
            var deviceMap = new Dictionary<string, DeviceInfo>();
            try { deviceMap = ExcelStepReader.ReadDeviceMap(excelPath); } catch (Exception) { }

            int passed = 0;
            int failed = 0;
            int runCount = 0;
            string firstError = "";

            foreach (ExcelStepRow row in rows)
            {
                if (!row.ShouldRun())
                {
                    StepRunner.SavePersistentSimple(this, row.StepNo, true, "", "SKIP", row);
                    runCount++;
                    continue;
                }
                DeviceInfo device = null;
                if (!string.IsNullOrEmpty(row.DeviceName)) deviceMap.TryGetValue(row.DeviceName, out device);

                try
                {
                    bool ok;
                    if (row.IsUdsStep())
                    {
                        row.Config1 = ExpandVariables(row.Config1);
                        row.Config2 = ExpandVariables(row.Config2);
                        row.Config3 = ExpandVariables(row.Config3);
                        row.DecodeConfig = ExpandVariables(row.DecodeConfig);
                        UdsStepConfig cfg = row.ToStepConfig(excelDir, device);
                        // 站点兜底：G 列和 Sheet2 都没有 host 时，用默认 host
                        if (string.IsNullOrEmpty(cfg.Host) && !string.IsNullOrEmpty(defaultHost))
                            cfg.Host = defaultHost.Trim();
                        string sessionKey = string.IsNullOrEmpty(sessionKeyOverride) ? cfg.SessionKey : sessionKeyOverride;
                        UdsStepResult result = UdsRequestExecutor.ExecuteStep(cfg, sessionKey);
                        ok = result.Success;
                        string measuredValue = result.FirstValue == null ? ""
                            : Convert.ToString(result.FirstValue, System.Globalization.CultureInfo.InvariantCulture);
                        string values, units, limits;
                        StepRunner.BuildMeasurements(result, cfg, row, out values, out units, out limits);
                        StepRunner.SavePersistentResult(this, row.StepNo, ok, result,
                            values, units, limits, cfg, row);
                        if (!ok)
                        {
                            UpdateStatusKey("DoIP_UDS_ExcelRunAll: FAIL 工步号 " + row.StepNo + " - " + result.Error);
                            if (string.IsNullOrEmpty(firstError))
                                firstError = "工步号 " + row.StepNo + ": " + result.Error;
                        }
                    }
                    else if (StepInterop.IsDelayRow(row))
                    {
                        int ms = StepInterop.DelayMsFromConfig(row.Config1, 1000);
                        System.Threading.Thread.Sleep(ms);
                        ok = true;
                        StepRunner.SavePersistentSimple(this, row.StepNo, true, ms.ToString(), "PASS", row);
                        UpdateStatusKey("DoIP_UDS_ExcelRunAll: DELAY " + ms + "ms 工步号 " + row.StepNo);
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
                        string clicked;
                        ok = StepInterop.ShowMessageBox(msg, ttl, okCancel, out clicked);
                        StepRunner.SavePersistentSimple(this, row.StepNo, ok, clicked, ok ? "PASS" : "FAIL", row);
                        UpdateStatusKey("DoIP_UDS_ExcelRunAll: MESSAGE " + clicked + " 工步号 " + row.StepNo);
                        if (!ok && string.IsNullOrEmpty(firstError))
                            firstError = "工步号 " + row.StepNo + ": operator clicked " + clicked;
                    }
                    else
                    {
                        // 其它协议行（电源/SCPI/Modbus 等）：本库不处理，跳过
                        continue;
                    }

                    if (maxSteps > 0 && runCount >= maxSteps) break;
                    runCount++;
                    if (ok) passed++;
                    else failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (string.IsNullOrEmpty(firstError))
                        firstError = "工步号 " + row.StepNo + ": " + ex.Message;
                    UpdateStatusKey("DoIP_UDS_ExcelRunAll: EXCEPTION 工步号 " + row.StepNo + " - " + ex.Message);
                }
            }

            SetReturn(0, passed);
            SetReturn(1, failed);
            SetReturn(2, runCount);
            SetReturn(3, failed == 0 ? "PASS" : "FAIL");
            SetReturn(4, firstError);
            UpdateStatusKey("DoIP_UDS_ExcelRunAll: " + passed + "/" + runCount + " passed" +
                         (failed > 0 ? ", failed: " + failed + " - " + firstError : ""));

            // ZLG 风格：函数不设 Fail，整表判定放返回值（$pass/$fail/$total/$verdict），
            // 由脚本自行处理；整表在函数内部已全部执行完，失败不会终止后续脚本。
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
