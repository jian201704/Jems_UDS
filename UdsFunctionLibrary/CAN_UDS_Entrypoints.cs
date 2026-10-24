using System;
using System.IO;
using System.Collections.Generic;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using Jabil.Uds.Core;
using Jabil.Uds.Can;
using ExcelDecodeCore;
using UdsEngine;

namespace UdsFunctionLibrary
{
    internal static class CanStepTransport
    {
        private sealed class InitializationDefaults
        {
            public int ChannelIndex;
            public int NominalBitrate;
            public int DataBitrate;
            public bool CanFd;
            public bool BitRateSwitch;
            public int CanFdStandard;
            public int CanFdMode;
            public uint RequestId;
            public uint ResponseId;
            public int FrameLength;
            public byte PaddingByte;
        }

        private static readonly Dictionary<string, InitializationDefaults> defaults =
            new Dictionary<string, InitializationDefaults>(StringComparer.OrdinalIgnoreCase);

        public static void SaveInitializationDefaults(string deviceKey, UdsStepConfig cfg)
        {
            string key = string.IsNullOrWhiteSpace(deviceKey) ? "default" : deviceKey.Trim();
            lock (defaults)
            {
                defaults[key] = new InitializationDefaults
                {
                    ChannelIndex = cfg.CanChannelIndex,
                    NominalBitrate = cfg.CanNominalBitrate,
                    DataBitrate = cfg.CanDataBitrate,
                    CanFd = cfg.CanFd,
                    BitRateSwitch = cfg.CanBitRateSwitch,
                    CanFdStandard = cfg.CanFdStandard,
                    CanFdMode = cfg.CanFdMode,
                    RequestId = cfg.CanRequestId,
                    ResponseId = cfg.CanResponseId,
                    FrameLength = cfg.CanFrameLength,
                    PaddingByte = cfg.CanPaddingByte
                };
            }
        }

        public static void ClearInitializationDefaults()
        {
            lock (defaults) defaults.Clear();
        }

        private static void ApplyInitializationDefaults(UdsStepConfig cfg)
        {
            string key = string.IsNullOrWhiteSpace(cfg.CanDeviceKey) ? "default" : cfg.CanDeviceKey.Trim();
            InitializationDefaults value;
            lock (defaults)
            {
                if (!defaults.TryGetValue(key, out value)) return;
            }
            if (!cfg.HasExplicitCanChannel) cfg.CanChannelIndex = value.ChannelIndex;
            if (!cfg.HasExplicitCanRequestId && value.RequestId != 0) cfg.CanRequestId = value.RequestId;
            if (!cfg.HasExplicitCanResponseId && value.ResponseId != 0) cfg.CanResponseId = value.ResponseId;
            if (cfg.CanFrameLength == 0) cfg.CanFrameLength = value.FrameLength;
            if (!cfg.HasExplicitCanChannel) cfg.CanPaddingByte = value.PaddingByte;
            if (!cfg.HasExplicitCanChannel || !cfg.HasExplicitCanRequestId || !cfg.HasExplicitCanResponseId)
            {
                if (!cfg.HasExplicitCanChannel)
                {
                    cfg.CanNominalBitrate = value.NominalBitrate;
                    cfg.CanDataBitrate = value.DataBitrate;
                    cfg.CanFd = value.CanFd;
                    cfg.CanBitRateSwitch = value.BitRateSwitch;
                    cfg.CanFdStandard = value.CanFdStandard;
                    cfg.CanFdMode = value.CanFdMode;
                }
            }
        }

