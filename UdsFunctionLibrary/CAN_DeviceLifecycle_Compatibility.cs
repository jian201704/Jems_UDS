using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    public sealed class ZCAN_OpenDevice : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_OpenDevice(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                ICanDeviceProvider provider = CanTransportRegistry.Current;
                if (provider == null) throw new InvalidOperationException("CAN provider is not registered. Run CAN_Init first.");
                uint deviceType = (uint)Math.Max(0, ArgInt(0, "device_type", 4));
                uint deviceIndex = (uint)Math.Max(0, ArgInt(1, "device_index", 0));
                string portInfo = ArgString(2, "Port_Info", "");
                string address = deviceType + "," + deviceIndex;
                ICanDeviceLifecycleProvider lifecycle = provider as ICanDeviceLifecycleProvider;
                IConfigurableCanDeviceProvider configurable = provider as IConfigurableCanDeviceProvider;
                if (lifecycle != null) lifecycle.OpenDevice(address, portInfo);
                else if (configurable != null) configurable.Configure(address, portInfo);
                else throw new NotSupportedException("The current CAN provider cannot open a device.");
                IntPtr token = CanDeviceHandleRegistry.Register(provider);
                SetReturn(0, token);
                SetReturn(1, true);
                UpdateStatusKey("ZCAN_OpenDevice: OK - type=" + deviceType + ", index=" + deviceIndex);
            }
            catch (Exception ex)
            {
                SetReturn(0, IntPtr.Zero);
                SetReturn(1, false);
                SetReturn(2, ex.Message);
                UpdateStatusKey("ZCAN_OpenDevice: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public abstract class ZCAN_InitChannelCompatibilityTest : UdsTestBase
    {
        protected ZCAN_InitChannelCompatibilityTest(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        protected TestResult Initialize(bool canFd)
        {
            try
            {
                ICanDeviceProvider provider = CanDeviceHandleRegistry.Resolve(ArgObject(0, "device_handle"));
                int channelIndex = Math.Max(0, ArgInt(1, "channel_index", 0));
                CanChannelOptions options = new CanChannelOptions
                {
                    ChannelIndex = channelIndex,
                    CanFd = canFd,
                    CanFdStandard = canFd ? ArgInt(2, "CANFDStandard", 0) : 0,
                    NominalBitrate = canFd ? Normalize(ArgInt(3, "arbit_baudrate", 500000)) : Normalize(ArgInt(2, "arbit_baudrate", 500000)),
                    DataBitrate = canFd ? Normalize(ArgInt(4, "data_baudrate", 2000000)) : 2000000,
                    CanFdMode = 0
                };
                ICanChannel channel = provider.OpenChannel(options);
                channel.Initialize();
                IntPtr token = CanChannelHandleRegistry.RegisterNew(channel, options);
                SetReturn(0, token);
                SetReturn(1, true);
                UpdateStatusKey((canFd ? "ZCANFD_InitCAN" : "ZCAN_InitCAN") + ": OK - channel " + channelIndex);
            }
            catch (Exception ex)
            {
                SetReturn(0, IntPtr.Zero);
                SetReturn(1, false);
                SetReturn(2, ex.Message);
                UpdateStatusKey((canFd ? "ZCANFD_InitCAN" : "ZCAN_InitCAN") + ": FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        private static int Normalize(int value)
        {
            return value > 0 && value < 100000 ? value * 1000 : value;
        }
    }

    public sealed class ZCAN_InitCAN : ZCAN_InitChannelCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_InitCAN(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return Initialize(false); }
    }

    public sealed class ZCANFD_InitCAN : ZCAN_InitChannelCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCANFD_InitCAN(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute() { return Initialize(true); }
    }

    public sealed class ZCAN_CloseDevice : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_CloseDevice(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                ICanDeviceProvider provider = CanDeviceHandleRegistry.Resolve(ArgObject(0, "device_handle"));
                RawCanChannel.CloseForProvider(provider);
                CanDeviceHandleRegistry.Remove(provider);
                string key = CanTransportRegistry.GetKey(provider);
                CanTransportRegistry.Remove(key);
                SetReturn(0, 1);
                UpdateStatusKey("ZCAN_CloseDevice: OK");
            }
            catch (Exception ex)
            {
                SetReturn(0, 0);
                SetReturn(1, ex.Message);
                UpdateStatusKey("ZCAN_CloseDevice: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
