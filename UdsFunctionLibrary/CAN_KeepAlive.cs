using System;
using System.Collections.Concurrent;
using System.Threading;
using Jabil.Uds.Can;
using UdsCore;
using ExcelDecodeCore;

namespace UdsFunctionLibrary
{
    internal static class CanKeepAliveManager
    {
        private sealed class Entry : IDisposable
        {
            public readonly object Sync = new object();
            public ICanChannel Channel;
            public CanChannelOptions Options;
            public string DeviceKey;
            public uint RequestId;
            public byte[] Payload;
            public Timer Timer;

            public void Dispose()
            {
                Timer timer = Timer;
                Timer = null;
                if (timer != null) timer.Dispose();
            }
        }

        private static readonly ConcurrentDictionary<string, Entry> Entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static void StartOrUpdate(UdsStepConfig cfg)
        {
            if (cfg == null) return;
            if (!cfg.KeepAliveEnabled)
            {
                Stop(cfg.CanDeviceKey, cfg.CanChannelIndex);
                return;
            }
            string key = Key(cfg);
            ICanChannel channel = RawCanChannel.Open(cfg.CanChannelIndex, cfg.CanFd,
                cfg.CanBitRateSwitch, cfg.CanNominalBitrate, cfg.CanDataBitrate,
                cfg.CanFdStandard, cfg.CanFdMode, cfg.CanDeviceKey);
            var options = new CanChannelOptions
            {
                ChannelIndex = cfg.CanChannelIndex,
                CanFd = cfg.CanFd,
                BitRateSwitch = cfg.CanBitRateSwitch,
                NominalBitrate = cfg.CanNominalBitrate,
                DataBitrate = cfg.CanDataBitrate,
                CanFdStandard = cfg.CanFdStandard,
                CanFdMode = cfg.CanFdMode,
                FrameLength = cfg.CanFrameLength,
                PaddingByte = cfg.CanPaddingByte
            };
            byte[] payload = HexUtils.TryParseHex(cfg.KeepAlivePayload);
            if (payload == null || payload.Length == 0)
                throw new InvalidOperationException("Invalid CAN Tester Present payload: " + cfg.KeepAlivePayload);
            int period = Math.Max(250, cfg.KeepAliveIntervalMs);

            Entry entry;
            if (!Entries.TryGetValue(key, out entry))
            {
                entry = new Entry();
                Entry existing;
                if (!Entries.TryAdd(key, entry))
                {
                    Entries.TryGetValue(key, out existing);
                    entry = existing;
                }
            }
            lock (entry.Sync)
            {
                entry.Channel = channel;
                entry.Options = options;
                entry.DeviceKey = cfg.CanDeviceKey;
                entry.RequestId = cfg.CanRequestId != 0 ? cfg.CanRequestId : 0x7E0U;
                entry.Payload = payload;
                if (entry.Timer != null) entry.Timer.Dispose();
                entry.Timer = new Timer(SendTick, entry, 0, period);
            }
        }

        public static void Stop(string deviceKey, int channelIndex)
        {
            Entry entry;
            if (Entries.TryRemove(Key(deviceKey, channelIndex), out entry)) entry.Dispose();
        }

        public static void StopDevice(string deviceKey)
        {
            string prefix = (string.IsNullOrWhiteSpace(deviceKey) ? CanTransportRegistry.CurrentKey : deviceKey.Trim()) + ":";
            foreach (var item in Entries)
            {
                if (!item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                Entry entry;
                if (Entries.TryRemove(item.Key, out entry)) entry.Dispose();
            }
        }

        public static void StopAll()
        {
            foreach (var item in Entries)
            {
                Entry entry;
                if (Entries.TryRemove(item.Key, out entry)) entry.Dispose();
            }
        }

        private static void SendTick(object state)
        {
            Entry entry = state as Entry;
            if (entry == null) return;
            lock (RawCanChannel.Sync(entry.Options.ChannelIndex, entry.DeviceKey))
            {
                lock (entry.Sync)
                {
                    if (entry.Channel == null || entry.Payload == null) return;
                    try
                    {
                        byte[] data = BuildSingleFrame(entry.Payload, entry.Options);
                        int sent = entry.Channel.Send(new[]
                        {
                            new CanFrame
                            {
                                Identifier = entry.RequestId,
                                Data = data,
                                BitRateSwitch = entry.Options.BitRateSwitch
                            }
                        });
                        if (sent < 1) return;
                    }
                    catch (Exception)
                    {
                        // Tester Present is best-effort; the next tick retries it.
                    }
                }
            }
        }

        private static byte[] BuildSingleFrame(byte[] payload, CanChannelOptions options)
        {
            if (payload.Length > 7)
                throw new InvalidOperationException("CAN Tester Present payload must fit in a single frame.");
            int length = payload.Length + 1;
            int frameLength = options.FrameLength;
            int maximum = options.CanFd ? 64 : 8;
            if (frameLength > 0)
            {
                if (frameLength < length || frameLength > maximum) frameLength = length;
            }
            else frameLength = length;
            byte[] data = new byte[frameLength];
            data[0] = (byte)payload.Length;
            Array.Copy(payload, 0, data, 1, payload.Length);
            for (int i = payload.Length + 1; i < data.Length; ++i) data[i] = options.PaddingByte;
            return data;
        }

        private static string Key(UdsStepConfig cfg)
        {
            return Key(cfg.CanDeviceKey, cfg.CanChannelIndex);
        }

        private static string Key(string deviceKey, int channelIndex)
        {
            string name = string.IsNullOrWhiteSpace(deviceKey) ? CanTransportRegistry.CurrentKey : deviceKey.Trim();
            return name + ":" + channelIndex;
        }
    }
}
