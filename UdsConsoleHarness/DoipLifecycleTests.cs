using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jabil.Uds.Core;
using UdsCore;

namespace UdsConsoleHarness
{
    internal static class DoipLifecycleTests
    {
        public static void Run(Action<bool, string, string> check)
        {
            Console.WriteLine("\n== DoIP lifecycle regressions ==");
            TestBufferedControlFrames(check);
            TestAcquireReusesConnectingSession(check);
            TestConnectionOnlyKeepAlive(check);
            TestRoutingActivationRetry(check);
            TestPartialControlFrame(check);
            TestRequestDeadline(check);
            TestPendingTimeout(check);
            TestRecoveryKeepAlive(check);
        }

        private static void TestAcquireReusesConnectingSession(Action<bool, string, string> check)
        {
            string key = "lifecycle-acquire-" + Guid.NewGuid().ToString("N");
            DoipSessionPool pool = DoipSessionPool.Instance;
            try
            {
                DoipSession first = pool.Acquire(key);
                DoipSession second = pool.Acquire(key);
                check(object.ReferenceEquals(first, second),
                    "same session key reuses the session while connection is starting",
                    "same instance=" + object.ReferenceEquals(first, second));
            }
            finally { pool.Release(key); }
        }

        private static void TestConnectionOnlyKeepAlive(Action<bool, string, string> check)
        {
            string key = "lifecycle-connection-only-keepalive-" + Guid.NewGuid().ToString("N");
            using (var gateway = new Gateway(stream =>
            {
                Activate(stream);
                byte[] testerPresent = ReadFrame(stream);
                byte[] requiredFrame = ReadFrame(stream);
                return testerPresent.Length == 14 && testerPresent[2] == 0x80 &&
                    testerPresent[12] == 0x3E && testerPresent[13] == 0x80 &&
                    requiredFrame.Length == 10 && requiredFrame[2] == 0 &&
                    requiredFrame[3] == 8 && requiredFrame[8] == 0x10 &&
                    requiredFrame[9] == 0x2D;
            }))
            {
                try
                {
                    UdsStepConfig cfg = Config(gateway.Port);
                    cfg.ConnectionOnly = true;
                    cfg.KeepAliveEnabled = true;
                    cfg.KeepAliveIntervalMs = 250;
                    cfg.KeepAlivePayload = "3E 80|02 FD 00 08 00 00 00 02 10 2D";
                    UdsStepResult result = UdsRequestExecutor.ExecuteStep(cfg, key);
                    string detail;
                    bool sent = gateway.Finish(out detail);
                    check(result.Success && sent,
                        "connection-only keepalive sends configured UDS and raw DoIP frames",
                        result.Error + detail);
                }
                finally { UdsRequestExecutor.ReleaseSession(key); }
            }
        }

        private static void TestRoutingActivationRetry(Action<bool, string, string> check)
        {
            string key = "lifecycle-routing-retry-" + Guid.NewGuid().ToString("N");
            using (var gateway = new ActivationRetryGateway())
            {
                try
                {
                    UdsStepConfig cfg = Config(gateway.Port);
                    cfg.ConnectionOnly = true;
                    cfg.RoutingActivationRetryCount = 1;
                    cfg.RoutingActivationRetryDelayMs = 0;
                    UdsStepResult result = UdsRequestExecutor.ExecuteStep(cfg, key);
                    UdsRequestExecutor.ReleaseSession(key);
                    string detail;
                    bool retried = gateway.Finish(out detail);
                    check(result.Success && retried,
                        "routing activation retries once after response code 0x03",
                        result.Error + detail);
                }
                finally { UdsRequestExecutor.ReleaseSession(key); }
            }
        }

        private static void TestBufferedControlFrames(Action<bool, string, string> check)
        {
            using (var client = new DoipClient())
            using (var gateway = new Gateway(stream =>
            {
                Activate(stream);
                ReadFrame(stream);
                WriteFrame(stream, 0x8002, new byte[] { 0x10, 0x08, 0x10, 0x2D, 0x00 });
                WriteFrame(stream, 0x0007, new byte[0]);
                return IsAliveResponse(ReadFrame(stream));
            }))
            {
                string error;
                bool activated = client.Connect("127.0.0.1", gateway.Port, 2000) &&
                    client.ActivateRouting(0x102D, 0, 2000, out error);
                bool sent = activated && client.SendKeepAliveOnly(0x102D, 0x1008, new byte[] { 0x3E, 0x80 });
                string detail;
                bool answered = gateway.Finish(out detail);
                check(sent && answered, "ACK before idle Alive Check does not block its reply", detail);
            }
        }

