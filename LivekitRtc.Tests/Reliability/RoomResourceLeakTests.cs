// author: https://github.com/pabloFuente

using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Repeated room lifecycles must leave the process where it was, on both sides of a session: the
/// publisher's room, sources and tracks once disposed, and the observer's remote participant,
/// publications, tracks and streams once that participant left. Handles are checked exactly (after
/// collection, since participants and publications are released by their finalizer, like in the
/// Node and Python SDKs); sockets, threads and the native heap with a budget that a room leaked
/// per cycle exceeds.
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class RoomResourceLeakTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public RoomResourceLeakTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    [Fact]
    public async Task ConnectPublishSubscribeDisconnectCycles_LeaveNothingBehind()
    {
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("room-cycles");
        var cycles = StressLevel.Scale(8, 80);
        using var observer = await _server.ConnectAsync("observer", roomName);
        RemoteAudioTrack? subscribed = null;
        var left = 0;
        observer.TrackSubscribed += (_, e) =>
        {
            if (e.Track is RemoteAudioTrack audio)
                Volatile.Write(ref subscribed, audio);
        };
        observer.ParticipantDisconnected += (_, _) => Interlocked.Increment(ref left);

        async Task CycleAsync(int cycle)
        {
            Volatile.Write(ref subscribed, null);
            var leftBefore = Volatile.Read(ref left);

            using var room = await _server.ConnectAsync($"publisher-{cycle}", roomName);
            using var audioSource = new AudioSource(48000, 1);
            using var videoSource = new VideoSource(320, 240);
            using var audioTrack = LocalAudioTrack.Create("cycle-audio", audioSource);
            using var videoTrack = LocalVideoTrack.Create("cycle-video", videoSource);
            await using (MediaPump.Audio(audioSource))
            await using (MediaPump.Video(videoSource))
            {
                await room.LocalParticipant!.PublishTrackAsync(audioTrack);
                await room.LocalParticipant.PublishTrackAsync(videoTrack);

                await Eventually.TrueAsync(
                    () => Volatile.Read(ref subscribed) != null,
                    Settle,
                    $"the observer subscribes to the audio of cycle {cycle}"
                );
                await using var stream = AudioStream.FromTrack(Volatile.Read(ref subscribed)!);
                Assert.NotNull(
                    await stream.ReadAsync().AsTask().Within(Settle, $"audio of cycle {cycle}")
                );
            }

            await room.DisconnectAsync();
            await Eventually.TrueAsync(
                () => Volatile.Read(ref left) > leftBefore,
                Settle,
                $"the observer sees publisher-{cycle} leave"
            );
        }

        for (var i = 0; i < 2; i++)
            await CycleAsync(-1 - i);
        Volatile.Write(ref subscribed, null);
        var baseline = ResourceSnapshot.Capture();

        for (var cycle = 0; cycle < cycles; cycle++)
            await CycleAsync(cycle);
        Volatile.Write(ref subscribed, null);

        await ResourceSnapshot.AssertWithinAsync(
            baseline,
            new LeakBudget { NativeHeapBytes = 48L * 1024 * 1024 },
            _output,
            $"{cycles} room cycles",
            TimeSpan.FromSeconds(20)
        );
        Assert.Empty(observer.RemoteParticipants);
        await observer.DisconnectAsync();
        log.AssertNoProblems();
    }

    [Fact]
    public async Task IncomingDataStreams_DoNotKeepTheirContentInNativeMemory()
    {
        // For every incoming stream the FFI hands over, next to the raw header, chunk and trailer
        // events the SDK reads, a native reader of the same stream. Unreleased, each reader kept
        // the whole stream in native memory for the rest of the process, whether or not a handler
        // was registered for its topic.
        var log = SdkLog.BeginScope();
        var streams = StressLevel.Scale(32, 64);
        const int Size = 1024 * 1024;
        var read = 0;
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("stream-memory"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity != "receiver")
                    return;
                room.RegisterTextStreamHandler(
                    "handled",
                    (reader, _) =>
                    {
                        reader.ReadAllAsync().AsTask().GetAwaiter().GetResult();
                        Interlocked.Increment(ref read);
                    }
                );
                room.RegisterByteStreamHandler(
                    "handled",
                    (reader, _) =>
                    {
                        reader.ReadAllAsync().AsTask().GetAwaiter().GetResult();
                        Interlocked.Increment(ref read);
                    }
                );
            }
        );
        var sender = group["sender"].LocalParticipant!;
        var to = new[] { "receiver" };
        var text = new string('t', Size);
        var bytes = new byte[Size];

        async Task SendAsync(int i)
        {
            // Half of the streams on a topic without handler
            var topic = i % 4 < 2 ? "handled" : "unhandled";
            if (i % 2 == 0)
            {
                await sender.SendTextAsync(text, topic, destinationIdentities: to);
            }
            else
            {
                await using var writer = await sender.StreamBytesAsync(
                    $"blob-{i}",
                    topic,
                    destinationIdentities: to
                );
                await writer.WriteAsync(bytes);
            }
        }

        for (var i = 0; i < 4; i++)
            await SendAsync(i);
        await Eventually.TrueAsync(
            () => Volatile.Read(ref read) >= 2,
            Settle,
            "the warm-up streams"
        );
        var baseline = ResourceSnapshot.Capture();

        for (var i = 0; i < streams; i++)
            await SendAsync(i);
        var handled = 2 + streams / 2;
        await Eventually.TrueAsync(
            () => Volatile.Read(ref read) >= handled,
            TimeSpan.FromSeconds(60),
            "every handled stream is read"
        );

        // Unreleased readers would keep the 32 MB the streams carried
        await ResourceSnapshot.AssertWithinAsync(
            baseline,
            new LeakBudget { NativeHeapBytes = 12L * 1024 * 1024 },
            _output,
            $"{streams} incoming streams of {Size / 1024} KB"
        );
        log.AssertNoProblems();
    }
}
