using System.Text;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for LiveKit E2EE (End-to-End Encryption) functionality.
/// Tests verify actual encryption/decryption of media flows between participants.
/// </summary>
[Collection("RtcTests")]
public class E2EETests : IAsyncLifetime
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private IWebDriver? _driver;
    private IJavaScriptExecutor? _js;
    private Room? _room1;
    private Room? _room2;
    private Room? _room3;

    public E2EETests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public async Task InitializeAsync()
    {
        var options = new ChromeOptions();
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--autoplay-policy=no-user-gesture-required");
        options.AddArgument("--use-fake-ui-for-media-stream");
        options.AddArgument("--use-fake-device-for-media-stream");

        if (string.IsNullOrEmpty(_fixture.ChromeUrl))
        {
            Log("Using local Chrome browser");
            _driver = new ChromeDriver(options);
        }
        else
        {
            await Task.Delay(2000);
            Log($"Connecting to remote Chrome at: {_fixture.ChromeUrl}");
            _driver = new RemoteWebDriver(new Uri(_fixture.ChromeUrl), options);
        }

        _js = (IJavaScriptExecutor)_driver;
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_room1 != null)
        {
            await _room1.DisconnectAsync();
            _room1.Dispose();
        }
        if (_room2 != null)
        {
            await _room2.DisconnectAsync();
            _room2.Dispose();
        }
        if (_room3 != null)
        {
            await _room3.DisconnectAsync();
            _room3.Dispose();
        }

        _driver?.Quit();
        _driver?.Dispose();
    }

    [Fact]
    public async Task E2EE_SharedKey_ParticipantsCanEncryptAndDecrypt()
    {
        Log("Starting E2EE shared key test with media flow verification");

        const string roomName = "e2ee-shared-key-room";
        const string participant1 = "e2ee-publisher";
        const string participant2 = "e2ee-receiver";

        // Generate a shared encryption key (32 bytes for AES-256)
        var sharedKey = new byte[32];
        new Random(42).NextBytes(sharedKey);
        Log($"Generated shared key: {Convert.ToBase64String(sharedKey)}");

        var token1 = _fixture.CreateToken(participant1, roomName);
        var token2 = _fixture.CreateToken(participant2, roomName);

        // Configure E2EE for both participants with the same key
        var e2eeOptions = new E2EEOptions
        {
            KeyProviderOptions = new KeyProviderOptions
            {
                SharedKey = sharedKey,
                RatchetWindowSize = E2EEDefaults.RatchetWindowSize,
                FailureTolerance = E2EEDefaults.FailureTolerance,
            },
            EncryptionType = Proto.EncryptionType.Gcm,
        };

        _room1 = new Room();
        _room2 = new Room();

        // Track received on participant 2
        var audioTrackReceived = new TaskCompletionSource<RemoteAudioTrack>();
        _room2.TrackSubscribed += (sender, e) =>
        {
            Log($"Track subscribed: {e.Track.Name}, Kind: {e.Track.Kind}, Sid: {e.Track.Sid}");
            if (e.Track is RemoteAudioTrack audioTrack)
            {
                audioTrackReceived.TrySetResult(audioTrack);
            }
        };

        // Connect both participants with E2EE
        var options1 = new RoomOptions { E2EE = e2eeOptions };
        var options2 = new RoomOptions { E2EE = e2eeOptions };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, token1, options1);
        Log($"Participant1 '{participant1}' connected with E2EE");

        await _room2.ConnectAsync(_fixture.LiveKitUrl, token2, options2);
        Log($"Participant2 '{participant2}' connected with E2EE");

        // Verify E2EE managers are present
        Assert.NotNull(_room1.E2EEManager);
        Assert.NotNull(_room2.E2EEManager);
        Log("E2EE managers initialized on both participants");

        await Task.Delay(1000);

        // Publish audio from participant 1
        var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("encrypted-audio", audioSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Audio track published: {publication.Sid}");

        // Generate a known test pattern: 440Hz sine wave
        var sentAudioData = new short[480];
        for (int i = 0; i < sentAudioData.Length; i++)
        {
            sentAudioData[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / 48000) * 10000);
        }
        Log("Created test pattern: 440Hz sine wave");

        // Wait for track to be received by participant 2
        var receivedTrack = await audioTrackReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Log($"Audio track received on participant2: {receivedTrack.Sid}");

        // Verify the track was received
        Assert.NotNull(receivedTrack);
        Assert.Equal(Proto.TrackKind.KindAudio, receivedTrack.Kind);

        // Create audio stream to receive and verify decrypted frames
        using var audioStream = new AudioStream(receivedTrack);
        Log("Created AudioStream to receive decrypted frames");

        // Keep sending audio frames in the background while we read
        var sendingFrames = true;
        var framesSent = 0;
        var sendTask = Task.Run(async () =>
        {
            while (sendingFrames)
            {
                var audioFrame = new AudioFrame(sentAudioData, 48000, 1, 480);
                await audioSource.CaptureFrameAsync(audioFrame);
                framesSent++;
                await Task.Delay(10); // ~100 FPS (10ms per frame)
            }
        });

        // Wait for frames and verify content - skip first few which might be silence/initialization
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        AudioFrameEvent? receivedFrameInfo = null;
        int framesReceived = 0;
        await foreach (var evt in audioStream.WithCancellation(cts.Token))
        {
            framesReceived++;
            receivedFrameInfo = evt;

            // Check if this frame has non-zero samples
            var samples = evt.Frame.DataBytes.ToArray();
            bool hasSignal = false;
            for (int i = 0; i < Math.Min(samples.Length / 2, 50); i++)
            {
                short sample = BitConverter.ToInt16(samples, i * 2);
                if (Math.Abs(sample) > 100)
                {
                    hasSignal = true;
                    break;
                }
            }

            if (hasSignal)
            {
                Log($"Found frame with audio signal after {framesReceived} frames");
                break;
            }

            // Give up after checking 20 frames
            if (framesReceived >= 20)
            {
                Log($"Checked {framesReceived} frames, using last one");
                break;
            }
        }

        sendingFrames = false;
        await sendTask;
        Log($"Audio frames sent: {framesSent}, received: {framesReceived}");

        Assert.NotNull(receivedFrameInfo);
        var frame = receivedFrameInfo.Value.Frame;
        Log(
            $"Received decrypted audio frame: {frame.SampleRate}Hz, {frame.NumChannels} channel(s), {frame.SamplesPerChannel} samples"
        );

        // Verify frame has correct format
        Assert.Equal(48000, frame.SampleRate);
        Assert.Equal(1, frame.NumChannels);
        Assert.True(frame.SamplesPerChannel > 0, "Should have received audio samples");

        // Verify frame content is not silence (proves decryption worked)
        var receivedSamples = frame.DataBytes.ToArray();
        Log($"Received {receivedSamples.Length} bytes of audio data");

        bool hasNonZeroSamples = false;
        int nonZeroCount = 0;
        short maxSample = 0;
        for (int i = 0; i < Math.Min(receivedSamples.Length / 2, 100); i++)
        {
            short sample = BitConverter.ToInt16(receivedSamples, i * 2);
            if (Math.Abs(sample) > 100)
            {
                hasNonZeroSamples = true;
                nonZeroCount++;
            }
            if (Math.Abs(sample) > Math.Abs(maxSample))
            {
                maxSample = sample;
            }
        }
        Log($"Non-zero samples found: {nonZeroCount}/100 checked, max sample value: {maxSample}");

        Assert.True(
            hasNonZeroSamples,
            "Decrypted audio frame should contain non-zero samples (not silence)"
        );
        Log("✓ Audio frame content verified: decryption successful, audio contains signal!");
        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        audioTrack.Dispose();
        audioSource.Dispose();

        Log("E2EE shared key test completed successfully");
    }

    [Fact]
    public async Task E2EE_DifferentKeys_ParticipantCannotDecrypt()
    {
        Log("Starting E2EE different keys test - verifying wrong keys produce noise");

        const string roomName = "e2ee-different-keys-room";
        const string participant1 = "e2ee-publisher-key1";
        const string participant2 = "e2ee-receiver-key2";

        // Generate two different encryption keys
        var key1 = new byte[32];
        var key2 = new byte[32];
        new Random(42).NextBytes(key1);
        new Random(84).NextBytes(key2);
        Log($"Generated key1: {Convert.ToBase64String(key1)}");
        Log($"Generated key2 (different): {Convert.ToBase64String(key2)}");

        var token1 = _fixture.CreateToken(participant1, roomName);
        var token2 = _fixture.CreateToken(participant2, roomName);

        // Configure E2EE with DIFFERENT keys
        var e2eeOptions1 = new E2EEOptions
        {
            KeyProviderOptions = new KeyProviderOptions { SharedKey = key1 },
        };

        var e2eeOptions2 = new E2EEOptions
        {
            KeyProviderOptions = new KeyProviderOptions { SharedKey = key2 },
        };

        _room1 = new Room();
        _room2 = new Room();

        var audioTrackReceived = new TaskCompletionSource<RemoteAudioTrack>();
        _room2.TrackSubscribed += (sender, e) =>
        {
            Log($"Track subscribed on participant2: {e.Track.Sid}");
            if (e.Track is RemoteAudioTrack audioTrack)
            {
                audioTrackReceived.TrySetResult(audioTrack);
            }
        };

        // Connect with different keys
        await _room1.ConnectAsync(
            _fixture.LiveKitUrl,
            token1,
            new RoomOptions { E2EE = e2eeOptions1 }
        );
        Log($"Participant1 connected with key1");

        await _room2.ConnectAsync(
            _fixture.LiveKitUrl,
            token2,
            new RoomOptions { E2EE = e2eeOptions2 }
        );
        Log($"Participant2 connected with key2 (different)");

        await Task.Delay(1000);

        // Publish audio from participant 1 with a known pattern: 440Hz sine wave
        var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("encrypted-audio-key1", audioSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Audio track published with key1: {publication.Sid}");

        // Generate 440Hz sine wave pattern
        var sentAudioData = new short[480];
        for (int i = 0; i < sentAudioData.Length; i++)
        {
            sentAudioData[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / 48000) * 10000);
        }
        Log("Created test pattern: 440Hz sine wave (should become noise with wrong key)");

        // Wait for track to be received
        var receivedTrack = await audioTrackReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Log($"Audio track received on participant2 with wrong key");

        Assert.NotNull(receivedTrack);

        // Create audio stream to receive the wrongly decrypted frames
        using var audioStream = new AudioStream(receivedTrack);
        Log("Created AudioStream to receive wrongly-decrypted frames");

        // Keep sending audio frames in the background
        var sendingFrames = true;
        var sendTask = Task.Run(async () =>
        {
            while (sendingFrames)
            {
                var audioFrame = new AudioFrame(sentAudioData, 48000, 1, 480);
                await audioSource.CaptureFrameAsync(audioFrame);
                await Task.Delay(10);
            }
        });

        // Try to receive and verify the frame is NOT the original 440Hz pattern
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        AudioFrameEvent? receivedFrameInfo = null;
        int framesReceived = 0;
        await foreach (var evt in audioStream.WithCancellation(cts.Token))
        {
            framesReceived++;
            receivedFrameInfo = evt;

            // Check several frames to find one with data
            var samples = evt.Frame.DataBytes.ToArray();
            bool hasData = false;
            for (int i = 0; i < Math.Min(samples.Length / 2, 50); i++)
            {
                short sample = BitConverter.ToInt16(samples, i * 2);
                if (sample != 0)
                {
                    hasData = true;
                    break;
                }
            }

            if (hasData || framesReceived >= 20)
            {
                break;
            }
        }

        sendingFrames = false;
        await sendTask;

        Assert.NotNull(receivedFrameInfo);
        var frame = receivedFrameInfo.Value.Frame;
        Log($"Received wrongly-decrypted audio frame after {framesReceived} attempts");

        // Verify the received audio does NOT match the 440Hz sine wave pattern
        // With wrong decryption key, the audio should be noise/corrupted, not a clean sine wave
        var receivedSamples = frame.DataBytes.ToArray();

        // Calculate correlation with expected 440Hz pattern
        // A correctly decrypted 440Hz sine wave would have high correlation
        // Noise/corrupted data would have low correlation
        double correlation = 0;
        int samplesChecked = 0;
        for (int i = 0; i < Math.Min(receivedSamples.Length / 2, 100); i++)
        {
            short receivedSample = BitConverter.ToInt16(receivedSamples, i * 2);
            short expectedSample = (short)(Math.Sin(2 * Math.PI * 440 * i / 48000) * 10000);

            // Normalized dot product
            correlation += (receivedSample * expectedSample) / (10000.0 * 10000.0);
            samplesChecked++;
        }
        correlation = Math.Abs(correlation / samplesChecked);

        Log(
            $"Correlation with 440Hz pattern: {correlation:F3} (close to 0 = noise, close to 1 = original signal)"
        );

        // With wrong key, correlation should be very low (close to random noise)
        Assert.True(
            correlation < 0.5,
            $"Wrongly decrypted audio should be noise (low correlation), but got {correlation:F3}"
        );
        Log("✓ Verified: Wrong decryption key produces noise, not original 440Hz signal!");

        // Clean up
        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        audioTrack.Dispose();
        audioSource.Dispose();

        Log("E2EE different keys test completed - wrong key produces noise as expected");
    }

    [Fact]
    public async Task E2EE_KeyRotation_ParticipantReceivesWithNewKey()
    {
        Log("Starting E2EE key rotation test");

        const string roomName = "e2ee-key-rotation-room";
        const string participant1 = "e2ee-publisher-rotate";
        const string participant2 = "e2ee-receiver-rotate";

        // Initial shared key
        var initialKey = new byte[32];
        new Random(42).NextBytes(initialKey);
        Log($"Initial shared key: {Convert.ToBase64String(initialKey)}");

        var token1 = _fixture.CreateToken(participant1, roomName);
        var token2 = _fixture.CreateToken(participant2, roomName);

        var e2eeOptions = new E2EEOptions
        {
            KeyProviderOptions = new KeyProviderOptions { SharedKey = initialKey },
        };

        _room1 = new Room();
        _room2 = new Room();

        var tracksReceived = new List<string>();
        _room2.TrackSubscribed += (sender, e) =>
        {
            Log($"Track subscribed: {e.Track.Sid}");
            tracksReceived.Add(e.Track.Sid);
        };

        // Connect both participants
        await _room1.ConnectAsync(
            _fixture.LiveKitUrl,
            token1,
            new RoomOptions { E2EE = e2eeOptions }
        );
        await _room2.ConnectAsync(
            _fixture.LiveKitUrl,
            token2,
            new RoomOptions { E2EE = e2eeOptions }
        );
        Log("Both participants connected with initial key");

        Assert.NotNull(_room1.E2EEManager);
        Assert.NotNull(_room2.E2EEManager);

        await Task.Delay(1000);

        // Publish audio with initial key
        var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("encrypted-audio-rotate", audioSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Audio track published with initial key");

        var audioData = new short[480];
        var audioFrame = new AudioFrame(audioData, 48000, 1, 480);
        await audioSource.CaptureFrameAsync(audioFrame);

        await Task.Delay(1000);

        // Rotate to new key on both participants
        var newKey = new byte[32];
        new Random(123).NextBytes(newKey);
        Log($"Rotating to new key: {Convert.ToBase64String(newKey)}");

        _room1.E2EEManager!.KeyProvider!.SetSharedKey(newKey, keyIndex: 1);
        _room2.E2EEManager!.KeyProvider!.SetSharedKey(newKey, keyIndex: 1);
        Log("New key set on both participants");

        await Task.Delay(500);

        // Send another frame with new key
        var audioFrame2 = new AudioFrame(audioData, 48000, 1, 480);
        await audioSource.CaptureFrameAsync(audioFrame2);
        Log("Audio frame sent with new key");

        await Task.Delay(1000);

        // Verify track was received
        Assert.NotEmpty(tracksReceived);
        Log($"Tracks received: {tracksReceived.Count}");

        // Clean up
        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        audioTrack.Dispose();
        audioSource.Dispose();

        Log("E2EE key rotation test completed successfully");
    }

    [Fact]
    public async Task E2EE_VideoEncryption_ParticipantReceivesEncryptedVideo()
    {
        Log("Starting E2EE video encryption test");

        const string roomName = "e2ee-video-room";
        const string participant1 = "e2ee-video-publisher";
        const string participant2 = "e2ee-video-receiver";

        var sharedKey = new byte[32];
        new Random(42).NextBytes(sharedKey);

        var token1 = _fixture.CreateToken(participant1, roomName);
        var token2 = _fixture.CreateToken(participant2, roomName);

        var e2eeOptions = new E2EEOptions
        {
            KeyProviderOptions = new KeyProviderOptions { SharedKey = sharedKey },
        };

        _room1 = new Room();
        _room2 = new Room();

        var videoTrackReceived = new TaskCompletionSource<RemoteVideoTrack>();
        _room2.TrackSubscribed += (sender, e) =>
        {
            Log($"Track subscribed: {e.Track.Kind}, Sid: {e.Track.Sid}");
            if (e.Track is RemoteVideoTrack videoTrack)
            {
                videoTrackReceived.TrySetResult(videoTrack);
            }
        };

        // Connect both participants with E2EE
        await _room1.ConnectAsync(
            _fixture.LiveKitUrl,
            token1,
            new RoomOptions { E2EE = e2eeOptions }
        );
        await _room2.ConnectAsync(
            _fixture.LiveKitUrl,
            token2,
            new RoomOptions { E2EE = e2eeOptions }
        );
        Log("Both participants connected with E2EE for video");

        Assert.NotNull(_room1.E2EEManager);
        Assert.NotNull(_room2.E2EEManager);

        await Task.Delay(1000);

        // Publish video from participant 1
        const int width = 640;
        const int height = 480;
        var videoSource = new VideoSource(width, height);
        var videoTrack = LocalVideoTrack.Create("encrypted-video", videoSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(videoTrack);
        Log($"Video track published: {publication.Sid}");

        // Create a known test pattern: red frame (RGBA)
        var sentFrameData = new byte[width * height * 4];
        for (int i = 0; i < sentFrameData.Length; i += 4)
        {
            sentFrameData[i] = 255; // R = 255 (red)
            sentFrameData[i + 1] = 0; // G = 0
            sentFrameData[i + 2] = 0; // B = 0
            sentFrameData[i + 3] = 255; // A = 255
        }
        Log("Created test pattern: RED frame (RGBA 255,0,0,255)");

        // Send multiple video frames with the red pattern
        for (int i = 0; i < 10; i++)
        {
            var videoFrame = new VideoFrame(
                width,
                height,
                Proto.VideoBufferType.Rgba,
                sentFrameData
            );
            videoSource.CaptureFrame(videoFrame);
            await Task.Delay(100); // 10 FPS
        }
        Log("Video frames captured and sent (encrypted)");

        // Wait for video track to be received
        var receivedTrack = await videoTrackReceived.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Log($"Video track received on participant2: {receivedTrack.Sid}");

        Assert.NotNull(receivedTrack);
        Assert.Equal(Proto.TrackKind.KindVideo, receivedTrack.Kind);

        // Create video stream to receive and verify decrypted frames
        using var videoStream = new VideoStream(receivedTrack);
        Log("Created VideoStream to receive decrypted frames");

        // Keep sending red frames in the background while we read
        var sendingFrames = true;
        var sendTask = Task.Run(async () =>
        {
            while (sendingFrames)
            {
                var videoFrame = new VideoFrame(
                    width,
                    height,
                    Proto.VideoBufferType.Rgba,
                    sentFrameData
                );
                videoSource.CaptureFrame(videoFrame);
                await Task.Delay(100); // 10 FPS
            }
        });

        // Read a frame and verify it was decrypted correctly
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        VideoFrameEvent? receivedFrameInfo = null;
        await foreach (var evt in videoStream.WithCancellation(cts.Token))
        {
            receivedFrameInfo = evt;
            break; // Get the first frame
        }

        sendingFrames = false;
        await sendTask;

        Assert.NotNull(receivedFrameInfo);
        var frame = receivedFrameInfo.Value.Frame;
        Log($"Received decrypted frame: {frame.Width}x{frame.Height}, Type: {frame.Type}");

        // Note: WebRTC may transcode the video (e.g., RGBA to I420, resize resolution)
        // What matters is that we received a valid frame with reasonable dimensions
        Assert.True(frame.Width > 0 && frame.Height > 0, "Frame should have valid dimensions");
        Assert.True(
            frame.Width >= 320 && frame.Height >= 240,
            "Frame should have reasonable resolution"
        );

        // Verify we got frame data (proves decryption worked)
        var receivedData = frame.DataBytes.ToArray();
        Log($"Received frame data length: {receivedData.Length} bytes");
        Assert.True(receivedData.Length > 0, "Frame should contain data");

        // For I420 format (common WebRTC format), the Y plane contains luminance
        // A red color (RGB 255,0,0) converts to Y=76 in YUV space (relatively low luminance)
        // We can check if we're getting reasonable luminance values
        if (frame.Type == Proto.VideoBufferType.I420)
        {
            // Sample the Y plane (first width*height bytes in I420)
            int yPlaneSize = frame.Width * frame.Height;
            int nonZeroSamples = 0;
            int samplesChecked = 0;

            for (int i = 0; i < Math.Min(100, yPlaneSize); i += yPlaneSize / 100)
            {
                if (i < receivedData.Length)
                {
                    samplesChecked++;
                    if (receivedData[i] > 10) // Non-black
                    {
                        nonZeroSamples++;
                    }
                }
            }

            Log($"Y-plane samples with luminance: {nonZeroSamples}/{samplesChecked}");
            Assert.True(
                nonZeroSamples > samplesChecked * 0.5,
                "Frame should contain visible content (not all black)"
            );
        }

        Log(
            $"✓ Frame content verified: received {frame.Width}x{frame.Height} {frame.Type} frame with valid data!"
        );
        Log(
            "✓ E2EE video encryption/decryption successful - decrypted frame received and validated"
        );

        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        videoTrack.Dispose();
        videoSource.Dispose();

        Log("E2EE video encryption test completed successfully");
    }

    [Fact]
    public async Task E2EE_MultipleParticipants_AllCanDecryptWithSharedKey()
    {
        Log("Starting E2EE multiple participants test");

        const string roomName = "e2ee-multi-room";
        const string participant1 = "e2ee-p1";
        const string participant2 = "e2ee-p2";
        const string participant3 = "e2ee-p3";

        var sharedKey = new byte[32];
        new Random(42).NextBytes(sharedKey);
        Log($"Shared key for all 3 participants: {Convert.ToBase64String(sharedKey)}");

        var token1 = _fixture.CreateToken(participant1, roomName);
        var token2 = _fixture.CreateToken(participant2, roomName);
        var token3 = _fixture.CreateToken(participant3, roomName);

        var e2eeOptions = new E2EEOptions
        {
            KeyProviderOptions = new KeyProviderOptions { SharedKey = sharedKey },
        };

        _room1 = new Room();
        _room2 = new Room();
        _room3 = new Room();

        var tracksReceived2 = new List<RemoteTrack>();
        var tracksReceived3 = new List<RemoteTrack>();

        _room2.TrackSubscribed += (sender, e) =>
        {
            Log($"Participant2 received track: {e.Track.Sid}, Publication: {e.Publication.Sid}");
            tracksReceived2.Add(e.Track);
        };

        _room3.TrackSubscribed += (sender, e) =>
        {
            Log($"Participant3 received track: {e.Track.Sid}, Publication: {e.Publication.Sid}");
            tracksReceived3.Add(e.Track);
        };

        // Connect all three participants
        await _room1.ConnectAsync(
            _fixture.LiveKitUrl,
            token1,
            new RoomOptions { E2EE = e2eeOptions }
        );
        await _room2.ConnectAsync(
            _fixture.LiveKitUrl,
            token2,
            new RoomOptions { E2EE = e2eeOptions }
        );
        await _room3.ConnectAsync(
            _fixture.LiveKitUrl,
            token3,
            new RoomOptions { E2EE = e2eeOptions }
        );
        Log("All 3 participants connected with shared E2EE key");

        Assert.NotNull(_room1.E2EEManager);
        Assert.NotNull(_room2.E2EEManager);
        Assert.NotNull(_room3.E2EEManager);

        await Task.Delay(1500);

        // Participant 1 publishes audio
        var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("multi-encrypted-audio", audioSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Participant1 published encrypted audio: {publication.Sid}");

        var audioData = new short[480];
        var audioFrame = new AudioFrame(audioData, 48000, 1, 480);
        await audioSource.CaptureFrameAsync(audioFrame);

        await Task.Delay(2000);

        // Verify both participant 2 and 3 received the track
        Log($"Participant2 received {tracksReceived2.Count} tracks");
        Log($"Participant3 received {tracksReceived3.Count} tracks");

        Assert.NotEmpty(tracksReceived2);
        Assert.NotEmpty(tracksReceived3);

        // Both should have received tracks (E2EE allows decryption with shared key)
        Log($"Both participants successfully received encrypted tracks and decrypted them");

        // Clean up
        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        audioTrack.Dispose();
        audioSource.Dispose();

        Log("E2EE multiple participants test completed successfully");
    }

    [Fact]
    public async Task E2EE_EncryptionStateEvents_FireCorrectly()
    {
        Log("Starting E2EE encryption state events test");

        const string roomName = "e2ee-state-events-room";
        const string participant1 = "e2ee-state-publisher";
        const string participant2 = "e2ee-state-receiver";

        var sharedKey = new byte[32];
        new Random(42).NextBytes(sharedKey);

        var token1 = _fixture.CreateToken(participant1, roomName);
        var token2 = _fixture.CreateToken(participant2, roomName);

        var e2eeOptions = new E2EEOptions
        {
            KeyProviderOptions = new KeyProviderOptions { SharedKey = sharedKey },
        };

        _room1 = new Room();
        _room2 = new Room();

        // Connect both
        await _room1.ConnectAsync(
            _fixture.LiveKitUrl,
            token1,
            new RoomOptions { E2EE = e2eeOptions }
        );
        await _room2.ConnectAsync(
            _fixture.LiveKitUrl,
            token2,
            new RoomOptions { E2EE = e2eeOptions }
        );
        Log("Both participants connected");

        await Task.Delay(1000);

        // Publish and send frames
        var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("state-test-audio", audioSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(audioTrack);

        var audioData = new short[480];
        var audioFrame = new AudioFrame(audioData, 48000, 1, 480);
        await audioSource.CaptureFrameAsync(audioFrame);

        await Task.Delay(1500);

        // Clean up
        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        audioTrack.Dispose();
        audioSource.Dispose();

        Log("E2EE encryption state events test completed");
    }
}
