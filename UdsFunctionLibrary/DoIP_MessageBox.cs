using System;
using JabilTest;
using ScriptVariableSpaceInterface;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_MessageBox — 弹窗等待（对齐 EonTest message 插件）。
    /// 模态弹窗阻塞等待操作员点击，返回点击的选项：
    ///   ReturnValue0: 0=确定(OK) / 1=取消(Cancel)
    ///   ReturnValue1: "OK" 或 "Cancel"（点击的选项）
    /// 是否判失败由调用方决定（ZLG 风格：本函数不设 Fail，状态放返回值）。
    ///
    /// 脚本示例:
    ///   DoIP_MessageBox("请插好治具后点确定", "线束检查", "okcancel")
    /// </summary>
    internal class DoIP_MessageBox : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_MessageBox(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string message = ArgString(0, "Message", "请点击确定继续");
            string title = ArgString(1, "Title", "JEMSts 提示");
            string buttons = ArgString(2, "Buttons", "ok");
            bool okCancel = buttons.IndexOf("okcancel", StringComparison.OrdinalIgnoreCase) >= 0
                         || buttons.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0;

            // 先清掉上一步残留的 RV，再发布本次点击选项
            ClearReturns();
            string clicked;
            bool ok = StepInterop.ShowMessageBox(message, title, okCancel, out clicked);

            SetReturn(0, ok ? 0 : 1);
            SetReturn(1, clicked);   // "OK" / "Cancel"
            UpdateStatusKey("DoIP_MessageBox: " + clicked + " - " + title + ": " + message);
            testResult.Status = TestStatus.Pass;   // ZLG 风格：成败放返回值，由脚本/ExcelRunAll 判定
            return testResult;
        }
    }
}
