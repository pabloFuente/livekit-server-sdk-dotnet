// author: https://github.com/pabloFuente

using Google.Protobuf.Reflection;
using LiveKit.Rtc;
using Xunit;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Unit tests of the mapping of <see cref="RoomOptions"/> into the FFI <c>Proto.RoomOptions</c> of
/// the <c>ConnectRequest</c>, and a guard that fails when a regenerated proto has a field the SDK
/// does not expose (the relay-only ICE policy was missing that way).
/// </summary>
public class RoomOptionsMappingTests
{
    [Fact]
    public void ToProto_Defaults_LeaveOptionalFieldsUnset()
    {
        var proto = new RoomOptions().ToProto();

        // Unset means the FFI applies its own defaults
        Assert.Null(proto.RtcConfig);
        Assert.False(proto.HasConnectTimeoutMs);
        Assert.False(proto.HasSinglePeerConnection);
        Assert.Null(proto.DataStream);
        Assert.Null(proto.Encryption);
        Assert.True(proto.AutoSubscribe);
        Assert.False(proto.Dynacast);
        Assert.False(proto.AdaptiveStream);
        Assert.Equal(3u, proto.JoinRetries);
    }

    [Fact]
    public void ToProto_EmptyRtcConfiguration_LeavesItsFieldsUnset()
    {
        var proto = new RoomOptions { RtcConfig = new RtcConfiguration() }.ToProto();

        Assert.NotNull(proto.RtcConfig);
        Assert.False(proto.RtcConfig.HasIceTransportType);
        Assert.False(proto.RtcConfig.HasContinualGatheringPolicy);
        Assert.Empty(proto.RtcConfig.IceServers);
    }

    [Theory]
    [InlineData(Proto.IceTransportType.TransportRelay)]
    [InlineData(Proto.IceTransportType.TransportNohost)]
    [InlineData(Proto.IceTransportType.TransportAll)]
    public void ToProto_MapsIceTransportType(Proto.IceTransportType type)
    {
        var proto = new RoomOptions
        {
            RtcConfig = new RtcConfiguration { IceTransportType = type },
        }.ToProto();

        // TransportRelay is the proto default (0): only the presence bit tells it from unset
        Assert.True(proto.RtcConfig.HasIceTransportType);
        Assert.Equal(type, proto.RtcConfig.IceTransportType);
    }

    [Fact]
    public void ToProto_MapsAllOptions()
    {
        var proto = FullyPopulatedOptions().ToProto();

        Assert.Equal(Proto.IceTransportType.TransportRelay, proto.RtcConfig.IceTransportType);
        Assert.Equal(
            Proto.ContinualGatheringPolicy.GatherOnce,
            proto.RtcConfig.ContinualGatheringPolicy
        );
        var server = Assert.Single(proto.RtcConfig.IceServers);
        Assert.Equal(new[] { "turn:turn.example.com:3478" }, server.Urls);
        Assert.Equal("user", server.Username);
        Assert.Equal("secret", server.Password);
        Assert.Equal(2500ul, proto.ConnectTimeoutMs);
        Assert.False(proto.SinglePeerConnection);
        Assert.Equal(1_000_000ul, proto.DataStream.MaxPayloadByteLength);
    }

    [Fact]
    public void ToProto_RoundsAConnectTimeoutUpToWholeMilliseconds()
    {
        var proto = new RoomOptions { ConnectTimeout = TimeSpan.FromTicks(1) }.ToProto();

        Assert.Equal(1ul, proto.ConnectTimeoutMs);
    }

