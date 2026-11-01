// author: https://github.com/pabloFuente

using Xunit;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Tests for <see cref="EncodedVideoSource"/> that need the native library but no LiveKit server.
/// </summary>
public class EncodedVideoSourceTests
{
    private static EncodedVideoFrame KeyFrame(byte[] data, int width = 640, int height = 480)
    {
        return new EncodedVideoFrame(
            data,
            width,
            height,
            Proto.VideoCodec.H264,
            Proto.EncodedFrameType.EncodedFrameKey,
            timestampUs: 0
        );
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(640, 0)]
    [InlineData(-1, 480)]
    public void EncodedVideoFrame_RejectsNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyFrame(new byte[] { 1 }, width, height));
    }

    [Fact]
    public void EncodedVideoFrame_RejectsMissingData()
    {
        Assert.Throws<ArgumentNullException>(() => KeyFrame(null!));
        Assert.Throws<ArgumentException>(() => KeyFrame(Array.Empty<byte>()));
    }

    [Fact]
    public void EncodedVideoSource_RejectsNonPositiveDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodedVideoSource(0, 480));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodedVideoSource(640, -1));
    }

    [Fact]
    public void EncodedVideoSource_CreatesTrackAndReportsFeedback()
    {
        using var source = new EncodedVideoSource(640, 480);
        Assert.Equal(640, source.Width);
        Assert.Equal(480, source.Height);

        using var track = source.CreateTrack("encoded");
        Assert.Equal(Proto.TrackKind.KindVideo, track.Kind);
        Assert.Equal("encoded", track.Name);

        var feedback = source.TakeFeedback();
        Assert.NotNull(feedback);

        // Capturing before the track is published is accepted or dropped by the native side, but
        // must neither throw nor crash. The payload is a minimal Annex B H.264 unit.
        var accepted = source.CaptureFrame(KeyFrame(new byte[] { 0, 0, 0, 1, 0x65, 0x88, 0x84 }));
        Assert.IsType<bool>(accepted);
    }

    [Fact]
    public void EncodedVideoSource_DisposedSource_Throws()
    {
        var source = new EncodedVideoSource(320, 240);
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.TakeFeedback());
        Assert.Throws<ObjectDisposedException>(() => source.CreateTrack());
    }
}
