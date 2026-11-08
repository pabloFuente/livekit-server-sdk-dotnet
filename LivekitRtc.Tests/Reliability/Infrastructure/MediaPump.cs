// author: https://github.com/pabloFuente

using LiveKit.Proto;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Feeds a source in real time from a background task (10 ms audio frames, about 30 video frames
/// per second) until disposed, so subscribers keep receiving frames.
/// </summary>
internal sealed class MediaPump : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private MediaPump(Func<CancellationToken, Task> loop)
    {
        _loop = Task.Run(() => loop(_stop.Token));
    }

    public static MediaPump Audio(AudioSource source) =>
        new(async stop =>
        {
            var samples = source.SampleRate / 100;
            var frame = new AudioFrame(
                new short[samples * source.NumChannels],
                source.SampleRate,
                source.NumChannels,
                samples
            );
            for (var i = 0; i < samples; i++)
                frame.SetSample(
                    0,
                    i,
                    (short)(6000 * Math.Sin(2 * Math.PI * 440 * i / source.SampleRate))
                );
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await source.CaptureFrameAsync(frame, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });

    public static MediaPump Video(VideoSource source) =>
        new(async stop =>
        {
            var frame = VideoFrame.Create(source.Width, source.Height, VideoBufferType.I420);
            while (!stop.IsCancellationRequested)
            {
                source.CaptureFrame(frame);
                try
                {
                    await Task.Delay(33, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _loop;
        _stop.Dispose();
    }
}
