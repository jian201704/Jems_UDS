using JabilTest;
using ScriptVariableSpaceInterface;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// Reads a named value from the JEMSts variable space.
    /// Script usage: ($value) = Script_GetVariable("Result_step_expectedContext")
    /// ReturnValue0 contains the value converted to text.
    /// </summary>
    internal class Script_GetVariable : UdsTestBase
    {
        public override string Version => "1.00.00";

        public Script_GetVariable(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        public override TestResult Execute()
        {
            string variableName = ArgString(0, "Variable Name", "");
            string value = string.IsNullOrWhiteSpace(variableName)
                ? ""
                : GetNamedString(variableName.Trim(), "");

            ClearReturns();
            SetReturn(0, value);
            UpdateStatusKey("Script_GetVariable: " + variableName + " = " + value);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
