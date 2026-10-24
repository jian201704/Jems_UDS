using System;
using System.Collections.Generic;
using System.IO;
using ExcelDecodeCore;

namespace UdsCore
{
    /// <summary>
    /// UDS-specific extensions over the shared ExcelDecodeCore sheet reader:
    /// detecting UDS steps, building a UdsStepConfig from G/P columns, and
    /// backfilling host/port/targetAddress from the 设备通道映射表 (Sheet2).
    /// </summary>
    public static class UdsExcelSteps
    {
        /// <summary>True when the row looks like a DoIP/UDS step (pluginId doip / config contains uds / action send_uds).</summary>
        public static bool IsUdsStep(this ExcelStepRow row)
        {
            if (row == null) return false;
            if (!string.IsNullOrEmpty(row.PluginId) &&
                row.PluginId.IndexOf("doip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.Config1.IndexOf("uds", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.Action.IndexOf("send_uds", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool ShouldRun(this ExcelStepRow row)
        {
            return row != null && row.IsEnabled;
        }

        /// <summary>Read all UDS steps from the shared 测试工步表 (protocol filter lives here, not in the shared core).</summary>
        public static List<ExcelStepRow> ReadAllUdsSteps(string excelPath, string sheetName = null)
        {
            var all = ExcelStepReader.ReadAllRows(excelPath, sheetName);
            var result = new List<ExcelStepRow>();
            foreach (ExcelStepRow row in all)
                if (row.IsUdsStep()) result.Add(row);
            return result;
        }

        /// <summary>
        /// Build a UdsStepConfig from G (配置参数1) + P (解码配置) columns, then backfill
        /// host/port/targetAddress from the Sheet2 device row.
        /// </summary>
        public static UdsStepConfig ToStepConfig(this ExcelStepRow row, string excelDirectory, DeviceInfo device = null,
                                                 string excelFilePath = "")
        {
            var cfg = UdsStepConfig.FromColumns(row.Config1, row.DecodeConfig);
            string dtcMarker = (row.Config1 + " " + row.Config2 + " " + row.Config3 + " " +
                                row.TestItem + " " + row.Remark).ToLowerInvariant();
            if (dtcMarker.Contains("见dtc检查清单") || dtcMarker.Contains("check dtc"))
            {
                var dtcSpec = new DecodeSpec
                {
                    Name = "DTC_Detect",
                    Offset = 0,
                    Length = -1,
                    Type = "hex",
                    HexSeparator = ""
                };
                dtcSpec.ForbiddenDtcFile = excelFilePath;
                dtcSpec.ForbiddenDtcSheet = "DCT检查清单";
                dtcSpec.ForbiddenDtcColumn = "C";
                var dtcOptions = ExcelDecodeCore.ConfigPairParser.Parse(row.Config1);
                dtcSpec.Validation = ExcelDecodeCore.ValueValidation.FromPairs(dtcOptions);
                dtcSpec.Validation.SetForbiddenDtcSource(
                    dtcSpec.ForbiddenDtcFile, dtcSpec.ForbiddenDtcSheet, dtcSpec.ForbiddenDtcColumn);
                cfg.DecodeSpecs.Clear();
                cfg.DecodeSpecs.Add(dtcSpec);
                cfg.HasDecodeConfig = true;
            }
            cfg.TestItem = string.IsNullOrEmpty(row.TestItem) ? ("UDS Step " + row.StepNo) : row.TestItem;
            cfg.LowerLimit = row.LowerLimit;
            cfg.UpperLimit = row.UpperLimit;
            cfg.Unit = row.Unit;
            int excelTimeoutMs;
            if (int.TryParse((row.Timeout ?? "").Trim(), out excelTimeoutMs) && excelTimeoutMs > 0)
                cfg.TimeoutMs = excelTimeoutMs;
            if (string.IsNullOrEmpty(cfg.SessionKey))
                cfg.SessionKey = string.IsNullOrEmpty(row.DeviceName) ? "DoIP_ECU" : row.DeviceName;
            if (!string.IsNullOrEmpty(excelDirectory))
                cfg.BaseDirectory = excelDirectory;
            if (device != null)
                ApplyDeviceMap(cfg, device);
            return cfg;
        }

        /// <summary>
        /// Backfill a step config from a Sheet2 device row (port of EonTest XlsxParser backfill).
        /// Only fills values the step did NOT explicitly set, so G column wins.
        /// </summary>
        public static void ApplyDeviceMap(UdsStepConfig cfg, DeviceInfo dev)
        {
            if (cfg == null || dev == null) return;
            if (dev.IsTcp)
            {
                if (!cfg.HasExplicitHost && !string.IsNullOrEmpty(dev.Address))
                    cfg.Host = dev.Address.Trim();

                // 端口：显式 Port 列优先，其次 SubAddress；仅当步骤未显式设端口且能解析为合法端口
                if (!cfg.HasExplicitPort)
                {
                    int port;
                    if (TryParsePort(dev.Port, out port))
                        cfg.Port = port;
                    else if (TryParsePort(dev.SubAddress, out port))
                        cfg.Port = port;
                }
            }
            // DoIP targetAddress 从"从地址"回填
            if (!cfg.HasExplicitTargetAddress && !string.IsNullOrEmpty(dev.TargetAddress))
                cfg.TargetAddress = HexUtils.TryParseAddress(dev.TargetAddress.Trim(), cfg.TargetAddress);
        }

        private static bool TryParsePort(string text, out int port)
        {
            port = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            // 端口是纯数字；IP 字符串（含点/冒号）不能当端口
            if (t.Contains(".") || t.Contains(":") || t.Contains("0x")) return false;
            if (!int.TryParse(t, out port)) return false;
            return port > 0 && port <= 65535;
        }
    }
}
