using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using Jabil.Uds.Core;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_Step — 配置驱动的 UDS 单步。G 列（配置参数1）原文作为 Argument0，
    /// P 列（解码配置）作为 Argument1。完全沿用 EonTest 测试工步表的配置语义。
    ///
    /// 脚本示例:
    ///   DoIP_UDS_Step("udsPayload=22 F1 05,expectedPrefix=62 F1 05,valueOffset=3,valueLength=2,valueType=uint16,endian=big,formula=x/4", "", "DoIP_ECU", "0", "5", "V")
    ///   ($result,$testItem,$req,$resp,$value,$unit,$limits,$testType) = DoIP_UDS_Step("...", "...", "DoIP_ECU")
    ///   第 3~5 个参数可单独传下限、上限、单位；为空时沿用配置字符串/P 列：
    ///   "valueType=uint16,unit=V,expectedMin=0,expectedMax=5"
    /// </summary>
    internal class DoIP_UDS_Step : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_Step(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string config1 = ArgString(0, "G Column (配置参数1)", "");
            string decodeConfig = ArgString(1, "P Column (解码配置)", "");
            string sessionKey = ArgString(2, "ECU/Session Key", "DoIP_ECU");
            string lowerLimitOverride = ArgString(3, "Lower Limit Override", "");
            string upperLimitOverride = ArgString(4, "Upper Limit Override", "");
            string unitOverride = ArgString(5, "Unit Override", "");

            config1 = ExpandVariables(config1);
            decodeConfig = ExpandVariables(decodeConfig);
            sessionKey = ExpandVariables(sessionKey);
            lowerLimitOverride = ExpandVariables(lowerLimitOverride);
            upperLimitOverride = ExpandVariables(upperLimitOverride);
            unitOverride = ExpandVariables(unitOverride);

            UdsStepConfig cfg;
            try
            {
                cfg = UdsStepConfig.FromColumns(config1, decodeConfig);
            }
            catch (Exception ex)
            {
                StepRunner.PublishError(this, "DoIP_UDS_Step", 2, "DoIP_UDS_Step: failed to parse config: " + ex.Message);
                return testResult;
            }

            if (!string.IsNullOrWhiteSpace(lowerLimitOverride)) cfg.LowerLimit = lowerLimitOverride.Trim();
            if (!string.IsNullOrWhiteSpace(upperLimitOverride)) cfg.UpperLimit = upperLimitOverride.Trim();
            if (!string.IsNullOrWhiteSpace(unitOverride)) cfg.Unit = unitOverride.Trim();

            if (cfg.VirtualMode)
            {
                UdsStepResult vr = UdsRequestExecutor.ExecuteStep(cfg, sessionKey);
                StepRunner.Publish(this, "DoIP_UDS_Step", vr, cfg);
                testResult.Status = TestStatus.Pass;
                return testResult;
            }

            if (string.IsNullOrEmpty(cfg.Host) && string.IsNullOrEmpty(cfg.UdsSequence) && string.IsNullOrEmpty(cfg.UdsPayload))
            {
                StepRunner.PublishError(this, "DoIP_UDS_Step", 2, "DoIP_UDS_Step: no host and no UDS command; set udsPayload/udsSequence or host.");
                return testResult;
            }
            if (string.IsNullOrEmpty(cfg.Host) &&
                !UdsRequestExecutor.SessionIsOpen(sessionKey) &&
                !UdsRequestExecutor.HasCachedTransportConfig(sessionKey))
            {
                StepRunner.PublishError(this, "DoIP_UDS_Step", 2,
                    "DoIP_UDS_Step: missing host and no open session (set host= in G column for the first step, or open the session first).");
                return testResult;
            }

            UdsStepResult result = UdsRequestExecutor.ExecuteStep(cfg, sessionKey);
            StepRunner.Publish(this, "DoIP_UDS_Step", result, cfg);

            // ZLG 风格：函数不设 Fail，成败状态放返回值（$result / ReturnValue0），由脚本自行判断，失败不终止。
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
