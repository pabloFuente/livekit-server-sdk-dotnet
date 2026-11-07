// author: https://github.com/pabloFuente

using System.Text;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end regression tests for the room, track, data stream and E2EE fixes of the
/// November 2026 audit. Each test names the symptom it guards against. The connection lifecycle
/// fixes (a single Disconnected with its reason, the release of a cancelled connect) are guarded by
/// <see cref="Reliability.RoomLifecycleTests"/>.
/// </summary>
[Collection("RtcTests")]
public class AuditRegressionTests
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AuditRegressionTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private static TaskCompletionSource<T> Tcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task RemoteTrackEvents_FireWhenParticipantLeaves_AndRemoteTracksCarryTheirInfo()
    {
        // TrackUnsubscribed used to be dropped with "[WARN] RoomEvent.TrackUnsubscribed:
        // Participant X not found" when the participant left, because the lookup ran on the
        // event chain after ParticipantDisconnected had removed it. RemoteTrack.Sid was never set.
        const string roomName = "test-leave-room";
        using var subscriberRoom = new Room();
        using var publisherRoom = new Room();
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("leave-audio", audioSource);

        var subscribed = Tcs<TrackSubscribedEventArgs>();
        var unsubscribed = Tcs<TrackSubscribedEventArgs>();
        var unpublished = Tcs<TrackPublishedEventArgs>();
        var disconnected = Tcs<Participant>();
        subscriberRoom.TrackSubscribed += (_, e) => subscribed.TrySetResult(e);
        subscriberRoom.TrackUnsubscribed += (_, e) => unsubscribed.TrySetResult(e);
        subscriberRoom.TrackUnpublished += (_, e) => unpublished.TrySetResult(e);
        subscriberRoom.ParticipantDisconnected += (_, p) => disconnected.TrySetResult(p);

        await _fixture.ConnectRoomsAndWaitForEvents(
            subscriberRoom,
            publisherRoom,
            _fixture.CreateToken("leave-subscriber", roomName),
            _fixture.CreateToken("leave-publisher", roomName),
            "leave-publisher",
            Log
        );
        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);

        var subscribedArgs = await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(publication.Sid, subscribedArgs.Track.Sid);
        Assert.Equal(subscribedArgs.Publication.Sid, subscribedArgs.Track.Sid);
        Assert.Equal("leave-audio", subscribedArgs.Track.Name);
        Assert.Same(subscribedArgs.Track, subscribedArgs.Publication.Track);

        await publisherRoom.DisconnectAsync();

        var unsubscribedArgs = await unsubscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(subscribedArgs.Track, unsubscribedArgs.Track);
        Assert.Null(unsubscribedArgs.Publication.Track);
        var unpublishedArgs = await unpublished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(publication.Sid, unpublishedArgs.Publication.Sid);
        var gone = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("leave-publisher", gone.Identity);
        Assert.Equal(Proto.DisconnectReason.ClientInitiated, gone.DisconnectReason);
        Assert.False(subscriberRoom.RemoteParticipants.ContainsKey("leave-publisher"));

        await subscriberRoom.DisconnectAsync();
    }

    [Fact]
    public async Task PublishTrackAsync_SetsTheLocalTrackSid_AndUnpublishRejectsNonSids()
    {
        using var room = new Room();
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("sid-audio", audioSource);
        await room.ConnectAsync(_fixture.LiveKitUrl, _fixture.CreateToken("sid", "test-sid-room"));

        Assert.NotEqual(string.Empty, audioTrack.Sid); // "TR_unknown" before publishing
        var publication = await room.LocalParticipant!.PublishTrackAsync(audioTrack);
        Assert.Equal(publication.Sid, audioTrack.Sid);
        Assert.StartsWith("TR_", audioTrack.Sid);

        // The FFI panics (and the process exits) on anything but a track SID: reject it first
        await Assert.ThrowsAsync<ArgumentException>(() =>
            room.LocalParticipant.UnpublishTrackAsync("sid-audio")
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            room.LocalParticipant.UnpublishTrackAsync("")
        );

        await room.LocalParticipant.UnpublishTrackAsync(publication.Sid);
        await room.DisconnectAsync();
    }

    [Fact]
    public async Task ParticipantAttributesChanged_UpdatesTheParticipant()
    {
        const string roomName = "test-attrs-room";
        using var observerRoom = new Room();
        using var targetRoom = new Room();
        var changed = Tcs<ParticipantAttributesChangedEventArgs>();
        observerRoom.ParticipantAttributesChanged += (_, e) =>
        {
            if (e.Participant.Identity == "attrs-target")
                changed.TrySetResult(e);
        };
        // Updating one's own attributes needs the CanUpdateOwnMetadata grant
        var targetToken = new Livekit.Server.Sdk.Dotnet.AccessToken(
            RtcTestFixture.API_KEY,
            RtcTestFixture.API_SECRET
        )
            .WithIdentity("attrs-target")
            .WithGrants(
                new Livekit.Server.Sdk.Dotnet.VideoGrants
                {
                    RoomJoin = true,
                    Room = roomName,
                    CanUpdateOwnMetadata = true,
                }
            )
            .ToJwt();
        await _fixture.ConnectRoomsAndWaitForEvents(
            observerRoom,
            targetRoom,
            _fixture.CreateToken("attrs-observer", roomName),
            targetToken,
            "attrs-target",
            Log
        );

        await targetRoom.LocalParticipant!.SetAttributesAsync(
            new Dictionary<string, string> { ["mood"] = "good" }
        );

        var args = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Used to stay empty until the next bulk participant update
        Assert.Equal("good", args.Participant.Attributes["mood"]);
        Assert.Equal("good", observerRoom.RemoteParticipants["attrs-target"].Attributes["mood"]);

        await targetRoom.DisconnectAsync();
        await observerRoom.DisconnectAsync();
    }

    private async Task<(Room sender, Room receiver)> ConnectStreamRoomsAsync(string roomName)
    {
        var sender = new Room();
        var receiver = new Room();
        await _fixture.ConnectRoomsAndWaitForEvents(
            receiver,
            sender,
            _fixture.CreateToken("stream-receiver", roomName),
            _fixture.CreateToken("stream-sender", roomName),
            "stream-sender",
            Log
        );
        return (sender, receiver);
    }

    [Fact]
    public async Task TextStream_MultiByteCharacters_SurviveChunkBoundaries()
    {
        // Chunks were cut at a raw byte offset, splitting "€" across two chunks, and the receiver
        // decoded each chunk on its own: U+FFFD at every boundary.
        var (sender, receiver) = await ConnectStreamRoomsAsync("test-utf8-room");
        using (sender)
        using (receiver)
        {
            var text =
                new string('a', DataStreamConstants.StreamChunkSize - 1)
                + "€"
                + new string('b', 100)
                + "日本語 ñ 🎉";
            var received = Tcs<string>();
            receiver.RegisterTextStreamHandler(
                "utf8",
                (reader, identity) =>
                    reader
                        .ReadAllAsync()
                        .AsTask()
                        .ContinueWith(t => received.TrySetResult(t.Result))
            );

            var writer = await sender.LocalParticipant!.StreamTextAsync(topic: "utf8");
            await writer.WriteAsync(text);
            await writer.CloseAsync();

            Assert.Equal(text, await received.Task.WaitAsync(TimeSpan.FromSeconds(15)));

            await sender.DisconnectAsync();
            await receiver.DisconnectAsync();
        }
    }

    [Fact]
    public async Task TextStreamWriter_Dispose_ClosesTheStream()
    {
        // The synchronous Dispose used to skip the trailer, leaving the reader open forever
        var (sender, receiver) = await ConnectStreamRoomsAsync("test-dispose-room");
        using (sender)
        using (receiver)
        {
            var received = Tcs<string>();
            receiver.RegisterTextStreamHandler(
                "dispose",
                (reader, identity) =>
                    reader
                        .ReadAllAsync()
                        .AsTask()
                        .ContinueWith(t => received.TrySetResult(t.Result))
            );

            using (var writer = await sender.LocalParticipant!.StreamTextAsync(topic: "dispose"))
            {
                await writer.WriteAsync("closed by Dispose");
            }

            Assert.Equal(
                "closed by Dispose",
                await received.Task.WaitAsync(TimeSpan.FromSeconds(15))
            );

            await sender.DisconnectAsync();
            await receiver.DisconnectAsync();
        }
    }

    [Fact]
    public async Task TextStreamReader_Fails_WhenTheSenderLeavesBeforeClosing()
    {
        // ReadAllAsync used to wait forever
        var (sender, receiver) = await ConnectStreamRoomsAsync("test-abandon-room");
        using (sender)
        using (receiver)
        {
            var outcome = Tcs<Exception?>();
            receiver.RegisterTextStreamHandler(
                "abandon",
                (reader, identity) =>
                    reader
                        .ReadAllAsync()
                        .AsTask()
                        .ContinueWith(t => outcome.TrySetResult(t.Exception?.GetBaseException()))
            );

            var writer = await sender.LocalParticipant!.StreamTextAsync(topic: "abandon");
            await writer.WriteAsync("partial");
            await Task.Delay(500);
            await sender.DisconnectAsync(); // never closes the stream

            var error = await outcome.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsType<StreamException>(error);

            await receiver.DisconnectAsync();
        }
    }

    [Fact]
    public async Task TextStreamHandler_MayBlockOnReadAllAsync()
    {
        // Handlers ran on the FFI callback thread, which also delivers the chunks: blocking in
        // the handler deadlocked the whole FFI event pipeline
        var (sender, receiver) = await ConnectStreamRoomsAsync("test-blocking-room");
        using (sender)
        using (receiver)
        {
            var received = Tcs<string>();
            receiver.RegisterTextStreamHandler(
                "blocking",
                (reader, _) =>
                    received.TrySetResult(reader.ReadAllAsync().AsTask().GetAwaiter().GetResult())
            );

            var writer = await sender.LocalParticipant!.StreamTextAsync(topic: "blocking");
            await writer.WriteAsync("read synchronously");
            await writer.CloseAsync();

            Assert.Equal(
                "read synchronously",
                await received.Task.WaitAsync(TimeSpan.FromSeconds(15))
            );

            await sender.DisconnectAsync();
            await receiver.DisconnectAsync();
        }
    }

    [Fact]
    public async Task DataTrackStream_Dispose_UnblocksAPendingRead()
    {
        // A pending ReadAsync used to throw ChannelClosedException when the stream was disposed
        const string roomName = "test-dt-dispose-room";
        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();
        var published = Tcs<RemoteDataTrack>();
        subscriberRoom.DataTrackPublished += (_, track) => published.TrySetResult(track);
        await _fixture.ConnectRoomsAndWaitForEvents(
            subscriberRoom,
            publisherRoom,
            _fixture.CreateToken("dt-subscriber", roomName),
            _fixture.CreateToken("dt-publisher", roomName),
            "dt-publisher",
            Log
        );
        var localTrack = await publisherRoom.LocalParticipant!.PublishDataTrackAsync("quiet");
        var remoteTrack = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stream = remoteTrack.Subscribe();
        var pendingRead = stream.ReadAsync();
        await Task.Delay(300);
        Assert.False(pendingRead.IsCompleted);
        stream.Dispose();

        Assert.Null(await pendingRead.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.IsEnded);

        localTrack.Unpublish();
        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
    }

    [Fact]
    public async Task FrameCryptor_SetEnabled_AddressesTheCryptorByTrack()
    {
        // The requests omitted the required track SID, so the FFI could not find the cryptor
        const string roomName = "test-cryptor-room";
        var sharedKey = new byte[32];
        new Random(11).NextBytes(sharedKey);
        RoomOptions E2EE() =>
            new()
            {
                E2EE = new E2EEOptions
                {
                    KeyProviderOptions = new KeyProviderOptions { SharedKey = sharedKey },
                },
            };

        using var publisherRoom = new Room();
        using var subscriberRoom = new Room();
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("cryptor-audio", audioSource);
        var subscribed = Tcs<RemoteTrack>();
        subscriberRoom.TrackSubscribed += (_, e) => subscribed.TrySetResult(e.Track);

        await subscriberRoom.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken("cryptor-subscriber", roomName),
            E2EE()
        );
        await publisherRoom.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken("cryptor-publisher", roomName),
            E2EE()
        );
        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);

        var cryptor = Assert.Single(
            publisherRoom.E2EEManager!.GetFrameCryptors(),
            c => c.TrackSid == publication.Sid
        );
        Assert.True(cryptor.Enabled);

        cryptor.SetEnabled(false);
        var disabled = Assert.Single(
            publisherRoom.E2EEManager.GetFrameCryptors(),
            c => c.TrackSid == publication.Sid
        );
        Assert.False(disabled.Enabled);

        cryptor.SetEnabled(true);
        var reenabled = Assert.Single(
            publisherRoom.E2EEManager.GetFrameCryptors(),
            c => c.TrackSid == publication.Sid
        );
        Assert.True(reenabled.Enabled);

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
    }
}
