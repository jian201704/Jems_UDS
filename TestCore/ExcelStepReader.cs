using System;
using System.Collections.Generic;

namespace TestCore
{
    /// <summary>One device row of the 设备通道映射表 (Sheet2). Protocol-agnostic.</summary>
    public class DeviceInfo
    {
        public string DeviceName = "";
        public string DeviceCategory = "";
        public string ChannelId = "";
        public string ConnType = "";        // 连接方式
        public string Address = "";         // 主地址 (host for TCP/LAN)
        public string SubAddress = "";      // 子地址/端口
        public string TargetAddress = "";   // 从地址 (e.g. DoIP target)
        public string Port = "";            // 显式端口
        public string BaudRate = "";
        public string DataBits = "";
        public string StopBits = "";
        public string Parity = "";
        public string ChannelParams = "";   // 固有参数/设备参数 key=value
        public bool Enabled = true;

        public bool IsTcp
        {
            get
            {
                string c = (ConnType ?? "").Trim().ToLowerInvariant();
                return c.Contains("tcp") || c.Contains("lan") || c.Contains("socket") ||
                       c.Contains("以太网") || c.Contains("ethernet") || c.Contains("网络");
            }
        }
    }

    /// <summary>One row of the shared 测试工步表 Excel sheet. Protocol-agnostic raw fields.</summary>
    public class ExcelStepRow
    {
        public string StepNo = "";
        public string DeviceType = "";
        public string DeviceName = "";
        public string Channel = "";
        public string TestItem = "";        // E column: 测试项名称
        public string Action = "";          // F column: 动作指令
        public string Config1 = "";         // G column: 配置参数1
        public string Config2 = "";
        public string Config3 = "";
        public string DecodeConfig = "";    // P column: 解码配置
        public string PluginId = "";
        public string LowerLimit = "";      // J column: 下限值
        public string UpperLimit = "";      // K column: 上限值
        public string Unit = "";            // L column: 单位
        public string Timeout = "";         // M column: 超时(ms)
        public string FailPolicy = "";      // N column: 失败处理
        public string Remark = "";          // O column: 备注
        public int DataRow = 0;             // physical row index (1-based) for diagnostics
    }

    /// <summary>
    /// Reads the shared 测试工步表 sheet. Columns are detected by header name
    /// (works with EonTest's actual Excel layout, positions are irrelevant).
    /// Protocol-agnostic: the raw G/P columns are exposed, protocol libraries
    /// build their own typed config from them.
    /// </summary>
    public static class ExcelStepReader
    {
        public const string DefaultSheetName = "测试工步表";

        /// <summary>Read a row by 工步号 (column A value). Returns null when not found.</summary>
        public static ExcelStepRow ReadRowByStepNo(string excelPath, int stepNo, string sheetName = null)
        {
            return ReadRowByStepNo(excelPath, stepNo.ToString(), sheetName);
        }

