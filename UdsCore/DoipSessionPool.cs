using System;
using System.Collections.Generic;
using System.Threading;
using ExcelDecodeCore;

namespace UdsCore
{
    /// <summary>One pooled DoIP/TCP session for an ECU.</summary>
    public class DoipSession : IDisposable
    {
        public string Key = "";
        public DoipClient Client = new DoipClient();
        public readonly object SyncRoot = new object();
        public ushort SourceAddress;
        public ushort TargetAddress;
        public bool RoutingActivated;
        public bool HasDiagnostic;
        public long LastActivityTick;         // Environment.TickCount64-ish

        // keep-alive (TesterPresent) configuration, refreshed from the first diagnostic step
        public bool KeepAliveEnabled;
        public bool KeepAliveBeforeDiagnostic;
        public int KeepAliveIntervalMs = 2000;
        public byte[] KeepAlivePayload = { 0x3E, 0x80 };           // 兼容：单条
        public List<byte[]> KeepAlivePayloads = new List<byte[]>(); // 多条：UDS 报文 或 原始 DoIP 帧（02 FD ...）
        public int KeepAliveTimeoutMs = 1000;

        // diagnostic session-control requests to replay after a NACK 0x08 reconnect
        public readonly List<byte[]> SessionControls = new List<byte[]>();

        public bool IsOpen { get { return Client != null && Client.IsConnected; } }

        public void Touch()
        {
            LastActivityTick = Environment.TickCount;
        }

        // ---- 后台周期 keep-alive：发完最后一条诊断后，即使无新步骤也按 KeepAliveIntervalMs 周期自动发 ----
        private System.Threading.Timer _keepAliveTimer;

        public void StartKeepAlive()
        {
            StopKeepAlive();
            if (!KeepAliveEnabled) return;
            int period = Math.Max(250, KeepAliveIntervalMs);
            _keepAliveTimer = new System.Threading.Timer(_ => KeepAliveTick(), null, period, period);
        }

        public void StopKeepAlive()
        {
            try { if (_keepAliveTimer != null) { _keepAliveTimer.Dispose(); _keepAliveTimer = null; } } catch (Exception) { }
        }

        private void KeepAliveTick()
        {
            try
            {
                if (!KeepAliveEnabled || (!HasDiagnostic && !KeepAliveBeforeDiagnostic)) return;
                if (Client == null || !Client.IsConnected) return;
                var payloads = KeepAlivePayloads.Count > 0
                    ? KeepAlivePayloads
                    : new List<byte[]> { KeepAlivePayload };
                foreach (byte[] payload in payloads)
                {
                    if (DoipClient.IsRawDoipFrame(payload))
                        Client.SendRawDoipFrame(payload);
                    else
                        Client.SendKeepAliveOnly(SourceAddress, TargetAddress, payload);
                }
                Touch();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Idle keep-alive refresh (main thread, called before a real request):
        /// if the session is idle longer than the interval and already has a diagnostic,
        /// send one TesterPresent frame.
        /// </summary>
        public void SendIdleKeepAlive()
        {
            if (!KeepAliveEnabled || !HasDiagnostic) return;
            long elapsed = Math.Max(0, Environment.TickCount - LastActivityTick);
            if (elapsed < KeepAliveIntervalMs) return;
            if (Client == null || !Client.IsConnected) return;

            // 一次周期内按顺序发完全部保活：UDS 报文封装 0x8001 发；原始 DoIP 帧（如 02 FD ...）原样发
            var payloads = KeepAlivePayloads.Count > 0
                ? KeepAlivePayloads
                : new List<byte[]> { KeepAlivePayload };
            foreach (byte[] payload in payloads)
            {
                if (DoipClient.IsRawDoipFrame(payload))
                    Client.SendRawDoipFrame(payload);
                else
                    Client.SendKeepAliveOnly(SourceAddress, TargetAddress, payload);
            }
            Touch();
        }

        public void Dispose()
        {
            StopKeepAlive();
            try { if (Client != null) Client.Dispose(); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Session pool keyed by ECU/session key, mirroring EonTest's sessions_ map
    /// (workflowId/cellId -> session). For JEMSts the key is the ECU name.
    /// </summary>
    public class DoipSessionPool
    {
        private readonly Dictionary<string, DoipSession> _sessions =
            new Dictionary<string, DoipSession>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        private static readonly Lazy<DoipSessionPool> _instance =
            new Lazy<DoipSessionPool>(() => new DoipSessionPool());

        public static DoipSessionPool Instance { get { return _instance.Value; } }

        public DoipSession Acquire(string key)
        {
            lock (_lock)
            {
                DoipSession session;
                if (_sessions.TryGetValue(key, out session))
                {
                    session.Touch();
                    return session;
                }
                session = new DoipSession { Key = key };
                session.Touch();
                _sessions[key] = session;
                return session;
            }
        }

        public bool TryGet(string key, out DoipSession session)
        {
            lock (_lock)
            {
                return _sessions.TryGetValue(key, out session) && session.IsOpen;
            }
        }

        public List<DoipSession> GetByBaseKey(string key)
        {
            var matches = new List<DoipSession>();
            if (string.IsNullOrEmpty(key)) return matches;
            lock (_lock)
            {
                foreach (KeyValuePair<string, DoipSession> entry in _sessions)
                {
                    if ((string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) ||
                         entry.Key.StartsWith(key + "|local=", StringComparison.OrdinalIgnoreCase)) &&
                        entry.Value.IsOpen)
                        matches.Add(entry.Value);
                }
            }
            return matches;
        }

        public void Release(string key)
        {
            lock (_lock)
            {
                var keysToRemove = new List<string>();
                foreach (KeyValuePair<string, DoipSession> entry in _sessions)
                {
                    if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) ||
                        entry.Key.StartsWith(key + "|local=", StringComparison.OrdinalIgnoreCase))
                    {
                        RemoveSession(entry.Value);
                        keysToRemove.Add(entry.Key);
                    }
                }
                foreach (string matchedKey in keysToRemove) _sessions.Remove(matchedKey);
            }
        }

        public void CloseAll()
        {
            lock (_lock)
            {
                foreach (DoipSession session in _sessions.Values)
                    RemoveSession(session);
                _sessions.Clear();
            }
        }

        private static void RemoveSession(DoipSession session)
        {
            try
            {
                lock (session.SyncRoot) session.Dispose();
            }
            catch (Exception) { }
        }
    }
}