        private static void TestPartialControlFrame(Action<bool, string, string> check)
        {
            using (var prefixReady = new ManualResetEventSlim(false))
            using (var client = new DoipClient())
            using (var gateway = new Gateway(stream =>
            {
                Activate(stream);
                ReadFrame(stream);
                byte[] ack = { 2, 0xFD, 0x80, 0x02, 0, 0, 0, 5, 0x10, 0x08, 0x10, 0x2D, 0 };
                stream.Write(ack, 0, 4);
                prefixReady.Set();
                ReadFrame(stream);
                stream.Write(ack, 4, ack.Length - 4);
                WriteFrame(stream, 0x0007, new byte[0]);
                bool answered = IsAliveResponse(ReadFrame(stream));
                WriteFrame(stream, 0x8001, new byte[] { 0x10, 0x08, 0x10, 0x2D, 0x62, 0xF1, 0x90, 1 });
                return answered;
            }))
            {
                string error = "";
                bool activated = client.Connect("127.0.0.1", gateway.Port, 2000) &&
                    client.ActivateRouting(0x102D, 0, 2000, out error);
                bool keptAlive = activated && client.SendKeepAliveOnly(0x102D, 0x1008, new byte[] { 0x3E, 0x80 });
                bool prefixSent = prefixReady.Wait(3000);
                byte[] response = null;
                bool sent = keptAlive && prefixSent && client.SendDiagnosticMessage(0x102D, 0x1008,
                    new byte[] { 0x22, 0xF1, 0x90 }, 2000, 0, out response, out error);
                string detail;
                bool answered = gateway.Finish(out detail);
                check(sent && answered && response != null && response.Length == 4 && response[0] == 0x62,
                    "partial stale ACK survives a new request and subsequent Alive Check", error + detail);
            }
        }

        private static void TestRequestDeadline(Action<bool, string, string> check)
        {
            using (var client = new DoipClient())
            using (var gateway = new Gateway(stream =>
            {
                Activate(stream);
                ReadFrame(stream);
                WriteFrame(stream, 0x0007, new byte[0]);
                bool answered = IsAliveResponse(ReadFrame(stream));
                return answered && stream.ReadByte() == -1;
            }))
            {
                string error = "";
                bool activated = client.Connect("127.0.0.1", gateway.Port, 2000) &&
                    client.ActivateRouting(0x102D, 0, 2000, out error);
                byte[] response = null;
                var timer = Stopwatch.StartNew();
                bool sent = activated && client.SendDiagnosticMessage(0x102D, 0x1008,
                    new byte[] { 0x22, 0xF1, 0x90 }, 120, 0, out response, out error);
                timer.Stop();
                string detail;
                bool answered = gateway.Finish(out detail);
                check(activated && !sent && answered && timer.ElapsedMilliseconds < 500,
                    "Alive Check preserves 120ms request deadline", "elapsed=" + timer.ElapsedMilliseconds + "ms; " + error + detail);
                check(!client.IsConnected, "timed-out request invalidates its connection", error);
            }
        }

        private static void TestPendingTimeout(Action<bool, string, string> check)
        {
            string key = "lifecycle-pending-" + Guid.NewGuid().ToString("N");
            using (var gateway = new Gateway(stream =>
            {
                Activate(stream);
                ReadFrame(stream);
                WriteFrame(stream, 0x8001, new byte[] { 0x10, 0x08, 0x10, 0x2D, 0x7F, 0x31, 0x78 });
                return stream.ReadByte() == -1;
            }))
            {
                try
                {
                    var cfg = Config(gateway.Port);
                    cfg.UdsPayload = "31 01 12 34{retry:2}";
                    cfg.TimeoutMs = 200;
                    cfg.ResponsePendingTimeoutMs = 100;
                    cfg.ResponsePendingRetryCount = 2;
                    cfg.SequenceRetryCount = 2;
                    cfg.SequenceRetryDelayMs = 0;
                    var result = UdsRequestExecutor.ExecuteStep(cfg, key);
                    string detail;
                    bool notReplayed = gateway.Finish(out detail);
                    check(!result.Success && result.Error.StartsWith("RESPONSE_PENDING_TIMEOUT", StringComparison.Ordinal) && notReplayed,
                        "pending timeout preserves original error without replay", result.Error + detail);
                    check(result.CombinedResponseHex == "7F 31 78",
                        "pending timeout retains last ECU response", result.CombinedResponseHex);
                }
                finally { UdsRequestExecutor.ReleaseSession(key); }
            }
        }

        private static void TestRecoveryKeepAlive(Action<bool, string, string> check)
        {
            string key = "lifecycle-recovery-" + Guid.NewGuid().ToString("N");
            using (var listener = new RecoveryGateway())
            {
                try
                {
                    var cfg = Config(listener.Port);
                    cfg.KeepAliveEnabled = true;
                    cfg.KeepAliveIntervalMs = 250;
                    cfg.UdsPayload = "22 F1 90";
                    cfg.NackRetryCount = 1;
                    cfg.NackRetryDelayMs = 50;
                    var result = UdsRequestExecutor.ExecuteStep(cfg, key);
                    string detail;
                    bool keptAlive = listener.Finish(out detail);
                    check(result.Success, "NACK recovery reconnects and completes request", result.Error);
                    check(keptAlive, "NACK recovery restarts periodic TesterPresent", detail);
                }
                finally { UdsRequestExecutor.ReleaseSession(key); }
            }
        }

