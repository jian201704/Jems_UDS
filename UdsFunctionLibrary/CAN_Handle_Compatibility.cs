using System;
using System.Collections.Generic;
using System.Threading;
using JabilTest;
using ScriptVariableSpaceInterface;
using Jabil.Uds.Can;

namespace UdsFunctionLibrary
{
    internal static class CanDeviceHandleRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<IntPtr, ICanDeviceProvider> Providers = new Dictionary<IntPtr, ICanDeviceProvider>();
        private static long nextToken = 0x20000;

        internal static IntPtr Register(ICanDeviceProvider provider)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            lock (Sync)
            {
                foreach (KeyValuePair<IntPtr, ICanDeviceProvider> item in Providers)
                    if (Object.ReferenceEquals(item.Value, provider)) return item.Key;
                IntPtr token = new IntPtr(Interlocked.Increment(ref nextToken));
                Providers[token] = provider;
                return token;
            }
        }

        internal static ICanDeviceProvider Resolve(object value)
        {
            IntPtr token;
            if (value is IntPtr) token = (IntPtr)value;
            else if (value is long) token = new IntPtr((long)value);
            else if (value is int) token = new IntPtr((int)value);
            else throw new ArgumentException("Expected a CAN device handle.");
            lock (Sync)
            {
                ICanDeviceProvider provider;
                if (!Providers.TryGetValue(token, out provider))
                    throw new InvalidOperationException("Unknown or released CAN device handle: " + token + ".");
                return provider;
            }
        }

        internal static void Remove(ICanDeviceProvider provider)
        {
            lock (Sync)
            {
                IntPtr found = IntPtr.Zero;
                foreach (KeyValuePair<IntPtr, ICanDeviceProvider> item in Providers)
                    if (Object.ReferenceEquals(item.Value, provider)) { found = item.Key; break; }
                if (found != IntPtr.Zero) Providers.Remove(found);
            }
        }

        internal static void Clear()
        {
            lock (Sync) Providers.Clear();
        }
    }

    internal static class CanChannelHandleRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<IntPtr, ICanChannel> Channels = new Dictionary<IntPtr, ICanChannel>();
        private static readonly Dictionary<IntPtr, CanChannelOptions> Options = new Dictionary<IntPtr, CanChannelOptions>();
        private static long nextToken = 0x10000;

        internal static IntPtr Register(ICanChannel channel)
        {
            return Register(channel, null);
        }

        internal static IntPtr Register(ICanChannel channel, CanChannelOptions options)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            lock (Sync)
            {
                foreach (KeyValuePair<IntPtr, ICanChannel> item in Channels)
                    if (Object.ReferenceEquals(item.Value, channel)) return item.Key;
                IntPtr token = new IntPtr(Interlocked.Increment(ref nextToken));
                Channels[token] = channel;
                if (options != null) Options[token] = options;
                return token;
            }
        }

        internal static ICanChannel Resolve(object value)
        {
            IntPtr token;
            if (value is IntPtr) token = (IntPtr)value;
            else if (value is long) token = new IntPtr((long)value);
            else if (value is int) token = new IntPtr((int)value);
            else throw new ArgumentException("Expected a CAN channel handle.");
            lock (Sync)
            {
                ICanChannel channel;
                if (!Channels.TryGetValue(token, out channel))
                    throw new InvalidOperationException("Unknown or released CAN channel handle: " + token + ".");
                return channel;
            }
        }

        internal static IntPtr RegisterNew(ICanChannel channel)
        {
            return Register(channel);
        }

        internal static IntPtr RegisterNew(ICanChannel channel, CanChannelOptions options)
        {
            return Register(channel, options);
        }

        internal static CanChannelOptions GetOptions(object value)
        {
            IntPtr token = value is IntPtr ? (IntPtr)value : new IntPtr(Convert.ToInt64(value));
            lock (Sync)
            {
                CanChannelOptions options;
                if (!Options.TryGetValue(token, out options)) return null;
                return options;
            }
        }

        internal static IntPtr GetToken(ICanChannel channel)
        {
            return Register(channel);
        }

        internal static void Remove(ICanChannel channel)
        {
            lock (Sync)
            {
                IntPtr found = IntPtr.Zero;
                foreach (KeyValuePair<IntPtr, ICanChannel> item in Channels)
                    if (Object.ReferenceEquals(item.Value, channel)) { found = item.Key; break; }
                if (found != IntPtr.Zero) { Channels.Remove(found); Options.Remove(found); }
            }
        }

        internal static void Clear()
        {
            lock (Sync) { Channels.Clear(); Options.Clear(); }
        }
    }

    public sealed class ZCAN_Start : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_Start(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                ICanChannel channel = CanChannelHandleRegistry.Resolve(ArgObject(0, "channel_handle"));
                channel.Start();
                SetReturn(0, 1);
                UpdateStatusKey("ZCAN_Start: OK");
            }
            catch (Exception ex)
            {
                SetReturn(0, 0);
                SetReturn(1, ex.Message);
                UpdateStatusKey("ZCAN_Start: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZCAN_Transmit : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCAN_Transmit(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                ICanChannel channel = CanChannelHandleRegistry.Resolve(ArgObject(0, "channel_handle"));
                uint identifier = RawCanChannel.ParseId(ArgString(1, "CANID", "0"));
                int frameType = ArgInt(2, "FrameType", 0);
                byte[] data = RawCanChannel.ParseData(ArgString(3, "CANdata", ""));
                int count = channel.Send(new List<CanFrame> { new CanFrame { Identifier = identifier, Extended = frameType != 0, Data = data } });
                SetReturn(0, count);
                UpdateStatusKey("ZCAN_Transmit: " + count + " frame(s)");
            }
            catch (Exception ex)
            {
                SetReturn(0, 0);
                SetReturn(1, ex.Message);
                UpdateStatusKey("ZCAN_Transmit: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }

    public sealed class ZCANFD_Transmit : UdsTestBase
    {
        public override string Version { get { return "1.00.00"; } }
        public ZCANFD_Transmit(ITestCellVariableSpace varSpace, object otherParameter) : base(varSpace, otherParameter) { }

        public override TestResult Execute()
        {
            try
            {
                ICanChannel channel = CanChannelHandleRegistry.Resolve(ArgObject(0, "channel_handle"));
                uint identifier = RawCanChannel.ParseId(ArgString(1, "CANID", "0"));
                int frameType = ArgInt(2, "FrameType", 0);
                byte[] data = RawCanChannel.ParseData(ArgString(3, "CANdata", ""));
                bool brs = ArgInt(4, "CANFD_EXP", 0) != 0;
                int count = channel.Send(new List<CanFrame> { new CanFrame { Identifier = identifier, Extended = frameType != 0, BitRateSwitch = brs, Data = data } });
                SetReturn(0, count);
                UpdateStatusKey("ZCANFD_Transmit: " + count + " frame(s)");
            }
            catch (Exception ex)
            {
                SetReturn(0, 0);
                SetReturn(1, ex.Message);
                UpdateStatusKey("ZCANFD_Transmit: FAIL - " + ex.Message);
            }
            testResult.Status = TestStatus.Pass;
            return testResult;
        }
    }
}
