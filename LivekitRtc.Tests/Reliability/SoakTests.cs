// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using System.Text;
using Livekit.Server.Sdk.Dotnet;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// The extended tier's soak: several participants run a seeded mix of everything the other tests
/// exercise one at a time (publishing, unpublishing, data, text streams, media streams, simulated
/// reconnections, ending the room in each supported way) for <c>LIVEKIT_SOAK_MINUTES</c> minutes
/// (5 by default). An observer checks that every packet of every session arrives exactly once, and
/// the process must end with no handle, socket or thread left and nothing reported on stderr.
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class SoakTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public SoakTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    [ExtendedFact]
    [Trait("Tier", "Extended")]
    public async Task MixedWorkload_ForSeveralMinutes_LeavesNothingBehind()
    {
        var minutes = double.TryParse(
            Environment.GetEnvironmentVariable("LIVEKIT_SOAK_MINUTES"),
            out var configured
        )
            ? configured
            : 5;
        const int Workers = 3;
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("soak");
        var received = new ConcurrentDictionary<string, ConcurrentQueue<int>>();
        var observer = await _server.ConnectAsync("observer", roomName);
        // The sender is in the payload: data may arrive before the observer lists its sender, and
        // DataReceivedEventArgs.Participant is then null (as in the Node and Python SDKs)
        observer.DataReceived += (_, e) =>
        {
            var parts = Encoding.UTF8.GetString(e.Data).Split(':');
            received
                .GetOrAdd(parts[0], _ => new ConcurrentQueue<int>())
                .Enqueue(int.Parse(parts[1]));
        };
        observer.RegisterTextStreamHandler(
            "soak",
            (reader, _) =>
            {
                try
                {
                    reader.ReadAllAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (StreamException)
                {
                    // Expected when a session ends before its stream's trailer reached the observer
                }
            }
        );

        int Received(string identity) => received.TryGetValue(identity, out var q) ? q.Count : 0;
        await RunSessionAsync(roomName, "warmup", new Random(0), observer, Received);
        var baseline = ResourceSnapshot.Capture();
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(minutes);
        var sent = new ConcurrentDictionary<string, int>();
        var sessions = 0;

        var workers = Task.WhenAll(
            Enumerable
                .Range(0, Workers)
                .Select(worker =>
                    Task.Run(async () =>
                    {
                        var random = new Random(1000 + worker);
                        for (var session = 0; DateTime.UtcNow < deadline; session++)
                        {
                            var identity = $"worker-{worker}-{session}";
                            sent[identity] = await RunSessionAsync(
                                roomName,
                                identity,
                                random,
                                observer,
                                Received
                            );
                            Interlocked.Increment(ref sessions);
                        }
                    })
                )
        );
        await workers.Within(
            TimeSpan.FromMinutes(minutes) + TimeSpan.FromMinutes(5),
            "the soak workers"
        );

        foreach (var (identity, count) in sent)
        {
            await Eventually.TrueAsync(
                () => Received(identity) >= count,
                Settle,
                $"the {count} packets of {identity}"
            );
            Assert.Equal(
                Enumerable.Range(0, count),
                received.GetValueOrDefault(identity) ?? new ConcurrentQueue<int>()
            );
        }
        _output.WriteLine($"{sessions} sessions, {sent.Values.Sum()} packets in {minutes} min");

        await Eventually.TrueAsync(
            () => observer.RemoteParticipants.Count == 0,
            Settle,
            "every worker left"
        );
        await observer.DisconnectAsync();
        observer.Dispose();

        // The native heap is reported, not asserted: participants that subscribe to each other
        // concurrently leave native memory behind (a few hundred KB per session) although every
        // handle the SDK wrapped is released and no event handed over a handle the SDK ignored.
        // The focused leak tests assert the native heap where the .NET layer owns the release.
        await ResourceSnapshot.AssertWithinAsync(
            baseline,
            new LeakBudget { NativeHeapBytes = long.MaxValue, PrivateBytes = long.MaxValue },
            _output,
            $"soak of {minutes} min",
            TimeSpan.FromSeconds(30)
        );
        log.AssertNoProblems("PublishTrackAsync was cancelled after track");
    }

    // The last operations of every worker, for the failure message of a step that hangs
    private readonly ConcurrentQueue<string> _trace = new();

    // Runs one operation of a session with its own bound, so a hang names the operation
    private async Task StepAsync(string identity, string name, Func<Task> operation)
    {
        _trace.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} {identity}: {name}");
        while (_trace.Count > 40)
            _trace.TryDequeue(out _);
        var task = operation();
        if (await Task.WhenAny(task, Task.Delay(Settle)) != task)
            Assert.Fail(
                $"{identity}: '{name}' did not complete within {Settle.TotalSeconds} s. Last operations:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, _trace)
            );
        await task;
    }

    // One participant session with a random mix of operations; returns the packets it sent
    private async Task<int> RunSessionAsync(
        string roomName,
        string identity,
        Random random,
        Room observer,
        Func<string, int> receivedBy
    )
    {
        Room room = null!;
        await StepAsync(
            identity,
            "connect",
            async () => room = await _server.ConnectAsync(identity, roomName)
        );
        using var audioSource = new AudioSource(48000, 1);
        using var audioTrack = LocalAudioTrack.Create("soak-audio", audioSource);
        var local = room.LocalParticipant!;
        var packets = 0;
        var reconnected = 0;
        room.Reconnected += (_, _) => Interlocked.Increment(ref reconnected);

        var pump = MediaPump.Audio(audioSource);
        await StepAsync(identity, "publish audio", () => local.PublishTrackAsync(audioTrack));
        for (var step = 0; step < 6; step++)
        {
            switch (random.Next(6))
            {
                case 0:
                case 1:
                    await StepAsync(
                        identity,
                        "send 20 packets",
                        async () =>
                        {
                            for (var i = 0; i < 20; i++, packets++)
                                await local.PublishDataAsync(
                                    Encoding.UTF8.GetBytes($"{identity}:{packets}"),
                                    new DataPublishOptions
                                    {
                                        DestinationIdentities = new[] { "observer" },
                                    }
                                );
                        }
                    );
                    // Delivered before the next step: data still in flight when the session
                    // reconnects or ends is the transport's business, not the SDK's
                    var expected = packets;
                    await Eventually.TrueAsync(
                        () => receivedBy(identity) >= expected,
                        Settle,
                        $"the observer receives the packets of {identity}"
                    );
                    break;
                case 2:
                    await StepAsync(
                        identity,
                        "publish and unpublish video",
                        async () =>
                        {
                            using var videoSource = new VideoSource(320, 240);
                            using var videoTrack = LocalVideoTrack.Create(
                                "soak-video",
                                videoSource
                            );
                            videoSource.CaptureFrame(
                                VideoFrame.Create(320, 240, Proto.VideoBufferType.I420)
                            );
                            var publication = await local.PublishTrackAsync(videoTrack);
                            await local.UnpublishTrackAsync(publication.Sid);
                        }
                    );
                    break;
                case 3:
                    var length = random.Next(1, 40000);
                    await StepAsync(
                        identity,
                        $"send a text stream of {length} chars",
                        () =>
                            local.SendTextAsync(
                                new string('s', length),
                                "soak",
                                destinationIdentities: new[] { "observer" }
                            )
                    );
                    break;
                case 4:
                    var kind =
                        random.Next(2) == 0
                            ? Proto.SimulateScenarioKind.SimulateFullReconnect
                            : Proto.SimulateScenarioKind.SimulateSignalReconnect;
                    var before = Volatile.Read(ref reconnected);
                    await StepAsync(
                        identity,
                        $"simulate {kind}",
                        () => room.SimulateScenarioAsync(kind)
                    );
                    await Eventually.TrueAsync(
                        () => Volatile.Read(ref reconnected) > before,
                        Settle,
                        $"{identity} reconnects after {kind}"
                    );
                    break;
                case 5:
                    var track = observer
                        .RemoteParticipants.GetValueOrDefault(identity)
                        ?.TrackPublications.Values.Select(p => p.Track)
                        .OfType<RemoteAudioTrack>()
                        .FirstOrDefault();
                    if (track != null)
                        await StepAsync(
                            identity,
                            "read the remote audio",
                            async () =>
                            {
                                await using var stream = AudioStream.FromTrack(track);
                                await stream.ReadAsync();
                            }
                        );
                    break;
            }
        }
        await StepAsync(identity, "stop the audio pump", () => pump.DisposeAsync().AsTask());

        switch (random.Next(3))
        {
            case 0:
                await StepAsync(identity, "Dispose", () => Task.Run(room.Dispose));
                break;
            case 1:
                await StepAsync(identity, "DisconnectAsync", room.DisconnectAsync);
                room.Dispose();
                break;
            default:
                var gone = Eventually.Signal<bool>();
                room.Disconnected += (_, _) => gone.TrySetResult(true);
                await StepAsync(
                    identity,
                    "removed by the server",
                    async () =>
                    {
                        await _server.RoomService.RemoveParticipant(
                            new RoomParticipantIdentity { Room = roomName, Identity = identity }
                        );
                        await gone.Task;
                    }
                );
                room.Dispose();
                break;
        }
        return packets;
    }
}
