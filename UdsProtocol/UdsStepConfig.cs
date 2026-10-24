using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ExcelDecodeCore;

namespace UdsCore
{
    /// <summary>
    /// Typed step configuration parsed from EonTest's G column (配置参数1) and P column (解码配置).
    /// Field set mirrors DoipMasterPlugin::executeStep reads.
    /// </summary>
    public class UdsStepConfig
    {
        // transport
        public string Host = "";
        public int Port = 13400;
        public ushort SourceAddress = 0x102D;
        public ushort TargetAddress = 0x1008;
        public byte ActivationType = 0x00;
        public int TimeoutMs = 5000;
        public int ResponsePendingTimeoutMs = 0;
        public int RoutingActivationRetryCount = 2;
        public int RoutingActivationRetryDelayMs = 1000;
        public bool TcpKeepAlive = true;
        public string LocalBindAddress = "";
        public int LocalBindPort = 0;

        // CAN transport
        public bool IsCan = false;
        public int CanChannelIndex = 0;
        public int CanNominalBitrate = 500000;
        public int CanDataBitrate = 2000000;
        public bool CanFd = false;
        public bool CanBitRateSwitch = false;
        public int CanFdStandard = 0;
        public int CanFdMode = 0;
        public uint CanRequestId = 0;
        public uint CanResponseId = 0;
        public string CanDeviceKey = "";
        public string CanCustomBitrateConfig = "";
        public int CanFrameLength = 0;
        public byte CanPaddingByte = 0xCC;
        public bool HasExplicitCanChannel = false;
        public bool HasExplicitCanNominalBitrate = false;
        public bool HasExplicitCanDataBitrate = false;
        public bool HasExplicitCanFd = false;
        public bool HasExplicitCanBitRateSwitch = false;
        public bool HasExplicitCanFdStandard = false;
        public bool HasExplicitCanFdMode = false;
        public bool HasExplicitCanCustomBitrateConfig = false;
        public bool HasExplicitCanRequestId = false;
        public bool HasExplicitCanResponseId = false;

        // whether these were explicitly set in the step (G column); used to avoid
        // Sheet2 (设备通道映射表) backfill overriding an explicit step value
        public bool HasExplicitHost = false;
        public bool HasExplicitPort = false;
        public bool HasExplicitSourceAddress = false;
        public bool HasExplicitTargetAddress = false;

        // command
        public string UdsPayload = "";     // single command (supports {pad:N} {retry:N})
        public string UdsSequence = "";    // pipe-separated command list
        public string ExpectedPrefix = ""; // pipe-separated per command
        public string ExpectedResponse = ""; // pipe-separated per command
        public string ExpectedSuffix = ""; // pipe-separated per command
        public string ExpectedByteOffset = ""; // pipe-separated zero-based offsets
        public string ExpectedByteValue = ""; // pipe-separated expected byte values
        public int DataFrom = -1;
        public bool HasExplicitDataFrom = false;
        public int MinRequestIntervalMs = 20;
        public int NackRetryCount = 2;
        public int NackRetryDelayMs = 100;
        public int SequenceRetryCount = 1;
        public int SequenceRetryDelayMs = 500;
        public int DecodeRetryCount = 0;
        public int DecodeRetryDelayMs = 500;
        public int ResponsePendingRetryCount = 3;
        public int UdsInitialDelayMs = 0;
        public int IdleTimeoutMs = 60000;

        // session
        public string ConnectionMode = "session";   // session / perStep
        public bool CloseConnection = false;
        public bool ConnectionOnly = false;

        // keep-alive (TesterPresent)
        public bool KeepAliveEnabled = false;
        public int KeepAliveIntervalMs = 2000;
        public string KeepAlivePayload = "3E 80";

        // virtual
        public bool VirtualMode = false;
        public string SimResponse = "50 01";

        // decode (P column)
        public List<DecodeSpec> DecodeSpecs = new List<DecodeSpec>();   // inline specs
        public string DecodeProfile = "";                                // JSON file reference
        public bool HasDecodeConfig = false;

