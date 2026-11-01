// author: https://github.com/pabloFuente

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Playout bookkeeping of <see cref="AudioSource"/>. Loads the native library, needs no Docker.
/// </summary>
public class AudioSourcePlayoutTests
{
    private static AudioFrame Tone(int samples)
    {
        var data = new short[samples];
        for (var i = 0; i < samples; i++)
            data[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / 48000) * 8000);
        return new AudioFrame(data, 48000, 1, samples);
    }

    [Fact(Timeout = 15000)]
    public async Task WaitForPlayoutAsync_CompletesWhenAnotherCaptureLandsMeanwhile()
    {
        // A capture used to replace the pending playout task, stranding earlier waiters
        using var source = new AudioSource(48000, 1, queueSizeMs: 1000);
        await source.CaptureFrameAsync(Tone(48000)); // 1 s

        var wait = source.WaitForPlayoutAsync();
        await Task.Delay(300);
        await source.CaptureFrameAsync(Tone(480)); // 10 ms, lands while the first drains

        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0.0, source.QueuedDuration, 1);
    }

    [Fact(Timeout = 15000)]
    public async Task WaitForPlayoutAsync_CancellationAffectsOnlyThatWaiter()
    {
        using var source = new AudioSource(48000, 1, queueSizeMs: 1000);
        await source.CaptureFrameAsync(Tone(48000));

        using var cts = new CancellationTokenSource(100);
        var cancelled = source.WaitForPlayoutAsync(cts.Token);
        var other = source.WaitForPlayoutAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await other.WaitAsync(TimeSpan.FromSeconds(5)); // used to be cancelled too
    }

    [Fact(Timeout = 15000)]
    public async Task Dispose_ReleasesPendingPlayoutWaiters()
    {
        var source = new AudioSource(48000, 1, queueSizeMs: 1000);
        await source.CaptureFrameAsync(Tone(48000));
        var wait = source.WaitForPlayoutAsync();

        source.Dispose();

        await wait.WaitAsync(TimeSpan.FromSeconds(2)); // used to hang
    }

    [Fact(Timeout = 60000)]
    public async Task CaptureFrameAsync_AcceptsFramesLongerThanTenSeconds()
    {
        // The callback only arrives once the frame fits the 1 s native queue, so a 12 s frame
        // takes about 11 s to be accepted: it used to hit a fixed 10 s timeout
        using var source = new AudioSource(48000, 1, queueSizeMs: 1000);
        await source.CaptureFrameAsync(Tone(48000 * 12));
    }
}
