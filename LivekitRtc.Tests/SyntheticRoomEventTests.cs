using System.Collections.Concurrent;
using LiveKit.Rtc.Tests.Reliability;
using Xunit.Abstractions;
using static LiveKit.Rtc.Tests.Reliability.SyntheticRoomEvents;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Room events a local server cannot produce on demand (a failed subscription; a move between
/// rooms, which is LiveKit Cloud-only), replayed into a connected room through the FFI callback
/// (<see cref="SyntheticRoomEvents"/>) to check how the room turns them into public events.
/// </summary>
[Collection("RtcTests")]
public class SyntheticRoomEventTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly Room _room = new();
    private readonly ConcurrentQueue<string> _sentinels = new();

    public SyntheticRoomEventTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _room.RoomMetadataChanged += (_, marker) => _sentinels.Enqueue(marker);
    }

    public async Task InitializeAsync()
    {
        // The FFI sends the room's SID in a RoomSidChanged event shortly after the connection: a
        // replayed event that lands before it would have its SID overwritten by the real one
        var sid = Eventually.Signal<string>();
        _room.RoomSidChanged += (_, value) => sid.TrySetResult(value);
        await _room.ConnectAsync(
            _fixture.LiveKitUrl,
            _fixture.CreateToken("replay", $"synthetic-events-{Guid.NewGuid():N}")
        );
        await sid.Task.Within(Timeout, "the RoomSidChanged event that follows the connection");
        _output.WriteLine($"Connected to {_room.Name} ({_room.Sid})");
    }

    public async Task DisposeAsync()
    {
        await _room.DisconnectAsync();
        _room.Dispose();
    }

    // Delivers the events, then waits until the room raised everything delivered before
    private async Task DeliverAsync(params Proto.RoomEvent[] events)
    {
        var marker = Guid.NewGuid().ToString("N");
        var replay = new SyntheticRoomEvents(_room);
        replay.Deliver(events);
        replay.Deliver(Sentinel(marker));
        var deadline = DateTime.UtcNow + Timeout;
        while (!_sentinels.Contains(marker) && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.Contains(marker, _sentinels);
    }

    private static Proto.RoomEvent SubscriptionFailed(string identity, string trackSid) =>
        new()
        {
            TrackSubscriptionFailed = new Proto.TrackSubscriptionFailed
            {
                ParticipantIdentity = identity,
                TrackSid = trackSid,
                Error = "subscription failed",
            },
        };

    [Fact]
    public async Task TrackSubscriptionFailed_IsRaisedWithTheRemoteParticipant()
    {
        var connected = new ConcurrentQueue<Participant>();
        var failed = new ConcurrentQueue<TrackSubscriptionFailedEventArgs>();
        _room.ParticipantConnected += (_, participant) => connected.Enqueue(participant);
        _room.TrackSubscriptionFailed += (_, e) => failed.Enqueue(e);

        await DeliverAsync(
            ParticipantConnected("remote", "PA_remote", NewNativeHandle()),
            SubscriptionFailed("remote", "TR_remote"),
            // A participant the room does not know: dropped
            SubscriptionFailed("unknown", "TR_unknown")
        );

        var participant = Assert.Single(connected);
        var failure = Assert.Single(failed);
        Assert.Equal("TR_remote", failure.TrackSid);
        Assert.Equal("subscription failed", failure.Error);
        Assert.Same(participant, failure.Participant);
    }

    [Fact]
    public async Task Moved_UpdatesTheRoomInfoBeforeItIsRaised()
    {
        // The room applies an event's state when it arrives and raises it later, from the event
        // chain: by then the sentinel that ends the replay has replaced the metadata, but no later
        // event changes the name
        string? nameWhenRaised = null;
        var moved = new ConcurrentQueue<Proto.RoomInfo>();
        _room.Moved += (_, info) =>
        {
            nameWhenRaised = _room.Name;
            moved.Enqueue(info);
        };
        const long creationTimeSeconds = 1_700_000_000;

        await DeliverAsync(
            new Proto.RoomEvent
            {
                Moved = new Proto.RoomInfo
                {
                    Sid = "RM_destination",
                    Name = "destination-room",
                    Metadata = "moved here",
                    LossyDcBufferedAmountLowThreshold = 0,
                    ReliableDcBufferedAmountLowThreshold = 0,
                    EmptyTimeout = 300,
                    DepartureTimeout = 20,
                    MaxParticipants = 0,
                    CreationTime = creationTimeSeconds,
                    NumParticipants = 2,
                    NumPublishers = 1,
                    ActiveRecording = false,
                },
            }
        );

        var info = Assert.Single(moved);
        Assert.Equal("RM_destination", info.Sid);
        Assert.Equal("moved here", info.Metadata);
        Assert.Equal("destination-room", nameWhenRaised);
        Assert.Equal("RM_destination", _room.Sid);
        Assert.Equal(2u, _room.NumParticipants);
        Assert.Equal(1u, _room.NumPublishers);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(creationTimeSeconds).DateTime,
            _room.CreationTime
        );
    }
}