    [Fact]
    public void ToProto_RejectsInvalidValues()
    {
        // The native SDK aborts the process on an enum value it does not know
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RoomOptions
            {
                RtcConfig = new RtcConfiguration { IceTransportType = (Proto.IceTransportType)42 },
            }.ToProto()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RoomOptions
            {
                RtcConfig = new RtcConfiguration
                {
                    ContinualGatheringPolicy = (Proto.ContinualGatheringPolicy)42,
                },
            }.ToProto()
        );
        Assert.Throws<ArgumentException>(() =>
            new RoomOptions
            {
                RtcConfig = new RtcConfiguration { IceServers = { new Proto.IceServer() } },
            }.ToProto()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RoomOptions { ConnectTimeout = TimeSpan.Zero }.ToProto()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RoomOptions { ConnectTimeout = TimeSpan.FromSeconds(-1) }.ToProto()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RoomOptions
            {
                DataStream = new DataStreamOptions { MaxPayloadByteLength = 0 },
            }.ToProto()
        );
    }

    [Fact]
    public void ToProto_CopiesTheIceServers()
    {
        var server = new Proto.IceServer { Urls = { "stun:stun.example.com:3478" } };
        var options = new RoomOptions
        {
            RtcConfig = new RtcConfiguration { IceServers = { server } },
        };

        var proto = options.ToProto();
        server.Urls.Add("stun:changed.example.com");

        Assert.Equal(new[] { "stun:stun.example.com:3478" }, proto.RtcConfig.IceServers[0].Urls);
    }

    // Fields deliberately not exposed, with the reason. Everything else in these protos must be
    // reachable from RoomOptions, like in the Node and Python SDKs.
    private static readonly Dictionary<string, HashSet<string>> IntentionallyNotExposed = new()
    {
        [Proto.RoomOptions.Descriptor.FullName] = new()
        {
            "e2ee", // deprecated, replaced by "encryption" (RoomOptions.E2EE)
            "other_sdks", // names SDKs layered on top of this one; neither Node nor Python expose it
        },
        [Proto.RoomDataStreamOptions.Descriptor.FullName] = new()
        {
            "use_legacy_client_implementation", // deprecated and ignored by the FFI
        },
        [Proto.RtcConfig.Descriptor.FullName] = new(),
    };

    [Fact]
    public void ToProto_MapsEveryProtoField()
    {
        var proto = FullyPopulatedOptions().ToProto();

        AssertAllFieldsSet(proto);
        AssertAllFieldsSet(proto.RtcConfig);
        AssertAllFieldsSet(proto.DataStream);
    }

    private static void AssertAllFieldsSet(Google.Protobuf.IMessage message)
    {
        var descriptor = message.Descriptor;
        var excluded = IntentionallyNotExposed[descriptor.FullName];
        foreach (var field in descriptor.Fields.InDeclarationOrder())
        {
            if (excluded.Contains(field.Name))
                continue;
            var isMapped =
                field.IsRepeated
                    ? ((System.Collections.IList)field.Accessor.GetValue(message)).Count > 0
                : field.FieldType == FieldType.Message ? field.Accessor.GetValue(message) != null
                : field.Accessor.HasValue(message);
            Assert.True(
                isMapped,
                $"Proto field '{descriptor.Name}.{field.Name}' is not mapped by RoomOptions.ToProto()"
            );
        }
    }

    private static RoomOptions FullyPopulatedOptions() =>
        new RoomOptions
        {
            AutoSubscribe = false,
            Dynacast = true,
            AdaptiveStream = true,
            JoinRetries = 1,
            E2EE = new E2EEOptions(),
            RtcConfig = new RtcConfiguration
            {
                IceTransportType = Proto.IceTransportType.TransportRelay,
                ContinualGatheringPolicy = Proto.ContinualGatheringPolicy.GatherOnce,
                IceServers =
                {
                    new Proto.IceServer
                    {
                        Urls = { "turn:turn.example.com:3478" },
                        Username = "user",
                        Password = "secret",
                    },
                },
            },
            ConnectTimeout = TimeSpan.FromMilliseconds(2500),
            SinglePeerConnection = false,
            DataStream = new DataStreamOptions { MaxPayloadByteLength = 1_000_000 },
        };
}
