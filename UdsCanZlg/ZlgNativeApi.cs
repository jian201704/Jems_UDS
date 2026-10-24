using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ZLGAPI
{
    internal static class ZlgNativeApi
    {
        internal static string LoadedLibraryPath { get; private set; }
        internal static int LoadError { get; private set; }

        static ZlgNativeApi()
        {
            string architecture = Environment.Is64BitProcess ? "zlgcan_x64" : "zlgcan_x86";
            string fileName = Path.Combine(architecture, "zlgcan.dll");
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "zlgcan", "zlgcan.dll"),
                Path.Combine(baseDirectory, "zlgcan", fileName),
                Path.Combine(baseDirectory, "Modules", "UserLibraries", "zlgcan", fileName),
                Path.Combine(Path.GetDirectoryName(typeof(ZlgNativeApi).Assembly.Location) ?? baseDirectory, fileName)
            };
            LoadError = 0;
            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                IntPtr handle = LoadLibrary(candidate);
                if (handle != IntPtr.Zero)
                {
                    LoadedLibraryPath = candidate;
                    return;
                }
                LoadError = Marshal.GetLastWin32Error();
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        internal static void EnsureZNetLibraryLoaded()
        {
            string architecture = Environment.Is64BitProcess ? "zlgcan_x64" : "zlgcan_x86";
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "UserLibraries", "zlgcan", architecture, "ZNetConfig.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zlgcan", architecture, "ZNetConfig.dll"),
                Path.Combine(Path.GetDirectoryName(typeof(ZlgNativeApi).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory, architecture, "ZNetConfig.dll")
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate) && LoadLibrary(candidate) != IntPtr.Zero) return;
            throw new DllNotFoundException("ZNetConfig.dll was not found beside the ZLG native CAN library.");
        }

        internal static void EnsureUdsLibraryLoaded()
        {
            string architecture = Environment.Is64BitProcess ? "zlgcan_x64" : "zlgcan_x86";
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "UserLibraries", "zlgcan", architecture, "zuds.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zlgcan", architecture, "zuds.dll"),
                Path.Combine(Path.GetDirectoryName(typeof(ZlgNativeApi).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory, architecture, "zuds.dll")
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate) && LoadLibrary(candidate) != IntPtr.Zero) return;
            throw new DllNotFoundException("zuds.dll was not found beside the ZLG native CAN library.");
        }

        internal const uint ZCAN_USBCAN2 = 4;
        internal const uint CAN_EFF_FLAG = 0x80000000U;
        internal const uint CAN_RTR_FLAG = 0x40000000U;
        internal const uint CAN_ID_FLAG = 0x1FFFFFFFU;

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr ZCAN_OpenDevice(uint deviceType, uint deviceIndex, uint reserved);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_GetDeviceInf(IntPtr deviceHandle, IntPtr info);

        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_CloseDevice(IntPtr deviceHandle);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr ZCAN_InitCAN(IntPtr deviceHandle, uint channelIndex, IntPtr config);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_SetValue(IntPtr deviceHandle, string path, string value);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ZcanDeviceInfo
        {
            internal ushort HardwareVersion;
            internal ushort FirmwareVersion;
            internal ushort DriverVersion;
            internal ushort InterfaceVersion;
            internal ushort IrqNumber;
            internal byte CanCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.I1)]
            internal byte[] SerialNumber;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40, ArraySubType = UnmanagedType.I1)]
            internal byte[] HardwareType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            internal byte[] Reserved;
        }

        internal static string DecodeDeviceText(byte[] value)
        {
            if (value == null || value.Length == 0) return "";
            int length = Array.IndexOf(value, (byte)0);
            if (length < 0) length = value.Length;
            return Encoding.ASCII.GetString(value, 0, length).Trim();
        }
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_SetValue(IntPtr deviceHandle, string path, IntPtr value);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr ZCAN_GetValue(IntPtr deviceHandle, string path);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_StartCAN(IntPtr channelHandle);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_ResetCAN(IntPtr channelHandle);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_Transmit(IntPtr channelHandle, IntPtr data, uint count);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_TransmitFD(IntPtr channelHandle, IntPtr data, uint count);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_GetReceiveNum(IntPtr channelHandle, byte type);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_Receive(IntPtr channelHandle, IntPtr data, uint count, int waitTime);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_ReceiveFD(IntPtr channelHandle, IntPtr data, uint count, int waitTime);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_SearchDevice(uint timeout, string ip);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr ZN_AddDevice(uint deviceType);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_DeleteDevice(IntPtr handle);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_ClearDevice();
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZN_GetDeviceCount();
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr ZN_GetDeviceAt(uint index);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_Connect(IntPtr handle, ref ZNetTcpConnectionNative info);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_Connect(IntPtr handle, ref ZNetComConnectionNative info);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_Connect(IntPtr handle, ref ZNetCloudConnectionNative info);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_Connect(IntPtr handle, ref ZNetBleConnectionNative info);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_IsConnected(IntPtr handle);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "ZN_Disonnect")]
        internal static extern int ZN_Disconnect(IntPtr handle);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_LoadInfo(IntPtr handle);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_GetProperty(IntPtr handle, string name, ref ZNetPropertyNative property);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_SetProperty(IntPtr handle, ref ZNetPropertyNative property);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_SubmitInfo(IntPtr handle, string password);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_Reset(IntPtr handle, string password);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_FactoryReset(IntPtr handle, string password);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate void ZNetUpgradeCallback(IntPtr context, float progress);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_Upgrade(IntPtr handle, byte[] data, uint length, IntPtr argument, ZNetUpgradeCallback callback, IntPtr context);
        [DllImport("ZNetConfig.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int ZN_ExecCmd(int command, IntPtr handle, IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_UDS_Request(IntPtr deviceHandle, IntPtr request, IntPtr response, IntPtr dataBuffer, uint dataBufferSize);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr ZCAN_InitLIN(IntPtr deviceHandle, uint channelIndex, IntPtr config);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_StartLIN(IntPtr channelHandle);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_ResetLIN(IntPtr channelHandle);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_TransmitLIN(IntPtr channelHandle, IntPtr data, uint count);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_GetLINReceiveNum(IntPtr channelHandle);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_ReceiveLIN(IntPtr channelHandle, IntPtr data, uint count, int waitTime);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_SetLINPublish(IntPtr channelHandle, IntPtr data, uint count);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_SetLINSubscribe(IntPtr channelHandle, IntPtr data, uint count);
        [DllImport("zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZCAN_WakeUpLIN(IntPtr channelHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate uint UdsTransmitDelegate(IntPtr context, IntPtr frame, uint count);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint ZUDS_Init(uint type);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern void ZUDS_Release(uint handle);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern void ZUDS_SetParam(uint handle, byte type, IntPtr param);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern void ZUDS_SetTransmitHandler(uint handle, IntPtr context, UdsTransmitDelegate callback);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern void ZUDS_OnReceive(uint handle, IntPtr frame);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern void ZUDS_Request(uint handle, IntPtr request, IntPtr response);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern void ZUDS_Stop(uint handle);
        [DllImport("zuds.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern void ZUDS_SetTesterPresent(uint handle, byte enable, IntPtr param);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UdsSessionParam { public ushort Timeout, EnhancedTimeout; public uint Reserved0, Reserved1; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UdsIsoTpParam
        {
            public byte Version, MaxDataLength, LocalStMin, BlockSize, FillByte, FrameType, ModifyRemoteStMin, RemoteStMin;
            public ushort FlowControlTimeout;
            public byte FillMode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public byte[] Reserved;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UdsTesterPresentParam { public uint Address; public ushort Cycle; public byte SuppressResponse; public uint Reserved; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UdsRequest { public uint Source, Target; public byte SuppressResponse, Sid; public ushort Reserved0; public IntPtr Param; public uint ParamLength, Reserved1; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UdsPositive { public byte Sid; public IntPtr Param; public uint ParamLength; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UdsNegative { public byte Code, Sid, ErrorCode; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct UdsResponseUnion { [FieldOffset(0)] public UdsPositive Positive; [FieldOffset(0)] public UdsNegative Negative; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UdsResponse { public byte Status, Type; public UdsResponseUnion Union; public uint Reserved; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal class UdsFrame
        {
            public uint Id; public byte Extend, Remote, DataLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Data = new byte[64];
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ChannelInitConfig
        {
            public uint CanType;
            public ChannelInitUnion Config;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ChannelInitUnion
        {
            public CanInitConfig Can;
            public CanFdInitConfig CanFd;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct CanInitConfig
        {
            public uint AccCode, AccMask, Reserved;
            public byte Filter, Timing0, Timing1, Mode;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct CanFdInitConfig
        {
            public uint AccCode, AccMask, ArbitrationTiming, DataTiming, Brp;
            public byte Filter, Mode;
            public ushort Pad;
            public uint Reserved;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct CanFrame
        {
            public uint Id;
            public byte Length, Pad, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Data;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct CanFdFrame
        {
            public uint Id;
            public byte Length, Flags, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Data;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TransmitData { public CanFrame Frame; public uint TransmitType; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TransmitFdData { public CanFdFrame Frame; public uint TransmitType; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct AutoTransmitData { public ushort Enable, Index; public uint Interval; public TransmitData Obj; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct AutoTransmitFdData { public ushort Enable, Index; public uint Interval; public TransmitFdData Obj; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ReceiveData { public CanFrame Frame; public ulong Timestamp; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct ReceiveFdData { public CanFdFrame Frame; public ulong Timestamp; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct HardwareUdsSessionParam
        {
            public uint Timeout, EnhancedTimeout;
            public byte ThreeInOne;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)] public byte[] Reserved;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct HardwareUdsTransportParam
        {
            public byte Version, MaxDataLength, LocalStMin, BlockSize, FillByte, ExtendedFrame, ModifyRemoteStMin, RemoteStMin;
            public uint FlowControlTimeout;
            public byte FillMode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] Reserved;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct HardwareUdsRequest
        {
            public uint RequestId;
            public byte Channel, FrameType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public byte[] Reserved0;
            public uint Source, Target;
            public byte SuppressResponse, Sid;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Reserved1;
            public HardwareUdsSessionParam Session;
            public HardwareUdsTransportParam Transport;
            public IntPtr Data;
            public uint DataLength, Reserved2;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct HardwareUdsResponse
        {
            public byte Status;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Reserved;
            public byte Type;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Raw;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        internal struct ZNetPropertyNative
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string Value;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        internal struct ZNetTcpConnectionNative
        {
            internal int CommType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] internal string Ip;
            internal ushort Port;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        internal struct ZNetComConnectionNative
        {
            internal int CommType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Port;
            internal int BaudRate, DataBits, StopBits, Parity, Timeout;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        internal struct ZNetCloudConnectionNative
        {
            internal int CommType, DeviceIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Id;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        internal struct ZNetBleConnectionNative
        {
            internal int CommType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string DeviceMac;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct LinInitConfig { public byte Mode, ChecksumMode, MaxLength, Reserved; public uint Baudrate; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct LinTransmit { public byte Id, Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Data; public byte ChecksumMode; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public byte[] Reserved; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct LinMessage { public byte Channel, DataType; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 46)] public byte[] Data; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct LinReceive { public ulong Timestamp; public byte Length, Direction, Checksum; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)] public byte[] Reserved; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Data; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct LinSubscribe { public byte Id, Length, ChecksumMode; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public byte[] Reserved; }

        internal static string GetValue(IntPtr deviceHandle, string path)
        {
            IntPtr value = ZCAN_GetValue(deviceHandle, path);
            return value == IntPtr.Zero ? "<null>" : (Marshal.PtrToStringAnsi(value) ?? "<empty>");
        }
    }
}
