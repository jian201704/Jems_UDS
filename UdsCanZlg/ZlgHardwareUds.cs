using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Jabil.Uds.Can;

namespace ZLGAPI
{
    public sealed class ZlgClosedUds : IZlgClosedUdsCapability
    {
        private readonly ICanChannel channel;
        private readonly CanChannelOptions options;
        private readonly ZlgNativeApi.UdsTransmitDelegate transmitCallback;
        private readonly GCHandle selfHandle;
        private readonly Thread receiveThread;
        private volatile bool stopping;
        private uint udsHandle;
        private int timeoutMilliseconds = 5000;
        private int enhancedTimeoutMilliseconds = 120000;

        public ZlgClosedUds(ICanChannel channel, CanChannelOptions options)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            this.channel = channel;
            this.options = options ?? throw new ArgumentNullException("options");
            ZlgNativeApi.EnsureUdsLibraryLoaded();
            udsHandle = ZlgNativeApi.ZUDS_Init(0);
            if (udsHandle == 0) throw new InvalidOperationException("ZUDS_Init returned an invalid handle.");
            transmitCallback = Transmit;
            selfHandle = GCHandle.Alloc(this);
            ZlgNativeApi.ZUDS_SetTransmitHandler(udsHandle, GCHandle.ToIntPtr(selfHandle), transmitCallback);
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "ZLG-Hardware-UDS-Receive" };
            receiveThread.Start();
        }

        public void ConfigureSession(int timeout, int enhancedTimeout)
        {
            timeoutMilliseconds = Math.Max(1, Math.Min(ushort.MaxValue, timeout));
            enhancedTimeoutMilliseconds = Math.Max(1, Math.Min(ushort.MaxValue, enhancedTimeout));
            SetNativeParam(0, new ZlgNativeApi.UdsSessionParam
            {
                Timeout = (ushort)timeoutMilliseconds,
                EnhancedTimeout = (ushort)enhancedTimeoutMilliseconds
            });
        }

        public void ConfigureTransport(byte localStMin, byte blockSize, byte remoteStMin, bool modifyRemoteStMin)
        {
            SetNativeParam(1, new ZlgNativeApi.UdsIsoTpParam
            {
                Version = (byte)(options.CanFd ? 1 : 0),
                MaxDataLength = (byte)(options.CanFd ? 64 : 8),
                LocalStMin = localStMin,
                BlockSize = blockSize,
                ModifyRemoteStMin = (byte)(modifyRemoteStMin ? 1 : 0),
                RemoteStMin = remoteStMin,
                FlowControlTimeout = (ushort)Math.Min(ushort.MaxValue, timeoutMilliseconds),
                Reserved = new byte[5]
            });
        }

        public byte[] Request(uint sourceAddress, uint targetAddress, byte[] payload, bool suppressResponse)
        {
            if (payload == null || payload.Length == 0) throw new ArgumentException("UDS payload is empty.", "payload");
            IntPtr requestPtr = IntPtr.Zero;
            IntPtr paramPtr = IntPtr.Zero;
            IntPtr responsePtr = IntPtr.Zero;
            try
            {
                byte sid = payload[0];
                byte[] parameters = new byte[payload.Length - 1];
                if (parameters.Length > 0) Array.Copy(payload, 1, parameters, 0, parameters.Length);
                if (parameters.Length > 0)
                {
                    paramPtr = Marshal.AllocHGlobal(parameters.Length);
                    Marshal.Copy(parameters, 0, paramPtr, parameters.Length);
                }
                ZlgNativeApi.UdsRequest request = new ZlgNativeApi.UdsRequest
                {
                    Source = sourceAddress, Target = targetAddress,
                    SuppressResponse = (byte)(suppressResponse ? 1 : 0), Sid = sid,
                    Param = paramPtr, ParamLength = (uint)parameters.Length
                };
                requestPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.UdsRequest)));
                Marshal.StructureToPtr(request, requestPtr, false);
                responsePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.UdsResponse)));
                ZlgNativeApi.ZUDS_Request(udsHandle, requestPtr, responsePtr);
                ZlgNativeApi.UdsResponse response = (ZlgNativeApi.UdsResponse)Marshal.PtrToStructure(responsePtr, typeof(ZlgNativeApi.UdsResponse));
                if (response.Status == 4) return new byte[0];
                if (response.Status != 0) throw new TimeoutException("Hardware UDS request failed with status " + response.Status + ".");
                if (response.Type == 0) return new byte[] { response.Union.Negative.Code, response.Union.Negative.Sid, response.Union.Negative.ErrorCode };
                int length = (int)response.Union.Positive.ParamLength;
                byte[] result = new byte[length + 1];
                result[0] = response.Union.Positive.Sid;
                if (length > 0 && response.Union.Positive.Param != IntPtr.Zero) Marshal.Copy(response.Union.Positive.Param, result, 1, length);
                return result;
            }
            finally
            {
                if (responsePtr != IntPtr.Zero) Marshal.FreeHGlobal(responsePtr);
                if (requestPtr != IntPtr.Zero) Marshal.FreeHGlobal(requestPtr);
                if (paramPtr != IntPtr.Zero) Marshal.FreeHGlobal(paramPtr);
            }
        }

        public void SetTesterPresent(bool enabled, uint address, ushort cycleMilliseconds, bool suppressResponse)
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                var param = new ZlgNativeApi.UdsTesterPresentParam { Address = address, Cycle = cycleMilliseconds, SuppressResponse = (byte)(suppressResponse ? 1 : 0) };
                ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.UdsTesterPresentParam)));
                Marshal.StructureToPtr(param, ptr, false);
                ZlgNativeApi.ZUDS_SetTesterPresent(udsHandle, (byte)(enabled ? 1 : 0), ptr);
            }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
        }

        private void SetNativeParam<T>(byte type, T value) where T : struct
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));
                Marshal.StructureToPtr(value, ptr, false);
                ZlgNativeApi.ZUDS_SetParam(udsHandle, type, ptr);
            }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
        }

        private uint Transmit(IntPtr context, IntPtr frame, uint count)
        {
            try
            {
                var frames = new List<CanFrame>();
                int size = Marshal.SizeOf(typeof(ZlgNativeApi.UdsFrame));
                for (uint index = 0; index < count; index++)
                {
                    var native = (ZlgNativeApi.UdsFrame)Marshal.PtrToStructure(frame + (int)(index * (uint)size), typeof(ZlgNativeApi.UdsFrame));
                    byte[] data = new byte[Math.Min(native.DataLength, (byte)(options.CanFd ? 64 : 8))];
                    if (data.Length > 0) Array.Copy(native.Data, data, data.Length);
                    frames.Add(new CanFrame { Identifier = native.Id, Extended = native.Extend != 0, Remote = native.Remote != 0, Data = data });
                }
                return (uint)channel.Send(frames);
            }
            catch (Exception) { return 0; }
        }

        private void ReceiveLoop()
        {
            while (!stopping)
            {
                try
                {
                    IList<CanFrame> frames = channel.Receive(20);
                    foreach (CanFrame frame in frames) Forward(frame);
                }
                catch (Exception) { if (!stopping) Thread.Sleep(10); }
            }
        }

        private void Forward(CanFrame frame)
        {
            var native = new ZlgNativeApi.UdsFrame
            {
                Id = frame.Identifier, Extend = (byte)(frame.Extended ? 1 : 0), Remote = (byte)(frame.Remote ? 1 : 0),
                DataLength = (byte)Math.Min(64, frame.Data == null ? 0 : frame.Data.Length), Data = new byte[64]
            };
            if (frame.Data != null) Array.Copy(frame.Data, native.Data, native.DataLength);
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZlgNativeApi.UdsFrame)));
                Marshal.StructureToPtr(native, ptr, false);
                ZlgNativeApi.ZUDS_OnReceive(udsHandle, ptr);
            }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
        }

        public void Dispose()
        {
            stopping = true;
            if (udsHandle != 0)
            {
                try { ZlgNativeApi.ZUDS_Stop(udsHandle); } catch (Exception) { }
                try { ZlgNativeApi.ZUDS_Release(udsHandle); } catch (Exception) { }
                udsHandle = 0;
            }
            if (selfHandle.IsAllocated) selfHandle.Free();
        }
    }
}
