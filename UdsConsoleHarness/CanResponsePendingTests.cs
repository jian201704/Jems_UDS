using System.Collections.Generic;
using Jabil.Uds.Can;
using Jabil.Uds.Core;
using UdsCore;
using UdsEngine;

namespace UdsConsoleHarness
{
    internal static class CanResponsePendingTests
    {
        public static void Run(System.Action<bool, string, string> check)
        {
            var channel = new PendingCanChannel();
            var options = new CanChannelOptions { ChannelIndex = 0 };
            var transport = new CanIsoTpTransport(channel, options, 0x7E0, 0x7E8);
            var cfg = new UdsStepConfig
            {
                UdsPayload = "11 01",
                ExpectedPrefix = "51 01",
                TimeoutMs = 100,
                ResponsePendingTimeoutMs = 500,
                MinRequestIntervalMs = 0,
                SequenceRetryCount = 2,
                SequenceRetryDelayMs = 0
            };

            UdsStepResult result = UdsTransactionExecutor.Execute(cfg, transport);
            check(result.Success && result.CombinedResponseHex == "51 01" && channel.RequestCount == 1,
                "CAN waits through NRC 0x78 without retransmitting",
                "success=" + result.Success + ", response=" + result.CombinedResponseHex +
                ", requests=" + channel.RequestCount);
        }

        private sealed class PendingCanChannel : ICanChannel
        {
            private readonly Queue<CanFrame> responses = new Queue<CanFrame>();
            public int RequestCount;

            public void Initialize() { }
            public void Start() { }

            public int Send(IList<CanFrame> frames)
            {
                foreach (CanFrame frame in frames)
                {
                    if (frame.Identifier != 0x7E0 || frame.Data.Length < 3 || frame.Data[1] != 0x11)
                        continue;
                    RequestCount++;
                    responses.Enqueue(new CanFrame { Identifier = 0x7E8, Data = new byte[] { 0x03, 0x7F, 0x11, 0x78 } });
                    responses.Enqueue(new CanFrame { Identifier = 0x7E8, Data = new byte[] { 0x02, 0x51, 0x01 } });
                }
                return frames.Count;
            }

            public IList<CanFrame> Receive(int timeoutMilliseconds)
            {
                if (responses.Count == 0) return new List<CanFrame>();
                return new List<CanFrame> { responses.Dequeue() };
            }

            public void Dispose() { }
        }
    }
}