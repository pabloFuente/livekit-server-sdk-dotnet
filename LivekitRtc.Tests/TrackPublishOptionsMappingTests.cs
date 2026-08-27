// author: https://github.com/pabloFuente

using LiveKit.Rtc;
using Xunit;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Unit tests that verify <see cref="TrackPublishOptions"/> is correctly mapped into the
/// FFI <c>Proto.TrackPublishOptions</c> that is sent inside the <c>PublishTrackRequest</c>.
///
/// Regression coverage for the missing <c>VideoCodec</c> mapping: <c>TrackPublishOptions</c>
/// had no way to select the publish codec (a parity gap with the Node/Python/Rust SDKs, which
/// all expose <c>videoCodec</c>), so every video track was silently published with the FFI
/// default codec (VP8) no matter what the application wanted.
/// </summary>
public class TrackPublishOptionsMappingTests
{
    [Fact]
    public void ToProto_Defaults_LeaveOptionalFieldsUnset()
    {
        var proto = new TrackPublishOptions().ToProto();

        // Unset means the FFI applies the SDK defaults, exactly as before these
        // options existed (VP8, dtx on, red on, no stream, no scalability mode)
        Assert.False(proto.HasVideoCodec);
        Assert.Equal(Proto.VideoCodec.Vp8, proto.VideoCodec);
        Assert.False(proto.HasDtx);
        Assert.False(proto.HasRed);
        Assert.False(proto.HasStream);
        Assert.False(proto.HasPreconnectBuffer);
        Assert.False(proto.HasScalabilityMode);
        Assert.False(proto.HasVideoEncoder);
        Assert.False(proto.HasDegradationPreference);
        Assert.Empty(proto.FrameMetadataFeatures);
        Assert.True(proto.Simulcast);
        Assert.Equal(Proto.TrackSource.SourceUnknown, proto.Source);
        Assert.Null(proto.VideoEncoding);
        Assert.Null(proto.AudioEncoding);
    }

    [Theory]
    [InlineData(Proto.VideoCodec.Vp8)]
    [InlineData(Proto.VideoCodec.H264)]
    [InlineData(Proto.VideoCodec.Av1)]
    [InlineData(Proto.VideoCodec.Vp9)]
    public void ToProto_MapsVideoCodec(Proto.VideoCodec codec)
    {
        var options = new TrackPublishOptions { VideoCodec = codec };

        var proto = options.ToProto();

        Assert.True(proto.HasVideoCodec);
        Assert.Equal(codec, proto.VideoCodec);
    }

    [Fact]
    public void ToProto_MapsAllOptions()
    {
        var proto = FullyPopulatedOptions().ToProto();

        Assert.Equal(Proto.VideoCodec.H264, proto.VideoCodec);
        Assert.False(proto.Dtx);
        Assert.False(proto.Red);
        Assert.False(proto.Simulcast);
        Assert.Equal(Proto.TrackSource.SourceCamera, proto.Source);
        Assert.Equal("my-stream", proto.Stream);
        Assert.True(proto.PreconnectBuffer);
        Assert.Equal(
            new[]
            {
                Proto.FrameMetadataFeature.FmfUserTimestamp,
                Proto.FrameMetadataFeature.FmfFrameId,
            },
            proto.FrameMetadataFeatures
        );
        Assert.Equal("L3T3_KEY", proto.ScalabilityMode);
        Assert.Equal(Proto.VideoEncoderBackend.EncoderBackendHardware, proto.VideoEncoder);
        Assert.Equal(Proto.DegradationPreference.MaintainFramerate, proto.DegradationPreference);
        Assert.Equal(1_500_000u, proto.VideoEncoding.MaxBitrate);
        Assert.Equal(30u, proto.VideoEncoding.MaxFramerate);
        Assert.Equal(64_000u, proto.AudioEncoding.MaxBitrate);
    }

    /// <summary>
    /// Guards against FFI proto drift: when a proto regeneration brings new
    /// <c>Proto.TrackPublishOptions</c> fields, this test fails naming the field until it is
    /// either mapped by <c>TrackPublishOptions.ToProto()</c> (and set in
    /// <see cref="FullyPopulatedOptions"/>) or explicitly listed as not exposed.
    /// </summary>
    [Fact]
    public void ToProto_CoversEveryProtoField()
    {
        var intentionallyNotExposed = new HashSet<string>();

        var proto = FullyPopulatedOptions().ToProto();

        foreach (var field in Proto.TrackPublishOptions.Descriptor.Fields.InDeclarationOrder())
        {
            if (intentionallyNotExposed.Contains(field.Name))
            {
                continue;
            }
            var isMapped = field.IsRepeated
                ? ((System.Collections.IList)field.Accessor.GetValue(proto)).Count > 0
                : field.Accessor.HasValue(proto);
            Assert.True(
                isMapped,
                $"Proto field '{field.Name}' is not mapped by TrackPublishOptions.ToProto()"
            );
        }
    }

    private static TrackPublishOptions FullyPopulatedOptions() =>
        new TrackPublishOptions
        {
            VideoEncoding = new VideoEncodingOptions { MaxBitrate = 1_500_000, MaxFramerate = 30 },
            AudioEncoding = new AudioEncodingOptions { MaxBitrate = 64_000 },
            VideoCodec = Proto.VideoCodec.H264,
            Dtx = false,
            Red = false,
            Simulcast = false,
            Source = Proto.TrackSource.SourceCamera,
            Stream = "my-stream",
            PreconnectBuffer = true,
            FrameMetadataFeatures = new List<Proto.FrameMetadataFeature>
            {
                Proto.FrameMetadataFeature.FmfUserTimestamp,
                Proto.FrameMetadataFeature.FmfFrameId,
            },
            ScalabilityMode = "L3T3_KEY",
            VideoEncoder = Proto.VideoEncoderBackend.EncoderBackendHardware,
            DegradationPreference = Proto.DegradationPreference.MaintainFramerate,
        };
}
