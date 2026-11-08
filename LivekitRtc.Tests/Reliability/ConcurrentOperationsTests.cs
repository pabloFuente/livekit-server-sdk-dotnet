// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// The public API used concurrently the way applications do: many rooms at once, concurrent
/// publishing, unpublishing, muting and data, cancellation in the middle of an operation, and
/// media streams opened, read and disposed while frames keep arriving. Each test checks that the
/// SDK's local state, the server's view and the handle accounting agree at the end.
/// </summary>
[Collection(RoomReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class ConcurrentOperationsTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    private readonly ReliabilityServerFixture _server;
    private readonly ITestOutputHelper _output;

    public ConcurrentOperationsTests(ReliabilityServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    [Fact]
    public async Task ManyRooms_ConnectPublishAndDisconnectInParallel_Independently()
    {
        var log = SdkLog.BeginScope();
        var count = StressLevel.Scale(6, 12);
        var baseline = ResourceSnapshot.Capture();
        var roomNames = Enumerable
            .Range(0, count)
            .Select(i => ReliabilityServerFixture.NewRoomName($"parallel-{i % 3}"))
            .ToArray();

        async Task<string> RunOne(int i)
        {
            using var source = new AudioSource(48000, 1);
            using var track = LocalAudioTrack.Create($"parallel-audio-{i}", source);
            using var room = await _server.ConnectAsync($"parallel-{i}", roomNames[i]);
            var publication = await room.LocalParticipant!.PublishTrackAsync(track);
            await Eventually.TrueAsync(
                async () =>
                    (await _server.TrackSidsAsync(roomNames[i], $"parallel-{i}")).Contains(
                        publication.Sid
                    ),
                TimeSpan.FromSeconds(45),
                $"the server lists the track of parallel-{i}"
            );
            await room.DisconnectAsync();
            return roomNames[i];
        }

        var finished = await Task.WhenAll(
                Enumerable.Range(0, count).Select(i => Task.Run(() => RunOne(i)))
            )
            .Within(TimeSpan.FromMinutes(2), "the parallel rooms");

        foreach (var name in finished.Distinct())
            await _server.WaitUntilEmptyAsync(name);
        await ResourceSnapshot.AssertWithinAsync(
            baseline,
            new LeakBudget { NativeHeapBytes = 64L * 1024 * 1024 },
            _output,
            $"{count} parallel rooms"
        );
        log.AssertNoProblems();
    }

    [Fact]
    public async Task ConcurrentPublishUnpublishAndMute_KeepTheLocalAndServerStateConsistent()
    {
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("publish-churn");
        using var room = await _server.ConnectAsync("publisher", roomName);
        var local = room.LocalParticipant!;
        var published = new ConcurrentQueue<string>();
        var unpublished = new ConcurrentQueue<string>();
        room.LocalTrackPublished += (_, e) => published.Enqueue(e.Publication.Sid);
        room.LocalTrackUnpublished += (_, e) => unpublished.Enqueue(e.Publication.Sid);

        var sources = new List<AudioSource>();
        LocalAudioTrack NewTrack(string name)
        {
            var source = new AudioSource(48000, 1);
            lock (sources)
                sources.Add(source);
            return LocalAudioTrack.Create(name, source);
        }

        try
        {
            // The server accepts at most 20 publications in flight per participant
            var first = await Task.WhenAll(
                Enumerable.Range(0, 10).Select(i => local.PublishTrackAsync(NewTrack($"first-{i}")))
            );

            // Unpublish half, publish five more and mute the rest, all at once
            var operations = new List<Task>();
            operations.AddRange(first.Take(5).Select(p => local.UnpublishTrackAsync(p.Sid)));
            var second = Enumerable
                .Range(0, 5)
                .Select(i => local.PublishTrackAsync(NewTrack($"second-{i}")))
                .ToArray();
            operations.AddRange(second);
            operations.AddRange(
                first.Skip(5).Select(p => Task.Run(() => ((LocalTrack)p.Track!).Mute()))
            );
            await Task.WhenAll(operations).Within(Settle, "the concurrent operations");

            var expected = first
                .Skip(5)
                .Concat(second.Select(t => t.Result))
                .Select(p => p.Sid)
                .OrderBy(s => s)
                .ToArray();
            Assert.Equal(expected, local.TrackPublications.Keys.OrderBy(s => s));
            Assert.All(first.Skip(5), p => Assert.True(p.Track!.IsMuted));
            await Eventually.TrueAsync(
                async () =>
                    (await _server.TrackSidsAsync(roomName, "publisher")).SequenceEqual(expected),
                Settle,
                "the server lists the same tracks as the SDK"
            );
            await Eventually.TrueAsync(
                () => published.Count == 15 && unpublished.Count == 5,
                Settle,
                "one LocalTrackPublished per publication and one LocalTrackUnpublished per unpublication"
            );
            Assert.Equal(15, published.Distinct().Count());
            Assert.Equal(
                first.Take(5).Select(p => p.Sid).OrderBy(s => s),
                unpublished.OrderBy(s => s)
            );
        }
        finally
        {
            await room.DisconnectAsync();
            foreach (var source in sources)
                source.Dispose();
        }
        log.AssertNoProblems();
    }

    [Fact]
    public async Task DataPublishedFromManyTasks_IsRaisedExactlyOnce_InEachTasksOrder()
    {
        var log = SdkLog.BeginScope();
        const int Tasks = 8;
        var perTask = StressLevel.Scale(60, 600);
        var received = new ConcurrentDictionary<string, ConcurrentQueue<int>>();
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("data-tasks"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity == "receiver")
                    room.DataReceived += (_, e) =>
                        received
                            .GetOrAdd(e.Topic!, _ => new ConcurrentQueue<int>())
                            .Enqueue(RoomGroup.SequenceNumber(e));
            }
        );

        await Task.WhenAll(
            Enumerable
                .Range(0, Tasks)
                .Select(t =>
                    Task.Run(() =>
                        group.SendSequenceAsync("sender", "receiver", perTask, $"task-{t}")
                    )
                )
        );

        await Eventually.TrueAsync(
            () => received.Values.Sum(q => q.Count) >= Tasks * perTask,
            Settle,
            "every packet"
        );
        Assert.Equal(Tasks, received.Count);
        Assert.All(received.Values, q => Assert.Equal(Enumerable.Range(0, perTask), q));
        log.AssertNoProblems();
    }

    [Fact]
    public async Task TextStreams_SentConcurrently_AreEachReadOnce()
    {
        var log = SdkLog.BeginScope();
        var count = StressLevel.Scale(20, 200);
        var texts = new ConcurrentBag<string>();
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("text-streams"),
            new[] { "receiver", "sender" },
            (identity, room) =>
            {
                if (identity == "receiver")
                    room.RegisterTextStreamHandler(
                        "concurrent",
                        // Handlers already run on the thread pool and may block on the reader
                        (reader, _) =>
                            texts.Add(reader.ReadAllAsync().AsTask().GetAwaiter().GetResult())
                    );
            }
        );

        var sender = group["sender"].LocalParticipant!;
        await Task.WhenAll(
            Enumerable
                .Range(0, count)
                .Select(i =>
                    sender.SendTextAsync(
                        $"text-{i}-" + new string('x', i * 997),
                        "concurrent",
                        destinationIdentities: new[] { "receiver" }
                    )
                )
        );

        await Eventually.TrueAsync(() => texts.Count >= count, Settle, "every text stream");
        Assert.Equal(
            Enumerable
                .Range(0, count)
                .Select(i => $"text-{i}-" + new string('x', i * 997))
                .OrderBy(t => t),
            texts.OrderBy(t => t)
        );
        log.AssertNoProblems();
    }

    [Fact]
    public async Task CancellingPublishTrackAsync_AtAnyPoint_LeavesNoTrackUnknownToTheSdk_AndDoesNotStallTheRoom()
    {
        // A publish cancelled after the FFI published the track must be undone, or the server
        // keeps a track the SDK does not list. Its LocalTrackPublished event used to wait on the
        // room's event chain for a registration that never came: every later event of the room
        // was delayed by 10 s, then "[WARN] RoomEvent.LocalTrackPublished: Publication ... not
        // found" and "LocalTrackUnpublished: Publication ... not found" were logged.
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("cancel-publish");
        using var room = new Room();
        await room.ConnectAsync(
            _server.Url,
            _server.Token("cancel-publish", roomName, canUpdateOwnMetadata: true)
        );
        var markers = new ConcurrentQueue<string>();
        room.ParticipantMetadataChanged += (_, p) => markers.Enqueue(p.Metadata);
        var outcomes = new List<string>();

        foreach (var delayMs in new[] { 0, 1, 2, 5, 10, 30, 80, 200 })
        {
            using var source = new AudioSource(48000, 1);
            using var track = LocalAudioTrack.Create($"cancel-{delayMs}", source);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(delayMs));
            try
            {
                var publication = await room.LocalParticipant!.PublishTrackAsync(
                    track,
                    null,
                    cts.Token
                );
                outcomes.Add($"{delayMs} ms: published");
                await room.LocalParticipant.UnpublishTrackAsync(publication.Sid);
            }
            catch (OperationCanceledException)
            {
                outcomes.Add($"{delayMs} ms: cancelled");
            }

            await Eventually.TrueAsync(
                async () => (await _server.TrackSidsAsync(roomName, "cancel-publish")).Length == 0,
                Settle,
                $"no track is left on the server after the {delayMs} ms attempt"
            );
            Assert.Empty(room.LocalParticipant!.TrackPublications);

            // The room's event chain still delivers promptly
            var marker = $"after-{delayMs}";
            await room.LocalParticipant.SetMetadataAsync(marker);
            await Eventually.TrueAsync(
                () => markers.Contains(marker),
                TimeSpan.FromSeconds(3),
                $"the room raises events promptly after the {delayMs} ms attempt"
            );
        }
        _output.WriteLine(string.Join(Environment.NewLine, outcomes));
        await room.DisconnectAsync();
        log.AssertNoProblems("PublishTrackAsync was cancelled after track");
    }

    [Fact]
    public async Task PublishingOneTrackTwiceConcurrently_FailsSafely()
    {
        // Not a supported use: whatever the FFI answers, the SDK must not crash, hang or list
        // the track twice
        var log = SdkLog.BeginScope();
        var roomName = ReliabilityServerFixture.NewRoomName("double-publish");
        using var room = await _server.ConnectAsync("double-publish", roomName);
        using var source = new AudioSource(48000, 1);
        using var track = LocalAudioTrack.Create("twice", source);

        var attempts = new[]
        {
            room.LocalParticipant!.PublishTrackAsync(track),
            room.LocalParticipant.PublishTrackAsync(track),
        };
        await Task.WhenAll(attempts.Select(a => a.ContinueWith(_ => { })))
            .Within(Settle, "both publish calls complete");

        foreach (var attempt in attempts.Where(a => a.IsFaulted))
            _output.WriteLine($"rejected: {attempt.Exception!.GetBaseException().Message}");
        Assert.Contains(attempts, a => a.IsCompletedSuccessfully);
        var sids = attempts
            .Where(a => a.IsCompletedSuccessfully)
            .Select(a => a.Result.Sid)
            .Distinct()
            .ToArray();
        Assert.Equal(
            sids.OrderBy(s => s),
            room.LocalParticipant.TrackPublications.Keys.OrderBy(s => s)
        );
        await room.DisconnectAsync();
    }

    [Fact]
    public async Task MediaStreams_OpenedReadAndDisposedConcurrently_WhileFramesFlow_ReleaseEveryFrame()
    {
        // Every frame the FFI delivers carries a native buffer the SDK must release: while the
        // stream is read, after it was disposed (the native side may still emit a few frames),
        // and when nobody reads. Several readers per stream and disposal from another thread.
        var log = SdkLog.BeginScope();
        var cycles = StressLevel.Scale(30, 300);
        var audioTrack = Eventually.Signal<RemoteAudioTrack>();
        var videoTrack = Eventually.Signal<RemoteVideoTrack>();
        await using var group = await RoomGroup.ConnectAsync(
            _server,
            ReliabilityServerFixture.NewRoomName("streams"),
            new[] { "viewer", "camera" },
            (identity, room) =>
            {
                if (identity == "viewer")
                    room.TrackSubscribed += (_, e) =>
                    {
                        if (e.Track is RemoteAudioTrack a)
                            audioTrack.TrySetResult(a);
                        else if (e.Track is RemoteVideoTrack v)
                            videoTrack.TrySetResult(v);
                    };
            }
        );
        using var audioSource = new AudioSource(48000, 1);
        using var videoSource = new VideoSource(640, 360);
        using var localAudio = LocalAudioTrack.Create("streams-audio", audioSource);
        using var localVideo = LocalVideoTrack.Create("streams-video", videoSource);
        await using var audioPump = MediaPump.Audio(audioSource);
        await using var videoPump = MediaPump.Video(videoSource);
        var camera = group["camera"].LocalParticipant!;
        await camera.PublishTrackAsync(localAudio);
        await camera.PublishTrackAsync(localVideo, new TrackPublishOptions { Simulcast = false });
        var remoteAudio = await audioTrack.Task.Within(Settle, "the audio subscription");
        var remoteVideo = await videoTrack.Task.Within(Settle, "the video subscription");

        // Frames flow before the measured cycles start
        using (var warmup = VideoStream.FromTrack(remoteVideo))
            Assert.NotNull(await warmup.ReadAsync().AsTask().Within(Settle, "a first video frame"));
        var baseline = ResourceSnapshot.Capture();
        var framesRead = 0L;

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var readBefore = Interlocked.Read(ref framesRead);
            var audio = AudioStream.FromTrack(remoteAudio, capacity: 8);
            var video = VideoStream.FromTrack(remoteVideo, capacity: 2);
            using var stop = new CancellationTokenSource();
            var readers = Enumerable
                .Range(0, 2)
                .SelectMany(_ =>
                    new[]
                    {
                        Task.Run(async () =>
                        {
                            while (await audio.ReadAsync() != null)
                                Interlocked.Increment(ref framesRead);
                        }),
                        Task.Run(async () =>
                        {
                            while (await video.ReadAsync() != null)
                                Interlocked.Increment(ref framesRead);
                        }),
                    }
                )
                .ToArray();
            await Eventually.TrueAsync(
                () => Interlocked.Read(ref framesRead) >= readBefore + 5,
                Settle,
                $"frames reach the readers of cycle {cycle}"
            );

            // Dispose from another thread while the readers wait, half of them asynchronously
            await Task.WhenAll(
                Task.Run(() => audio.Dispose()),
                cycle % 2 == 0 ? Task.Run(() => video.Dispose()) : video.DisposeAsync().AsTask()
            );
            var ended = await Task.WhenAll(
                    readers.Select(r => r.ContinueWith(t => t.Exception?.GetBaseException()))
                )
                .Within(Settle, "the readers end after Dispose");
            Assert.All(ended, e => Assert.True(e is null or ObjectDisposedException, $"{e}"));
        }

        // A stream nobody reads must release its frames too
        using (var unread = VideoStream.FromTrack(remoteVideo, capacity: 1))
            await Task.Delay(TimeSpan.FromSeconds(1));

        _output.WriteLine($"{framesRead} frames read over {cycles} cycles");
        await ResourceSnapshot.AssertWithinAsync(
            baseline,
            new LeakBudget { NativeHeapBytes = 32L * 1024 * 1024 },
            _output,
            $"{cycles} stream cycles",
            TimeSpan.FromSeconds(15)
        );
        log.AssertNoProblems();
    }
}
