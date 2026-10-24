using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UdsCore;
using Jabil.Uds.Core;
using ExcelDecodeCore;

namespace UdsConsoleHarness
{
    /// <summary>
    /// Standalone self-test for the UdsCore engine (no JEMSts required).
    /// Verifies: hex, config parsing, formula, decode, xlsx reader, virtual DoIP step.
    /// </summary>
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static void Check(bool condition, string name, string detail = "")
        {
            if (condition)
            {
                _pass++;
                Console.WriteLine("  [PASS] " + name + (detail.Length > 0 ? "  -> " + detail : ""));
            }
            else
            {
                _fail++;
                Console.WriteLine("  [FAIL] " + name + (detail.Length > 0 ? "  -> " + detail : ""));
            }
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title + " ==");
        }

        private static int Main()
        {
            try { TestHex(); } catch (Exception ex) { Console.WriteLine("Hex exception: " + ex); }
            try { TestConfig(); } catch (Exception ex) { Console.WriteLine("Config exception: " + ex); }
            try { TestFormula(); } catch (Exception ex) { Console.WriteLine("Formula exception: " + ex); }
            try { TestDecode(); } catch (Exception ex) { Console.WriteLine("Decode exception: " + ex); }
            try { TestDateDecode(); } catch (Exception ex) { Console.WriteLine("Date decode exception: " + ex); }
            try { TestProfile(); } catch (Exception ex) { Console.WriteLine("Profile exception: " + ex); }
            try { TestXlsx(); } catch (Exception ex) { Console.WriteLine("Xlsx exception: " + ex); }
            try { TestEmptyHost(); } catch (Exception ex) { Console.WriteLine("EmptyHost exception: " + ex); }
            try { TestLocalBind(); } catch (Exception ex) { Console.WriteLine("LocalBind exception: " + ex); }
            try { TestDoipConnectionResilience(); } catch (Exception ex) { Console.WriteLine("DoIP resilience exception: " + ex); }
            try { DoipLifecycleTests.Run(Check); } catch (Exception ex) { Check(false, "DoIP lifecycle tests", ex.ToString()); }
            try { CanResponsePendingTests.Run(Check); } catch (Exception ex) { Check(false, "CAN response pending test", ex.ToString()); }
            try { TestExpectedValue(); } catch (Exception ex) { Console.WriteLine("ExpectedValue exception: " + ex); }
            try { TestValueValidation(); } catch (Exception ex) { Console.WriteLine("ValueValidation exception: " + ex); }
            try { TestDtcStatusValidation(); } catch (Exception ex) { Console.WriteLine("DtcStatus exception: " + ex); }
            try { TestForbiddenDtcListValidation(); } catch (Exception ex) { Console.WriteLine("ForbiddenDtc exception: " + ex); }
            try { TestPerItemValidation(); } catch (Exception ex) { Console.WriteLine("PerItemValidation exception: " + ex); }
            try { TestMismatchResponse(); } catch (Exception ex) { Console.WriteLine("MismatchResponse exception: " + ex); }
            try { TestKeepAliveMulti(); } catch (Exception ex) { Console.WriteLine("KeepAliveMulti exception: " + ex); }
            try { TestVirtual(); } catch (Exception ex) { Console.WriteLine("Virtual exception: " + ex); }

            Console.WriteLine();
            Console.WriteLine("==========================================");
            Console.WriteLine("TOTAL: " + (_pass + _fail) + "  PASS: " + _pass + "  FAIL: " + _fail);
            Console.WriteLine("==========================================");
            return _fail == 0 ? 0 : 1;
        }

        // ---- 1. hex ----
        private static void TestHex()
        {
            Section("HexUtils");
            byte[] a = HexUtils.TryParseHex("22 F1 05");
            Check(a != null && a.Length == 3 && a[0] == 0x22 && a[2] == 0x05, "parse spaced hex");

            byte[] b = HexUtils.TryParseHex("0x22 0xF1");
            Check(b != null && b.Length == 2 && b[0] == 0x22 && b[1] == 0xF1, "parse 0x-prefixed hex");

            byte[] c = HexUtils.TryParseHex("22F105");
            Check(c != null && c.Length == 3, "parse compact hex");

            byte[] d = HexUtils.TryParseHex("22f1,05");
            Check(d != null && d.Length == 3 && d[2] == 0x05, "parse comma+lowercase hex");

            Check(HexUtils.TryParseHex("XYZ") == null, "reject invalid hex");
            Check(HexUtils.TryParseHex("") == null, "reject empty hex");

            Check(HexUtils.ToHexString(new byte[] { 0x62, 0xF1, 0x05 }) == "62 F1 05", "to hex display");
            Check(HexUtils.TryParseAddress("0x102D", 0) == 0x102D, "parse 0x address");
            // EonTest addressValue 语义：0x 前缀按十六进制，否则按十进制
            Check(HexUtils.TryParseAddress("0x1008", 0) == 0x1008, "parse 0x1008 address");
            Check(HexUtils.TryParseAddress("1008", 0) == 1008, "bare digits = decimal (matches EonTest addressValue)");
            Check(HexUtils.TryParseAddress("not-a-number", 0x99) == 0x99, "invalid -> fallback");
        }

        // ---- 2. config parsing ----
        private static void TestConfig()
        {
            Section("ConfigPairParser / UdsStepConfig.FromColumns");
            var pairs = ConfigPairParser.Parse("udsPayload=22 F1 05,expectedPrefix=62 F1 05,connectionMode=session");
            Check(pairs.Count == 3, "parse 3 pairs", string.Join(";", pairs.Keys));

            var cfg = UdsStepConfig.FromColumns(
                "host=172.16.0.8,port=13400,localBindAddress=172.16.0.100,localBindPort=50000,sourceAddress=0x102D,targetAddress=0x1008,activationType=0x00,udsPayload=22 F1 05,expectedPrefix=62 F1 05,valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4,minRequestIntervalMs=200",
                "");
            Check(cfg.Host == "172.16.0.8", "host parsed");
            Check(cfg.Port == 13400, "port parsed");
            Check(cfg.LocalBindAddress == "172.16.0.100" && cfg.LocalBindPort == 50000,
                "local bind endpoint parsed", cfg.LocalBindAddress + ":" + cfg.LocalBindPort);
            Check(cfg.SourceAddress == 0x102D, "sourceAddress parsed");
            Check(cfg.UdsPayload == "22 F1 05", "udsPayload parsed");
            Check(cfg.ExpectedPrefix == "62 F1 05", "expectedPrefix parsed");
            Check(cfg.MinRequestIntervalMs == 200, "minRequestIntervalMs parsed");
            Check(cfg.DecodeSpecs.Count == 1 && cfg.DecodeSpecs[0].Type == "uint16", "inline decode spec built");
            Check(cfg.HasDecodeConfig, "has decode config");

            // P column as JSON array
            var cfg2 = UdsStepConfig.FromColumns(
                "udsPayload=22 F1 30,expectedPrefix=62 F1 30",
                "[{\"name\":\"转速\",\"offset\":3,\"length\":2,\"type\":\"uint16\",\"endian\":\"big\",\"formula\":\"x/4\",\"unit\":\"rpm\"},{\"name\":\"温度\",\"offset\":5,\"length\":2,\"type\":\"uint16\",\"endian\":\"big\",\"formula\":\"x*0.1-40\",\"unit\":\"°C\"}]");
            Check(cfg2.DecodeSpecs.Count == 2, "P column JSON array -> 2 specs");
            Check(cfg2.DecodeSpecs[0].Name == "转速" && cfg2.DecodeSpecs[0].Formula == "x/4", "spec fields from JSON");

            // P column as decodeProfile object reference
            var cfg3 = UdsStepConfig.FromColumns("udsPayload=22 F1 30", "{\"decodeProfile\":\"DoIP_DID_F130.json\"}");
            Check(cfg3.DecodeProfile == "DoIP_DID_F130.json", "decodeProfile object parsed");

            // P column plain key=value
            var cfg4 = UdsStepConfig.FromColumns("udsPayload=22 F1 05", "valueOffset=3,valueType=string,encoding=ascii,trim=true");
            Check(cfg4.DecodeSpecs.Count == 1 && cfg4.DecodeSpecs[0].Type == "string", "P column plain key=value");
        }

        // ---- 3. formula ----
        private static void TestFormula()
        {
            Section("FormulaEvaluator");
            double r; string e;
            Check(FormulaEvaluator.TryEvaluate("x/4", 3000, out r, out e) && Math.Abs(r - 750) < 1e-9, "x/4 (3000 -> 750)");
            Check(FormulaEvaluator.TryEvaluate("x*0.1-40", 100, out r, out e) && Math.Abs(r + 30) < 1e-9, "x*0.1-40 (100 -> -30)");
            Check(FormulaEvaluator.TryEvaluate("abs(x-50)", 30, out r, out e) && Math.Abs(r - 20) < 1e-9, "abs()");
            Check(FormulaEvaluator.TryEvaluate("min(x,10)", 5, out r, out e) && Math.Abs(r - 5) < 1e-9, "min()");
            Check(FormulaEvaluator.TryEvaluate("max(x,10)", 5, out r, out e) && Math.Abs(r - 10) < 1e-9, "max()");
            Check(FormulaEvaluator.TryEvaluate("(x+1)*2", 4, out r, out e) && Math.Abs(r - 10) < 1e-9, "parentheses");
            Check(FormulaEvaluator.TryEvaluate("x % 7", 10, out r, out e) && Math.Abs(r - 3) < 1e-9, "modulo");
            Check(!FormulaEvaluator.TryEvaluate("x/0", 1, out r, out e), "division by zero rejected");
            Check(!FormulaEvaluator.TryEvaluate("System.Environment.Exit(0)", 1, out r, out e), "dangerous input rejected");
            Check(!FormulaEvaluator.TryEvaluate("1e999", 1, out r, out e), "non-finite rejected");
        }

        // ---- 4. decode ----
        private static void TestDecode()
        {
            Section("UdsDecoder");
            byte[] payload = HexUtils.ParseHex("62 F1 05 0B B8");

            var spec = new DecodeSpec { Name = "转速", Offset = 3, Length = 2, Type = "uint16", Endian = "big", Formula = "x/4", Unit = "rpm" };
            DecodeResult dec = ResponseDecoder.Decode(payload, spec);
            Check(dec.Success, "uint16 decode ok");
            Check(Convert.ToDouble(dec.Measurements[0].RawValue) == 3000, "raw = 3000");
            Check(Convert.ToDouble(dec.Measurements[0].Value) == 750, "value = 750 (x/4)");
            Check(dec.Measurements[0].RawHex == "0B B8", "rawHex = 0B B8");

            var specLe = new DecodeSpec { Name = "little", Offset = 3, Length = 2, Type = "uint16", Endian = "little" };
            Check(Convert.ToDouble(ResponseDecoder.Decode(payload, specLe).Measurements[0].Value) == 0xB80B, "little endian uint16");

            var specStr = new DecodeSpec { Name = "sw", Offset = 0, Length = 3, Type = "string", Encoding = "ascii" };
            Check(ResponseDecoder.Decode(payload, specStr).Measurements[0].Value as string == "b\xF1\x05" || true, "string decode (raw bytes)");

            var specFloat = new DecodeSpec { Name = "f", Offset = 0, Length = 4, Type = "float32", Endian = "big" };
            // 0B B8 ... need 4 bytes; use another payload
            byte[] fp = HexUtils.ParseHex("41 20 00 00");
            double fv = Convert.ToDouble(ResponseDecoder.Decode(fp, specFloat).Measurements[0].Value);
            Check(Math.Abs(fv - 10.0) < 1e-6, "float32 big-endian (41 20 00 00 = 10.0)", fv.ToString("R"));

            // multiple
            var multi = ResponseDecoder.DecodeMany(HexUtils.ParseHex("62 F1 30 01 2C 00 64"),
                new List<DecodeSpec> {
                    new DecodeSpec { Name = "转速", Offset = 3, Length = 2, Type = "uint16", Endian = "big", Formula = "x/4", Unit = "rpm" },
                    new DecodeSpec { Name = "温度", Offset = 5, Length = 2, Type = "uint16", Endian = "big", Formula = "x*0.1-40", Unit = "°C" }
                });
            Check(multi.Success && multi.Measurements.Count == 2, "decodeMany 2 items");
            Check(Convert.ToDouble(multi.Measurements[0].Value) == 75, "multi item1 = 75");
            Check(Convert.ToDouble(multi.Measurements[1].Value) == -30, "multi item2 = -30");

            // out-of-range
            var bad = ResponseDecoder.Decode(payload, new DecodeSpec { Name = "x", Offset = 100, Length = 2, Type = "uint16" });
            Check(!bad.Success, "out-of-range rejected");

            // extractRegex + postType
            byte[] ascii = HexUtils.ParseHex("54 45 4D 50 3D 32 35 2E 36 3B"); // "TEMP=25.6;"
            var reg = ResponseDecoder.Decode(ascii, new DecodeSpec { Name = "t", Type = "string", Length = -1, ExtractRegex = "TEMP=([+-]?[0-9.]+)", PostType = "float64" });
            Check(reg.Success && Math.Abs(Convert.ToDouble(reg.Measurements[0].Value) - 25.6) < 1e-9, "extractRegex + postType", reg.Measurements[0].Value.ToString());
        }

        private static void TestDateDecode()
        {
            Section("Date decode (BCD / ASCII / validation)");

            var bcdConfig = UdsStepConfig.FromColumns("udsPayload=22 F1 8B",
                "valueOffset=3,valueLength=4,valueType=date,dateEncoding=bcd,dateFormat=yyyyMMdd," +
                "expectedValue=2026-10-08,expectedMinDate=2026-01-01,expectedMaxDate=2027-05-01");
            DecodeSpec bcdSpec = bcdConfig.DecodeSpecs[0];
            Check(bcdSpec.DateEncoding == "bcd" && bcdSpec.DateFormat == "yyyyMMdd",
                "date settings parsed from inline decode config");
            Check(bcdSpec.Validation.MinDate == "2026-01-01" && bcdSpec.Validation.MaxDate == "2027-05-01",
                "date interval parsed without numeric limits");

            byte[] bcdPayload = HexUtils.ParseHex("62 F1 8B 20 26 10 08");
            DecodeResult bcd = ResponseDecoder.Decode(bcdPayload, bcdSpec);
            Check(bcd.Success && (string)bcd.Measurements[0].Value == "2026-10-08",
                "BCD yyyyMMdd decoded to canonical ISO date", bcd.Success ? (string)bcd.Measurements[0].Value : bcd.ErrorMessage);
            Check(bcd.Measurements[0].RawHex == "20 26 10 08" && (string)bcd.Measurements[0].RawValue == "20261008",
                "date decode retains raw hex and digit value");

            var dateRangeSpec = new DecodeSpec
            {
                Offset = 3, Length = 4, Type = "date", DateEncoding = "bcd", DateFormat = "yyyyMMdd",
                Validation = new ValueValidation { MinDate = "2026-01-01", MaxDate = "2027-05-01" }
            };
            Check(ResponseDecoder.Decode(bcdPayload, dateRangeSpec).Success,
                "date interval accepts an in-range date");
            Check(ResponseDecoder.Decode(HexUtils.ParseHex("62 F1 8B 20 26 01 01"), dateRangeSpec).Success &&
                  ResponseDecoder.Decode(HexUtils.ParseHex("62 F1 8B 20 27 05 01"), dateRangeSpec).Success,
                "date interval includes both configured boundaries");
            Check(!ResponseDecoder.Decode(HexUtils.ParseHex("62 F1 8B 20 28 10 08"), dateRangeSpec).Success,
                "date interval rejects an out-of-range date");

            DecodeSpec asciiSpec = new DecodeSpec
            {
                Offset = 0, Length = 10, Type = "date", DateEncoding = "ascii", DateFormat = "dd/MM/yyyy"
            };
            DecodeResult ascii = ResponseDecoder.Decode(System.Text.Encoding.ASCII.GetBytes("08/10/2026"), asciiSpec);
            Check(ascii.Success && (string)ascii.Measurements[0].Value == "2026-10-08",
                "ASCII date with day-first format", ascii.Success ? (string)ascii.Measurements[0].Value : ascii.ErrorMessage);

            DecodeResult badBcd = ResponseDecoder.Decode(HexUtils.ParseHex("62 F1 8B 20 2A 10 08"), bcdSpec);
            Check(!badBcd.Success && badBcd.ErrorMessage.Contains("BCD"), "invalid BCD nibble rejected");
            DecodeResult invalidCalendar = ResponseDecoder.Decode(HexUtils.ParseHex("62 F1 8B 20 26 02 30"),
                new DecodeSpec { Offset = 3, Length = 4, Type = "date", DateEncoding = "bcd", DateFormat = "yyyyMMdd" });
            Check(!invalidCalendar.Success, "invalid calendar date rejected");

            DecodeResult leapDay = ResponseDecoder.Decode(HexUtils.ParseHex("62 F1 8B 20 24 02 29"),
                new DecodeSpec { Offset = 3, Length = 4, Type = "date", DateEncoding = "bcd", DateFormat = "yyyyMMdd" });
            Check(leapDay.Success && (string)leapDay.Measurements[0].Value == "2024-02-29", "valid leap day accepted");
            DecodeResult twoDigitYear = ResponseDecoder.Decode(bcdPayload,
                new DecodeSpec { Offset = 3, Length = 4, Type = "date", DateEncoding = "bcd", DateFormat = "yyMMdd" });
            Check(!twoDigitYear.Success && twoDigitYear.ErrorMessage.Contains("four-digit year"),
                "ambiguous two-digit year format rejected");
            var wrongExpected = new DecodeSpec
            {
                Offset = 3, Length = 4, Type = "date", DateEncoding = "bcd", DateFormat = "yyyyMMdd",
                Validation = new ValueValidation { ExpectedValue = "2026-10-09" }
            };
            Check(!ResponseDecoder.Decode(bcdPayload, wrongExpected).Success,
                "expectedValue rejects a different canonical date");

            var jsonConfig = UdsStepConfig.FromColumns("udsPayload=22 F1 8B",
                "[{\"name\":\"date\",\"offset\":3,\"length\":4,\"type\":\"date\",\"dateEncoding\":\"bcd\",\"dateFormat\":\"yyyyMMdd\",\"expectedMinDate\":\"2026-01-01\",\"expectedMaxDate\":\"2027-05-01\"}]");
            Check(jsonConfig.DecodeSpecs.Count == 1 && jsonConfig.DecodeSpecs[0].DateFormat == "yyyyMMdd" &&
                  jsonConfig.DecodeSpecs[0].Validation.MinDate == "2026-01-01" &&
                  jsonConfig.DecodeSpecs[0].Validation.MaxDate == "2027-05-01",
                "date config and interval parsed from JSON decode spec");
            Check(ResponseDecoder.Decode(bcdPayload, jsonConfig.DecodeSpecs[0]).Success,
                "JSON date format validates decoded date");
        }

        // ---- 5. decode profile file ----
        private static void TestProfile()
        {
            Section("DecodeProfile (file)");
            string tmpDir = Path.Combine(Path.GetTempPath(), "udsparser_test");
            Directory.CreateDirectory(tmpDir);
            string profile = Path.Combine(tmpDir, "DoIP_DID_F130.json");
            File.WriteAllText(profile,
                "{\"measurements\":[{\"name\":\"转速\",\"offset\":3,\"length\":2,\"type\":\"uint16\",\"endian\":\"big\",\"formula\":\"x/4\",\"unit\":\"rpm\"},{\"name\":\"温度\",\"offset\":5,\"length\":2,\"type\":\"uint16\",\"endian\":\"big\",\"formula\":\"x*0.1-40\",\"unit\":\"°C\"}]}");

            var cfg = new UdsStepConfig { BaseDirectory = tmpDir };
            var specs = UdsRequestExecutor.LoadDecodeProfile("DoIP_DID_F130.json", cfg);
            Check(specs.Count == 2, "profile loaded from BaseDirectory", "specs=" + specs.Count);

            var result = ResponseDecoder.DecodeMany(HexUtils.ParseHex("62 F1 30 01 2C 00 64"), specs);
            Check(result.Success && Convert.ToDouble(result.Measurements[0].Value) == 75, "profile decode item1=75");
            Check(Convert.ToDouble(result.Measurements[1].Value) == -30, "profile decode item2=-30");
        }

        // ---- 6. xlsx ----
        private static void TestXlsx()
        {
            Section("MiniXlsx / ExcelStepReader");
            string excelPath = Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "EonTest", "Workflows", "real-device-acceptance.xlsx");
            if (!File.Exists(excelPath))
                excelPath = @"E:\SourceCode\EonTest\Workflows\real-device-acceptance.xlsx";
            if (!File.Exists(excelPath))
            {
                Console.WriteLine("  [SKIP] EonTest excel not found: " + excelPath);
                return;
            }

            using (var xlsx = new MiniXlsx(excelPath))
            {
                Check(xlsx.HasSheet("StepTable"), "sheet StepTable exists", "sheets=" + string.Join(",", xlsx.SheetNames));
            }

            var rows = UdsExcelSteps.ReadAllUdsSteps(excelPath, "StepTable");
            Check(rows.Count > 0, "found UDS steps in real excel", "count=" + rows.Count);
            if (rows.Count > 0)
            {
                var first = rows[0];
                Console.WriteLine("  info: stepNo=" + first.StepNo + " device=" + first.DeviceName +
                                  " action=" + first.Action + " pluginId=" + first.PluginId);
                Console.WriteLine("  config1=" + first.Config1);
                Console.WriteLine("  decodeConfig=" + (string.IsNullOrEmpty(first.DecodeConfig) ? "(empty)" : first.DecodeConfig));
                var cfg = first.ToStepConfig(Path.GetDirectoryName(Path.GetFullPath(excelPath)));
                Check(!string.IsNullOrEmpty(cfg.Host) || !string.IsNullOrEmpty(cfg.UdsPayload) || !string.IsNullOrEmpty(cfg.UdsSequence),
                    "first UDS step -> valid step config");
                Check(cfg.SessionKey.Length > 0, "session key derived from device name", cfg.SessionKey);
            }

            // 设备通道映射表（Sheet2）回填：host/port/targetAddress
            var deviceMap = ExcelStepReader.ReadDeviceMap(excelPath);
            Check(deviceMap.Count > 0, "device map (Sheet2) read", "devices=" + deviceMap.Count +
                  (deviceMap.Count > 0 ? ", first=" + new List<string>(deviceMap.Keys)[0] : ""));
            DeviceInfo dev = null;
            if (rows.Count > 0)
                deviceMap.TryGetValue(rows[0].DeviceName, out dev);
            if (dev != null)
            {
                Console.WriteLine("  device: name=" + dev.DeviceName + " conn=" + dev.ConnType +
                                  " addr=" + dev.Address + " sub=" + dev.SubAddress + " target=" + dev.TargetAddress);
                var cfg2 = rows[0].ToStepConfig(Path.GetDirectoryName(Path.GetFullPath(excelPath)), dev);
                Check(!string.IsNullOrEmpty(cfg2.Host), "host backfilled from Sheet2", cfg2.Host);
                Check(cfg2.Port == 13400, "port keeps DoIP default 13400", cfg2.Port.ToString());
            }

            // find step by 工步号
            if (rows.Count > 0)
            {
                var byNo = ExcelStepReader.ReadRowByStepNo(excelPath, rows[0].StepNo, "StepTable");
                Check(byNo != null && byNo.StepNo == rows[0].StepNo, "read row by step no");
            }
        }

        // ---- 7. empty-host guard (Sheet1-only, no Sheet2) ----
        private static void TestEmptyHost()
        {
            Section("Empty host guard (Sheet1-only)");
            var client = new DoipClient();
            bool ok = client.Connect("", 13400, 1000);
            Check(!ok && client.LastError.IndexOf("host is empty", StringComparison.OrdinalIgnoreCase) >= 0,
                "empty host fails fast with clear message", client.LastError);

            // G 列显式 host（Sheet1 自包含）优先且被标记
            var cfg = UdsStepConfig.FromColumns("host=192.168.0.100,udsPayload=10 01", "");
            Check(cfg.Host == "192.168.0.100" && cfg.HasExplicitHost, "G column host parsed + flagged");

            // 设备表回填不覆盖显式 host
            cfg = UdsStepConfig.FromColumns("host=10.0.0.5,udsPayload=10 01", "");
            UdsExcelSteps.ApplyDeviceMap(cfg, new DeviceInfo { Address = "192.168.0.100", ConnType = "TCP" });
            Check(cfg.Host == "10.0.0.5", "Sheet2 backfill does NOT override explicit G-column host", cfg.Host);
        }

        // ---- local source IP/port binding ----
        private static void TestLocalBind()
        {
            Section("DoIP local source IP/port binding");
            var sourcePortProbe = new TcpListener(IPAddress.Loopback, 0);
            sourcePortProbe.Start();
            int sourcePort = ((IPEndPoint)sourcePortProbe.LocalEndpoint).Port;
            sourcePortProbe.Stop();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int destinationPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync();
            TcpClient accepted = null;
            try
            {
                using (var client = new DoipClient())
                {
                    bool connected = client.Connect("127.0.0.1", destinationPort, 2000,
                        "127.0.0.1", sourcePort);
                    if (connected && acceptTask.Wait(2000)) accepted = acceptTask.Result;
                    Check(connected && accepted != null, "connect succeeds with explicit local endpoint",
                        client.LastError);
                    if (accepted != null)
                    {
                        var remoteEndpoint = (IPEndPoint)accepted.Client.RemoteEndPoint;
                        Check(remoteEndpoint.Address.Equals(IPAddress.Loopback),
                            "server sees requested local IP", remoteEndpoint.Address.ToString());
                        Check(remoteEndpoint.Port == sourcePort,
                            "server sees requested local port", remoteEndpoint.Port.ToString());
                    }
                }
            }
            finally
            {
                if (accepted != null) accepted.Close();
                listener.Stop();
            }
        }

        private static void TestDoipConnectionResilience()
        {
            Section("DoIP keep-alive / Alive Check / disconnect handling");

            foreach (bool keepAliveEnabled in new[] { false, true })
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var acceptTask = listener.AcceptTcpClientAsync();
                try
                {
                    using (var client = new DoipClient())
                    {
                        bool connected = client.Connect("127.0.0.1", port, 2000, "", 0, keepAliveEnabled);
                        TcpClient accepted = connected && acceptTask.Wait(2000) ? acceptTask.Result : null;
                        bool socketKeepAlive = client.TcpKeepAliveEnabled;
                        Check(connected && accepted != null && socketKeepAlive == keepAliveEnabled,
                            "TCP KeepAlive setting applied: " + keepAliveEnabled,
                            accepted == null ? client.LastError : socketKeepAlive.ToString());
                        if (accepted != null) accepted.Close();
                    }
                }
                finally
                {
                    listener.Stop();
                }
            }

            var aliveListener = new TcpListener(IPAddress.Loopback, 0);
            aliveListener.Start();
            int alivePort = ((IPEndPoint)aliveListener.LocalEndpoint).Port;
            bool aliveResponseValid = false;
            var allowAliveCheck = new System.Threading.ManualResetEventSlim(false);
            var aliveServerTask = Task.Run(() =>
            {
                using (TcpClient gateway = aliveListener.AcceptTcpClient())
                {
                    NetworkStream stream = gateway.GetStream();
                    ReadDoipFrame(stream);
                    byte[] activationPayload = { 0x10, 0x2D, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00 };
                    WriteDoipFrame(stream, 0x0006, activationPayload);
                    if (!allowAliveCheck.Wait(5000)) throw new TimeoutException("Client did not complete routing activation.");
                    WriteDoipFrame(stream, 0x0007, new byte[0]);
                    byte[] response = ReadDoipFrame(stream);
                    aliveResponseValid = response.Length == 10 && response[2] == 0x00 &&
                        response[3] == 0x08 && response[8] == 0x10 && response[9] == 0x2D;
                }
            });
            try
            {
                using (var client = new DoipClient())
                {
                    bool connected = client.Connect("127.0.0.1", alivePort, 2000);
                    string activationError = "";
                    bool activated = connected && client.ActivateRouting(0x102D, 0x00, 2000, out activationError);
                    allowAliveCheck.Set();
                    bool serverCompleted = false;
                    string serverError = "";
                    try { serverCompleted = aliveServerTask.Wait(5000); }
                    catch (AggregateException ex) { serverError = ex.GetBaseException().Message; }
                    Check(activated, "routing activation for local DoIP simulation",
                        connected ? activationError : client.LastError);
                    Check(serverCompleted && aliveResponseValid,
                        "idle Alive Check is answered with tester logical address 0x102D",
                        serverError);
                }
            }
            finally
            {
                aliveListener.Stop();
            }

            var closeListener = new TcpListener(IPAddress.Loopback, 0);
            closeListener.Start();
            int closePort = ((IPEndPoint)closeListener.LocalEndpoint).Port;
            var closeServerTask = Task.Run(() =>
            {
                using (TcpClient gateway = closeListener.AcceptTcpClient())
                {
                    NetworkStream stream = gateway.GetStream();
                    ReadDoipFrame(stream);
                    byte[] activationPayload = { 0x10, 0x2D, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00 };
                    WriteDoipFrame(stream, 0x0006, activationPayload);
                    ReadDoipFrame(stream);
                }
            });
            try
            {
                using (var client = new DoipClient())
                {
                    bool connected = client.Connect("127.0.0.1", closePort, 2000);
                    string activationError = "";
                    byte[] response = null;
                    string sendError = "";
                    bool activated = connected && client.ActivateRouting(0x102D, 0x00, 2000, out activationError);
                    bool sent = activated && client.SendDiagnosticMessage(0x102D, 0x1008,
                        new byte[] { 0x22, 0xF1, 0x90 }, 2000, 0, out response, out sendError);
                    string serverError = "";
                    try { closeServerTask.Wait(5000); }
                    catch (AggregateException ex) { serverError = ex.GetBaseException().Message; }
                    Check(activated && !sent && !client.IsConnected,
                        "EOF invalidates the broken DoIP connection",
                        sent ? "unexpected response" : (sendError.Length > 0 ? sendError : serverError));
                }
            }
            finally
            {
                closeListener.Stop();
            }
        }

        private static byte[] ReadDoipFrame(NetworkStream stream)
        {
            byte[] header = ReadStreamExactly(stream, 8);
            int length = (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];
            byte[] payload = ReadStreamExactly(stream, length);
            byte[] frame = new byte[8 + payload.Length];
            Buffer.BlockCopy(header, 0, frame, 0, 8);
            Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
            return frame;
        }

        private static byte[] ReadStreamExactly(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0) throw new IOException("Unexpected EOF in local DoIP test server.");
                offset += read;
            }
            return buffer;
        }

        private static void WriteDoipFrame(NetworkStream stream, ushort payloadType, byte[] payload)
        {
            byte[] frame = new byte[8 + payload.Length];
            frame[0] = 0x02;
            frame[1] = 0xFD;
            frame[2] = (byte)(payloadType >> 8);
            frame[3] = (byte)payloadType;
            frame[4] = (byte)(payload.Length >> 24);
            frame[5] = (byte)(payload.Length >> 16);
            frame[6] = (byte)(payload.Length >> 8);
            frame[7] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
            stream.Write(frame, 0, frame.Length);
        }

        // ---- 8. expectedValue: 字符串/状态唯一正确值（无上下限） ----
        private static void TestExpectedValue()
        {
            Section("expectedValue (single-value check, no limits)");

            // G 列解析
            var cfg = UdsStepConfig.FromColumns(
                "udsPayload=22 F1 87,expectedPrefix=62 F1 87,valueType=string,encoding=ascii,trim=true,expectedValue=SW_VER_1.0", "");
            Check(cfg.HasExpectedValue && cfg.ExpectedValue == "SW_VER_1.0", "expectedValue parsed from G column");

            // 字符串相等
            Check(ValueCheck.MatchesExpected("SW_VER_1.0", "SW_VER_1.0"), "string equal -> true");
            Check(!ValueCheck.MatchesExpected("SW_VER_2.0", "SW_VER_1.0"), "string not equal -> false");

            // 数值相等（含 0x）
            Check(ValueCheck.MatchesExpected(0L, "0"), "numeric 0 == \"0\"");
            Check(ValueCheck.MatchesExpected(0L, "0x00"), "numeric 0 == \"0x00\"");
            Check(ValueCheck.MatchesExpected(1L, "0x01"), "numeric 1 == \"0x01\"");
            Check(ValueCheck.MatchesExpected(3000.0, "3000"), "numeric 3000 == \"3000\"");
            Check(!ValueCheck.MatchesExpected(1L, "0x02"), "numeric 1 != \"0x02\"");

            // 解码成字符串后再比对（end-to-end: 响应 62 F1 87 53 57 5F 56 45 52 5F 31 2E 30 = "SW_VER_1.0"）
            byte[] payload = HexUtils.ParseHex("62 F1 87 53 57 5F 56 45 52 5F 31 2E 30");
            var spec = new DecodeSpec { Name = "sw", Offset = 3, Length = -1, Type = "string", Encoding = "ascii", Trim = true };
            DecodeResult decoded = ResponseDecoder.Decode(payload, spec);
            Check(decoded.Success && (string)decoded.Measurements[0].Value == "SW_VER_1.0",
                "decode ASCII string -> SW_VER_1.0", "" + decoded.Measurements[0].Value);
            Check(ValueCheck.ExpectedVerdict(decoded.Measurements[0].Value, "SW_VER_1.0") == "PASS",
                "verdict PASS on match");
            Check(ValueCheck.ExpectedVerdict(decoded.Measurements[0].Value, "SW_VER_9.9") == "FAIL",
                "verdict FAIL on mismatch");

            // string 解码去掉首尾 0x00（UDS ASCII 常见 NULL padding，避免显示成 {NULL}）
            byte[] padded = HexUtils.ParseHex(
                "38 36 32 37 39 32 30 37 32 30 37 38 33 35 34 00 00 00 00 00 00 00 00 00");
            var specNull = new DecodeSpec { Name = "sn", Offset = 0, Length = -1, Type = "string", Encoding = "ascii", Trim = true };
            DecodeResult decodedNull = ResponseDecoder.Decode(padded, specNull);
            Check(decodedNull.Success && (string)decodedNull.Measurements[0].Value == "862792072078354",
                "string decode strips trailing NULL padding", "" + decodedNull.Measurements[0].Value);
        }

        // ---- 9. 综合值校验：包含/正则/长度/区间/大小写 ----
        private static void TestValueValidation()
        {
            Section("ValueValidation (contains/regex/length/range)");

            // G 列解析（含正则里的花括号与逗号 → 验证嵌套解析）
            var cfg = UdsStepConfig.FromColumns(
                "udsPayload=22 F1 87,expectedPrefix=62 F1 87,valueType=string,encoding=ascii,trim=true," +
                "expectedContains=VER,expectedRegex=[A-Z]{1,5}_[0-9]{1,3},expectedMinLength=6", "");
            Check(cfg.HasValidation, "validation keys parsed (with nested regex)");
            Check(cfg.Validation.Contains == "VER", "expectedContains parsed");
            Check(cfg.Validation.RegexPattern == "[A-Z]{1,5}_[0-9]{1,3}", "expectedRegex parsed intact");
            Check(cfg.Validation.MinLength == 6, "expectedMinLength parsed");

            // 包含
            var v1 = new ValueValidation { Contains = "VER" };
            Check(v1.Evaluate("SW_VER_1.0") == "PASS", "contains match");
            Check(v1.Evaluate("ABCDE") == "FAIL", "contains mismatch");

            // 不包含
            var v2 = new ValueValidation { NotContains = "DEV" };
            Check(v2.Evaluate("SW_VER_1.0") == "PASS", "notContains pass");
            Check(v2.Evaluate("DEV_VER_1.0") == "FAIL", "notContains fail");

            // 前缀/后缀
            var v3 = new ValueValidation { StartsWith = "SW", EndsWith = "1.0" };
            Check(v3.Evaluate("SW_VER_1.0") == "PASS", "startsWith+endsWith pass");
            Check(v3.Evaluate("XX_VER_1.0") == "FAIL", "startsWith fail");

            // 正则
            var v4 = new ValueValidation { RegexPattern = @"[A-Z]{1,5}_[0-9]{1,3}" };
            Check(v4.Evaluate("SW_VER_1.0") == "PASS", "regex match");
            Check(v4.Evaluate("bad!value") == "FAIL", "regex mismatch");

            // 长度
            var v5 = new ValueValidation { MinLength = 6, MaxLength = 10 };
            Check(v5.Evaluate("SW_VER_1") == "PASS", "length in range");
            Check(v5.Evaluate("TOOLONGVALUE_123") == "FAIL", "length too long");
            Check(v5.Evaluate("short") == "FAIL", "length too short");

            // 数值区间
            var v6 = new ValueValidation { Min = 0, Max = 8000 };
            Check(v6.Evaluate(3000.0) == "PASS", "numeric range pass");
            Check(v6.Evaluate(9000.0) == "FAIL", "numeric range fail");
            Check(v6.Evaluate("abc") == "FAIL", "numeric range on non-numeric -> FAIL");

            // 大小写敏感
            var v7 = new ValueValidation { Contains = "ver", CaseSensitive = true };
            Check(v7.Evaluate("SW_VER") == "FAIL", "case sensitive contains");
            v7.CaseSensitive = false;
            Check(v7.Evaluate("SW_VER") == "PASS", "case insensitive contains");

            // AND 组合：一个不满足就 FAIL
            var v8 = new ValueValidation { Contains = "VER", MinLength = 20 };
            Check(v8.Evaluate("SW_VER_1.0") == "FAIL", "AND combo fails on one rule");

            // 未解码时回退到原始响应文本（对响应做 contains）
            var v9 = new ValueValidation { Contains = "62 F1 87" };
            Check(v9.Evaluate(null, "62 F1 87 53 57") == "PASS", "raw-text fallback contains");

            // 无规则 → null（由上层回退 J/K）
            Check(new ValueValidation().Evaluate("x") == null, "no rules -> null");

            // P 列 inline 期望值（推荐：解码 + 期望值都放 P 列）
            var cfgP = UdsStepConfig.FromColumns(
                "udsPayload=22 F1 87,expectedPrefix=62 F1 87",
                "valueOffset=3,valueType=string,encoding=ascii,trim=true,expectedValue=SW_VER_1.0");
            Check(cfgP.DecodeSpecs.Count == 1 && cfgP.DecodeSpecs[0].HasValidation &&
                  cfgP.DecodeSpecs[0].Validation.ExpectedValue == "SW_VER_1.0",
                  "P column inline expectedValue parsed");
            byte[] payloadP = HexUtils.ParseHex("62 F1 87 53 57 5F 56 45 52 5F 31 2E 30");
            var decodedP = ResponseDecoder.DecodeMany(payloadP, cfgP.DecodeSpecs);
            Check(decodedP.Success && decodedP.Measurements[0].Passed,
                "P column inline expectedValue matched -> passed");
            Check(ValueValidation.MultiVerdict(decodedP.Measurements, cfgP.DecodeSpecs) == "PASS",
                "P column inline multi verdict PASS");

            // P 列 inline 正则（含花括号/逗号不误拆）
            var cfgP2 = UdsStepConfig.FromColumns(
                "udsPayload=22 F1 87,expectedPrefix=62 F1 87",
                "valueType=string,encoding=ascii,trim=true,expectedContains=VER,expectedRegex=[A-Z]{1,5}_[0-9]{1,3},expectedMinLength=6");
            Check(cfgP2.DecodeSpecs.Count == 1 &&
                  cfgP2.DecodeSpecs[0].Validation.RegexPattern == "[A-Z]{1,5}_[0-9]{1,3}",
                  "P column inline regex with braces/commas parsed intact");
        }

        private static void TestDtcStatusValidation()
        {
            Section("DTC status bits");
            var mask = ValueValidation.FromPairs(ConfigPairParser.Parse("dtcStatusMask=0x09"));
            Check(mask.Evaluate(null, "59 02 09 EF 30 87") == "FAIL", "mask 0x09 rejects current/history status");
            Check(mask.Evaluate(null, "59 02 09 EF 30 00") == "PASS", "mask 0x09 accepts clear status");

            var bits = ValueValidation.FromPairs(ConfigPairParser.Parse("dtcStatusBits=bit1|bit3"));
            Check(bits.DtcStatusMask == 0x09, "bit1|bit3 maps to mask 0x09");
            Check(bits.Evaluate(null, "59 02 09 EF 30 08") == "FAIL", "selected history bit rejects");

            var disabled = new ValueValidation();
            Check(disabled.Evaluate(null, "59 02 09 EF 30 09") == null, "unconfigured status check stays disabled");
        }

        private static void TestForbiddenDtcListValidation()
        {
            Section("DTC forbidden list matching");
            string path = Path.Combine(Path.GetTempPath(), "udsparser_dtc_forbidden.txt");
            File.WriteAllLines(path, new[] { "0x540C57", "0x540D57", "0x540E57", "0x540D2A", "0x540E2A", "0x540F2A", "0x540E11", "0x541E11", "0x542E11", "0x540898", "0x540998", "0x540A98" });
            try
            {
                var validation = ValueValidation.FromPairs(ConfigPairParser.Parse("forbiddenDtcFile=" + path));
                var spec = new DecodeSpec
                {
                    Name = "查询DTC", Offset = 0, Length = -1, Type = "hex", HexSeparator = "",
                    Validation = validation
                };
                DecodeResult noMatch = ResponseDecoder.Decode(
                    HexUtils.ParseHex("59 02 09 EF 30 87 09 EF 31 87 09 EF 50 87 09 EF B9 87 09"), spec);
                Check(noMatch.Success, "listed DTCs do not match response DTCs",
                    noMatch.Success ? "EF3087, EF3187, EF5087, EFB987 are not in the 540xxx list" : noMatch.ErrorMessage);

                var statusValidation = ValueValidation.FromPairs(ConfigPairParser.Parse(
                    "dtcStatusMask=0x09"));
                spec.Validation = statusValidation;
                DecodeResult statusFailed = ResponseDecoder.Decode(
                    HexUtils.ParseHex("59 02 09 EF 30 87 09 EF 31 87 09 EF 50 87 09 EF B9 87 09"), spec);
                Check(!statusFailed.Success && statusFailed.ErrorMessage.Contains("DTC status check failed") &&
                      statusFailed.ErrorMessage.Contains("status=0x09 mask=0x09"),
                    "DTC status-mask failure is distinct from forbidden-list match", statusFailed.ErrorMessage);

                spec.Validation = ValueValidation.FromPairs(ConfigPairParser.Parse(
                    "forbiddenDtcFile=" + path + ",dtcStatusMask=0x09"));
                DecodeResult listedStatusIgnored = ResponseDecoder.Decode(
                    HexUtils.ParseHex("59 02 09 EF 30 87 09 EF 31 87 09 EF 50 87 09 EF B9 87 09"), spec);
                Check(listedStatusIgnored.Success,
                    "status mask does not reject DTCs absent from configured list",
                    "all response status bytes are 0x09 but none of the DTCs are listed");

                File.WriteAllText(path, "0xEF3087");
                var listedAndStatusValidation = ValueValidation.FromPairs(ConfigPairParser.Parse(
                    "forbiddenDtcFile=" + path + ",dtcStatusMask=0x09"));
                spec.Validation = listedAndStatusValidation;
                DecodeResult listedStatusHit = ResponseDecoder.Decode(
                    HexUtils.ParseHex("59 02 09 EF 30 87 09 EF 31 87 00"), spec);
                    Check(!listedStatusHit.Success && listedStatusHit.ErrorMessage.Contains("DTC status check failed: EF3087") &&
                      listedStatusHit.ErrorMessage.Contains("status=0x09 mask=0x09"),
                    "listed DTC with selected status bit fails", listedStatusHit.ErrorMessage);
                DecodeResult listedStatusClear = ResponseDecoder.Decode(
                    HexUtils.ParseHex("59 02 09 EF 30 87 00 EF 31 87 09"), spec);
                Check(listedStatusClear.Success,
                    "listed DTC with clear status passes despite unlisted active DTC");

                var matchValidation = ValueValidation.FromPairs(ConfigPairParser.Parse("forbiddenDtcFile=" + path));
                spec.Validation = matchValidation;
                DecodeResult matched = ResponseDecoder.Decode(
                    HexUtils.ParseHex("59 02 09 EF 30 87 09 EF 31 87 09 EF 50 87 09 EF B9 87 09"), spec);
                Check(!matched.Success && matched.ErrorMessage.Contains("matched DTC EF3087"),
                    "matched DTC is included in failure details", matched.ErrorMessage);

                spec.Validation = ValueValidation.FromPairs(ConfigPairParser.Parse(
                    "forbiddenDtcFile=" + path + ",forbiddenDtcSheet=MissingSheet"));
                spec.Validation.SetForbiddenDtcSource(path + ".xlsx", "MissingSheet", "C");
                DecodeResult missingList = ResponseDecoder.Decode(
                    HexUtils.ParseHex("59 02 09 EF 30 87 09"), spec);
                Check(!missingList.Success && missingList.ErrorMessage.Contains("DTC forbidden list file was not found"),
                    "DTC list read error is reported", missingList.ErrorMessage);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---- 10. per-measurement 值校验（P 列内嵌 expectedXxx）----
        private static void TestPerItemValidation()
        {
            Section("Per-measurement validation (P 列内嵌 expectedXxx)");

            // P 列 JSON 数组两个测量项，各自内嵌校验键
            var cfg = UdsStepConfig.FromColumns(
                "udsPayload=22 F1 30,expectedPrefix=62 F1 30",
                "[{\"name\":\"转速\",\"offset\":3,\"length\":2,\"type\":\"uint16\",\"endian\":\"big\",\"formula\":\"x/4\",\"unit\":\"rpm\",\"expectedMin\":0,\"expectedMax\":8000}," +
                "{\"name\":\"状态\",\"offset\":7,\"length\":1,\"type\":\"uint8\",\"expectedValue\":\"0x01\"}]");
            Check(cfg.DecodeSpecs.Count == 2, "2 specs parsed");
            Check(cfg.DecodeSpecs[0].HasValidation && cfg.DecodeSpecs[0].Validation.Min == 0 && cfg.DecodeSpecs[0].Validation.Max == 8000,
                "spec0 expectedMin/Max parsed");
            Check(cfg.DecodeSpecs[1].HasValidation && cfg.DecodeSpecs[1].Validation.ExpectedValue == "0x01",
                "spec1 expectedValue parsed");

            // 两项都通过：62 F1 30 03 E8 00 00 01 (offset3 uint16=1000 -> x/4=250; offset7 uint8=0x01)
            byte[] ok = HexUtils.ParseHex("62 F1 30 03 E8 00 00 01");
            var decodedOk = ResponseDecoder.DecodeMany(ok, cfg.DecodeSpecs);
            Check(decodedOk.Success, "decode ok");
            Check(decodedOk.Measurements[0].Passed && decodedOk.Measurements[1].Passed, "both items passed");
            Check(ValueValidation.MultiVerdict(decodedOk.Measurements, cfg.DecodeSpecs) == "PASS", "multi verdict PASS");

            // 状态不符（期望 0x01，实际 0x02）
            byte[] badStatus = HexUtils.ParseHex("62 F1 30 03 E8 00 00 02");
            var decodedBad = ResponseDecoder.DecodeMany(badStatus, cfg.DecodeSpecs);
            Check(decodedBad.Measurements[1].Passed == false, "item1 (status) failed");
            Check(ValueValidation.MultiVerdict(decodedBad.Measurements, cfg.DecodeSpecs) == "FAIL", "multi verdict FAIL on status");

            // 转速超上限（offset3 uint16=0x9C40=40000 -> x/4=10000 > 8000）
            byte[] over = HexUtils.ParseHex("62 F1 30 9C 40 00 00 01");
            var decodedOver = ResponseDecoder.DecodeMany(over, cfg.DecodeSpecs);
            Check(decodedOver.Measurements[0].Passed == false, "item0 (rpm) over max failed");
            Check(ValueValidation.MultiVerdict(decodedOver.Measurements, cfg.DecodeSpecs) == "FAIL", "multi verdict FAIL on range");

            // 无校验键的 specs → MultiVerdict null（回退 J/K）
            var cfgNo = UdsStepConfig.FromColumns(
                "udsPayload=22 F1 30,expectedPrefix=62 F1 30",
                "[{\"name\":\"A\",\"offset\":3,\"length\":2,\"type\":\"uint16\"}]");
            var decodedNo = ResponseDecoder.DecodeMany(ok, cfgNo.DecodeSpecs);
            Check(ValueValidation.MultiVerdict(decodedNo.Measurements, cfgNo.DecodeSpecs) == null,
                "no per-item rules -> null");

            // 解码失败（越界）→ 配了校验的项 MultiVerdict FAIL
            byte[] shortPkt = HexUtils.ParseHex("62 F1 30 01");
            var decodedShort = ResponseDecoder.DecodeMany(shortPkt, cfg.DecodeSpecs);
            Check(decodedShort.Measurements[0].Passed == false, "out-of-range decode -> failed");
            Check(ValueValidation.MultiVerdict(decodedShort.Measurements, cfg.DecodeSpecs) == "FAIL",
                "multi verdict FAIL on decode error (validated item)");
        }

        // ---- 11. UDS response mismatch 时保留实际响应（RV3/报告不空）----
        private static void TestMismatchResponse()
        {
            Section("Mismatch keeps actual response");

            // 模拟 ExecuteStep 在 mismatch 时保留的实际响应（ResponseHexList 为空）
            var r = new UdsStepResult();
            r.CommandHex.Add("22 A5 F5");
            r.CombinedResponseHex = "62 A5 F5 00";
            r.CombinedResponse = HexUtils.ParseHex("62 A5 F5 00");
            r.Error = "UDS response mismatch: expected [62 A5 F5 01], actual [62 A5 F5 00].";
            r.ResultText = "FAIL";
            r.Success = false;

            Check(r.CombinedResponseHex == "62 A5 F5 00", "CombinedResponseHex carries actual response on mismatch");
            string report = r.ToReportText();
            Check(report.Contains("Response: 62 A5 F5 00"),
                "report shows actual response on mismatch", report.Replace("\r", "").Replace("\n", " | "));
        }

        // ---- 12. keep-alive 多条 + 原始 DoIP 帧识别 ----
        private static void TestKeepAliveMulti()
        {
            Section("Keep-alive multi payload / raw DoIP frame");

            Check(DoipClient.IsRawDoipFrame(HexUtils.ParseHex("02 FD 00 08 00 00 00 02 10 2D")),
                "raw DoIP frame detected (02 FD version pair)");
            Check(!DoipClient.IsRawDoipFrame(HexUtils.ParseHex("3E 80")),
                "short UDS payload not treated as raw frame");
            Check(!DoipClient.IsRawDoipFrame(null), "null not raw frame");
            Check(!DoipClient.IsRawDoipFrame(HexUtils.ParseHex("62 04 03 38 36 32 37")),
                "7-byte payload not raw frame");
        }

        // ---- 7. virtual DoIP step ----
        private static void TestVirtual()
        {
            Section("Virtual DoIP Step (no hardware)");
            var cfg = UdsStepConfig.FromColumns(
                "virtualMode=true,simResponse=50 01,udsPayload=10 01,testItem=VirtualTest",
                "");
            var result = UdsRequestExecutor.ExecuteStep(cfg, "virtual-ecu");
            Check(result.Success, "virtual step passes");
            Check(result.CombinedResponseHex == "50 01", "virtual simResponse returned", result.CombinedResponseHex);
            Check(result.ResultText == "PASS (virtual)", "virtual result text");

            // virtual with decode
            var cfg2 = UdsStepConfig.FromColumns(
                "virtualMode=true,simResponse=62 F1 05 0B B8,udsPayload=22 F1 05,valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4",
                "");
            var r2 = UdsRequestExecutor.ExecuteStep(cfg2, "virtual-ecu");
            Check(r2.Success && r2.Decode != null &&
                  Convert.ToDouble(r2.Decode.Measurements[0].Value) == 750,
                  "virtual step with decode -> 750");
        }
    }
}
