using System;
using System.Collections.Generic;
using System.Globalization;
using JabilTest;
using ScriptVariableSpaceInterface;
using ExcelDecodeCore;
using Jabil.Uds.Can;
using Jabil.Uds.Core;

namespace UdsFunctionLibrary
{
    internal sealed class UdsCompatibilitySession : IDisposable
    {
        internal readonly ICanUdsTransport Transport;
        internal int TimeoutMilliseconds = 5000;
        internal int EnhancedTimeoutMilliseconds = 120000;
        internal byte LocalStMin;
        internal byte BlockSize;
        internal byte RemoteStMin;
        internal bool ModifyRemoteStMin;
        internal bool Stopped;

        internal UdsCompatibilitySession(ICanUdsTransport transport)
        {
            Transport = transport;
        }

        public void Dispose()
        {
            if (Transport != null) Transport.Dispose();
        }
    }

    internal static class UdsCompatibilityRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<uint, UdsCompatibilitySession> Sessions = new Dictionary<uint, UdsCompatibilitySession>();
        private static uint nextHandle;
        private static readonly Dictionary<uint, IZlgClosedUdsCapability> ZlgClosedSessions = new Dictionary<uint, IZlgClosedUdsCapability>();

        internal static uint Add(UdsCompatibilitySession session)
        {
            lock (Sync)
            {
                uint handle = ++nextHandle;
                if (handle == 0) handle = ++nextHandle;
                Sessions[handle] = session;
                return handle;
            }
        }

        internal static UdsCompatibilitySession Get(uint handle)
        {
            lock (Sync)
            {
                UdsCompatibilitySession session;
                if (!Sessions.TryGetValue(handle, out session))
                    throw new InvalidOperationException("Unknown or released ZUDS handle: " + handle + ".");
                return session;
            }
        }

        internal static void Remove(uint handle)
        {
            UdsCompatibilitySession session = null;
            lock (Sync)
            {
                if (Sessions.TryGetValue(handle, out session)) Sessions.Remove(handle);
            }
            if (session != null) session.Dispose();
        }

        internal static uint AddZlgClosed(IZlgClosedUdsCapability session)
        {
            lock (Sync)
            {
                uint handle = ++nextHandle;
                if (handle == 0) handle = ++nextHandle;
                ZlgClosedSessions[handle] = session;
                return handle;
            }
        }

        internal static IZlgClosedUdsCapability GetZlgClosed(uint handle)
        {
            lock (Sync)
            {
                IZlgClosedUdsCapability session;
                if (!ZlgClosedSessions.TryGetValue(handle, out session)) throw new InvalidOperationException("Unknown ZLG closed UDS handle: " + handle + ".");
                return session;
            }
        }

