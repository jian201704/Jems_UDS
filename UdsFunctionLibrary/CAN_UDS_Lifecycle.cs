using System;
using System.IO;
using System.Reflection;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    public class CAN_UDS_Init : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }

        public CAN_UDS_Init(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string providerDll = ArgString(0, "Provider DLL", "");
            string providerType = ArgString(1, "Provider Type", "");
            string deviceAddress = ArgString(2, "Device Address", "");
            string serialNumber = ArgString(3, "Device Serial Number", "");
            string deviceKey = ArgString(4, "Device Key", "");
            ICanDeviceProvider provider = null;
            bool registered = false;
            try
            {
                if (string.IsNullOrWhiteSpace(providerDll))
                    throw new InvalidOperationException("missing provider DLL path");
                if (!File.Exists(providerDll))
                    throw new FileNotFoundException("CAN provider DLL not found", providerDll);
                if (string.IsNullOrWhiteSpace(providerType))
                    throw new InvalidOperationException("missing provider type name");

                Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(providerDll));
                Type type = assembly.GetType(providerType.Trim(), true, true);
                if (!typeof(ICanDeviceProvider).IsAssignableFrom(type))
                    throw new InvalidOperationException(providerType + " does not implement ICanDeviceProvider");

                provider = (ICanDeviceProvider)Activator.CreateInstance(type);
                ICanDeviceLifecycleProvider lifecycle = provider as ICanDeviceLifecycleProvider;
                IConfigurableCanDeviceProvider configurable = provider as IConfigurableCanDeviceProvider;
                ICanDeviceSelector selector = provider as ICanDeviceSelector;
                if (!string.IsNullOrWhiteSpace(serialNumber) && selector != null)
                    selector.OpenDeviceByIdentifier(deviceAddress.Trim(), serialNumber.Trim());
                else if (lifecycle != null)
                    lifecycle.OpenDevice(deviceAddress.Trim(), serialNumber.Trim());
                else if (configurable != null)
                    configurable.Configure(deviceAddress.Trim(), serialNumber.Trim());
                else
                    throw new InvalidOperationException(providerType + " does not implement a device open/configure interface.");
                if (string.IsNullOrWhiteSpace(deviceKey)) deviceKey = string.IsNullOrWhiteSpace(serialNumber) ? deviceAddress.Trim() : serialNumber.Trim();
                if (string.IsNullOrWhiteSpace(deviceKey)) deviceKey = "default";
                CanTransportRegistry.Register(deviceKey, provider);
                CanDeviceHandleRegistry.Register(provider);
                registered = true;

                SetReturn(0, true);
                SetReturn(1, providerType.Trim());
                SetReturn(2, "CAN provider registered");
                SetReturn(3, deviceAddress.Trim());
                SetReturn(4, serialNumber.Trim());
                SetReturn(5, deviceKey);
                UpdateStatusKey("CAN_UDS_Init: OK - " + providerType.Trim());
            }
            catch (Exception ex)
            {
                if (registered)
                    CanTransportRegistry.Remove(deviceKey);
                else if (provider is IDisposable)
                {
                    try { ((IDisposable)provider).Dispose(); } catch (Exception) { }
                }
                SetReturn(0, false);
                SetReturn(1, providerType.Trim());
                SetReturn(2, ex.Message);
                UpdateStatusKey("CAN_UDS_Init: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public class CAN_UDS_Release : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }

        public CAN_UDS_Release(ITestCellVariableSpace varSpace, object otherParameter)
            : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            CanKeepAliveManager.StopAll();
            RawCanChannel.CloseAllDevices();
            CanStepTransport.ClearInitializationDefaults();
            CanDeviceHandleRegistry.Clear();
            CanTransportRegistry.Clear();
            SetReturn(0, true);
            SetReturn(1, "CAN provider released");
            UpdateStatusKey("CAN_UDS_Release: OK");
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    // Clear lifecycle names for non-UDS CAN callers; CAN_UDS_* remains compatible.
    public sealed class CAN_Init : CAN_UDS_Init
    {
        public CAN_Init(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
    }

    public sealed class CAN_SelectDevice : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_SelectDevice(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            string key = ArgString(0, "Device Key", "");
            bool selected = CanTransportRegistry.Select(key);
            SetReturn(0, selected);
            SetReturn(1, selected ? key : "Unknown device key: " + key);
            UpdateStatusKey("CAN_SelectDevice: " + (selected ? "OK - " : "FAIL - ") + key);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class CAN_CloseDevice : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_CloseDevice(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            string key = ArgString(0, "Device Key", "");
            try
            {
                ICanDeviceProvider provider = CanTransportRegistry.Get(key);
                if (provider == null) throw new InvalidOperationException("Unknown device key: " + key);
                CanKeepAliveManager.StopDevice(key);
                RawCanChannel.CloseForProvider(provider);
                CanDeviceHandleRegistry.Remove(provider);
                CanTransportRegistry.Remove(key);
                SetReturn(0, true);
                SetReturn(1, "CAN device released: " + key);
                UpdateStatusKey("CAN_CloseDevice: OK - " + key);
            }
            catch (Exception ex)
            {
                SetReturn(0, false);
                SetReturn(1, ex.Message);
                UpdateStatusKey("CAN_CloseDevice: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class CAN_Close : CAN_UDS_Release
    {
        public CAN_Close(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
    }
}