using System;
using System.Collections.Generic;
using JabilTest;
using ScriptVariableSpaceInterface;
using ExcelDecodeCore;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    internal static class CanCompatibilityFormatting
    {
        internal static string Format(IList<CanFrame> frames)
        {
            var lines = new List<string>();
            foreach (CanFrame frame in frames)
            {
                string id = frame.Extended ? frame.Identifier.ToString("X8") : frame.Identifier.ToString("X4");
                lines.Add("ID:" + id + ",data:" + HexUtils.ToHexString(frame.Data) + ";");
            }
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        internal static ICanChannel Resolve(UdsTestBase test, string name)
        {
            return CanChannelHandleRegistry.Resolve(test.ArgObject(0, name));
        }
    }

    public abstract class CanReceiveCompatibilityTest : UdsTestBase
    {
        protected CanReceiveCompatibilityTest(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        protected TestResult ExecuteReceive(bool canFd, string name)
        {
            try
            {
                ICanChannel channel = CanCompatibilityFormatting.Resolve(this, "channel_handle");
                ICanTypedReceiveChannel typed = channel as ICanTypedReceiveChannel;
                IList<CanFrame> frames = typed == null
                    ? channel.Receive(100)
                    : typed.Receive(100, canFd ? 2 : 1);
                SetReturn(0, CanCompatibilityFormatting.Format(frames));
                SetReturn(1, frames.Count);
                UpdateStatusKey(name + ": " + frames.Count + " frame(s)");
            }
            catch (Exception ex)
            {
                SetReturn(0, "");
                SetReturn(1, 0);
                SetReturn(2, ex.Message);
                UpdateStatusKey(name + ": FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        protected TestResult ExecuteCount(bool canFd, string name)
        {
            try
            {
                ICanChannel channel = CanCompatibilityFormatting.Resolve(this, "channel_handle");
                ICanReceiveCountChannel counted = channel as ICanReceiveCountChannel;
                if (counted == null) throw new NotSupportedException("The current CAN provider does not expose receive queue counts.");
                int count = counted.GetReceiveCount(canFd);
                SetReturn(0, count);
                UpdateStatusKey(name + ": " + count + " frame(s)");
            }
            catch (Exception ex)
            {
                SetReturn(0, 0);
                SetReturn(1, ex.Message);
                UpdateStatusKey(name + ": FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        protected TestResult ExecuteTransmitReceive(bool canFd, string name)
        {
            try
            {
                ICanChannel channel = CanCompatibilityFormatting.Resolve(this, "channel_handle");
                uint identifier = RawCanChannel.ParseId(ArgString(1, "CANID", "0"));
                int frameType = ArgInt(2, "FrameType", 0);
                byte[] data = RawCanChannel.ParseData(ArgString(3, "CANdata", ""));
                bool brs = canFd && ArgInt(4, "CANFD_EXP", 0) != 0;
                int sent = channel.Send(new List<CanFrame> { new CanFrame { Identifier = identifier, Extended = frameType != 0, BitRateSwitch = brs, Data = data } });
                if (sent <= 0) throw new InvalidOperationException("CAN transmit returned zero.");
                ICanTypedReceiveChannel typed = channel as ICanTypedReceiveChannel;
                IList<CanFrame> frames = typed == null ? channel.Receive(500) : typed.Receive(500, canFd ? 2 : 1);
                SetReturn(0, CanCompatibilityFormatting.Format(frames));
                SetReturn(1, frames.Count);
                UpdateStatusKey(name + ": sent=" + sent + ", received=" + frames.Count);
            }
            catch (Exception ex)
            {
                SetReturn(0, "");
                SetReturn(1, 0);
                SetReturn(2, ex.Message);
                UpdateStatusKey(name + ": FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZCAN_GetReceiveNum : CanReceiveCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_GetReceiveNum(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return ExecuteCount(false, "ZCAN_GetReceiveNum"); }
    }

    public sealed class ZCANFD_GetReceiveNum : CanReceiveCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCANFD_GetReceiveNum(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return ExecuteCount(true, "ZCANFD_GetReceiveNum"); }
    }

    public sealed class ZCAN_Receive : CanReceiveCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_Receive(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return ExecuteReceive(false, "ZCAN_Receive"); }
    }

    public sealed class ZCANFD_Receive : CanReceiveCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCANFD_Receive(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return ExecuteReceive(true, "ZCANFD_Receive"); }
    }

    public sealed class ZCAN_Transmit_Receive : CanReceiveCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_Transmit_Receive(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return ExecuteTransmitReceive(false, "ZCAN_Transmit_Receive"); }
    }

    public sealed class ZCANFD_Transmit_Receive : CanReceiveCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCANFD_Transmit_Receive(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return ExecuteTransmitReceive(true, "ZCANFD_Transmit_Receive"); }
    }
}
