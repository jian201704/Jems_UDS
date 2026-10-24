using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    public abstract class CanDeviceCompatibilityTest : UdsTestBase
    {
        protected CanDeviceCompatibilityTest(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        protected ICanDeviceDiagnostics Diagnostics()
        {
            ICanDeviceProvider provider = CanTransportRegistry.Current;
            if (provider == null) throw new InvalidOperationException("CAN provider is not registered. Run CAN_Init first.");
            ICanDeviceDiagnostics diagnostics = provider as ICanDeviceDiagnostics;
            if (diagnostics == null) throw new NotSupportedException("The current CAN provider does not support device diagnostics.");
            return diagnostics;
        }

        protected void PassStatus(string name, string message)
        {
            SetReturn(0, true);
            SetReturn(1, message);
            UpdateStatusKey(name + ": OK - " + message);
            testResult.Status = TestStatus.Pass;
        }

        protected void FailStatus(string name, Exception error)
        {
            SetReturn(0, false);
            SetReturn(1, error.Message);
            UpdateStatusKey(name + ": FAIL - " + error.Message);
            testResult.Status = TestStatus.Pass;
        }
    }

    public sealed class ZCAN_GetDeviceInfo : CanDeviceCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_GetDeviceInfo(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                string info = Diagnostics().GetDeviceInfo();
                SetReturn(0, info);
                for (int i = 1; i <= 7; i++) SetReturn(i, "");
                UpdateStatusKey("ZCAN_GetDeviceInfo: OK - " + info);
                testResult.Status = TestStatus.Pass;
            }
            catch (Exception ex)
            {
                SetReturn(0, "");
                SetReturn(1, ex.Message);
                UpdateStatusKey("ZCAN_GetDeviceInfo: FAIL - " + ex.Message);
                testResult.Status = TestStatus.Pass;
            }
            return testResult;
        }
    }

    public sealed class ZCAN_SetValue_Str : CanDeviceCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_SetValue_Str(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                string path = ArgString(1, "Property Path", "");
                string value = ArgString(2, "Property Value", "");
                bool result = Diagnostics().SetValue(path, value);
                SetReturn(0, result);
                SetReturn(1, result ? "" : "Device configuration was rejected.");
                UpdateStatusKey("ZCAN_SetValue_Str: " + (result ? "OK" : "FAIL") + " - " + path);
                testResult.Status = TestStatus.Pass;
            }
            catch (Exception ex)
            {
                SetReturn(0, false);
                SetReturn(1, ex.Message);
                UpdateStatusKey("ZCAN_SetValue_Str: FAIL - " + ex.Message);
                testResult.Status = TestStatus.Pass;
            }
            return testResult;
        }
    }
}
