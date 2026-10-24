using System;

namespace Jabil.Uds.Core
{
    public sealed class DiagnosticFrame
    {
        public DiagnosticFrame(uint sourceAddress, uint targetAddress, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            SourceAddress = sourceAddress;
            TargetAddress = targetAddress;
            Payload = (byte[])payload.Clone();
        }

        public uint SourceAddress { get; private set; }
        public uint TargetAddress { get; private set; }
        public byte[] Payload { get; private set; }
    }

    public interface IUdsTransport : IDisposable
    {
        void Connect();
        DiagnosticFrame Request(DiagnosticFrame request, int timeoutMilliseconds);
    }

    public interface IUdsResponsePendingTransport : IUdsTransport
    {
        DiagnosticFrame Request(DiagnosticFrame request, int timeoutMilliseconds,
                                int responsePendingTimeoutMilliseconds);
    }
}

namespace ExcelDecodeCore
{
    public sealed class ExpectedContext
    {
        public string ExpectedPrefix = "";
        public string ExpectedResponse = "";
        public string ExpectedSuffix = "";
        public string ExpectedByteOffset = "";
        public string ExpectedByteValue = "";
        public int ValueOffset;
        public int ValueLength;
        public string ValueType = "";
        public string Endian = "big";
        public string Formula = "";
        public string DecodeProfile = "";
    }

    public sealed class StepResult
    {
        public string StepNo = "";
        public string TestItem = "";
        public string Request = "";
        public string Response = "";
        public string Value = "";
        public string Unit = "";
        public string Limits = "";
        public string TestType = "";
        public ExpectedContext ExpectedContext = new ExpectedContext();
    }
}
