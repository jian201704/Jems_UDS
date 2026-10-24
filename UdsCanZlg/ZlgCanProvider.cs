using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Jabil.Uds.Can;

namespace ZLGAPI
{
    public sealed class ZlgCanDeviceProvider : IConfigurableCanDeviceProvider, ICanDeviceLifecycleProvider, ICanDeviceSelector, ICanDeviceDiagnostics, ICanCustomBitrateCapability, ICanAutoSendCapability, IZlgClosedUdsProvider, IZcanHardwareUdsProvider, ILinProvider, IZNetProvider, IDisposable
    {
        private IntPtr deviceHandle;
        private uint deviceType;
        private uint deviceIndex;
        private readonly Dictionary<string, ZlgCanChannel> channels = new Dictionary<string, ZlgCanChannel>();
        private readonly Dictionary<int, ZlgLinCapability> linChannels = new Dictionary<int, ZlgLinCapability>();
        private ZlgNetCapability znet;

        public ZlgCanDeviceProvider() { }
        public ZlgCanDeviceProvider(IntPtr handle)
        {
            if (handle == IntPtr.Zero) throw new ArgumentException("handle");
            deviceHandle = handle;
        }

        public void Configure(string deviceAddress, string deviceSerialNumber)
        {
            deviceType = ZlgNativeApi.ZCAN_USBCAN2;
            deviceIndex = 0;
            string[] parts = (deviceAddress ?? "").Trim().Split(',', ':');
            uint parsed;
            if (parts.Length > 0 && UInt32.TryParse(parts[0].Trim(), out parsed)) deviceType = parsed;
            if (parts.Length > 1 && UInt32.TryParse(parts[1].Trim(), out parsed)) deviceIndex = parsed;
            if (!string.IsNullOrWhiteSpace(deviceSerialNumber))
                throw new NotSupportedException("The deployed ZLG zlgcan.dll does not export ZCAN_OpenDeviceBySN. This adapter supports device type/index only; use an empty serial/barcode parameter or provide a verified ZLG SDK with a documented device-selection API.");
            deviceHandle = ZlgNativeApi.ZCAN_OpenDevice(deviceType, deviceIndex, 0);
            if (deviceHandle == IntPtr.Zero)
            {
                string library = ZlgNativeApi.LoadedLibraryPath ?? "not loaded";
                throw new InvalidOperationException("ZLG CAN device open failed. type=" + deviceType + ", index=" + deviceIndex + ", serial=" + (deviceSerialNumber ?? "") + ", native=" + library + ". Check that the ZLG device driver is installed, the device is connected, and the type/index/serial match the hardware (41=USBCANFD_200U, 42=USBCANFD_100U, 4=USBCAN2).");
            }
        }

        public void OpenDevice(string deviceAddress, string deviceSerialNumber)
        {
            if (string.IsNullOrWhiteSpace(deviceSerialNumber))
                Configure(deviceAddress, "");
            else
                OpenDeviceByIdentifier(deviceAddress, deviceSerialNumber);
        }

        public void OpenDeviceByIdentifier(string deviceAddress, string deviceIdentifier)
        {
            uint requestedType = ParseDeviceType(deviceAddress);
            uint selectedIndex;
            IntPtr selectedHandle = FindDeviceBySerial(requestedType, deviceIdentifier, 48, out selectedIndex);
            if (selectedHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "ZLG CAN device with type=" + requestedType + " and serial/barcode='" + (deviceIdentifier ?? "") +
                    "' was not found. ZCAN_OpenDevice returned no usable handle for indexes 0..47. " +
                    "Loaded native library: " + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded") +
                    ". Verify the adapter DLL matches the hardware SDK, the process is x64, the device type is correct, " +
                    "and no official ZLG tool currently owns the device.");
            }
            deviceType = requestedType;
            deviceIndex = selectedIndex;
            deviceHandle = selectedHandle;
        }

        private static uint ParseDeviceType(string deviceAddress)
        {
            uint deviceType = ZlgNativeApi.ZCAN_USBCAN2;
            string[] parts = (deviceAddress ?? "").Trim().Split(',', ':');
            uint parsed;
            if (parts.Length > 0 && UInt32.TryParse(parts[0].Trim(), out parsed)) deviceType = parsed;
            return deviceType;
        }

