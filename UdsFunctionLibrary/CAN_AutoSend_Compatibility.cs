using System;
using System.Collections.Generic;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    public abstract class CanAutoSendCompatibilityTest : UdsTestBase
    {
        protected CanAutoSendCompatibilityTest(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        protected ICanAutoSendCapability Capability()
        {
            ICanDeviceProvider provider = CanDeviceHandleRegistry.Resolve(ArgObject(0, "device_handle"));
            ICanAutoSendCapability capability = provider as ICanAutoSendCapability;
            if (capability == null) throw new NotSupportedException("The current CAN provider does not support automatic transmission.");
            return capability;
        }

        protected CanFrame Frame()
        {
            return new CanFrame
            {
                Identifier = RawCanChannel.ParseId(ArgString(4, "CANID", "0")),
                Extended = ArgInt(5, "FrameType", 0) != 0,
                Data = RawCanChannel.ParseData(ArgString(6, "CANdata", ""))
            };
        }

        protected TestResult Finish(string name, bool result)
        {
            SetReturn(0, result.ToString());
            UpdateStatusKey(name + ": " + result);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        protected TestResult Fail(string name, Exception error)
        {
            SetReturn(0, "False");
            SetReturn(1, error.Message);
            UpdateStatusKey(name + ": FAIL - " + error.Message);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZCAN_AddSingleAutoSendFrame : CanAutoSendCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_AddSingleAutoSendFrame(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                int channel = ArgInt(1, "channel_index", 0);
                ushort index = (ushort)Math.Max(0, ArgInt(2, "frame_index", 0));
                bool fd = ArgInt(3, "can_type", 0) != 0;
                uint interval = (uint)Math.Max(0, ArgInt(7, "Interval", 100));
                return Finish("ZCAN_AddSingleAutoSendFrame", Capability().SetAutoSend(channel, index, fd, true, Frame(), interval));
            }
            catch (Exception ex) { return Fail("ZCAN_AddSingleAutoSendFrame", ex); }
        }
    }

    public sealed class ZCAN_CancelSingleAutoSendFrame : CanAutoSendCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_CancelSingleAutoSendFrame(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                int channel = ArgInt(1, "channel_index", 0);
                ushort index = (ushort)Math.Max(0, ArgInt(2, "frame_index", 0));
                bool fd = ArgInt(3, "can_type", 0) != 0;
                uint interval = (uint)Math.Max(0, ArgInt(7, "Interval", 100));
                return Finish("ZCAN_CancelSingleAutoSendFrame", Capability().SetAutoSend(channel, index, fd, false, Frame(), interval));
            }
            catch (Exception ex) { return Fail("ZCAN_CancelSingleAutoSendFrame", ex); }
        }
    }

    public sealed class ZCAN_AutoSend_Enable : CanAutoSendCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_AutoSend_Enable(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { return Finish("ZCAN_AutoSend_Enable", Capability().ApplyAutoSend(ArgInt(1, "channel_index", 0))); }
            catch (Exception ex) { return Fail("ZCAN_AutoSend_Enable", ex); }
        }
    }

    public sealed class ZCAN_AutoSend_Disable : CanAutoSendCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_AutoSend_Disable(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { return Finish("ZCAN_AutoSend_Disable", Capability().ClearAutoSend(ArgInt(1, "channel_index", 0))); }
            catch (Exception ex) { return Fail("ZCAN_AutoSend_Disable", ex); }
        }
    }
}
