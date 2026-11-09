// author: https://github.com/pabloFuente

using System.Runtime.CompilerServices;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Repeated lifecycles of the resource wrappers must leave the process where it was. After a
/// warm-up (lazy native initialization, JIT), each scenario runs many cycles and checks that the
/// SDK released every handle it wrapped, plus, more coarsely, that the native heap, the open
/// descriptors and the threads did not grow with the cycle count: an owned buffer the SDK received
/// but never released shows up there even though no wrapper counts it.
/// </summary>
[Collection(NativeReliabilityCollection.Name)]
[Trait("Category", "Reliability")]
public class NativeResourceLeakTests
{
    private readonly ITestOutputHelper _output;

    public NativeResourceLeakTests(ITestOutputHelper output)
    {
        _output = output;
        FfiClient.Instance.Initialize();
    }

    public static TheoryData<string> Scenarios =>
        new()
        {
            "audio source, track and captures",
            "video source, track and captures",
            "resampler pushes and flush",
            "video frame conversions",
            "wrappers left to the finalizer",
        };

    private static async Task RunCycleAsync(string scenario, int cycle)
    {
        switch (scenario)
        {
            case "audio source, track and captures":
            {
                using var source = new AudioSource(48000, 2);
                using var track = LocalAudioTrack.Create("leak-audio", source);
                var frame = new AudioFrame(new short[960], 48000, 2, 480);
                for (var i = 0; i < 3; i++)
                    await source.CaptureFrameAsync(frame);
                source.ClearQueue();
                break;
            }
            case "video source, track and captures":
            {
                using var source = new VideoSource(640, 480);
                using var track = LocalVideoTrack.Create("leak-video", source);
                var frame = VideoFrame.Create(640, 480, VideoBufferType.I420);
                for (var i = 0; i < 3; i++)
                    source.CaptureFrame(frame);
                break;
            }
            case "resampler pushes and flush":
            {
                using var resampler = new AudioResampler(48000, 16000, 2);
                var frame = new AudioFrame(new short[9600], 48000, 2, 4800);
                for (var i = 0; i < 3; i++)
                    resampler.Push(frame);
                resampler.Flush();
                break;
            }
            case "video frame conversions":
            {
                // 1280x720: an unreleased output buffer costs 1.4 MB per conversion
                var frame = VideoFrame.Create(1280, 720, VideoBufferType.Rgba);
                frame.Convert(VideoBufferType.I420).Convert(VideoBufferType.Rgba);
                break;
            }
            case "wrappers left to the finalizer":
                Abandon();
                // An abandoned AudioSource keeps a native thread ticking every 10 ms until its
                // wrapper is finalized, and a cycle allocates too little to trigger a collection:
                // where collections are rare, thousands of those threads piled up and the
                // scenario took up to half an hour (Windows runners). Collecting now and then
                // bounds them and still leaves the release to the finalizer.
                if (cycle % 50 == 49)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Abandon()
    {
        var source = new AudioSource(48000, 1);
        LocalAudioTrack.Create("abandoned", source);
        new VideoSource(320, 240);
        new AudioResampler(48000, 16000);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task RepeatedCycles_ReleaseEveryHandle_AndDoNotGrowTheProcess(string scenario)
    {
        var cycles = StressLevel.Scale(200, 3000);
        var log = SdkLog.BeginScope();

        for (var i = 0; i < 20; i++)
            await RunCycleAsync(scenario, i);
        var baseline = ResourceSnapshot.Capture();

        for (var i = 0; i < cycles; i++)
            await RunCycleAsync(scenario, i);

        await ResourceSnapshot.AssertWithinAsync(
            baseline,
            new LeakBudget(),
            _output,
            $"{cycles} cycles of {scenario} ({NativeHeap.Source})"
        );
        log.AssertNoProblems();
    }
}