        public static ExcelStepRow ReadRowByStepNo(string excelPath, string stepNo, string sheetName = null)
        {
            if (sheetName == null) sheetName = DefaultSheetName;
            using (var xlsx = new MiniXlsx(excelPath))
            {
                var headers = FindHeaders(xlsx, sheetName);
                if (headers == null) return null;
                int stepCol = headers["stepNo"];
                var rows = xlsx.ReadSheet(sheetName);
                for (int i = 1; i < rows.Count; i++) // skip header (index 0 is header row)
                {
                    var values = rows[i];
                    string no = Get(values, stepCol);
                    if (string.Equals(no, stepNo, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(no.TrimStart('0'), stepNo.TrimStart('0'), StringComparison.OrdinalIgnoreCase))
                        return BuildRow(values, headers, i + 1);
                }
            }
            return null;
        }

        /// <summary>Read a row by physical data index (1-based, counting from the first data row).</summary>
        public static ExcelStepRow ReadRowByIndex(string excelPath, int dataIndex, string sheetName = null)
        {
            if (sheetName == null) sheetName = DefaultSheetName;
            using (var xlsx = new MiniXlsx(excelPath))
            {
                var headers = FindHeaders(xlsx, sheetName);
                if (headers == null) return null;
                var rows = xlsx.ReadSheet(sheetName);
                if (dataIndex < 1 || dataIndex >= rows.Count) return null;
                return BuildRow(rows[dataIndex], headers, dataIndex + 1);
            }
        }

        /// <summary>Read all data rows of the sheet (protocol-agnostic; UDS-side code filters).</summary>
        public static List<ExcelStepRow> ReadAllRows(string excelPath, string sheetName = null)
        {
            var result = new List<ExcelStepRow>();
            if (sheetName == null) sheetName = DefaultSheetName;
            using (var xlsx = new MiniXlsx(excelPath))
            {
                var headers = FindHeaders(xlsx, sheetName);
                if (headers == null) return result;
                var rows = xlsx.ReadSheet(sheetName);
                for (int i = 1; i < rows.Count; i++)
                {
                    var row = BuildRow(rows[i], headers, i + 1);
                    if (row != null) result.Add(row);
                }
            }
            return result;
        }

        /// <summary>Read the first N data rows regardless of type.</summary>
        public static List<ExcelStepRow> ReadRows(string excelPath, int maxRows, string sheetName = null)
        {
            var result = new List<ExcelStepRow>();
            if (sheetName == null) sheetName = DefaultSheetName;
            using (var xlsx = new MiniXlsx(excelPath))
            {
                var headers = FindHeaders(xlsx, sheetName);
                if (headers == null) return result;
                var rows = xlsx.ReadSheet(sheetName);
                for (int i = 1; i < rows.Count && result.Count < maxRows; i++)
                {
                    var row = BuildRow(rows[i], headers, i + 1);
                    if (row != null) result.Add(row);
                }
            }
            return result;
        }

        // ---- 设备通道映射表 (Sheet2) ----

        /// <summary>
        /// Read the 设备通道映射表 (Sheet2) into a map keyed by 设备名称.
        /// Sheet name candidates match EonTest XlsxParser: DeviceChannel / 设备通道完整映射表 / 设备资源映射 / 设备通道映射表,
        /// falling back to the 2nd sheet. Columns are detected by header keywords.
        /// </summary>
        public static Dictionary<string, DeviceInfo> ReadDeviceMap(string excelPath, string sheetName = null)
        {
            var result = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
            using (var xlsx = new MiniXlsx(excelPath))
            {
                string actual = sheetName;
                if (string.IsNullOrEmpty(actual))
                {
                    foreach (string candidate in new[]
                    {
                        "DeviceChannel", "设备通道完整映射表", "设备资源映射", "设备通道映射表"
                    })
                    {
                        if (xlsx.HasSheet(candidate)) { actual = candidate; break; }
                    }
                    if (actual == null && xlsx.SheetNames.Count >= 2) actual = xlsx.SheetNames[1];
                }
                if (actual == null || !xlsx.HasSheet(actual)) return result;

                var map = FindDeviceHeaders(xlsx, actual);
                if (map == null) return result;

                var rows = xlsx.ReadSheet(actual);
                for (int i = 1; i < rows.Count; i++) // skip header row
                {
                    var values = rows[i];
                    var dev = BuildDevice(values, map, i + 1);
                    if (dev != null && dev.DeviceName.Length > 0)
                        result[dev.DeviceName] = dev;
                }
            }
            return result;
        }

        // ---- internals ----

        private static Dictionary<string, int> FindHeaders(MiniXlsx xlsx, string sheetName)
        {
            if (!xlsx.HasSheet(sheetName)) return null;
            var rows = xlsx.ReadSheet(sheetName);
            for (int r = 0; r < rows.Count && r < 20; r++)
            {
                var row = rows[r];
                if (row.Count == 0) continue;
                string first = Get(row, 0);
                if (first.IndexOf("工步号", StringComparison.OrdinalIgnoreCase) < 0 &&
                    first.IndexOf("step", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < row.Count; c++)
                {
                    string h = row[c];
                    if (string.IsNullOrWhiteSpace(h)) continue;
                    string t = h.Trim().ToLowerInvariant();
                    if (t.Contains("工步号") || t.Contains("step")) map["stepNo"] = c;
                    else if (t.Contains("设备分类")) map["deviceType"] = c;
                    else if (t.Contains("设备名称")) map["deviceName"] = c;
                    else if (t.Contains("通道") || t.Contains("端口")) map["channel"] = c;
                    else if (t.Contains("测试项名称") || t.Contains("测试项")) map["testItem"] = c;
                    else if (t.Contains("动作指令") || t == "action") map["action"] = c;
                    else if (t.Contains("配置参数1")) map["config1"] = c;
                    else if (t.Contains("配置参数2")) map["config2"] = c;
                    else if (t.Contains("配置参数3")) map["config3"] = c;
                    else if (t.Contains("解码配置") || t.Contains("decodeconfig") ||
                             t.Contains("decode profile") || t.Contains("decodeprofile")) map["decodeConfig"] = c;
                    else if (t.Contains("插件id") || t.Contains("pluginid") || t.Contains("plugin id")) map["pluginId"] = c;
                    else if (t.Contains("下限")) map["lower"] = c;
                    else if (t.Contains("上限")) map["upper"] = c;
                    else if (t.Contains("单位")) map["unit"] = c;
                    else if (t.Contains("超时")) map["timeout"] = c;
                    else if (t.Contains("失败处理")) map["failPolicy"] = c;
                    else if (t.Contains("备注")) map["remark"] = c;
                }
                if (map.ContainsKey("config1") || map.ContainsKey("action") || map.ContainsKey("stepNo"))
                    return map;
            }
            return null;
        }

        private static ExcelStepRow BuildRow(List<string> values, Dictionary<string, int> headers, int dataRow)
        {
            var row = new ExcelStepRow { DataRow = dataRow };
            row.StepNo = Get(values, headers, "stepNo");
            row.DeviceType = Get(values, headers, "deviceType");
            row.DeviceName = Get(values, headers, "deviceName");
            row.Channel = Get(values, headers, "channel");
            row.TestItem = Get(values, headers, "testItem");
            row.Action = Get(values, headers, "action");
            row.Config1 = Get(values, headers, "config1");
            row.Config2 = Get(values, headers, "config2");
            row.Config3 = Get(values, headers, "config3");
            row.DecodeConfig = Get(values, headers, "decodeConfig");
            row.PluginId = Get(values, headers, "pluginId");
            row.LowerLimit = Get(values, headers, "lower");
            row.UpperLimit = Get(values, headers, "upper");
            row.Unit = Get(values, headers, "unit");
            row.Timeout = Get(values, headers, "timeout");
            row.FailPolicy = Get(values, headers, "failPolicy");
            row.Remark = Get(values, headers, "remark");
            return row;
        }

        private static Dictionary<string, int> FindDeviceHeaders(MiniXlsx xlsx, string sheetName)
        {
            var rows = xlsx.ReadSheet(sheetName);
            for (int r = 0; r < rows.Count && r < 30; r++)
            {
                var row = rows[r];
                if (row.Count == 0) continue;
                bool hasName = false, hasChan = false, hasConnOrAddr = false;
                foreach (string h in row)
                {
                    if (string.IsNullOrWhiteSpace(h)) continue;
                    string t = h.Trim().ToLowerInvariant();
                    if (t.Contains("设备名称") || t.Contains("名称")) hasName = true;
                    if (t.Contains("通道号") || t.Contains("通道")) hasChan = true;
                    if (t.Contains("连接") || t.Contains("主地址") || t.Contains("端口") ||
                        t.Contains("波特") || t.Contains("启用") || t.Contains("状态")) hasConnOrAddr = true;
                }
                if (!hasName || !hasChan || !hasConnOrAddr) continue;

                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < row.Count; c++)
                {
                    string h = row[c];
                    if (string.IsNullOrWhiteSpace(h)) continue;
                    string t = h.Trim().ToLowerInvariant();
                    if (t.Contains("设备名称") || t.Contains("名称")) map["deviceName"] = c;
                    else if (t.Contains("设备分类") || t.Contains("分类")) map["deviceCategory"] = c;
                    else if (t.Contains("通道号") || (t.Contains("通道") && !t.Contains("固有"))) map["channelId"] = c;
                    else if (t.Contains("连接方式") || t.Contains("连接")) map["connType"] = c;
                    else if (t.Contains("主地址")) map["address"] = c;
                    else if (t.Contains("从地址")) map["targetAddress"] = c;
                    else if (t.Contains("子地址") || t.Contains("子端口")) map["subAddress"] = c;
                    else if (t.Contains("端口") && !t.Contains("波特")) map["port"] = c;
                    else if (t.Contains("波特率") || t.Contains("波特")) map["baudRate"] = c;
                    else if (t.Contains("数据位")) map["dataBits"] = c;
                    else if (t.Contains("停止位")) map["stopBits"] = c;
                    else if (t.Contains("校验位") || t.Contains("奇偶")) map["parity"] = c;
                    else if (t.Contains("固有参数") || t.Contains("设备参数") || (t.Contains("参数") && !t.Contains("配置"))) map["channelParams"] = c;
                    else if (t.Contains("启用") || t.Contains("状态")) map["enabled"] = c;
                }
                if (map.ContainsKey("deviceName")) return map;
            }
            return null;
        }

        private static DeviceInfo BuildDevice(List<string> values, Dictionary<string, int> map, int dataRow)
        {
            var dev = new DeviceInfo();
            dev.DeviceName = Get(values, map, "deviceName");
            dev.DeviceCategory = Get(values, map, "deviceCategory");
            dev.ChannelId = Get(values, map, "channelId");
            dev.ConnType = Get(values, map, "connType");
            dev.Address = Get(values, map, "address");
            dev.SubAddress = Get(values, map, "subAddress");
            dev.TargetAddress = Get(values, map, "targetAddress");
            dev.Port = Get(values, map, "port");
            dev.BaudRate = Get(values, map, "baudRate");
            dev.DataBits = Get(values, map, "dataBits");
            dev.StopBits = Get(values, map, "stopBits");
            dev.Parity = Get(values, map, "parity");
            dev.ChannelParams = Get(values, map, "channelParams");
            string enabled = Get(values, map, "enabled");
            if (enabled.Length > 0)
                dev.Enabled = !enabled.Equals("-") && !enabled.Equals("否") && !enabled.Equals("no") &&
                              !enabled.Equals("false") && !enabled.Equals("0");
            return dev;
        }

        private static string Get(List<string> values, int col)
        {
            return col >= 0 && col < values.Count ? (values[col] ?? "").Trim() : "";
        }

        private static string Get(List<string> values, Dictionary<string, int> headers, string key)
        {
            int col;
            if (!headers.TryGetValue(key, out col)) return "";
            return Get(values, col);
        }
    }
}
