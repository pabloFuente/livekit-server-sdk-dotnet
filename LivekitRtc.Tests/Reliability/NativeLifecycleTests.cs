// author: https://github.com/pabloFuente

using System.Runtime.CompilerServices;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Ownership of the native handles behind the SDK's resource wrappers, without a server: every
/// handle the FFI hands over is wrapped once and released exactly once, whether the wrapper is
/// disposed once, many times, from many threads at once, or never (finalizer). Use after disposal
/// fails with an exception instead of reaching the FFI with a released handle. These tests check
/// the .NET wrappers through the SDK's handle accounting (<see cref="FfiHandle.LiveCount"/>,
/// <see cref="FfiHandle.FailedReleaseCount"/>), not what the native objects do.
/// </summary>
[Collection(NativeReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class NativeLifecycleTests
{
    private readonly ITestOutputHelper _output;

    public NativeLifecycleTests(ITestOutputHelper output)
    {
        _output = output;
        FfiClient.Instance.Initialize();
    }

    public static TheoryData<string> Wrappers =>
        new()
        {
            "AudioSource",
            "VideoSource",
            "EncodedVideoSource",
            "AudioResampler",
            "LocalAudioTrack",
            "LocalVideoTrack",
        };

    // Each wrapper with the source it needs; the source is not part of what is measured
    private static (IDisposable Wrapper, IDisposable? Owner) Create(string kind)
    {
        switch (kind)
        {
            case "AudioSource":
                return (new AudioSource(48000, 1), null);
            case "VideoSource":
                return (new VideoSource(320, 240), null);
            case "EncodedVideoSource":
                return (new EncodedVideoSource(320, 240), null);
            case "AudioResampler":
                return (new AudioResampler(48000, 16000), null);
            case "LocalAudioTrack":
            {
                var source = new AudioSource(48000, 1);
                return (LocalAudioTrack.Create("lifecycle-audio", source), source);
            }
            case "LocalVideoTrack":
            {
                var source = new VideoSource(320, 240);
                return (LocalVideoTrack.Create("lifecycle-video", source), source);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    // An operation that needs the native handle
    private static void Use(IDisposable wrapper)
    {
        switch (wrapper)
        {
            case AudioSource source:
                source.ClearQueue();
                break;
            case VideoSource source:
                source.CaptureFrame(VideoFrame.Create(320, 240, VideoBufferType.I420));
                break;
            case EncodedVideoSource source:
                source.TakeFeedback();
                break;
            case AudioResampler resampler:
                resampler.Push(new AudioFrame(new short[480], 48000, 1, 480));
                break;
            case LocalTrack track:
                track.Mute();
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void Dispose_ReleasesTheHandleOnce_AndRepeatedDisposeIsHarmless(string kind)
    {
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;
        var failed = FfiHandle.FailedReleaseCount;

        var (wrapper, owner) = Create(kind);
        owner?.Dispose(); // a track keeps working without its source wrapper
        Assert.True(FfiHandle.LiveCount >= live + 1, $"{kind} wraps its native handle");
        Use(wrapper);

        wrapper.Dispose();
        var afterFirstDispose = FfiHandle.LiveCount;
        wrapper.Dispose();
        wrapper.Dispose();

        // A second release of the same handle would be rejected by the native library and counted
        Assert.True(afterFirstDispose <= live, $"{kind}.Dispose releases its handle");
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void UseAfterDispose_Throws_InsteadOfReachingTheFfiWithAReleasedHandle(string kind)
    {
        var (wrapper, owner) = Create(kind);
        owner?.Dispose();
        wrapper.Dispose();
        var failed = FfiHandle.FailedReleaseCount;

        // ObjectDisposedException derives from InvalidOperationException, which is what the SDK
        // raises when the FFI rejects a released handle: either is a safe failure. An
        // AudioSource drops a capture after disposal on purpose (documented), so it is not used.
        var error = Record.Exception(() => Use(wrapper));
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void ConcurrentDispose_ReleasesTheHandleExactlyOnce(string kind)
    {
        const int Threads = 8;
        var rounds = StressLevel.Scale(25, 250);
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;
        var failed = FfiHandle.FailedReleaseCount;

        for (var round = 0; round < rounds; round++)
        {
            var (wrapper, owner) = Create(kind);
            using var start = new Barrier(Threads);
            var disposers = Enumerable
                .Range(0, Threads)
                .Select(_ =>
                    Task.Factory.StartNew(
                        () =>
                        {
                            start.SignalAndWait();
                            wrapper.Dispose();
                        },
                        TaskCreationOptions.LongRunning
                    )
                )
                .ToArray();
            Task.WaitAll(disposers, TimeSpan.FromSeconds(10));
            owner?.Dispose();
        }

        Assert.True(FfiHandle.LiveCount <= live, "every concurrently disposed handle is released");
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void UndisposedWrappers_AreReleasedByTheFinalizer(string kind)
    {
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;
        var failed = FfiHandle.FailedReleaseCount;

        CreateAndAbandon(kind, StressLevel.Scale(50, 500));
        Assert.True(FfiHandle.LiveCount > live, "the abandoned wrappers still hold their handles");

        ResourceSnapshot.CollectGarbage();

        Assert.True(
            FfiHandle.LiveCount <= live,
            $"{FfiHandle.LiveCount - live} handle(s) of unreachable {kind} wrappers were not finalized"
        );
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    // Not inlined, so no reference to the wrappers survives on the caller's stack
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndAbandon(string kind, int count)
    {
        for (var i = 0; i < count; i++)
            Create(kind);
    }

    [Fact]
    public void CreatingATrackFromADisposedSource_FailsWithoutWrappingAnything()
    {
        // The FFI answers a request on a released handle with an error, not with a response: the
        // SDK must raise it and wrap nothing
        var audioSource = new AudioSource(48000, 1);
        var videoSource = new VideoSource(320, 240);
        audioSource.Dispose();
        videoSource.Dispose();
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;

        Assert.ThrowsAny<InvalidOperationException>(() =>
            LocalAudioTrack.Create("orphan", audioSource)
        );
        Assert.ThrowsAny<InvalidOperationException>(() => audioSource.CreateTrack());
        Assert.ThrowsAny<InvalidOperationException>(() =>
            LocalVideoTrack.Create("orphan", videoSource)
        );

        Assert.True(FfiHandle.LiveCount <= live, $"{FfiHandle.LiveCount - live} handle(s) leaked");
    }

    [Fact]
    public void ATrack_OutlivesItsSourceWrapper_AndASourceOutlivesItsTrack()
    {
        var failed = FfiHandle.FailedReleaseCount;

        // Track disposed after its source
        var source = new AudioSource(48000, 1);
        var track = LocalAudioTrack.Create("outlives-source", source);
        source.Dispose();
        track.Mute();
        Assert.True(track.IsMuted);
        track.Unmute();
        track.Dispose();

        // Source keeps capturing after its track is gone
        using var videoSource = new VideoSource(320, 240);
        var videoTrack = LocalVideoTrack.Create("outlived-by-source", videoSource);
        videoTrack.Dispose();
        videoSource.CaptureFrame(VideoFrame.Create(320, 240, VideoBufferType.I420));

        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    [Fact]
    public async Task AudioSource_DisposedWhileACaptureIsPending_EndsTheCaptureAndThePlayoutWait()
    {
        // A capture completes only once the native queue accepted the whole frame: a 3 s frame
        // into a 100 ms queue stays pending for seconds. Disposing meanwhile must neither hang
        // the capture nor the playout wait, and must release the handle once.
        var failed = FfiHandle.FailedReleaseCount;
        var source = new AudioSource(48000, 1, queueSizeMs: 100);
        var longFrame = new AudioFrame(new short[48000 * 3], 48000, 1, 48000 * 3);

        var capture = source.CaptureFrameAsync(longFrame); // the request is sent synchronously
        var playout = source.WaitForPlayoutAsync();
        Assert.False(capture.IsCompleted);

        source.Dispose();

        await playout.Within(TimeSpan.FromSeconds(2), "WaitForPlayoutAsync after Dispose");
        var error = await Record.ExceptionAsync(() =>
            capture.Within(TimeSpan.FromSeconds(20), "a capture pending during Dispose")
        );
        _output.WriteLine($"pending capture ended with: {error?.GetType().Name ?? "success"}");
        Assert.True(
            error == null || error is InvalidOperationException,
            $"unexpected outcome: {error}"
        );

        // Later captures are dropped (documented), not sent with the released handle
        await source.CaptureFrameAsync(new AudioFrame(new short[480], 48000, 1, 480));
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    [Fact]
    public async Task AudioSource_DisposedWithAudioQueued_NeitherHangsNorCrashes()
    {
        // The native source (webrtc-sys AudioTrackSource) frees its queue before it stops the
        // 10 ms task that plays the queue out, and without the lock that task holds. A release
        // that lands while the task erases the played samples leaves it spinning forever, and
        // Dispose, which waits for the task's thread, never returns (the soak's "'dispose the
        // audio track and source' did not complete within 30 s"), or lets it read the freed queue
        // and crash the process. The SDK empties the queue before it releases the source.
        var cycles = StressLevel.Scale(300, 2000);
        var failed = FfiHandle.FailedReleaseCount;
        // 10 s of stereo audio: each erase moves about 2 MB, which widens the window of the race
        const int QueueMs = 10000;
        const int Samples = 48 * (QueueMs - 100);
        var frame = new AudioFrame(new short[Samples * 2], 48000, 2, Samples);

        for (var i = 0; i < cycles; i++)
        {
            var source = new AudioSource(48000, 2, QueueMs);
            await source.CaptureFrameAsync(frame); // accepted at once: the queue has room for it
            await Task.Delay(i % 20); // releases at every phase of the 10 ms task
            await Task.Run(source.Dispose)
                .Within(TimeSpan.FromSeconds(10), $"Dispose with audio queued (cycle {i})");
        }
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    [Fact]
    public async Task AudioSource_CancelledCapture_NeverHangsTheNextCaptures_AndTheSourceRecovers()
    {
        // Cancelling stops the wait, not the native capture, and the native source refuses a new
        // capture while it still consumes the previous frame. The SDK must surface that refusal
        // instead of hanging, must not deliver the late callback of the cancelled capture to a
        // later one, and the source must work again once the frame is consumed.
        using var source = new AudioSource(48000, 1, queueSizeMs: 100);
        using var cts = new CancellationTokenSource();
        var cancelled = source.CaptureFrameAsync(
            new AudioFrame(new short[48000], 48000, 1, 48000), // 1 s
            cts.Token
        );
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        var tenMs = new AudioFrame(new short[480], 48000, 1, 480);
        var refused = 0;
        await Eventually.TrueAsync(
            async () =>
            {
                var error = await Record.ExceptionAsync(() =>
                    source
                        .CaptureFrameAsync(tenMs)
                        .Within(TimeSpan.FromSeconds(15), "a capture after a cancelled one")
                );
                if (error == null)
                    return true;
                Assert.IsType<InvalidOperationException>(error);
                refused++;
                return false;
            },
            TimeSpan.FromSeconds(10),
            "the source accepts captures again"
        );
        _output.WriteLine($"{refused} capture(s) refused while the cancelled frame drained");

        for (var i = 0; i < 20; i++)
            await source
                .CaptureFrameAsync(tenMs)
                .Within(TimeSpan.FromSeconds(15), $"capture {i} after recovery");
    }

    [Fact]
    public async Task AudioSource_AnAlreadyCancelledCapture_ThrowsWithoutReachingTheSource()
    {
        // Standard .NET practice: an already cancelled token throws before any side effect. Had the
        // 1 s frame reached the native source, the source would refuse the next captures while it
        // consumes it (see the test above)
        using var source = new AudioSource(48000, 1, queueSizeMs: 100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.CaptureFrameAsync(
                new AudioFrame(new short[48000], 48000, 1, 48000),
                new CancellationToken(canceled: true)
            )
        );

        Assert.Equal(0, source.QueuedDuration);
        await source
            .CaptureFrameAsync(new AudioFrame(new short[480], 48000, 1, 480))
            .Within(TimeSpan.FromSeconds(5), "a capture right after the cancelled one");
    }

    [Fact]
    public void VideoFrameConversions_ReleaseTheBufferTheFfiReturns()
    {
        // VideoFrame.Convert receives an owned native buffer, copies it and must release it
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;
        var failed = FfiHandle.FailedReleaseCount;
        var frame = VideoFrame.Create(320, 240, VideoBufferType.Rgba);

        for (var i = 0; i < 50; i++)
        {
            var i420 = frame.Convert(VideoBufferType.I420);
            var back = i420.Convert(VideoBufferType.Rgba);
            Assert.Equal(frame.DataBytes.Length, back.DataBytes.Length);
        }

        Assert.True(FfiHandle.LiveCount <= live, $"{FfiHandle.LiveCount - live} handle(s) leaked");
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    public static TheoryData<string> EventsWithOwnedHandles =>
        new()
        {
            "ParticipantConnected",
            "TrackPublished",
            "TrackSubscribed",
            "DataPacketReceived",
            "DataTrackPublished",
            "TextStreamOpened",
            "ByteStreamOpened",
        };

    private static RoomEvent EventCarrying(string kind, FfiOwnedHandle? handle) =>
        kind switch
        {
            "ParticipantConnected" => new RoomEvent
            {
                ParticipantConnected = new ParticipantConnected
                {
                    Info = handle == null ? null : new OwnedParticipant { Handle = handle },
                },
            },
            "TrackPublished" => new RoomEvent
            {
                TrackPublished = new TrackPublished
                {
                    Publication =
                        handle == null ? null : new OwnedTrackPublication { Handle = handle },
                },
            },
            "TrackSubscribed" => new RoomEvent
            {
                TrackSubscribed = new TrackSubscribed
                {
                    Track = handle == null ? null : new OwnedTrack { Handle = handle },
                },
            },
            "DataPacketReceived" => new RoomEvent
            {
                DataPacketReceived = new DataPacketReceived
                {
                    User =
                        handle == null
                            ? null
                            : new UserPacket { Data = new OwnedBuffer { Handle = handle } },
                },
            },
            "DataTrackPublished" => new RoomEvent
            {
                DataTrackPublished = new DataTrackPublished
                {
                    Track = handle == null ? null : new OwnedRemoteDataTrack { Handle = handle },
                },
            },
            "TextStreamOpened" => new RoomEvent
            {
                TextStreamOpened = new TextStreamOpened
                {
                    Reader = handle == null ? null : new OwnedTextStreamReader { Handle = handle },
                },
            },
            "ByteStreamOpened" => new RoomEvent
            {
                ByteStreamOpened = new ByteStreamOpened
                {
                    Reader = handle == null ? null : new OwnedByteStreamReader { Handle = handle },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    [Theory]
    [MemberData(nameof(EventsWithOwnedHandles))]
    public void RoomEventsTheRoomDoesNotApply_ReleaseTheHandleTheyCarry_ExactlyOnce(string kind)
    {
        // The room releases the native object an event hands over when it does not apply the
        // event (a connection that ended, a stream reader it does not use). Checked here with a
        // real native handle that no wrapper owns: once released, the native library no longer
        // knows it.
        var failed = FfiHandle.FailedReleaseCount;
        var created = FfiClient.Instance.SendRequest(
            new FfiRequest
            {
                NewAudioSource = new NewAudioSourceRequest
                {
                    Type = AudioSourceType.AudioSourceNative,
                    SampleRate = 48000,
                    NumChannels = 1,
                    QueueSizeMs = 100,
                },
            }
        );
        var handleId = created.NewAudioSource.Source.Handle.Id;

        Room.ReleaseOwnedHandles(EventCarrying(kind, new FfiOwnedHandle { Id = handleId }));

        Assert.False(NativeMethods.DropHandle(handleId), $"{kind}: the handle was not released");
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);

        // An event without its owned object releases nothing and does not throw
        Room.ReleaseOwnedHandles(EventCarrying(kind, null));
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }
}
