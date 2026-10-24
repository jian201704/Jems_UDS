using System;
using System.Threading;
using JabilTest;
using ScriptVariableSpaceInterface;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_Delay — 延时等待步骤（对齐 EonTest delay 插件）。
    /// 只等待，不返回测量值（ReturnValue 仅 RV0=0 成功），适合"等待 XXX 毫秒"的工步。
    ///
    /// 脚本示例:
    ///   DoIP_Delay(3000)          // 等 3 秒
    ///   DoIP_Delay("delayMs=5000")  // 也可直接传 G 列片段
    /// </summary>
    internal class DoIP_Delay : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_Delay(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            int delayMs = ArgInt(0, "Delay (ms)", 1000);
            if (delayMs < 0) delayMs = 0;

            Thread.Sleep(delayMs);

            // delay 不返回测量值，仅成功状态；先清掉上一步残留的 RV
            ClearReturns();
            UpdateStatusKey("DoIP_Delay: " + delayMs + " ms");
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
