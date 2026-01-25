using System.Diagnostics;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for track subscription functionality.
/// </summary>
[Collection("RtcTests")]
public class TrackSubscriptionTests : IAsyncLifetime
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TrackSubscriptionTests(RtcTestFixture fixture, ITestOutputHelper output)
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
    public async Task SubscribeToRemoteAudioTrack_ReceivesAudioData()
    {
        const string roomName = "test-subscription-room";
        const string publisherIdentity = "publisher";
        const string subscriberIdentity = "subscriber";

        Log("Starting SubscribeToRemoteAudioTrack test");

        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var subscriberToken = _fixture.CreateToken(subscriberIdentity, roomName);

        // Create publisher room
        using var publisherRoom = new Room();
        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        Assert.True(publisherRoom.IsConnected);
        Log("Publisher connected");

        // Create subscriber room
        using var subscriberRoom = new Room();

        // Set up event to catch track subscription
        var trackSubscribedTcs =
            new TaskCompletionSource<(RemoteAudioTrack track, TrackPublication publication)>();
        subscriberRoom.TrackSubscribed += (sender, args) =>
        {
            if (args.Track is RemoteAudioTrack audioTrack)
            {
                Log($"Subscriber received audio track: {args.Publication.Sid}");
                trackSubscribedTcs.TrySetResult((audioTrack, args.Publication));
            }
        };

        await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
        Assert.True(subscriberRoom.IsConnected);
        Log("Subscriber connected");

        // Publish audio track from publisher
        const int sampleRate = 48000;
        const int numChannels = 1;
        using var audioSource = new AudioSource(sampleRate, numChannels);
        var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);

        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        Assert.NotNull(publication);
        Log($"Audio track published: {publication.Sid}");

        // Wait for subscriber to receive the track
        var (remoteTrack, remotePub) = await trackSubscribedTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(10)
        );

        Assert.NotNull(remoteTrack);
        Assert.NotNull(remotePub);
        Assert.Equal(Proto.TrackKind.KindAudio, remoteTrack.Kind);

        // Clean up
        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();

        Log("Test completed successfully!");
    }

    [Fact]
    public async Task SubscribeToRemoteVideoTrack_ReceivesVideoData()
    {
        const string roomName = "test-video-subscription-room";
        const string publisherIdentity = "video-publisher";
        const string subscriberIdentity = "video-subscriber";

        Log("Starting SubscribeToRemoteVideoTrack test");

        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var subscriberToken = _fixture.CreateToken(subscriberIdentity, roomName);

        // Create publisher room
        using var publisherRoom = new Room();
        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        Log("Publisher connected");

        // Create subscriber room
        using var subscriberRoom = new Room();

        var trackSubscribedTcs =
            new TaskCompletionSource<(RemoteVideoTrack track, TrackPublication publication)>();
        subscriberRoom.TrackSubscribed += (sender, args) =>
        {
            if (args.Track is RemoteVideoTrack videoTrack)
            {
                Log($"Subscriber received video track: {args.Publication.Sid}");
                trackSubscribedTcs.TrySetResult((videoTrack, args.Publication));
            }
        };

        await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
        Log("Subscriber connected");

        // Publish video track from publisher
        const int width = 640;
        const int height = 480;
        using var videoSource = new VideoSource(width, height);
        var videoTrack = LocalVideoTrack.Create("test-video", videoSource);

        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(videoTrack);
        Assert.NotNull(publication);
        Log($"Video track published: {publication.Sid}");

        // Wait for subscriber to receive the track
        var (remoteTrack, remotePub) = await trackSubscribedTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(10)
        );

        Assert.NotNull(remoteTrack);
        Assert.NotNull(remotePub);
        Assert.Equal(Proto.TrackKind.KindVideo, remoteTrack.Kind);

        // Clean up
        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();

        Log("Test completed successfully!");
    }

    [Fact]
    public async Task MultipleParticipants_SubscribeToEachOther()
    {
        const string roomName = "test-multi-participant-room";
        const string participant1Identity = "participant-1";
        const string participant2Identity = "participant-2";

        Log("Starting MultipleParticipants test");

        var token1 = _fixture.CreateToken(participant1Identity, roomName);
        var token2 = _fixture.CreateToken(participant2Identity, roomName);

        using var room1 = new Room();
        using var room2 = new Room();

        // Track what each participant receives
        var room1TracksTcs = new TaskCompletionSource<TrackPublication>();
        var room2TracksTcs = new TaskCompletionSource<TrackPublication>();

        room1.TrackSubscribed += (sender, args) =>
        {
            Log($"Room1 received track from {args.Participant.Identity}");
            room1TracksTcs.TrySetResult(args.Publication);
        };

        room2.TrackSubscribed += (sender, args) =>
        {
            Log($"Room2 received track from {args.Participant.Identity}");
            room2TracksTcs.TrySetResult(args.Publication);
        };

        // Connect both participants
        await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
        await room2.ConnectAsync(_fixture.LiveKitUrl, token2);

        Log("Both participants connected");

        // Each participant publishes audio
        using var audioSource1 = new AudioSource(48000, 1);
        using var audioSource2 = new AudioSource(48000, 1);

        var track1 = LocalAudioTrack.Create("audio-1", audioSource1);
        var track2 = LocalAudioTrack.Create("audio-2", audioSource2);

        var pub1 = await room1.LocalParticipant!.PublishTrackAsync(track1);
        var pub2 = await room2.LocalParticipant!.PublishTrackAsync(track2);

        Log($"Participant 1 published: {pub1.Sid}");
        Log($"Participant 2 published: {pub2.Sid}");

        // Wait for cross-subscriptions
        var receivedByRoom1 = await room1TracksTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var receivedByRoom2 = await room2TracksTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(receivedByRoom1);
        Assert.NotNull(receivedByRoom2);
        Assert.Equal(pub2.Sid, receivedByRoom1.Sid);
        Assert.Equal(pub1.Sid, receivedByRoom2.Sid);

        Log("Both participants successfully subscribed to each other's tracks");

        await room1.DisconnectAsync();
        await room2.DisconnectAsync();

        Log("Test completed successfully!");
    }

    [Fact]
    public async Task SetSubscribed_UnsubscribeAndResubscribe_WorksCorrectly()
    {
        const string roomName = "test-setsubscribed-room";
        const string publisherIdentity = "publisher";
        const string subscriberIdentity = "subscriber";

        Log("Starting SetSubscribed test");

        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var subscriberToken = _fixture.CreateToken(subscriberIdentity, roomName);

        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();

        // Track subscription events
        var trackSubscribedTcs = new TaskCompletionSource<RemoteTrackPublication>();
        var trackUnsubscribedTcs = new TaskCompletionSource<RemoteTrackPublication>();
        var trackResubscribedTcs = new TaskCompletionSource<RemoteTrackPublication>();

        int subscriptionCount = 0;

        subscriberRoom.TrackSubscribed += (sender, args) =>
        {
            subscriptionCount++;
            Log($"Track subscribed (count: {subscriptionCount}): {args.Publication.Sid}");

            if (subscriptionCount == 1)
            {
                trackSubscribedTcs.TrySetResult((RemoteTrackPublication)args.Publication);
            }
            else if (subscriptionCount == 2)
            {
                trackResubscribedTcs.TrySetResult((RemoteTrackPublication)args.Publication);
            }
        };

        subscriberRoom.TrackUnsubscribed += (sender, args) =>
        {
            Log($"Track unsubscribed: {args.Publication.Sid}");
            trackUnsubscribedTcs.TrySetResult((RemoteTrackPublication)args.Publication);
        };

        // Connect both rooms
        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        Log("Publisher connected");

        // Connect subscriber with AutoSubscribe=true (default)
        await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
        Log("Subscriber connected");

        // Publish audio track
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);
        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Audio track published: {publication.Sid}");

        // Wait for automatic subscription
        var remotePublication = await trackSubscribedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(remotePublication);
        Assert.NotNull(remotePublication.Track);
        Assert.True(remotePublication.IsSubscribed);
        Log("Track automatically subscribed");

        // Wait a bit for subscription to stabilize
        await Task.Delay(500);

        // Unsubscribe using SetSubscribed
        Log("Calling SetSubscribed(false)...");
        remotePublication.SetSubscribed(false);

        // Wait for unsubscribe event
        var unsubscribedPub = await trackUnsubscribedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(unsubscribedPub);
        Assert.Equal(remotePublication.Sid, unsubscribedPub.Sid);
        Log("Track successfully unsubscribed");

        // Wait a bit before resubscribing
        await Task.Delay(500);

        // Resubscribe using SetSubscribed
        Log("Calling SetSubscribed(true)...");
        remotePublication.SetSubscribed(true);

        // Wait for resubscribe event
        var resubscribedPub = await trackResubscribedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(resubscribedPub);
        Assert.Equal(remotePublication.Sid, resubscribedPub.Sid);
        Assert.NotNull(resubscribedPub.Track);
        Assert.True(resubscribedPub.IsSubscribed);
        Log("Track successfully resubscribed");

        // Clean up
        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();

        Log("Test completed successfully!");
    }

    [Fact]
    public async Task WaitForSubscriptionAsync_WaitsForFirstSubscriber()
    {
        const string roomName = "test-wait-for-subscription-room";
        const string publisherIdentity = "publisher-waiting";
        const string subscriberIdentity = "subscriber-joining";

        Log("Starting WaitForSubscriptionAsync test");

        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var subscriberToken = _fixture.CreateToken(subscriberIdentity, roomName);

        // Create publisher room
        using var publisherRoom = new Room();
        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        Assert.True(publisherRoom.IsConnected);
        Log("Publisher connected");

        // Publish audio track from publisher
        const int sampleRate = 48000;
        const int numChannels = 1;
        using var audioSource = new AudioSource(sampleRate, numChannels);
        var audioTrack = LocalAudioTrack.Create("test-audio-wait", audioSource);

        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        Assert.NotNull(publication);
        Assert.IsType<LocalTrackPublication>(publication);
        var localPublication = (LocalTrackPublication)publication;
        Log($"Audio track published: {publication.Sid}");

        // Start waiting for subscription in a separate task
        var waitTask = Task.Run(async () =>
        {
            Log("Publisher starting to wait for first subscription...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await localPublication.WaitForSubscriptionAsync();
            sw.Stop();
            Log($"Publisher's wait completed after {sw.ElapsedMilliseconds}ms");
            return sw.ElapsedMilliseconds;
        });

        // Wait a bit to ensure the wait has started
        await Task.Delay(500);

        // Verify the wait task hasn't completed yet (no subscribers yet)
        Assert.False(
            waitTask.IsCompleted,
            "WaitForSubscriptionAsync should not complete before subscriber joins"
        );
        Log("Verified that wait is still pending (no subscribers yet)");

        // Now connect subscriber
        using var subscriberRoom = new Room();
        var trackSubscribedTcs =
            new TaskCompletionSource<(RemoteAudioTrack track, TrackPublication pub)>();
        subscriberRoom.TrackSubscribed += (sender, args) =>
        {
            if (args.Track is RemoteAudioTrack audioTrack)
            {
                Log($"Subscriber received audio track: {args.Publication.Sid}");
                trackSubscribedTcs.TrySetResult((audioTrack, args.Publication));
            }
        };

        await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
        Assert.True(subscriberRoom.IsConnected);
        Log("Subscriber connected");

        // Wait for subscriber to receive the track
        var (remoteTrack, remotePub) = await trackSubscribedTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(10)
        );
        Assert.NotNull(remoteTrack);
        Assert.NotNull(remotePub);
        Log("Subscriber successfully subscribed to track");

        // Now the wait task should complete
        var elapsedMs = await waitTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(
            waitTask.IsCompleted,
            "WaitForSubscriptionAsync should complete after subscriber joins"
        );
        Log($"Publisher's WaitForSubscriptionAsync completed successfully after {elapsedMs}ms");

        // Clean up
        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();

        Log("Test completed successfully!");
    }

    [Fact]
    public async Task WaitForSubscriptionAsync_WithCancellation_ThrowsTaskCanceledException()
    {
        const string roomName = "test-wait-cancellation-room";
        const string publisherIdentity = "publisher-cancelling";

        Log("Starting WaitForSubscriptionAsync cancellation test");

        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);

        // Create publisher room
        using var publisherRoom = new Room();
        await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
        Assert.True(publisherRoom.IsConnected);
        Log("Publisher connected");

        // Publish audio track from publisher
        const int sampleRate = 48000;
        const int numChannels = 1;
        using var audioSource = new AudioSource(sampleRate, numChannels);
        var audioTrack = LocalAudioTrack.Create("test-audio-cancel", audioSource);

        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        Assert.NotNull(publication);
        Assert.IsType<LocalTrackPublication>(publication);
        var localPublication = (LocalTrackPublication)publication;
        Log($"Audio track published: {publication.Sid}");

        // Create a cancellation token that will cancel after 1 second
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Wait for subscription with cancellation token (should timeout)
        Log("Starting WaitForSubscriptionAsync with 1-second timeout...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await localPublication.WaitForSubscriptionAsync(cts.Token);
        });
        sw.Stop();

        Log($"TaskCanceledException thrown as expected after {sw.ElapsedMilliseconds}ms");
        Assert.True(
            sw.ElapsedMilliseconds >= 900 && sw.ElapsedMilliseconds <= 1500,
            $"Cancellation should occur around 1 second, but took {sw.ElapsedMilliseconds}ms"
        );

        // Clean up
        await publisherRoom.DisconnectAsync();

        Log("Cancellation test completed successfully!");
    }
}
