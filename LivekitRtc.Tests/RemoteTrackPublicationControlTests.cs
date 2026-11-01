// author: https://github.com/pabloFuente

using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for the subscriber-side controls on <see cref="RemoteTrackPublication"/>:
/// SetVideoQuality, SetEnabled and SetVideoDimensions.
/// </summary>
[Collection("RtcTests")]
public class RemoteTrackPublicationControlTests
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RemoteTrackPublicationControlTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private async Task<(Room publisher, Room subscriber)> ConnectPair(
        string roomName,
        string publisherIdentity,
        string subscriberIdentity
    )
    {
        var publisherRoom = new Room();
        var subscriberRoom = new Room();
        await _fixture.ConnectRoomsAndWaitForEvents(
            publisherRoom,
            subscriberRoom,
            _fixture.CreateToken(publisherIdentity, roomName),
            _fixture.CreateToken(subscriberIdentity, roomName),
            subscriberIdentity,
            Log
        );
        return (publisherRoom, subscriberRoom);
    }

    [Fact]
    public async Task SimulcastVideo_QualityEnabledAndDimensionsCanBeChanged()
    {
        var (publisherRoom, subscriberRoom) = await ConnectPair(
            "test-publication-controls-room",
            "ctl-publisher",
            "ctl-subscriber"
        );
        using (publisherRoom)
        using (subscriberRoom)
        {
            var subscribedTcs = new TaskCompletionSource<RemoteTrackPublication>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            subscriberRoom.TrackSubscribed += (_, args) =>
            {
                if (args.Track is RemoteVideoTrack)
                    subscribedTcs.TrySetResult(args.Publication);
            };

            using var videoSource = new VideoSource(1280, 720);
            var videoTrack = LocalVideoTrack.Create("simulcast-video", videoSource);
            var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(
                videoTrack,
                new TrackPublishOptions
                {
                    Source = Proto.TrackSource.SourceCamera,
                    Simulcast = true,
                }
            );
            Log($"Published {publication.Sid}, simulcasted={publication.Simulcasted}");

            var remote = await subscribedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Proto.TrackKind.KindVideo, remote.Kind);
            Assert.True(remote.Simulcasted, "a 720p publication with Simulcast = true has layers");

            // None of these may throw; the server acknowledges them silently
            remote.SetVideoQuality(Proto.VideoQuality.Low);
            remote.SetVideoQuality(Proto.VideoQuality.Medium);
            remote.SetVideoQuality(Proto.VideoQuality.High);
            remote.SetVideoDimensions(640, 360);
            remote.SetEnabled(false);
            remote.SetEnabled(true);
            Log("Quality, dimensions and enabled state changed without errors");

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
        }
    }

    [Fact]
    public async Task SetVideoQuality_OnAudioPublication_Throws()
    {
        var (publisherRoom, subscriberRoom) = await ConnectPair(
            "test-publication-controls-audio-room",
            "ctl-audio-publisher",
            "ctl-audio-subscriber"
        );
        using (publisherRoom)
        using (subscriberRoom)
        {
            var subscribedTcs = new TaskCompletionSource<RemoteTrackPublication>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            subscriberRoom.TrackSubscribed += (_, args) =>
            {
                if (args.Track is RemoteAudioTrack)
                    subscribedTcs.TrySetResult(args.Publication);
            };

            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("audio", audioSource);
            await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);

            var remote = await subscribedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Proto.TrackKind.KindAudio, remote.Kind);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                remote.SetVideoQuality(Proto.VideoQuality.High)
            );
            Assert.Contains("video publications", ex.Message);

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
        }
    }
}
