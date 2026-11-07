// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using LiveKit.Rtc.Internal;
using Livekit.Server.Sdk.Dotnet;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// The connection lifecycle of <see cref="Room"/> as the .NET layer manages it: connect,
/// disconnect, dispose, cancellation and failures, in order, repeated, concurrently and from a
/// UI-like thread. Each test checks the room's public state and events, that the server no longer
/// lists a participant the SDK gave up on, and that the SDK released the native handles it wrapped.
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class RoomLifecycleTests
{
    private static readonly TimeSpan ServerSettle = TimeSpan.FromSeconds(15);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public RoomLifecycleTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    [Fact]
    public async Task DisconnectAndDispose_AreHarmless_BeforeConnecting_AndWhenRepeated()
    {
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("repeat");

        var neverConnected = new Room();
        await neverConnected.DisconnectAsync();
        neverConnected.Dispose();
        neverConnected.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            neverConnected.ConnectAsync(_server.Url, _server.Token("never", roomName))
        );

        var room = await _server.ConnectAsync("repeat", roomName);
        var disconnected = 0;
        room.Disconnected += (_, _) => Interlocked.Increment(ref disconnected);
        await room.DisconnectAsync();
        await room.DisconnectAsync();
        room.Disconnect();
        room.Dispose();
        room.Dispose();

        await Eventually.TrueAsync(() => disconnected > 0, ServerSettle, "Disconnected is raised");
        Assert.Equal(1, Volatile.Read(ref disconnected));
        Assert.False(room.IsConnected);
        await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        log.AssertNoProblems();
    }

    [Theory]
    [InlineData("DisconnectAsync")]
    [InlineData("Dispose")]
    public async Task EndingAConnectedRoom_RaisesDisconnectedOnceWithItsReason_LeavesTheServer_AndReleasesTheHandles(
        string how
    )
    {
        // Disconnected and ConnectionStateChanged(ConnDisconnected) used to be raised twice (by the
        // FFI's event and by DisconnectAsync), always with UnknownReason and after LocalParticipant
        // was cleared, and Dispose skipped the teardown events. Both ways end the room the same.
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("end");
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;
        var failed = FfiHandle.FailedReleaseCount;

        await EndAConnectedRoomAsync(roomName, how);

        await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        await Eventually.TrueAsync(
            () =>
            {
                ResourceSnapshot.CollectGarbage();
                return FfiHandle.LiveCount <= live;
            },
            ServerSettle,
            "every handle of the ended room is released"
        );
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
        log.AssertNoProblems();
    }

    private async Task EndAConnectedRoomAsync(string roomName, string how)
    {
        var room = new Room();
        var reasons = new ConcurrentQueue<Proto.DisconnectReason>();
        var states = new ConcurrentQueue<Proto.ConnectionState>();
        var participantSeenByHandler = Eventually.Signal<bool>();
        room.Disconnected += (_, reason) =>
        {
            reasons.Enqueue(reason);
            participantSeenByHandler.TrySetResult(room.LocalParticipant?.Identity == "ended");
        };
        room.ConnectionStateChanged += (_, state) => states.Enqueue(state);
        await room.ConnectAsync(_server.Url, _server.Token("ended", roomName));
        using var source = new AudioSource(48000, 1);
        using var track = LocalAudioTrack.Create("ended-audio", source);
        await room.LocalParticipant!.PublishTrackAsync(track);

        if (how == "Dispose")
            room.Dispose();
        else
            await room.DisconnectAsync();

        Assert.True(
            await participantSeenByHandler.Task.Within(ServerSettle, $"Disconnected after {how}"),
            "a Disconnected handler can still read LocalParticipant"
        );
        await Eventually.TrueAsync(
            () => room.LocalParticipant == null,
            ServerSettle,
            "the room clears its participants after the handlers ran"
        );
        Assert.Equal(new[] { Proto.DisconnectReason.ClientInitiated }, reasons);
        // No spurious initial Disconnected, one Connected, one Disconnected
        Assert.Equal(
            new[] { Proto.ConnectionState.ConnConnected, Proto.ConnectionState.ConnDisconnected },
            states
        );
        Assert.False(room.IsConnected);
        Assert.Empty(room.RemoteParticipants);
        room.Dispose();
    }

    [Fact]
    public async Task EndedRooms_AreNotRetainedByTheSdk()
    {
        // A room listens to the process-wide FFI client from ConnectAsync to its disconnection.
        // Once it ended (disposed, disconnected, removed by the server or abandoned while
        // connecting) nothing in the SDK may keep it, or its participants, tracks and handlers,
        // alive. Such a leak holds no native handle, so only reachability shows it.
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("retained");
        var rooms = await EndRoomsInEveryWayAsync(roomName);

        await Eventually.TrueAsync(
            () =>
            {
                ResourceSnapshot.CollectGarbage();
                return rooms.All(r => !r.Room.IsAlive);
            },
            ServerSettle,
            "every ended room is collected, still alive: "
                + string.Join(", ", rooms.Where(r => r.Room.IsAlive).Select(r => r.How))
        );
        log.AssertNoProblems();
    }

    // Not inlined: no reference to the rooms may survive in the caller's frame
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private async Task<List<(string How, WeakReference Room)>> EndRoomsInEveryWayAsync(
        string roomName
    )
    {
        var ended = new List<(string, WeakReference)>();

        var disposed = await _server.ConnectAsync("disposed", roomName);
        disposed.Disconnected += (_, _) => { };
        disposed.Dispose();
        ended.Add(("disposed", new WeakReference(disposed)));

        var disconnected = await _server.ConnectAsync("disconnected", roomName);
        await disconnected.DisconnectAsync(); // and never disposed
        ended.Add(("disconnected, not disposed", new WeakReference(disconnected)));

        var removed = await _server.ConnectAsync("removed", roomName);
        var gone = Eventually.Signal<bool>();
        removed.Disconnected += (_, _) => gone.TrySetResult(true);
        await _server.RoomService.RemoveParticipant(
            new RoomParticipantIdentity { Room = roomName, Identity = "removed" }
        );
        await gone.Task.Within(ServerSettle, "the removed room disconnects");
        ended.Add(("removed by the server, not disposed", new WeakReference(removed)));

        var abandoned = new Room();
        using (var cts = new CancellationTokenSource())
        {
            var connect = abandoned.ConnectAsync(
                _server.Url,
                _server.Token("abandoned", roomName),
                null,
                cts.Token
            );
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        }
        ended.Add(("cancelled while connecting", new WeakReference(abandoned)));

        // Let the deferred cleanups of the event chains run
        await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        return ended;
    }

    [Fact]
    public async Task ConcurrentDisconnectsAndDisposes_AllComplete_AndDisconnectedIsRaisedOnce()
    {
        // DisconnectAsync read the room handle without synchronization: a second caller could
        // find it cleared by the first (NullReferenceException) or send a disconnect for a
        // released handle (InvalidOperationException). Disconnecting is idempotent: concurrent
        // callers share one disconnection.
        var log = SdkLog.BeginScope();
        var rounds = StressLevel.Scale(5, 30);
        for (var round = 0; round < rounds; round++)
        {
            var roomName = ReliabilityServerFixture.NewRoomName("concurrent-disconnect");
            var room = await _server.ConnectAsync("concurrent-disconnect", roomName);
            var disconnected = 0;
            room.Disconnected += (_, _) => Interlocked.Increment(ref disconnected);

            using var start = new Barrier(6);
            var callers = new List<Task>();
            for (var i = 0; i < 6; i++)
            {
                var caller = i;
                callers.Add(
                    Task.Run(async () =>
                    {
                        start.SignalAndWait();
                        if (caller % 3 == 0)
                            room.Dispose();
                        else if (caller % 3 == 1)
                            room.Disconnect();
                        else
                            await room.DisconnectAsync();
                    })
                );
            }
            await Task.WhenAll(callers).Within(ServerSettle, "concurrent disconnects");

            await Eventually.TrueAsync(
                () => Volatile.Read(ref disconnected) > 0,
                ServerSettle,
                "Disconnected is raised"
            );
            Assert.Equal(1, Volatile.Read(ref disconnected));
            Assert.False(room.IsConnected);
            await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        }
        log.AssertNoProblems();
    }

    [Fact]
    public async Task ConcurrentConnects_OnOneRoom_ConnectOnce_AndRejectTheOtherCalls()
    {
        // Both calls used to pass the "already connected" check, both joined, and the room kept
        // only the last connection: the first native room stayed in the server, unreachable.
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("concurrent-connect");
        using var room = new Room();
        var token = _server.Token("concurrent-connect", roomName);

        using var start = new Barrier(4);
        var attempts = Enumerable
            .Range(0, 4)
            .Select(_ =>
                Task.Run(async () =>
                {
                    start.SignalAndWait();
                    await room.ConnectAsync(_server.Url, token);
                })
            )
            .ToArray();
        await Task.WhenAll(attempts.Select(a => a.ContinueWith(_ => { })))
            .Within(TimeSpan.FromSeconds(40), "the concurrent connects");

        var succeeded = attempts.Count(a => a.IsCompletedSuccessfully);
        var rejected = attempts
            .Where(a => a.IsFaulted)
            .Select(a => a.Exception!.GetBaseException())
            .ToList();
        _output.WriteLine(
            $"{succeeded} succeeded, rejected: {string.Join(", ", rejected.Select(e => e.Message))}"
        );
        Assert.Equal(1, succeeded);
        Assert.All(rejected, e => Assert.IsType<InvalidOperationException>(e));
        Assert.True(room.IsConnected);
        Assert.Equal(new[] { "concurrent-connect" }, await _server.IdentitiesAsync(roomName));

        await room.DisconnectAsync();
        await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task Dispose_WhileConnecting_ReleasesTheLateConnection()
    {
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("dispose-connecting");
        var room = new Room();

        var connect = room.ConnectAsync(_server.Url, _server.Token("dispose-connecting", roomName));
        room.Dispose(); // the connect request is already in flight

        var error = await Record.ExceptionAsync(() =>
            connect.Within(TimeSpan.FromSeconds(40), "a connect disposed in flight")
        );
        Assert.IsType<ObjectDisposedException>(error);
        Assert.False(room.IsConnected);
        Assert.Null(room.LocalParticipant);

        // The FFI completed the connection; the SDK must leave it in the background
        await Task.Delay(TimeSpan.FromSeconds(2));
        await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task CancellingConnectAsync_AtAnyPoint_LeavesNobodyInTheRoom_AndTheRoomReusable()
    {
        // Wherever the cancellation lands (before the FFI connected, while it waits for the
        // callback, right after it), the outcome is either a connected room or a cancelled call
        // whose late connection the SDK releases. Before 0.1.6 the late connection stayed in the
        // room, and the FFI terminated the process 15 s later waiting for ReadyForRoomEvent.
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("cancel-connect");
        using var room = new Room();
        var outcomes = new List<string>();

        foreach (var delayMs in new[] { 0, 1, 5, 15, 40, 100, 250, 600 })
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(delayMs));
            try
            {
                await room.ConnectAsync(
                    _server.Url,
                    _server.Token("cancel-connect", roomName),
                    null,
                    cts.Token
                );
                outcomes.Add($"{delayMs} ms: connected");
                Assert.True(room.IsConnected);
                await room.DisconnectAsync();
            }
            catch (OperationCanceledException)
            {
                outcomes.Add($"{delayMs} ms: cancelled");
                Assert.False(room.IsConnected);
                Assert.Null(room.LocalParticipant);
            }

            await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        }
        _output.WriteLine(string.Join(Environment.NewLine, outcomes));

        await room.ConnectAsync(_server.Url, _server.Token("cancel-connect", roomName));
        Assert.Equal(new[] { "cancel-connect" }, await _server.IdentitiesAsync(roomName));
        await room.DisconnectAsync();
        log.AssertNoProblems("[WARN] Could not release an abandoned room connection");
    }

    [Fact]
    public async Task FailedConnects_ThrowRoomException_WrapNoHandle_AndLeaveTheRoomUsable()
    {
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("failed-connect");
        using var room = new Room();
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;

        var badSecret = new AccessToken(RtcTestFixture.API_KEY, new string('x', 48))
            .WithIdentity("failed-connect")
            .WithGrants(new VideoGrants { RoomJoin = true, Room = roomName })
            .ToJwt();
        var noRetries = new RoomOptions { JoinRetries = 0 };
        await Assert.ThrowsAsync<RoomException>(() =>
            room.ConnectAsync(_server.Url, badSecret, noRetries)
        );
        await Assert.ThrowsAsync<RoomException>(() =>
            room.ConnectAsync(
                "ws://127.0.0.1:1",
                _server.Token("failed-connect", roomName),
                noRetries
            )
        );
        Assert.False(room.IsConnected);
        // Retried: earlier tests may still release handles in the background (an abandoned
        // connection is released after its native disconnect), which this count includes
        await Eventually.TrueAsync(
            () =>
            {
                ResourceSnapshot.CollectGarbage();
                return FfiHandle.LiveCount <= live;
            },
            ServerSettle,
            "the failed connects wrapped no handle"
        );

        await room.ConnectAsync(_server.Url, _server.Token("failed-connect", roomName));
        Assert.True(room.IsConnected);
        await room.DisconnectAsync();
        log.AssertNoProblems();
    }

    [Fact]
    public async Task OneRoomObject_ReusedForManyConnections_RaisesEveryEventOncePerConnection()
    {
        // ConnectAsync subscribes the room to the FFI events and the disconnection unsubscribes
        // it: a leftover subscription would deliver every event twice from the second connection
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("reuse");
        var cycles = StressLevel.Scale(4, 25);
        using var observer = await _server.ConnectAsync("reuse-observer", roomName);
        using var room = new Room();
        var connected = 0;
        var disconnected = 0;
        var received = new ConcurrentQueue<string>();
        room.Connected += (_, _) => Interlocked.Increment(ref connected);
        room.Disconnected += (_, _) => Interlocked.Increment(ref disconnected);
        room.DataReceived += (_, e) =>
            received.Enqueue(System.Text.Encoding.UTF8.GetString(e.Data));

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var seen = Eventually.Signal<bool>();
            void OnJoin(object? s, Participant p)
            {
                if (p.Identity == "reuse")
                    seen.TrySetResult(true);
            }
            observer.ParticipantConnected += OnJoin;
            await room.ConnectAsync(_server.Url, _server.Token("reuse", roomName));
            await seen.Task.Within(ServerSettle, "the observer sees the reused room join");
            observer.ParticipantConnected -= OnJoin;

            // Connected is queued behind the previous connection's deferred cleanup: once it was
            // raised, that cleanup ran, and must not have touched this connection
            var expectedConnected = cycle + 1;
            await Eventually.TrueAsync(
                () => Volatile.Read(ref connected) == expectedConnected,
                ServerSettle,
                $"Connected of cycle {cycle}"
            );
            Assert.True(room.IsConnected);
            Assert.NotNull(room.LocalParticipant);
            Assert.True(room.RemoteParticipants.ContainsKey("reuse-observer"));

            await observer.LocalParticipant!.PublishDataAsync(
                System.Text.Encoding.UTF8.GetBytes($"cycle-{cycle}"),
                new DataPublishOptions { DestinationIdentities = new[] { "reuse" } }
            );
            await Eventually.TrueAsync(
                () => received.Contains($"cycle-{cycle}"),
                ServerSettle,
                $"the data of cycle {cycle} arrives"
            );
            await room.DisconnectAsync();
        }

        await Eventually.TrueAsync(
            () => Volatile.Read(ref disconnected) == cycles,
            ServerSettle,
            "one Disconnected per connection"
        );
        Assert.Equal(cycles, Volatile.Read(ref connected));
        Assert.Equal(cycles, received.Count);
        Assert.Equal(cycles, received.Distinct().Count());
        log.AssertNoProblems();
    }

    [Theory]
    [InlineData("removed by the server", Proto.DisconnectReason.ParticipantRemoved)]
    [InlineData("room deleted", Proto.DisconnectReason.RoomDeleted)]
    public async Task ServerInitiatedDisconnect_CleansUpLikeDisconnectAsync(
        string how,
        Proto.DisconnectReason expected
    )
    {
        // The FFI's own Disconnected event takes the same cleanup path as DisconnectAsync: one
        // Disconnected with the real reason, then the room is reusable
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("server-disconnect");
        using var room = await _server.ConnectAsync("kicked", roomName);
        var reasons = new ConcurrentQueue<Proto.DisconnectReason>();
        room.Disconnected += (_, reason) => reasons.Enqueue(reason);

        if (expected == Proto.DisconnectReason.ParticipantRemoved)
            await _server.RoomService.RemoveParticipant(
                new RoomParticipantIdentity { Room = roomName, Identity = "kicked" }
            );
        else
            await _server.RoomService.DeleteRoom(new DeleteRoomRequest { Room = roomName });

        await Eventually.TrueAsync(() => !reasons.IsEmpty, ServerSettle, $"Disconnected ({how})");
        await Eventually.TrueAsync(
            () => room.LocalParticipant == null,
            ServerSettle,
            "the room clears its state"
        );
        Assert.Equal(new[] { expected }, reasons);
        Assert.False(room.IsConnected);
        Assert.Equal(Proto.ConnectionState.ConnDisconnected, room.ConnectionState);

        await room.DisconnectAsync(); // nothing left to disconnect
        await room.ConnectAsync(_server.Url, _server.Token("kicked", roomName));
        Assert.True(room.IsConnected);
        await room.DisconnectAsync();
        Assert.Single(reasons.Skip(1));
        log.AssertNoProblems();
    }

    [Fact]
    public async Task SynchronousDisconnectAndDispose_OnAUiLikeThread_DoNotDeadlock()
    {
        // Room.Disconnect and Room.Dispose block on DisconnectAsync: resuming on the captured
        // synchronization context would deadlock a WPF/WinForms/MAUI UI thread
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("ui-thread");

        foreach (var useDispose in new[] { false, true })
        {
            var room = await _server.ConnectAsync("ui-thread", roomName);
            var completed = SingleThreadSynchronizationContext.TryRunBlocking(
                () =>
                {
                    if (useDispose)
                        room.Dispose();
                    else
                        room.Disconnect();
                },
                TimeSpan.FromSeconds(20),
                out var error,
                out var posted
            );
            Assert.True(
                completed,
                $"Room.{(useDispose ? "Dispose" : "Disconnect")} deadlocked on a single-threaded synchronization context ({posted} continuation(s) posted to the blocked thread)"
            );
            Assert.Null(error);
            Assert.False(room.IsConnected);
            room.Dispose();
            await _server.WaitForIdentitiesAsync(roomName, Array.Empty<string>(), ServerSettle);
        }
        log.AssertNoProblems();
    }
}
