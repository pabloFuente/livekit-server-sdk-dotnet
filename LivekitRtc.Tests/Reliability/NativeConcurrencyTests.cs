// author: https://github.com/pabloFuente

using System.Collections.Concurrent;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// The .NET side of the FFI contract under concurrency, without a server: buffers handed to the
/// FFI stay pinned while it reads them, pointers the FFI returns are read only while their handle
/// is alive, synchronous wrappers do not deadlock on a UI-like synchronization context, and
/// independent wrappers can be used from many threads at once.
/// </summary>
[Collection(NativeReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class NativeConcurrencyTests
{
    private readonly ITestOutputHelper _output;

    public NativeConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
        FfiClient.Instance.Initialize();
    }

    [Fact]
    public void AudioResampler_DisposeDuringPush_OnlyEverThrowsObjectDisposedException()
    {
        // Push copies the output from a pointer into the native resampler's own buffer after the
        // request returned. A Dispose that released the handle in between freed that buffer
        // under the copy (a use-after-free), and a Dispose between the disposed check and the
        // request reached the FFI with a released handle ("FFI request failed: invalid handle").
        // Dispose must wait for an in-flight Push, and a Push after it must throw
        // ObjectDisposedException.
        var rounds = StressLevel.Scale(300, 3000);
        var input = new byte[1600 * 2]; // 100 ms of 16 kHz mono
        var unexpected = new ConcurrentQueue<Exception>();
        var pushes = 0L;
        var failed = FfiHandle.FailedReleaseCount;

        for (var round = 0; round < rounds; round++)
        {
            var resampler = new AudioResampler(16000, 48000);
            using var start = new Barrier(3);
            var pushers = Enumerable
                .Range(0, 2)
                .Select(_ =>
                    Task.Factory.StartNew(
                        () =>
                        {
                            start.SignalAndWait();
                            while (true)
                            {
                                try
                                {
                                    foreach (var frame in resampler.Push(input))
                                        Assert.Equal(48000, frame.SampleRate);
                                    Interlocked.Increment(ref pushes);
                                }
                                catch (ObjectDisposedException)
                                {
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    unexpected.Enqueue(ex);
                                    return;
                                }
                            }
                        },
                        TaskCreationOptions.LongRunning
                    )
                )
                .ToArray();

            start.SignalAndWait();
            Thread.SpinWait(Random.Shared.Next(0, 20000));
            resampler.Dispose();
            Assert.True(Task.WaitAll(pushers, TimeSpan.FromSeconds(10)), "pushers stopped");
        }

        _output.WriteLine($"{rounds} rounds, {pushes} pushes");
        Assert.True(
            unexpected.IsEmpty,
            $"{unexpected.Count} pushes failed with something else than ObjectDisposedException, first: {unexpected.FirstOrDefault()}"
        );
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }

    [Fact]
    public void AudioResampler_PushAndFlushFromManyThreads_KeepEveryCallConsistent()
    {
        // Documented as safe from several threads: the output pointer of one call must not be
        // overwritten by another call before it was copied
        using var resampler = new AudioResampler(48000, 48000);
        var input = new byte[480 * 2];
        var errors = new ConcurrentQueue<Exception>();
        var samplesOut = 0L;
        var calls = StressLevel.Scale(400, 4000);

        Parallel.For(
            0,
            calls,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i =>
            {
                try
                {
                    var frames = i % 10 == 9 ? resampler.Flush() : resampler.Push(input);
                    foreach (var frame in frames)
                    {
                        Assert.Equal(frame.SamplesPerChannel, frame.Data.Length);
                        Interlocked.Add(ref samplesOut, frame.SamplesPerChannel);
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            }
        );

        Assert.Empty(errors);
        Assert.True(samplesOut > 0);
    }

    [Fact]
    public void VideoFrameConvert_UnderCompactingGcPressure_ReadsTheRightPixels()
    {
        // End-to-end check of the pinning fixed in 0.1.5: if a compacting GC could move the
        // source pixels while the FFI reads them, conversions of the same frame would differ
        var frame = VideoFrame.Create(160, 120, VideoBufferType.Rgba);
        new Random(7).NextBytes(frame.DataBytes);
        var reference = frame.Convert(VideoBufferType.I420).DataBytes;
        var iterations = StressLevel.Scale(300, 3000);

        using var stop = new CancellationTokenSource();
        var pressure = StartGcPressure(stop.Token);
        try
        {
            Parallel.For(
                0,
                iterations,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                i =>
                {
                    // A fresh copy each time, so the source array is young and movable
                    var copy = new VideoFrame(
                        frame.Width,
                        frame.Height,
                        VideoBufferType.Rgba,
                        (byte[])frame.DataBytes.Clone()
                    );
                    var converted = copy.Convert(VideoBufferType.I420).DataBytes;
                    Assert.True(
                        converted.AsSpan().SequenceEqual(reference),
                        $"conversion {i} read other pixels than the frame's"
                    );
                }
            );
        }
        finally
        {
            stop.Cancel();
            pressure.Wait();
        }
    }

    [Fact]
    public void AudioResamplerPush_UnderCompactingGcPressure_ReadsTheRightSamples()
    {
        // Same check for the samples Push hands to the FFI. The resampler dithers its 16-bit
        // output, so runs differ by a unit or two; samples read from a moved buffer would not
        // resemble the input at all.
        const int Tolerance = 8;
        var input = new short[4800];
        for (var i = 0; i < input.Length; i++)
            input[i] = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / 48000.0));
        short[] Resample(short[] samples)
        {
            using var resampler = new AudioResampler(48000, 24000);
            var frame = new AudioFrame((short[])samples.Clone(), 48000, 1, samples.Length);
            return resampler
                .Push(frame)
                .Concat(resampler.Flush())
                .SelectMany(f => f.DataArray)
                .ToArray();
        }
        var reference = Resample(input);
        var iterations = StressLevel.Scale(200, 2000);

        using var stop = new CancellationTokenSource();
        var pressure = StartGcPressure(stop.Token);
        try
        {
            Parallel.For(
                0,
                iterations,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                i =>
                {
                    var output = Resample(input);
                    Assert.Equal(reference.Length, output.Length);
                    var worst = output.Zip(reference, (a, b) => Math.Abs(a - b)).Max();
                    Assert.True(
                        worst <= Tolerance,
                        $"resampling {i} differs by {worst} from the reference: it read other samples than the input's"
                    );
                }
            );
        }
        finally
        {
            stop.Cancel();
            pressure.Wait();
        }
    }

    private static Task StartGcPressure(CancellationToken stop) =>
        Task.Factory.StartNew(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var garbage = new byte[64 * 1024];
                    GC.KeepAlive(garbage);
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, compacting: true);
                    Thread.Sleep(1);
                }
            },
            TaskCreationOptions.LongRunning
        );

    [Fact]
    public void AudioSourceCaptureFrame_OnAUiLikeThread_DoesNotDeadlock()
    {
        // The synchronous CaptureFrame blocks on CaptureFrameAsync. If that resumes on the
        // captured synchronization context, a UI thread calling it waits for itself forever.
        using var source = new AudioSource(48000, 1);
        var frame = new AudioFrame(new short[480], 48000, 1, 480);

        var completed = SingleThreadSynchronizationContext.TryRunBlocking(
            () =>
            {
                for (var i = 0; i < 5; i++)
                    source.CaptureFrame(frame);
            },
            TimeSpan.FromSeconds(15),
            out var error,
            out var posted
        );

        Assert.True(
            completed,
            $"AudioSource.CaptureFrame deadlocked on a single-threaded synchronization context ({posted} continuation(s) were posted to the blocked thread)"
        );
        Assert.Null(error);
    }

    [Fact]
    public async Task IndependentWrappers_UsedFromManyThreadsAtOnce_ReleaseEveryHandle()
    {
        ResourceSnapshot.CollectGarbage();
        var live = FfiHandle.LiveCount;
        var failed = FfiHandle.FailedReleaseCount;
        var workers = StressLevel.Scale(16, 64);
        var cycles = StressLevel.Scale(10, 100);

        var tasks = Enumerable
            .Range(0, workers)
            .Select(w =>
                Task.Run(async () =>
                {
                    for (var c = 0; c < cycles; c++)
                    {
                        using var audio = new AudioSource(48000, 1, queueSizeMs: 50);
                        using var audioTrack = LocalAudioTrack.Create($"a{w}-{c}", audio);
                        using var video = new VideoSource(64, 48);
                        using var videoTrack = LocalVideoTrack.Create($"v{w}-{c}", video);
                        using var resampler = new AudioResampler(48000, 16000);
                        var frame = new AudioFrame(new short[480], 48000, 1, 480);
                        await audio.CaptureFrameAsync(frame);
                        video.CaptureFrame(VideoFrame.Create(64, 48, VideoBufferType.I420));
                        resampler.Push(frame);
                        audioTrack.Mute();
                    }
                })
            )
            .ToArray();
        await Task.WhenAll(tasks).Within(TimeSpan.FromMinutes(2), "the workers");

        ResourceSnapshot.CollectGarbage();
        Assert.True(FfiHandle.LiveCount <= live, $"{FfiHandle.LiveCount - live} handles leaked");
        Assert.Equal(failed, FfiHandle.FailedReleaseCount);
    }
}
