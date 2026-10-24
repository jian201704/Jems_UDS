namespace UdsCore
{
    /// <summary>DoIP protocol constants. Port of EonTest DoipProtocolLayer.h Doip namespace.</summary>
    public static class DoipConstants
    {
        public const byte ProtocolVersion = 0x02;          // ISO 13400-2:2019
        public const byte InverseProtocolVersion = 0xFD;
        public const int HeaderSize = 8;

        public const ushort UdpDiscoveryPort = 13400;
        public const ushort TcpDataPort = 13400;

        public static class PayloadType
        {
            public const ushort GenericNegativeAck = 0x0000;
            public const ushort VehicleIdRequest = 0x0001;
            public const ushort VehicleIdRequestWithEid = 0x0002;
            public const ushort VehicleIdRequestWithVin = 0x0003;
            public const ushort VehicleAnnouncement = 0x0004;
            public const ushort RoutingActivationRequest = 0x0005;
            public const ushort RoutingActivationResponse = 0x0006;
            public const ushort AliveCheckRequest = 0x0007;
            public const ushort AliveCheckResponse = 0x0008;
            public const ushort DoipEntityStatusRequest = 0x4001;
            public const ushort DoipEntityStatusResponse = 0x4002;
            public const ushort DiagnosticPowerModeRequest = 0x4003;
            public const ushort DiagnosticPowerModeResponse = 0x4004;
            public const ushort DiagnosticMessage = 0x8001;
            public const ushort DiagnosticMessagePositiveAck = 0x8002;
            public const ushort DiagnosticMessageNegativeAck = 0x8003;
        }

        public static class ActivationType
        {
            public const byte Default = 0x00;
            public const byte WwhObd = 0x01;
            public const byte CentralSecurity = 0xE0;
        }

        public static class ActivationResponse
        {
            public const byte Successful = 0x10;
            public const byte NoSocketAvailable = 0x11;
            public const byte InvalidSourceAddress = 0x12;
            public const byte AlreadyActive = 0x13;
            public const byte AuthenticationMissing = 0x14;
            public const byte AuthenticationRejected = 0x15;
            public const byte UnsupportedActType = 0x16;
            public const byte TlsRequired = 0x17;
        }

        public static class NackCode
        {
            public const byte IncorrectPattern = 0x00;
            public const byte UnknownPayload = 0x01;
            public const byte MessageTooLarge = 0x02;
            public const byte OutOfMemory = 0x03;
            public const byte InvalidTarget = 0x04;
        }

        public static class DiagnosticNackCode
        {
            public const byte InvalidSourceAddress = 0x02;
            public const byte UnknownTargetAddress = 0x03;
            public const byte DiagnosticMessageTooLarge = 0x04;
            public const byte OutOfMemory = 0x05;
            public const byte TargetUnreachable = 0x06;
            public const byte UnknownNetwork = 0x07;
            public const byte TransportProtocolError = 0x08;
        }
    }
}
