using System;
using System.IO;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    internal static class ZNetCompat
    {
        internal static IZNetCapability Capability()
        {
            IZNetProvider provider = CanTransportRegistry.Current as IZNetProvider;
            if (provider == null) throw new InvalidOperationException("Current provider does not support ZNetConfig.dll.");
            return provider.OpenZNet();
        }
        internal static IntPtr Handle(object value) { return value is IntPtr ? (IntPtr)value : new IntPtr(Convert.ToInt64(value)); }
        internal static bool SetBySerial(string serial, string name, string value, bool reset, string password)
        {
            IZNetCapability capability = Capability();
            for (uint index = 0; index < capability.GetDeviceCount(); index++)
            {
                IntPtr handle = capability.GetDeviceAt(index);
                ZNetProperty property;
                if (capability.GetProperty(handle, "SERIES_NUM", out property) && string.Equals(property.Value, serial, StringComparison.OrdinalIgnoreCase))
                {
                    if (!capability.SetProperty(handle, name, value)) return false;
                    return !reset || capability.Reset(handle, password);
                }
            }
            return false;
        }
    }

    public abstract class ZNetCompatibilityTest : UdsTestBase
    {
        protected ZNetCompatibilityTest(ITestCellVariableSpace space, object parameter) : base(space, parameter) { }
        protected TestResult Error(string name, Exception error) { SetReturn(0, false); SetReturn(1, error.Message); UpdateStatusKey(name + ": FAIL - " + error.Message); testResult.Status = TestStatus.Pass; return testResult; }
    }

    public sealed class ZNet_SearchDevice : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_SearchDevice(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().Search((uint)ArgInt(0, "time out", 2000), ArgString(1, "IP", ""))); } catch (Exception e) { return Error("ZNet_SearchDevice", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_AddDevice : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_AddDevice(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().AddDevice((uint)ArgInt(0, "devType", 0))); } catch (Exception e) { return Error("ZNet_AddDevice", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_DeleteDevice : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_DeleteDevice(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().DeleteDevice(ZNetCompat.Handle(ArgObject(0, "handle")))); } catch (Exception e) { return Error("ZNet_DeleteDevice", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_ClearDevice : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_ClearDevice(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().ClearDevices()); } catch (Exception e) { return Error("ZNet_ClearDevice", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_GetDeviceCount : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_GetDeviceCount(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, (int)ZNetCompat.Capability().GetDeviceCount()); } catch (Exception e) { return Error("ZNet_GetDeviceCount", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_GetDeviceAt : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_GetDeviceAt(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().GetDeviceAt((uint)ArgInt(0, "index", 0))); } catch (Exception e) { return Error("ZNet_GetDeviceAt", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_GetDeviceCountToFile : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.10.01"; } }
        public ZNet_GetDeviceCountToFile(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute()
        {
            try
            {
                IZNetCapability capability = ZNetCompat.Capability();
                uint count = capability.GetDeviceCount();
                string path = Path.Combine(Environment.CurrentDirectory, "ZLG_CANNetDevice_List.ini");
                using (var writer = new StreamWriter(path, false))
                {
                    for (uint index = 0; index < count; index++)
                    {
                        IntPtr handle = capability.GetDeviceAt(index);
                        ZNetProperty name; ZNetProperty serial; ZNetProperty ip;
                        capability.GetProperty(handle, "NAME", out name);
                        capability.GetProperty(handle, "SERIES_NUM", out serial);
                        capability.GetProperty(handle, "IP", out ip);
                        writer.WriteLine("[Dev{0}]", index);
                        writer.WriteLine("NAME={0}", name.Value);
                        writer.WriteLine("SERIES_NUM={0}", serial.Value);
                        writer.WriteLine("IP={0}", ip.Value);
                    }
                }
                SetReturn(0, (int)count); SetReturn(1, EnumerableHandles(capability, count));
            }
            catch (Exception e) { return Error("ZNet_GetDeviceCountToFile", e); }
            testResult.Status = TestStatus.Pass; return testResult;
        }
        private static IntPtr[] EnumerableHandles(IZNetCapability capability, uint count)
        {
            var handles = new IntPtr[count];
            for (uint index = 0; index < count; index++) handles[index] = capability.GetDeviceAt(index);
            return handles;
        }
    }
    public sealed class ZNet_LoadInfo : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_LoadInfo(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().LoadInfo(ZNetCompat.Handle(ArgObject(0, "handle")))); } catch (Exception e) { return Error("ZNet_LoadInfo", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_GetProperty : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_GetProperty(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { ZNetProperty property; bool ok = ZNetCompat.Capability().GetProperty(ZNetCompat.Handle(ArgObject(0, "handle")), ArgString(1, "Propery Name", ""), out property); SetReturn(0, ok); SetReturn(1, property.Value); } catch (Exception e) { return Error("ZNet_GetProperty", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_SetProperty : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_SetProperty(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().SetProperty(ZNetCompat.Handle(ArgObject(0, "handle")), ArgString(1, "Propery Name", ""), ArgString(2, "Propery Value", ""))); } catch (Exception e) { return Error("ZNet_SetProperty", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_SubmitInfo : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_SubmitInfo(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().SubmitInfo(ZNetCompat.Handle(ArgObject(0, "handle")), ArgString(1, "Pwd", ""))); } catch (Exception e) { return Error("ZNet_SubmitInfo", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_Reset : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_Reset(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().Reset(ZNetCompat.Handle(ArgObject(0, "handle")), ArgString(1, "Pwd", ""))); } catch (Exception e) { return Error("ZNet_Reset", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_FactoryReset : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_FactoryReset(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.Capability().FactoryReset(ZNetCompat.Handle(ArgObject(0, "handle")), ArgString(1, "Pwd", ""))); } catch (Exception e) { return Error("ZNet_FactoryReset", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_SetPropertyBySN : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_SetPropertyBySN(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.SetBySerial(ArgString(0, "SN", ""), ArgString(1, "Propery Name", ""), ArgString(2, "Propery Value", ""), false, "")); } catch (Exception e) { return Error("ZNet_SetPropertyBySN", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
    public sealed class ZNet_SetPropertyBySnAndReset : ZNetCompatibilityTest
    {
        public override string Version { get { return "1.00.00"; } }
        public ZNet_SetPropertyBySnAndReset(ITestCellVariableSpace s, object p) : base(s, p) { }
        public override TestResult Execute() { try { SetReturn(0, ZNetCompat.SetBySerial(ArgString(0, "SN", ""), ArgString(1, "Propery Name", ""), ArgString(2, "Propery Value", ""), true, ArgString(3, "Pwd", ""))); } catch (Exception e) { return Error("ZNet_SetPropertyBySnAndReset", e); } testResult.Status = TestStatus.Pass; return testResult; }
    }
}