        private static IntPtr FindDeviceBySerial(uint requestedType, string serial, int maxDevices, out uint selectedIndex)
        {
            selectedIndex = 0;
            string requestedSerial = (serial ?? "").Trim();
            for (uint index = 0; index < (uint)Math.Max(1, maxDevices); index++)
            {
                IntPtr candidate = ZlgNativeApi.ZCAN_OpenDevice(requestedType, index, 0);
                if (candidate == IntPtr.Zero) continue;
                IntPtr infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.ZcanDeviceInfo)));
                bool keepOpen = false;
                try
                {
                    if (ZlgNativeApi.ZCAN_GetDeviceInf(candidate, infoPtr) != 1)
                        throw new InvalidOperationException("ZLG device information read failed for device type=" + requestedType + ", index=" + index + ".");
                    ZlgNativeApi.ZcanDeviceInfo info = (ZlgNativeApi.ZcanDeviceInfo)Marshal.PtrToStructure(infoPtr, typeof(ZlgNativeApi.ZcanDeviceInfo));
                    string candidateSerial = ZlgNativeApi.DecodeDeviceText(info.SerialNumber);
                    if (string.Equals(candidateSerial, requestedSerial, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = index;
                        keepOpen = true;
                        return candidate;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(infoPtr);
                    if (!keepOpen) ZlgNativeApi.ZCAN_CloseDevice(candidate);
                }
            }
            return IntPtr.Zero;
        }

        public ICanChannel OpenChannel(CanChannelOptions options)
        {
            if (deviceHandle == IntPtr.Zero) throw new InvalidOperationException("ZLG CAN device is not configured.");
            if (options == null) throw new ArgumentNullException("options");
            if (options.CanFd && deviceType == ZlgNativeApi.ZCAN_USBCAN2)
                throw new InvalidOperationException("CAN-FD requires a CAN-FD device type. Current device type is 4 (USBCAN2). For ZCAN_USBCANFD_200U initialize with device address '41,0'.");
            string key = options.ChannelIndex + ":" + (options.CanFd ? "fd" : "can") + ":" + options.NominalBitrate + ":" + options.DataBitrate + ":" + options.CanFdStandard + ":" + options.CanFdMode + ":" + (options.BitRateSwitch ? "brs" : "nobrs");
            ZlgCanChannel existing;
            if (channels.TryGetValue(key, out existing)) return existing;
            var channel = new ZlgCanChannel(deviceHandle, options);
            channels.Add(key, channel);
            return channel;
        }

        public IZlgClosedUdsCapability OpenZlgClosedUds(CanChannelOptions options)
        {
            ICanChannel channel = OpenChannel(options);
            channel.Initialize();
            channel.Start();
            return new ZlgClosedUds(channel, options);
        }

        public HardwareUdsResponse HardwareUdsRequest(CanChannelOptions options, uint requestId, uint sourceAddress, uint targetAddress, byte[] payload, uint frameType, byte fillByte, byte fillMode, bool suppressResponse)
        {
            if (deviceHandle == IntPtr.Zero) throw new InvalidOperationException("ZLG CAN device is not configured.");
            if (options == null) throw new ArgumentNullException("options");
            if (payload == null || payload.Length == 0) throw new ArgumentException("UDS payload is empty.", "payload");
            IntPtr requestPtr = IntPtr.Zero, responsePtr = IntPtr.Zero, dataPtr = IntPtr.Zero;
            try
            {
                byte[] parameters = new byte[payload.Length - 1];
                if (parameters.Length > 0) { dataPtr = Marshal.AllocHGlobal(parameters.Length); Array.Copy(payload, 1, parameters, 0, parameters.Length); Marshal.Copy(parameters, 0, dataPtr, parameters.Length); }
                var request = new ZlgNativeApi.HardwareUdsRequest
                {
                    RequestId = requestId, Channel = (byte)options.ChannelIndex, FrameType = (byte)frameType,
                    Reserved0 = new byte[2], Source = sourceAddress, Target = targetAddress,
                    SuppressResponse = (byte)(suppressResponse ? 1 : 0), Sid = payload[0], Reserved1 = new byte[6],
                    Session = new ZlgNativeApi.HardwareUdsSessionParam { Timeout = 2000, EnhancedTimeout = 5000, Reserved = new byte[7] },
                    Transport = new ZlgNativeApi.HardwareUdsTransportParam { Version = (byte)(frameType == 0 ? 0 : 1), MaxDataLength = (byte)(frameType == 0 ? 8 : 64), FillByte = fillByte, FillMode = fillMode, FlowControlTimeout = 1000, Reserved = new byte[3] },
                    Data = dataPtr, DataLength = (uint)parameters.Length
                };
                requestPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.HardwareUdsRequest)));
                responsePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.HardwareUdsResponse)));
                IntPtr dataBuffer = Marshal.AllocHGlobal(5000);
                try
                {
                    Marshal.StructureToPtr(request, requestPtr, false);
                    uint result = ZlgNativeApi.ZCAN_UDS_Request(deviceHandle, requestPtr, responsePtr, dataBuffer, 5000);
                    if (result != 1) throw new InvalidOperationException("ZCAN_UDS_Request returned " + result + ".");
                    var response = (ZlgNativeApi.HardwareUdsResponse)Marshal.PtrToStructure(responsePtr, typeof(ZlgNativeApi.HardwareUdsResponse));
                    byte[] responsePayload;
                    if (response.Status == 0 && response.Type == 1)
                    {
                        int length = response.Raw == null || response.Raw.Length < 5 ? 0 : BitConverter.ToInt32(new byte[] { response.Raw[1], response.Raw[2], response.Raw[3], response.Raw[4] }, 0);
                        responsePayload = new byte[length + 1]; responsePayload[0] = response.Raw[0];
                        if (length > 0) Marshal.Copy(dataBuffer, responsePayload, 1, length);
                    }
                    else responsePayload = response.Raw == null ? new byte[0] : (byte[])response.Raw.Clone();
                    return new HardwareUdsResponse { Status = response.Status, Type = response.Type, Payload = responsePayload };
                }
                finally { Marshal.FreeHGlobal(dataBuffer); }
            }
            finally
            {
                if (responsePtr != IntPtr.Zero) Marshal.FreeHGlobal(responsePtr);
                if (requestPtr != IntPtr.Zero) Marshal.FreeHGlobal(requestPtr);
                if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            }
        }

        public ILinCapability OpenLin(int channelIndex)
        {
            if (deviceHandle == IntPtr.Zero) throw new InvalidOperationException("ZLG CAN device is not configured.");
            ZlgLinCapability existing;
            if (linChannels.TryGetValue(channelIndex, out existing)) return existing;
            var capability = new ZlgLinCapability(deviceHandle, channelIndex);
            linChannels[channelIndex] = capability;
            return capability;
        }

        public IZNetCapability OpenZNet()
        {
            if (znet == null) znet = new ZlgNetCapability();
            return znet;
        }

        public string GetDeviceInfo()
        {
            return "type=" + deviceType + ",index=" + deviceIndex + ",native=" + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded");
        }

        public bool SetValue(string path, string value)
        {
            if (deviceHandle == IntPtr.Zero) throw new InvalidOperationException("ZLG CAN device is not configured.");
            return ZlgNativeApi.ZCAN_SetValue(deviceHandle, path ?? "", value ?? "") == 1;
        }

        public bool SetCustomBitrate(int channelIndex, string bitrate)
        {
            if (string.IsNullOrWhiteSpace(bitrate)) throw new ArgumentException("Custom bitrate cannot be empty.", "bitrate");
            return ZlgNativeApi.ZCAN_SetValue(deviceHandle, channelIndex + "/baud_rate_custom", bitrate) == 1;
        }

        public bool SetAutoSend(int channelIndex, ushort frameIndex, bool canFd, bool enabled, CanFrame frame, uint interval)
        {
            if (deviceHandle == IntPtr.Zero) throw new InvalidOperationException("ZLG CAN device is not configured.");
            if (frame == null) throw new ArgumentNullException("frame");
            IntPtr ptr = IntPtr.Zero;
            try
            {
                string path = channelIndex + (canFd ? "/auto_send_canfd" : "/auto_send");
                if (canFd)
                {
                    var native = new ZlgNativeApi.AutoTransmitFdData
                    {
                        Enable = (ushort)(enabled ? 1 : 0), Index = frameIndex, Interval = interval,
                        Obj = new ZlgNativeApi.TransmitFdData
                        {
                            TransmitType = 0,
                            Frame = new ZlgNativeApi.CanFdFrame { Id = ToNativeId(frame), Length = (byte)Math.Min(64, frame.Data == null ? 0 : frame.Data.Length), Flags = frame.BitRateSwitch ? (byte)1 : (byte)0, Data = new byte[64] }
                        }
                    };
                    if (frame.Data != null) Array.Copy(frame.Data, native.Obj.Frame.Data, native.Obj.Frame.Length);
                    ptr = Marshal.AllocHGlobal(Marshal.SizeOf(native));
                    Marshal.StructureToPtr(native, ptr, false);
                }
                else
                {
                    var native = new ZlgNativeApi.AutoTransmitData
                    {
                        Enable = (ushort)(enabled ? 1 : 0), Index = frameIndex, Interval = interval,
                        Obj = new ZlgNativeApi.TransmitData
                        {
                            TransmitType = 0,
                            Frame = new ZlgNativeApi.CanFrame { Id = ToNativeId(frame), Length = (byte)Math.Min(8, frame.Data == null ? 0 : frame.Data.Length), Data = new byte[8] }
                        }
                    };
                    if (frame.Data != null) Array.Copy(frame.Data, native.Obj.Frame.Data, native.Obj.Frame.Length);
                    ptr = Marshal.AllocHGlobal(Marshal.SizeOf(native));
                    Marshal.StructureToPtr(native, ptr, false);
                }
                return ZlgNativeApi.ZCAN_SetValue(deviceHandle, path, ptr) == 1;
            }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
        }

        public bool ApplyAutoSend(int channelIndex)
        {
            return SetValue(channelIndex + "/apply_auto_send", "0");
        }

        public bool ClearAutoSend(int channelIndex)
        {
            return SetValue(channelIndex + "/clear_auto_send", "0");
        }

        private static uint ToNativeId(CanFrame frame)
        {
            return frame.Identifier | (frame.Extended ? ZlgNativeApi.CAN_EFF_FLAG : 0) | (frame.Remote ? ZlgNativeApi.CAN_RTR_FLAG : 0);
        }

        public void Dispose()
        {
            foreach (ZlgCanChannel channel in channels.Values)
            {
                try { channel.Dispose(); } catch (Exception) { }
            }
            channels.Clear();
            foreach (ZlgLinCapability lin in linChannels.Values)
            {
                try { lin.Dispose(); } catch (Exception) { }
            }
            linChannels.Clear();
            znet = null;
            if (deviceHandle != IntPtr.Zero)
            {
                ZlgNativeApi.ZCAN_CloseDevice(deviceHandle);
                deviceHandle = IntPtr.Zero;
            }
        }
    }

    public sealed class ZlgCanChannel : ICanChannel, ICanTypedReceiveChannel, ICanReceiveCountChannel, ICanResetChannel
    {
        private readonly IntPtr deviceHandle;
        private readonly CanChannelOptions options;
        private IntPtr channelHandle;
        private bool initialized;
        private bool started;

        public ZlgCanChannel(IntPtr deviceHandle, CanChannelOptions options)
        {
            this.deviceHandle = deviceHandle;
            this.options = options ?? throw new ArgumentNullException("options");
        }

        public void Initialize()
        {
            if (initialized) return;
            if (options.CanFd)
            {
                string standardPath = options.ChannelIndex + "/canfd_standard";
                if (ZlgNativeApi.ZCAN_SetValue(deviceHandle, standardPath, options.CanFdStandard.ToString()) != 1)
                    throw new InvalidOperationException("ZLG CAN-FD standard configuration failed: " + options.CanFdStandard + ", path=" + standardPath + ", native=" + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded") + ".");
                if (string.IsNullOrWhiteSpace(options.CustomBitrate))
                {
                    string arbitrationPath = options.ChannelIndex + "/canfd_abit_baud_rate";
                    if (options.NominalBitrate > 0 && ZlgNativeApi.ZCAN_SetValue(deviceHandle, arbitrationPath, options.NominalBitrate.ToString()) != 1)
                        throw new InvalidOperationException("ZLG CAN-FD arbitration bitrate configuration failed: " + options.NominalBitrate + " bit/s, path=" + arbitrationPath + ", native=" + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded") + ".");
                    string dataPath = options.ChannelIndex + "/canfd_dbit_baud_rate";
                    if (options.DataBitrate > 0 && ZlgNativeApi.ZCAN_SetValue(deviceHandle, dataPath, options.DataBitrate.ToString()) != 1)
                        throw new InvalidOperationException("ZLG CAN-FD data bitrate configuration failed: " + options.DataBitrate + " bit/s, path=" + dataPath + ", native=" + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded") + ".");
                }
            }
            if (!string.IsNullOrWhiteSpace(options.CustomBitrate))
            {
                string customPath = options.ChannelIndex + "/baud_rate_custom";
                if (ZlgNativeApi.ZCAN_SetValue(deviceHandle, customPath, options.CustomBitrate) != 1)
                    throw new InvalidOperationException("ZLG custom bitrate configuration failed: " + options.CustomBitrate + ".");
            }
            var config = new ZlgNativeApi.ChannelInitConfig
            {
                CanType = options.CanFd ? 1U : 0U,
                Config = new ZlgNativeApi.ChannelInitUnion
                {
                    Can = new ZlgNativeApi.CanInitConfig(),
                    CanFd = new ZlgNativeApi.CanFdInitConfig { Mode = (byte)options.CanFdMode }
                }
            };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(config));
            try
            {
                Marshal.StructureToPtr(config, ptr, false);
                channelHandle = ZlgNativeApi.ZCAN_InitCAN(deviceHandle, (uint)options.ChannelIndex, ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            if (channelHandle == IntPtr.Zero)
                throw new InvalidOperationException("ZLG CAN channel initialization failed: deviceType=" + options.DeviceAddress + ", channel=" + options.ChannelIndex + ", canFd=" + options.CanFd + ", native=" + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded") + ".");
            initialized = true;
        }

        public void Start()
        {
            if (started) return;
            if (!initialized) throw new InvalidOperationException("ZLG CAN channel is not initialized. Call Initialize first.");
            if (ZlgNativeApi.ZCAN_StartCAN(channelHandle) != 1)
                throw new InvalidOperationException("ZLG CAN channel start failed: channel=" + options.ChannelIndex + ", native=" + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded") + ".");
            started = true;
        }

        public int Send(IList<CanFrame> frames)
        {
            if (!started) throw new InvalidOperationException("ZLG CAN channel is not started. Call Initialize and Start first.");
            if (frames == null || frames.Count == 0) return 0;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] == null) throw new ArgumentException("CAN frame " + i + " is null.");
                int length = frames[i].Data == null ? 0 : frames[i].Data.Length;
                int maxLength = options.CanFd ? 64 : 8;
                if (length > maxLength) throw new ArgumentException("CAN frame " + i + " payload length " + length + " exceeds " + maxLength + ".");
            }
            int size = options.CanFd ? Marshal.SizeOf(typeof(ZlgNativeApi.TransmitFdData)) : Marshal.SizeOf(typeof(ZlgNativeApi.TransmitData));
            IntPtr buffer = Marshal.AllocHGlobal(size * frames.Count);
            try
            {
                for (int i = 0; i < frames.Count; i++)
                {
                    CanFrame source = frames[i];
                    if (options.CanFd)
                    {
                        var frame = new ZlgNativeApi.CanFdFrame { Id = AddFlags(source), Length = (byte)Math.Min(64, source.Data == null ? 0 : source.Data.Length), Flags = source.BitRateSwitch ? (byte)1 : (byte)0, Data = new byte[64] };
                        if (source.Data != null) Array.Copy(source.Data, frame.Data, frame.Length);
                        Marshal.StructureToPtr(new ZlgNativeApi.TransmitFdData { Frame = frame }, buffer + size * i, false);
                    }
                    else
                    {
                        var frame = new ZlgNativeApi.CanFrame { Id = AddFlags(source), Length = (byte)Math.Min(8, source.Data == null ? 0 : source.Data.Length), Data = new byte[8] };
                        if (source.Data != null) Array.Copy(source.Data, frame.Data, frame.Length);
                        Marshal.StructureToPtr(new ZlgNativeApi.TransmitData { Frame = frame }, buffer + size * i, false);
                    }
                }
                uint result = options.CanFd ? ZlgNativeApi.ZCAN_TransmitFD(channelHandle, buffer, (uint)frames.Count) : ZlgNativeApi.ZCAN_Transmit(channelHandle, buffer, (uint)frames.Count);
                if (result == 0)
                {
                    CanFrame first = frames[0];
                    int length = first.Data == null ? 0 : first.Data.Length;
                    throw new InvalidOperationException("ZLG transmit returned 0. fd=" + (options.CanFd ? "1" : "0") +
                        ", channel=" + options.ChannelIndex + ", handle=0x" + channelHandle.ToInt64().ToString("X") +
                        ", count=" + frames.Count + ", structSize=" + size + ", id=0x" + first.Identifier.ToString("X") +
                        ", length=" + length + ", brs=" + (first.BitRateSwitch ? "1" : "0") +
                        ", configuredAbit=" + ZlgNativeApi.GetValue(deviceHandle, options.ChannelIndex + "/canfd_abit_baud_rate") +
                        ", configuredDbit=" + ZlgNativeApi.GetValue(deviceHandle, options.ChannelIndex + "/canfd_dbit_baud_rate") +
                        ", configuredStandard=" + ZlgNativeApi.GetValue(deviceHandle, options.ChannelIndex + "/canfd_standard") +
                        ", configuredCustom=" + ZlgNativeApi.GetValue(deviceHandle, options.ChannelIndex + "/baud_rate_custom") +
                        ", native=" + (ZlgNativeApi.LoadedLibraryPath ?? "not loaded") + ".");
                }
                return (int)result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public IList<CanFrame> Receive(int timeoutMilliseconds)
        {
            return Receive(timeoutMilliseconds, options.CanFd ? 2 : 1);
        }

        public IList<CanFrame> Receive(int timeoutMilliseconds, int receiveMode)
        {
            if (!started) throw new InvalidOperationException("ZLG CAN channel is not started. Call Initialize and Start first.");
            if (receiveMode < 0 || receiveMode > 3) throw new ArgumentOutOfRangeException("receiveMode", "Use 0=default, 1=CAN, 2=CAN-FD, 3=both.");
            if (receiveMode == 0) receiveMode = options.CanFd ? 2 : 1;
            var result = new List<CanFrame>();
            if ((receiveMode & 1) != 0) ReceiveType(result, timeoutMilliseconds, false);
            if ((receiveMode & 2) != 0) ReceiveType(result, timeoutMilliseconds, true);
            return result;
        }

        public int GetReceiveCount(bool canFd)
        {
            if (!started) throw new InvalidOperationException("ZLG CAN channel is not started. Call Initialize and Start first.");
            return (int)Math.Min(Int32.MaxValue, ZlgNativeApi.ZCAN_GetReceiveNum(channelHandle, canFd ? (byte)1 : (byte)0));
        }

        public void Reset()
        {
            if (channelHandle == IntPtr.Zero) return;
            if (ZlgNativeApi.ZCAN_ResetCAN(channelHandle) != 1)
                throw new InvalidOperationException("ZLG CAN channel reset failed.");
            started = false;
        }

        private void ReceiveType(List<CanFrame> result, int timeoutMilliseconds, bool canFd)
        {
            byte type = canFd ? (byte)1 : (byte)0;
            uint count = Math.Min(256, ZlgNativeApi.ZCAN_GetReceiveNum(channelHandle, type));
            if (count == 0) return;
            int size = canFd ? Marshal.SizeOf(typeof(ZlgNativeApi.ReceiveFdData)) : Marshal.SizeOf(typeof(ZlgNativeApi.ReceiveData));
            IntPtr buffer = Marshal.AllocHGlobal(size * (int)count);
            try
            {
                uint received = canFd ? ZlgNativeApi.ZCAN_ReceiveFD(channelHandle, buffer, count, timeoutMilliseconds) : ZlgNativeApi.ZCAN_Receive(channelHandle, buffer, count, timeoutMilliseconds);
                for (int i = 0; i < received; i++)
                {
                    if (canFd)
                    {
                        var item = Marshal.PtrToStructure<ZlgNativeApi.ReceiveFdData>(buffer + size * i);
                        result.Add(ToFrame(item.Frame.Id, item.Frame.Data, item.Frame.Length, item.Frame.Flags != 0));
                    }
                    else
                    {
                        var item = Marshal.PtrToStructure<ZlgNativeApi.ReceiveData>(buffer + size * i);
                        result.Add(ToFrame(item.Frame.Id, item.Frame.Data, item.Frame.Length, false));
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose()
        {
            if (channelHandle != IntPtr.Zero) ZlgNativeApi.ZCAN_ResetCAN(channelHandle);
            channelHandle = IntPtr.Zero;
            initialized = false;
            started = false;
        }

        private static uint AddFlags(CanFrame frame) { return frame.Identifier | (frame.Extended ? ZlgNativeApi.CAN_EFF_FLAG : 0) | (frame.Remote ? ZlgNativeApi.CAN_RTR_FLAG : 0); }
        private static CanFrame ToFrame(uint id, byte[] data, byte length, bool brs)
        {
            int count = Math.Min(length, (byte)(data == null ? 0 : data.Length));
            var frame = new CanFrame { Identifier = id & ZlgNativeApi.CAN_ID_FLAG, Extended = (id & ZlgNativeApi.CAN_EFF_FLAG) != 0, Remote = (id & ZlgNativeApi.CAN_RTR_FLAG) != 0, BitRateSwitch = brs, Data = new byte[count] };
            if (count > 0) Array.Copy(data, frame.Data, count);
            return frame;
        }
    }
}
