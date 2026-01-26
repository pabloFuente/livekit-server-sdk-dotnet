// author: https://github.com/pabloFuente

using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

[Collection("LiveKit")]
public class RoomOptionsTests : IClassFixture<RtcTestFixture>
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RoomOptionsTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message) =>
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

    [Fact]
    public async Task RoomOptions_AutoSubscribe_True_SubscribesAutomatically()
    {
        Log("Starting AutoSubscribe=true test");

        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();

        var trackSubscribedTcs = new TaskCompletionSource<RemoteTrack>();
        var localTrackSubscribedTcs = new TaskCompletionSource<LocalTrackPublication>();

        subscriberRoom.TrackSubscribed += (sender, args) =>
        {
            Log($"TrackSubscribed: {args.Track.Sid}");
            trackSubscribedTcs.TrySetResult(args.Track);
        };

        publisherRoom.LocalTrackSubscribed += (sender, args) =>
        {
            Log($"LocalTrackSubscribed: {args.Publication.Sid}");
            localTrackSubscribedTcs.TrySetResult(args.Publication);
        };

        // Connect publisher normally
        var publisherToken = _fixture.CreateToken("publisher", "test-autosub-room");
        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        Log("Publisher connected");

        // Publish a track before subscriber joins
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);
        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Audio track published: {publication.Sid}");

        // Connect subscriber with AutoSubscribe=true (default)
        var subscriberToken = _fixture.CreateToken("subscriber", "test-autosub-room");
        var options = new RoomOptions { AutoSubscribe = true };

        await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken, options);
        Log("Subscriber connected with AutoSubscribe=true");

        // Wait for automatic subscription
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var subscribedTrack = await trackSubscribedTcs.Task.WaitAsync(cts.Token);

        Assert.NotNull(subscribedTrack);
        Log("Track was automatically subscribed");

        // Wait for publisher to be notified of the subscription
        using var localSubCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var localPub = await localTrackSubscribedTcs.Task.WaitAsync(localSubCts.Token);

        Assert.NotNull(localPub);
        Log($"Publisher received LocalTrackSubscribed for publication {localPub.Sid}");

        Log("Test passed - AutoSubscribe=true works");

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
        Log("Test completed");
    }

    [Fact]
    public async Task RoomOptions_AutoSubscribe_False_DoesNotSubscribeAutomatically()
    {
        Log("Starting AutoSubscribe=false test");

        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();

        var trackSubscribedTcs = new TaskCompletionSource<RemoteTrack>();
        var trackPublishedTcs = new TaskCompletionSource<TrackPublication>();

        subscriberRoom.TrackSubscribed += (sender, args) =>
        {
            Log($"TrackSubscribed: {args.Track.Sid}");
            trackSubscribedTcs.TrySetResult(args.Track);
        };

        subscriberRoom.TrackPublished += (sender, args) =>
        {
            Log($"TrackPublished: {args.Publication.Sid}");
            trackPublishedTcs.TrySetResult(args.Publication);
        };

        // Connect publisher normally
        var publisherToken = _fixture.CreateToken("publisher", "test-no-autosub-room");
        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        Log("Publisher connected");

        // Connect subscriber with AutoSubscribe=false
        var subscriberToken = _fixture.CreateToken("subscriber", "test-no-autosub-room");
        var options = new RoomOptions { AutoSubscribe = false };

        await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken, options);
        Log("Subscriber connected with AutoSubscribe=false");

        // Publish a track
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);
        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Audio track published: {publication.Sid}");

        // Wait for TrackPublished event
        using var publishedCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var publishedPub = await trackPublishedTcs.Task.WaitAsync(publishedCts.Token);
        Assert.NotNull(publishedPub);
        Log("TrackPublished event received");

        // Verify track was NOT automatically subscribed
        using var subscribedCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await trackSubscribedTcs.Task.WaitAsync(subscribedCts.Token);
            Assert.Fail(
                "Track should NOT have been automatically subscribed with AutoSubscribe=false"
            );
        }
        catch (OperationCanceledException)
        {
            Log("Track was NOT automatically subscribed (expected behavior)");
            Log("Test passed - AutoSubscribe=false works");
        }

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
        Log("Test completed");
    }

    [Fact]
    public async Task RoomOptions_Dynacast_CanBeSet()
    {
        Log("Starting Dynacast option test");

        using var room = new Room();

        var options = new RoomOptions { Dynacast = true };

        var token = _fixture.CreateToken("test-dynacast", "test-dynacast-room");
        await room.ConnectAsync(_fixture.LiveKitUrl, token, options);

        Assert.True(room.IsConnected);
        Log("Connected with Dynacast=true");
        Log("Test passed - Dynacast option accepted");

        await room.DisconnectAsync();
        Log("Test completed");
    }

    [Fact]
    public async Task RoomOptions_AdaptiveStream_CanBeSet()
    {
        Log("Starting AdaptiveStream option test");

        using var room = new Room();

        var options = new RoomOptions { AdaptiveStream = true };

        var token = _fixture.CreateToken("test-adaptive", "test-adaptive-room");
        await room.ConnectAsync(_fixture.LiveKitUrl, token, options);

        Assert.True(room.IsConnected);
        Log("Connected with AdaptiveStream=true");
        Log("Test passed - AdaptiveStream option accepted");

        await room.DisconnectAsync();
        Log("Test completed");
    }
}
