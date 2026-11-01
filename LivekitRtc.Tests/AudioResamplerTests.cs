// author: https://github.com/pabloFuente

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Loads the native library, needs no Docker.
/// </summary>
public class AudioResamplerTests
{
    [Fact]
    public void Flush_BeforeAnyPush_ReturnsNoFrames()
    {
        // Used to return a single frame with zero samples
        using var resampler = new AudioResampler(48000, 16000);
        Assert.Empty(resampler.Flush());
    }

    [Fact]
    public void Push_RejectsFramesOfAnotherFormat()
    {
        using var resampler = new AudioResampler(48000, 16000, numChannels: 1);
        // Stereo samples fed as mono used to come out as garbage of half the duration
        Assert.Throws<ArgumentException>(() =>
            resampler.Push(new AudioFrame(new short[960], 48000, 2, 480))
        );
        Assert.Throws<ArgumentException>(() =>
            resampler.Push(new AudioFrame(new short[441], 44100, 1, 441))
        );
    }

    [Fact]
    public void Push_ThenFlush_ResamplesTheWholeInput()
    {
        using var resampler = new AudioResampler(48000, 16000);
        var input = new short[4800]; // 100 ms
        for (var i = 0; i < input.Length; i++)
            input[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / 48000) * 8000);

        var frames = resampler.Push(new AudioFrame(input, 48000, 1, 4800));
        frames.AddRange(resampler.Flush());

        var samples = frames.Sum(f => f.SamplesPerChannel);
        Assert.InRange(samples, 1500, 1700); // 100 ms at 16 kHz, give or take filter delay
        Assert.All(frames, f => Assert.Equal(16000, f.SampleRate));
    }
}
