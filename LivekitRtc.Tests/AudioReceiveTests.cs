// author: https://github.com/pabloFuente

using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for receiving audio from LiveKit room and saving to WAV file.
/// </summary>
[Collection("RtcTests")]
public class AudioReceiveTests : IAsyncLifetime
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private IWebDriver? _driver;
    private IJavaScriptExecutor? _js;

    // WAV file constants
    private const int BitsPerSample = 16;
    private static readonly string WavFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "livekit_received_audio.wav"
    );

    public AudioReceiveTests(RtcTestFixture fixture, ITestOutputHelper output)
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
        options.AddArgument("--allow-file-access-from-files");
        options.AddArgument("--disable-web-security");

        // For containerized Chrome, use fake device (will trigger Web Audio API fallback)
        // For local Chrome, don't use fake device to allow Web Audio API to work directly
        if (!string.IsNullOrEmpty(_fixture.ChromeUrl))
        {
            options.AddArgument("--use-fake-device-for-media-stream");
        }

        if (string.IsNullOrEmpty(_fixture.ChromeUrl))
        {
            Log("Using local Chrome browser");
            _driver = new ChromeDriver(options);
        }
        else
        {
            await Task.Delay(2000);
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

        // Don't delete WAV file - leave it for the user to inspect
        // File location: ~/livekit_received_audio.wav

        return Task.CompletedTask;
    }

    private void WriteWavHeader(BinaryWriter writer, int sampleRate, int numChannels)
    {
        // Calculate derived values
        int byteRate = sampleRate * numChannels * BitsPerSample / 8;
        short blockAlign = (short)(numChannels * BitsPerSample / 8);

        // Write RIFF header
        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(0); // ChunkSize placeholder (will be updated later)
        writer.Write(new[] { 'W', 'A', 'V', 'E' });

        // Write fmt subchunk
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16); // Subchunk1Size (16 for PCM)
        writer.Write((short)1); // AudioFormat (1 = PCM)
        writer.Write((short)numChannels); // NumChannels
        writer.Write(sampleRate); // SampleRate
        writer.Write(byteRate); // ByteRate
        writer.Write(blockAlign); // BlockAlign
        writer.Write((short)BitsPerSample); // BitsPerSample

        // Write data subchunk header
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(0); // Subchunk2Size placeholder (will be updated later)
    }

    private void UpdateWavHeader(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
        var fileSize = stream.Length;

        var chunkSize = (int)(fileSize - 8);
        var subchunk2Size = (int)(fileSize - 44);

        // Update ChunkSize at offset 4
        stream.Seek(4, SeekOrigin.Begin);
        var writer = new BinaryWriter(stream);
        writer.Write(chunkSize);

        // Update Subchunk2Size at offset 40
        stream.Seek(40, SeekOrigin.Begin);
        writer.Write(subchunk2Size);
    }

    [Fact]
    public async Task ReceiveAudioFromChrome_SaveToWavFile()
    {
        const string roomName = "test-audio-receive-room";
        const string publisherIdentity = "chrome-publisher";
        const string receiverIdentity = "dotnet-receiver";

        Log("Starting ReceiveAudioFromChrome_SaveToWavFile test");

        // Create tokens
        var publisherToken = _fixture.CreateToken(publisherIdentity, roomName);
        var receiverToken = _fixture.CreateToken(receiverIdentity, roomName);
        Log("Tokens created");

        // Set up the .NET receiver room
        using var receiverRoom = new Room();
        string? trackToProcess = null;
        BinaryWriter? wavWriter = null;
        var audioReceivedTcs = new TaskCompletionSource<bool>();
        var framesReceived = 0;

        receiverRoom.ParticipantConnected += (sender, participant) =>
        {
            Log($"[EVENT] Participant connected: {participant.Identity}");
            Log(
                $"[EVENT] Participant has {participant.TrackPublications.Count} track publications"
            );
            foreach (var pub in participant.TrackPublications.Values)
            {
                var isSubscribed = (pub is RemoteTrackPublication rp) ? rp.IsSubscribed : false;
                Log($"[EVENT]   - Track: {pub.Sid}, Kind: {pub.Kind}, Subscribed: {isSubscribed}");
            }
        };

        receiverRoom.TrackPublished += (sender, e) =>
        {
            Log(
                $"[EVENT] Track published by {e.Participant.Identity}: {e.Publication.Sid}, kind: {e.Publication.Kind}"
            );
        };

        receiverRoom.TrackSubscribed += async (sender, e) =>
        {
            Log($"[EVENT] Track subscribed: {e.Track.Sid}, kind: {e.Track.Kind}");

            if (e.Track is RemoteAudioTrack audioTrack)
            {
                trackToProcess = e.Track.Sid;
                Log($"Starting audio stream for track {audioTrack.Sid}");

                try
                {
                    var stream = new AudioStream(audioTrack);
                    Log($"AudioStream created with sample rate: {stream.SampleRate}");

                    await foreach (var frameEvent in stream)
                    {
                        if (trackToProcess == null)
                        {
                            Log("Track unsubscribed, stopping stream");
                            break;
                        }

                        var frame = frameEvent.Frame;

                        // Create WAV file on first frame
                        if (wavWriter == null)
                        {
                            Log($"Creating WAV file: {WavFilePath}");
                            Log(
                                $"Audio format: {frame.SampleRate}Hz, {frame.NumChannels} channels, {frame.SamplesPerChannel} samples/channel"
                            );

                            var fileStream = new FileStream(
                                WavFilePath,
                                FileMode.Create,
                                FileAccess.Write
                            );
                            wavWriter = new BinaryWriter(fileStream);
                            WriteWavHeader(wavWriter, frame.SampleRate, frame.NumChannels);
                        }

                        // Write audio data to WAV file
                        var audioData = frame.DataArray;
                        foreach (var sample in audioData)
                        {
                            wavWriter.Write(sample);
                        }

                        framesReceived++;
                        if (framesReceived == 1 || framesReceived % 100 == 0)
                        {
                            Log($"Received {framesReceived} audio frames");
                        }

                        // Signal that we've received audio after a reasonable amount
                        if (framesReceived >= 50)
                        {
                            audioReceivedTcs.TrySetResult(true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error in audio stream: {ex.Message}");
                    Log($"Stack trace: {ex.StackTrace}");
                    audioReceivedTcs.TrySetException(ex);
                }
            }
        };

        receiverRoom.TrackUnsubscribed += (sender, e) =>
        {
            Log($"Track unsubscribed: {e.Publication.Sid}");
            if (e.Publication.Sid == trackToProcess)
            {
                trackToProcess = null;

                if (wavWriter != null)
                {
                    Log("Closing WAV file and updating header");
                    wavWriter.Close();
                    wavWriter.Dispose();
                    wavWriter = null;

                    UpdateWavHeader(WavFilePath);
                    Log("WAV file finalized");
                }
            }
        };

        // Connect receiver to room
        Log("Connecting receiver to room");
        var roomOptions = new RoomOptions { AutoSubscribe = true };
        await receiverRoom.ConnectAsync(_fixture.LiveKitUrl, receiverToken, roomOptions);
        Log($"Receiver connected to room: {receiverRoom.Name}");

        // Load the publisher HTML in Chrome
        var publisherHtmlPath = Path.Combine(_fixture.WebFilesPath, "publisher.html");
        Assert.True(
            File.Exists(publisherHtmlPath),
            $"Publisher HTML not found at: {publisherHtmlPath}"
        );

        var publisherUrl = $"{_fixture.WebFilesInternalUrl}/publisher.html";
        Log($"Navigating Chrome to: {publisherUrl}");
        _driver!.Navigate().GoToUrl(publisherUrl);
        await Task.Delay(3000);

        // Connect Chrome publisher to room
        // Use internal URL for containerized Chrome
        var chromeLiveKitUrl = _fixture.UseLocalChrome
            ? _fixture.LiveKitUrl
            : _fixture.LiveKitInternalUrl;
        Log($"Connecting Chrome publisher to room at {chromeLiveKitUrl}");
        var jsScript =
            $@"
            window.connectAndPublish('{chromeLiveKitUrl}', '{publisherToken}').then(callback);
        ";

        var result = await ExecuteAsyncScript(jsScript);
        Log($"Chrome publisher result: {result}");

        // Get test state from window
        var js = (IJavaScriptExecutor)_driver!;
        var testStateJson = js.ExecuteScript("return JSON.stringify(window.testState);");
        Log($"Chrome test state: {testStateJson}");

        // Check browser console logs
        var logs = _driver!.Manage().Logs.GetLog("browser");
        if (logs.Count > 0)
        {
            Log("Browser console logs:");
            foreach (var logEntry in logs)
            {
                Log($"  [{logEntry.Level}] {logEntry.Message}");
            }
        }

        // Check remote participants
        await Task.Delay(3000); // Give time for the participant to fully connect
        Log($"Room has {receiverRoom.RemoteParticipants.Count} remote participants");
        foreach (var rp in receiverRoom.RemoteParticipants.Values)
        {
            Log($"  Remote participant: {rp.Identity}, {rp.TrackPublications.Count} tracks");
            foreach (var pub in rp.TrackPublications.Values)
            {
                var isSubscribed =
                    (pub is RemoteTrackPublication remotePub) ? remotePub.IsSubscribed : false;
                Log($"    - Track {pub.Sid}: Kind={pub.Kind}, Subscribed={isSubscribed}");
            }
        }

        // Wait for audio frames to be received
        Log("Waiting for audio frames...");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var audioReceived = await audioReceivedTcs.Task.WaitAsync(cts.Token);
        Assert.True(audioReceived, "Should have received audio frames");

        Log($"Total frames received: {framesReceived}");
        Assert.True(
            framesReceived >= 50,
            $"Should have received at least 50 frames, got {framesReceived}"
        );

        // Stop Chrome publisher
        Log("Stopping Chrome publisher");
        _js!.ExecuteScript("window.disconnect()");

        await Task.Delay(1000);

        // Disconnect receiver
        Log("Disconnecting receiver");
        await receiverRoom.DisconnectAsync();

        // Verify WAV file was created and has data
        Assert.True(File.Exists(WavFilePath), "WAV file should exist");
        var fileInfo = new FileInfo(WavFilePath);
        Log($"WAV file size: {fileInfo.Length} bytes");
        Assert.True(
            fileInfo.Length > 1000,
            $"WAV file should contain audio data, got {fileInfo.Length} bytes"
        );

        Log("Test completed successfully!");
    }

    private async Task<string> ExecuteAsyncScript(string script)
    {
        var resultTask = Task.Run(() =>
        {
            var jsExecutor = (IJavaScriptExecutor)_driver!;
            var result = jsExecutor.ExecuteAsyncScript(
                "var callback = arguments[arguments.length - 1]; " + script
            );
            return result?.ToString() ?? "null";
        });

        return await resultTask;
    }
}
