using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_Release — 关闭指定 ECU 的 DoIP 会话（断开 TCP）。
    ///
    /// 脚本示例:
    ///   DoIP_UDS_Release("DoIP_ECU")
    /// </summary>
    internal class DoIP_UDS_Release : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_Release(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string sessionKey = ArgString(0, "ECU/Session Key", "DoIP_ECU");
            bool ok = UdsRequestExecutor.ReleaseSession(sessionKey);

            SetReturn(0, ok ? 0 : 1);
            SetReturn(1, sessionKey);
            UpdateStatusKey("DoIP_UDS_Release: " + sessionKey + " " + (ok ? "released" : "not open"));

            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
