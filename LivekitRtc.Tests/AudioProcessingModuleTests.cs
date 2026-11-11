// author: https://github.com/pabloFuente

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Loads the native library, needs no Docker.
/// </summary>
public class AudioProcessingModuleTests
{
    private const int SampleRate = 48000;
    private const int SamplesPer10Ms = SampleRate / 100;

    private static AudioFrame ConstantFrame(short value, int periods = 1)
    {
        var data = new short[SamplesPer10Ms * periods];
        Array.Fill(data, value);
        return new AudioFrame(data, SampleRate, 1, data.Length);
    }

    [Fact]
    public void ProcessStream_WritesTheProcessedAudioIntoTheFrame()
    {
        // The high-pass filter removes a constant (DC) offset: once it settled, the samples the
        // module wrote back into the managed buffer are close to zero
        using var apm = new AudioProcessingModule(highPassFilter: true);
        AudioFrame frame = ConstantFrame(8000);
        for (var i = 0; i < 100; i++)
        {
            frame = ConstantFrame(8000);
            apm.ProcessStream(frame);
        }

        var meanAbs = frame.DataArray.Average(s => Math.Abs((int)s));
        Assert.True(meanAbs < 800, $"Mean absolute sample after the filter: {meanAbs}");
    }

    [Fact]
    public void ProcessReverseStream_AndSetStreamDelayMs_AcceptValidInput()
    {
        using var apm = new AudioProcessingModule(echoCancellation: true);
        apm.SetStreamDelayMs(20);

        var far = ConstantFrame(1000);
        var near = ConstantFrame(1000, periods: 2); // a whole number of 10 ms periods is accepted
        apm.ProcessReverseStream(far);
        apm.ProcessStream(near);
    }

    [Fact]
    public void Process_ProcessesOnlyTheSamplesOfTheFrame()
    {
        // A backing array longer than the frame: what follows the frame is not audio
        using var apm = new AudioProcessingModule(highPassFilter: true);
        var data = new short[SamplesPer10Ms + 100];
        Array.Fill(data, (short)8000);
        for (var i = 0; i < 50; i++)
        {
            Array.Fill(data, (short)8000, SamplesPer10Ms, 100);
            apm.ProcessStream(new AudioFrame(data, SampleRate, 1, SamplesPer10Ms));
        }

        Assert.All(data.Skip(SamplesPer10Ms), sample => Assert.Equal(8000, sample));
    }

    [Fact]
    public void Process_RejectsFramesThatAreNotAWholeNumberOf10MsPeriods()
    {
        // The native module would abort the whole process on these
        using var apm = new AudioProcessingModule(noiseSuppression: true);
        Assert.Throws<ArgumentException>(() =>
            apm.ProcessStream(new AudioFrame(new short[480], SampleRate, 1, 479))
        );
        Assert.Throws<ArgumentException>(() =>
            apm.ProcessReverseStream(new AudioFrame(new short[700], SampleRate, 1, 700))
        );
        Assert.Throws<ArgumentException>(() =>
            apm.ProcessStream(new AudioFrame(new short[480], 50, 1, 480))
        );
        Assert.Throws<ArgumentException>(() =>
            apm.ProcessStream(new AudioFrame(Array.Empty<short>(), SampleRate, 1, 0))
        );
        Assert.Throws<ArgumentNullException>(() => apm.ProcessStream(null!));
    }

    [Fact]
    public void Dispose_ThenUse_Throws()
    {
        var apm = new AudioProcessingModule(autoGainControl: true);
        apm.Dispose();
        apm.Dispose();

        Assert.Throws<ObjectDisposedException>(() => apm.ProcessStream(ConstantFrame(0)));
        Assert.Throws<ObjectDisposedException>(() => apm.ProcessReverseStream(ConstantFrame(0)));
        Assert.Throws<ObjectDisposedException>(() => apm.SetStreamDelayMs(10));
    }
}
