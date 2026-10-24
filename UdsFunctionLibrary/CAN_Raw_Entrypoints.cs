using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JabilTest;
using ScriptVariableSpaceInterface;
using ExcelDecodeCore;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    internal static class RawCanChannel
    {
        private static readonly ConcurrentDictionary<string, ICanChannel> OpenChannels = new ConcurrentDictionary<string, ICanChannel>();
        private static readonly ConcurrentDictionary<string, CanChannelOptions> ChannelOptions = new ConcurrentDictionary<string, CanChannelOptions>();
        private static readonly ConcurrentDictionary<string, string> CustomBitrateConfigs = new ConcurrentDictionary<string, string>();
        private static readonly ConcurrentDictionary<string, ICanDeviceProvider> ChannelProviders = new ConcurrentDictionary<string, ICanDeviceProvider>();
        private static readonly ConcurrentDictionary<string, object> ChannelLocks = new ConcurrentDictionary<string, object>();

        private static string Key(int channelIndex)
        {
            return CanTransportRegistry.CurrentKey + ":" + channelIndex.ToString();
        }

        private static string Key(string deviceKey, int channelIndex)
        {
            return (string.IsNullOrWhiteSpace(deviceKey) ? CanTransportRegistry.CurrentKey : deviceKey.Trim()) + ":" + channelIndex.ToString();
        }

        private static ICanDeviceProvider Provider(string deviceKey)
        {
            ICanDeviceProvider provider = string.IsNullOrWhiteSpace(deviceKey) ? CanTransportRegistry.Current : CanTransportRegistry.Get(deviceKey.Trim());
            if (provider == null) throw new InvalidOperationException("CAN provider is not registered for device key: " + deviceKey);
            return provider;
        }

        public static object Sync(int channelIndex, string deviceKey)
        {
            return ChannelLocks.GetOrAdd(Key(deviceKey, channelIndex), delegate(string ignored) { return new object(); });
        }

        public static ICanChannel Open(int channelIndex, bool canFd, bool bitRateSwitch, int nominalBitrate, int dataBitrate, int canFdStandard, int canFdMode)
        {
            return Open(channelIndex, canFd, bitRateSwitch, nominalBitrate, dataBitrate, canFdStandard, canFdMode, "");
        }

        public static ICanChannel Open(int channelIndex, bool canFd, bool bitRateSwitch, int nominalBitrate, int dataBitrate, int canFdStandard, int canFdMode, string deviceKey)
        {
            ICanDeviceProvider provider = Provider(deviceKey);
            string key = Key(deviceKey, channelIndex);
            lock (Sync(channelIndex, deviceKey))
            {
                ICanChannel existing;
                if (OpenChannels.TryGetValue(key, out existing)) return existing;
                var options = new CanChannelOptions
                {
                    ChannelIndex = channelIndex,
                    CanFd = canFd,
                    BitRateSwitch = bitRateSwitch,
                    NominalBitrate = nominalBitrate,
                    DataBitrate = dataBitrate,
                    CanFdStandard = canFdStandard,
                    CanFdMode = canFdMode,
                    CustomBitrateConfig = GetCustomBitrate(channelIndex, deviceKey)
                };
                ICanChannel channel = provider.OpenChannel(options);
                channel.Initialize();
                channel.Start();
                OpenChannels[key] = channel;
                ChannelOptions[key] = options;
                ChannelProviders[key] = provider;
                CanChannelHandleRegistry.RegisterNew(channel, options);
                return channel;
            }
        }

        public static bool SetCustomBitrate(int channelIndex, string bitrateConfig)
        {
            ICanDeviceProvider provider = CanTransportRegistry.Current;
            ICanCustomBitrateCapability capability = provider as ICanCustomBitrateCapability;
            if (capability == null) throw new NotSupportedException("The current CAN provider does not support custom bitrate.");
            bool result = capability.SetCustomBitrate(channelIndex, bitrateConfig);
            if (result) CustomBitrateConfigs[Key(channelIndex)] = bitrateConfig ?? "";
            return result;
        }

        public static bool SetCustomBitrate(int channelIndex, string bitrateConfig, string deviceKey)
        {
            ICanDeviceProvider provider = Provider(deviceKey);
            ICanCustomBitrateCapability capability = provider as ICanCustomBitrateCapability;
            if (capability == null) throw new NotSupportedException("The selected CAN provider does not support custom bitrate.");
            bool result = capability.SetCustomBitrate(channelIndex, bitrateConfig);
            if (result) CustomBitrateConfigs[Key(deviceKey, channelIndex)] = bitrateConfig ?? "";
            return result;
        }

        private static string GetCustomBitrate(int channelIndex)
        {
            string bitrateConfig;
            return CustomBitrateConfigs.TryGetValue(Key(channelIndex), out bitrateConfig) ? bitrateConfig : "";
        }

        private static string GetCustomBitrate(int channelIndex, string deviceKey)
        {
            string bitrateConfig;
            return CustomBitrateConfigs.TryGetValue(Key(deviceKey, channelIndex), out bitrateConfig) ? bitrateConfig : "";
        }

        public static ICanChannel Get(int channelIndex)
        {
            ICanChannel channel;
            if (!OpenChannels.TryGetValue(Key(channelIndex), out channel))
                throw new InvalidOperationException("CAN channel " + channelIndex + " is not open. Run CAN_OpenChannel first.");
            return channel;
        }

        public static ICanChannel Get(int channelIndex, string deviceKey)
        {
            ICanChannel channel;
            if (!OpenChannels.TryGetValue(Key(deviceKey, channelIndex), out channel))
                throw new InvalidOperationException("CAN channel " + channelIndex + " is not open for device " + deviceKey + ".");
            return channel;
        }

        public static IntPtr GetHandle(int channelIndex)
        {
            return CanChannelHandleRegistry.GetToken(Get(channelIndex));
        }

        public static IntPtr GetHandle(int channelIndex, string deviceKey)
        {
            return CanChannelHandleRegistry.GetToken(Get(channelIndex, deviceKey));
        }

        public static bool IsBitRateSwitch(int channelIndex)
        {
            CanChannelOptions options;
            if (!ChannelOptions.TryGetValue(Key(channelIndex), out options))
                throw new InvalidOperationException("CAN channel " + channelIndex + " is not open. Run CAN_OpenChannel first.");
            return options.BitRateSwitch;
        }

        public static bool IsBitRateSwitch(int channelIndex, string deviceKey)
        {
            CanChannelOptions options;
            if (!ChannelOptions.TryGetValue(Key(deviceKey, channelIndex), out options)) throw new InvalidOperationException("CAN channel " + channelIndex + " is not open.");
            return options.BitRateSwitch;
        }

        public static bool IsCanFd(int channelIndex)
        {
            CanChannelOptions options;
            if (!ChannelOptions.TryGetValue(Key(channelIndex), out options))
                throw new InvalidOperationException("CAN channel " + channelIndex + " is not open. Run CAN_OpenChannel first.");
            return options.CanFd;
        }

        public static bool IsCanFd(int channelIndex, string deviceKey)
        {
            CanChannelOptions options;
            if (!ChannelOptions.TryGetValue(Key(deviceKey, channelIndex), out options)) throw new InvalidOperationException("CAN channel " + channelIndex + " is not open.");
            return options.CanFd;
        }

        public static void Close(int channelIndex)
        {
            string key = Key(channelIndex);
            ICanChannel channel;
            if (OpenChannels.TryGetValue(key, out channel))
            {
                try { channel.Dispose(); } catch (Exception) { }
                CanChannelHandleRegistry.Remove(channel);
                ICanChannel ignoredChannel; CanChannelOptions ignoredOptions;
                OpenChannels.TryRemove(key, out ignoredChannel);
                ChannelOptions.TryRemove(key, out ignoredOptions);
            }
        }

        public static void Close(int channelIndex, string deviceKey)
        {
            string key = Key(deviceKey, channelIndex);
            lock (Sync(channelIndex, deviceKey))
            {
                ICanChannel channel;
                if (OpenChannels.TryGetValue(key, out channel))
                {
                    try { channel.Dispose(); } catch (Exception) { }
                    CanChannelHandleRegistry.Remove(channel);
                    ICanChannel ignoredChannel; CanChannelOptions ignoredOptions; string ignoredBitrate; ICanDeviceProvider ignoredProvider;
                    OpenChannels.TryRemove(key, out ignoredChannel);
                    ChannelOptions.TryRemove(key, out ignoredOptions);
                    CustomBitrateConfigs.TryRemove(key, out ignoredBitrate);
                    ChannelProviders.TryRemove(key, out ignoredProvider);
                }
            }
        }

        public static void CloseAll()
        {
            string prefix = CanTransportRegistry.CurrentKey + ":";
            List<string> keys = new List<string>();
            foreach (KeyValuePair<string, ICanChannel> item in OpenChannels)
            {
                if (item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) keys.Add(item.Key);
            }
            foreach (string key in keys)
            {
                try { OpenChannels[key].Dispose(); } catch (Exception) { }
                CanChannelHandleRegistry.Remove(OpenChannels[key]);
                ICanChannel ignoredChannel; CanChannelOptions ignoredOptions; string ignoredBitrate; ICanDeviceProvider ignoredProvider;
                OpenChannels.TryRemove(key, out ignoredChannel);
                ChannelOptions.TryRemove(key, out ignoredOptions);
                CustomBitrateConfigs.TryRemove(key, out ignoredBitrate);
                ChannelProviders.TryRemove(key, out ignoredProvider);
            }
        }

        public static void CloseForProvider(ICanDeviceProvider provider)
        {
            List<string> keys = new List<string>();
            foreach (KeyValuePair<string, ICanDeviceProvider> item in ChannelProviders)
                if (Object.ReferenceEquals(item.Value, provider)) keys.Add(item.Key);
            foreach (string key in keys)
            {
                try { OpenChannels[key].Dispose(); } catch (Exception) { }
                CanChannelHandleRegistry.Remove(OpenChannels[key]);
                ICanChannel ignoredChannel; CanChannelOptions ignoredOptions; string ignoredBitrate; ICanDeviceProvider ignoredProvider;
                OpenChannels.TryRemove(key, out ignoredChannel);
                ChannelOptions.TryRemove(key, out ignoredOptions);
                CustomBitrateConfigs.TryRemove(key, out ignoredBitrate);
                ChannelProviders.TryRemove(key, out ignoredProvider);
            }
        }

        public static void CloseAllDevices()
        {
            List<string> keys = new List<string>(OpenChannels.Keys);
            foreach (string key in keys)
            {
                try { OpenChannels[key].Dispose(); } catch (Exception) { }
                CanChannelHandleRegistry.Remove(OpenChannels[key]);
            }
            OpenChannels.Clear();
            ChannelOptions.Clear();
            CustomBitrateConfigs.Clear();
            ChannelProviders.Clear();
        }

        public static uint ParseId(string text)
        {
            return HexUtils.TryParseAddress(text, 0);
        }

        public static byte[] ParseData(string text)
        {
            byte[] data = HexUtils.TryParseHex(text ?? "");
            if (data == null) throw new InvalidOperationException("CAN data is not valid hexadecimal.");
            return data;
        }
    }

    public sealed class CAN_OpenChannel : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_OpenChannel(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                int channel = ArgInt(0, "CAN Channel", 0);
                bool canFd = ArgInt(1, "CAN-FD (0/1)", 0) != 0;
                bool brs = ArgInt(2, "BRS (0/1)", canFd ? 1 : 0) != 0;
                int nominal = NormalizeBitrate(ArgInt(3, "Nominal Bitrate", 500000));
                int data = NormalizeBitrate(ArgInt(4, "CAN-FD Data Bitrate", 2000000));
                int standard = ArgInt(5, "CAN-FD Standard (0=ISO,1=BOSCH)", 0);
                int mode = ArgInt(6, "CAN-FD Mode (0=Normal,1=Receive only)", 0);
                string deviceKey = ArgString(7, "Device Key", "");
                RawCanChannel.Open(channel, canFd, brs, nominal, data, standard, mode, deviceKey);
                SetReturn(0, true); SetReturn(1, channel); SetReturn(2, "CAN channel initialized and started");
                SetReturn(3, RawCanChannel.GetHandle(channel, deviceKey));
                UpdateStatusKey("CAN_OpenChannel: OK - channel " + channel);
            }
            catch (Exception ex)
            {
                SetReturn(0, false); SetReturn(1, -1); SetReturn(2, ex.Message);
                UpdateStatusKey("CAN_OpenChannel: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }

        private static int NormalizeBitrate(int value)
        {
            return value > 0 && value < 100000 ? value * 1000 : value;
        }
    }

    public sealed class CAN_Send : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_Send(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                int channelIndex = ArgInt(0, "CAN Channel", 0);
                uint identifier = RawCanChannel.ParseId(ArgString(1, "CAN ID", "0x7E0"));
                byte[] data = RawCanChannel.ParseData(ArgString(2, "CAN Data", ""));
                bool extended = ArgInt(3, "Extended Frame (0/1)", 0) != 0;
                bool remote = ArgInt(4, "Remote Frame (0/1)", 0) != 0;
                string deviceKey = ArgString(5, "Device Key", "");
                bool bitRateSwitch = RawCanChannel.IsBitRateSwitch(channelIndex, deviceKey);
                ICanChannel channel = RawCanChannel.Get(channelIndex, deviceKey);
                int sent;
                lock (RawCanChannel.Sync(channelIndex, deviceKey))
                    sent = channel.Send(new List<CanFrame> { new CanFrame { Identifier = identifier, Data = data, Extended = extended, Remote = remote, BitRateSwitch = bitRateSwitch } });
                SetReturn(0, sent > 0);
                SetReturn(1, sent);
                string detail = "CAN TX ID=" + identifier.ToString("X") + " DATA=" + HexUtils.ToHexString(data) +
                                " FD=" + (RawCanChannel.IsCanFd(channelIndex, deviceKey) ? "1" : "0") +
                                " BRS=" + (bitRateSwitch ? "1" : "0") + " RESULT=" + sent;
                SetReturn(2, detail);
                UpdateStatusKey("CAN_Send: " + (sent > 0 ? "OK" : "FAIL") + " - " + detail);
            }
            catch (Exception ex)
            {
                SetReturn(0, false); SetReturn(1, 0); SetReturn(2, ex.Message);
                UpdateStatusKey("CAN_Send: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class CAN_Receive : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_Receive(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                int channelIndex = ArgInt(0, "CAN Channel", 0);
                int timeout = Math.Max(0, ArgInt(1, "Timeout (ms)", 100));
                string deviceKey = ArgString(3, "Device Key", "");
                ICanChannel channel = RawCanChannel.Get(channelIndex, deviceKey);
                int receiveMode = ArgInt(2, "Receive Mode (0=default,1=CAN,2=CAN-FD,3=both)", 0);
                ICanTypedReceiveChannel typedChannel = channel as ICanTypedReceiveChannel;
                IList<CanFrame> frames;
                lock (RawCanChannel.Sync(channelIndex, deviceKey))
                    frames = typedChannel == null ? channel.Receive(timeout) : typedChannel.Receive(timeout, receiveMode);
                var lines = new List<string>();
                foreach (CanFrame frame in frames) lines.Add(frame.Identifier.ToString("X") + ":" + HexUtils.ToHexString(frame.Data));
                SetReturn(0, frames.Count > 0); SetReturn(1, frames.Count); SetReturn(2, string.Join("|", lines.ToArray()));
                UpdateStatusKey("CAN_Receive: " + frames.Count + " frame(s)");
            }
            catch (Exception ex)
            {
                SetReturn(0, false); SetReturn(1, 0); SetReturn(2, ex.Message);
                UpdateStatusKey("CAN_Receive: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class CAN_CloseChannel : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public CAN_CloseChannel(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }
        public override TestResult Execute()
        {
            int channel = ArgInt(0, "CAN Channel", 0);
            string deviceKey = ArgString(1, "Device Key", "");
            RawCanChannel.Close(channel, deviceKey);
            SetReturn(0, true); SetReturn(1, channel); SetReturn(2, "CAN channel closed");
            UpdateStatusKey("CAN_CloseChannel: OK - channel " + channel);
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}