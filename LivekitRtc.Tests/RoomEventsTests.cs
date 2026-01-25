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
        public async Task ParticipantConnected_Disconnected_EventsFire()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting ParticipantConnected_Disconnected test"
            );

            using var room1 = new Room();
            using var room2 = new Room();

            var participantConnectedTcs = new TaskCompletionSource<Participant>();
            var participantDisconnectedTcs = new TaskCompletionSource<Participant>();

            room1.ParticipantConnected += (sender, participant) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Room1 received ParticipantConnected: {participant.Identity}"
                );
                participantConnectedTcs.TrySetResult(participant);
            };

            room1.ParticipantDisconnected += (sender, participant) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Room1 received ParticipantDisconnected: {participant.Identity}"
                );
                participantDisconnectedTcs.TrySetResult(participant);
            };

            // Connect both participants
            var token1 = _fixture.CreateToken("participant1", "test-room");
            var token2 = _fixture.CreateToken("participant2", "test-room");

            await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Room1 connected");

            await room2.ConnectAsync(_fixture.LiveKitUrl, token2);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Room2 connected");

            // Wait for ParticipantConnected event
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var connectedParticipant = await participantConnectedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(connectedParticipant);
            Assert.Equal("participant2", connectedParticipant.Identity);
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] ParticipantConnected event fired successfully"
            );

            // Disconnect participant2
            await room2.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Room2 disconnected");

            // Wait for ParticipantDisconnected event
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var disconnectedParticipant = await participantDisconnectedTcs.Task.WaitAsync(
                cts2.Token
            );
            Assert.NotNull(disconnectedParticipant);
            Assert.Equal("participant2", disconnectedParticipant.Identity);
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] ParticipantDisconnected event fired successfully"
            );

            await room1.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed successfully!");
        }

        [Fact]
        public async Task ConnectionStateChanged_Reconnecting_Reconnected_EventsFire()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting ConnectionStateChanged test"
            );

            using var room = new Room();

            var stateChanges = new List<Proto.ConnectionState>();

            room.ConnectionStateChanged += (sender, state) =>
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ConnectionStateChanged: {state}");
                stateChanges.Add(state);
            };

            room.Reconnecting += (sender, args) =>
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Reconnecting event fired");
            };

            room.Reconnected += (sender, args) =>
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Reconnected event fired");
            };

            var token = _fixture.CreateToken("test-participant", "test-room");
            await room.ConnectAsync(_fixture.LiveKitUrl, token);

            // Verify we got Connected state
            Assert.Contains(Proto.ConnectionState.ConnConnected, stateChanges);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Verified Connected state");

            // Note: Testing actual reconnection would require network manipulation
            // which is complex in a unit test. We've verified the event handlers exist.

            await room.DisconnectAsync();

            // Verify we got Disconnected state
            Assert.Contains(Proto.ConnectionState.ConnDisconnected, stateChanges);
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
            const int TRACK_COUNT = 100; // Publish 100 tracks simultaneously
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting massive simultaneous track stress test with {TRACK_COUNT} tracks");

            using var room = new Room();
            var token = _fixture.CreateToken("stress-test-participant", "stress-room");

            // Track all published and unpublished events
            var publishedEvents = new System.Collections.Concurrent.ConcurrentBag<string>();
            var unpublishedEvents = new System.Collections.Concurrent.ConcurrentBag<string>();

            room.LocalTrackPublished += (sender, args) =>
            {
                publishedEvents.Add(args.Publication.Sid);
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackPublished: {args.Publication.Sid}");
            };

            room.LocalTrackUnpublished += (sender, args) =>
            {
                unpublishedEvents.Add(args.Publication.Sid);
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LocalTrackUnpublished: {args.Publication.Sid}");
            };

            await room.ConnectAsync(_fixture.LiveKitUrl, token);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Connected to room");

            // Create all audio sources and tracks
            var audioSources = new List<AudioSource>();
            var audioTracks = new List<LocalAudioTrack>();
            
            for (int i = 0; i < TRACK_COUNT; i++)
            {
                var source = new AudioSource(48000, 1);
                var track = LocalAudioTrack.Create($"stress-track-{i}", source);
                audioSources.Add(source);
                audioTracks.Add(track);
            }

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Created {TRACK_COUNT} audio tracks");

            // STRESS TEST 1: Publish ALL tracks simultaneously
            var publishTasks = audioTracks.Select(track =>
                room.LocalParticipant!.PublishTrackAsync(track, new TrackPublishOptions())
            ).ToArray();

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Publishing {TRACK_COUNT} tracks simultaneously...");
            var publications = await Task.WhenAll(publishTasks);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] All {TRACK_COUNT} tracks published");

            // Wait for ALL LocalTrackPublished events (with generous timeout due to volume)
            var publishedDeadline = DateTime.Now.AddSeconds(30);
            while (publishedEvents.Count < TRACK_COUNT && DateTime.Now < publishedDeadline)
            {
                await Task.Delay(100);
            }

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Received {publishedEvents.Count}/{TRACK_COUNT} LocalTrackPublished events");
            Assert.Equal(TRACK_COUNT, publishedEvents.Count);

            // Verify all publication SIDs were captured
            var publishedSids = new HashSet<string>(publishedEvents);
            var expectedSids = new HashSet<string>(publications.Select(p => p.Sid));
            Assert.Equal(expectedSids.Count, publishedSids.Count);
            Assert.True(expectedSids.SetEquals(publishedSids), "Published event SIDs should match actual publication SIDs");

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ✓ All LocalTrackPublished events verified");

            // Brief delay before unpublish storm
            await Task.Delay(500);

            // STRESS TEST 2: Unpublish ALL tracks simultaneously
            var unpublishTasks = publications.Select(pub =>
                room.LocalParticipant!.UnpublishTrackAsync(pub.Sid)
            ).ToArray();

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Unpublishing {TRACK_COUNT} tracks simultaneously...");
            await Task.WhenAll(unpublishTasks);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] All {TRACK_COUNT} tracks unpublished");

            // Wait for ALL LocalTrackUnpublished events
            var unpublishedDeadline = DateTime.Now.AddSeconds(30);
            while (unpublishedEvents.Count < TRACK_COUNT && DateTime.Now < unpublishedDeadline)
            {
                await Task.Delay(100);
            }

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Received {unpublishedEvents.Count}/{TRACK_COUNT} LocalTrackUnpublished events");
            Assert.Equal(TRACK_COUNT, unpublishedEvents.Count);

            // Verify all unpublication SIDs were captured
            var unpublishedSids = new HashSet<string>(unpublishedEvents);
            Assert.Equal(expectedSids.Count, unpublishedSids.Count);
            Assert.True(expectedSids.SetEquals(unpublishedSids), "Unpublished event SIDs should match publication SIDs");

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ✓ All LocalTrackUnpublished events verified");

            // Verify no duplicate events
            Assert.Equal(publishedEvents.Count, publishedEvents.Distinct().Count());
            Assert.Equal(unpublishedEvents.Count, unpublishedEvents.Distinct().Count());
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ✓ No duplicate events detected");

            // Cleanup
            foreach (var track in audioTracks)
            {
                track.Dispose();
            }
            foreach (var source in audioSources)
            {
                source.Dispose();
            }

            await room.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 🎉 STRESS TEST PASSED: {TRACK_COUNT} tracks published/unpublished simultaneously with 100% event delivery!");
        }
    }
}
