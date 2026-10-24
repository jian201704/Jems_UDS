using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ExcelDecodeCore;
using Jabil.Uds.Core;

namespace UdsCore
{
    /// <summary>
    /// Executes one UDS step from a typed config, mirroring EonTest's DoipMasterPlugin::executeStep.
    /// Owns the session lifecycle via DoipSessionPool.
    /// </summary>
    public static class UdsRequestExecutor
    {
        private static readonly object SessionConfigLock = new object();
        private static readonly Dictionary<string, UdsStepConfig> SessionConfigs =
            new Dictionary<string, UdsStepConfig>(StringComparer.OrdinalIgnoreCase);

        public static UdsStepResult ExecuteStep(UdsStepConfig cfg, IUdsTransport transport)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            if (transport == null) throw new ArgumentNullException("transport");
            if (cfg.VirtualMode) return ExecuteStep(cfg, cfg.SessionKey);

            var result = new UdsStepResult { Step = BuildStepResult(cfg) };
            List<string> commands = BuildCommands(cfg);
            foreach (string command in commands) result.CommandHex.Add(command);
            if (commands.Count == 0)
            {
                result.Error = "No UDS command specified; set udsPayload or udsSequence.";
                result.ResultText = "FAIL";
                SyncStepResult(result);
                return result;
            }

            try
            {
                transport.Connect();
                List<string> prefixes = SplitPipe(cfg.ExpectedPrefix);
                List<string> responses = SplitPipe(cfg.ExpectedResponse);
                List<string> suffixes = SplitPipe(cfg.ExpectedSuffix);
                List<string> offsets = SplitPipe(cfg.ExpectedByteOffset);
                List<string> values = SplitPipe(cfg.ExpectedByteValue);
                for (int index = 0; index < commands.Count; index++)
                {
                    string command = commands[index];
                    ParseInlineAnnotations(ref command, out int padSize, out int ignoredRetry, out int ignoredDelay);
                    byte[] payload = HexUtils.TryParseHex(command);
                    if (payload == null || payload.Length == 0) throw new InvalidOperationException("Invalid UDS command at index " + index + ".");
                    if (padSize > 0 && payload.Length < padSize) payload = PadRight(payload, padSize);
                    DiagnosticFrame request = new DiagnosticFrame(cfg.SourceAddress, cfg.TargetAddress, payload);
                    DiagnosticFrame response = transport.Request(request, cfg.TimeoutMs);
                    byte[] responsePayload = response == null ? new byte[0] : response.Payload;
                    result.ActualRequestHex.Add(HexUtils.ToHexString(payload));
                    result.ResponseHexList.Add(HexUtils.ToHexString(responsePayload));
                    string prefix = index < prefixes.Count ? prefixes[index] : "";
                    string expected = index < responses.Count ? responses[index] : "";
                    string suffix = index < suffixes.Count ? suffixes[index] : "";
                    string offset = index < offsets.Count ? offsets[index] : "";
                    string value = index < values.Count ? values[index] : "";
                    if (prefix.Length > 0 || expected.Length > 0 || suffix.Length > 0 || offset.Length > 0)
                    {
                        string matchError;
                        if (!MatchesExpectedResponse(responsePayload, expected, prefix, suffix, offset, value, out matchError))
                        {
                            result.Error = matchError;
                            result.CombinedResponse = responsePayload;
                            result.CombinedResponseHex = HexUtils.ToHexString(responsePayload);
                            result.ResultText = "FAIL";
                            SyncStepResult(result);
                            return result;
                        }
                    }
                }

                int responseIndex = cfg.HasExplicitDataFrom && cfg.DataFrom >= 0 && cfg.DataFrom < result.ResponseHexList.Count
                    ? cfg.DataFrom : result.ResponseHexList.Count - 1;
                result.CombinedResponseHex = result.ResponseHexList[responseIndex];
                result.CombinedResponse = HexUtils.TryParseHex(result.CombinedResponseHex) ?? new byte[0];
                DecodeResult decode = DecodeResultIfConfigured(cfg, result.CombinedResponse);
                result.Decode = decode;
                if (decode != null && decode.Measurements.Count > 0)
                {
                    DecodedMeasurement first = decode.Measurements[0];
                    result.FirstName = first.Name;
                    result.FirstRawValue = first.RawValue;
                    result.FirstValue = first.Value;
                    result.FirstUnit = first.Unit;
                }
                if (decode != null && !decode.Success)
                {
                    result.Error = decode.ErrorMessage;
                    result.ResultText = "FAIL";
                    SyncStepResult(result);
                    return result;
                }
                result.Success = true;
                result.ResultText = "PASS";
                SyncStepResult(result);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.ResultText = "FAIL";
                SyncStepResult(result);
                return result;
            }
            finally
            {
                try { transport.Dispose(); } catch (Exception) { }
            }
        }

