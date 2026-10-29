using System;
using System.Collections.Generic;
using System.Diagnostics;
using Jabil.Uds.Core;

namespace Jabil.Uds.Can
{
    public sealed class CanFrame
    {
        public uint Identifier;
        public bool Extended;
        public bool Remote;
        public bool BitRateSwitch;
        public byte[] Data = new byte[0];
    }

    public interface ICanChannel : IDisposable
    {
        void Initialize();
        void Start();
        int Send(IList<CanFrame> frames);
        IList<CanFrame> Receive(int timeoutMilliseconds);
    }

    public interface ICanTypedReceiveChannel
    {
        IList<CanFrame> Receive(int timeoutMilliseconds, int receiveMode);
    }

    public interface ICanReceiveCountChannel
    {
        int GetReceiveCount(bool canFd);
    }

    public interface ICanResetChannel
    {
        void Reset();
    }

    public interface ICanAutoSendCapability
    {
        bool SetAutoSend(int channelIndex, ushort frameIndex, bool canFd, bool enabled, CanFrame frame, uint interval);
        bool ApplyAutoSend(int channelIndex);
        bool ClearAutoSend(int channelIndex);
    }

    public interface ICanDeviceProvider
    {
        ICanChannel OpenChannel(CanChannelOptions options);
    }

    public interface ICanDeviceDiagnostics
    {
        string GetDeviceInfo();
        bool SetValue(string path, string value);
    }

    public sealed class CanBitrateSettings
    {
        public int NominalBitrate;
        public int DataBitrate;
        public string CustomBitrateConfig = "";

        // Compatibility alias for callers using the earlier name.
        public string CustomBitrate
        {
            get { return CustomBitrateConfig; }
            set { CustomBitrateConfig = value ?? ""; }
        }
    }

    public interface ICanCustomBitrateCapability
    {
        bool SetCustomBitrate(int channelIndex, string bitrate);
    }

    public interface IConfigurableCanDeviceProvider : ICanDeviceProvider
    {
        void Configure(string deviceAddress, string deviceSerialNumber);
    }

    public interface ICanDeviceLifecycleProvider : ICanDeviceProvider
    {
        void OpenDevice(string deviceAddress, string deviceSerialNumber);
    }

    // Optional capability: providers may resolve a physical device by serial/barcode.
    public interface ICanDeviceSelector
    {
        void OpenDeviceByIdentifier(string deviceAddress, string deviceIdentifier);
    }

    public sealed class CanChannelOptions
    {
        public int ChannelIndex;
        public int NominalBitrate = 500000;
        public int DataBitrate = 2000000;
        public bool CanFd;
        public bool BitRateSwitch;
        public int CanFdStandard;
        public int CanFdMode;
        // Optional fixed data length and ISO-TP padding byte. Zero means native payload length.
        public int FrameLength;
        public byte PaddingByte = 0xCC;
        // Complete vendor timing string; provider maps it to its native API.
        public string CustomBitrateConfig = "";
        public string CustomBitrate
        {
            get { return CustomBitrateConfig; }
            set { CustomBitrateConfig = value ?? ""; }
        }
        public string DeviceAddress = "";
        public string DeviceSerialNumber = "";
    }

    public interface ICanUdsTransport : IUdsTransport
    {
        CanChannelOptions Options { get; }
    }

    public interface IZlgClosedUdsCapability : IDisposable
    {
        void ConfigureSession(int timeoutMilliseconds, int enhancedTimeoutMilliseconds);
        void ConfigureTransport(byte localStMin, byte blockSize, byte remoteStMin, bool modifyRemoteStMin);
        byte[] Request(uint sourceAddress, uint targetAddress, byte[] payload, bool suppressResponse);
        void SetTesterPresent(bool enabled, uint address, ushort cycleMilliseconds, bool suppressResponse);
    }

    public interface IZlgClosedUdsProvider
    {
        IZlgClosedUdsCapability OpenZlgClosedUds(CanChannelOptions options);
    }

