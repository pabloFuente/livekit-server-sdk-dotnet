// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests
{
    [Collection("LiveKit E2E Tests")]
    public class RoomEventsTests : IClassFixture<RtcTestFixture>, IAsyncLifetime
    {
        private readonly RtcTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public RoomEventsTests(RtcTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ConnectionStateChanged_Reconnecting_Reconnected_EventsFire()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting ConnectionStateChanged test"
            );

            using var room = new Room();

            // Room's event chain dispatches ConnectionStateChanged asynchronously. Record states
            // in a thread-safe queue and wait for the expected state instead of asserting right away
            var stateChanges =
                new System.Collections.Concurrent.ConcurrentQueue<Proto.ConnectionState>();
            var connectedTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var disconnectedTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            room.ConnectionStateChanged += (sender, state) =>
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ConnectionStateChanged: {state}");
                stateChanges.Enqueue(state);
                if (state == Proto.ConnectionState.ConnConnected)
                {
                    connectedTcs.TrySetResult(true);
                }
                else if (
                    state == Proto.ConnectionState.ConnDisconnected
                    && connectedTcs.Task.IsCompleted
                )
                {
                    // ConnectAsync also reports Disconnected as its initial state; only the one
                    // following Connected is the result of DisconnectAsync
                    disconnectedTcs.TrySetResult(true);
                }
            };

            var reconnectingTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var reconnectedTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            room.Reconnecting += (sender, args) =>
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Reconnecting event fired");
                reconnectingTcs.TrySetResult(true);
            };

            room.Reconnected += (sender, args) =>
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Reconnected event fired");
                reconnectedTcs.TrySetResult(true);
            };

            var token = _fixture.CreateToken("test-participant", "test-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);

            // Verify we got Connected state
            await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(Proto.ConnectionState.ConnConnected, stateChanges);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Verified Connected state");

            // Force a full reconnect through the FFI instead of manipulating the network
            await room.SimulateScenarioAsync(Proto.SimulateScenarioKind.SimulateFullReconnect);
            await reconnectingTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Verified Reconnecting and Reconnected events"
            );

            // The Connected state that follows Reconnecting is dispatched before the Reconnected
            // event on the same chain, so it is already recorded here
            var statesAfterReconnect = stateChanges.ToArray();
            var reconnectingIndex = Array.IndexOf(
                statesAfterReconnect,
                Proto.ConnectionState.ConnReconnecting
            );
            Assert.True(
                reconnectingIndex
                    > Array.IndexOf(statesAfterReconnect, Proto.ConnectionState.ConnConnected),
                "Reconnecting state should be reported after the first Connected state"
            );
            Assert.True(
                Array.LastIndexOf(statesAfterReconnect, Proto.ConnectionState.ConnConnected)
                    > reconnectingIndex,
                "Connected state should be reported again after the Reconnecting state"
            );
            Assert.True(room.IsConnected);

            await room.DisconnectAsync();

            // Verify we got Disconnected state after the Connected one
            await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var states = stateChanges.ToArray();
            Assert.Contains(Proto.ConnectionState.ConnDisconnected, states);
            Assert.True(
                Array.LastIndexOf(states, Proto.ConnectionState.ConnDisconnected)
                    > Array.IndexOf(states, Proto.ConnectionState.ConnConnected),
                "Disconnected state should be reported after Connected state"
            );
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed successfully!");
        }

        [Fact]
        public async Task LocalTrackPublished_Unpublished_EventsFire()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting LocalTrackPublished_Unpublished test"
            );

            using var room = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);

            var publishedTcs = new TaskCompletionSource<LocalTrackPublication>();
            var unpublishedTcs = new TaskCompletionSource<LocalTrackPublication>();

            room.LocalTrackPublished += (sender, args) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackPublished: {args.Publication.Sid}"
                );
                publishedTcs.TrySetResult(args.Publication);
            };

            room.LocalTrackUnpublished += (sender, args) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackUnpublished: {args.Publication.Sid}"
                );
                unpublishedTcs.TrySetResult(args.Publication);
            };

            var token = _fixture.CreateToken("test-publisher", "test-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Connected to room");

            // Publish track
            var options = new TrackPublishOptions();
            var publication = await room.LocalParticipant!.PublishTrackAsync(audioTrack, options);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Track published: {publication.Sid}");

            // Wait for LocalTrackPublished event
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var publishedPub = await publishedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(publishedPub);
            Assert.Equal(publication.Sid, publishedPub.Sid);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackPublished event verified");

            // Verify track exists in publications
            Assert.True(room.LocalParticipant.TrackPublications.ContainsKey(publication.Sid));

            // Unpublish track
            await room.LocalParticipant.UnpublishTrackAsync(publication.Sid);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Track unpublished");

            // Wait for LocalTrackUnpublished event
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var unpublishedPub = await unpublishedTcs.Task.WaitAsync(cts2.Token);
            Assert.NotNull(unpublishedPub);
            Assert.Equal(publication.Sid, unpublishedPub.Sid);
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackUnpublished event verified"
            );

            // Verify track removed from local publications
            Assert.False(room.LocalParticipant.TrackPublications.ContainsKey(publication.Sid));
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Track removed from publications verified"
            );

            await room.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed successfully!");
        }

        [Fact]
        public async Task TrackPublished_Unpublished_RemoteEvents()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting TrackPublished_Unpublished remote events test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);

            var trackPublishedTcs = new TaskCompletionSource<RemoteTrackPublication>();
            var trackUnpublishedTcs = new TaskCompletionSource<RemoteTrackPublication>();

            subscriberRoom.TrackPublished += (sender, args) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber: TrackPublished from {args.Participant.Identity}, track: {args.Publication.Sid}"
                );
                trackPublishedTcs.TrySetResult(args.Publication);
            };

            subscriberRoom.TrackUnpublished += (sender, args) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber: TrackUnpublished from {args.Participant.Identity}, track: {args.Publication.Sid}"
                );
                trackUnpublishedTcs.TrySetResult(args.Publication);
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken("publisher", "test-room");
            var subscriberToken = _fixture.CreateToken("subscriber", "test-room");

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Both participants connected");

            // Wait a bit for participants to see each other
            await Task.Delay(500);

            // Publish track
            var options = new TrackPublishOptions();
            var publication = await publisherRoom.LocalParticipant!.PublishTrackAsync(
                audioTrack,
                options
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Publisher published track: {publication.Sid}"
            );

            // Wait for TrackPublished event on subscriber
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remotePub = await trackPublishedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remotePub);
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber received TrackPublished event"
            );

            // Unpublish track
            await publisherRoom.LocalParticipant.UnpublishTrackAsync(publication.Sid);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Publisher unpublished track");

            // Wait for TrackUnpublished event on subscriber
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var unpublishedPub = await trackUnpublishedTcs.Task.WaitAsync(cts2.Token);
            Assert.NotNull(unpublishedPub);
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber received TrackUnpublished event"
            );

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed successfully!");
        }

        [Fact]
        public async Task LocalTrackPublished_Unpublished_MassiveSimultaneousStressTest()
        {
            // The LiveKit server caps concurrent in-flight track publications at
            // cMaxPendingTracks = 20 per participant. Spread the load over several
            // participants so each stays within the cap
            const int PARTICIPANT_COUNT = 5;
            const int TRACKS_PER_PARTICIPANT = 20;
            const int TRACK_COUNT = PARTICIPANT_COUNT * TRACKS_PER_PARTICIPANT;

            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting massive simultaneous track stress test with "
                    + $"{TRACK_COUNT} tracks across {PARTICIPANT_COUNT} participants"
            );

            // Track all published and unpublished events across every participant
            var publishedEvents = new System.Collections.Concurrent.ConcurrentBag<string>();
            var unpublishedEvents = new System.Collections.Concurrent.ConcurrentBag<string>();

            var rooms = new List<Room>();
            var audioSources = new List<AudioSource>();
            var audioTracks = new List<LocalAudioTrack>();

            try
            {
                for (int p = 0; p < PARTICIPANT_COUNT; p++)
                {
                    var room = new Room();
                    rooms.Add(room);

                    room.LocalTrackPublished += (sender, args) =>
                    {
                        publishedEvents.Add(args.Publication.Sid);
                        _output.WriteLine(
                            $"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackPublished: {args.Publication.Sid}"
                        );
                    };

                    room.LocalTrackUnpublished += (sender, args) =>
                    {
                        unpublishedEvents.Add(args.Publication.Sid);
                        _output.WriteLine(
                            $"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackUnpublished: {args.Publication.Sid}"
                        );
                    };

                    var token = _fixture.CreateToken($"stress-participant-{p}", $"stress-room-{p}");
                    await room.ConnectAsync(_fixture.LiveKitUrl, token);
                }

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Connected {PARTICIPANT_COUNT} participants"
                );

                // Create all audio sources and tracks, grouped per participant
                var tracksByParticipant = new List<List<LocalAudioTrack>>();
                for (int p = 0; p < PARTICIPANT_COUNT; p++)
                {
                    var participantTracks = new List<LocalAudioTrack>();
                    for (int t = 0; t < TRACKS_PER_PARTICIPANT; t++)
                    {
                        var source = new AudioSource(48000, 1);
                        var track = LocalAudioTrack.Create($"stress-track-{p}-{t}", source);
                        audioSources.Add(source);
                        audioTracks.Add(track);
                        participantTracks.Add(track);
                    }
                    tracksByParticipant.Add(participantTracks);
                }

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Created {TRACK_COUNT} audio tracks"
                );

                // STRESS TEST 1: Publish ALL tracks simultaneously
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Publishing {TRACK_COUNT} tracks simultaneously..."
                );

                var publishTasks = new List<Task<LocalTrackPublication>>();
                for (int p = 0; p < PARTICIPANT_COUNT; p++)
                {
                    var localParticipant = rooms[p].LocalParticipant!;
                    foreach (var track in tracksByParticipant[p])
                    {
                        publishTasks.Add(
                            localParticipant.PublishTrackAsync(track, new TrackPublishOptions())
                        );
                    }
                }

                var publications = await Task.WhenAll(publishTasks);
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] All {TRACK_COUNT} tracks published"
                );

                // Wait for ALL LocalTrackPublished events (with generous timeout due to volume)
                var publishedDeadline = DateTime.Now.AddSeconds(30);
                while (publishedEvents.Count < TRACK_COUNT && DateTime.Now < publishedDeadline)
                {
                    await Task.Delay(100);
                }

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Received {publishedEvents.Count}/{TRACK_COUNT} LocalTrackPublished events"
                );
                Assert.Equal(TRACK_COUNT, publishedEvents.Count);

                // Verify all publication SIDs were captured
                var publishedSids = new HashSet<string>(publishedEvents);
                var expectedSids = new HashSet<string>(publications.Select(x => x.Sid));
                Assert.Equal(expectedSids.Count, publishedSids.Count);
                Assert.True(
                    expectedSids.SetEquals(publishedSids),
                    "Published event SIDs should match actual publication SIDs"
                );

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] ✓ All LocalTrackPublished events verified"
                );

                // Brief delay before unpublish storm
                await Task.Delay(500);

                // STRESS TEST 2: Unpublish ALL tracks simultaneously
                var unpublishTasks = new List<Task>();
                for (int p = 0; p < PARTICIPANT_COUNT; p++)
                {
                    var localParticipant = rooms[p].LocalParticipant!;
                    var offset = p * TRACKS_PER_PARTICIPANT;
                    for (int t = 0; t < TRACKS_PER_PARTICIPANT; t++)
                    {
                        unpublishTasks.Add(
                            localParticipant.UnpublishTrackAsync(publications[offset + t].Sid)
                        );
                    }
                }

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Unpublishing {TRACK_COUNT} tracks simultaneously..."
                );
                await Task.WhenAll(unpublishTasks);
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] All {TRACK_COUNT} tracks unpublished"
                );

                // Wait for ALL LocalTrackUnpublished events
                var unpublishedDeadline = DateTime.Now.AddSeconds(30);
                while (unpublishedEvents.Count < TRACK_COUNT && DateTime.Now < unpublishedDeadline)
                {
                    await Task.Delay(100);
                }

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Received {unpublishedEvents.Count}/{TRACK_COUNT} LocalTrackUnpublished events"
                );
                Assert.Equal(TRACK_COUNT, unpublishedEvents.Count);

                // Verify all unpublication SIDs were captured
                var unpublishedSids = new HashSet<string>(unpublishedEvents);
                Assert.Equal(expectedSids.Count, unpublishedSids.Count);
                Assert.True(
                    expectedSids.SetEquals(unpublishedSids),
                    "Unpublished event SIDs should match publication SIDs"
                );

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] ✓ All LocalTrackUnpublished events verified"
                );

                // Verify no duplicate events
                Assert.Equal(publishedEvents.Count, publishedEvents.Distinct().Count());
                Assert.Equal(unpublishedEvents.Count, unpublishedEvents.Distinct().Count());
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ✓ No duplicate events detected");

                foreach (var room in rooms)
                {
                    await room.DisconnectAsync();
                }

                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] 🎉 STRESS TEST PASSED: {TRACK_COUNT} tracks published/unpublished simultaneously with 100% event delivery!"
                );
            }
            finally
            {
                // Cleanup
                foreach (var track in audioTracks)
                {
                    track.Dispose();
                }
                foreach (var source in audioSources)
                {
                    source.Dispose();
                }
                foreach (var room in rooms)
                {
                    room.Dispose();
                }
            }
        }

        [Fact]
        public async Task LocalTrackSubscribed_EventFires_WhenRemoteParticipantSubscribes()
        {
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting LocalTrackSubscribed test");

            using var room1 = new Room();
            using var room2 = new Room();

            var trackSubscribedTcs = new TaskCompletionSource<LocalTrackPublication>();

            room1.LocalTrackSubscribed += (sender, e) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackSubscribed event fired for track: {e.Publication.Sid}"
                );
                trackSubscribedTcs.TrySetResult(e.Publication);
            };

            var token1 = _fixture.CreateToken("participant1", "test-room");
            var token2 = _fixture.CreateToken("participant2", "test-room");

            await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
            await room2.ConnectAsync(_fixture.LiveKitUrl, token2);

            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);
            var publication = await room1.LocalParticipant!.PublishTrackAsync(audioTrack);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() =>
                trackSubscribedTcs.TrySetException(
                    new TimeoutException("LocalTrackSubscribed event did not fire")
                )
            );

            var subscribedPublication = await trackSubscribedTcs.Task;
            Assert.Equal(publication.Sid, subscribedPublication.Sid);

            await room1.DisconnectAsync();
            await room2.DisconnectAsync();

            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackSubscribed test completed successfully"
            );
        }
    }
}
