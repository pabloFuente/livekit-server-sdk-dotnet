// author: https://github.com/pabloFuente

using Xunit;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Unit tests for the <see cref="DataTrackFrame"/> mapping to and from the FFI proto. No Docker.
/// </summary>
public class DataTrackFrameTests
{
    [Fact]
    public void ToProto_FromProto_RoundTripsPayloadAndTimestamp()
    {
        var frame = new DataTrackFrame(new byte[] { 1, 2, 3 }, 42UL);

        var proto = frame.ToProto();
        Assert.True(proto.HasUserTimestamp);
        Assert.Equal(42UL, proto.UserTimestamp);
        Assert.Equal(new byte[] { 1, 2, 3 }, proto.Payload.ToByteArray());

        var back = DataTrackFrame.FromProto(proto);
        Assert.Equal(new byte[] { 1, 2, 3 }, back.Payload);
        Assert.Equal(42UL, back.UserTimestamp);
    }

    [Fact]
    public void ToProto_WithoutTimestamp_LeavesTheFieldUnset()
    {
        var proto = new DataTrackFrame(new byte[] { 9 }).ToProto();

        Assert.False(proto.HasUserTimestamp);
        Assert.Null(DataTrackFrame.FromProto(proto).UserTimestamp);
    }

    [Fact]
    public void Constructor_RejectsNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => new DataTrackFrame(null!));
    }
}
