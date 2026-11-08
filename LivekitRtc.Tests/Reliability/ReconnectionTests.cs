// author: https://github.com/pabloFuente

using LiveKit.Rtc.Internal;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// What the .NET layer does with the FFI's reconnection events: the room's state and events stay
/// consistent through repeated reconnections (the publication objects the application holds stay
/// valid and are re-keyed, every event is raised once and in order, handlers are not duplicated),
/// and a disconnection or disposal in the middle of a reconnection ends the room cleanly. The
/// reconnections are forced with <see cref="Room.SimulateScenarioAsync"/>; how the FFI reconnects
/// is not what is tested. Real server outages are in <see cref="ServerOutageTests"/>.
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class ReconnectionTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public ReconnectionTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    [Theory]
    [InlineData(Proto.SimulateScenarioKind.SimulateFullReconnect)]
    [InlineData(Proto.SimulateScenarioKind.SimulateDisconnectSignalOnResume)]
    public async Task RepeatedFullReconnects_KeepThePublicationsTheEventsAndTheRemoteViewConsistent(
        Proto.SimulateScenarioKind kind
    )
    {
        // A full reconnect republishes the local tracks under new SIDs (SIMULATE_DISCONNECT_SIGNAL_ON_RESUME
        // reaches it by escalating a failed resume). Per cycle: Reconnecting, one
        // LocalTrackRepublished per track with its previous SID, Reconnected, in that order.
        var log = SdkLog.BeginScope();
        await using var scenario = await ReconnectionScenario.StartAsync(_server, "full-reconnect");
        var cycles = StressLevel.Scale(3, 15);

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            while (scenario.Events.TryDequeue(out _)) { }
            var previousSids = scenario.Publications.Select(p => p.Sid).ToArray();

            await scenario.Publisher.SimulateScenarioAsync(kind);
            await Eventually.TrueAsync(
                () => scenario.Events.Contains("Reconnected"),
                Settle,
                $"Reconnected of cycle {cycle}"
            );

            var events = scenario.Events.ToArray();
            _output.WriteLine($"cycle {cycle}: {string.Join(", ", events)}");
            Assert.Equal("Reconnecting", events[0]);
            Assert.Equal("Reconnected", events[^1]);
            Assert.Equal(
                previousSids.Select(s => $"LocalTrackRepublished {s}").OrderBy(e => e),
                events[1..^1].OrderBy(e => e)
            );
            Assert.All(scenario.Publications, p => Assert.DoesNotContain(p.Sid, previousSids));
            Assert.True(scenario.Publisher.IsConnected);
            Assert.Equal(Proto.ConnectionState.ConnConnected, scenario.Publisher.ConnectionState);

            await scenario.WaitUntilConsistentAsync(_server);
            await scenario.AssertDataArrivesOnceAsync($"after-cycle-{cycle}");
        }
        log.AssertNoProblems();
    }

    [Fact]
    public async Task ASubscriberThatFullyReconnects_RebuildsItsViewOfTheRemoteTracks()
    {
        // The other side of a full reconnect: the FFI tells the reconnecting room that every remote
        // participant left and joined again, with their publications and subscriptions. The room
        // must end with the same participants subscribed to the same tracks, nothing dropped.
        var log = SdkLog.BeginScope();
        await using var scenario = await ReconnectionScenario.StartAsync(
            _server,
            "subscriber-reconnect"
        );
        var sids = scenario.Publications.Select(p => p.Sid).ToArray();
        var reconnected = 0;
        scenario.Observer.Reconnected += (_, _) => Interlocked.Increment(ref reconnected);
        var cycles = StressLevel.Scale(3, 15);

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            await scenario.Observer.SimulateScenarioAsync(
                Proto.SimulateScenarioKind.SimulateFullReconnect
            );
            var expected = cycle + 1;
            await Eventually.TrueAsync(
                () => Volatile.Read(ref reconnected) == expected,
                Settle,
                $"the observer reconnects ({cycle})"
            );
            await scenario.WaitUntilConsistentAsync(_server);
            Assert.Equal(sids, scenario.Publications.Select(p => p.Sid));
            Assert.Equal(new[] { "publisher" }, scenario.Observer.RemoteParticipants.Keys);
        }
        log.AssertNoProblems();
    }

    [Fact]
    public async Task RepeatedSignalResumes_KeepTheSamePublications()
    {
        // A resume re-establishes the signaling connection only: nothing is republished
        var log = SdkLog.BeginScope();
        await using var scenario = await ReconnectionScenario.StartAsync(_server, "resume");
        var sids = scenario.Publications.Select(p => p.Sid).ToArray();
        var cycles = StressLevel.Scale(3, 15);

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            while (scenario.Events.TryDequeue(out _)) { }
            await scenario.Publisher.SimulateScenarioAsync(
                Proto.SimulateScenarioKind.SimulateSignalReconnect
            );
            await Eventually.TrueAsync(
                () => scenario.Events.Contains("Reconnected"),
                Settle,
                $"Reconnected of resume {cycle}"
            );
            _output.WriteLine($"resume {cycle}: {string.Join(", ", scenario.Events)}");
            Assert.DoesNotContain(scenario.Events, e => e.StartsWith("LocalTrackRepublished"));
            Assert.Equal(sids, scenario.Publications.Select(p => p.Sid));

            await scenario.WaitUntilConsistentAsync(_server);
            await scenario.AssertDataArrivesOnceAsync($"after-resume-{cycle}");
        }
        log.AssertNoProblems();
    }

    [Theory]
    [InlineData("DisconnectAsync")]
    [InlineData("Dispose")]
    public async Task DisconnectingDuringAReconnection_EndsTheRoomCleanly(string how)
    {
        // Ending the room while the FFI rebuilds its session: the call completes, Disconnected is
        // raised once and is the last event (no Reconnected after it), the room clears its state,
        // and no handle is released twice
        var log = SdkLog.BeginScope();
        var failed = FfiHandle.FailedReleaseCount;
        await using var scenario = await ReconnectionScenario.StartAsync(
            _server,
            "disconnect-reconnecting"
        );
        while (scenario.Events.TryDequeue(out _)) { }

        await scenario.Publisher.SimulateScenarioAsync(
            Proto.SimulateScenarioKind.SimulateFullReconnect
        );
        await Eventually.TrueAsync(
            () => scenario.Events.Contains("Reconnecting"),
            Settle,
            "Reconnecting"
        );
        var took = System.Diagnostics.Stopwatch.StartNew();
        if (how == "Dispose")
            await Task.Run(() => scenario.Publisher.Dispose()).Within(Settle, "Dispose");
        else
            await scenario.Publisher.DisconnectAsync().Within(Settle, "DisconnectAsync");
        _output.WriteLine($"{how} took {took.ElapsedMilliseconds} ms");

        await Eventually.TrueAsync(
            () => scenario.Events.Any(e => e.StartsWith("Disconnected")),
            Settle,
            "Disconnected"
        );
        await Task.Delay(TimeSpan.FromSeconds(2)); // a late Reconnected would land here
        var events = scenario.Events.ToArray();
        _output.WriteLine(string.Join(", ", events));
        Assert.Single(events, e => e.StartsWith("Disconnected"));
        Assert.StartsWith("Disconnected", events[^1]);
        Assert.False(scenario.Publisher.IsConnected);
        Assert.Null(scenario.Publisher.LocalParticipant);
        Assert.Empty(scenario.Publisher.RemoteParticipants);

        // Not checked: the server's participant list. A client that leaves while its signal
        // connection is down cannot tell the server, which drops it after its own timeout (13 s
        // to more than 30 s under load). RoomLifecycleTests checks the release of ended rooms.
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
        log.AssertNoProblems();
    }

    [Fact]
    public async Task CancellingSimulateScenarioAsync_LeavesTheRoomConsistent()
    {
        var log = SdkLog.BeginScope();
        await using var scenario = await ReconnectionScenario.StartAsync(
            _server,
            "cancel-scenario"
        );
        while (scenario.Events.TryDequeue(out _)) { }

        // An already cancelled token throws before the scenario is requested: nothing happens
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scenario.Publisher.SimulateScenarioAsync(
                Proto.SimulateScenarioKind.SimulateFullReconnect,
                new CancellationToken(canceled: true)
            )
        );
        await Task.Delay(TimeSpan.FromSeconds(2)); // a reconnection would start within this window
        Assert.Empty(scenario.Events);
        Assert.Equal(Proto.ConnectionState.ConnConnected, scenario.Publisher.ConnectionState);

        // Cancelled once requested, it cancels the wait for the acknowledgement, not the
        // scenario: the call throws, or completes if the acknowledgement already arrived
        using var cts = new CancellationTokenSource();
        var call = scenario.Publisher.SimulateScenarioAsync(
            Proto.SimulateScenarioKind.SimulateFullReconnect,
            cts.Token
        );
        cts.Cancel();
        var error = await Record.ExceptionAsync(() => call);
        _output.WriteLine(error == null ? "acknowledged before the wait" : "wait cancelled");
        Assert.True(error == null || error is OperationCanceledException, $"{error}");

        await Eventually.TrueAsync(
            () => scenario.Events.Contains("Reconnected"),
            Settle,
            "the scenario still runs"
        );
        await scenario.WaitUntilConsistentAsync(_server);
        await scenario.AssertDataArrivesOnceAsync("after-cancelled-scenario");
        log.AssertNoProblems();
    }

    [Fact]
    public async Task ReconnectionCycles_DoNotAccumulateHandles()
    {
        // Each full reconnect hands the SDK new publication and track handles; the ones they
        // replace must become collectable and be released
        var log = SdkLog.BeginScope();
        var cycles = StressLevel.Scale(4, 20);
        await using var scenario = await ReconnectionScenario.StartAsync(
            _server,
            "reconnect-handles"
        );
        await scenario.Publisher.SimulateScenarioAsync(
            Proto.SimulateScenarioKind.SimulateFullReconnect
        );
        await Eventually.TrueAsync(
            () => scenario.Events.Contains("Reconnected"),
            Settle,
            "warm-up"
        );
        await scenario.WaitUntilConsistentAsync(_server);
        var baseline = ResourceSnapshot.Capture();

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            while (scenario.Events.TryDequeue(out _)) { }
            await scenario.Publisher.SimulateScenarioAsync(
                Proto.SimulateScenarioKind.SimulateFullReconnect
            );
            await Eventually.TrueAsync(
                () => scenario.Events.Contains("Reconnected"),
                Settle,
                $"cycle {cycle}"
            );
            await scenario.WaitUntilConsistentAsync(_server);
        }

        await ResourceSnapshot.AssertWithinAsync(
            baseline,
            new LeakBudget { Handles = 4, NativeHeapBytes = 48L * 1024 * 1024 },
            _output,
            $"{cycles} full reconnects",
            TimeSpan.FromSeconds(20)
        );
        log.AssertNoProblems();
    }
}
