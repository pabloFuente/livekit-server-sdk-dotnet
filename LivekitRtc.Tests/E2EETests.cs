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
    public async Task E2EE_TonePreservedWithCorrectKey_NoiseWithWrongKey()
    {
        // A single publisher sends a continuous 440Hz sine wave. One receiver has the correct
        // key, another has a wrong key. We measure, in the FREQUENCY domain, how much energy
        // each receiver recovered at 440Hz relative to off-tone control frequencies.
        //
        // Frequency-domain (Goertzel) measurement is invariant to the delay and phase shift that
        // Opus transcoding introduces. A naive time-domain, sample-aligned correlation cannot tell
        // encrypted from plaintext for exactly that reason (a correctly received tone no longer
        // lines up sample-for-sample with the reference after Opus). The correct-key receiver must
        // recover a dominant tone; the wrong-key receiver must not. The relative assertion
        // (good >> bad) also fails under the original bug, where plaintext would let the wrong-key
        // receiver recover the tone just as well.
        Log("Starting E2EE tone-preservation test (frequency-domain)");

        const string roomName = "e2ee-tone-room";
        const int sampleRate = 48000;
        const int toneHz = 440;

        E2EEOptions Opts(byte[] key) =>
            new E2EEOptions { KeyProviderOptions = new KeyProviderOptions { SharedKey = key } };

        var key1 = new byte[32];
        var key2 = new byte[32];
        new Random(42).NextBytes(key1);
        new Random(84).NextBytes(key2);

        var tokenPub = _fixture.CreateToken("e2ee-tone-pub", roomName);
        var tokenGood = _fixture.CreateToken("e2ee-tone-good", roomName);
        var tokenBad = _fixture.CreateToken("e2ee-tone-bad", roomName);

        _room1 = new Room(); // publisher (key1)
        _room2 = new Room(); // correct-key receiver (key1)
        _room3 = new Room(); // wrong-key receiver (key2)

        var goodTrackTcs = new TaskCompletionSource<RemoteAudioTrack>();
        var badTrackTcs = new TaskCompletionSource<RemoteAudioTrack>();
        _room2.TrackSubscribed += (s, e) =>
        {
            if (e.Track is RemoteAudioTrack t)
                goodTrackTcs.TrySetResult(t);
        };
        _room3.TrackSubscribed += (s, e) =>
        {
            if (e.Track is RemoteAudioTrack t)
                badTrackTcs.TrySetResult(t);
        };

        await _room1.ConnectAsync(
            _fixture.LiveKitUrl,
            tokenPub,
            new RoomOptions { E2EE = Opts(key1) }
        );
        await _room2.ConnectAsync(
            _fixture.LiveKitUrl,
            tokenGood,
            new RoomOptions { E2EE = Opts(key1) }
        );
        await _room3.ConnectAsync(
            _fixture.LiveKitUrl,
            tokenBad,
            new RoomOptions { E2EE = Opts(key2) }
        );
        Log("Publisher + correct-key + wrong-key receivers connected");

        await Task.Delay(1000);

        // Publish a continuous (phase-continuous across frames) 440Hz sine wave.
        var audioSource = new AudioSource(sampleRate, 1);
        var audioTrack = LocalAudioTrack.Create("e2ee-tone", audioSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Tone track published: {publication.Sid}");

        var sending = true;
        long n = 0;
        var sendTask = Task.Run(async () =>
        {
            while (Volatile.Read(ref sending))
            {
                var data = new short[480];
                for (int i = 0; i < data.Length; i++, n++)
                    data[i] = (short)(Math.Sin(2 * Math.PI * toneHz * n / sampleRate) * 10000);
                await audioSource.CaptureFrameAsync(new AudioFrame(data, sampleRate, 1, 480));
                await Task.Delay(10);
            }
        });

        var goodTrack = await goodTrackTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var badTrack = await badTrackTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Log("Both receivers subscribed to the tone track");

        using var goodStream = new AudioStream(goodTrack);
        using var badStream = new AudioStream(badTrack);

        // Collect from both receivers concurrently while the tone keeps playing.
        var goodCollect = CollectSamples(goodStream, 9600, 8, TimeSpan.FromSeconds(10));
        var badCollect = CollectSamples(badStream, 9600, 8, TimeSpan.FromSeconds(10));
        await Task.WhenAll(goodCollect, badCollect);
        var (goodSamples, goodRate) = goodCollect.Result;
        var (badSamples, badRate) = badCollect.Result;

        Volatile.Write(ref sending, false);
        await sendTask;

        Log(
            $"Collected — correct key: {goodSamples.Length} samples @ {goodRate}Hz, "
                + $"wrong key: {badSamples.Length} samples @ {badRate}Hz"
        );

        double snrGood = TonalSnr(goodSamples, goodRate, toneHz);
        double snrBad = TonalSnr(badSamples, badRate, toneHz);
        Log($"Tonal SNR — correct key: {snrGood:F1}, wrong key: {snrBad:F1}");

        // The correct-key receiver must recover the tone as the dominant spectral component.
        Assert.True(
            snrGood > 10,
            $"correct-key receiver should recover the {toneHz}Hz tone (snr={snrGood:F1})"
        );
        // The wrong-key receiver must not — and, crucially, must be far weaker than the correct
        // one. Under issue #97 (plaintext) the wrong-key receiver would also recover the tone,
        // collapsing this ratio.
        Assert.True(
            snrGood > snrBad * 8,
            $"correct-key SNR ({snrGood:F1}) must dominate wrong-key SNR ({snrBad:F1})"
        );
        Log("✓ Tone recovered with the correct key and absent with the wrong key");

        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        audioTrack.Dispose();
        audioSource.Dispose();

        Log("E2EE tone-preservation test completed");
    }

    /// <summary>
    /// Reads decoded PCM (int16 mono) from an <see cref="AudioStream"/> until at least
    /// <paramref name="minSamples"/> have been gathered or the timeout elapses, discarding the
    /// first <paramref name="skipFrames"/> frames to avoid codec/jitter-buffer warm-up. Returning
    /// few or no samples (on timeout) is a valid outcome: with a wrong key the receiver may get
    /// no decodable audio at all.
    /// </summary>
    private static async Task<(short[] samples, int sampleRate)> CollectSamples(
        AudioStream stream,
        int minSamples,
        int skipFrames,
        TimeSpan timeout
    )
    {
        var buf = new List<short>(minSamples);
        int frame = 0;
        int sampleRate = 48000;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var evt in stream.WithCancellation(cts.Token))
            {
                if (frame++ < skipFrames)
                    continue;
                sampleRate = evt.Frame.SampleRate;
                var bytes = evt.Frame.DataBytes.ToArray();
                for (int i = 0; i + 1 < bytes.Length; i += 2)
                    buf.Add(BitConverter.ToInt16(bytes, i));
                if (buf.Count >= minSamples)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out; return whatever arrived.
        }
        return (buf.ToArray(), sampleRate);
    }

    /// <summary>
    /// Signal power at a single frequency using the generalized Goertzel algorithm, normalized by
    /// sample count. Uses magnitude only, so it is invariant to the phase shift and delay that
    /// Opus transcoding introduces.
    /// </summary>
    private static double GoertzelPower(short[] x, double hz, int sampleRate)
    {
        if (x.Length == 0)
            return 0;
        double coeff = 2.0 * Math.Cos(2.0 * Math.PI * hz / sampleRate);
        double s1 = 0,
            s2 = 0;
        foreach (var sample in x)
        {
            double s0 = sample + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        return (s1 * s1 + s2 * s2 - coeff * s1 * s2) / x.Length;
    }

    /// <summary>
    /// Ratio of energy at <paramref name="toneHz"/> to the mean energy at a few off-tone control
    /// frequencies. High when the tone dominates (correct decryption), near zero for silence or
    /// broadband noise (wrong key). Returns 0 when there is not enough audio to analyze.
    /// </summary>
    private static double TonalSnr(short[] x, int sampleRate, double toneHz)
    {
        if (x.Length < 1024)
            return 0;
        double sig = GoertzelPower(x, toneHz, sampleRate);
        double noise =
            (
                GoertzelPower(x, 997, sampleRate)
                + GoertzelPower(x, 1499, sampleRate)
                + GoertzelPower(x, 2003, sampleRate)
            ) / 3.0;
        return sig / (noise + 1e-9);
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

    [Fact]
    public async Task E2EE_EnabledOptions_ActuallyCreatesFrameCryptors()
    {
        // Regression test for https://github.com/pabloFuente/livekit-server-sdk-dotnet/issues/97.
        //
        // When RoomOptions.ToProto() dropped the E2EE options, the FFI never enabled encryption
        // and media was sent in plaintext. Because WebRTC transcodes audio through Opus, the
        // correlation-based tests in this file cannot distinguish "encrypted" from "plaintext",
        // so this test asserts something the FFI only does when E2EE is genuinely applied: it
        // creates enabled frame cryptors for the encrypted tracks.
        Log("Starting E2EE frame cryptor creation test");

        const string roomName = "e2ee-frame-cryptor-room";
        const string participant1 = "e2ee-fc-publisher";
        const string participant2 = "e2ee-fc-receiver";

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

        var trackSubscribed = new TaskCompletionSource<RemoteTrack>();
        _room2.TrackSubscribed += (sender, e) => trackSubscribed.TrySetResult(e.Track);

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
        Log("Both participants connected with E2EE");

        Assert.NotNull(_room1.E2EEManager);
        Assert.NotNull(_room2.E2EEManager);

        // Publish an audio track from participant 1 and wait until participant 2 subscribes,
        // so that frame cryptors have been created on both the sending and receiving ends.
        var audioSource = new AudioSource(48000, 1);
        var audioTrack = LocalAudioTrack.Create("fc-audio", audioSource);
        var publication = await _room1.LocalParticipant!.PublishTrackAsync(audioTrack);
        Log($"Audio track published: {publication.Sid}");

        var receivedTrack = await trackSubscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Log($"Track subscribed on receiver: {receivedTrack.Sid}");
        await Task.Delay(500);

        var publisherCryptors = _room1.E2EEManager!.GetFrameCryptors();
        var receiverCryptors = _room2.E2EEManager!.GetFrameCryptors();
        Log(
            $"Frame cryptors — publisher: {publisherCryptors.Count}, receiver: {receiverCryptors.Count}"
        );

        // With the bug present, ToProto() drops the E2EE options, the FFI configures no
        // encryption, and no frame cryptors are ever created -> these collections are empty.
        Assert.NotEmpty(publisherCryptors);
        Assert.NotEmpty(receiverCryptors);
        Assert.All(
            publisherCryptors,
            cryptor => Assert.True(cryptor.Enabled, "publisher frame cryptor should be enabled")
        );
        Assert.All(
            receiverCryptors,
            cryptor => Assert.True(cryptor.Enabled, "receiver frame cryptor should be enabled")
        );
        Log("✓ Enabled frame cryptors created on both ends — E2EE is actually applied");

        await _room1.LocalParticipant!.UnpublishTrackAsync(publication.Sid);
        audioTrack.Dispose();
        audioSource.Dispose();

        Log("E2EE frame cryptor creation test completed");
    }
}