        public static CanIsoTpTransport Create(UdsStepConfig cfg)
        {
            ApplyInitializationDefaults(cfg);
            ICanDeviceProvider provider = string.IsNullOrWhiteSpace(cfg.CanDeviceKey)
                ? CanTransportRegistry.Current
                : CanTransportRegistry.Get(cfg.CanDeviceKey);
            if (provider == null)
                throw new InvalidOperationException("CAN provider is not registered for device key: " + cfg.CanDeviceKey);
            var options = new CanChannelOptions
            {
                ChannelIndex = cfg.CanChannelIndex,
                NominalBitrate = cfg.CanNominalBitrate,
                DataBitrate = cfg.CanDataBitrate,
                CanFd = cfg.CanFd,
                BitRateSwitch = cfg.CanBitRateSwitch,
                CanFdStandard = cfg.CanFdStandard,
                CanFdMode = cfg.CanFdMode,
                FrameLength = cfg.CanFrameLength,
                PaddingByte = cfg.CanPaddingByte,
                CustomBitrateConfig = cfg.CanCustomBitrateConfig
            };
            uint requestId = cfg.CanRequestId != 0 ? cfg.CanRequestId : 0x7E0U;
            uint responseId = cfg.CanResponseId != 0 ? cfg.CanResponseId : 0x7E8U;
            return new CanIsoTpTransport(provider, options, requestId, responseId);
        }

        public static bool IsInitializeOnly(string config)
        {
            Dictionary<string, string> pairs = ConfigPairParser.Parse(config);
            return ConfigPairParser.GetBool(pairs, "initializeOnly", false) ||
                   ConfigPairParser.GetBool(pairs, "initOnly", false);
        }

        public static void Initialize(UdsStepConfig cfg)
        {
            RawCanChannel.Open(cfg.CanChannelIndex, cfg.CanFd, cfg.CanBitRateSwitch,
                cfg.CanNominalBitrate, cfg.CanDataBitrate, cfg.CanFdStandard,
                cfg.CanFdMode, cfg.CanDeviceKey);
            SaveInitializationDefaults(cfg.CanDeviceKey, cfg);
            CanKeepAliveManager.StartOrUpdate(cfg);
        }

        public static string SessionKey(UdsStepConfig cfg, string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(cfg.CanDeviceKey)) return sessionKey;
            return cfg.CanDeviceKey.Trim() + ":" + sessionKey;
        }

