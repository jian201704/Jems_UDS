using System;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    public sealed class CAN_SetCustomBitrate : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_SetCustomBitrate(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                int channelIndex = ArgInt(0, "Channel Index", 0);
                string bitrateConfig = ArgString(1, "Custom Bitrate Config", "");
                string deviceKey = ArgString(2, "Device Key", "");
                bool result = RawCanChannel.SetCustomBitrate(channelIndex, bitrateConfig, deviceKey);
                SetReturn(0, result);
                SetReturn(1, result ? "" : "The provider rejected the custom bitrate.");
                UpdateStatusKey("CAN_SetCustomBitrate: " + (result ? "OK" : "FAIL"));
            }
            catch (Exception ex)
            {
                SetReturn(0, false);
                SetReturn(1, ex.Message);
                UpdateStatusKey("CAN_SetCustomBitrate: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
