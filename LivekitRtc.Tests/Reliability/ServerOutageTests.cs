// author: https://github.com/pabloFuente

using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Real transport interruptions: the LiveKit server is frozen with <c>docker pause</c>, so the
/// connections stay open but nothing answers, as with a hung server or a network that silently
/// drops packets. The FFI detects it (about 15 s) and reconnects; these tests check what the .NET
/// layer reports and leaves behind: Reconnecting then Reconnected with a consistent room when the
/// server comes back, a bounded DisconnectAsync while it does not answer, and a clean, reusable
/// room once the reconnection budget (about 60 s) is exhausted.
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class ServerOutageTests
{
    private static readonly TimeSpan Detection = TimeSpan.FromSeconds(45);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public ServerOutageTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    [Fact]
    public async Task AShortOutage_IsReportedAsReconnectingThenReconnected_AndTheRoomStaysConsistent()
    {
        var log = SdkLog.BeginScope();
        await using var scenario = await ReconnectionScenario.StartAsync(_server, "short-outage");
        while (scenario.Events.TryDequeue(out _)) { }

        await _server.PauseAsync();
        try
        {
            await Eventually.TrueAsync(
                () => scenario.Events.Contains("Reconnecting"),
                Detection,
                "the SDK reports Reconnecting"
            );
            Assert.Equal(
                Proto.ConnectionState.ConnReconnecting,
                scenario.Publisher.ConnectionState
            );
            Assert.True(scenario.Publisher.IsConnected, "a reconnecting room is still connected");
        }
        finally
        {
            await _server.UnpauseAsync();
        }

        await Eventually.TrueAsync(
            () => scenario.Events.Contains("Reconnected"),
            Detection,
            "the SDK reports Reconnected"
        );
        _output.WriteLine(string.Join(", ", scenario.Events));
        Assert.DoesNotContain(scenario.Events, e => e.StartsWith("Disconnected"));
        Assert.Equal(Proto.ConnectionState.ConnConnected, scenario.Publisher.ConnectionState);
        await scenario.WaitUntilConsistentAsync(_server);
        await scenario.AssertDataArrivesOnceAsync("after-outage");
        log.AssertNoProblems();
    }

    [ExtendedFact]
    [Trait("Tier", "Extended")]
    public async Task DisconnectAsync_WhileTheServerDoesNotAnswer_CompletesAndEndsTheRoom()
    {
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("disconnect-outage");
        var room = await _server.ConnectAsync("disconnect-outage", roomName);
        var reconnecting = Eventually.Signal<bool>();
        var disconnected = 0;
        room.Reconnecting += (_, _) => reconnecting.TrySetResult(true);
        room.Disconnected += (_, _) => Interlocked.Increment(ref disconnected);

        await _server.PauseAsync();
        try
        {
            await reconnecting.Task.Within(Detection, "Reconnecting");
            await room.DisconnectAsync()
                .Within(TimeSpan.FromSeconds(20), "DisconnectAsync during an outage");
            Assert.False(room.IsConnected);
            await Eventually.TrueAsync(
                () => room.LocalParticipant == null,
                Detection,
                "the room clears its state"
            );
            Assert.Equal(1, Volatile.Read(ref disconnected));
        }
        finally
        {
            await _server.UnpauseAsync();
            room.Dispose();
        }
        log.AssertNoProblems();
    }

    [ExtendedFact]
    [Trait("Tier", "Extended")]
    public async Task AnOutageLongerThanTheReconnectionBudget_EndsInDisconnected_AndTheRoomIsReusable()
    {
        // The FFI gives up after about 60 s of attempts and reports the disconnection itself:
        // the SDK must take the same cleanup path as DisconnectAsync
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("long-outage");
        using var room = await _server.ConnectAsync("long-outage", roomName);
        using var source = new AudioSource(48000, 1);
        using var track = LocalAudioTrack.Create("long-outage-audio", source);
        await room.LocalParticipant!.PublishTrackAsync(track);
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        room.Reconnecting += (_, _) => events.Enqueue("Reconnecting");
        room.Reconnected += (_, _) => events.Enqueue("Reconnected");
        room.Disconnected += (_, r) => events.Enqueue($"Disconnected {r}");

        await _server.PauseAsync();
        try
        {
            await Eventually.TrueAsync(
                () => events.Any(e => e.StartsWith("Disconnected")),
                TimeSpan.FromSeconds(150),
                "the SDK reports Disconnected once the reconnection budget is exhausted"
            );
        }
        finally
        {
            await _server.UnpauseAsync();
        }

        _output.WriteLine(string.Join(", ", events));
        Assert.Equal("Reconnecting", events.First());
        Assert.Single(events, e => e.StartsWith("Disconnected"));
        Assert.DoesNotContain("Reconnected", events);
        Assert.False(room.IsConnected);
        await Eventually.TrueAsync(
            () => room.LocalParticipant == null,
            Detection,
            "the room clears its state"
        );

        await room.ConnectAsync(
            _server.Url,
            _server.Token("long-outage", ReliabilityServerFixture.NewRoomName("long-outage-again"))
        );
        Assert.True(room.IsConnected);
        await room.DisconnectAsync();
        log.AssertNoProblems();
    }
}
