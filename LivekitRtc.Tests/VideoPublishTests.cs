using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for LiveKit RTC video publishing.
/// </summary>
[Collection("RtcTests")]
public class VideoPublishTests : IAsyncLifetime
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private IWebDriver? _driver;
    private IJavaScriptExecutor? _js;

    public VideoPublishTests(RtcTestFixture fixture, ITestOutputHelper output)
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
    public async Task PublishVideoTrack_ChromeReceivesVideo()
    {
        const string roomName = "test-video-room";
        const string publisherIdentity = "dotnet-video-publisher";
        const string subscriberIdentity = "chrome-video-subscriber";

        Log("Starting PublishVideoTrack_ChromeReceivesVideo test");

        // Create tokens
        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var subscriberToken = _fixture.CreateToken(subscriberIdentity, roomName);
        Log("Tokens created");

        // Load the video receiver HTML in Chrome via the HTTP server
        var receiverHtmlPath = Path.Combine(_fixture.WebFilesPath, "video-receiver.html");
        Assert.True(
            File.Exists(receiverHtmlPath),
            $"Video receiver HTML not found at: {receiverHtmlPath}"
        );

        var receiverUrl = $"{_fixture.WebFilesInternalUrl}/video-receiver.html";
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

        // Create video source and track
        const int width = 640;
        const int height = 480;
        using var videoSource = new VideoSource(width, height);
        var videoTrack = LocalVideoTrack.Create("test-video", videoSource);

        // Publish the track
        var options = new TrackPublishOptions { Source = Proto.TrackSource.SourceCamera };
        var publication = await room.LocalParticipant.PublishTrackAsync(videoTrack, options);

        Assert.NotNull(publication);
        Assert.NotEmpty(publication.Sid);
        Log($"Track published with SID: {publication.Sid}");

        // Wait for Chrome to receive the video track
        Log("Waiting for Chrome to receive the video track subscription...");
        var wait2 = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
        var videoReceived = wait2.Until(d =>
        {
            var received = ((IJavaScriptExecutor)d).ExecuteScript(
                "return window.testState.videoTrackReceived;"
            );
            var error = ((IJavaScriptExecutor)d).ExecuteScript("return window.testState.error;");
            if (error != null && !string.IsNullOrEmpty(error.ToString()))
            {
                Log($"Chrome error: {error}");
            }
            return received is true;
        });

        Assert.True(videoReceived, "Chrome should have received the video track");
        Log("Chrome received the video track!");

        // Verify the track details
        var trackSid = _js.ExecuteScript("return window.testState.videoTrackSid;")?.ToString();
        var participantId = _js.ExecuteScript("return window.testState.participantIdentity;")
            ?.ToString();

        Assert.NotEmpty(trackSid ?? "");
        Assert.Equal(publisherIdentity, participantId);
        Log($"Track verified - SID: {trackSid}, Publisher: {participantId}");

        // Now send some video frames (colorful gradient pattern)
        Log("Sending video frames...");

        for (int frameCount = 0; frameCount < 60; frameCount++) // Send 60 frames (~2 seconds at 30fps)
        {
            var frame = GenerateTestFrame(width, height, frameCount);
            videoSource.CaptureFrame(frame);
            await Task.Delay(33); // ~30fps
        }
        Log("Video frames sent");

        // Check if video frames were received
        await Task.Delay(1000); // Give some time for video processing
        var framesReceived = _js.ExecuteScript("return window.testState.videoFramesReceived;");
        var frameCountReceived = Convert.ToInt32(framesReceived);
        Log($"Video frames received by Chrome: {frameCountReceived}");

        // We expect at least some video frames to be received
        Assert.True(frameCountReceived > 0, $"Expected video frames, got {frameCountReceived}");

        // Disconnect
        await room.DisconnectAsync();
        Assert.False(room.IsConnected);
        Log("Test completed successfully!");
    }

    /// <summary>
    /// Generates a test video frame with a colorful gradient pattern.
    /// </summary>
    private VideoFrame GenerateTestFrame(int width, int height, int frameNumber)
    {
        var frame = VideoFrame.Create(width, height, Proto.VideoBufferType.Rgba);
        var data = frame.DataMutable;

        // Generate a moving gradient pattern
        int offset = frameNumber * 2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;

                // Create a colorful moving gradient
                byte r = (byte)((x + offset) % 256);
                byte g = (byte)((y + offset) % 256);
                byte b = (byte)(((x + y) / 2 + offset) % 256);
                byte a = 255;

                data[index] = r;
                data[index + 1] = g;
                data[index + 2] = b;
                data[index + 3] = a;
            }
        }

        return frame;
    }

    [Fact]
    public void CreateVideoTrack_FromVideoSource()
    {
        const int width = 1280;
        const int height = 720;

        using var videoSource = new VideoSource(width, height);

        Assert.Equal(width, videoSource.Width);
        Assert.Equal(height, videoSource.Height);

        var track = LocalVideoTrack.Create("test-video-track", videoSource);

        Assert.NotNull(track);
        Assert.Equal(Proto.TrackKind.KindVideo, track.Kind);
    }

    [Fact]
    public void VideoFrame_Creation()
    {
        const int width = 640;
        const int height = 480;

        var frame = VideoFrame.Create(width, height, Proto.VideoBufferType.Rgba);

        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Equal(Proto.VideoBufferType.Rgba, frame.Type);
        Assert.Equal(width * height * 4, frame.DataBytes.Length); // RGBA = 4 bytes per pixel
    }

    [Fact]
    public void VideoFrame_Conversion()
    {
        const int width = 320;
        const int height = 240;

        // Create an RGBA frame
        var rgbaFrame = VideoFrame.Create(width, height, Proto.VideoBufferType.Rgba);

        // Fill with test data
        var data = rgbaFrame.DataMutable;
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = 255; // R
            data[i + 1] = 0; // G
            data[i + 2] = 0; // B
            data[i + 3] = 255; // A
        }

        // Convert to I420 (YUV)
        var i420Frame = rgbaFrame.Convert(Proto.VideoBufferType.I420);

        Assert.Equal(width, i420Frame.Width);
        Assert.Equal(height, i420Frame.Height);
        Assert.Equal(Proto.VideoBufferType.I420, i420Frame.Type);
    }
}