        public static UdsStepResult ExecuteStep(UdsStepConfig cfg, string sessionKey)
        {
            var result = new UdsStepResult();
            result.Step = BuildStepResult(cfg);
            if (string.IsNullOrEmpty(sessionKey)) sessionKey = cfg.SessionKey;
            if (string.IsNullOrEmpty(sessionKey)) sessionKey = DefaultKey(cfg);
            ApplyCachedTransportConfig(cfg, sessionKey);
            sessionKey = AddLocalBindingToSessionKey(sessionKey, cfg);
            RememberTransportConfig(cfg, sessionKey);

            if (cfg.VirtualMode)
            {
                result.Success = true;
                result.ResultText = "PASS (virtual)";
                result.CombinedResponse = HexUtils.TryParseHex(cfg.SimResponse) ?? new byte[0];
                result.CombinedResponseHex = result.CombinedResponse.Length > 0
                    ? HexUtils.ToHexString(result.CombinedResponse) : cfg.SimResponse;
                result.ResponseHexList.Add(result.CombinedResponseHex);
                string virtualMatchError;
                if ((cfg.ExpectedResponse.Length > 0 || cfg.ExpectedPrefix.Length > 0 ||
                     cfg.ExpectedSuffix.Length > 0 || cfg.ExpectedByteOffset.Length > 0) &&
                    !MatchesExpectedResponse(result.CombinedResponse, cfg.ExpectedResponse,
                                              cfg.ExpectedPrefix, cfg.ExpectedSuffix,
                                              cfg.ExpectedByteOffset, cfg.ExpectedByteValue,
                                              out virtualMatchError))
                {
                    result.Success = false;
                    result.ResultText = "FAIL";
                    result.Error = virtualMatchError;
                    SyncStepResult(result);
                    return result;
                }
                if (cfg.HasDecodeConfig && result.CombinedResponse.Length > 0)
                {
                    List<DecodeSpec> specs = new List<DecodeSpec>(cfg.DecodeSpecs);
                    if (specs.Count == 0 && !string.IsNullOrEmpty(cfg.DecodeProfile))
                        specs = LoadDecodeProfile(cfg.DecodeProfile, cfg);
                    if (specs.Count > 0)
                        result.Decode = ResponseDecoder.DecodeMany(result.CombinedResponse, specs);
                    if (result.Decode != null && result.Decode.Measurements.Count > 0)
                    {
                        DecodedMeasurement first = result.Decode.Measurements[0];
                        result.FirstName = first.Name;
                        result.FirstRawValue = first.RawValue;
                        result.FirstValue = first.Value;
                        result.FirstUnit = first.Unit;
                    }
                    if (result.Decode != null && !result.Decode.Success)
                    {
                        result.Error = result.Decode.ErrorMessage;
                        result.ResultText = "FAIL";
                        result.Success = false;
                        return result;
                    }
                }
                SyncStepResult(result);
                return result;
            }

            if (cfg.ConnectionOnly)
            {
                DoipSession connectionSession = DoipSessionPool.Instance.Acquire(sessionKey);
                try
                {
                    string connectionError;
                    if (!EnsureSession(connectionSession, cfg, out connectionError))
                    {
                        result.Error = connectionError;
                        result.ResultText = "FAIL";
                        SyncStepResult(result);
                        return result;
                    }
                    lock (connectionSession.SyncRoot)
                    {
                        connectionSession.KeepAliveBeforeDiagnostic = cfg.KeepAliveEnabled;
                        connectionSession.StartKeepAlive();
                    }
                    result.Success = true;
                    result.ResultText = "PASS";
                    SyncStepResult(result);
                    return result;
                }
                finally
                {
                    // Keep the pooled session and its background Tester Present alive.
                }
            }

            // Build command list from udsSequence (pipe-separated) or single udsPayload.
            List<string> commands = BuildCommands(cfg);
            if (commands.Count == 0)
            {
                result.Error = "No UDS command specified; set udsPayload or udsSequence.";
                result.ResultText = "FAIL";
                return result;
            }
            foreach (string c in commands) result.CommandHex.Add(c);

            List<string> expectedPrefixes = SplitPipe(cfg.ExpectedPrefix);
            List<string> expectedResponses = SplitPipe(cfg.ExpectedResponse);
            List<string> expectedSuffixes = SplitPipe(cfg.ExpectedSuffix);
            List<string> expectedByteOffsets = SplitPipe(cfg.ExpectedByteOffset);
            List<string> expectedByteValues = SplitPipe(cfg.ExpectedByteValue);

            DoipSession session;
            bool perStep = cfg.IsPerStep;
            if (perStep)
            {
                session = new DoipSession { Key = sessionKey, SourceAddress = cfg.SourceAddress, TargetAddress = cfg.TargetAddress };
            }
            else
            {
                session = DoipSessionPool.Instance.Acquire(sessionKey);
            }

            try
            {
                if (!EnsureSession(session, cfg, out string sessError))
                {
                    result.Error = sessError;
                    result.ResultText = "FAIL";
                    if (perStep) CloseSession(session);
                    return result;
                }

                // 路由激活后的第一条诊断：等待 udsInitialDelayMs（默认 0），给网关/ECU 就绪时间；
                // 后续诊断按 minRequestIntervalMs 节流（与 EonTest 对齐）。
                if (!session.HasDiagnostic && cfg.UdsInitialDelayMs > 0)
                    Thread.Sleep(cfg.UdsInitialDelayMs);
                if (session.HasDiagnostic && cfg.MinRequestIntervalMs > 0)
                    Thread.Sleep(cfg.MinRequestIntervalMs);
                session.SendIdleKeepAlive();

                bool sequencePassed = false;
                bool responsePendingReceived = false;
                bool connectionLost = false;
                for (int seqRetry = 0;
                     seqRetry < cfg.SequenceRetryCount +
                         (responsePendingReceived ? cfg.ResponsePendingRetryCount : 0);
                     seqRetry++)
                {
                    if (seqRetry > 0) Thread.Sleep(cfg.SequenceRetryDelayMs);
                    result.ResponseHexList.Clear();
                    bool allPassed = true;
                    string firstError = "";

                    for (int seqIdx = 0; seqIdx < commands.Count; seqIdx++)
                    {
                        if (seqIdx > 0 && cfg.MinRequestIntervalMs > 0)
                            Thread.Sleep(cfg.MinRequestIntervalMs);

                        string cmdStr = commands[seqIdx];
                        ParseInlineAnnotations(ref cmdStr, out int padSize, out int cmdRetryCount, out int cmdRetryDelayMs);
                        byte[] seqUds = HexUtils.TryParseHex(cmdStr);
                        if (seqUds == null || seqUds.Length == 0)
                        {
                            firstError = "udsSequence[" + seqIdx + "] is empty.";
                            allPassed = false;
                            break;
                        }
                        if (padSize > 0 && seqUds.Length < padSize)
                            seqUds = PadRight(seqUds, padSize);

                        string thisPrefix = seqIdx < expectedPrefixes.Count ? expectedPrefixes[seqIdx] : "";
                        string thisResponse = seqIdx < expectedResponses.Count ? expectedResponses[seqIdx] : "";
                        string thisSuffix = seqIdx < expectedSuffixes.Count ? expectedSuffixes[seqIdx] : "";
                        string thisByteOffset = seqIdx < expectedByteOffsets.Count ? expectedByteOffsets[seqIdx] : "";
                        string thisByteValue = seqIdx < expectedByteValues.Count ? expectedByteValues[seqIdx] : "";
                        bool hasExpectedCheck = thisPrefix.Length > 0 || thisResponse.Length > 0 ||
                                                thisSuffix.Length > 0 || thisByteOffset.Length > 0;

                        bool cmdOk = false;
                        byte[] seqResponse = null;
                        bool pendingTimeout = false;
                        for (int cmdRetry = 0; cmdRetry <= cmdRetryCount; cmdRetry++)
                        {
                            if (cmdRetry > 0) Thread.Sleep(cmdRetryDelayMs);
                            bool seqSent = false;
                            string seqError = "";
                            for (int attempt = 0; attempt <= cfg.NackRetryCount; attempt++)
                            {
                                if (attempt > 0) Thread.Sleep(cfg.NackRetryDelayMs);
                                if (session.Client.SendDiagnosticMessage(
                                        cfg.SourceAddress, cfg.TargetAddress, seqUds,
                                    cfg.TimeoutMs, cfg.ResponsePendingTimeoutMs,
                                    out seqResponse, out seqError))
                                {
                                    seqSent = true;
                                    break;
                                }
                                if (seqError.IndexOf("0X08", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    attempt < cfg.NackRetryCount)
                                {
                                    // NACK 0x08 (too many active sockets)：重建会话并回放 session controls。
                                    // 网关释放旧 socket 需要时间：恢复失败时若还有重试余量，多等一拍再重建一次。
                                    if (!RecoverSession(session, cfg, out string recError))
                                    {
                                        // 把触发 0x08 的原指令一并带进错误，方便定位是哪条指令引起的
                                        seqError = "sending " + cmdStr + " got NACK 0x08; " + recError;
                                        if (attempt + 1 < cfg.NackRetryCount)
                                        {
                                            Thread.Sleep(Math.Max(500, cfg.NackRetryDelayMs * 2));
                                            continue;
                                        }
                                        break;
                                    }
                                    continue;
                                }
                                break;
                            }
                            if (!seqSent)
                            {
                                firstError = seqError;
                                connectionLost = !session.Client.IsConnected;
                                if (seqResponse != null && seqResponse.Length > 0)
                                {
                                    result.CombinedResponse = seqResponse;
                                    result.CombinedResponseHex = HexUtils.ToHexString(seqResponse);
                                }
                                if (seqError.IndexOf("RESPONSE_PENDING_TIMEOUT", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    pendingTimeout = true;
                                    responsePendingReceived = true;
                                    break;
                                }
                                if (connectionLost) break;
                                // 31 01 failure with retries left -> send 31 02 stop routine first
                                if (seqUds.Length >= 2 && seqUds[0] == 0x31 && seqUds[1] == 0x01 &&
                                    cmdRetry < cmdRetryCount && seqUds.Length >= 4)
                                {
                                    byte[] stopReq = { 0x31, 0x02, seqUds[2], seqUds[3] };
                                    byte[] stopRsp;
                                    string stopErr;
                                    session.Client.SendDiagnosticMessage(
                                        cfg.SourceAddress, cfg.TargetAddress, stopReq, 3000, 0,
                                        out stopRsp, out stopErr);
                                }
                                continue;
                            }

                            if (hasExpectedCheck)
                            {
                                if (!MatchesExpectedResponse(seqResponse, thisResponse, thisPrefix,
                                                              thisSuffix, thisByteOffset, thisByteValue,
                                                              out string matchErr))
                                {
                                    firstError = matchErr;
                                    // 保留实际收到的响应（即使不匹配）：供 RV3 与报告显示，
                                    // 否则用户只能看到“mismatch”却看不到实际收到了什么。
                                    if (seqResponse != null && seqResponse.Length > 0)
                                    {
                                        result.CombinedResponseHex = HexUtils.ToHexString(seqResponse);
                                        result.CombinedResponse = seqResponse;
                                    }
                                    if (seqResponse != null && seqResponse.Length >= 3 &&
                                        seqUds.Length >= 1 && seqResponse[0] == 0x7F &&
                                        seqResponse[1] == seqUds[0] && seqResponse[2] == 0x78)
                                    {
                                        responsePendingReceived = true;
                                    }
                                    continue;
                                }
                            }
                            cmdOk = true;
                            break;
                        }
                        if (pendingTimeout)
                        {
                            allPassed = false;
                            break;
                        }
                        if (!cmdOk)
                        {
                            allPassed = false;
                            break;
                        }

                        result.ActualRequestHex.Add(HexUtils.ToHexString(seqUds));
                        result.ResponseHexList.Add(HexUtils.ToHexString(seqResponse));
                        session.HasDiagnostic = true;
                        session.Touch();
                        if (seqUds.Length >= 1 && seqUds[0] == 0x10)
                        {
                            string key = HexUtils.ToHexString(seqUds);
                            if (!session.SessionControls.Exists(c => HexUtils.ToHexString(c) == key))
                                session.SessionControls.Add((byte[])seqUds.Clone());
                        }
                    }

                    if (allPassed) { sequencePassed = true; break; }
                    result.Error = firstError;
                    if (connectionLost) break;
                }

                if (!sequencePassed)
                {
                    // 序列未全部成功（部分或全部失败）：返回 FAIL。
                    // RV3 优先给“失败指令实际收到的报文”（mismatch 时已写入 CombinedResponseHex，
                    // 例如 NRC 7F 31 22，或内容不符的 71 03……），而不是前一条成功的 71 01；
                    // 仅当完全没收到报文（超时）时，才回退到最后一条成功响应。
                    if (string.IsNullOrEmpty(result.CombinedResponseHex) && result.ResponseHexList.Count > 0)
                    {
                        int fallbackIndex = cfg.HasExplicitDataFrom && cfg.DataFrom >= 0 && cfg.DataFrom < result.ResponseHexList.Count
                            ? cfg.DataFrom : result.ResponseHexList.Count - 1;
                        result.CombinedResponseHex = result.ResponseHexList[fallbackIndex];
                        result.CombinedResponse = HexUtils.TryParseHex(result.CombinedResponseHex) ?? new byte[0];
                    }
                    else if (!string.IsNullOrEmpty(result.CombinedResponseHex))
                    {
                        // 已保留失败指令的实际报文：同步字节
                        result.CombinedResponse = HexUtils.TryParseHex(result.CombinedResponseHex) ?? new byte[0];
                    }
                    else
                    {
                        result.CombinedResponse = new byte[0];
                    }

                    bool pendingResponse = result.CombinedResponse.Length >= 3 &&
                        result.CombinedResponse[0] == 0x7F && result.CombinedResponse[2] == 0x78;
                    // 完全没有响应：若序列含 31 01，尽力发 31 02 停止例程。
                    // 收到 0x78 时已经有真实响应，不执行任何清理请求。
                    if (result.ResponseHexList.Count == 0 && !pendingResponse && session.Client.IsConnected)
                    {
                        foreach (string cmd in commands)
                        {
                            byte[] cmdBytes = HexUtils.TryParseHex(cmd);
                            if (cmdBytes != null && cmdBytes.Length >= 4 && cmdBytes[0] == 0x31 && cmdBytes[1] == 0x01)
                            {
                                byte[] stopReq = { 0x31, 0x02, cmdBytes[2], cmdBytes[3] };
                                byte[] stopRsp;
                                string stopErr;
                                session.Client.SendDiagnosticMessage(
                                    cfg.SourceAddress, cfg.TargetAddress, stopReq, 5000, 0,
                                    out stopRsp, out stopErr);
                                break;
                            }
                        }
                    }
                    result.Trace = session.Client.LastTrace;
                    if (perStep && !pendingResponse) CloseSession(session);
                    result.ResultText = "FAIL";
                    return result;
                }

                // Success path
                if (result.ResponseHexList.Count > 0)
                {
                    int responseIndex = cfg.HasExplicitDataFrom && cfg.DataFrom >= 0 && cfg.DataFrom < result.ResponseHexList.Count
                        ? cfg.DataFrom : result.ResponseHexList.Count - 1;
                    result.CombinedResponseHex = result.ResponseHexList[responseIndex];
                }
                else
                    result.CombinedResponseHex = "";
                result.CombinedResponse = HexUtils.TryParseHex(result.CombinedResponseHex) ?? new byte[0];

                if (cfg.HasDecodeConfig && result.CombinedResponse.Length > 0)
                {
                    List<DecodeSpec> specs = new List<DecodeSpec>(cfg.DecodeSpecs);
                    if (specs.Count == 0 && !string.IsNullOrEmpty(cfg.DecodeProfile))
                        specs = LoadDecodeProfile(cfg.DecodeProfile, cfg);
                    if (specs.Count > 0)
                        result.Decode = ResponseDecoder.DecodeMany(result.CombinedResponse, specs);
                    if (result.Decode != null && result.Decode.Measurements.Count > 0)
                    {
                        DecodedMeasurement first = result.Decode.Measurements[0];
                        result.FirstName = first.Name;
                        result.FirstRawValue = first.RawValue;
                        result.FirstValue = first.Value;
                        result.FirstUnit = first.Unit;
                    }
                    if (result.Decode != null && !result.Decode.Success)
                    {
                        result.Error = result.Decode.ErrorMessage;
                        result.ResultText = "FAIL";
                        result.Success = false;
                        if (perStep || cfg.CloseConnection) CloseSession(session);
                        return result;
                    }
                }

                result.ResultText = "PASS";
                result.Success = true;
                if (perStep || cfg.CloseConnection) CloseSession(session);
                return result;
            }
            finally
            {
                SyncStepResult(result);
                if (perStep) CloseSession(session);
            }
        }

        private static void SyncStepResult(UdsStepResult result)
        {
            if (result == null || result.Step == null) return;
            result.Step.Request = result.ActualRequestHex.Count > 0
                ? string.Join(" | ", result.ActualRequestHex)
                : string.Join(" | ", result.CommandHex);
            result.Step.Response = !string.IsNullOrEmpty(result.CombinedResponseHex)
                ? result.CombinedResponseHex
                : string.Join(" | ", result.ResponseHexList);
            result.Step.Value = result.FirstValue == null ? "" : Convert.ToString(result.FirstValue, System.Globalization.CultureInfo.InvariantCulture);
            result.Step.TestType = result.Success ? "PASS" : "FAIL";
        }

        private static DecodeResult DecodeResultIfConfigured(UdsStepConfig cfg, byte[] response)
        {
            if (!cfg.HasDecodeConfig || response == null || response.Length == 0) return null;
            List<DecodeSpec> specs = new List<DecodeSpec>(cfg.DecodeSpecs);
            if (specs.Count == 0 && !string.IsNullOrEmpty(cfg.DecodeProfile))
                specs = LoadDecodeProfile(cfg.DecodeProfile, cfg);
            return specs.Count == 0 ? null : ResponseDecoder.DecodeMany(response, specs);
        }

        private static StepResult BuildStepResult(UdsStepConfig cfg)
        {
            var step = new StepResult();
            if (cfg == null) return step;
            step.TestItem = cfg.TestItem;
            step.Request = cfg.UdsSequence.Length > 0 ? cfg.UdsSequence : cfg.UdsPayload;
            step.Unit = cfg.Unit;
            step.Limits = cfg.LowerLimit + ".." + cfg.UpperLimit;
            step.ExpectedContext.ExpectedPrefix = cfg.ExpectedPrefix;
            step.ExpectedContext.ExpectedResponse = cfg.ExpectedResponse;
            step.ExpectedContext.ExpectedSuffix = cfg.ExpectedSuffix;
            step.ExpectedContext.ExpectedByteOffset = cfg.ExpectedByteOffset;
            step.ExpectedContext.ExpectedByteValue = cfg.ExpectedByteValue;
            step.ExpectedContext.DecodeProfile = cfg.DecodeProfile;
            if (cfg.DecodeSpecs.Count > 0)
            {
                DecodeSpec spec = cfg.DecodeSpecs[0];
                step.ExpectedContext.ValueOffset = spec.Offset;
                step.ExpectedContext.ValueLength = spec.Length;
                step.ExpectedContext.ValueType = spec.Type;
                step.ExpectedContext.Endian = spec.Endian;
                step.ExpectedContext.Formula = spec.Formula;
            }
            return step;
        }

        public static bool HasCachedTransportConfig(string sessionKey)
        {
            if (string.IsNullOrEmpty(sessionKey)) return false;
            lock (SessionConfigLock)
            {
                foreach (string key in SessionConfigs.Keys)
                    if (IsSessionKeyMatch(key, sessionKey)) return true;
                return false;
            }
        }

        private static void ApplyCachedTransportConfig(UdsStepConfig cfg, string sessionKey)
        {
            if (cfg == null || !string.IsNullOrEmpty(cfg.Host) || string.IsNullOrEmpty(sessionKey)) return;
            UdsStepConfig cached;
            string qualifiedKey = AddLocalBindingToSessionKey(sessionKey, cfg);
            lock (SessionConfigLock)
            {
                if (!SessionConfigs.TryGetValue(qualifiedKey, out cached) &&
                    !SessionConfigs.TryGetValue(sessionKey, out cached))
                {
                    cached = null;
                    foreach (KeyValuePair<string, UdsStepConfig> entry in SessionConfigs)
                    {
                        if (!IsSessionKeyMatch(entry.Key, sessionKey)) continue;
                        if (cached != null) return;
                        cached = entry.Value;
                    }
                    if (cached == null) return;
                }
            }

            cfg.Host = cached.Host;
            cfg.Port = cached.Port;
            cfg.SourceAddress = cached.SourceAddress;
            cfg.TargetAddress = cached.TargetAddress;
            cfg.ActivationType = cached.ActivationType;
            cfg.TimeoutMs = cached.TimeoutMs;
            cfg.ResponsePendingTimeoutMs = cached.ResponsePendingTimeoutMs;
            cfg.TcpKeepAlive = cached.TcpKeepAlive;
            if (string.IsNullOrWhiteSpace(cfg.LocalBindAddress))
                cfg.LocalBindAddress = cached.LocalBindAddress;
            if (cfg.LocalBindPort == 0) cfg.LocalBindPort = cached.LocalBindPort;
            cfg.KeepAliveEnabled = cached.KeepAliveEnabled;
            cfg.KeepAliveIntervalMs = cached.KeepAliveIntervalMs;
            cfg.KeepAlivePayload = cached.KeepAlivePayload;
            cfg.IdleTimeoutMs = cached.IdleTimeoutMs;
        }

        private static void RememberTransportConfig(UdsStepConfig cfg, string sessionKey)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.Host) || string.IsNullOrEmpty(sessionKey)) return;
            lock (SessionConfigLock)
                SessionConfigs[sessionKey] = cfg;
        }

        // ---- low-level session API (used by manual Init/Request/Release functions) ----

        /// <summary>Open (or reuse) a pooled session for the given key: connect TCP + activate routing.</summary>
        public static bool OpenSession(UdsStepConfig cfg, string sessionKey, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(sessionKey)) sessionKey = DefaultKey(cfg);
            sessionKey = AddLocalBindingToSessionKey(sessionKey, cfg);
            DoipSession session = DoipSessionPool.Instance.Acquire(sessionKey);
            return EnsureSession(session, cfg, out error);
        }

        /// <summary>
        /// Send one raw UDS request on the pooled session (no expected-matching, no decode).
        /// Creates the session on first use.
        /// </summary>
        public static bool SendOnSession(UdsStepConfig cfg, string sessionKey, byte[] payload,
                                         out byte[] response, out string error)
        {
            response = null;
            error = "";
            if (string.IsNullOrEmpty(sessionKey)) sessionKey = DefaultKey(cfg);
            sessionKey = AddLocalBindingToSessionKey(sessionKey, cfg);
            if (cfg.VirtualMode)
            {
                response = HexUtils.TryParseHex(cfg.SimResponse) ?? new byte[0];
                return true;
            }
            DoipSession session;
            if (!DoipSessionPool.Instance.TryGet(sessionKey, out session))
            {
                if (!OpenSession(cfg, sessionKey, out error)) return false;
                DoipSessionPool.Instance.TryGet(sessionKey, out session);
            }
            if (session == null) { error = "session not available"; return false; }
            if (session.HasDiagnostic && cfg.MinRequestIntervalMs > 0)
                Thread.Sleep(cfg.MinRequestIntervalMs);
            if (!session.Client.SendDiagnosticMessage(cfg.SourceAddress, cfg.TargetAddress, payload,
                    cfg.TimeoutMs, cfg.ResponsePendingTimeoutMs, out response, out error))
                return false;
            session.HasDiagnostic = true;
            session.Touch();
            return true;
        }

        public static bool ReleaseSession(string sessionKey)
        {
            if (string.IsNullOrEmpty(sessionKey)) return false;
            DoipSessionPool.Instance.Release(sessionKey);
            return true;
        }

        public static bool SetKeepAlive(string sessionKey, bool enabled, int intervalMs, byte[] payload)
        {
            List<DoipSession> sessions = DoipSessionPool.Instance.GetByBaseKey(sessionKey);
            if (sessions.Count == 0) return false;
            foreach (DoipSession session in sessions)
            {
                session.KeepAliveEnabled = enabled;
                if (intervalMs > 0) session.KeepAliveIntervalMs = intervalMs;
                if (payload != null && payload.Length > 0)
                {
                    session.KeepAlivePayload = payload;
                    session.KeepAlivePayloads = new List<byte[]> { payload };
                }
                session.StartKeepAlive();
            }
            return true;
        }

        public static bool SessionIsOpen(string sessionKey)
        {
            return !string.IsNullOrEmpty(sessionKey) &&
                   DoipSessionPool.Instance.GetByBaseKey(sessionKey).Count > 0;
        }

        public static void CloseAllSessions()
        {
            DoipSessionPool.Instance.CloseAll();
        }

        // ---- session management ----

        private static bool EnsureSession(DoipSession session, UdsStepConfig cfg, out string error)
        {
            lock (session.SyncRoot)
                return EnsureSessionLocked(session, cfg, out error);
        }

        private static bool EnsureSessionLocked(DoipSession session, UdsStepConfig cfg, out string error)
        {
            error = "";
            if (!cfg.ConnectionOnly) session.KeepAliveBeforeDiagnostic = false;
            if (session.Client == null || !session.Client.IsConnected) session.Client = new DoipClient();
            if (!session.Client.IsConnected)
            {
                int activationRetry = 0;
                while (true)
                {
                    if (!session.Client.Connect(cfg.Host, cfg.Port, cfg.TimeoutMs,
                        cfg.LocalBindAddress, cfg.LocalBindPort, cfg.TcpKeepAlive))
                    {
                        error = "DoIP connect failed: " + session.Client.LastError;
                        return false;
                    }
                    session.SourceAddress = cfg.SourceAddress;
                    session.TargetAddress = cfg.TargetAddress;
                    if (session.Client.ActivateRouting(cfg.SourceAddress, cfg.ActivationType, cfg.TimeoutMs, out error))
                        break;

                    int responseCode = session.Client.LastRoutingActivationResponseCode;
                    string activationError = error;
                    CloseSession(session);
                    if (responseCode != 0x03 || activationRetry >= cfg.RoutingActivationRetryCount)
                    {
                        error = activationError;
                        return false;
                    }

                    activationRetry++;
                    if (cfg.RoutingActivationRetryDelayMs > 0)
                        Thread.Sleep(cfg.RoutingActivationRetryDelayMs);
                    session.Client = new DoipClient();
                }
                session.RoutingActivated = true;
                session.KeepAliveEnabled = cfg.KeepAliveEnabled;
                session.KeepAliveIntervalMs = cfg.KeepAliveIntervalMs;
                session.KeepAlivePayloads = ParseKeepAlivePayloads(cfg.KeepAlivePayload);
                session.KeepAlivePayload = session.KeepAlivePayloads.Count > 0
                    ? session.KeepAlivePayloads[0]
                    : new byte[] { 0x3E, 0x80 };
                session.KeepAliveTimeoutMs = Math.Min(cfg.TimeoutMs, 1000);
                session.HasDiagnostic = false;
                // 后台周期 keep-alive：路由激活 + 首次诊断后自动周期性发送（无需等下一个步骤）
                session.StartKeepAlive();
            }
            else
            {
                // addresses changed -> recreate
                if (session.SourceAddress != cfg.SourceAddress || session.TargetAddress != cfg.TargetAddress)
                {
                    CloseSession(session);
                    return EnsureSession(session, cfg, out error);
                }
                // idle timeout -> close and recreate
                long idle = Math.Max(0, Environment.TickCount - session.LastActivityTick);
                if (cfg.IdleTimeoutMs > 0 && idle > cfg.IdleTimeoutMs)
                {
                    CloseSession(session);
                    return EnsureSession(session, cfg, out error);
                }
            }
            session.Touch();
            return true;
        }

        private static bool RecoverSession(DoipSession session, UdsStepConfig cfg, out string error)
        {
            error = "";
            bool recovered = false;
            try
            {
                CloseSession(session);
                session.Client = new DoipClient();
                if (!session.Client.Connect(cfg.Host, cfg.Port, cfg.TimeoutMs,
                    cfg.LocalBindAddress, cfg.LocalBindPort, cfg.TcpKeepAlive))
                {
                    error = "NACK 0x08 recovery: reconnect failed: " + session.Client.LastError;
                    return false;
                }
                if (!session.Client.ActivateRouting(cfg.SourceAddress, cfg.ActivationType, cfg.TimeoutMs, out error))
                {
                    error = "NACK 0x08 recovery: reconnect OK, routing activation failed: " + error;
                    return false;
                }
                session.RoutingActivated = true;
                session.HasDiagnostic = false;
                // NACK 0x08 = ISO 13400 "too many active sockets"：旧的 DoIP 诊断 socket
                // 可能还没被网关/ECU 释放。重连+路由激活成功后，先等一小段时间再回放
                // 会话控制（10 01 等），给网关清理旧 socket 的时间，避免回放立刻又 0x08。
                int settleMs = Math.Max(300, cfg.NackRetryDelayMs);
                if (settleMs > 0) Thread.Sleep(settleMs);
                foreach (byte[] control in session.SessionControls)
                {
                    byte[] rsp;
                    string e;
                        if (!session.Client.SendDiagnosticMessage(cfg.SourceAddress, cfg.TargetAddress, control,
                            cfg.TimeoutMs, cfg.ResponsePendingTimeoutMs, out rsp, out e))
                    {
                        // 重建 TCP + 路由激活成功，但回放会话控制仍被拒绝：
                        // 常见原因是 ECU/网关仍被占用（旧连接未释放）或并发 socket 冲突。
                        error = "NACK 0x08 recovery: reconnected+routing OK, but replaying session control " +
                                HexUtils.ToHexString(control) + " failed: " + e +
                                " (ECU/gateway may still be busy or old DoIP socket not released; " +
                                "close other DoIP clients, release the session, and retry)";
                        return false;
                    }
                    session.HasDiagnostic = true;
                    session.Touch();
                }
                session.Touch();
                session.StartKeepAlive();
                recovered = true;
                return true;
            }
            catch (Exception ex)
            {
                error = "NACK 0x08 recovery failed: " + ex.Message;
                return false;
            }
            finally
            {
                if (!recovered) CloseSession(session);
            }
        }

        private static void CloseSession(DoipSession session)
        {
            try
            {
                lock (session.SyncRoot) session.Dispose();
            }
            catch (Exception) { }
        }

        // ---- command building ----

        /// <summary>Parse keep-alive payload list: '|'-separated entries, each UDS bytes or a raw DoIP frame.</summary>
        private static List<byte[]> ParseKeepAlivePayloads(string text)
        {
            var list = new List<byte[]>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (string part in text.Split('|'))
            {
                byte[] data = HexUtils.TryParseHex(part.Trim());
                if (data != null && data.Length > 0) list.Add(data);
            }
            return list;
        }

        private static List<string> BuildCommands(UdsStepConfig cfg)
        {
            var commands = new List<string>();
            if (!string.IsNullOrEmpty(cfg.UdsSequence))
            {
                foreach (string part in cfg.UdsSequence.Split('|'))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length == 0) continue;
                    byte[] data = HexUtils.TryParseHex(trimmed);
                    commands.Add(data != null && data.Length > 0 ? HexUtils.ToHexString(data) : trimmed);
                }
            }
            else if (!string.IsNullOrEmpty(cfg.UdsPayload))
            {
                commands.Add(cfg.UdsPayload.Trim());
            }
            return commands;
        }

        private static void ParseInlineAnnotations(ref string cmdStr, out int padSize, out int cmdRetryCount, out int cmdRetryDelayMs)
        {
            padSize = 0;
            cmdRetryCount = 0;
            cmdRetryDelayMs = 500;
            int searchPos = 0;
            while (true)
            {
                int braceStart = cmdStr.IndexOf('{', searchPos);
                if (braceStart < 0) break;
                int braceEnd = cmdStr.IndexOf('}', braceStart);
                if (braceEnd < 0) break;
                string inner = cmdStr.Substring(braceStart + 1, braceEnd - braceStart - 1);
                foreach (string rawPart in inner.Split(','))
                {
                    string part = rawPart.Trim();
                    if (part.Length == 0) continue;
                    int sep = part.IndexOf('=');
                    if (sep < 0) sep = part.IndexOf(':');
                    string key = sep < 0 ? part : part.Substring(0, sep).Trim();
                    string val = sep < 0 ? "" : part.Substring(sep + 1).Trim();
                    int v;
                    if (key.Equals("pad", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, out v) && v > 0) padSize = v;
                    }
                    else if (key.Equals("retry", StringComparison.OrdinalIgnoreCase) ||
                             key.Equals("count", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, out v) && v > 0) cmdRetryCount = Math.Min(v, 200);
                    }
                    else if (key.Equals("delay", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, out v) && v >= 0) cmdRetryDelayMs = v;
                    }
                }
                cmdStr = cmdStr.Substring(0, braceStart) + cmdStr.Substring(braceEnd + 1);
                searchPos = braceStart;
            }
        }

        private static bool MatchesExpectedResponse(byte[] response, string expectedResponse, string expectedPrefix,
                                string expectedSuffix, string expectedByteOffset,
                                string expectedByteValue, out string errorMessage)
        {
            errorMessage = "";
            byte[] exact = HexUtils.TryParseHex(expectedResponse);
            byte[] prefix = HexUtils.TryParseHex(expectedPrefix);
            if (exact != null && exact.Length > 0 && !SequenceEqual(response, exact))
            {
                errorMessage = "UDS response mismatch: expected [" + HexUtils.ToHexString(exact) +
                               "], actual [" + HexUtils.ToHexString(response) + "].";
                return false;
            }
            if (prefix != null && prefix.Length > 0 && !StartsWith(response, prefix))
            {
                errorMessage = "UDS response prefix mismatch: expected [" + HexUtils.ToHexString(prefix) +
                               "], actual [" + HexUtils.ToHexString(response) + "].";
                return false;
            }
            byte[] suffix = HexUtils.TryParseHex(expectedSuffix);
            if (suffix != null && suffix.Length > 0 && !EndsWith(response, suffix))
            {
                errorMessage = "UDS response suffix mismatch: expected [" +
                                HexUtils.ToHexString(suffix) + "], actual [" +
                                HexUtils.ToHexString(response) + "].";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(expectedByteOffset))
            {
                if (!int.TryParse(expectedByteOffset, out int offset) || offset < 0 || offset >= response.Length)
                {
                    errorMessage = "UDS response byte offset invalid: " + expectedByteOffset;
                    return false;
                }
                byte[] expectedByte = HexUtils.TryParseHex(expectedByteValue);
                if (expectedByte == null || expectedByte.Length != 1 || response[offset] != expectedByte[0])
                {
                    errorMessage = "UDS response byte mismatch at offset " + offset + ".";
                    return false;
                }
            }
            return true;
        }

        private static bool EndsWith(byte[] data, byte[] suffix)
        {
            if (data.Length < suffix.Length) return false;
            int start = data.Length - suffix.Length;
            for (int i = 0; i < suffix.Length; i++) if (data[start + i] != suffix[i]) return false;
            return true;
        }

        // ---- decode profile ----

        /// <summary>
        /// Load a decode profile JSON file referenced by <paramref name="name"/>.
        /// Search order: as-given path; cfg.BaseDirectory; cfg.BaseDirectory/DecodeProfiles; CWD.
        /// </summary>
        public static List<DecodeSpec> LoadDecodeProfile(string name, UdsStepConfig cfg)
        {
            string path = ResolveProfilePath(name, cfg);
            if (path == null) return new List<DecodeSpec>();
            try
            {
                string text = File.ReadAllText(path);
                object json;
                if (!MiniJson.TryParse(text, out json)) return new List<DecodeSpec>();

                var arr = json as List<object>;
                if (arr != null)
                {
                    var specs = new List<DecodeSpec>();
                    foreach (object item in arr)
                    {
                        var node = item as Dictionary<string, object>;
                        if (node != null) specs.Add(DecodeSpec.FromJson(node));
                    }
                    return specs;
                }

                var obj = json as Dictionary<string, object>;
                if (obj != null && obj.TryGetValue("measurements", out object measurements))
                {
                    var measureList = measurements as List<object>;
                    if (measureList != null)
                    {
                        var specs = new List<DecodeSpec>();
                        foreach (object item in measureList)
                        {
                            var node = item as Dictionary<string, object>;
                            if (node != null) specs.Add(DecodeSpec.FromJson(node));
                        }
                        return specs;
                    }
                }
            }
            catch (Exception)
            {
                // fall through
            }
            return new List<DecodeSpec>();
        }

        private static string ResolveProfilePath(string name, UdsStepConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var candidates = new List<string>();
            candidates.Add(name);
            if (!string.IsNullOrEmpty(cfg.BaseDirectory))
            {
                candidates.Add(Path.Combine(cfg.BaseDirectory, name));
                candidates.Add(Path.Combine(cfg.BaseDirectory, "DecodeProfiles", name));
            }
            candidates.Add(Path.Combine(Environment.CurrentDirectory, "DecodeProfiles", name));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, name));
            foreach (string candidate in candidates)
            {
                try { if (File.Exists(candidate)) return candidate; } catch (Exception) { }
            }
            return null;
        }

        // ---- helpers ----

        private static List<string> SplitPipe(string value)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return list;
            foreach (string part in value.Split('|'))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) list.Add(trimmed);
            }
            return list;
        }

        private static byte[] PadRight(byte[] data, int length)
        {
            if (data.Length >= length) return data;
            byte[] result = new byte[length];
            Array.Copy(data, result, data.Length);
            return result;
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

        private static string DefaultKey(UdsStepConfig cfg)
        {
            return cfg.Host + ":" + cfg.Port + ":" + cfg.SourceAddress.ToString("X4") + ":" + cfg.TargetAddress.ToString("X4");
        }

        private static string AddLocalBindingToSessionKey(string sessionKey, UdsStepConfig cfg)
        {
            if (cfg == null) return sessionKey;
            string address = (cfg.LocalBindAddress ?? "").Trim();
            if (address.Length == 0 && cfg.LocalBindPort == 0) return sessionKey;
            string suffix = "|local=" + address.ToLowerInvariant() + ":" + cfg.LocalBindPort;
            return sessionKey.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? sessionKey
                : sessionKey + suffix;
        }

        private static bool IsSessionKeyMatch(string key, string baseKey)
        {
            return string.Equals(key, baseKey, StringComparison.OrdinalIgnoreCase) ||
                   key.StartsWith(baseKey + "|local=", StringComparison.OrdinalIgnoreCase);
        }
    }
}
