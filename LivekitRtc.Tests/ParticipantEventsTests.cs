// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using Livekit.Server.Sdk.Dotnet;
using Xunit.Abstractions;
using ApiPermission = Livekit.Server.Sdk.Dotnet.ParticipantPermission;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for the participant-level room events: ParticipantConnected and
/// ParticipantDisconnected, the metadata, name, attributes, permissions and encryption status
/// changes, ParticipantActive, ActiveSpeakersChanged, LocalTrackRepublished (forced with
/// Room.SimulateScenarioAsync) and DataChannelBufferedAmountLowThresholdChanged.
/// </summary>
[Collection("RtcTests")]
public class ParticipantEventsTests
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ParticipantEventsTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    [Fact]
    public async Task ParticipantConnected_Disconnected_EventsFire()
    {
        Log($"Starting ParticipantConnected_Disconnected test");

        using var room1 = new Room();
        using var room2 = new Room();

        var participantConnectedTcs = new TaskCompletionSource<Participant>();
        var participantDisconnectedTcs = new TaskCompletionSource<Participant>();

        room1.ParticipantConnected += (sender, participant) =>
        {
            Log($"Room1 received ParticipantConnected: {participant.Identity}");
            participantConnectedTcs.TrySetResult(participant);
        };

        room1.ParticipantDisconnected += (sender, participant) =>
        {
            Log($"Room1 received ParticipantDisconnected: {participant.Identity}");
            participantDisconnectedTcs.TrySetResult(participant);
        };

        // Connect both participants
        var token1 = _fixture.CreateToken("participant1", "test-room");
        var token2 = _fixture.CreateToken("participant2", "test-room");

        await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
        Log($"Room1 connected");

        await room2.ConnectAsync(_fixture.LiveKitUrl, token2);
        Log($"Room2 connected");

        // Wait for ParticipantConnected event
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connectedParticipant = await participantConnectedTcs.Task.WaitAsync(cts1.Token);
        Assert.NotNull(connectedParticipant);
        Assert.Equal("participant2", connectedParticipant.Identity);
        Log($"ParticipantConnected event fired successfully");

        // Disconnect participant2
        await room2.DisconnectAsync();
        Log($"Room2 disconnected");

        // Wait for ParticipantDisconnected event
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var disconnectedParticipant = await participantDisconnectedTcs.Task.WaitAsync(cts2.Token);
        Assert.NotNull(disconnectedParticipant);
        Assert.Equal("participant2", disconnectedParticipant.Identity);
        Log($"ParticipantDisconnected event fired successfully");

        await room1.DisconnectAsync();
        Log($"Test completed successfully!");
    }

    [Fact]
    public async Task ParticipantMetadataChanged_EventFires_WhenParticipantUpdatesMetadata()
    {
        Log($"Starting ParticipantMetadataChanged test");

        using var room1 = new Room();
        using var room2 = new Room();

        var participantConnectedTcs = new TaskCompletionSource<Participant>();
        var metadataChangedTcs = new TaskCompletionSource<Participant>();

        room1.ParticipantConnected += (sender, participant) =>
        {
            if (participant.Identity == "participant2")
            {
                Log($"Participant2 connected to room1");
                participantConnectedTcs.TrySetResult(participant);
            }
        };

        room1.ParticipantMetadataChanged += (sender, participant) =>
        {
            Log($"ParticipantMetadataChanged event fired for participant: {participant.Identity}");
            metadataChangedTcs.TrySetResult(participant);
        };

        var token1 = _fixture.CreateToken("participant1", "test-room");
        var token2 = new Livekit.Server.Sdk.Dotnet.AccessToken(
            RtcTestFixture.API_KEY,
            RtcTestFixture.API_SECRET
        )
            .WithIdentity("participant2")
            .WithName("participant2")
            .WithGrants(
                new Livekit.Server.Sdk.Dotnet.VideoGrants
                {
                    RoomJoin = true,
                    Room = "test-room",
                    CanUpdateOwnMetadata = true,
                }
            )
            .ToJwt();

        await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
        await room2.ConnectAsync(_fixture.LiveKitUrl, token2);

        // Wait for room1 to see participant2
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await participantConnectedTcs.Task.WaitAsync(connectCts.Token);
        Log($"Room1 sees participant2, proceeding with metadata update");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() =>
            metadataChangedTcs.TrySetException(
                new TimeoutException("ParticipantMetadataChanged event did not fire")
            )
        );

        await room2.LocalParticipant!.SetMetadataAsync("test-metadata");

        var participant = await metadataChangedTcs.Task;
        Assert.Equal("participant2", participant.Identity);
        Assert.Equal("test-metadata", participant.Metadata);

        await room1.DisconnectAsync();
        await room2.DisconnectAsync();

        Log($"ParticipantMetadataChanged test completed successfully");
    }

    [Fact]
    public async Task ParticipantNameChanged_EventFires_WhenParticipantUpdatesName()
    {
        Log($"Starting ParticipantNameChanged test");

        using var room1 = new Room();
        using var room2 = new Room();

        var participantConnectedTcs = new TaskCompletionSource<Participant>();
        var nameChangedTcs = new TaskCompletionSource<Participant>();

        room1.ParticipantConnected += (sender, participant) =>
        {
            if (participant.Identity == "participant2")
            {
                Log($"Participant2 connected to room1");
                participantConnectedTcs.TrySetResult(participant);
            }
        };

        room1.ParticipantNameChanged += (sender, participant) =>
        {
            Log($"ParticipantNameChanged event fired for participant: {participant.Identity}");
            nameChangedTcs.TrySetResult(participant);
        };

        var token1 = _fixture.CreateToken("participant1", "test-room");
        var token2 = new Livekit.Server.Sdk.Dotnet.AccessToken(
            RtcTestFixture.API_KEY,
            RtcTestFixture.API_SECRET
        )
            .WithIdentity("participant2")
            .WithName("participant2")
            .WithGrants(
                new Livekit.Server.Sdk.Dotnet.VideoGrants
                {
                    RoomJoin = true,
                    Room = "test-room",
                    CanUpdateOwnMetadata = true,
                }
            )
            .ToJwt();

        await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
        await room2.ConnectAsync(_fixture.LiveKitUrl, token2);

        // Wait for room1 to see participant2
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await participantConnectedTcs.Task.WaitAsync(connectCts.Token);
        Log($"Room1 sees participant2, proceeding with name update");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() =>
            nameChangedTcs.TrySetException(
                new TimeoutException("ParticipantNameChanged event did not fire")
            )
        );

        await room2.LocalParticipant!.SetNameAsync("test-name");

        var participant = await nameChangedTcs.Task;
        Assert.Equal("participant2", participant.Identity);
        Assert.Equal("test-name", participant.Name);

        await room1.DisconnectAsync();
        await room2.DisconnectAsync();

        Log($"ParticipantNameChanged test completed successfully");
    }

    [Fact]
    public async Task ParticipantAttributesChanged_EventFires_WhenParticipantUpdatesAttributes()
    {
        Log($"Starting ParticipantAttributesChanged test");

        using var room1 = new Room();
        using var room2 = new Room();

        var participantConnectedTcs = new TaskCompletionSource<Participant>();
        var attributesChangedTcs =
            new TaskCompletionSource<ParticipantAttributesChangedEventArgs>();

        room1.ParticipantConnected += (sender, participant) =>
        {
            if (participant.Identity == "participant2")
            {
                Log($"Participant2 connected to room1");
                participantConnectedTcs.TrySetResult(participant);
            }
        };

        room1.ParticipantAttributesChanged += (sender, e) =>
        {
            Log(
                $"ParticipantAttributesChanged event fired for participant: {e.Participant.Identity}"
            );
            attributesChangedTcs.TrySetResult(e);
        };

        var token1 = _fixture.CreateToken("participant1", "test-room");
        var token2 = new Livekit.Server.Sdk.Dotnet.AccessToken(
            RtcTestFixture.API_KEY,
            RtcTestFixture.API_SECRET
        )
            .WithIdentity("participant2")
            .WithName("participant2")
            .WithGrants(
                new Livekit.Server.Sdk.Dotnet.VideoGrants
                {
                    RoomJoin = true,
                    Room = "test-room",
                    CanUpdateOwnMetadata = true,
                }
            )
            .ToJwt();

        await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
        await room2.ConnectAsync(_fixture.LiveKitUrl, token2);

        // Wait for room1 to see participant2
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await participantConnectedTcs.Task.WaitAsync(connectCts.Token);
        Log($"Room1 sees participant2, proceeding with attributes update");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() =>
            attributesChangedTcs.TrySetException(
                new TimeoutException("ParticipantAttributesChanged event did not fire")
            )
        );

        var testAttributes = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" },
        };
        await room2.LocalParticipant!.SetAttributesAsync(testAttributes);

        var eventArgs = await attributesChangedTcs.Task;
        Assert.Equal("participant2", eventArgs.Participant.Identity);
        Assert.Equal(2, eventArgs.Attributes.Count);
        Assert.Equal("value1", eventArgs.Attributes["key1"]);
        Assert.Equal("value2", eventArgs.Attributes["key2"]);
        Assert.Equal(2, eventArgs.ChangedAttributes.Count);
        Assert.Equal("value1", eventArgs.ChangedAttributes["key1"]);
        Assert.Equal("value2", eventArgs.ChangedAttributes["key2"]);

        await room1.DisconnectAsync();
        await room2.DisconnectAsync();

        Log($"ParticipantAttributesChanged test completed successfully");
    }

    [Fact]
    public async Task ParticipantActive_FiresWhenRemoteParticipantJoins()
    {
        const string roomName = "test-participant-active-room";
        using var firstRoom = new Room();
        using var secondRoom = new Room();

        var activeTcs = new TaskCompletionSource<RemoteParticipant>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        firstRoom.ParticipantActive += (_, participant) =>
        {
            Log($"ParticipantActive: {participant.Identity}");
            if (participant.Identity == "pa-second")
                activeTcs.TrySetResult(participant);
        };

        await firstRoom.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken("pa-first", roomName)
        );
        await secondRoom.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken("pa-second", roomName)
        );

        var participant = await activeTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("pa-second", participant.Identity);

        await secondRoom.DisconnectAsync();
        await firstRoom.DisconnectAsync();
    }

    [Fact]
    public async Task ParticipantPermissionsChanged_FiresForRemoteAndLocal_AfterServerUpdate()
    {
        const string roomName = "test-permissions-room";
        const string observerIdentity = "perm-observer";
        const string targetIdentity = "perm-target";

        using var observerRoom = new Room();
        using var targetRoom = new Room();

        var remoteTcs = new TaskCompletionSource<ParticipantPermissionsChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var localTcs = new TaskCompletionSource<ParticipantPermissionsChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        observerRoom.ParticipantPermissionsChanged += (_, args) =>
        {
            Log($"Observer saw permissions change for {args.Participant.Identity}");
            if (args.Participant.Identity == targetIdentity)
                remoteTcs.TrySetResult(args);
        };
        targetRoom.ParticipantPermissionsChanged += (_, args) =>
        {
            Log($"Target saw permissions change for {args.Participant.Identity}");
            if (args.Participant is LocalParticipant)
                localTcs.TrySetResult(args);
        };

        await _fixture.ConnectRoomsAndWaitForEvents(
            observerRoom,
            targetRoom,
            _fixture.CreateToken(observerIdentity, roomName),
            _fixture.CreateToken(targetIdentity, roomName),
            targetIdentity,
            Log
        );

        // Change the target's permissions through the server API
        var client = new RoomServiceClient(
            _fixture.LiveKitUrl.Replace("ws://", "http://"),
            RtcTestFixture.API_KEY,
            RtcTestFixture.API_SECRET
        );
        await client.UpdateParticipant(
            new UpdateParticipantRequest
            {
                Room = roomName,
                Identity = targetIdentity,
                Permission = new ApiPermission
                {
                    CanSubscribe = true,
                    CanPublish = false,
                    CanPublishData = false,
                },
            }
        );
        Log("Permissions updated through RoomServiceClient");

        var remoteArgs = await remoteTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(remoteArgs.Permissions);
        Assert.True(remoteArgs.Permissions!.CanSubscribe);
        Assert.False(remoteArgs.Permissions.CanPublish);
        Assert.False(remoteArgs.Permissions.CanPublishData);
        // The participant object is updated before the event fires. A bulk participant update
        // may replace the info object afterwards, so compare by value, not by reference.
        Assert.Equal(remoteArgs.Permissions, remoteArgs.Participant.Permissions);

        var localArgs = await localTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(targetRoom.LocalParticipant, localArgs.Participant);
        Assert.NotNull(localArgs.Permissions);
        Assert.False(localArgs.Permissions!.CanPublish);

        await targetRoom.DisconnectAsync();
        await observerRoom.DisconnectAsync();
    }

    [Fact]
    public async Task SetDataChannelBufferedAmountLowThreshold_RaisesRoomEvent()
    {
        const string roomName = "test-threshold-room";
        using var room = new Room();

        var tcs = new TaskCompletionSource<DataChannelBufferedAmountLowThresholdChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        room.DataChannelBufferedAmountLowThresholdChanged += (_, args) =>
        {
            Log($"Threshold changed: {args.Kind} = {args.Threshold}");
            tcs.TrySetResult(args);
        };

        await room.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken("threshold-participant", roomName)
        );

        room.LocalParticipant!.SetDataChannelBufferedAmountLowThreshold(
            65536,
            Proto.DataPacketKind.KindReliable
        );

        var args = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Proto.DataPacketKind.KindReliable, args.Kind);
        Assert.Equal(65536UL, args.Threshold);

        await room.DisconnectAsync();
    }

    [Fact]
    public async Task LocalTrackRepublished_AfterFullReconnect_UpdatesPublicationInPlace()
    {
        const string roomName = "test-republish-room";
        const string publisherIdentity = "republish-publisher";
        const string subscriberIdentity = "republish-subscriber";

        using var subscriberRoom = new Room();
        using var publisherRoom = new Room();
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("republished-audio", audioSource);

        var reconnectingTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var republishedTcs = new TaskCompletionSource<LocalTrackRepublishedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reconnectedTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        publisherRoom.Reconnecting += (_, _) =>
        {
            Log("Publisher: Reconnecting");
            reconnectingTcs.TrySetResult(true);
        };
        publisherRoom.LocalTrackRepublished += (_, args) =>
        {
            Log($"Publisher: LocalTrackRepublished {args.PreviousSid} -> {args.Publication.Sid}");
            republishedTcs.TrySetResult(args);
        };
        publisherRoom.Reconnected += (_, _) =>
        {
            Log("Publisher: Reconnected");
            reconnectedTcs.TrySetResult(true);
        };

        // Every SID the subscriber sees published by the publisher, in order
        var publishedSids = new ConcurrentQueue<string>();
        using var publishedSignal = new SemaphoreSlim(0);
        subscriberRoom.TrackPublished += (_, args) =>
        {
            Log(
                $"Subscriber: TrackPublished {args.Publication.Sid} by {args.Participant.Identity}"
            );
            if (args.Participant.Identity == publisherIdentity)
            {
                publishedSids.Enqueue(args.Publication.Sid);
                publishedSignal.Release();
            }
        };

        await _fixture.ConnectRoomsAndWaitForEvents(
            subscriberRoom,
            publisherRoom,
            _fixture.CreateToken(subscriberIdentity, roomName),
            _fixture.CreateToken(publisherIdentity, roomName),
            publisherIdentity,
            Log
        );

        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(
            audioTrack,
            new TrackPublishOptions { Source = Proto.TrackSource.SourceMicrophone }
        );
        var originalSid = publication.Sid;
        Log($"Published {originalSid}");
        Assert.True(
            await publishedSignal.WaitAsync(TimeSpan.FromSeconds(10)),
            "The subscriber never saw the first publication"
        );
        Assert.True(publishedSids.TryDequeue(out var firstSeenSid));
        Assert.Equal(originalSid, firstSeenSid);

        await publisherRoom.SimulateScenarioAsync(Proto.SimulateScenarioKind.SimulateFullReconnect);
        Log("Full reconnect requested");

        await reconnectingTcs.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var republished = await republishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // The publication object survives the republish and is updated in place
        Assert.Equal(originalSid, republished.PreviousSid);
        Assert.Same(publication, republished.Publication);
        Assert.Same(publisherRoom.LocalParticipant, republished.Participant);
        Assert.False(string.IsNullOrEmpty(publication.Sid));
        Assert.NotEqual(originalSid, publication.Sid);
        Assert.Same(audioTrack, publication.Track);
        Assert.Equal(publication.Sid, audioTrack.Sid);

        // The participant's publications are re-keyed, with no stale entry left behind
        var localPublications = publisherRoom.LocalParticipant.TrackPublications;
        Assert.Single(localPublications);
        Assert.True(localPublications.ContainsKey(publication.Sid));
        Assert.False(localPublications.ContainsKey(originalSid));

        // The subscriber sees the republished track under its new SID, and exactly once
        Assert.True(
            await publishedSignal.WaitAsync(TimeSpan.FromSeconds(20)),
            "The subscriber never saw the republished track"
        );
        Assert.True(publishedSids.TryDequeue(out var republishedSeenSid));
        Assert.Equal(publication.Sid, republishedSeenSid);
        await WaitUntilAsync(
            () =>
                subscriberRoom.RemoteParticipants.TryGetValue(publisherIdentity, out var p)
                && p.TrackPublications.ContainsKey(publication.Sid)
                && !p.TrackPublications.ContainsKey(originalSid),
            TimeSpan.FromSeconds(10),
            "The subscriber still lists the previous publication"
        );
        var remoteAudioPublications = subscriberRoom
            .RemoteParticipants[publisherIdentity]
            .TrackPublications.Values.Where(p => p.Kind == Proto.TrackKind.KindAudio)
            .ToList();
        var remotePublication = Assert.Single(remoteAudioPublications);
        Assert.Equal(publication.Sid, remotePublication.Sid);

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
    }

    [Fact]
    public async Task ParticipantEncryptionStatusChanged_FollowsTheRemotePublications()
    {
        const string roomName = "test-encryption-status-room";
        const string publisherIdentity = "enc-publisher";
        const string subscriberIdentity = "enc-subscriber";

        // The status only depends on the remote publications being encrypted, but share the key
        // anyway so the subscriber can also decrypt them
        var sharedKey = new byte[32];
        new Random(7).NextBytes(sharedKey);
        RoomOptions WithE2EE() =>
            new RoomOptions
            {
                E2EE = new E2EEOptions
                {
                    KeyProviderOptions = new KeyProviderOptions { SharedKey = sharedKey },
                },
            };

        using var subscriberRoom = new Room();
        using var publisherRoom = new Room();
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("encrypted-audio", audioSource);

        var publisherJoinedTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        subscriberRoom.ParticipantConnected += (_, participant) =>
        {
            if (participant.Identity == publisherIdentity)
                publisherJoinedTcs.TrySetResult(true);
        };
        var statusChanges = new ConcurrentQueue<ParticipantEncryptionStatusChangedEventArgs>();
        using var statusSignal = new SemaphoreSlim(0);
        subscriberRoom.ParticipantEncryptionStatusChanged += (_, args) =>
        {
            Log($"Subscriber: {args.Participant.Identity} encrypted = {args.IsEncrypted}");
            if (args.Participant.Identity == publisherIdentity)
            {
                statusChanges.Enqueue(args);
                statusSignal.Release();
            }
        };

        await subscriberRoom.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken(subscriberIdentity, roomName),
            WithE2EE()
        );
        await publisherRoom.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken(publisherIdentity, roomName),
            WithE2EE()
        );
        await publisherJoinedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Publishing an encrypted track turns the remote participant's status on...
        var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Published {publication.Sid}");
        Assert.True(
            await statusSignal.WaitAsync(TimeSpan.FromSeconds(10)),
            "No encryption status change after publishing"
        );
        Assert.True(statusChanges.TryDequeue(out var encrypted));
        Assert.True(encrypted.IsEncrypted);
        Assert.Same(subscriberRoom.RemoteParticipants[publisherIdentity], encrypted.Participant);

        // ...and unpublishing its only track turns it off again
        await publisherRoom.LocalParticipant.UnpublishTrackAsync(publication.Sid);
        Log($"Unpublished {publication.Sid}");
        Assert.True(
            await statusSignal.WaitAsync(TimeSpan.FromSeconds(10)),
            "No encryption status change after unpublishing"
        );
        Assert.True(statusChanges.TryDequeue(out var cleared));
        Assert.False(cleared.IsEncrypted);

        await publisherRoom.DisconnectAsync();
        await subscriberRoom.DisconnectAsync();
    }

    [Fact]
    public async Task ActiveSpeakersChanged_ListsThePublisher_WhileLoudAudioIsPublished()
    {
        const string roomName = "test-active-speakers-room";
        const string publisherIdentity = "speaker-publisher";
        const string listenerIdentity = "speaker-listener";

        using var listenerRoom = new Room();
        using var publisherRoom = new Room();
        using var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("loud-audio", audioSource);

        var listenerTcs = new TaskCompletionSource<ActiveSpeakersChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var publisherTcs = new TaskCompletionSource<ActiveSpeakersChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        listenerRoom.ActiveSpeakersChanged += (_, args) =>
        {
            Log(
                $"Listener: speakers = [{string.Join(", ", args.Speakers.Select(s => s.Identity))}]"
            );
            if (args.Speakers.Any(s => s.Identity == publisherIdentity))
                listenerTcs.TrySetResult(args);
        };
        publisherRoom.ActiveSpeakersChanged += (_, args) =>
        {
            Log(
                $"Publisher: speakers = [{string.Join(", ", args.Speakers.Select(s => s.Identity))}]"
            );
            if (args.Speakers.Any(s => s is LocalParticipant))
                publisherTcs.TrySetResult(args);
        };

        await _fixture.ConnectRoomsAndWaitForEvents(
            listenerRoom,
            publisherRoom,
            _fixture.CreateToken(listenerIdentity, roomName),
            _fixture.CreateToken(publisherIdentity, roomName),
            publisherIdentity,
            Log
        );

        await publisherRoom.LocalParticipant!.PublishTrackAsync(
            audioTrack,
            new TrackPublishOptions { Source = Proto.TrackSource.SourceMicrophone }
        );

        // A loud 500 Hz tone (five full cycles per 10 ms frame, about -3 dBFS) fed in real time
        // until the server reports the publisher as an active speaker
        var tone = new short[480];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = (short)(Math.Sin(2 * Math.PI * 500 * i / 48000) * 23000);
        using var stopSending = new CancellationTokenSource();
        var sendTask = Task.Run(async () =>
        {
            while (!stopSending.IsCancellationRequested)
            {
                await audioSource.CaptureFrameAsync(new AudioFrame(tone, 48000, 1, 480));
                await Task.Delay(10);
            }
        });

        try
        {
            var listenerArgs = await listenerTcs.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var speaker = Assert.Single(
                listenerArgs.Speakers,
                s => s.Identity == publisherIdentity
            );
            Assert.Same(listenerRoom.RemoteParticipants[publisherIdentity], speaker);

            // The publisher's own room reports it too, as its local participant
            var publisherArgs = await publisherTcs.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Same(
                publisherRoom.LocalParticipant,
                Assert.Single(publisherArgs.Speakers, s => s is LocalParticipant)
            );
        }
        finally
        {
            stopSending.Cancel();
            await sendTask;
        }

        await publisherRoom.DisconnectAsync();
        await listenerRoom.DisconnectAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail(message);
            await Task.Delay(100);
        }
    }
}