        internal static void RemoveZlgClosed(uint handle)
        {
            IZlgClosedUdsCapability session = null;
            lock (Sync)
            {
                if (ZlgClosedSessions.TryGetValue(handle, out session)) ZlgClosedSessions.Remove(handle);
            }
            if (session != null) session.Dispose();
        }
    }

    public abstract class UdsCompatibilityTest : UdsTestBase
    {
        protected UdsCompatibilityTest(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        protected uint Handle() { return (uint)Math.Max(0, ArgInt(0, "ZUDS_HANDLE", 0)); }
        protected TestResult Failure(string name, Exception error)
        {
            SetReturn(0, false);
            SetReturn(1, error.Message);
            UpdateStatusKey(name + ": FAIL - " + error.Message);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZUDS_Init : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_Init(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                object channelValue = ArgObject(0, "channel_handle");
                ICanChannel channel = CanChannelHandleRegistry.Resolve(channelValue);
                CanChannelOptions channelOptions = CanChannelHandleRegistry.GetOptions(channelValue);
                bool canFd = channelOptions != null ? channelOptions.CanFd : ArgInt(1, "CAN_Type", 0) != 0;
                ICanDeviceProvider provider = CanTransportRegistry.Current;
                CanChannelOptions options = channelOptions ?? new CanChannelOptions { ChannelIndex = 0, CanFd = canFd };
                ICanUdsTransport transport;
                if (channelOptions != null)
                    transport = new CanIsoTpTransport(channel, options, 0, 0);
                else
                {
                    if (provider == null) throw new InvalidOperationException("CAN provider is not registered.");
                    transport = new CanIsoTpTransport(provider, options, 0, 0);
                }
                uint handle = UdsCompatibilityRegistry.Add(new UdsCompatibilitySession(transport));
                SetReturn(0, (int)handle);
                UpdateStatusKey("ZUDS_Init: OK - channel=" + channel.GetType().Name);
            }
            catch (Exception ex) { return Failure("ZUDS_Init", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZUDS_Request : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_Request(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                UdsCompatibilitySession session = UdsCompatibilityRegistry.Get(Handle());
                string sourceText = ArgString(1, "Src addr", "0x7E0");
                string targetText = ArgString(2, "Dst addr", "0x7E8");
                string requestText = ArgString(3, "uds send data", "");
                uint source = ParseAddress(sourceText);
                uint target = ParseAddress(targetText);
                byte[] payload = RawCanChannel.ParseData(requestText);
                if (payload.Length == 0) throw new InvalidOperationException("UDS request is empty.");
                CanIsoTpTransport canTransport = session.Transport as CanIsoTpTransport;
                if (canTransport != null)
                {
                    canTransport.SetAddresses(source, target);
                    canTransport.ConfigureIsoTp(session.LocalStMin, session.BlockSize, session.RemoteStMin, session.ModifyRemoteStMin);
                }
                DiagnosticFrame response = session.Transport.Request(new DiagnosticFrame(source, target, payload), session.TimeoutMilliseconds);
                byte[] responsePayload = response == null ? new byte[0] : response.Payload;
                string responseHex = HexUtils.ToHexString(responsePayload);
                SetReturn(0, responseHex);
                SetReturn(1, 0);
                SetReturn(2, responsePayload.Length > 0 && responsePayload[0] == 0x7F ? 0 : 1);
                SetReturn(3, "Request: " + HexUtils.ToHexString(payload) + Environment.NewLine + "Response: " + responseHex);
                UpdateStatusKey("ZUDS_Request: OK - " + responseHex);
            }
            catch (Exception ex) { return Failure("ZUDS_Request", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        private static uint ParseAddress(string text)
        {
            string value = (text ?? "").Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            return UInt32.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }

    public sealed class ZUDS_Set_Session_Param : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_Set_Session_Param(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                UdsCompatibilitySession session = UdsCompatibilityRegistry.Get(Handle());
                session.TimeoutMilliseconds = Math.Max(1, ArgInt(1, "response timeout", session.TimeoutMilliseconds));
                session.EnhancedTimeoutMilliseconds = Math.Max(1, ArgInt(2, "receive native resp timeout", session.EnhancedTimeoutMilliseconds));
                SetReturn(0, true);
            }
            catch (Exception ex) { return Failure("ZUDS_Set_Session_Param", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZUDS_Set_Transport_Param : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_Set_Transport_Param(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                UdsCompatibilitySession session = UdsCompatibilityRegistry.Get(Handle());
                Array array = null;
                try { array = GetArrayVariable("Argument1", "Array Variable") as Array; }
                catch (Exception) { }
                if (array != null)
                {
                    session.LocalStMin = Convert.ToByte(array.GetValue(1));
                    session.BlockSize = Convert.ToByte(array.GetValue(2));
                    session.RemoteStMin = Convert.ToByte(array.GetValue(6));
                    session.ModifyRemoteStMin = Convert.ToByte(array.GetValue(5)) != 0;
                }
                SetReturn(0, true);
            }
            catch (Exception ex) { return Failure("ZUDS_Set_Transport_Param", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZUDS_Stop : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_Stop(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { UdsCompatibilityRegistry.Get(Handle()).Stopped = true; SetReturn(0, true); }
            catch (Exception ex) { return Failure("ZUDS_Stop", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZUDS_Release : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_Release(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { UdsCompatibilityRegistry.Remove(Handle()); SetReturn(0, true); }
            catch (Exception ex) { return Failure("ZUDS_Release", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZUDS_TesterPresent : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_TesterPresent(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                UdsCompatibilitySession session = UdsCompatibilityRegistry.Get(Handle());
                uint source = ParseAddress(ArgString(1, "Src addr", "0x7E0"));
                uint target = ParseAddress(ArgString(2, "Dst addr", "0x7E8"));
                byte suppress = (byte)ArgInt(3, "suppress_response", 0);
                byte[] payload = new byte[] { 0x3E, (byte)(suppress == 0 ? 0x00 : 0x80) };
                CanIsoTpTransport canTransport = session.Transport as CanIsoTpTransport;
                if (canTransport != null) canTransport.SetAddresses(source, target);
                DiagnosticFrame response = session.Transport.Request(new DiagnosticFrame(source, target, payload), session.TimeoutMilliseconds);
                byte[] responsePayload = response == null ? new byte[0] : response.Payload;
                SetReturn(0, HexUtils.ToHexString(responsePayload));
                SetReturn(1, 0);
                UpdateStatusKey("ZUDS_TesterPresent: OK - " + HexUtils.ToHexString(responsePayload));
            }
            catch (Exception ex) { return Failure("ZUDS_TesterPresent", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        private static uint ParseAddress(string text)
        {
            string value = (text ?? "").Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            return UInt32.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }

    public sealed class ZUDS_ZlgEngine_Init : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_ZlgEngine_Init(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                object channelValue = ArgObject(0, "channel_handle");
                CanChannelOptions options = CanChannelHandleRegistry.GetOptions(channelValue) ?? new CanChannelOptions { CanFd = ArgInt(1, "CAN_Type", 0) != 0 };
                IZlgClosedUdsProvider provider = CanTransportRegistry.Current as IZlgClosedUdsProvider;
                if (provider == null) throw new InvalidOperationException("Current CAN provider does not expose the ZLG closed UDS engine.");
                IZlgClosedUdsCapability capability = provider.OpenZlgClosedUds(options);
                uint handle = UdsCompatibilityRegistry.AddZlgClosed(capability);
                SetReturn(0, (int)handle);
                UpdateStatusKey("ZUDS_ZlgEngine_Init: OK");
            }
            catch (Exception ex) { return Failure("ZUDS_ZlgEngine_Init", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZUDS_ZlgEngine_Request : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_ZlgEngine_Request(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                IZlgClosedUdsCapability capability = UdsCompatibilityRegistry.GetZlgClosed(Handle());
                uint source = ParseAddress(ArgString(1, "Src addr", "0x7E0"));
                uint target = ParseAddress(ArgString(2, "Dst addr", "0x7E8"));
                byte[] payload = RawCanChannel.ParseData(ArgString(3, "uds send data", ""));
                bool suppress = ArgInt(4, "suppress_response", 0) != 0;
                byte[] response = capability.Request(source, target, payload, suppress);
                SetReturn(0, HexUtils.ToHexString(response));
                SetReturn(1, 0);
                SetReturn(2, response.Length > 0 && response[0] == 0x7F ? 0 : 1);
                UpdateStatusKey("ZUDS_ZlgEngine_Request: OK - " + HexUtils.ToHexString(response));
            }
            catch (Exception ex) { return Failure("ZUDS_ZlgEngine_Request", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        private static uint ParseAddress(string text)
        {
            string value = (text ?? "").Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            return UInt32.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }

    public sealed class ZUDS_ZlgEngine_Release : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZUDS_ZlgEngine_Release(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { UdsCompatibilityRegistry.RemoveZlgClosed(Handle()); SetReturn(0, true); }
            catch (Exception ex) { return Failure("ZUDS_ZlgEngine_Release", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZCAN_HWUDS_Request : UdsCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_HWUDS_Request(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                ICanDeviceProvider current = CanTransportRegistry.Current;
                IZcanHardwareUdsProvider provider = current as IZcanHardwareUdsProvider;
                if (provider == null) throw new InvalidOperationException("Current CAN provider does not support ZCAN_HWUDS_Request.");
                int channel = ArgInt(1, "channel index", 0);
                uint frameType = (uint)ArgInt(5, "isCanfd", 0);
                var options = new CanChannelOptions { ChannelIndex = channel, CanFd = frameType != 0 };
                string sourceText = ArgString(2, "Src addr", "0x7E0");
                string targetText = ArgString(3, "Dst addr", "0x7E8");
                byte[] payload = RawCanChannel.ParseData(ArgString(4, "uds send data", ""));
                byte fillByte = (byte)ArgInt(6, "fillByte", 0xCC);
                byte fillMode = (byte)ArgInt(7, "fillMode", 0);
                HardwareUdsResponse response = provider.HardwareUdsRequest(options, 0, ParseAddress(sourceText), ParseAddress(targetText), payload, frameType, fillByte, fillMode, false);
                string responseHex = HexUtils.ToHexString(response.Payload);
                SetReturn(0, responseHex);
                SetReturn(1, (int)response.Status);
                SetReturn(2, (int)response.Type);
                SetReturn(3, "ZCAN_HWUDS_Request: " + responseHex);
                UpdateStatusKey("ZCAN_HWUDS_Request: status=" + response.Status + ", type=" + response.Type);
            }
            catch (Exception ex) { return Failure("ZCAN_HWUDS_Request", ex); }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        private static uint ParseAddress(string text)
        {
            string value = (text ?? "").Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            return UInt32.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }
}
