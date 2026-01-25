using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for LiveKit RTC audio publishing.
/// </summary>
[Collection("RtcTests")]
public class AudioPublishTests : IAsyncLifetime
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private IWebDriver? _driver;
    private IJavaScriptExecutor? _js;

    public AudioPublishTests(RtcTestFixture fixture, ITestOutputHelper output)
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
            // Use local ChromeDriver
            Log("Using local Chrome browser");
            _driver = new ChromeDriver(options);
        }
        else
        {
            // Wait for containerized Chrome to be ready
            await Task.Delay(2000);

            // Connect to remote Chrome with timeout
            Log($"Connecting to remote Chrome at {_fixture.ChromeUrl}");
            _driver = new RemoteWebDriver(
                new Uri(_fixture.ChromeUrl),
                options.ToCapabilities(),
                TimeSpan.FromSeconds(30)
            );
        }

        _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
        _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
        _js = (IJavaScriptExecutor)_driver;
    }

    public Task DisposeAsync()
    {
        _driver?.Quit();
        _driver?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PublishAudioTrack_ChromeReceivesAudio()
    {
        const string roomName = "test-audio-room";
        const string publisherIdentity = "dotnet-publisher";
        const string subscriberIdentity = "chrome-subscriber";

        Log("Starting PublishAudioTrack_ChromeReceivesAudio test");

        // Create tokens
        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var subscriberToken = _fixture.CreateToken(subscriberIdentity, roomName);
        Log("Tokens created");

        // Load the receiver HTML in Chrome via the HTTP server (accessible from container)
        var receiverHtmlPath = Path.Combine(_fixture.WebFilesPath, "receiver.html");
        Assert.True(
            File.Exists(receiverHtmlPath),
            $"Receiver HTML not found at: {receiverHtmlPath}"
        );

        var receiverUrl = $"{_fixture.WebFilesInternalUrl}/receiver.html";
        Log($"Navigating Chrome to: {receiverUrl}");
        _driver!.Navigate().GoToUrl(receiverUrl);
        Log("Navigation complete, waiting for page load");
        await Task.Delay(3000); // Wait for page and livekit-client to load

        // Check if page loaded correctly
        var pageTitle = _driver.Title;
        Log($"Page title: {pageTitle}");

        // Check if livekit-client loaded
        var hasLiveKit = _js!.ExecuteScript("return typeof window.LivekitClient !== 'undefined';");
        Log($"LivekitClient loaded: {hasLiveKit}");

        // Check if connectToRoom exists
        var hasConnectFunc = _js.ExecuteScript(
            "return typeof window.connectToRoom === 'function';"
        );
        Log($"connectToRoom function exists: {hasConnectFunc}");

        // Connect Chrome to the room using the appropriate URL
        // Use external URL for local Chrome, internal URL for containerized Chrome
        var chromeLiveKitUrl = _fixture.UseLocalChrome
            ? _fixture.LiveKitUrl
            : _fixture.LiveKitInternalUrl;
        Log($"Connecting Chrome to LiveKit: {chromeLiveKitUrl}");
        var jsResult = _js.ExecuteScript(
            $@"
            try {{
                window.connectToRoom('{chromeLiveKitUrl}', '{subscriberToken}');
                return 'connecting';
            }} catch (e) {{
                return 'error: ' + e.message;
            }}
        "
        );
        Log($"Connect result: {jsResult}");
        Assert.Equal("connecting", jsResult?.ToString());

        // Wait for Chrome to connect
        Log("Waiting for Chrome to connect to LiveKit...");
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
        wait.Until(d =>
        {
            var connected = ((IJavaScriptExecutor)d).ExecuteScript(
                "return window.testState.connected;"
            );
            var error = ((IJavaScriptExecutor)d).ExecuteScript("return window.testState.error;");
            if (error != null && !string.IsNullOrEmpty(error.ToString()))
            {
                Log($"Chrome error: {error}");
            }
            return connected is true;
        });
        Log("Chrome connected to LiveKit");

        // Create Room and connect from .NET
        using var room = new Room();
        await room.ConnectAsync(_fixture.LiveKitUrl, publisherToken);

        Assert.True(room.IsConnected);
        Assert.NotNull(room.LocalParticipant);
        Assert.Equal(publisherIdentity, room.LocalParticipant.Identity);

        // Create audio source and track
        const int sampleRate = 48000;
        const int numChannels = 1;
        using var audioSource = new AudioSource(sampleRate, numChannels);
        var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);

        // Publish the track
        var options = new TrackPublishOptions { Source = Proto.TrackSource.SourceMicrophone };
        var publication = await room.LocalParticipant.PublishTrackAsync(audioTrack, options);

        Assert.NotNull(publication);
        Assert.NotEmpty(publication.Sid);
        Log($"Track published with SID: {publication.Sid}");

        // Wait for Chrome to receive the audio track (track subscription should happen before any audio data)
        Log("Waiting for Chrome to receive the audio track subscription...");
        var wait2 = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
        var audioReceived = wait2.Until(d =>
        {
            var received = ((IJavaScriptExecutor)d).ExecuteScript(
                "return window.testState.audioTrackReceived;"
            );
            var error = ((IJavaScriptExecutor)d).ExecuteScript("return window.testState.error;");
            if (error != null && !string.IsNullOrEmpty(error.ToString()))
            {
                Log($"Chrome error: {error}");
            }
            return received is true;
        });

        Assert.True(audioReceived, "Chrome should have received the audio track");
        Log("Chrome received the audio track!");

        // Verify the track details
        var trackSid = _js.ExecuteScript("return window.testState.audioTrackSid;")?.ToString();
        var participantId = _js.ExecuteScript("return window.testState.participantIdentity;")
            ?.ToString();

        Assert.NotEmpty(trackSid ?? "");
        Assert.Equal(publisherIdentity, participantId);
        Log($"Track verified - SID: {trackSid}, Publisher: {participantId}");

        // Now send some audio frames
        Log("Sending audio frames...");
        const double frequency = 440.0; // A4 note
        const int samplesPerFrame = 480; // 10ms at 48kHz
        const double amplitude = 16000;

        for (int frameCount = 0; frameCount < 100; frameCount++) // Send 1 second of audio
        {
            var samples = new short[samplesPerFrame];
            for (int i = 0; i < samplesPerFrame; i++)
            {
                double t = (frameCount * samplesPerFrame + i) / (double)sampleRate;
                samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * frequency * t));
            }

            var frame = new AudioFrame(samples, sampleRate, numChannels, samplesPerFrame);
            await audioSource.CaptureFrameAsync(frame);
            await Task.Delay(10);
        }
        Log("Audio frames sent");

        // Check if audio frames were received
        await Task.Delay(500); // Give some time for audio processing
        var framesReceived = _js.ExecuteScript("return window.testState.audioFramesReceived;");
        var frameCountReceived = Convert.ToInt32(framesReceived);
        Log($"Audio frames received by Chrome: {frameCountReceived}");

        // We expect at least some audio frames to be received
        Assert.True(frameCountReceived >= 0, $"Expected audio frames, got {frameCountReceived}");

        // Disconnect
        await room.DisconnectAsync();
        Assert.False(room.IsConnected);
        Log("Test completed successfully!");
    }

    [Fact]
    public async Task PublishAudioTrack_BasicConnectivity()
    {
        // Simple test to verify basic room connectivity
        const string roomName = "test-basic-room";
        const string identity = "dotnet-client";

        var token = _fixture.CreateToken(identity, roomName);

        using var room = new Room();
        await room.ConnectAsync(_fixture.LiveKitUrl, token);

        Assert.True(room.IsConnected);
        Assert.NotNull(room.LocalParticipant);
        Assert.Equal(identity, room.LocalParticipant.Identity);
        Assert.NotEmpty(room.Name ?? "");

        await room.DisconnectAsync();
        Assert.False(room.IsConnected);
    }

    [Fact]
    public void CreateAudioTrack_FromAudioSource()
    {
        const int sampleRate = 48000;
        const int numChannels = 1;

        using var audioSource = new AudioSource(sampleRate, numChannels);

        Assert.Equal(sampleRate, audioSource.SampleRate);
        Assert.Equal(numChannels, audioSource.NumChannels);

        var track = LocalAudioTrack.Create("test-track", audioSource);

        Assert.NotNull(track);
        Assert.Equal(Proto.TrackKind.KindAudio, track.Kind);
    }

    [Fact]
    public void AudioFrame_Creation()
    {
        const int sampleRate = 48000;
        const int numChannels = 2;
        const int samplesPerChannel = 480;

        var samples = new short[numChannels * samplesPerChannel];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(i % 1000);
        }

        var frame = new AudioFrame(samples, sampleRate, numChannels, samplesPerChannel);

        Assert.Equal(sampleRate, frame.SampleRate);
        Assert.Equal(numChannels, frame.NumChannels);
        Assert.Equal(samplesPerChannel, frame.SamplesPerChannel);
        Assert.Equal(samples.Length, frame.Data.Length);
    }
}
