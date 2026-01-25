using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc;

var url = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("LIVEKIT_URL");
var token = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("LIVEKIT_TOKEN");

if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
{
    Console.WriteLine("Usage: dotnet run <LIVEKIT_URL> <ACCESS_TOKEN>");
    return 1;
}

Console.WriteLine("=== LiveKit RTC - Publish Audio & Video Example ===\n");
Console.WriteLine("Publishing 440Hz audio and colored video continuously.");
Console.WriteLine("Press Ctrl+C to stop.\n");

var room = new Room();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nStopping publication...");
};

try
{
    await room.ConnectAsync(url, token);
    Console.WriteLine($"✓ Connected to room: {room.Name}\n");

    // Create and publish audio track
    const int sampleRate = 48000;
    const int channels = 1;
    var audioSource = new AudioSource(sampleRate, channels);
    var audioTrack = LocalAudioTrack.Create("sine-wave", audioSource);
    var audioPublication = await room.LocalParticipant!.PublishTrackAsync(audioTrack);
    Console.WriteLine($"✓ Published audio track: {audioPublication.Sid}");

    // Create and publish video track
    const int width = 640;
    const int height = 480;
    var videoSource = new VideoSource(width, height);
    var videoTrack = LocalVideoTrack.Create("color-frames", videoSource);
    var videoPublication = await room.LocalParticipant!.PublishTrackAsync(videoTrack);
    Console.WriteLine($"✓ Published video track: {videoPublication.Sid}\n");

    Console.WriteLine("Publishing audio (440Hz) and video (cycling colors)...\n");

    // Audio parameters
    const int frequency = 440; // A4 note
    const int framesPerSecond = 100; // 10ms frames
    const int samplesPerFrame = sampleRate / framesPerSecond;
    var audioData = new short[samplesPerFrame];

    // Video parameters
    const int fps = 30;
    var videoData = new byte[width * height * 4]; // RGBA
    var colors = new[]
    {
        (R: 255, G: 0, B: 0, Name: "Red"),
        (R: 0, G: 255, B: 0, Name: "Green"),
        (R: 0, G: 0, B: 255, Name: "Blue"),
    };

    // Shared frame counters
    int audioFrameCount = 0;
    int videoFrameCount = 0;

    // Start audio publishing task
    var audioTask = Task.Run(async () =>
    {
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // Generate sine wave samples
                int sampleOffset = audioFrameCount * samplesPerFrame;
                for (int i = 0; i < samplesPerFrame; i++)
                {
                    double time = (sampleOffset + i) / (double)sampleRate;
                    audioData[i] = (short)(Math.Sin(2 * Math.PI * frequency * time) * 10000);
                }

                // Capture audio frame
                var audioFrame = new AudioFrame(audioData, sampleRate, channels, samplesPerFrame);
                await audioSource.CaptureFrameAsync(audioFrame);

                Interlocked.Increment(ref audioFrameCount);
                await Task.Delay(10, cts.Token); // 10ms per frame
            }
        }
        catch (OperationCanceledException) { }
    });

    // Start video publishing task
    var videoTask = Task.Run(async () =>
    {
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                int currentCount = videoFrameCount;
                // Cycle through colors every second
                var colorIndex = (currentCount / fps) % colors.Length;
                var color = colors[colorIndex];

                // Fill frame with solid color
                for (int i = 0; i < videoData.Length; i += 4)
                {
                    videoData[i] = (byte)color.R; // R
                    videoData[i + 1] = (byte)color.G; // G
                    videoData[i + 2] = (byte)color.B; // B
                    videoData[i + 3] = 255; // A
                }

                // Capture video frame
                var videoFrame = new VideoFrame(width, height, VideoBufferType.Rgba, videoData);
                videoSource.CaptureFrame(videoFrame);

                int newCount = Interlocked.Increment(ref videoFrameCount);

                // Log progress every second
                if (newCount % fps == 0)
                {
                    int seconds = newCount / fps;
                    int audioCount = Interlocked.CompareExchange(ref audioFrameCount, 0, 0); // Read safely
                    Console.WriteLine(
                        $"  {seconds}s - Video: {color.Name}, Audio: {audioCount} frames"
                    );
                }

                await Task.Delay(1000 / fps, cts.Token); // Maintain target FPS
            }
        }
        catch (OperationCanceledException) { }
    });

    // Wait for both tasks to complete (when cancelled)
    await Task.WhenAll(audioTask, videoTask);

    Console.WriteLine("\n✓ Stopped publishing");

    // Cleanup
    await room.LocalParticipant!.UnpublishTrackAsync(audioPublication.Sid);
    await room.LocalParticipant!.UnpublishTrackAsync(videoPublication.Sid);
    audioTrack.Dispose();
    audioSource.Dispose();
    videoTrack.Dispose();
    videoSource.Dispose();

    await room.DisconnectAsync();
    Console.WriteLine("✓ Disconnected");

    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("✓ Cancelled");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
    return 1;
}
finally
{
    room.Dispose();
}
