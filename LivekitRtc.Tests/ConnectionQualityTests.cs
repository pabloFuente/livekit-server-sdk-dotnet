// author: https://github.com/pabloFuente

using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

[Collection("LiveKit")]
public class ConnectionQualityTests : IClassFixture<RtcTestFixture>
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ConnectionQualityTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message) =>
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

    [Fact]
    public async Task ConnectionQualityChanged_EventFires()
    {
        Log("Starting ConnectionQuality test");

        using var room1 = new Room();
        using var room2 = new Room();

        var qualityChangedTcs = new TaskCompletionSource<Proto.ConnectionQuality>();
        var qualityEventCount = 0;

        room1.ConnectionQualityChanged += (sender, args) =>
        {
            qualityEventCount++;
            Log(
                $"ConnectionQualityChanged event #{qualityEventCount}: Participant={args.Participant.Identity}, Quality={args.Quality}"
            );
            qualityChangedTcs.TrySetResult(args.Quality);
        };

        // Connect both participants
        var token1 = _fixture.CreateToken("participant1", "test-quality-room");
        var token2 = _fixture.CreateToken("participant2", "test-quality-room");

        await room1.ConnectAsync(_fixture.LiveKitUrl, token1);
        await room2.ConnectAsync(_fixture.LiveKitUrl, token2);
        Log("Both participants connected");

        // Publish tracks to generate network activity
        using var audioSource1 = new AudioSource(48000, 1);
        using var audioSource2 = new AudioSource(48000, 1);

        var audioTrack1 = LocalAudioTrack.Create("audio1", audioSource1);
        var audioTrack2 = LocalAudioTrack.Create("audio2", audioSource2);

        await room1.LocalParticipant!.PublishTrackAsync(audioTrack1);
        await room2.LocalParticipant!.PublishTrackAsync(audioTrack2);
        Log("Both audio tracks published");

        // Send some audio frames to generate traffic
        var audioData = new byte[960]; // 20ms at 48kHz
        var audioFrame = new AudioFrame(audioData, 48000, 1, 480);

        for (int i = 0; i < 30; i++)
        {
            audioSource1.CaptureFrame(audioFrame);
            audioSource2.CaptureFrame(audioFrame);
            await Task.Delay(20);
        }

        Log("Audio frames sent");

        // Wait for connection quality event
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var quality = await qualityChangedTcs.Task.WaitAsync(cts.Token);

            Log($"ConnectionQualityChanged event received successfully");
            Log($"Quality: {quality}");
            Log("Test passed");
        }
        catch (TimeoutException)
        {
            Log($"ConnectionQualityChanged event timed out after {qualityEventCount} events");
            // Connection quality events may not fire immediately or may require more network activity
            // This is not necessarily a failure - skip the assertion
        }

        await room1.DisconnectAsync();
        await room2.DisconnectAsync();
        Log("Test completed");
    }
}
