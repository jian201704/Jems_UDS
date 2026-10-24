using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using UdsCore;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// DoIP_UDS_Request — 在已建立（或自动建立）的会话上发一条 UDS 请求并取回响应。
    /// 返回值约定对齐现有 ZLG ZUDS_Request：
    ///   ($respData, $status, $type, $uds_str) = DoIP_UDS_Request("DoIP_ECU", "22 F1 05", "62 F1 05", "", 5000)
    ///   $status: 0=发送成功, 1=响应超时, 2=发送失败, 其他=响应异常
    ///   $type:   0=消极(NRC), 1=积极
    ///   $uds_str: "请求 -> 响应" 字符串
    /// </summary>
    internal class DoIP_UDS_Request : UdsTestBase
    {
        public override string Version => "1.00.00";

        public DoIP_UDS_Request(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string sessionKey = ArgString(0, "ECU/Session Key", "DoIP_ECU");
            string payloadHex = ArgString(1, "UDS Payload Hex", "");
            string expectedPrefix = ArgString(2, "Expected Prefix", "");
            string expectedResponse = ArgString(3, "Expected Response", "");
            int timeoutMs = ArgInt(4, "Timeout (ms)", 5000);

            byte[] payload = HexUtils.TryParseHex(payloadHex);
            if (payload == null || payload.Length == 0)
            {
                StepRunner.PublishError(this, "DoIP_UDS_Request", 2, "DoIP_UDS_Request: invalid UDS payload hex: '" + payloadHex + "'");
                return testResult;
            }

            // 如果会话尚未建立，需要一个 host。允许从会话本身已有信息推断；否则报错提示先 Init。
            var cfg = new UdsStepConfig
            {
                TimeoutMs = timeoutMs <= 0 ? 5000 : timeoutMs,
                MinRequestIntervalMs = 20,
                SessionKey = sessionKey
            };

            // 没有配置 host/address 时，若会话已存在则直接复用
            byte[] response;
            string error;
            bool ok;
            if (UdsRequestExecutor.SessionIsOpen(sessionKey))
            {
                ok = UdsRequestExecutor.SendOnSession(cfg, sessionKey, payload, out response, out error);
            }
            else
            {
                // 无法自动建立（缺 host），尝试用默认 172.16.0.8 以便直接使用；也可先 DoIP_UDS_Init
                cfg.Host = "172.16.0.8";
                cfg.Port = 13400;
                cfg.SourceAddress = 0x102D;
                cfg.TargetAddress = 0x1008;
                ok = UdsRequestExecutor.SendOnSession(cfg, sessionKey, payload, out response, out error);
            }

            string respHex = response == null ? "" : HexUtils.ToHexString(response);
            int status = 2;  // send fail default
            int type = 0;

            if (ok && response != null)
            {
                bool matched = true;
                if (!string.IsNullOrEmpty(expectedPrefix) || !string.IsNullOrEmpty(expectedResponse))
                {
                    byte[] prefix = HexUtils.TryParseHex(expectedPrefix);
                    byte[] exact = HexUtils.TryParseHex(expectedResponse);
                    matched = true;
                    if (prefix != null && prefix.Length > 0 && !StartsWith(response, prefix)) matched = false;
                    if (exact != null && exact.Length > 0 && !SequenceEqual(response, exact)) matched = false;
                }
                status = matched ? 0 : 4;   // 4 = 响应不匹配（响应异常）
                type = (response.Length >= 1 && response[0] == 0x7F) ? 0 : 1;
            }
            else
            {
                if (response != null && response.Length > 0 && response[0] == 0x7F)
                    status = 4; // 返回实际 UDS 否定响应，错误文本放在 ReturnValue4
                else if (error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                    status = 1;
            }

            SetReturn(0, respHex);                                     // $respData
            SetReturn(1, status);                                      // $status
            SetReturn(2, type);                                        // $type
            SetReturn(3, payloadHex + " -> " + respHex);               // $uds_str
            SetReturn(4, error);

            if (status == 0)
            {
                UpdateStatusKey("DoIP_UDS_Request: " + payloadHex + " -> " + respHex + " (positive)");
            }
            else
            {
                UpdateStatusKey("DoIP_UDS_Request: FAIL status=" + status + " " + error);
            }
            // ZLG 风格：函数不设 Fail，真实状态放 $status，由脚本自行判断，失败不终止。
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        private static bool SequenceEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (data[i] != prefix[i]) return false;
            return true;
        }
    }
}
