using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ExcelDecodeCore;

namespace UdsCore
{
    /// <summary>
    /// DoIP TCP client. Direct port of EonTest DoipProtocolLayer (TCP part) on top of TcpClient.
    /// Responsibilities: TCP connect, Routing Activation, Diagnostic Message send/receive,
    /// ACK/NACK handling, UDS Response Pending (0x78) handling, keep-alive.
    /// </summary>
    public class DoipClient : IDisposable
    {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private bool _routingActive;
        private bool _disposed;
        private ushort _sourceAddress;
        private Timer _aliveCheckTimer;
        // IO 互斥：主线程诊断交换与后台 keep-alive 发送互斥，避免帧交错
        private readonly object _ioLock = new object();

        public bool RoutingActive { get { return _routingActive; } }
        public bool IsConnected { get { return _tcp != null && _tcp.Connected; } }
        public bool TcpKeepAliveEnabled
        {
            get
            {
                try
                {
                    return _tcp != null && Convert.ToInt32(
                        _tcp.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)) != 0;
                }
                catch (Exception) { return false; }
            }
        }

        public string LastError = "";
        public int LastRoutingActivationResponseCode = -1;
        // 最近一次诊断收发过程（TX 指令 → RX 各帧 → 超时等），失败时用于定位
        public string LastTrace = "";
        private readonly System.Text.StringBuilder _trace = new System.Text.StringBuilder();

        public bool Connect(string host, int port, int timeoutMs)
        {
            return Connect(host, port, timeoutMs, "", 0, true);
        }

        public bool Connect(string host, int port, int timeoutMs, string localBindAddress, int localBindPort)
        {
            return Connect(host, port, timeoutMs, localBindAddress, localBindPort, true);
        }