        public static UdsStepResult Execute(UdsStepConfig cfg, string sessionKey)
        {
            CanIsoTpTransport transport = Create(cfg);
            CanKeepAliveManager.StartOrUpdate(cfg);
            lock (RawCanChannel.Sync(cfg.CanChannelIndex, cfg.CanDeviceKey))
                return UdsTransactionExecutor.Execute(cfg, transport);
        }
    }

    internal class CAN_UDS_Step : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }

        public CAN_UDS_Step(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string config1 = ExpandVariables(ArgString(0, "G Column (配置参数1)", ""));
            string decodeConfig = ExpandVariables(ArgString(1, "P Column (解码配置)", ""));
            string canDeviceKey = ExpandVariables(ArgString(2, "CAN Device Key", ""));
            UdsStepConfig cfg;
            try { cfg = UdsStepConfig.FromColumns(config1, decodeConfig); }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "CAN_UDS_Step", 2, "CAN_UDS_Step: failed to parse config: " + ex.Message);
                return testResult;
            }
            cfg.IsCan = true;
            if (canDeviceKey.Length > 0) cfg.CanDeviceKey = canDeviceKey.Trim();
            string lower = ExpandVariables(ArgString(3, "Lower Limit Override", ""));
            string upper = ExpandVariables(ArgString(4, "Upper Limit Override", ""));
            string unit = ExpandVariables(ArgString(5, "Unit Override", ""));
            if (lower.Length > 0) cfg.LowerLimit = lower.Trim();
            if (upper.Length > 0) cfg.UpperLimit = upper.Trim();
            if (unit.Length > 0) cfg.Unit = unit.Trim();
            bool initializeOnly = CanStepTransport.IsInitializeOnly(config1);
            if (initializeOnly)
            {
                try
                {
                    CanStepTransport.Initialize(cfg);
                    SetReturn(0, true);
                    SetReturn(1, "CAN channel initialized");
                    SetReturn(2, "");
                    SetReturn(3, "");
                    SetReturn(4, "");
                    SetReturn(5, "");
                    SetReturn(6, "");
                    SetReturn(7, 0);
                    SetReturn(8, "");
                    SetReturn(9, "PASS");
                    SetReturn(10, "");
                    SetPersistent("Result_step_expectedContext", "");
                    SetPersistent("Result_step_FailureStage", "PASS");
                    SetPersistent("Result_step_Error", "");
                    UpdateStatusKey("CAN_UDS_Step: initialized only - channel " + cfg.CanChannelIndex);
                }
                catch (Exception ex)
                {
                    SetReturn(0, false);
                    SetReturn(1, "CAN initialization failed");
                    SetReturn(2, "");
                    SetReturn(3, "");
                    SetReturn(4, "");
                    SetReturn(5, "");
                    SetReturn(6, "");
                    SetReturn(7, 0);
                    SetReturn(8, "");
                    SetReturn(9, "FAIL");
                    SetReturn(10, ex.Message);
                    SetPersistent("Result_step_expectedContext", "");
                    SetPersistent("Result_step_FailureStage", "CONFIG");
                    SetPersistent("Result_step_Error", ex.Message);
                    UpdateStatusKey("CAN_UDS_Step: initialization failed - " + ex.Message);
                }
                testResult.Status = TestStatus.Pass;
                return testResult;
            }
            try
            {
                UdsStepResult result = UdsTransactionExecutor.Execute(cfg, CanStepTransport.Create(cfg));
                StepRunner.Publish(this, "CAN_UDS_Step", result, cfg);
            }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "CAN_UDS_Step", 2, "CAN_UDS_Step: " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    internal class CAN_UDS_ExcelStep : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }

        public CAN_UDS_ExcelStep(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string excelPath = ArgString(0, "Excel Path", "");
            string sheetName = ArgString(1, "Sheet Name", ExcelStepReader.DefaultSheetName);
            string stepNo = ArgString(2, "工步号 (Step No)", "");
            if (string.IsNullOrEmpty(excelPath) || !File.Exists(excelPath))
            {
                StepRunner.PublishError(this, "CAN_UDS_ExcelStep", 2, "CAN_UDS_ExcelStep: Excel file not found: '" + excelPath + "'");
                return testResult;
            }
            ExcelStepRow row;
            try { row = ExcelStepReader.ReadRowByStepNo(excelPath, stepNo, sheetName); }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "CAN_UDS_ExcelStep", 2, "CAN_UDS_ExcelStep: failed to read Excel: " + ex.Message);
                return testResult;
            }
            if (row == null)
            {
                StepRunner.PublishError(this, "CAN_UDS_ExcelStep", 2, "CAN_UDS_ExcelStep: step not found: " + stepNo);
                return testResult;
            }
            if (!row.ShouldRun())
            {
                StepRunner.SavePersistentSimple(this, stepNo, true, "", "SKIP", row);
                ClearReturns();
                UpdateStatusKey("CAN_UDS_ExcelStep: SKIP disabled 工步号 " + stepNo);
                testResult.Status = TestStatus.Pass;
                return testResult;
            }
            row.Config1 = ExpandVariables(row.Config1);
            row.DecodeConfig = ExpandVariables(row.DecodeConfig);
            UdsStepConfig cfg = row.ToStepConfig(Path.GetDirectoryName(Path.GetFullPath(excelPath)), null, excelPath);
            cfg.IsCan = true;
            if (CanStepTransport.IsInitializeOnly(row.Config1))
            {
                try
                {
                    CanStepTransport.Initialize(cfg);
                    StepRunner.SavePersistentSimple(this, stepNo, true, "", "PASS", row);
                    UpdateStatusKey("CAN_UDS_ExcelStep: initialized only - channel " + cfg.CanChannelIndex);
                }
                catch (Exception ex)
                {
                    StepRunner.PublishError(this, "CAN_UDS_ExcelStep", 2, "CAN_UDS_ExcelStep: CAN initialization failed: " + ex.Message);
                }
                testResult.Status = TestStatus.Pass;
                return testResult;
            }
            try
            {
                string sessionKey = CanStepTransport.SessionKey(cfg, cfg.SessionKey);
                UdsStepResult result = UdsTransactionExecutor.Execute(cfg, CanStepTransport.Create(cfg));
                StepRunner.Publish(this, "CAN_UDS_ExcelStep", result, cfg, row);
            }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "CAN_UDS_ExcelStep", 2, "CAN_UDS_ExcelStep: " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    internal class CAN_UDS_ExcelRunAll : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }

        public CAN_UDS_ExcelRunAll(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string excelPath = ArgString(0, "Excel Path", "");
            string sheetName = ArgString(1, "Sheet Name", ExcelStepReader.DefaultSheetName);
            int maxSteps = ArgInt(2, "Max Steps (0=all)", 0);
            if (string.IsNullOrEmpty(excelPath) || !File.Exists(excelPath))
            {
                StepRunner.PublishError(this, "CAN_UDS_ExcelRunAll", 2, "CAN_UDS_ExcelRunAll: Excel file not found: '" + excelPath + "'");
                return testResult;
            }
            List<ExcelStepRow> rows;
            try { rows = ExcelStepReader.ReadAllRows(excelPath, sheetName); }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "CAN_UDS_ExcelRunAll", 2, "CAN_UDS_ExcelRunAll: failed to read Excel: " + ex.Message);
                return testResult;
            }
            int passed = 0, failed = 0, runCount = 0;
            string firstError = "";
            string excelDir = Path.GetDirectoryName(Path.GetFullPath(excelPath));
            foreach (ExcelStepRow row in rows)
            {
                if (maxSteps > 0 && runCount >= maxSteps) break;
                if (!row.ShouldRun())
                {
                    StepRunner.SavePersistentSimple(this, row.StepNo, true, "", "SKIP", row);
                    continue;
                }
                if (!row.IsUdsStep()) continue;
                bool ok = false;
                try
                {
                    row.Config1 = ExpandVariables(row.Config1);
                    row.DecodeConfig = ExpandVariables(row.DecodeConfig);
                    UdsStepConfig cfg = row.ToStepConfig(excelDir, null, excelPath);
                    cfg.IsCan = true;
                    if (CanStepTransport.IsInitializeOnly(row.Config1))
                    {
                        CanStepTransport.Initialize(cfg);
                        StepRunner.SavePersistentSimple(this, row.StepNo, true, "", "PASS", row);
                        runCount++;
                        passed++;
                        UpdateStatusKey("CAN_UDS_ExcelRunAll: initialized only - channel " + cfg.CanChannelIndex);
                        continue;
                    }
                    UdsStepResult result = UdsTransactionExecutor.Execute(cfg, CanStepTransport.Create(cfg));
                    ok = result.Success;
                    string values, units, limits;
                    StepRunner.BuildMeasurements(result, cfg, row, out values, out units, out limits);
                    StepRunner.SavePersistentResult(this, row.StepNo, ok, result, values, units, limits, cfg, row);
                    if (!ok && firstError.Length == 0) firstError = "工步号 " + row.StepNo + ": " + result.Error;
                    UpdateStatusKey("CAN_UDS_ExcelRunAll: " + (ok ? "PASS" : "FAIL") + " 工步号 " + row.StepNo);
                }
                catch (Exception ex)
                {
                    firstError = firstError.Length == 0 ? "工步号 " + row.StepNo + ": " + ex.Message : firstError;
                    UpdateStatusKey("CAN_UDS_ExcelRunAll: EXCEPTION 工步号 " + row.StepNo + " - " + ex.Message);
                }
                runCount++;
                if (ok) passed++; else failed++;
            }
            SetReturn(0, passed);
            SetReturn(1, failed);
            SetReturn(2, runCount);
            SetReturn(3, failed == 0 ? "PASS" : "FAIL");
            SetReturn(4, firstError);
            UpdateStatusKey("CAN_UDS_ExcelRunAll: " + passed + "/" + runCount + " passed");
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
