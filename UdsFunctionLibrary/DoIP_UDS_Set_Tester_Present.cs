using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_Set_Tester_Present — 配置/关闭 UDS TesterPresent 保活。
    /// 默认 3E 80（抑制响应，只发不读）；可配置为 3E 00 等需响应的请求。
    ///
    /// 脚本示例:
    ///   DoIP_UDS_Set_Tester_Present("DoIP_ECU", 2000, "3E 80", 1)   // 开启
    ///   DoIP_UDS_Set_Tester_Present("DoIP_ECU", 0, "", 0)           // 关闭
    /// </summary>
    internal class DoIP_UDS_Set_Tester_Present : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_Set_Tester_Present(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string sessionKey = ArgString(0, "ECU/Session Key", "DoIP_ECU");
            int intervalMs = ArgInt(1, "Interval (ms)", 2000);
            string payloadHex = ArgString(2, "Payload Hex", "3E 80");
            bool enable = ArgInt(3, "Enable (0/1)", 1) != 0;

            byte[] payload = HexUtils.TryParseHex(payloadHex);
            if (payload == null || payload.Length == 0) payload = new byte[] { 0x3E, 0x80 };
            if (intervalMs < 250) intervalMs = 250;

            bool ok = UdsRequestExecutor.SetKeepAlive(sessionKey, enable, intervalMs, payload);

            SetReturn(0, ok ? 0 : 1);
            SetReturn(1, enable ? 1 : 0);
            SetReturn(2, intervalMs);
            UpdateStatusKey("DoIP_UDS_Set_Tester_Present: " + sessionKey + " " +
                         (enable ? "ON " + intervalMs + "ms " + HexUtils.ToHexString(payload) : "OFF"));

            // ZLG 风格：函数不设 Fail，状态放返回值（ReturnValue0，0=成功/1=会话未开），由脚本自行判断。
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
