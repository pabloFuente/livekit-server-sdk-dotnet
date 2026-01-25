using System.Diagnostics;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for track unpublish functionality.
/// </summary>
[Collection("RtcTests")]
public class TrackUnpublishTests : IAsyncLifetime
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TrackUnpublishTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnpublishTrack_RemovesTrackFromRoom()
    {
        const string roomName = "test-unpublish-room";
        const string publisherIdentity = "unpublish-publisher";
        const string subscriberIdentity = "unpublish-subscriber";

        Log("Starting UnpublishTrack test");

        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var subscriberToken = _fixture.CreateToken(subscriberIdentity, roomName);

        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();

        var trackPublishedTcs = new TaskCompletionSource<TrackPublication>();
        var trackUnpublishedTcs = new TaskCompletionSource<TrackPublication>();

        subscriberRoom.TrackPublished += (sender, args) =>
        {
            Log($"Subscriber notified of published track: {args.Publication.Sid}");
            trackPublishedTcs.TrySetResult(args.Publication);
        };

        subscriberRoom.TrackUnpublished += (sender, args) =>
        {
            Log($"Subscriber notified of unpublished track: {args.Publication.Sid}");
            trackUnpublishedTcs.TrySetResult(args.Publication);
        };

        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
        Log("Both participants connected");

        // Publish audio track
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);
        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);

        Log($"Track published: {publication.Sid}");

        // Wait for subscriber to be notified
        var publishedNotification = await trackPublishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(publishedNotification);
        Assert.Equal(publication.Sid, publishedNotification.Sid);

        // Verify track exists in publications
        Assert.True(publisherRoom.LocalParticipant.TrackPublications.ContainsKey(publication.Sid));

        // Unpublish the track
        await publisherRoom.LocalParticipant.UnpublishTrackAsync(publication.Sid);
        Log("Track unpublished");

        // Verify track removed from local publications
        Assert.False(publisherRoom.LocalParticipant.TrackPublications.ContainsKey(publication.Sid));

        // Wait for subscriber to be notified
        var unpublishedNotification = await trackUnpublishedTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(5)
        );
        Assert.NotNull(unpublishedNotification);
        Assert.Equal(publication.Sid, unpublishedNotification.Sid);

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();

        Log("Test completed successfully!");
    }

    [Fact]
    public async Task UnpublishMultipleTracks_RemovesAllTracks()
    {
        const string roomName = "test-multi-unpublish-room";
        const string identity = "multi-unpublish-publisher";

        Log("Starting UnpublishMultipleTracks test");

        var token = _fixture.CreateToken(identity, roomName);
        using var room = new Room();

        await room.ConnectAsync(_fixture.LiveKitUrl, token);
        Log("Publisher connected");

        // Publish multiple tracks
        using var audioSource = new AudioSource(48000, 1);
        using var videoSource = new VideoSource(640, 480);

        var audioTrack = LocalAudioTrack.Create("audio", audioSource);
        var videoTrack = LocalVideoTrack.Create("video", videoSource);

        var audioPub = await room.LocalParticipant!.PublishTrackAsync(audioTrack);
        var videoPub = await room.LocalParticipant.PublishTrackAsync(videoTrack);

        Log($"Audio published: {audioPub.Sid}");
        Log($"Video published: {videoPub.Sid}");

        Assert.Equal(2, room.LocalParticipant.TrackPublications.Count);

        // Unpublish audio
        await room.LocalParticipant.UnpublishTrackAsync(audioPub.Sid);
        Log("Audio track unpublished");
        Assert.Single(room.LocalParticipant.TrackPublications);
        Assert.False(room.LocalParticipant.TrackPublications.ContainsKey(audioPub.Sid));

        // Unpublish video
        await room.LocalParticipant.UnpublishTrackAsync(videoPub.Sid);
        Log("Video track unpublished");
        Assert.Empty(room.LocalParticipant.TrackPublications);
        Assert.False(room.LocalParticipant.TrackPublications.ContainsKey(videoPub.Sid));

        await room.DisconnectAsync();

        Log("Test completed successfully!");
    }
}
