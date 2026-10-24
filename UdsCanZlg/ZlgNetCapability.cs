using System;
using Jabil.Uds.Can;

namespace ZLGAPI
{
    public sealed class ZlgNetCapability : IZNetCapability
    {
        public ZlgNetCapability() { ZlgNativeApi.EnsureZNetLibraryLoaded(); }
        public bool Search(uint timeoutMilliseconds, string ip) { return ZlgNativeApi.ZN_SearchDevice(timeoutMilliseconds, ip ?? "") == 0; }
        public IntPtr AddDevice(uint deviceType) { return ZlgNativeApi.ZN_AddDevice(deviceType); }
        public bool DeleteDevice(IntPtr handle) { return ZlgNativeApi.ZN_DeleteDevice(handle) == 0; }
        public bool ClearDevices() { return ZlgNativeApi.ZN_ClearDevice() == 0; }
        public uint GetDeviceCount() { return ZlgNativeApi.ZN_GetDeviceCount(); }
        public IntPtr GetDeviceAt(uint index) { return ZlgNativeApi.ZN_GetDeviceAt(index); }
        public bool ConnectTcp(IntPtr handle, ZNetTcpConnection connection)
        {
            var native = new ZlgNativeApi.ZNetTcpConnectionNative { CommType = 1, Ip = connection == null ? "" : connection.Ip ?? "", Port = connection == null ? (ushort)0 : connection.Port };
            return ZlgNativeApi.ZN_Connect(handle, ref native) == 0;
        }
        public bool ConnectCom(IntPtr handle, ZNetComConnection connection)
        {
            var source = connection ?? new ZNetComConnection();
            var native = new ZlgNativeApi.ZNetComConnectionNative { CommType = 2, Port = source.Port ?? "", BaudRate = source.BaudRate, DataBits = source.DataBits, StopBits = source.StopBits, Parity = source.Parity, Timeout = source.Timeout };
            return ZlgNativeApi.ZN_Connect(handle, ref native) == 0;
        }
        public bool ConnectCloud(IntPtr handle, ZNetCloudConnection connection)
        {
            var source = connection ?? new ZNetCloudConnection();
            var native = new ZlgNativeApi.ZNetCloudConnectionNative { CommType = 3, DeviceIndex = source.DeviceIndex, Id = source.Id ?? "" };
            return ZlgNativeApi.ZN_Connect(handle, ref native) == 0;
        }
        public bool ConnectBle(IntPtr handle, ZNetBleConnection connection)
        {
            var source = connection ?? new ZNetBleConnection();
            var native = new ZlgNativeApi.ZNetBleConnectionNative { CommType = 4, DeviceName = source.DeviceName ?? "", DeviceMac = source.DeviceMac ?? "" };
            return ZlgNativeApi.ZN_Connect(handle, ref native) == 0;
        }
        public bool IsConnected(IntPtr handle) { return ZlgNativeApi.ZN_IsConnected(handle) != 0; }
        public bool Disconnect(IntPtr handle) { return ZlgNativeApi.ZN_Disconnect(handle) == 0; }
        public bool Upgrade(IntPtr handle, byte[] image, Action<float> progress)
        {
            if (image == null || image.Length == 0) return false;
            ZlgNativeApi.ZNetUpgradeCallback callback = delegate(IntPtr context, float value) { if (progress != null) progress(value); };
            return ZlgNativeApi.ZN_Upgrade(handle, image, (uint)image.Length, IntPtr.Zero, callback, IntPtr.Zero) == 0;
        }
        public int ExecuteCommand(int command, IntPtr handle, IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5)
        {
            return ZlgNativeApi.ZN_ExecCmd(command, handle, arg1, arg2, arg3, arg4, arg5);
        }
        public bool LoadInfo(IntPtr handle) { return ZlgNativeApi.ZN_LoadInfo(handle) == 0; }
        public bool GetProperty(IntPtr handle, string name, out ZNetProperty property)
        {
            var native = new ZlgNativeApi.ZNetPropertyNative { Name = name ?? "", Value = "" };
            bool result = ZlgNativeApi.ZN_GetProperty(handle, name ?? "", ref native) == 0;
            property = new ZNetProperty { Name = native.Name ?? name ?? "", Value = native.Value ?? "" };
            return result;
        }
        public bool SetProperty(IntPtr handle, string name, string value)
        {
            var native = new ZlgNativeApi.ZNetPropertyNative { Name = name ?? "", Value = value ?? "" };
            return ZlgNativeApi.ZN_SetProperty(handle, ref native) == 0;
        }
        public bool SubmitInfo(IntPtr handle, string password) { return ZlgNativeApi.ZN_SubmitInfo(handle, password ?? "") == 0; }
        public bool Reset(IntPtr handle, string password) { return ZlgNativeApi.ZN_Reset(handle, password ?? "") == 0; }
        public bool FactoryReset(IntPtr handle, string password) { return ZlgNativeApi.ZN_FactoryReset(handle, password ?? "") == 0; }
    }
}