        private static UdsStepConfig Config(int port)
        {
            return new UdsStepConfig
            {
                Host = "127.0.0.1", Port = port, SourceAddress = 0x102D, TargetAddress = 0x1008,
                TimeoutMs = 2000, MinRequestIntervalMs = 0
            };
        }

        private static bool IsAliveResponse(byte[] frame)
        {
            return frame.Length == 10 && frame[2] == 0 && frame[3] == 8 && frame[8] == 0x10 && frame[9] == 0x2D;
        }

        private static void Activate(NetworkStream stream)
        {
            ActivateWithCode(stream, 0x10);
        }

        private static void ActivateWithCode(NetworkStream stream, byte responseCode)
        {
            ReadFrame(stream);
            WriteFrame(stream, 0x0006, new byte[] { 0x10, 0x2D, 0x10, 0x08, responseCode, 0, 0, 0, 0 });
        }

        private static byte[] ReadFrame(NetworkStream stream)
        {
            byte[] header = ReadExact(stream, 8);
            int length = (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];
            byte[] frame = new byte[8 + length];
            Buffer.BlockCopy(header, 0, frame, 0, 8);
            Buffer.BlockCopy(ReadExact(stream, length), 0, frame, 8, length);
            return frame;
        }

        private static byte[] ReadExact(NetworkStream stream, int count)
        {
            byte[] bytes = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int received = stream.Read(bytes, offset, count - offset);
                if (received == 0) throw new IOException("Unexpected EOF in DoIP regression gateway.");
                offset += received;
            }
            return bytes;
        }

        private static void WriteFrame(NetworkStream stream, ushort type, byte[] payload)
        {
            byte[] frame = new byte[8 + payload.Length];
            frame[0] = 2;
            frame[1] = 0xFD;
            frame[2] = (byte)(type >> 8);
            frame[3] = (byte)type;
            frame[4] = (byte)(payload.Length >> 24);
            frame[5] = (byte)(payload.Length >> 16);
            frame[6] = (byte)(payload.Length >> 8);
            frame[7] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
            stream.Write(frame, 0, frame.Length);
        }

        private class Gateway : IDisposable
        {
            protected readonly TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            protected Task<bool> Server;
            public int Port { get { return ((IPEndPoint)Listener.LocalEndpoint).Port; } }

            protected Gateway() { Listener.Start(); }

            public Gateway(Func<NetworkStream, bool> handler) : this()
            {
                Server = Task.Run(() =>
                {
                    using (TcpClient peer = Listener.AcceptTcpClient())
                    {
                        peer.GetStream().ReadTimeout = 3000;
                        return handler(peer.GetStream());
                    }
                });
            }

            public bool Finish(out string detail)
            {
                try
                {
                    bool finished = Server.Wait(5000);
                    detail = finished ? "" : "Gateway did not finish within 5s.";
                    return finished && Server.Result;
                }
                catch (AggregateException ex)
                {
                    detail = ex.GetBaseException().Message;
                    return false;
                }
            }

            public void Dispose() { Listener.Stop(); }
        }

        private sealed class RecoveryGateway : Gateway
        {
            public RecoveryGateway()
            {
                Server = Task.Run(() =>
                {
                    using (TcpClient first = Listener.AcceptTcpClient())
                    {
                        NetworkStream stream = first.GetStream();
                        stream.ReadTimeout = 3000;
                        Activate(stream);
                        ReadFrame(stream);
                        WriteFrame(stream, 0x8003, new byte[] { 0x10, 0x08, 0x10, 0x2D, 0x08 });
                        if (stream.ReadByte() != -1) return false;
                    }
                    using (TcpClient second = Listener.AcceptTcpClient())
                    {
                        NetworkStream stream = second.GetStream();
                        stream.ReadTimeout = 2000;
                        Activate(stream);
                        ReadFrame(stream);
                        WriteFrame(stream, 0x8001, new byte[] { 0x10, 0x08, 0x10, 0x2D, 0x62, 0xF1, 0x90, 0x01 });
                        byte[] keepAlive = ReadFrame(stream);
                        return keepAlive.Length == 14 && keepAlive[12] == 0x3E && keepAlive[13] == 0x80;
                    }
                });
            }
        }

        private sealed class ActivationRetryGateway : Gateway
        {
            public ActivationRetryGateway()
            {
                Server = Task.Run(() =>
                {
                    using (TcpClient first = Listener.AcceptTcpClient())
                    {
                        NetworkStream stream = first.GetStream();
                        stream.ReadTimeout = 2000;
                        ActivateWithCode(stream, 0x03);
                        if (stream.ReadByte() != -1) return false;
                    }
                    using (TcpClient second = Listener.AcceptTcpClient())
                    {
                        NetworkStream stream = second.GetStream();
                        stream.ReadTimeout = 2000;
                        ActivateWithCode(stream, 0x10);
                        return stream.ReadByte() == -1;
                    }
                });
            }
        }
    }
}