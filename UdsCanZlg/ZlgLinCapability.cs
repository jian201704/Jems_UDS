using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Jabil.Uds.Can;

namespace ZLGAPI
{
    public sealed class ZlgLinCapability : ILinCapability
    {
        private readonly IntPtr deviceHandle;
        private readonly int channelIndex;
        private IntPtr channelHandle;
        private bool initialized;
        private bool started;

        public ZlgLinCapability(IntPtr deviceHandle, int channelIndex)
        {
            if (deviceHandle == IntPtr.Zero) throw new ArgumentException("deviceHandle");
            this.deviceHandle = deviceHandle;
            this.channelIndex = channelIndex;
        }

        public void Initialize(bool master, byte checksumMode, byte maxLength, uint baudrate)
        {
            if (initialized) return;
            var config = new ZlgNativeApi.LinInitConfig { Mode = (byte)(master ? 1 : 0), ChecksumMode = checksumMode, MaxLength = maxLength, Baudrate = baudrate };
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.LinInitConfig)));
                Marshal.StructureToPtr(config, ptr, false);
                channelHandle = ZlgNativeApi.ZCAN_InitLIN(deviceHandle, (uint)channelIndex, ptr);
            }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
            if (channelHandle == IntPtr.Zero) throw new InvalidOperationException("ZLG LIN initialization failed.");
            initialized = true;
        }

        public void Start()
        {
            if (!initialized) throw new InvalidOperationException("LIN is not initialized.");
            if (started) return;
            if (ZlgNativeApi.ZCAN_StartLIN(channelHandle) != 1) throw new InvalidOperationException("ZLG LIN start failed.");
            started = true;
        }

        public void Reset()
        {
            if (channelHandle != IntPtr.Zero) ZlgNativeApi.ZCAN_ResetLIN(channelHandle);
            started = false;
        }

        public int Transmit(IList<LinMessage> messages)
        {
            RequireStarted();
            if (messages == null || messages.Count == 0) return 0;
            int size = Marshal.SizeOf(typeof(ZlgNativeApi.LinMessage));
            IntPtr buffer = Marshal.AllocHGlobal(size * messages.Count);
            try
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    LinMessage message = messages[i];
                    if (message == null || message.Data == null || message.Data.Length > 46) throw new ArgumentException("Invalid LIN message.");
                    var native = new ZlgNativeApi.LinMessage { Channel = (byte)channelIndex, DataType = 0, Data = new byte[46] };
                    native.Data[0] = (byte)(message.Id & 0x3F);
                    Array.Copy(message.Data, 0, native.Data, 1, message.Data.Length);
                    Marshal.StructureToPtr(native, buffer + size * i, false);
                }
                return (int)ZlgNativeApi.ZCAN_TransmitLIN(channelHandle, buffer, (uint)messages.Count);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public int GetReceiveCount()
        {
            RequireStarted();
            return (int)Math.Min(Int32.MaxValue, ZlgNativeApi.ZCAN_GetLINReceiveNum(channelHandle));
        }

        public IList<LinMessage> Receive(int timeoutMilliseconds, int maxCount)
        {
            RequireStarted();
            int count = Math.Max(1, Math.Min(256, maxCount));
            int size = Marshal.SizeOf(typeof(ZlgNativeApi.LinReceive));
            IntPtr buffer = Marshal.AllocHGlobal(size * count);
            var result = new List<LinMessage>();
            try
            {
                uint received = ZlgNativeApi.ZCAN_ReceiveLIN(channelHandle, buffer, (uint)count, timeoutMilliseconds);
                for (int i = 0; i < received; i++)
                {
                    var native = (ZlgNativeApi.LinMessage)Marshal.PtrToStructure(buffer + size * i, typeof(ZlgNativeApi.LinMessage));
                    if (native.DataType != 0) continue;
                    int length = native.Data == null ? 0 : Math.Max(0, native.Data.Length - 1);
                    byte[] data = new byte[length];
                    if (length > 0) Array.Copy(native.Data, 1, data, 0, length);
                    result.Add(new LinMessage { Id = native.Data == null ? (byte)0 : native.Data[0], Data = data });
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Publish(byte id, byte[] data, byte checksumMode)
        {
            RequireStarted();
            if (data == null || data.Length > 8) throw new ArgumentException("Invalid LIN publish data.");
            var native = new ZlgNativeApi.LinTransmit { Id = (byte)(id & 0x3F), Length = (byte)data.Length, Data = new byte[8], ChecksumMode = checksumMode, Reserved = new byte[5] };
            Array.Copy(data, native.Data, data.Length);
            CallConfig(ZlgNativeApi.ZCAN_SetLINPublish, native, Marshal.SizeOf(typeof(ZlgNativeApi.LinTransmit)));
        }

        public void Subscribe(byte id, byte length, byte checksumMode)
        {
            RequireStarted();
            var native = new ZlgNativeApi.LinSubscribe { Id = (byte)(id & 0x3F), Length = length, ChecksumMode = checksumMode, Reserved = new byte[5] };
            CallConfig(ZlgNativeApi.ZCAN_SetLINSubscribe, native, Marshal.SizeOf(typeof(ZlgNativeApi.LinSubscribe)));
        }

        public void WakeUp()
        {
            RequireStarted();
            if (ZlgNativeApi.ZCAN_WakeUpLIN(channelHandle) != 1) throw new InvalidOperationException("ZLG LIN wake-up failed.");
        }

        private delegate uint ConfigDelegate(IntPtr handle, IntPtr data, uint count);
        private void CallConfig(ConfigDelegate method, object value, int size)
        {
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(value, ptr, false); if (method(channelHandle, ptr, 1) != 1) throw new InvalidOperationException("ZLG LIN configuration failed."); }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        private void RequireStarted() { if (!started) throw new InvalidOperationException("LIN is not started."); }
        public void Dispose() { Reset(); channelHandle = IntPtr.Zero; initialized = false; }
    }
}
