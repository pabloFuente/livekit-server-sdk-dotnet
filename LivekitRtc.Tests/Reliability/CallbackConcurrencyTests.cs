// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using System.Text;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// How the .NET layer turns native callbacks into <see cref="Room"/> events: the FFI callback
/// thread hands each room event to the room's FIFO chain, which runs the user handlers. These
/// tests check the guarantees of that dispatch with real traffic: order and exactly-once delivery,
/// isolation from throwing, slow and reentrant handlers and between rooms, the effect of
/// subscribing and unsubscribing during delivery, nothing raised after <see cref="Room.Disconnected"/>,
/// and RPC invocations served outside the chain.
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class CallbackConcurrencyTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public CallbackConcurrencyTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    [Fact]
    public async Task ReliableData_IsRaisedInOrder_ExactlyOnce_AtHighRate()
    {
        var log = SdkLog.BeginScope();
        var count = StressLevel.Scale(500, 5000);
        var received = new ConcurrentQueue<int>();
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("order"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity == "receiver")
                    room.DataReceived += (_, e) => received.Enqueue(RoomGroup.SequenceNumber(e));
            }
        );

        await group.SendSequenceAsync("sender", "receiver", count);

        await Eventually.TrueAsync(() => received.Count >= count, Settle, $"{count} packets");
        Assert.Equal(Enumerable.Range(0, count), received);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task AThrowingHandler_HidesTheEventFromNoOtherHandler_AndStopsNoLaterEvent()
    {
        // The chain invoked each event as a whole: the first subscriber that threw stopped the
        // invocation, so the subscribers after it never got the event
        var log = SdkLog.BeginScope();
        const int Count = 50;
        var afterTheThrowingOne = new ConcurrentQueue<int>();
        var joinedSeen = Eventually.Signal<string>();
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("throwing"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity != "receiver")
                    return;
                room.DataReceived += (_, _) => throw new InvalidOperationException("handler bug");
                room.DataReceived += (_, e) =>
                    afterTheThrowingOne.Enqueue(RoomGroup.SequenceNumber(e));
                room.ParticipantConnected += (_, _) =>
                    throw new InvalidOperationException("handler bug");
                room.ParticipantConnected += (_, p) =>
                {
                    if (p.Identity == "late")
                        joinedSeen.TrySetResult(p.Identity);
                };
            }
        );

        await group.SendSequenceAsync("sender", "receiver", Count);
        using var late = await _server.ConnectAsync("late", group.Name);

        await Eventually.TrueAsync(
            () => afterTheThrowingOne.Count >= Count,
            Settle,
            "the second DataReceived handler gets every packet"
        );
        Assert.Equal(Enumerable.Range(0, Count), afterTheThrowingOne);
        Assert.Equal("late", await joinedSeen.Task.Within(Settle, "ParticipantConnected"));

        // Each failure is logged once, with the exception (the packets, then "sender" and "late"
        // joining), and nothing else went wrong
        var errors = log.Problems().Where(l => l.Contains("handler bug")).ToList();
        Assert.Equal(Count + 2, errors.Count);
        log.AssertNoProblems("handler bug", "   at ");
    }

    [Fact]
    public async Task ABlockedHandler_StallsOnlyItsOwnRoom_AndLosesNothing()
    {
        // Each room has its own chain: a handler that blocks delays the later events of its room
        // (documented), but not those of another room in the process, and drops nothing
        var log = SdkLog.BeginScope();
        var count = StressLevel.Scale(100, 1000);
        var gate = new ManualResetEventSlim(false);
        var blocked = new ConcurrentQueue<int>();
        var free = new ConcurrentQueue<int>();
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("blocked"),
            new[] { "blocked", "free", "sender" },
            (identity, room) =>
            {
                if (identity == "blocked")
                    room.DataReceived += (_, e) =>
                    {
                        blocked.Enqueue(RoomGroup.SequenceNumber(e));
                        gate.Wait(Settle);
                    };
                else if (identity == "free")
                    room.DataReceived += (_, e) => free.Enqueue(RoomGroup.SequenceNumber(e));
            }
        );

        await Task.WhenAll(
            group.SendSequenceAsync("sender", "blocked", count),
            group.SendSequenceAsync("sender", "free", count)
        );
        await Eventually.TrueAsync(
            () => free.Count >= count,
            Settle,
            "the other room gets its packets"
        );
        Assert.Equal(new[] { 0 }, blocked); // still inside the first handler

        gate.Set();
        await Eventually.TrueAsync(
            () => blocked.Count >= count,
            Settle,
            "the blocked room catches up"
        );
        Assert.Equal(Enumerable.Range(0, count), blocked);
        Assert.Equal(Enumerable.Range(0, count), free);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task SubscribingAndUnsubscribingDuringDelivery_TakesEffectFromTheNextEvent()
    {
        var log = SdkLog.BeginScope();
        var once = new ConcurrentQueue<int>();
        var addedLater = new ConcurrentQueue<int>();
        var permanent = new ConcurrentQueue<int>();
        EventHandler<DataReceivedEventArgs>? selfRemoving = null;
        EventHandler<DataReceivedEventArgs> lateHandler = (_, e) =>
            addedLater.Enqueue(RoomGroup.SequenceNumber(e));
        var added = 0;
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("resubscribe"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity != "receiver")
                    return;
                selfRemoving = (_, e) =>
                {
                    once.Enqueue(RoomGroup.SequenceNumber(e));
                    room.DataReceived -= selfRemoving;
                };
                room.DataReceived += selfRemoving;
                room.DataReceived += (_, e) =>
                {
                    if (Interlocked.Exchange(ref added, 1) == 0)
                        room.DataReceived += lateHandler;
                };
                room.DataReceived += (_, e) => permanent.Enqueue(RoomGroup.SequenceNumber(e));
            }
        );

        // Meanwhile another thread keeps adding and removing handlers of its own
        using var stop = new CancellationTokenSource();
        var churned = 0L;
        var churn = Task.Run(() =>
        {
            var receiver = group["receiver"];
            while (!stop.IsCancellationRequested)
            {
                EventHandler<DataReceivedEventArgs> transient = (_, _) =>
                    Interlocked.Increment(ref churned);
                receiver.DataReceived += transient;
                Thread.Yield();
                receiver.DataReceived -= transient;
            }
        });

        var count = StressLevel.Scale(200, 2000);
        await group.SendSequenceAsync("sender", "receiver", count);
        await Eventually.TrueAsync(() => permanent.Count >= count, Settle, "every packet");
        stop.Cancel();
        await churn;

        Assert.Equal(new[] { 0 }, once);
        Assert.Equal(Enumerable.Range(1, count - 1), addedLater);
        Assert.Equal(Enumerable.Range(0, count), permanent);
        _output.WriteLine($"transient handlers ran {churned} times");
        log.AssertNoProblems();
    }

    [Fact]
    public async Task AHandlerMayBlockOnTheRoomsOwnOperations_WithoutDeadlock()
    {
        // A synchronous handler that waits for an FFI round trip (publishing data, publishing a
        // track) must not deadlock: the SDK resolves those waits on the FFI callback thread, not
        // on the chain the handler occupies
        var log = SdkLog.BeginScope();
        var replies = new ConcurrentQueue<string>();
        var localPublished = Eventually.Signal<string>();
        using var source = new AudioSource(48000, 1);
        using var track = LocalAudioTrack.Create("from-handler", source);
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("reentrant"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity == "sender")
                {
                    room.DataReceived += (_, e) => replies.Enqueue(Encoding.UTF8.GetString(e.Data));
                    return;
                }
                room.LocalTrackPublished += (_, e) =>
                    localPublished.TrySetResult(e.Publication.Sid);
                room.DataReceived += (_, e) =>
                {
                    var n = RoomGroup.SequenceNumber(e);
                    room.LocalParticipant!.PublishDataAsync(
                            Encoding.UTF8.GetBytes($"reply-{n}"),
                            new DataPublishOptions { DestinationIdentities = new[] { "sender" } }
                        )
                        .GetAwaiter()
                        .GetResult();
                    if (n == 0)
                        room.LocalParticipant.PublishTrackAsync(track).GetAwaiter().GetResult();
                };
            }
        );

        await group.SendSequenceAsync("sender", "receiver", 5);

        await Eventually.TrueAsync(() => replies.Count >= 5, Settle, "every reply");
        Assert.Equal(Enumerable.Range(0, 5).Select(n => $"reply-{n}"), replies);
        Assert.Equal(
            track.Sid,
            await localPublished.Task.Within(Settle, "LocalTrackPublished after the handler")
        );
        log.AssertNoProblems();
    }

    [Theory]
    [InlineData("DisconnectAsync from another thread")]
    [InlineData("Dispose inside a DataReceived handler")]
    public async Task NothingIsRaised_AfterDisconnected_WhileARemoteFloodsData(string how)
    {
        // The disposal boundary: once Disconnected was raised, the room raises nothing else, even
        // with packets still arriving. Disposing the room from one of its own handlers completes.
        var log = SdkLog.BeginScope();
        var sequence = 0L;
        var events = new ConcurrentQueue<(long Order, string Name)>();
        void Record(string name) => events.Enqueue((Interlocked.Increment(ref sequence), name));
        var disposedFromHandler = Eventually.Signal<bool>();

        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("boundary"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity != "receiver")
                    return;
                room.DataReceived += (_, e) =>
                {
                    Record("DataReceived");
                    if (how.StartsWith("Dispose") && RoomGroup.SequenceNumber(e) == 100)
                    {
                        room.Dispose();
                        disposedFromHandler.TrySetResult(true);
                    }
                };
                room.Disconnected += (_, _) => Record("Disconnected");
                room.ConnectionStateChanged += (_, s) => Record($"ConnectionStateChanged {s}");
                room.ParticipantDisconnected += (_, _) => Record("ParticipantDisconnected");
                room.TrackUnsubscribed += (_, _) => Record("TrackUnsubscribed");
            }
        );

        using var stopFlood = new CancellationTokenSource();
        var flood = group.SendSequenceAsync(
            "sender",
            "receiver",
            int.MaxValue,
            stop: stopFlood.Token
        );
        await Eventually.TrueAsync(
            () => events.Count(e => e.Name == "DataReceived") > 100,
            Settle,
            "the flood reaches the receiver"
        );
        if (how.StartsWith("Dispose"))
            await disposedFromHandler.Task.Within(Settle, "Dispose called from a handler returns");
        else
            await group["receiver"].DisconnectAsync().Within(Settle, "DisconnectAsync");

        // Keep flooding for a while after the disconnection
        await Task.Delay(TimeSpan.FromSeconds(1));
        stopFlood.Cancel();
        await flood;

        var recorded = events.OrderBy(e => e.Order).Select(e => e.Name).ToList();
        var disconnectedAt = recorded.IndexOf("Disconnected");
        Assert.True(disconnectedAt >= 0, "Disconnected was raised");
        Assert.Single(recorded, "Disconnected");
        Assert.Empty(recorded.Skip(disconnectedAt + 1));
        Assert.Equal("ConnectionStateChanged ConnDisconnected", recorded[disconnectedAt - 1]);
        _output.WriteLine(
            $"{recorded.Count(e => e == "DataReceived")} packets before Disconnected"
        );
        log.AssertNoProblems();
    }

    [Fact]
    public async Task RpcInvocations_AreServedWhileTheEventChainIsBlocked_AndConcurrently()
    {
        // RPC invocations bypass the chain on purpose: a blocked room handler must not delay them,
        // and concurrent calls from several callers each get their own answer
        var log = SdkLog.BeginScope();
        var gate = new ManualResetEventSlim(false);
        var inHandler = Eventually.Signal<bool>();
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("rpc"),
            new[] { "server", "caller-a", "caller-b" },
            (identity, room) =>
            {
                if (identity != "server")
                    return;
                room.DataReceived += (_, _) =>
                {
                    inHandler.TrySetResult(true);
                    gate.Wait(Settle);
                };
            }
        );
        var server = group["server"];
        server.LocalParticipant!.RegisterRpcMethod(
            "echo",
            async data =>
            {
                await Task.Yield();
                return $"{data.CallerIdentity}:{data.Payload}";
            }
        );

        await group.SendSequenceAsync("caller-a", "server", 1);
        await inHandler.Task.Within(Settle, "the server's chain is blocked");

        var callsPerCaller = StressLevel.Scale(15, 150);
        var calls = new[] { "caller-a", "caller-b" }
            .SelectMany(caller =>
                Enumerable
                    .Range(0, callsPerCaller)
                    .Select(async i =>
                        (
                            Expected: $"{caller}:{i}",
                            Actual: await group[caller]
                                .LocalParticipant!.PerformRpcAsync("server", "echo", $"{i}")
                        )
                    )
            )
            .ToArray();
        var results = await Task.WhenAll(calls).Within(Settle, "RPCs while the chain is blocked");
        Assert.All(results, r => Assert.Equal(r.Expected, r.Actual));

        gate.Set();
        log.AssertNoProblems();
    }
}
