using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ExcelDecodeCore;
using Jabil.Uds.Core;
using UdsCore;

namespace UdsEngine
{
    /// <summary>Transport-neutral execution of one UDS transaction.</summary>
    public static class UdsTransactionExecutor
    {
        public static UdsStepResult Execute(UdsStepConfig cfg, IUdsTransport transport)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            if (transport == null) throw new ArgumentNullException("transport");
            if (cfg.VirtualMode) return ExecuteOnce(cfg, transport);

            int totalRetries = Math.Max(Math.Max(0, cfg.SequenceRetryCount - 1), cfg.DecodeRetryCount);
            int originalSequenceRetryCount = cfg.SequenceRetryCount;
            UdsStepResult result = null;
            try
            {
                // ExecuteOnce performs one sequence attempt. Prevent its per-command
                // retry loop from consuming sequenceRetryCount.
                cfg.SequenceRetryCount = 1;
                for (int attempt = 0; attempt <= totalRetries; attempt++)
                {
                    result = ExecuteOnce(cfg, transport);
                    if (result.Success || attempt >= totalRetries) return result;

                    bool decodeFailed = result.Decode != null && !result.Decode.Success;
                    int delay = decodeFailed ? cfg.DecodeRetryDelayMs : cfg.SequenceRetryDelayMs;
                    if (delay > 0) Thread.Sleep(delay);
                }
                return result;
            }
            finally
            {
                cfg.SequenceRetryCount = originalSequenceRetryCount;
            }
        }