    public sealed class HardwareUdsResponse
    {
        public byte Status;
        public byte Type;
        public byte[] Payload = new byte[0];
    }

    public interface IZcanHardwareUdsProvider
    {
        HardwareUdsResponse HardwareUdsRequest(CanChannelOptions options, uint requestId, uint sourceAddress, uint targetAddress, byte[] payload, uint frameType, byte fillByte, byte fillMode, bool suppressResponse);
    }

    public sealed class LinMessage
    {
        public byte Id;
        public byte[] Data = new byte[0];
        public byte Checksum;
        public byte Direction;
    }

    public interface ILinCapability : IDisposable
    {
        void Initialize(bool master, byte checksumMode, byte maxLength, uint baudrate);
        void Start();
        void Reset();
        int Transmit(IList<LinMessage> messages);
        int GetReceiveCount();
        IList<LinMessage> Receive(int timeoutMilliseconds, int maxCount);
        void Publish(byte id, byte[] data, byte checksumMode);
        void Subscribe(byte id, byte length, byte checksumMode);
        void WakeUp();
    }

    public interface ILinProvider
    {
        ILinCapability OpenLin(int channelIndex);
    }

    public sealed class ZNetProperty
    {
        public string Name = "";
        public string Value = "";
    }

    public sealed class ZNetTcpConnection
    {
        public string Ip = "";
        public ushort Port;
    }

    public sealed class ZNetComConnection
    {
        public string Port = "";
        public int BaudRate = 115200;
        public int DataBits = 8;
        public int StopBits = 1;
        public int Parity;
        public int Timeout = 1000;
    }

    public sealed class ZNetCloudConnection
    {
        public int DeviceIndex;
        public string Id = "";
    }

    public sealed class ZNetBleConnection
    {
        public string DeviceName = "";
        public string DeviceMac = "";
    }

    public interface IZNetCapability
    {
        bool Search(uint timeoutMilliseconds, string ip);
        IntPtr AddDevice(uint deviceType);
        bool DeleteDevice(IntPtr handle);
        bool ClearDevices();
        uint GetDeviceCount();
        IntPtr GetDeviceAt(uint index);
        bool ConnectTcp(IntPtr handle, ZNetTcpConnection connection);
        bool ConnectCom(IntPtr handle, ZNetComConnection connection);
        bool ConnectCloud(IntPtr handle, ZNetCloudConnection connection);
        bool ConnectBle(IntPtr handle, ZNetBleConnection connection);
        bool IsConnected(IntPtr handle);
        bool Disconnect(IntPtr handle);
        bool Upgrade(IntPtr handle, byte[] image, Action<float> progress);
        int ExecuteCommand(int command, IntPtr handle, IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);
        bool LoadInfo(IntPtr handle);
        bool GetProperty(IntPtr handle, string name, out ZNetProperty property);
        bool SetProperty(IntPtr handle, string name, string value);
        bool SubmitInfo(IntPtr handle, string password);
        bool Reset(IntPtr handle, string password);
        bool FactoryReset(IntPtr handle, string password);
    }

    public interface IZNetProvider
    {
        IZNetCapability OpenZNet();
    }

