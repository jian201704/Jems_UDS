using System;
using System.Threading;
using System.Windows.Forms;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// 共享的 delay / 弹窗步骤执行逻辑（对齐 EonTest 的 delay / message 插件）。
    /// ExcelRunAll / ExcelStep / 独立 Test 函数共用，保证同一张表两个平台行为一致。
    /// </summary>
    internal static class StepInterop
    {
        /// <summary>按 动作指令/PluginId 识别 delay 行（对齐 EonTest inferPluginId）。</summary>
        public static bool IsDelayRow(ExcelStepRow row)
        {
            if (row == null) return false;
            if (row.PluginId.IndexOf("delay", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string a = row.Action ?? "";
            return a.IndexOf("delay", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("延时", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("等待", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>按 动作指令/PluginId 识别弹窗行（对齐 EonTest inferPluginId message）。</summary>
        public static bool IsMessageRow(ExcelStepRow row)
        {
            if (row == null) return false;
            if (row.PluginId.IndexOf("message", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string a = row.Action ?? "";
            return a.IndexOf("message", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("msg", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("popup", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("弹窗", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("提示", StringComparison.OrdinalIgnoreCase) >= 0
                || a.IndexOf("确认", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>delay 行：G 列 delayMs=...（默认 1000ms）。</summary>
        public static int DelayMsFromConfig(string config1, int fallbackMs)
        {
            var kv = ConfigPairParser.Parse(config1);
            string v;
            int ms;
            if (kv.TryGetValue("delayMs", out v) && int.TryParse(v, out ms))
                return Math.Max(0, ms);
            return fallbackMs;
        }

        /// <summary>弹窗（模态阻塞）：确定返回 true，取消返回 false；clicked 返回点击的选项文本。</summary>
        public static bool ShowMessageBox(string message, string title, bool okCancel, out string clicked)
        {
            MessageBoxButtons btns = okCancel ? MessageBoxButtons.OKCancel : MessageBoxButtons.OK;
            DialogResult r = MessageBox.Show(message, title, btns,
                MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
            clicked = (r == DialogResult.OK) ? "OK" : "Cancel";
            return r == DialogResult.OK;
        }
    }
}
