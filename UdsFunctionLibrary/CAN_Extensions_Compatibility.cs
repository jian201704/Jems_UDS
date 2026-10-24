using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    public sealed class CAN_SetDeviceValue : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_SetDeviceValue(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string deviceKey = ArgString(0, "Device Key", "");
            string path = ArgString(1, "Property Path", "");
            string value = ArgString(2, "Property Value", "");
            try
            {
                ICanDeviceProvider provider = CanTransportRegistry.Get(deviceKey);
                if (provider == null) throw new InvalidOperationException("CAN provider is not registered for device key: " + deviceKey);
                ICanDeviceDiagnostics diagnostics = provider as ICanDeviceDiagnostics;
                if (diagnostics == null) throw new NotSupportedException("The selected CAN provider does not support device configuration.");
                bool result = diagnostics.SetValue(path, value);
                SetReturn(0, result);
                SetReturn(1, result ? "" : "Device configuration was rejected: " + path);
                UpdateStatusKey("CAN_SetDeviceValue: " + (result ? "OK" : "FAIL") + " - " + deviceKey + "/" + path);
            }
            catch (Exception ex)
            {
                SetReturn(0, false);
                SetReturn(1, ex.Message);
                UpdateStatusKey("CAN_SetDeviceValue: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class CAN_SetFilter : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_SetFilter(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string deviceKey = ArgString(0, "Device Key", "");
            int channelIndex = ArgInt(1, "Channel Index", 0);
            string mode = ArgString(2, "Filter Mode", "0");
            string startId = ArgString(3, "Start ID", "0");
            string endId = ArgString(4, "End ID", "0");
            try
            {
                ICanDeviceProvider provider = CanTransportRegistry.Get(deviceKey);
                if (provider == null) throw new InvalidOperationException("CAN provider is not registered for device key: " + deviceKey);
                ICanDeviceDiagnostics diagnostics = provider as ICanDeviceDiagnostics;
                if (diagnostics == null) throw new NotSupportedException("The selected CAN provider does not support CAN filters.");
                string prefix = channelIndex.ToString();
                bool result = diagnostics.SetValue(prefix + "/filter_mode", mode);
                result = diagnostics.SetValue(prefix + "/filter_start", startId) && result;
                result = diagnostics.SetValue(prefix + "/filter_end", endId) && result;
                SetReturn(0, result);
                SetReturn(1, result ? "" : "CAN filter configuration was rejected.");
                UpdateStatusKey("CAN_SetFilter: " + (result ? "OK" : "FAIL") + " - " + deviceKey + ", channel=" + channelIndex);
            }
            catch (Exception ex)
            {
                SetReturn(0, false);
                SetReturn(1, ex.Message);
                UpdateStatusKey("CAN_SetFilter: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class CAN_EnableFilter : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_EnableFilter(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string deviceKey = ArgString(0, "Device Key", "");
            int channelIndex = ArgInt(1, "Channel Index", 0);
            return SetFilterValue(deviceKey, channelIndex, "/filter_ack", "0");
        }

        private TestResult SetFilterValue(string deviceKey, int channelIndex, string suffix, string value)
        {
            try
            {
                ICanDeviceProvider provider = CanTransportRegistry.Get(deviceKey);
                if (provider == null) throw new InvalidOperationException("CAN provider is not registered for device key: " + deviceKey);
                ICanDeviceDiagnostics diagnostics = provider as ICanDeviceDiagnostics;
                if (diagnostics == null) throw new NotSupportedException("The selected CAN provider does not support CAN filters.");
                bool result = diagnostics.SetValue(channelIndex.ToString() + suffix, value);
                SetReturn(0, result);
                SetReturn(1, result ? "" : "CAN filter operation was rejected.");
                UpdateStatusKey("CAN_EnableFilter: " + (result ? "OK" : "FAIL") + " - " + deviceKey + ", channel=" + channelIndex);
            }
            catch (Exception ex)
            {
                SetReturn(0, false);
                SetReturn(1, ex.Message);
                UpdateStatusKey("CAN_EnableFilter: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class CAN_ClearFilter : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_ClearFilter(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string deviceKey = ArgString(0, "Device Key", "");
            int channelIndex = ArgInt(1, "Channel Index", 0);
            try
            {
                ICanDeviceProvider provider = CanTransportRegistry.Get(deviceKey);
                if (provider == null) throw new InvalidOperationException("CAN provider is not registered for device key: " + deviceKey);
                ICanDeviceDiagnostics diagnostics = provider as ICanDeviceDiagnostics;
                if (diagnostics == null) throw new NotSupportedException("The selected CAN provider does not support CAN filters.");
                bool result = diagnostics.SetValue(channelIndex.ToString() + "/filter_clear", "0");
                SetReturn(0, result);
                SetReturn(1, result ? "" : "CAN filter clear operation was rejected.");
                UpdateStatusKey("CAN_ClearFilter: " + (result ? "OK" : "FAIL") + " - " + deviceKey + ", channel=" + channelIndex);
            }
            catch (Exception ex)
            {
                SetReturn(0, false);
                SetReturn(1, ex.Message);
                UpdateStatusKey("CAN_ClearFilter: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public abstract class CanExtensionCompatibilityTest : UdsTestBase
    {
        protected CanExtensionCompatibilityTest(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        protected ICanDeviceDiagnostics DeviceDiagnostics()
        {
            ICanDeviceProvider provider = CanDeviceHandleRegistry.Resolve(ArgObject(0, "device_handle"));
            ICanDeviceDiagnostics diagnostics = provider as ICanDeviceDiagnostics;
            if (diagnostics == null) throw new NotSupportedException("The current CAN provider does not support device configuration.");
            return diagnostics;
        }

        protected bool SetDeviceValue(string path, string value)
        {
            return DeviceDiagnostics().SetValue(path, value);
        }

        protected TestResult Result(string name, object value)
        {
            SetReturn(0, value);
            UpdateStatusKey(name + ": " + value);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        protected TestResult Failure(string name, Exception error)
        {
            SetReturn(0, false);
            SetReturn(1, error.Message);
            UpdateStatusKey(name + ": FAIL - " + error.Message);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZCAN_InterRes_Enable : CanExtensionCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_InterRes_Enable(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { return Result("ZCAN_InterRes_Enable", SetDeviceValue(ArgInt(1, "channel_index", 0) + "/initenal_resistance", "1")); }
            catch (Exception ex) { return Failure("ZCAN_InterRes_Enable", ex); }
        }
    }

    public sealed class ZCAN_InterRes_Disable : CanExtensionCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_InterRes_Disable(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { return Result("ZCAN_InterRes_Disable", SetDeviceValue(ArgInt(1, "channel_index", 0) + "/initenal_resistance", "0").ToString()); }
            catch (Exception ex) { return Failure("ZCAN_InterRes_Disable", ex); }
        }
    }

    public sealed class ZCAN_SetFilter : CanExtensionCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_SetFilter(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                string channel = ArgInt(1, "channel_index", 0).ToString();
                bool result = SetDeviceValue(channel + "/filter_mode", ArgInt(2, "filter_mode", 0).ToString());
                result = SetDeviceValue(channel + "/filter_start", ArgString(3, "start_ID", "0")) && result;
                result = SetDeviceValue(channel + "/filter_end", ArgString(4, "end_ID", "0")) && result;
                return Result("ZCAN_SetFilter", result);
            }
            catch (Exception ex) { return Failure("ZCAN_SetFilter", ex); }
        }
    }

    public sealed class ZCAN_EnableFilter : CanExtensionCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_EnableFilter(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { return Result("ZCAN_EnableFilter", SetDeviceValue(ArgInt(1, "channel_index", 0) + "/filter_ack", "0")); }
            catch (Exception ex) { return Failure("ZCAN_EnableFilter", ex); }
        }
    }

    public sealed class ZCAN_ClearFilter : CanExtensionCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_ClearFilter(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try { return Result("ZCAN_ClearFilter", SetDeviceValue(ArgInt(1, "channel_index", 0) + "/filter_clear", "0") ? 1 : 0); }
            catch (Exception ex) { return Failure("ZCAN_ClearFilter", ex); }
        }
    }

    public sealed class ZCAN_ResetCAN : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_ResetCAN(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            try
            {
                ICanChannel channel = CanChannelHandleRegistry.Resolve(ArgObject(0, "channel_handle"));
                ICanResetChannel reset = channel as ICanResetChannel;
                if (reset == null) throw new NotSupportedException("The current CAN provider does not support channel reset.");
                reset.Reset();
                SetReturn(0, 1);
                UpdateStatusKey("ZCAN_ResetCAN: OK");
            }
            catch (Exception ex)
            {
                SetReturn(0, 0);
                SetReturn(1, ex.Message);
                UpdateStatusKey("ZCAN_ResetCAN: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