    public static class CanTransportRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ICanDeviceProvider> providers = new Dictionary<string, ICanDeviceProvider>(StringComparer.OrdinalIgnoreCase);
        private static string currentKey = "";
        public static void Register(ICanDeviceProvider deviceProvider)
        {
            Register("default", deviceProvider);
        }
        public static void Register(string key, ICanDeviceProvider deviceProvider)
        {
            if (deviceProvider == null) throw new ArgumentNullException("deviceProvider");
            if (String.IsNullOrWhiteSpace(key)) key = "default";
            ICanDeviceProvider previous = null;
            lock (Sync)
            {
                if (providers.TryGetValue(key, out previous) && !Object.ReferenceEquals(previous, deviceProvider)) providers.Remove(key);
                providers[key] = deviceProvider;
                currentKey = key;
            }
            if (previous != null && !Object.ReferenceEquals(previous, deviceProvider)) DisposeProvider(previous);
        }
        public static bool Select(string key)
        {
            lock (Sync)
            {
                if (String.IsNullOrWhiteSpace(key) || !providers.ContainsKey(key)) return false;
                currentKey = key;
                return true;
            }
        }
        public static string GetKey(ICanDeviceProvider deviceProvider)
        {
            lock (Sync)
            {
                foreach (KeyValuePair<string, ICanDeviceProvider> item in providers)
                    if (Object.ReferenceEquals(item.Value, deviceProvider)) return item.Key;
                return "";
            }
        }
        public static void Remove(string key)
        {
            ICanDeviceProvider removed = null;
            lock (Sync)
            {
                if (!providers.TryGetValue(key, out removed)) return;
                providers.Remove(key);
                if (String.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                    currentKey = providers.Count == 0 ? "" : new List<string>(providers.Keys)[0];
            }
            DisposeProvider(removed);
        }
        public static string CurrentKey { get { lock (Sync) return currentKey; } }
        public static ICanDeviceProvider Get(string key)
        {
            lock (Sync)
            {
                ICanDeviceProvider value;
                return !String.IsNullOrWhiteSpace(key) && providers.TryGetValue(key, out value) ? value : null;
            }
        }
        public static ICanDeviceProvider Current { get { lock (Sync) { ICanDeviceProvider value; return providers.TryGetValue(currentKey, out value) ? value : null; } } }
        public static void Clear()
        {
            ICanDeviceProvider[] previous;
            lock (Sync)
            {
                previous = new List<ICanDeviceProvider>(providers.Values).ToArray();
                providers.Clear();
                currentKey = "";
            }
            foreach (ICanDeviceProvider item in previous) DisposeProvider(item);
        }

        private static void DisposeProvider(ICanDeviceProvider deviceProvider)
        {
            IDisposable disposable = deviceProvider as IDisposable;
            if (disposable != null)
            {
                try { disposable.Dispose(); } catch (Exception) { }
            }
        }
    }

