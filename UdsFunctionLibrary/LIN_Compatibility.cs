using System;
using System.Collections.Generic;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    internal static class LinHandleRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<IntPtr, ILinCapability> Handles = new Dictionary<IntPtr, ILinCapability>();
        private static long nextToken = 0x30000;

        internal static IntPtr Add(ILinCapability capability)
        {
            lock (Sync)
            {
                IntPtr token = new IntPtr(++nextToken);
                Handles[token] = capability;
                return token;
            }
        }

        internal static ILinCapability Get(object value)
        {
            IntPtr token = value is IntPtr ? (IntPtr)value : new IntPtr(Convert.ToInt64(value));
            lock (Sync)
            {
                ILinCapability capability;
                if (!Handles.TryGetValue(token, out capability)) throw new InvalidOperationException("Unknown LIN channel handle: " + token + ".");
                return capability;
            }
        }

        internal static void Remove(object value)
        {
            IntPtr token = value is IntPtr ? (IntPtr)value : new IntPtr(Convert.ToInt64(value));
            ILinCapability capability = null;
            lock (Sync) { if (Handles.TryGetValue(token, out capability)) Handles.Remove(token); }
            if (capability != null) capability.Dispose();
        }
    }

    public abstract class LinCompatibilityTest : UdsTestBase
    {
        protected LinCompatibilityTest(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        protected ILinCapability Channel() { return LinHandleRegistry.Get(ArgObject(0, "lin_channel_handle")); }
        protected static byte ParseByte(string text) { return Convert.ToByte((text ?? "0").Trim().Replace("0x", ""), 16); }
        protected static byte[] Data(string text) { return RawCanChannel.ParseData(text ?? ""); }
        protected TestResult Error(string name, Exception error)
        {
            SetReturn(0, 0); SetReturn(1, error.Message); UpdateStatusKey(name + ": FAIL - " + error.Message);
            testResult.Status = TestStatus.Pass; return testResult;
        }
    }

    public sealed class ZLG_InitLIN : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_InitLIN(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute()
        {
            try
            {
                ILinProvider provider = CanTransportRegistry.Current as ILinProvider;
                if (provider == null) throw new InvalidOperationException("Current CAN provider does not support LIN.");
                int index = ArgInt(1, "channel_index", 0);
                ILinCapability capability = provider.OpenLin(index);
                capability.Initialize(ArgInt(2, "LinMode", 0) != 0, (byte)ArgInt(4, "ChkSumMode", 0), 8, (uint)Math.Max(1000, ArgInt(3, "LinBaud", 19200)));
                SetReturn(0, LinHandleRegistry.Add(capability));
            }
            catch (Exception ex) { return Error("ZLG_InitLIN", ex); }
            testResult.Status = TestStatus.Pass; return testResult;
        }
    }

    public sealed class ZLG_StartLIN : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_StartLIN(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute() { try { Channel().Start(); SetReturn(0, 1); } catch (Exception ex) { return Error("ZLG_StartLIN", ex); } testResult.Status = TestStatus.Pass; return testResult; }
    }

    public sealed class ZLG_ResetLIN : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_ResetLIN(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute() { try { Channel().Reset(); SetReturn(0, 1); } catch (Exception ex) { return Error("ZLG_ResetLIN", ex); } testResult.Status = TestStatus.Pass; return testResult; }
    }

    public sealed class ZLG_TransmitLIN : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_TransmitLIN(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute()
        {
            try { byte id = ParseByte(ArgString(1, "LINID", "0")); int sent = Channel().Transmit(new List<LinMessage> { new LinMessage { Id = id, Data = Data(ArgString(2, "LIN Msg", "")) } }); SetReturn(0, sent); }
            catch (Exception ex) { return Error("ZLG_TransmitLIN", ex); }
            testResult.Status = TestStatus.Pass; return testResult;
        }
    }

    public sealed class ZLG_SetLINPublish : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_SetLINPublish(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute()
        {
            try { Channel().Publish(ParseByte(ArgString(1, "ID", "0")), Data(ArgString(2, "Data", "")), 0); SetReturn(0, 1); }
            catch (Exception ex) { return Error("ZLG_SetLINPublish", ex); }
            testResult.Status = TestStatus.Pass; return testResult;
        }
    }

    public sealed class ZLG_SetLINPublishEx : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_SetLINPublishEx(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute() { return new ZLG_SetLINPublish(VariableSpace, null).Execute(); }
    }

    public sealed class ZLG_ReceiveLIN : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_ReceiveLIN(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute()
        {
            try
            {
                IList<LinMessage> messages = Channel().Receive(50, Math.Max(1, ArgInt(1, "ReceiveMsgCount", 1)));
                List<string> lines = new List<string>();
                foreach (LinMessage message in messages) lines.Add(message.Id.ToString("X2") + " " + HexUtils.ToHexString(message.Data));
                SetReturn(0, string.Join(";" + Environment.NewLine, lines.ToArray()));
            }
            catch (Exception ex) { return Error("ZLG_ReceiveLIN", ex); }
            testResult.Status = TestStatus.Pass; return testResult;
        }
    }

    public sealed class ZLG_WakeUpLIN : LinCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZLG_WakeUpLIN(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        public override TestResult Execute() { try { Channel().WakeUp(); SetReturn(0, 1); } catch (Exception ex) { return Error("ZLG_WakeUpLIN", ex); } testResult.Status = TestStatus.Pass; return testResult; }
    }
}
