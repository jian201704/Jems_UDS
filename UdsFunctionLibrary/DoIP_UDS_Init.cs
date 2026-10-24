using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_Init — 手动建立 DoIP 会话（TCP 连接 + Routing Activation）。
    /// 适合想手动控制会话生命周期的脚本（对齐现有 ZLG CAN 的 ZUDS_Init 习惯）。
    ///
    /// 脚本示例:
    ///   ($handle,$ok) = DoIP_UDS_Init("172.16.0.8", 13400, "0x102D", "0x1008", "0x00", "DoIP_ECU")
    /// </summary>
    internal class DoIP_UDS_Init : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_Init(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string host = ArgString(0, "Host", "");
            int port = ArgInt(1, "Port", 13400);
            string sourceAddress = ArgString(2, "Source Address", "0x102D");
            string targetAddress = ArgString(3, "Target Address", "0x1008");
            string activationType = ArgString(4, "Activation Type", "0x00");
            string sessionKey = ArgString(5, "ECU/Session Key", "DoIP_ECU");

            if (string.IsNullOrEmpty(host))
            {
                StepRunner.PublishError(this, "DoIP_UDS_Init", 2, "DoIP_UDS_Init: missing host (Argument0).");
                return testResult;
            }

            var cfg = new UdsStepConfig
            {
                Host = host,
                Port = port == 0 ? 13400 : port,
                SourceAddress = HexUtils.TryParseAddress(sourceAddress, 0x102D),
                TargetAddress = HexUtils.TryParseAddress(targetAddress, 0x1008),
                ActivationType = HexUtils.TryParseByte(activationType, 0x00),
                TimeoutMs = 5000,
                SessionKey = sessionKey
            };

            string error;
            if (!UdsRequestExecutor.OpenSession(cfg, sessionKey, out error))
            {
                SetReturn(0, sessionKey);
                SetReturn(1, 1); // status: fail
                SetReturn(2, error);
                UpdateStatusKey("DoIP_UDS_Init: FAIL - " + error);
                // ZLG 风格：不设 Fail，状态放返回值
                testResult.Status = TestStatus.Pass;
                return testResult;
            }

            SetReturn(0, sessionKey);
            SetReturn(1, 0); // status: success
            SetReturn(2, host + ":" + port + " routing active");
            UpdateStatusKey("DoIP_UDS_Init: OK - " + host + ":" + port);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
