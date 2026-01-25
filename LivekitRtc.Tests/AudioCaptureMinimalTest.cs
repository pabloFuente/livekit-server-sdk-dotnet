using System;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

public class AudioCaptureMinimalTest
{
    private readonly ITestOutputHelper _output;

    public AudioCaptureMinimalTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 30000)]
    public async Task CaptureFrame_WithoutPublishing_ShouldReceiveCallback()
    {
        _output.WriteLine("Creating audio source...");
        using var audioSource = new AudioSource(48000, 1);

        _output.WriteLine("Creating audio frame...");
        // Create a buffer for 10ms of 48kHz mono audio (480 samples)
        short[] samples = new short[480];

        // Fill with a simple sine wave at 440Hz (A4 note)
        for (int i = 0; i < 480; i++)
        {
            samples[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / 48000) * 16000);
        }

        var frame = new AudioFrame(samples, 48000, 1, 480);

        _output.WriteLine("Attempting to capture frame...");
        try
        {
            await audioSource.CaptureFrameAsync(frame);
            _output.WriteLine("SUCCESS: Frame captured!");
        }
        catch (TimeoutException ex)
        {
            _output.WriteLine($"TIMEOUT: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}