        private static UdsStepResult ExecuteOnce(UdsStepConfig cfg, IUdsTransport transport)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            if (transport == null) throw new ArgumentNullException("transport");
            UdsStepResult result = new UdsStepResult { Step = BuildStepResult(cfg) };
            if (cfg.VirtualMode)
            {
                result.CombinedResponse = HexUtils.TryParseHex(cfg.SimResponse) ?? new byte[0];
                result.CombinedResponseHex = HexUtils.ToHexString(result.CombinedResponse);
                result.ResponseHexList.Add(result.CombinedResponseHex);
                string virtualError;
                result.Success = MatchesExpectedResponse(result.CombinedResponse,
                    cfg.ExpectedResponse, cfg.ExpectedPrefix, cfg.ExpectedSuffix,
                    cfg.ExpectedByteOffset, cfg.ExpectedByteValue, out virtualError);
                result.ResultText = result.Success ? "PASS (virtual)" : "FAIL";
                if (!result.Success) result.Error = virtualError;
                result.Decode = DecodeResultIfConfigured(cfg, result.CombinedResponse);
                SetFirstMeasurement(result);
                if (result.Decode != null && !result.Decode.Success)
                {
                    result.Success = false;
                    result.ResultText = "FAIL";
                    result.Error = result.Decode.ErrorMessage;
                }
                SyncStepResult(result);
                return result;
            }
            List<string> commands = BuildCommands(cfg);
            foreach (string command in commands) result.CommandHex.Add(command);
            if (commands.Count == 0)
            {
                result.Error = "No UDS command specified; set udsPayload or udsSequence.";
                result.ResultText = "FAIL";
                return result;
            }
            try
            {
                transport.Connect();
                if (cfg.UdsInitialDelayMs > 0) Thread.Sleep(cfg.UdsInitialDelayMs);
                List<string> prefixes = SplitPipe(cfg.ExpectedPrefix);
                List<string> responses = SplitPipe(cfg.ExpectedResponse);
                List<string> suffixes = SplitPipe(cfg.ExpectedSuffix);
                List<string> offsets = SplitPipe(cfg.ExpectedByteOffset);
                List<string> values = SplitPipe(cfg.ExpectedByteValue);
                for (int index = 0; index < commands.Count; index++)
                {
                    string command = commands[index];
                    int padSize, retryCount, retryDelay;
                    ParseInlineAnnotations(ref command, out padSize, out retryCount, out retryDelay);
                    byte[] payload = HexUtils.TryParseHex(command);
                    if (payload == null || payload.Length == 0) throw new InvalidOperationException("Invalid UDS command at index " + index + ".");
                    if (padSize > 0 && payload.Length < padSize) payload = PadRight(payload, padSize);
                    string prefix = index < prefixes.Count ? prefixes[index] : "";
                    string expected = index < responses.Count ? responses[index] : "";
                    string suffix = index < suffixes.Count ? suffixes[index] : "";
                    string offset = index < offsets.Count ? offsets[index] : "";
                    string value = index < values.Count ? values[index] : "";
                    bool matched = false;
                    string lastError = "";
                    byte[] lastResponsePayload = new byte[0];
                    for (int attempt = 0; attempt <= retryCount; attempt++)
                    {
                        try
                        {
                            if (cfg.MinRequestIntervalMs > 0 &&
                                (attempt > 0 || index > 0))
                                Thread.Sleep(cfg.MinRequestIntervalMs);
                            var requestFrame = new DiagnosticFrame(cfg.SourceAddress, cfg.TargetAddress, payload);
                            var pendingTransport = transport as IUdsResponsePendingTransport;
                            DiagnosticFrame response = pendingTransport != null
                                ? pendingTransport.Request(requestFrame, cfg.TimeoutMs, cfg.ResponsePendingTimeoutMs)
                                : transport.Request(requestFrame, Math.Max(cfg.TimeoutMs, cfg.ResponsePendingTimeoutMs));
                            byte[] responsePayload = response == null ? new byte[0] : response.Payload;
                            lastResponsePayload = responsePayload;
                            result.CombinedResponse = responsePayload;
                            result.CombinedResponseHex = HexUtils.ToHexString(responsePayload);
                            if (prefix.Length == 0 && expected.Length == 0 && suffix.Length == 0 && offset.Length == 0)
                            {
                                matched = true;
                            }
                            else
                            {
                                matched = MatchesExpectedResponse(responsePayload, expected, prefix, suffix, offset, value, out lastError);
                            }
                            if (matched) break;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex.Message;
                        }
                        if (attempt < retryCount && retryDelay > 0) Thread.Sleep(retryDelay);
                    }
                    result.ActualRequestHex.Add(HexUtils.ToHexString(payload));
                    result.ResponseHexList.Add(HexUtils.ToHexString(lastResponsePayload));
                    if (!matched)
                    {
                        result.Error = lastError.Length == 0 ? "UDS response mismatch." : lastError;
                        result.ResultText = "FAIL";
                        return result;
                    }
                }
                int responseIndex = cfg.HasExplicitDataFrom && cfg.DataFrom >= 0 && cfg.DataFrom < result.ResponseHexList.Count ? cfg.DataFrom : result.ResponseHexList.Count - 1;
                result.CombinedResponseHex = result.ResponseHexList[responseIndex];
                result.CombinedResponse = HexUtils.TryParseHex(result.CombinedResponseHex) ?? new byte[0];
                result.Decode = DecodeResultIfConfigured(cfg, result.CombinedResponse);
                SetFirstMeasurement(result);
                if (result.Decode != null && !result.Decode.Success)
                {
                    result.Error = result.Decode.ErrorMessage;
                    result.ResultText = "FAIL";
                    return result;
                }
                result.Success = true;
                result.ResultText = "PASS";
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.ResultText = "FAIL";
                return result;
            }
            finally
            {
                SyncStepResult(result);
            }
        }

        private static DecodeResult DecodeResultIfConfigured(UdsStepConfig cfg, byte[] response)
        {
            if (!cfg.HasDecodeConfig || response == null || response.Length == 0) return null;
            List<DecodeSpec> specs = new List<DecodeSpec>(cfg.DecodeSpecs);
            if (specs.Count == 0 && !string.IsNullOrEmpty(cfg.DecodeProfile)) specs = LoadDecodeProfile(cfg.DecodeProfile, cfg);
            return specs.Count == 0 ? null : ResponseDecoder.DecodeMany(response, specs);
        }

        private static void SetFirstMeasurement(UdsStepResult result)
        {
            if (result.Decode == null || result.Decode.Measurements.Count == 0) return;
            DecodedMeasurement first = result.Decode.Measurements[0];
            result.FirstName = first.Name; result.FirstRawValue = first.RawValue; result.FirstValue = first.Value; result.FirstUnit = first.Unit;
        }

        private static StepResult BuildStepResult(UdsStepConfig cfg)
        {
            StepResult step = new StepResult { TestItem = cfg.TestItem, Request = cfg.UdsSequence.Length > 0 ? cfg.UdsSequence : cfg.UdsPayload, Unit = cfg.Unit, Limits = cfg.LowerLimit + ".." + cfg.UpperLimit };
            step.ExpectedContext.ExpectedPrefix = cfg.ExpectedPrefix;
            step.ExpectedContext.ExpectedResponse = cfg.ExpectedResponse;
            step.ExpectedContext.ExpectedSuffix = cfg.ExpectedSuffix;
            step.ExpectedContext.ExpectedByteOffset = cfg.ExpectedByteOffset;
            step.ExpectedContext.ExpectedByteValue = cfg.ExpectedByteValue;
            step.ExpectedContext.DecodeProfile = cfg.DecodeProfile;
            return step;
        }

        private static void SyncStepResult(UdsStepResult result)
        {
            result.Step.Request = result.ActualRequestHex.Count > 0 ? string.Join(" | ", result.ActualRequestHex) : string.Join(" | ", result.CommandHex);
            result.Step.Response = !string.IsNullOrEmpty(result.CombinedResponseHex) ? result.CombinedResponseHex : string.Join(" | ", result.ResponseHexList);
            result.Step.Value = result.FirstValue == null ? "" : Convert.ToString(result.FirstValue, System.Globalization.CultureInfo.InvariantCulture);
            result.Step.TestType = result.Success ? "PASS" : "FAIL";
        }

        private static List<string> BuildCommands(UdsStepConfig cfg)
        {
            List<string> commands = new List<string>();
            if (!string.IsNullOrEmpty(cfg.UdsSequence))
            {
                foreach (string part in cfg.UdsSequence.Split('|'))
                    if (part.Trim().Length > 0) commands.Add(part.Trim());
            }
            else if (!string.IsNullOrEmpty(cfg.UdsPayload))
            {
                commands.Add(cfg.UdsPayload.Trim());
            }
            return commands;
        }

        private static List<string> SplitPipe(string value)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return list;
            foreach (string part in value.Split('|')) if (part.Trim().Length > 0) list.Add(part.Trim());
            return list;
        }

        private static void ParseInlineAnnotations(ref string command, out int pad, out int retry, out int delay)
        {
            pad = 0; retry = 0; delay = 500;
            int start = command.IndexOf('{');
            while (start >= 0)
            {
                int end = command.IndexOf('}', start);
                if (end < 0) break;
                string body = command.Substring(start + 1, end - start - 1);
                foreach (string item in body.Split(','))
                {
                    string[] pair = item.Trim().Split(new[] { '=', ':' }, 2);
                    int value;
                    if (pair.Length == 2 && int.TryParse(pair[1].Trim(), out value))
                    {
                        if (pair[0].Trim().Equals("pad", StringComparison.OrdinalIgnoreCase)) pad = Math.Max(0, value);
                        if (pair[0].Trim().Equals("retry", StringComparison.OrdinalIgnoreCase) || pair[0].Trim().Equals("count", StringComparison.OrdinalIgnoreCase)) retry = Math.Min(200, Math.Max(0, value));
                        if (pair[0].Trim().Equals("delay", StringComparison.OrdinalIgnoreCase)) delay = Math.Max(0, value);
                    }
                }
                command = command.Substring(0, start) + command.Substring(end + 1);
                start = command.IndexOf('{', start);
            }
        }

        private static bool MatchesExpectedResponse(byte[] response, string exactText, string prefixText, string suffixText, string offsetText, string valueText, out string error)
        {
            error = ""; byte[] exact = HexUtils.TryParseHex(exactText); byte[] prefix = HexUtils.TryParseHex(prefixText); byte[] suffix = HexUtils.TryParseHex(suffixText);
            if (exact != null && exact.Length > 0 && !Equal(response, exact)) { error = "UDS response mismatch: expected [" + HexUtils.ToHexString(exact) + "], actual [" + HexUtils.ToHexString(response) + "]."; return false; }
            if (prefix != null && prefix.Length > 0 && !Starts(response, prefix)) { error = "UDS response prefix mismatch: expected [" + HexUtils.ToHexString(prefix) + "], actual [" + HexUtils.ToHexString(response) + "]."; return false; }
            if (suffix != null && suffix.Length > 0 && !Ends(response, suffix)) { error = "UDS response suffix mismatch: expected [" + HexUtils.ToHexString(suffix) + "], actual [" + HexUtils.ToHexString(response) + "]."; return false; }
            int offset = 0; byte[] expectedByte;
            if (!string.IsNullOrWhiteSpace(offsetText) && (!int.TryParse(offsetText, out offset) || offset < 0 || offset >= response.Length || (expectedByte = HexUtils.TryParseHex(valueText)) == null || expectedByte.Length != 1 || response[offset] != expectedByte[0])) { error = "UDS response byte mismatch at offset " + offsetText + "."; return false; }
            return true;
        }

        private static byte[] PadRight(byte[] data, int length) { byte[] result = new byte[length]; Array.Copy(data, result, Math.Min(data.Length, length)); return result; }
        private static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static bool Starts(byte[] a, byte[] b) { if (a.Length < b.Length) return false; for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static bool Ends(byte[] a, byte[] b) { if (a.Length < b.Length) return false; int start = a.Length - b.Length; for (int i = 0; i < b.Length; i++) if (a[start + i] != b[i]) return false; return true; }

        private static List<DecodeSpec> LoadDecodeProfile(string name, UdsStepConfig cfg)
        {
            string[] candidates = { name, Path.Combine(cfg.BaseDirectory ?? "", name), Path.Combine(cfg.BaseDirectory ?? "", "DecodeProfiles", name), Path.Combine(Environment.CurrentDirectory, name) };
            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    object json;
                    if (!MiniJson.TryParse(File.ReadAllText(path), out json)) continue;
                    List<object> array = json as List<object>;
                    Dictionary<string, object> obj = json as Dictionary<string, object>;
                    object values = array;
                    if (obj != null) obj.TryGetValue("measurements", out values);
                    List<DecodeSpec> result = new List<DecodeSpec>();
                    List<object> items = values as List<object>;
                    if (items == null) continue;
                    foreach (object item in items)
                    {
                        Dictionary<string, object> node = item as Dictionary<string, object>;
                        if (node != null) result.Add(DecodeSpec.FromJson(node));
                    }
                    return result;
                }
                catch (Exception) { }
            }
            return new List<DecodeSpec>();
        }
    }
}