    public sealed class CanIsoTpTransport : ICanUdsTransport, IUdsResponsePendingTransport
    {
        private readonly ICanDeviceProvider provider;
        private readonly CanChannelOptions options;
        private uint requestId;
        private uint responseId;
        private byte localStMin;
        private byte blockSize;
        private byte remoteStMin;
        private bool modifyRemoteStMin;
        private ICanChannel channel;
        private bool connected;
        public CanIsoTpTransport(ICanDeviceProvider provider, CanChannelOptions options, uint requestId, uint responseId)
        {
            this.provider = provider ?? throw new ArgumentNullException("provider");
            this.options = options ?? throw new ArgumentNullException("options");
            this.requestId = requestId;
            this.responseId = responseId;
        }
        public CanIsoTpTransport(ICanChannel channel, CanChannelOptions options, uint requestId, uint responseId)
        {
            this.options = options ?? throw new ArgumentNullException("options");
            this.channel = channel ?? throw new ArgumentNullException("channel");
            this.requestId = requestId;
            this.responseId = responseId;
            connected = true;
        }
        public CanChannelOptions Options { get { return options; } }
        public void SetAddresses(uint requestIdentifier, uint responseIdentifier)
        {
            requestId = requestIdentifier;
            responseId = responseIdentifier;
        }
        public void ConfigureIsoTp(byte localSeparationTime, byte sendBlockSize, byte forcedRemoteSeparationTime, bool forceRemoteSeparationTime)
        {
            localStMin = localSeparationTime;
            blockSize = sendBlockSize;
            remoteStMin = forcedRemoteSeparationTime;
            modifyRemoteStMin = forceRemoteSeparationTime;
        }
        public void Connect()
        {
            if (connected) return;
            channel = provider.OpenChannel(options);
            channel.Initialize();
            channel.Start();
            connected = true;
        }
        public void SendNoResponse(byte[] payload)
        {
            if (!connected) throw new InvalidOperationException("CAN channel is not open.");
            if (payload == null || payload.Length == 0 || payload.Length > 7)
                throw new ArgumentException("A non-empty single-frame payload is required.", "payload");
            byte[] data = new byte[payload.Length + 1];
            data[0] = (byte)payload.Length;
            Array.Copy(payload, 0, data, 1, payload.Length);
            SendFrames(new List<CanFrame> { Frame(requestId, PadFrame(data)) });
        }
        public IList<CanFrame> ReceiveRaw(int timeoutMilliseconds, int receiveMode)
        {
            if (!connected) throw new InvalidOperationException("CAN channel is not open.");
            if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            ICanTypedReceiveChannel typedChannel = channel as ICanTypedReceiveChannel;
            return typedChannel == null
                ? channel.Receive(timeoutMilliseconds)
                : typedChannel.Receive(timeoutMilliseconds, receiveMode);
        }
        public DiagnosticFrame Request(DiagnosticFrame request, int timeoutMilliseconds)
        {
            return Request(request, timeoutMilliseconds, 0);
        }
        public DiagnosticFrame Request(DiagnosticFrame request, int timeoutMilliseconds,
                                       int responsePendingTimeoutMilliseconds)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (!connected) throw new InvalidOperationException("CAN channel is not open.");
            DrainPendingFrames();
            byte[] payload = request.Payload ?? new byte[0];
            if (payload.Length <= (options.CanFd ? 62 : 7))
            {
                byte[] data;
                if (options.CanFd && payload.Length > 7)
                {
                    data = new byte[payload.Length + 2];
                    data[0] = 0;
                    data[1] = (byte)payload.Length;
                    Array.Copy(payload, 0, data, 2, payload.Length);
                }
                else
                {
                    data = new byte[payload.Length + 1];
                    data[0] = (byte)payload.Length;
                    Array.Copy(payload, 0, data, 1, payload.Length);
                }
                SendFrames(new List<CanFrame> { Frame(requestId, PadFrame(data)) });
            }
            else SendMultiFrame(payload, timeoutMilliseconds);
            byte[] responsePayload = ReceiveIsoTp(timeoutMilliseconds);
            Stopwatch pendingTimer = null;
            while (responsePayload.Length >= 3 && payload.Length > 0 &&
                   responsePayload[0] == 0x7F && responsePayload[1] == payload[0] &&
                   responsePayload[2] == 0x78)
            {
                if (pendingTimer == null) pendingTimer = Stopwatch.StartNew();
                int pendingTimeout = responsePendingTimeoutMilliseconds > 0
                    ? responsePendingTimeoutMilliseconds
                    : (int)Math.Max(timeoutMilliseconds,
                        Math.Min(120000L, Math.Max(0L, (long)timeoutMilliseconds * 4L)));
                int elapsed = (int)pendingTimer.ElapsedMilliseconds;
                if (elapsed >= pendingTimeout)
                    throw new TimeoutException("CAN response pending timeout after NRC 0x78.");
                responsePayload = ReceiveIsoTp(Math.Max(1, pendingTimeout - elapsed));
            }
            return new DiagnosticFrame(request.SourceAddress, request.TargetAddress, responsePayload);
        }
        private void SendMultiFrame(byte[] payload, int timeoutMilliseconds)
        {
            if (payload.Length > 4095) throw new InvalidOperationException("UDS payload exceeds ISO-TP maximum length.");
            int firstLength = options.CanFd ? 62 : 6;
            byte[] first = new byte[firstLength + 2];
            first[0] = (byte)(0x10 | ((payload.Length >> 8) & 0x0F));
            first[1] = (byte)payload.Length;
            Array.Copy(payload, 0, first, 2, firstLength);
            SendFrames(new List<CanFrame> { Frame(requestId, PadFrame(first)) });
            CanFrame flow = WaitForFrame(timeoutMilliseconds);
            if (flow.Data.Length < 3 || (flow.Data[0] & 0xF0) != 0x30) throw new TimeoutException("ISO-TP flow-control frame was not received.");
            if ((flow.Data[0] & 0x0F) == 2) throw new InvalidOperationException("ISO-TP receiver overflow.");
            int negotiatedBlockSize = flow.Data[1], blockCount = 0, offset = firstLength;
            int stMin = DecodeStMin(modifyRemoteStMin ? remoteStMin : flow.Data[2]);
            int localDelay = DecodeStMin(localStMin);
            byte sequence = 1;
            while (offset < payload.Length)
            {
                int count = Math.Min(options.CanFd ? 63 : 7, payload.Length - offset);
                byte[] data = new byte[count + 1];
                data[0] = (byte)(0x20 | (sequence++ & 0x0F));
                Array.Copy(payload, offset, data, 1, count);
                SendFrames(new List<CanFrame> { Frame(requestId, PadFrame(data)) });
                offset += count; blockCount++;
                if (Math.Max(stMin, localDelay) > 0 && offset < payload.Length) System.Threading.Thread.Sleep(Math.Max(stMin, localDelay));
                int activeBlockSize = blockSize > 0 ? blockSize : negotiatedBlockSize;
                if (activeBlockSize > 0 && blockCount >= activeBlockSize && offset < payload.Length) { flow = WaitForFrame(timeoutMilliseconds); blockCount = 0; }
            }
        }
        private static int DecodeStMin(byte value)
        {
            if (value <= 0x7F) return value;
            if (value >= 0xF1 && value <= 0xF9) return 1;
            return 0;
        }
        private byte[] ReceiveIsoTp(int timeoutMilliseconds)
        {
            CanFrame frame = WaitForFrame(timeoutMilliseconds);
            if (frame.Data.Length == 0) throw new TimeoutException("Empty CAN response.");
            // CAN-FD single frames with more than 7 payload bytes use an escape length byte.
            if (options.CanFd && (frame.Data[0] & 0xF0) == 0x00 && (frame.Data[0] & 0x0F) == 0 && frame.Data.Length >= 2)
            {
                int singleFrameLength = frame.Data[1];
                if (singleFrameLength <= 0 || singleFrameLength > frame.Data.Length - 2)
                    throw InvalidResponseFrame(frame, "Invalid CAN-FD single-frame length");
                return Copy(frame.Data, 2, singleFrameLength);
            }
            if ((frame.Data[0] & 0xF0) == 0x00) return Copy(frame.Data, 1, Math.Min(frame.Data[0] & 0x0F, frame.Data.Length - 1));
            if ((frame.Data[0] & 0xF0) != 0x10 || frame.Data.Length < 2) throw InvalidResponseFrame(frame, "Invalid ISO-TP response frame");
            int total = ((frame.Data[0] & 0x0F) << 8) | frame.Data[1];
            byte[] result = new byte[total];
            int copied = Math.Min(options.CanFd ? 62 : 6, total);
            Array.Copy(frame.Data, 2, result, 0, Math.Min(copied, frame.Data.Length - 2));
            SendFrames(new List<CanFrame> { Frame(requestId, PadFrame(new byte[] { 0x30, blockSize, localStMin })) });
            int offset = copied; byte sequence = 1;
            while (offset < total)
            {
                CanFrame next = WaitForFrame(timeoutMilliseconds);
                if (next.Data.Length < 2 || (next.Data[0] & 0xF0) != 0x20 || (next.Data[0] & 0x0F) != (sequence++ & 0x0F)) throw InvalidResponseFrame(next, "Invalid ISO-TP consecutive frame");
                int count = Math.Min(options.CanFd ? 63 : 7, total - offset);
                Array.Copy(next.Data, 1, result, offset, Math.Min(count, next.Data.Length - 1)); offset += count;
            }
            return result;
        }
        private CanFrame WaitForFrame(int timeoutMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            var observed = new List<string>();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                int remaining = Math.Max(1, timeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds);
                IList<CanFrame> frames = channel.Receive(Math.Min(50, remaining));
                foreach (CanFrame frame in frames)
                {
                    if (!frame.Remote && frame.Identifier == responseId) return frame;
                    if (observed.Count < 8)
                        observed.Add("0x" + frame.Identifier.ToString("X") + "/" + (frame.Data == null ? 0 : frame.Data.Length) + ":" + (frame.Data == null ? "" : BitConverter.ToString(frame.Data).Replace("-", " ")));
                }
                if (frames.Count == 0) System.Threading.Thread.Sleep(Math.Min(5, remaining));
            }
            throw new TimeoutException("CAN response timeout. expected=0x" + responseId.ToString("X") + ", observed=" + (observed.Count == 0 ? "<none>" : string.Join("|", observed.ToArray())) + ".");
        }
        private void DrainPendingFrames()
        {
            for (int pass = 0; pass < 32; pass++)
            {
                IList<CanFrame> frames = channel.Receive(0);
                if (frames == null || frames.Count == 0) return;
            }
        }
        private void SendFrames(IList<CanFrame> frames)
        {
            int sent = channel.Send(frames);
            if (sent < frames.Count)
                throw new InvalidOperationException("CAN transmit incomplete. expected=" + frames.Count + ", sent=" + sent + ".");
        }
        private static InvalidOperationException InvalidResponseFrame(CanFrame frame, string reason)
        {
            string data = frame.Data == null ? "" : BitConverter.ToString(frame.Data).Replace("-", " ");
            return new InvalidOperationException(reason + ". id=0x" + frame.Identifier.ToString("X") + ", length=" + (frame.Data == null ? 0 : frame.Data.Length) + ", data=" + data);
        }
        public void Dispose() { channel = null; connected = false; }
        private CanFrame Frame(uint id, byte[] data) { return new CanFrame { Identifier = id, Data = data, BitRateSwitch = options.BitRateSwitch }; }
        private byte[] PadFrame(byte[] data)
        {
            int frameLength = options.FrameLength;
            int nativeMaximum = options.CanFd ? 64 : 8;
            if (frameLength <= 0)
            {
                if (!options.CanFd) return data;
                frameLength = AutomaticCanFdLength(data.Length);
            }
            else if (options.CanFd && !IsValidCanFdLength(frameLength))
            {
                throw new InvalidOperationException("CAN-FD frame length must be one of 0-8, 12, 16, 20, 24, 32, 48, or 64.");
            }
            if (options.CanFd && frameLength < data.Length)
                frameLength = AutomaticCanFdLength(data.Length);
            if (frameLength < data.Length || frameLength > nativeMaximum)
                throw new InvalidOperationException("CAN frame length must be between " + data.Length + " and " + nativeMaximum + ".");
            if (frameLength == data.Length) return data;
            byte[] padded = new byte[frameLength];
            Array.Copy(data, padded, data.Length);
            for (int i = data.Length; i < padded.Length; ++i) padded[i] = options.PaddingByte;
            return padded;
        }
        private static int AutomaticCanFdLength(int dataLength)
        {
            if (dataLength <= 8) return dataLength;
            int[] lengths = { 12, 16, 20, 24, 32, 48, 64 };
            for (int i = 0; i < lengths.Length; ++i)
                if (dataLength <= lengths[i]) return lengths[i];
            throw new InvalidOperationException("CAN-FD frame data exceeds 64 bytes: " + dataLength + ".");
        }
        private static bool IsValidCanFdLength(int length)
        {
            return length >= 0 && length <= 8 ||
                   length == 12 || length == 16 || length == 20 || length == 24 ||
                   length == 32 || length == 48 || length == 64;
        }
        private static byte[] Copy(byte[] source, int offset, int count) { byte[] result = new byte[count]; Array.Copy(source, offset, result, 0, count); return result; }
    }
}
