using System.Text;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// The managed helpers of <see cref="AudioFrame"/>. Pure managed code: no native library, no Docker.
/// </summary>
public class AudioFrameTests
{
    [Fact]
    public void Constructors_RejectBuffersShorterThanTheFrame()
    {
        Assert.Throws<ArgumentException>(() => new AudioFrame(new short[3], 48000, 2, 2));
        Assert.Throws<ArgumentException>(() => new AudioFrame(new byte[7], 48000, 2, 2));
    }

    [Fact]
    public void ByteConstructor_RejectsAnOddByteCount_AndCopiesLittleEndianSamples()
    {
        Assert.Throws<ArgumentException>(() => new AudioFrame(new byte[5], 48000, 1, 2));

        var bytes = new byte[] { 0x01, 0x00, 0xFF, 0xFF };
        var frame = new AudioFrame(bytes, 48000, 1, 2);
        bytes[0] = 0x7F;

        Assert.Equal(new short[] { 1, -1 }, frame.DataArray);
    }

    [Fact]
    public void Samples_AreInterleavedByChannel()
    {
        // Stereo, three samples per channel: L0 R0 L1 R1 L2 R2
        var frame = new AudioFrame(new short[] { 10, 20, 11, 21, 12, 22 }, 48000, 2, 3);

        Assert.Equal(11, frame.GetSample(0, 1));
        Assert.Equal(22, frame.GetSample(1, 2));

        frame.SetSample(1, 0, -5);
        frame.DataMutable[4] = 99;
        Assert.Equal(-5, frame.GetSample(1, 0));
        Assert.Equal(99, frame.GetSample(0, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => frame.GetSample(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.GetSample(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.SetSample(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.SetSample(0, -1, 0));
    }

    [Fact]
    public void DurationAndToString_DescribeTheFrame()
    {
        var frame = new AudioFrame(new short[960], 48000, 2, 480);

        Assert.Equal(0.01, frame.Duration, 6);
        Assert.Equal(
            "AudioFrame(sampleRate=48000, numChannels=2, samplesPerChannel=480, duration=0.010s)",
            frame.ToString()
        );
    }

    [Fact]
    public void Clone_CopiesTheSamplesAndTheUserdata()
    {
        var userdata = new Dictionary<string, object> { ["source"] = "mic" };
        var frame = new AudioFrame(new short[] { 1, 2, 3, 4 }, 16000, 1, 4, userdata);

        var clone = frame.Clone();
        frame.SetSample(0, 0, 100);
        frame.Userdata["source"] = "changed";

        Assert.Equal(new short[] { 1, 2, 3, 4 }, clone.DataArray);
        Assert.Equal("mic", clone.Userdata["source"]);
        Assert.Equal(16000, clone.SampleRate);
        Assert.Equal(1, clone.NumChannels);
        Assert.Equal(4, clone.SamplesPerChannel);
    }

    [Fact]
    public void ToWavBytes_WritesAPcm16HeaderFollowedByTheSamples()
    {
        var samples = new short[] { 1, -1, 300, -300 };
        var frame = new AudioFrame(samples, 16000, 2, 2);

        var wav = frame.ToWavBytes();

        Assert.Equal(44 + samples.Length * 2, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20)); // PCM
        Assert.Equal(2, BitConverter.ToInt16(wav, 22)); // channels
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24)); // sample rate
        Assert.Equal(16000 * 2 * 2, BitConverter.ToInt32(wav, 28)); // byte rate
        Assert.Equal(4, BitConverter.ToInt16(wav, 32)); // block align
        Assert.Equal(16, BitConverter.ToInt16(wav, 34)); // bits per sample
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(samples.Length * 2, BitConverter.ToInt32(wav, 40));
        for (int i = 0; i < samples.Length; i++)
            Assert.Equal(samples[i], BitConverter.ToInt16(wav, 44 + i * 2));
    }

    [Fact]
    public void Combine_ConcatenatesFramesInOrder()
    {
        var first = new AudioFrame(new short[] { 1, 2, 3, 4 }, 48000, 2, 2);
        // A buffer longer than the frame: only the frame's samples are combined
        var second = new AudioFrame(new short[] { 5, 6, 7, 7 }, 48000, 2, 1);

        var combined = new[] { first, second }.Combine();

        Assert.Equal(new short[] { 1, 2, 3, 4, 5, 6 }, combined.DataArray);
        Assert.Equal(3, combined.SamplesPerChannel);
        Assert.Equal(2, combined.NumChannels);
        Assert.Equal(48000, combined.SampleRate);
    }

    [Fact]
    public void Combine_OfOneFrame_ReturnsACopy()
    {
        var frame = new AudioFrame(new short[] { 1, 2 }, 48000, 1, 2);

        var combined = new[] { frame }.Combine();
        frame.SetSample(0, 0, 9);

        Assert.Equal(new short[] { 1, 2 }, combined.DataArray);
    }

    [Fact]
    public void Combine_RejectsNoFramesAndMismatchedFormats()
    {
        var mono48 = new AudioFrame(new short[2], 48000, 1, 2);

        Assert.Throws<ArgumentException>(() => Array.Empty<AudioFrame>().Combine());
        Assert.Throws<ArgumentException>(() =>
            new[] { mono48, new AudioFrame(new short[2], 16000, 1, 2) }.Combine()
        );
        Assert.Throws<ArgumentException>(() =>
            new[] { mono48, new AudioFrame(new short[4], 48000, 2, 2) }.Combine()
        );
    }
}