        public bool Connect(string host, int port, int timeoutMs, string localBindAddress, int localBindPort,
                            bool tcpKeepAlive)
        {
            Close();
            if (string.IsNullOrWhiteSpace(host))
            {
                LastError = "host is empty: set host=<IP> in G column (配置参数1) of the first DoIP step, " +
                            "or pass a default host argument.";
                return false;
            }
            if (port <= 0 || port > 65535)
            {
                LastError = "invalid port: " + port;
                return false;
            }
            if (localBindPort < 0 || localBindPort > 65535)
            {
                LastError = "invalid local bind port: " + localBindPort;
                return false;
            }
            try
            {
                string bindAddressText = string.IsNullOrWhiteSpace(localBindAddress)
                    ? ""
                    : localBindAddress.Trim();
                bool bindRequested = bindAddressText.Length > 0 || localBindPort > 0;
                if (!bindRequested)
                {
                    _tcp = new TcpClient();
                    var task = _tcp.ConnectAsync(host, port);
                    if (!task.Wait(Math.Max(1, timeoutMs)))
                    {
                        LastError = "TCP connect timed out.";
                        Close();
                        return false;
                    }
                }
                else
                {
                    IPAddress localAddress = null;
                    if (bindAddressText.Length > 0 && !IPAddress.TryParse(bindAddressText, out localAddress))
                    {
                        LastError = "invalid local bind IP address: " + bindAddressText;
                        return false;
                    }

                    IPAddress[] remoteAddresses;
                    IPAddress remoteLiteral;
                    if (IPAddress.TryParse(host, out remoteLiteral))
                    {
                        remoteAddresses = new[] { remoteLiteral };
                    }
                    else
                    {
                        Task<IPAddress[]> resolveTask = Dns.GetHostAddressesAsync(host);
                        if (!resolveTask.Wait(Math.Max(1, timeoutMs)))
                        {
                            LastError = "DNS lookup timed out.";
                            return false;
                        }
                        remoteAddresses = resolveTask.Result;
                    }

                    IPAddress remoteAddress = null;
                    for (int index = 0; index < remoteAddresses.Length; index++)
                    {
                        if (localAddress == null || remoteAddresses[index].AddressFamily == localAddress.AddressFamily)
                        {
                            remoteAddress = remoteAddresses[index];
                            break;
                        }
                    }
                    if (remoteAddress == null)
                    {
                        LastError = "No remote address matches local bind address family.";
                        return false;
                    }

                    AddressFamily family = remoteAddress.AddressFamily;
                    _tcp = new TcpClient(family);
                    IPAddress bindAddress = localAddress ??
                        (family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any);
                    _tcp.Client.Bind(new IPEndPoint(bindAddress, localBindPort));
                    var task = _tcp.ConnectAsync(remoteAddress, port);
                    if (!task.Wait(Math.Max(1, timeoutMs)))
                    {
                        LastError = "TCP connect timed out.";
                        Close();
                        return false;
                    }
                }
                _tcp.NoDelay = true;          // equivalent of TCP_NODELAY / LowDelayOption
                _tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, tcpKeepAlive);
                _stream = _tcp.GetStream();
                _stream.ReadTimeout = Math.Max(1, timeoutMs);
                _stream.WriteTimeout = Math.Max(1, timeoutMs);
                return true;
            }
            catch (Exception ex)
            {
                LastError = "TCP connect failed: " + ex.Message;
                Close();
                return false;
            }
        }

        /// <summary>Send ISO 13400 Routing Activation and wait for the activation response.</summary>
        public bool ActivateRouting(ushort sourceAddr, byte activationType, int timeoutMs, out string errorMessage)
        {
            errorMessage = "";
            LastRoutingActivationResponseCode = -1;
            if (!IsConnected) { errorMessage = "DoIP TCP is not connected."; return false; }
            try
            {
                _sourceAddress = sourceAddr;
                byte[] request = BuildRoutingActivationRequest(sourceAddr, activationType);
                if (!SendTcpFrame(request, out errorMessage)) return false;

                byte[] response;
                if (!ReceiveTcpFrame(timeoutMs, out response, out errorMessage)) return false;

                ushort payloadType;
                uint payloadLength;
                int headerEnd;
                if (!ParseDoipHeader(response, out payloadType, out payloadLength, out headerEnd))
                {
                    errorMessage = "Invalid routing activation response.";
                    return false;
                }
                if (payloadType != DoipConstants.PayloadType.RoutingActivationResponse)
                {
                    errorMessage = "Expected routing activation response.";
                    return false;
                }
                byte[] payload = Slice(response, headerEnd);
                byte responseCode;
                if (!ParseRoutingActivationResponse(payload, out responseCode))
                {
                    errorMessage = "Routing activation response too short.";
                    return false;
                }
                LastRoutingActivationResponseCode = responseCode;
                if (responseCode != DoipConstants.ActivationResponse.Successful)
                {
                    errorMessage = "Routing activation denied: 0x" + responseCode.ToString("X2");
                    return false;
                }
                _routingActive = true;
                StartAliveCheckMonitor();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Routing activation failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Send one UDS diagnostic message and return the final UDS response.
        /// Handles: DoIP ACK/NACK frames, UDS Response Pending (0x78) with extended timeout.
        /// </summary>
        public bool SendDiagnosticMessage(ushort sourceAddr, ushort targetAddr, byte[] udsPayload,
                                          int timeoutMs, int responsePendingTimeoutMs,
                                          out byte[] udsResponse, out string errorMessage)
        {
            // 整个“请求-响应”交换独占 IO 锁，避免后台 keep-alive 帧插入打断
            lock (_ioLock)
            {
            udsResponse = null;
            errorMessage = "";
            if (!_routingActive) { errorMessage = "DoIP routing not activated."; return false; }
            if (udsPayload == null || udsPayload.Length == 0) { errorMessage = "empty UDS payload."; return false; }

            if (!ProcessIdleFrames(out errorMessage)) return false;

            byte[] diagMsg = BuildDiagnosticMessage(sourceAddr, targetAddr, udsPayload);
            _trace.Clear();
            _trace.Append("TX " + HexUtils.ToHexString(diagMsg) + " (UDS " + HexUtils.ToHexString(udsPayload) + ")");
            if (!SendTcpFrame(diagMsg, out errorMessage))
            {
                _trace.Append(" | TX FAIL: " + errorMessage);
                LastTrace = _trace.ToString();
                return false;
            }

            // ECU 可能先返回一个或多个诊断 ACK/NACK，再返回最终 0x8001 响应。
            Stopwatch waitTimer = Stopwatch.StartNew();
            bool hasResponsePending = false;
            Stopwatch responsePendingTimer = null;
            while (true)
            {
                int remainingMs;
                if (hasResponsePending)
                {
                    int extendedTimeout = responsePendingTimeoutMs > 0
                        ? responsePendingTimeoutMs
                        : Math.Max(timeoutMs, Math.Min(120000, timeoutMs * 4));
                    remainingMs = Math.Max(1, extendedTimeout - (int)responsePendingTimer.ElapsedMilliseconds);
                }
                else
                {
                    remainingMs = timeoutMs < 0 ? timeoutMs : Math.Max(1, timeoutMs - (int)waitTimer.ElapsedMilliseconds);
                }

                byte[] response;
                if (!ReceiveTcpFrame(remainingMs, out response, out errorMessage))
                {
                    _trace.Append(" | RX FAIL: " + errorMessage);
                    if (hasResponsePending && errorMessage.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _trace.Append(" (0x78 ResponsePending)");
                        errorMessage = "RESPONSE_PENDING_TIMEOUT: ECU did not complete within extended timeout after 0x78.";
                    }
                    LastTrace = _trace.ToString();
                    return false;
                }

                ushort payloadType;
                uint payloadLength;
                int headerEnd;
                if (!ParseDoipHeader(response, out payloadType, out payloadLength, out headerEnd))
                {
                    _trace.Append(" | RX INVALID " + HexUtils.ToHexString(response));
                    LastTrace = _trace.ToString();
                    errorMessage = "Invalid DoIP diagnostic response.";
                    return false;
                }

                _trace.Append(" | RX 0x" + payloadType.ToString("X4") + " " + HexUtils.ToHexString(response));

                byte[] payload = Slice(response, headerEnd);
                if (payloadType == DoipConstants.PayloadType.DiagnosticMessage)
                {
                    byte[] candidate;
                    if (!ParseDiagnosticMessageResponse(payload, out candidate))
                    {
                        errorMessage = "Invalid DoIP diagnostic response payload.";
                        return false;
                    }

                    if (candidate.Length >= 3 && udsPayload.Length >= 1 &&
                        candidate[0] == 0x7F && candidate[1] == udsPayload[0] && candidate[2] == 0x78)
                    {
                        udsResponse = candidate;
                        if (!hasResponsePending)
                        {
                            _trace.Append(" [0x78 ResponsePending]");
                            hasResponsePending = true;
                            responsePendingTimer = Stopwatch.StartNew();
                        }
                        continue;
                    }
                    udsResponse = candidate;
                    LastTrace = _trace + " [final]";
                    return true;
                }

                if (payloadType == DoipConstants.PayloadType.DiagnosticMessagePositiveAck ||
                    payloadType == DoipConstants.PayloadType.DiagnosticMessageNegativeAck)
                {
                    if (payload.Length < 5)
                    {
                        errorMessage = "Malformed DoIP diagnostic ACK/NACK.";
                        LastTrace = _trace.ToString();
                        return false;
                    }
                    byte code = payload[4];
                    if (payloadType == DoipConstants.PayloadType.DiagnosticMessageNegativeAck && code != 0x00)
                    {
                        _trace.Append(" [NACK 0x" + code.ToString("X2") + "]");
                        LastTrace = _trace.ToString();
                        errorMessage = "DoIP diagnostic NACK: 0x" + code.ToString("X2") + " (" + NackReason(code) + ")";
                        return false;
                    }
                    int extendedTimeout = Math.Max(timeoutMs, Math.Min(120000, timeoutMs * 4));
                    int ackTimeoutMs = hasResponsePending ? extendedTimeout : timeoutMs;
                    if (ackTimeoutMs >= 0 && waitTimer.ElapsedMilliseconds >= ackTimeoutMs)
                    {
                        errorMessage = "Timed out waiting for final DoIP diagnostic response.";
                        return false;
                    }
                    continue;
                }

                LastTrace = _trace.ToString();
                errorMessage = "Unexpected DoIP response type 0x" + payloadType.ToString("X4") + ".";
                return false;
            }
            }
        }

        /// <summary>
        /// TesterPresent keep-alive frame. Default "3E 80" (suppress response) — send only.
        /// If the payload does not suppress the response, read and discard it.
        /// </summary>
        public bool SendKeepAliveOnly(ushort sourceAddr, ushort targetAddr, byte[] udsPayload)
        {
            // 与主线程诊断交换互斥：后台 keep-alive 发送不与诊断请求响应交叉
            lock (_ioLock)
            {
            if (!_routingActive) return false;
            if (!IsConnected) return false;
            try
            {
                byte[] frame = BuildDiagnosticMessage(sourceAddr, targetAddr, udsPayload);
                string ignored;
                if (!SendTcpFrame(frame, out ignored)) return false;
                bool suppressResponse = udsPayload.Length >= 2 && (udsPayload[1] & 0x80) != 0;
                if (!suppressResponse)
                {
                    byte[] resp;
                    ReceiveTcpFrame(500, out resp, out ignored);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            }
        }

        /// <summary>
        /// Send a raw ISO 13400 DoIP frame verbatim (e.g. a custom keep-alive frame like
        /// "02 FD 00 08 00 00 00 02 10 2D"). No re-wrapping — caller supplies complete bytes.
        /// </summary>
        public bool SendRawDoipFrame(byte[] frame)
        {
            lock (_ioLock)
            {
                if (!IsConnected) return false;
                try { string ignored; return SendTcpFrame(frame, out ignored); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>
        /// True if the bytes look like a complete DoIP frame: version/inverse pair (02 FD or 03 FC)
        /// plus at least a full 8-byte header. Such entries in a keep-alive list are sent verbatim.
        /// </summary>
        public static bool IsRawDoipFrame(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return false;
            byte v = bytes[0];
            byte inv = bytes[1];
            return (v == 0x02 && inv == 0xFD) || (v == 0x03 && inv == 0xFC);
        }

        // ---- frame building ----

        private byte[] BuildDoipHeader(ushort payloadType, uint payloadLength)
        {
            byte[] header = new byte[DoipConstants.HeaderSize];
            header[0] = DoipConstants.ProtocolVersion;
            header[1] = DoipConstants.InverseProtocolVersion;
            header[2] = (byte)((payloadType >> 8) & 0xFF);
            header[3] = (byte)(payloadType & 0xFF);
            header[4] = (byte)((payloadLength >> 24) & 0xFF);
            header[5] = (byte)((payloadLength >> 16) & 0xFF);
            header[6] = (byte)((payloadLength >> 8) & 0xFF);
            header[7] = (byte)(payloadLength & 0xFF);
            return header;
        }

        public static byte[] BuildRoutingActivationRequest(ushort sourceAddr, byte actType)
        {
            byte[] payload = new byte[7];
            payload[0] = (byte)((sourceAddr >> 8) & 0xFF);
            payload[1] = (byte)(sourceAddr & 0xFF);
            payload[2] = actType;
            byte[] frame = new byte[DoipConstants.HeaderSize + payload.Length];
            byte[] header = new byte[DoipConstants.HeaderSize];
            header[0] = DoipConstants.ProtocolVersion;
            header[1] = DoipConstants.InverseProtocolVersion;
            header[2] = 0x00; header[3] = 0x05;
            header[4] = 0x00; header[5] = 0x00; header[6] = 0x00; header[7] = 0x07;
            Buffer.BlockCopy(header, 0, frame, 0, DoipConstants.HeaderSize);
            Buffer.BlockCopy(payload, 0, frame, DoipConstants.HeaderSize, payload.Length);
            return frame;
        }

        private byte[] BuildDiagnosticMessage(ushort sourceAddr, ushort targetAddr, byte[] udsPayload)
        {
            int payloadLen = 4 + udsPayload.Length;
            byte[] frame = new byte[DoipConstants.HeaderSize + payloadLen];
            byte[] header = BuildDoipHeader(DoipConstants.PayloadType.DiagnosticMessage, (uint)payloadLen);
            Buffer.BlockCopy(header, 0, frame, 0, DoipConstants.HeaderSize);
            frame[DoipConstants.HeaderSize] = (byte)((sourceAddr >> 8) & 0xFF);
            frame[DoipConstants.HeaderSize + 1] = (byte)(sourceAddr & 0xFF);
            frame[DoipConstants.HeaderSize + 2] = (byte)((targetAddr >> 8) & 0xFF);
            frame[DoipConstants.HeaderSize + 3] = (byte)(targetAddr & 0xFF);
            Buffer.BlockCopy(udsPayload, 0, frame, DoipConstants.HeaderSize + 4, udsPayload.Length);
            return frame;
        }

        // ---- low level IO ----

        private bool SendTcpFrame(byte[] data, out string errorMessage)
        {
            errorMessage = "";
            lock (_ioLock)
            {
                try
                {
                    if (_stream == null) { errorMessage = "stream not open."; return false; }
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = "TCP write failed: " + ex.Message;
                    Close();
                    return false;
                }
            }
        }

        private bool ReceiveTcpFrame(int timeoutMs, out byte[] data, out string errorMessage)
        {
            data = null;
            errorMessage = "";
            lock (_ioLock)
            {
                NetworkStream stream = _stream;
                if (stream == null) { errorMessage = "stream not open."; return false; }
                int oldReadTimeout = stream.ReadTimeout;
                var timer = Stopwatch.StartNew();
                try
                {
                    while (true)
                    {
                        string readError;
                        byte[] header = ReadExact(8, timer, timeoutMs, out readError);
                        if (header == null)
                        {
                            errorMessage = readError;
                            Close();
                            return false;
                        }

                        ushort payloadType;
                        uint payloadLength;
                        int headerEnd;
                        if (!ParseDoipHeader(header, out payloadType, out payloadLength, out headerEnd) ||
                            payloadLength > int.MaxValue - DoipConstants.HeaderSize)
                        {
                            errorMessage = "Invalid DoIP header or payload length.";
                            Close();
                            return false;
                        }
                        byte[] payload = ReadExact((int)payloadLength, timer, timeoutMs, out readError);
                        if (payload == null)
                        {
                            errorMessage = readError + " (DoIP payload: " + payloadLength + " bytes).";
                            Close();
                            return false;
                        }

                        data = new byte[8 + payload.Length];
                        Buffer.BlockCopy(header, 0, data, 0, 8);
                        Buffer.BlockCopy(payload, 0, data, 8, payload.Length);

                        if (payloadType == DoipConstants.PayloadType.AliveCheckRequest && payload.Length == 0)
                        {
                            byte[] aliveResponse = BuildAliveCheckResponse(_sourceAddress);
                            string sendError;
                            if (!SendTcpFrame(aliveResponse, out sendError))
                            {
                                errorMessage = "Failed to answer DoIP Alive Check: " + sendError;
                                Close();
                                return false;
                            }
                            continue;
                        }
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = "TCP read failed: " + ex.Message;
                    Close();
                    return false;
                }
                finally
                {
                    if (ReferenceEquals(_stream, stream)) stream.ReadTimeout = oldReadTimeout;
                }
            }
        }

        /// <summary>Read exactly <paramref name="count"/> bytes, honoring ReadTimeout. Returns null on timeout/disconnect.</summary>
        private byte[] ReadExact(int count, Stopwatch timer, int timeoutMs, out string errorMessage)
        {
            errorMessage = "";
            if (count == 0) return new byte[0];
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read;
                try
                {
                    if (timeoutMs >= 0)
                    {
                        long remaining = timeoutMs - timer.ElapsedMilliseconds;
                        if (remaining <= 0)
                        {
                            errorMessage = "TCP read timed out.";
                            return null;
                        }
                        _stream.ReadTimeout = (int)Math.Min(int.MaxValue, remaining);
                    }
                    else _stream.ReadTimeout = System.Threading.Timeout.Infinite;
                    read = _stream.Read(buffer, offset, count - offset);
                }
                catch (IOException ex)
                {
                    SocketException socketException = ex.InnerException as SocketException;
                    errorMessage = socketException != null && socketException.SocketErrorCode == SocketError.TimedOut
                        ? "TCP read timed out."
                        : "TCP read failed: " + ex.Message;
                    return null;
                }
                catch (ObjectDisposedException ex)
                {
                    errorMessage = "TCP connection closed: " + ex.Message;
                    return null;
                }
                if (read <= 0)
                {
                    errorMessage = "TCP peer closed the connection (EOF).";
                    return null;
                }
                offset += read;
            }
            return buffer;
        }

        private byte[] BuildAliveCheckResponse(ushort sourceAddr)
        {
            byte[] frame = new byte[DoipConstants.HeaderSize + 2];
            byte[] header = BuildDoipHeader(DoipConstants.PayloadType.AliveCheckResponse, 2);
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            frame[DoipConstants.HeaderSize] = (byte)((sourceAddr >> 8) & 0xFF);
            frame[DoipConstants.HeaderSize + 1] = (byte)(sourceAddr & 0xFF);
            return frame;
        }

        private void StartAliveCheckMonitor()
        {
            StopAliveCheckMonitor();
            _aliveCheckTimer = new Timer(_ => CheckForIdleAliveCheck(), null, 100, 100);
        }

        private void StopAliveCheckMonitor()
        {
            try
            {
                if (_aliveCheckTimer != null)
                {
                    _aliveCheckTimer.Dispose();
                    _aliveCheckTimer = null;
                }
            }
            catch (Exception) { }
        }

        private void CheckForIdleAliveCheck()
        {
            if (!Monitor.TryEnter(_ioLock)) return;
            try
            {
                if (!_routingActive || _tcp == null || _stream == null) return;
                try
                {
                    if (_tcp.Client.Poll(0, SelectMode.SelectRead) && _tcp.Available == 0)
                    {
                        Close();
                        return;
                    }
                    string error;
                    ProcessIdleFrames(out error);
                }
                catch (Exception)
                {
                    Close();
                }
            }
            finally { Monitor.Exit(_ioLock); }
        }

        private bool ProcessIdleFrames(out string errorMessage)
        {
            errorMessage = "";
            lock (_ioLock)
            {
                NetworkStream stream = _stream;
                if (_tcp == null || stream == null) { errorMessage = "stream not open."; return false; }
                int oldReadTimeout = stream.ReadTimeout;
                try
                {
                    for (int frameIndex = 0; frameIndex < 64; frameIndex++)
                    {
                        if (_tcp.Available < DoipConstants.HeaderSize) return true;
                        byte[] header = new byte[DoipConstants.HeaderSize];
                        int peeked = _tcp.Client.Receive(header, 0, header.Length, SocketFlags.Peek);
                        ushort payloadType;
                        uint payloadLength;
                        int headerEnd;
                        if (peeked < header.Length) return true;
                        if (!ParseDoipHeader(header, out payloadType, out payloadLength, out headerEnd) ||
                            payloadLength > int.MaxValue - DoipConstants.HeaderSize)
                        {
                            errorMessage = "Invalid buffered DoIP header or payload length.";
                            Close();
                            return false;
                        }
                        int frameLength = header.Length + (int)payloadLength;
                        if (_tcp.Available < frameLength) return true;
                        string readError;
                        if (ReadExact(frameLength, Stopwatch.StartNew(), 1000, out readError) == null)
                        {
                            errorMessage = readError;
                            Close();
                            return false;
                        }
                        if (payloadType == DoipConstants.PayloadType.AliveCheckRequest && payloadLength == 0 &&
                            !SendTcpFrame(BuildAliveCheckResponse(_sourceAddress), out errorMessage)) return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = "Buffered DoIP receive failed: " + ex.Message;
                    Close();
                    return false;
                }
                finally
                {
                    if (ReferenceEquals(_stream, stream)) stream.ReadTimeout = oldReadTimeout;
                }
            }
        }

        // ---- parsing ----

        public static bool ParseDoipHeader(byte[] data, out ushort payloadType, out uint payloadLength, out int headerEnd)
        {
            payloadType = 0;
            payloadLength = 0;
            headerEnd = 0;
            if (data == null || data.Length < DoipConstants.HeaderSize) return false;
            if (data[0] != DoipConstants.ProtocolVersion || data[1] != DoipConstants.InverseProtocolVersion) return false;
            payloadType = (ushort)((data[2] << 8) | data[3]);
            payloadLength = ((uint)data[4] << 24) | ((uint)data[5] << 16) | ((uint)data[6] << 8) | (uint)data[7];
            headerEnd = DoipConstants.HeaderSize;
            return true;
        }

        private static bool ParseRoutingActivationResponse(byte[] payload, out byte responseCode)
        {
            responseCode = 0;
            if (payload == null || payload.Length < 9) return false;
            responseCode = payload[4];
            return true;
        }

        private static bool ParseDiagnosticMessageResponse(byte[] payload, out byte[] udsResponse)
        {
            udsResponse = null;
            if (payload == null || payload.Length < 5) return false;
            udsResponse = Slice(payload, 4); // skip source + target addresses
            return true;
        }

        private static string NackReason(byte code)
        {
            switch (code)
            {
                case DoipConstants.DiagnosticNackCode.InvalidSourceAddress: return "invalid source address";
                case DoipConstants.DiagnosticNackCode.UnknownTargetAddress: return "unknown target address";
                case DoipConstants.DiagnosticNackCode.DiagnosticMessageTooLarge: return "diagnostic message too large";
                case DoipConstants.DiagnosticNackCode.OutOfMemory: return "out of memory";
                case DoipConstants.DiagnosticNackCode.TargetUnreachable: return "target unreachable";
                case DoipConstants.DiagnosticNackCode.UnknownNetwork: return "unknown network";
                case DoipConstants.DiagnosticNackCode.TransportProtocolError:
                    // ISO 13400-2:2012 ackCode 0x08 = too many active sockets（网关连接数超限）
                    return "too many active sockets (连接数超限: 检查其它 DoIP 客户端是否占用 / 网关最大连接数)";
                default: return "reserved or vendor-specific code";
            }
        }

        private static byte[] Slice(byte[] data, int start)
        {
            if (start >= data.Length) return new byte[0];
            byte[] result = new byte[data.Length - start];
            Array.Copy(data, start, result, 0, result.Length);
            return result;
        }

        public void Close()
        {
            lock (_ioLock)
            {
                _routingActive = false;
                StopAliveCheckMonitor();
                try { if (_stream != null) _stream.Dispose(); } catch (Exception) { }
                try { if (_tcp != null) _tcp.Close(); } catch (Exception) { }
                _stream = null;
                _tcp = null;
            }
        }

        public void Dispose()
        {
            if (!_disposed) { Close(); _disposed = true; }
        }
    }
}
