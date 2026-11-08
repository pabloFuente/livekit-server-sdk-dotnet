// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using LiveKit.Rtc.Internal;
using Xunit.Abstractions;
using static LiveKit.Rtc.Tests.Reliability.SyntheticRoomEvents;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// The orders in which the FFI delivers the remote track events of a participant that leaves
/// while its tracks are being subscribed, as recorded from the soak (participants joining, fully
/// reconnecting and leaving concurrently). The Rust SDK dispatches TrackSubscribed from the media
/// path and TrackUnsubscribed from the unpublication, with no lock spanning the state change and
/// the dispatch, so the unsubscription can come first, twice, or after the participant left. The
/// room must end in the state the native side ended in, raise balanced events, and release every
/// handle, without a warning for an order it knows. Each test replays the recorded sequence into a
/// connected room (<see cref="SyntheticRoomEvents"/>).
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class RemoteTrackEventOrderTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public RemoteTrackEventOrderTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    /// <summary>
    /// A connected room alone in its LiveKit room, recording the remote events it raises.
    /// </summary>
    private sealed class Replay : IDisposable
    {
        public Room Room { get; }
        public SyntheticRoomEvents Events { get; }
        public ConcurrentQueue<string> Raised { get; } = new();
        private readonly ConcurrentQueue<string> _sentinels = new();

        private Replay(Room room)
        {
            Room = room;
            Events = new SyntheticRoomEvents(room);
            room.ParticipantConnected += (_, p) =>
                Raised.Enqueue($"ParticipantConnected {p.Identity}");
            room.ParticipantDisconnected += (_, p) =>
                Raised.Enqueue($"ParticipantDisconnected {p.Identity}");
            room.TrackPublished += (_, e) => Raised.Enqueue($"TrackPublished {e.Publication.Sid}");
            room.TrackUnpublished += (_, e) =>
                Raised.Enqueue($"TrackUnpublished {e.Publication.Sid}");
            room.TrackSubscribed += (_, e) => Raised.Enqueue($"TrackSubscribed {e.Track.Sid}");
            room.TrackUnsubscribed += (_, e) => Raised.Enqueue($"TrackUnsubscribed {e.Track.Sid}");
            room.RoomMetadataChanged += (_, marker) => _sentinels.Enqueue(marker);
        }

        public static async Task<Replay> StartAsync(ReliabilityServerFixture server) =>
            new(
                await server.ConnectAsync(
                    "replay",
                    ReliabilityServerFixture.NewRoomName("event-order")
                )
            );

        /// <summary>
        /// Delivers the sequence, then waits until the room raised everything it delivered.
        /// </summary>
        public async Task<string[]> DeliverAsync(params LiveKit.Proto.RoomEvent[] sequence)
        {
            var marker = Guid.NewGuid().ToString("N");
            Events.Deliver(sequence);
            Events.Deliver(Sentinel(marker));
            await Eventually.TrueAsync(
                () => _sentinels.Contains(marker),
                Settle,
                "the room processed the replayed sequence"
            );
            var raised = Raised.ToArray();
            while (Raised.TryDequeue(out _)) { }
            return raised;
        }

        public void Dispose() => Room.Dispose();
    }

    [Fact]
    public async Task TheUsualOrder_RaisesEveryEventOnce()
    {
        var log = SdkLog.BeginScope();
        using var replay = await Replay.StartAsync(_server);
        var track = NewNativeHandle();

        var raised = await replay.DeliverAsync(
            ParticipantConnected("ghost", "PA_ghost", NewNativeHandle()),
            TrackPublished("ghost", "TR_ghost", NewNativeHandle()),
            TrackSubscribed("ghost", "TR_ghost", track),
            TrackUnsubscribed("ghost", "TR_ghost"),
            TrackUnpublished("ghost", "TR_ghost"),
            ParticipantDisconnected("ghost")
        );

        Assert.Equal(
            new[]
            {
                "ParticipantConnected ghost",
                "TrackPublished TR_ghost",
                "TrackSubscribed TR_ghost",
                "TrackUnsubscribed TR_ghost",
                "TrackUnpublished TR_ghost",
                "ParticipantDisconnected ghost",
            },
            raised
        );
        Assert.Empty(replay.Room.RemoteParticipants);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task AnUnsubscriptionBeforeItsSubscription_RaisesNeither_AndReleasesTheLateTrack()
    {
        // Recorded: TrackUnsubscribed, TrackSubscribed, TrackUnpublished, ParticipantDisconnected
        // in the same millisecond. The room used to warn "TrackUnsubscribed: ... is not
        // subscribed", then raise TrackSubscribed for a track that was already gone, never
        // followed by a TrackUnsubscribed.
        var log = SdkLog.BeginScope();
        using var replay = await Replay.StartAsync(_server);
        var lateTrack = NewNativeHandle();

        var raised = await replay.DeliverAsync(
            ParticipantConnected("ghost", "PA_ghost", NewNativeHandle()),
            TrackPublished("ghost", "TR_ghost", NewNativeHandle()),
            TrackUnsubscribed("ghost", "TR_ghost"),
            TrackSubscribed("ghost", "TR_ghost", lateTrack),
            TrackUnpublished("ghost", "TR_ghost"),
            ParticipantDisconnected("ghost")
        );

        Assert.Equal(
            new[]
            {
                "ParticipantConnected ghost",
                "TrackPublished TR_ghost",
                "TrackUnpublished TR_ghost",
                "ParticipantDisconnected ghost",
            },
            raised
        );
        Assert.True(WasReleased(lateTrack), "the late track's handle was released");
        Assert.Empty(replay.Room.RemoteParticipants);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task ASubscriptionAfterTheParticipantLeft_IsReleasedWithoutWarning()
    {
        // Recorded: TrackUnpublished, ParticipantDisconnected, then TrackSubscribed
        var log = SdkLog.BeginScope();
        using var replay = await Replay.StartAsync(_server);
        var lateTrack = NewNativeHandle();

        var raised = await replay.DeliverAsync(
            ParticipantConnected("ghost", "PA_ghost", NewNativeHandle()),
            TrackPublished("ghost", "TR_ghost", NewNativeHandle()),
            TrackUnpublished("ghost", "TR_ghost"),
            ParticipantDisconnected("ghost"),
            TrackSubscribed("ghost", "TR_ghost", lateTrack),
            TrackUnsubscribed("ghost", "TR_ghost")
        );

        Assert.Equal(
            new[]
            {
                "ParticipantConnected ghost",
                "TrackPublished TR_ghost",
                "TrackUnpublished TR_ghost",
                "ParticipantDisconnected ghost",
            },
            raised
        );
        Assert.True(WasReleased(lateTrack), "the late track's handle was released");
        log.AssertNoProblems();
    }

    [Fact]
    public async Task ADuplicatedUnsubscriptionThenALateSubscription_KeepTheRoomConsistentAcrossARejoin()
    {
        // Recorded during the room's own reconnection: the subscription, two unsubscriptions, a
        // late subscription, the unpublication and the departure, then the participant joins again
        // with the same track and is subscribed to it
        var log = SdkLog.BeginScope();
        using var replay = await Replay.StartAsync(_server);
        var firstTrack = NewNativeHandle();
        var lateTrack = NewNativeHandle();
        var secondTrack = NewNativeHandle();

        var raised = await replay.DeliverAsync(
            ParticipantConnected("ghost", "PA_ghost", NewNativeHandle()),
            TrackPublished("ghost", "TR_ghost", NewNativeHandle()),
            TrackSubscribed("ghost", "TR_ghost", firstTrack),
            TrackUnsubscribed("ghost", "TR_ghost"),
            TrackUnsubscribed("ghost", "TR_ghost"),
            TrackSubscribed("ghost", "TR_ghost", lateTrack),
            TrackUnpublished("ghost", "TR_ghost"),
            ParticipantDisconnected("ghost"),
            ParticipantConnected("ghost", "PA_ghost", NewNativeHandle()),
            TrackPublished("ghost", "TR_ghost", NewNativeHandle()),
            TrackSubscribed("ghost", "TR_ghost", secondTrack)
        );

        Assert.Equal(
            new[]
            {
                "ParticipantConnected ghost",
                "TrackPublished TR_ghost",
                "TrackSubscribed TR_ghost",
                "TrackUnsubscribed TR_ghost",
                "TrackUnpublished TR_ghost",
                "ParticipantDisconnected ghost",
                "ParticipantConnected ghost",
                "TrackPublished TR_ghost",
                "TrackSubscribed TR_ghost",
            },
            raised
        );
        Assert.True(WasReleased(lateTrack), "the late track's handle was released");
        var publication = Assert.IsType<RemoteTrackPublication>(
            replay.Room.RemoteParticipants["ghost"].GetTrackPublication("TR_ghost")
        );
        Assert.NotNull(publication.Track);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task EventsOfAParticipantTheRoomNeverSaw_AreStillReported()
    {
        // Nothing explains these: they keep their warning (and the track is released, as before)
        var log = SdkLog.BeginScope();
        using var replay = await Replay.StartAsync(_server);
        var strangerTrack = NewNativeHandle();

        var raised = await replay.DeliverAsync(
            TrackSubscribed("stranger", "TR_stranger", strangerTrack),
            TrackUnsubscribed("stranger", "TR_stranger")
        );

        Assert.Empty(raised);
        Assert.True(WasReleased(strangerTrack), "the stranger's track handle was released");
        Assert.Contains(
            log.Problems(),
            l => l.Contains("TrackSubscribed: Participant stranger not found")
        );
        Assert.Contains(
            log.Problems(),
            l => l.Contains("TrackUnsubscribed: Participant stranger not found")
        );
    }

    [Fact]
    public async Task ReplayedSequences_ReleaseEveryHandleOnce()
    {
        // The participant, publication and track wrappers of the replays above are released by
        // their finalizers once the room drops them; none twice
        var failed = FfiHandle.FailedReleaseCount;
        using (var replay = await Replay.StartAsync(_server))
        {
            for (var i = 0; i < 20; i++)
            {
                var identity = $"ghost-{i}";
                await replay.DeliverAsync(
                    ParticipantConnected(identity, $"PA_{i}", NewNativeHandle()),
                    TrackPublished(identity, $"TR_{i}", NewNativeHandle()),
                    TrackUnsubscribed(identity, $"TR_{i}"),
                    TrackSubscribed(identity, $"TR_{i}", NewNativeHandle()),
                    TrackUnpublished(identity, $"TR_{i}"),
                    ParticipantDisconnected(identity)
                );
            }
        }
        ResourceSnapshot.CollectGarbage();
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }
}