        // single-value validation (string / status / numeric): 唯一正确值，无上下限
        public string ExpectedValue = "";
        public bool HasExpectedValue = false;

        // comprehensive value validation rules (contains/regex/length/range/equality)
        public ValueValidation Validation = new ValueValidation();
        public bool HasValidation { get { return Validation != null && Validation.HasAny; } }

        // step bookkeeping
        public string TestItem = "UDS Step";
        public string LowerLimit = "";
        public string UpperLimit = "";
        public string Unit = "";
        public string SaveAs = "";

        /// <summary>The key used for session pooling (ECU identity).</summary>
        public string SessionKey = "";

        /// <summary>Base directory used to resolve decodeProfile JSON files (e.g. Excel file dir).</summary>
        public string BaseDirectory = "";

        public bool IsPerStep
        {
            get { return string.Equals(ConnectionMode, "perStep", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Parse a G column string (key=value pairs) into a typed config.</summary>
        public static UdsStepConfig FromPairs(IDictionary<string, string> pairs)
        {
            var cfg = new UdsStepConfig();
            cfg.Host = ConfigPairParser.Get(pairs, "host", ConfigPairParser.Get(pairs, "ip", ""));
            cfg.Port = ConfigPairParser.GetInt(pairs, "port", 13400);
            if (cfg.Port == 0) cfg.Port = 13400;
            cfg.SourceAddress = HexUtils.TryParseAddress(ConfigPairParser.Get(pairs, "sourceAddress", "0x102D"), 0x102D);
            cfg.TargetAddress = HexUtils.TryParseAddress(ConfigPairParser.Get(pairs, "targetAddress", "0x1008"), 0x1008);
            cfg.HasExplicitHost = ConfigPairParser.Get(pairs, "host", null) != null ||
                                  ConfigPairParser.Get(pairs, "ip", null) != null;
            cfg.HasExplicitPort = ConfigPairParser.Get(pairs, "port", null) != null;
            cfg.HasExplicitSourceAddress = ConfigPairParser.Get(pairs, "sourceAddress", null) != null;
            cfg.HasExplicitTargetAddress = ConfigPairParser.Get(pairs, "targetAddress", null) != null;
            cfg.ActivationType = HexUtils.TryParseByte(ConfigPairParser.Get(pairs, "activationType", "0x00"), 0x00);
            cfg.TimeoutMs = ConfigPairParser.GetInt(pairs, "timeoutMs",
                             ConfigPairParser.GetInt(pairs, "timeout", 5000));
            cfg.ResponsePendingTimeoutMs = Math.Max(0,
                ConfigPairParser.GetInt(pairs, "responsePendingTimeoutMs", 0));
            cfg.RoutingActivationRetryCount = Clamp(
                ConfigPairParser.GetInt(pairs, "routingActivationRetryCount", 2), 0, 5);
            cfg.RoutingActivationRetryDelayMs = Clamp(
                ConfigPairParser.GetInt(pairs, "routingActivationRetryDelayMs", 1000), 0, 60000);
            cfg.TcpKeepAlive = ConfigPairParser.GetBool(pairs, "tcpKeepAlive", true);
            cfg.LocalBindAddress = ConfigPairParser.Get(pairs, "localBindAddress", "");
            cfg.LocalBindPort = ConfigPairParser.GetInt(pairs, "localBindPort", 0);
            cfg.IsCan = ConfigPairParser.GetBool(pairs, "can", false) ||
                        ConfigPairParser.GetBool(pairs, "canFd", false) ||
                        ConfigPairParser.Get(pairs, "transport", "").Equals("can", StringComparison.OrdinalIgnoreCase);
            cfg.CanChannelIndex = ConfigPairParser.GetInt(pairs, "canChannel", 0);
            cfg.HasExplicitCanChannel = ConfigPairParser.Get(pairs, "canChannel", null) != null;
            cfg.HasExplicitCanNominalBitrate = ConfigPairParser.Get(pairs, "canNominalBitrate", null) != null;
            cfg.HasExplicitCanDataBitrate = ConfigPairParser.Get(pairs, "canDataBitrate", null) != null;
            cfg.HasExplicitCanFd = ConfigPairParser.Get(pairs, "canFd", null) != null;
            cfg.HasExplicitCanBitRateSwitch = ConfigPairParser.Get(pairs, "canBitRateSwitch", null) != null;
            cfg.HasExplicitCanFdStandard = ConfigPairParser.Get(pairs, "canFdStandard", null) != null;
            cfg.HasExplicitCanFdMode = ConfigPairParser.Get(pairs, "canFdMode", null) != null;
            cfg.HasExplicitCanCustomBitrateConfig = ConfigPairParser.Get(pairs, "canCustomBitrateConfig", null) != null;
            cfg.CanNominalBitrate = NormalizeBitrate(ConfigPairParser.GetInt(pairs, "canNominalBitrate", 500000));
            cfg.CanDataBitrate = NormalizeBitrate(ConfigPairParser.GetInt(pairs, "canDataBitrate", 2000000));
            cfg.CanFd = ConfigPairParser.GetBool(pairs, "canFd", false);
            cfg.CanBitRateSwitch = ConfigPairParser.GetBool(pairs, "canBitRateSwitch", cfg.CanFd);
            cfg.CanFdStandard = ConfigPairParser.GetInt(pairs, "canFdStandard", 0);
            cfg.CanFdMode = ConfigPairParser.GetInt(pairs, "canFdMode", 0);
            cfg.CanRequestId = HexUtils.TryParseAddress(ConfigPairParser.Get(pairs, "canRequestId", "0"), 0);
            cfg.CanResponseId = HexUtils.TryParseAddress(ConfigPairParser.Get(pairs, "canResponseId", "0"), 0);
            cfg.HasExplicitCanRequestId = ConfigPairParser.Get(pairs, "canRequestId", null) != null;
            cfg.HasExplicitCanResponseId = ConfigPairParser.Get(pairs, "canResponseId", null) != null;
            cfg.CanDeviceKey = ConfigPairParser.Get(pairs, "canDeviceKey", ConfigPairParser.Get(pairs, "deviceKey", ""));
            cfg.CanCustomBitrateConfig = ConfigPairParser.Get(pairs, "canCustomBitrateConfig", "");
            cfg.CanFrameLength = Math.Max(0, ConfigPairParser.GetInt(pairs, "canMinFrameLength",
                ConfigPairParser.GetInt(pairs, "canFrameLength", 0)));
            cfg.CanPaddingByte = HexUtils.TryParseByte(
                ConfigPairParser.Get(pairs, "canPaddingByte", "CC"), 0xCC);

            cfg.UdsPayload = ConfigPairParser.Get(pairs, "udsPayload", "");
            cfg.UdsSequence = ConfigPairParser.Get(pairs, "udsSequence", "");
            cfg.ExpectedPrefix = ConfigPairParser.Get(pairs, "expectedPrefix", "");
            cfg.ExpectedResponse = ConfigPairParser.Get(pairs, "expectedResponse", "");
            cfg.ExpectedSuffix = ConfigPairParser.Get(pairs, "expectedSuffix", "");
            cfg.ExpectedByteOffset = ConfigPairParser.Get(pairs, "expectedByteOffset", "");
            cfg.ExpectedByteValue = ConfigPairParser.Get(pairs, "expectedByteValue", "");
            cfg.DataFrom = ConfigPairParser.GetInt(pairs, "dataFrom", -1);
            cfg.HasExplicitDataFrom = ConfigPairParser.Get(pairs, "dataFrom", null) != null;
            cfg.MinRequestIntervalMs = ConfigPairParser.GetInt(pairs, "minRequestIntervalMs", 20);
            cfg.NackRetryCount = Clamp(ConfigPairParser.GetInt(pairs, "udsNackRetryCount",
                ConfigPairParser.GetInt(pairs, "doipNackRetryCount", 2)), 0, 5);
            cfg.NackRetryDelayMs = Clamp(ConfigPairParser.GetInt(pairs, "udsNackRetryDelayMs",
                ConfigPairParser.GetInt(pairs, "doipNackRetryDelayMs", 100)), 50, 2000);
            cfg.SequenceRetryCount = Clamp(ConfigPairParser.GetInt(pairs, "sequenceRetryCount", 1), 1, 20);
            cfg.SequenceRetryDelayMs = Math.Max(0, ConfigPairParser.GetInt(pairs, "sequenceRetryDelayMs", 500));
            cfg.DecodeRetryCount = Clamp(ConfigPairParser.GetInt(pairs, "decodeRetryCount",
                ConfigPairParser.GetInt(pairs, "decodeRetry", 0)), 0, 20);
            cfg.DecodeRetryDelayMs = Math.Max(0, ConfigPairParser.GetInt(pairs, "decodeRetryDelayMs",
                ConfigPairParser.GetInt(pairs, "decodeRetryDelay", 500)));
            cfg.ResponsePendingRetryCount = Clamp(ConfigPairParser.GetInt(pairs, "responsePendingRetryCount", 3), 0, 20);
            cfg.UdsInitialDelayMs = Math.Max(0, ConfigPairParser.GetInt(pairs, "udsInitialDelayMs", 0));
            cfg.IdleTimeoutMs = Math.Max(0, ConfigPairParser.GetInt(pairs, "idleTimeoutMs", 60000));

            cfg.ConnectionMode = ConfigPairParser.Get(pairs, "connectionMode", "session");
            cfg.CloseConnection = ConfigPairParser.GetBool(pairs, "closeConnection", false);
            cfg.ConnectionOnly = ConfigPairParser.GetBool(pairs, "initializeOnly",
                ConfigPairParser.GetBool(pairs, "connectionOnly", false));

            cfg.KeepAliveEnabled = ConfigPairParser.GetBool(pairs, "canKeepAliveEnabled",
                ConfigPairParser.GetBool(pairs, "udsKeepAliveEnabled", false));
            cfg.KeepAliveIntervalMs = Math.Max(250, ConfigPairParser.GetInt(pairs, "canKeepAliveIntervalMs",
                ConfigPairParser.GetInt(pairs, "udsKeepAliveIntervalMs", 2000)));
            cfg.KeepAlivePayload = ConfigPairParser.Get(pairs, "canKeepAlivePayload",
                ConfigPairParser.Get(pairs, "udsKeepAlivePayload", "3E 80"));

            cfg.VirtualMode = ConfigPairParser.GetBool(pairs, "virtualMode", false);
            cfg.SimResponse = ConfigPairParser.Get(pairs, "simResponse", "50 01");

            cfg.TestItem = ConfigPairParser.Get(pairs, "testItem", "UDS Step");
            cfg.LowerLimit = ConfigPairParser.Get(pairs, "lowerLimit", "");
            cfg.UpperLimit = ConfigPairParser.Get(pairs, "upperLimit", "");
            cfg.Unit = ConfigPairParser.Get(pairs, "unit", "");
            cfg.SaveAs = ConfigPairParser.Get(pairs, "saveAs",
                ConfigPairParser.Get(pairs, "resultName", ""));

            // 唯一正确值（字符串/状态/数值），无上下限时用这个
            cfg.ExpectedValue = ConfigPairParser.Get(pairs, "expectedValue", "");
            cfg.HasExpectedValue = cfg.ExpectedValue.Length > 0;
            // 综合校验规则：包含/正则/长度/数值区间/相等
            cfg.Validation = ValueValidation.FromPairs(pairs);

            // decode profile reference
            cfg.DecodeProfile = ConfigPairParser.Get(pairs, "decodeProfile", "");
            // inline single-value decode keys -> one DecodeSpec
            var inlineSpec = DecodeSpec.FromPairs(pairs);
            if (inlineSpec.HasExplicitDecode())
            {
                if (string.IsNullOrEmpty(inlineSpec.Name))
                    inlineSpec.Name = cfg.TestItem;
                cfg.DecodeSpecs.Add(inlineSpec);
                cfg.HasDecodeConfig = true;
            }
            if (!string.IsNullOrEmpty(cfg.DecodeProfile)) cfg.HasDecodeConfig = true;
            return cfg;
        }

        private static int NormalizeBitrate(int value)
        {
            return value > 0 && value < 100000 ? value * 1000 : value;
        }

        /// <summary>
        /// Build a config from a G column string plus an optional P column decode string.
        /// P column may be: JSON array (decodeSpecs), JSON object (decodeProfile ref / measurements),
        /// or plain key=value pairs (inline single decode).
        /// </summary>
        public static UdsStepConfig FromColumns(string config1, string decodeConfig)
        {
            var pairs = ConfigPairParser.Parse(config1);
            var cfg = FromPairs(pairs);
            ApplyDecodeColumn(cfg, decodeConfig);
            return cfg;
        }

        public static void ApplyDecodeColumn(UdsStepConfig cfg, string decodeConfig)
        {
            if (string.IsNullOrWhiteSpace(decodeConfig)) return;

            // try JSON first (mirrors XlsxParser / DoipMasterPlugin decode handling)
            object json;
            string decodeText = decodeConfig.Trim();
            if (!MiniJson.TryParse(decodeText, out json))
            {
                // Be tolerant of legacy P-column JSON written as "key"=value.
                string normalized = Regex.Replace(decodeText, "(\\\"[^\\\"]+\\\")\\s*=", "$1:");
                if (!string.Equals(normalized, decodeText, StringComparison.Ordinal))
                    MiniJson.TryParse(normalized, out json);
            }
            if (json != null)
            {
                var array = json as List<object>;
                if (array != null)
                {
                    cfg.DecodeSpecs.Clear();
                    foreach (object item in array)
                    {
                        var node = item as Dictionary<string, object>;
                        if (node != null) cfg.DecodeSpecs.Add(DecodeSpec.FromJson(node));
                    }
                    cfg.HasDecodeConfig = cfg.DecodeSpecs.Count > 0;
                    return;
                }
                var obj = json as Dictionary<string, object>;
                if (obj != null)
                {
                    // object with "measurements" array (decodeProfile file content)
                    object measurements;
                    if (obj.TryGetValue("measurements", out measurements))
                    {
                        var measureList = measurements as List<object>;
                        if (measureList != null)
                        {
                            cfg.DecodeSpecs.Clear();
                            foreach (object item in measureList)
                            {
                                var node = item as Dictionary<string, object>;
                                if (node != null) cfg.DecodeSpecs.Add(DecodeSpec.FromJson(node));
                            }
                            cfg.HasDecodeConfig = cfg.DecodeSpecs.Count > 0;
                            return;
                        }
                    }
                    // object with "decodeProfile" -> file reference
                    object profile;
                    if (obj.TryGetValue("decodeProfile", out profile) && profile != null)
                    {
                        cfg.DecodeProfile = Convert.ToString(profile, System.Globalization.CultureInfo.InvariantCulture).Trim();
                        cfg.HasDecodeConfig = true;
                        return;
                    }
                    // Accept a single JSON decode object as a convenience in P column.
                    var single = DecodeSpec.FromJson(obj);
                    if (single.HasExplicitDecode())
                    {
                        if (string.IsNullOrEmpty(single.Name)) single.Name = cfg.TestItem;
                        cfg.DecodeSpecs.Clear();
                        cfg.DecodeSpecs.Add(single);
                        cfg.HasDecodeConfig = true;
                    }
                }
                return;
            }

            // plain key=value inline decode
            var decodePairs = ConfigPairParser.Parse(decodeConfig);
            if (decodePairs.Count == 0) return;
            var inline = DecodeSpec.FromPairs(decodePairs);
            if (inline.HasExplicitDecode())
            {
                if (string.IsNullOrEmpty(inline.Name)) inline.Name = cfg.TestItem;
                cfg.DecodeSpecs.Clear();
                cfg.DecodeSpecs.Add(inline);
                cfg.HasDecodeConfig = true;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
