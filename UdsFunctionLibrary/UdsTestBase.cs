using System;
using System.Text.RegularExpressions;
using JabilTest;
using ScriptVariableSpaceInterface;

namespace UdsFunctionLibrary
{
    /// <summary>
    /// Common base for all UDS functions. Provides typed argument reading and
    /// return-value setting helpers for the JEMSts variable space.
    /// </summary>
    public abstract class UdsTestBase : Test
    {
        protected UdsTestBase(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter)
        {
        }

        // ---- argument helpers (missing args -> fallback, never throw) ----

        protected string ArgString(int index, string argumentName, string fallback)
        {
            try { return GetStringVariable("Argument" + index, argumentName); }
            catch (Exception) { return fallback; }
        }

        protected int ArgInt(int index, string argumentName, int fallback)
        {
            try { return GetIntegerVariable("Argument" + index, argumentName); }
            catch (Exception) { return fallback; }
        }

        protected double ArgDouble(int index, string argumentName, double fallback)
        {
            try { return GetFloatVariable("Argument" + index, argumentName); }
            catch (Exception) { return fallback; }
        }

        internal object ArgObject(int index, string argumentName)
        {
            return GetObjectVariable("Argument" + index, argumentName);
        }

        // ---- return helpers ----

        /// <summary>Set ReturnValueN with a VariableType that matches the runtime type.</summary>
        internal void SetReturn(int index, object value)
        {
            VariableSpace.SetVariable(new ScriptVariable("ReturnValue" + index, TypeOf(value), value));
        }

        internal void SetPersistent(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string scriptName = name.Trim().Replace('.', '_');
            VariableSpace.SetVariable(new ScriptVariable(scriptName, TypeOf(value), value));
        }

        internal string GetNamedString(string name, string fallback = "")
        {
            try { return GetStringVariable(name, name); }
            catch (Exception) { return fallback; }
        }

        internal int GetNamedInteger(string name, int fallback = 0)
        {
            try { return GetIntegerVariable(name, name); }
            catch (Exception) { return fallback; }
        }

        internal double GetNamedDouble(string name, double fallback = 0.0)
        {
            try { return GetFloatVariable(name, name); }
            catch (Exception) { return fallback; }
        }

        internal bool GetNamedBoolean(string name, bool fallback = false)
        {
            string value = GetNamedString(name, null);
            if (value == null) return fallback;
            bool parsed;
            if (bool.TryParse(value, out parsed)) return parsed;
            if (value == "1") return true;
            if (value == "0") return false;
            return fallback;
        }

        internal string ExpandVariables(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"\$\{([^}]+)\}", delegate(Match match)
            {
                string name = match.Groups[1].Value.Trim();
                if (name.Length == 0) return match.Value;
                string value = GetNamedString(name, null);
                if (value != null) return value;
                try { return GetIntegerVariable(name, name).ToString(); }
                catch (Exception) { }
                try { return GetFloatVariable(name, name).ToString(System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception) { return match.Value; }
            });
        }

        /// <summary>清空所有 ReturnValue（RV0-RV7），避免非 UDS 步骤残留上一步的数据。</summary>
        internal void ClearReturns()
        {
            SetReturn(0, 0);
            for (int i = 1; i <= 7; ++i) SetReturn(i, "");
        }

        private static VariableType TypeOf(object value)
        {
            if (value == null) return VariableType.Object;
            Type t = value.GetType();
            if (t == typeof(string)) return VariableType.String;
            if (t == typeof(bool)) return VariableType.Boolean;
            if (t == typeof(int)) return VariableType.Integer;
            if (t == typeof(long)) return VariableType.Long;
            if (t == typeof(double) || t == typeof(float)) return VariableType.Double;
            if (t == typeof(uint) || t == typeof(ulong)) return VariableType.Long;
            return VariableType.Object;
        }

        internal void UpdateStatus(string message)
        {
            try { VariableSpace.UpdateStatusText(message); }
            catch (Exception) { }
        }

        /// <summary>
        /// Print KEY information via UpdateStatusTextFromUpdateStatus (the channel JEMSts's own
        /// FailTest uses). Unlike UpdateStatusText it is recorded for every verbosity level except 0,
        /// so important verdicts/errors always show up.
        /// </summary>
        internal void UpdateStatusKey(string message)
        {
            try { VariableSpace.UpdateStatusTextFromUpdateStatus(message); }
            catch (Exception) { }
        }
    }
}
