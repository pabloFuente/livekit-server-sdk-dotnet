using System.Threading.Channels;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// <see cref="AudioMixer"/> with in-memory sources. Pure managed code: no native library, no Docker.
/// </summary>
public class AudioMixerTests
{
    private const int SampleRate = 48000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Properties_DefaultTheBlockToATenthOfASecond()
    {
        using var mixer = new AudioMixer(SampleRate, 2);

        Assert.Equal((uint)SampleRate, mixer.SampleRate);
        Assert.Equal(2u, mixer.NumChannels);
        Assert.Equal(SampleRate / 10, mixer.ChunkSize);
    }

    [Fact]
    public async Task MixesTheStreamsSampleBySample_AndClipsToInt16()
    {
        await using var mixer = new AudioMixer(SampleRate, 1, blockSize: 4, streamTimeoutMs: 200);
        var second = new FakeSource();
        // The first source only answers once the mixer reads both in the same round, so a
        // round that started between the two AddStream calls cannot mix it alone
        var first = new FakeSource(gate: second.FirstRead.Task);
        first.Write(Frame(1000, -1000, 30000, -30000));
        second.Write(Frame(2000, -500, 10000, -10000));

        mixer.AddStream(first);
        mixer.AddStream(second);
        var mixed = await mixer.ReadAsync().AsTask().WaitAsync(Timeout);

        Assert.NotNull(mixed);
        Assert.Equal(new short[] { 3000, -1500, 32767, -32768 }, mixed.DataArray);
        Assert.Equal(SampleRate, mixed.SampleRate);
        Assert.Equal(1, mixed.NumChannels);
        Assert.Equal(4, mixed.SamplesPerChannel);
    }

    [Fact]
    public async Task AnExhaustedStreamIsRemoved_AndEndInputCompletesTheOutput()
    {
        await using var mixer = new AudioMixer(SampleRate, 1, blockSize: 4);
        var source = new FakeSource();
        // One block and a half: the last block is padded with silence
        source.Write(Frame(1, 2, 3, 4, 5, 6));
        source.End();

        mixer.AddStream(source);
        mixer.EndInput();
        var frames = new List<AudioFrame>();
        using var cts = new CancellationTokenSource(Timeout);
        await foreach (var frame in mixer.WithCancellation(cts.Token))
            frames.Add(frame);

        Assert.False(cts.IsCancellationRequested, "the output never completed");
        Assert.Equal(2, frames.Count);
        Assert.Equal(new short[] { 1, 2, 3, 4 }, frames[0].DataArray);
        Assert.Equal(new short[] { 5, 6, 0, 0 }, frames[1].DataArray);
        Assert.Equal(0, mixer.StreamCount);
        Assert.Throws<InvalidOperationException>(() => mixer.AddStream(new FakeSource()));
    }

    [Fact]
    public async Task AStalledStreamThatReturnsNullOnCancellation_StaysInTheMix()
    {
        // AudioStream.ReadAsync returns null when cancelled: on a timeout that means "no frame
        // yet", not "finished"
        await using var mixer = new AudioMixer(SampleRate, 1, blockSize: 2, streamTimeoutMs: 20);
        var source = new FakeSource(nullOnCancellation: true);
        mixer.AddStream(source);

        await Task.Delay(300);
        Assert.Equal(1, mixer.StreamCount);

        source.Write(Frame(7, 8));
        var frame = await mixer.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.NotNull(frame);
        Assert.Equal(new short[] { 7, 8 }, frame.DataArray);
    }

    [Fact]
    public async Task AddAndRemoveStream_TrackTheSources()
    {
        await using var mixer = new AudioMixer(SampleRate, 1, blockSize: 2);
        var source = new FakeSource();

        mixer.AddStream(source);
        mixer.AddStream(source);
        Assert.Equal(1, mixer.StreamCount);

        mixer.RemoveStream(source);
        Assert.Equal(0, mixer.StreamCount);
        Assert.False(mixer.TryRead(out var none));
        Assert.Null(none);
    }

    [Fact]
    public async Task TryRead_ReturnsAMixedFrameOnceAvailable()
    {
        await using var mixer = new AudioMixer(SampleRate, 1, blockSize: 2);
        var source = new FakeSource();
        source.Write(Frame(4, 5));
        mixer.AddStream(source);

        AudioFrame? frame = null;
        var deadline = DateTime.UtcNow + Timeout;
        while (!mixer.TryRead(out frame) && DateTime.UtcNow < deadline)
            await Task.Delay(5);

        Assert.NotNull(frame);
        Assert.Equal(new short[] { 4, 5 }, frame.DataArray);
    }

    [Fact]
    public async Task Dispose_EndsAPendingRead_AndLaterReadsThrow()
    {
        var mixer = new AudioMixer(SampleRate, 1, blockSize: 2);
        var pending = mixer.ReadAsync().AsTask();

        mixer.Dispose();
        mixer.Dispose();

        Assert.Null(await pending.WaitAsync(Timeout));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => mixer.ReadAsync().AsTask());
    }

    [Fact]
    public async Task DisposeAsync_WaitsForTheMixingLoop()
    {
        var mixer = new AudioMixer(SampleRate, 1, blockSize: 2);
        var source = new FakeSource();
        mixer.AddStream(source);

        await mixer.DisposeAsync().AsTask().WaitAsync(Timeout);
        await mixer.DisposeAsync();

        // The loop is gone: a frame written now is never read from the source
        source.Write(Frame(1, 2));
        Assert.Equal(1, source.Pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => mixer.ReadAsync().AsTask());
    }

    private static AudioFrame Frame(params short[] samples) =>
        new AudioFrame(samples, SampleRate, 1, samples.Length);

    private sealed class FakeSource : IAudioStreamSource
    {
        private readonly Channel<AudioFrame> _frames = Channel.CreateUnbounded<AudioFrame>();
        private readonly Task _gate;
        private readonly bool _nullOnCancellation;

        public FakeSource(Task? gate = null, bool nullOnCancellation = false)
        {
            _gate = gate ?? Task.CompletedTask;
            _nullOnCancellation = nullOnCancellation;
        }

        public TaskCompletionSource FirstRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Pending => _frames.Reader.Count;

        public void Write(AudioFrame frame) => _frames.Writer.TryWrite(frame);

        public void End() => _frames.Writer.TryComplete();

        public async ValueTask<AudioFrame?> ReadAsync(CancellationToken cancellationToken = default)
        {
            FirstRead.TrySetResult();
            try
            {
                await _gate.WaitAsync(cancellationToken);
                return await _frames.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
            catch (OperationCanceledException) when (_nullOnCancellation)
            {
                return null;
            }
        }
    }
}
