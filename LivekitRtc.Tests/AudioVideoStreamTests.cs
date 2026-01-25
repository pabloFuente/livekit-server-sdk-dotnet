// author: https://github.com/pabloFuente

using System;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests
{
    [Collection("LiveKit E2E Tests")]
    public class AudioVideoStreamTests : IClassFixture<RtcTestFixture>, IAsyncLifetime
    {
        private readonly RtcTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public AudioVideoStreamTests(RtcTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task AudioStream_ReceivesFrames_FromRemoteTrack()
        {
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting AudioStream test");

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);

            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber: Received RemoteAudioTrack: {args.Track.Sid}"
                    );
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken("audio-publisher", "test-room");
            var subscriberToken = _fixture.CreateToken("audio-subscriber", "test-room");

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Both participants connected");

            // Publish audio track
            var options = new TrackPublishOptions();
            await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack, options);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Audio track published");

            // Start capturing audio frames
            var frameCount = 0;
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        48000,
                        1,
                        480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    frameCount++;
                    await Task.Delay(10);
                }
            });

            // Wait for track subscription
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteAudioTrack = await trackSubscribedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteAudioTrack);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Remote audio track received");

            // Create AudioStream from remote track
            using var audioStream = new AudioStream(remoteAudioTrack);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created");

            var receivedFrameCount = 0;
            var streamTask = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await foreach (var evt in audioStream.WithCancellation(cts.Token))
                    {
                        var frame = evt.Frame;
                        receivedFrameCount++;
                        _output.WriteLine(
                            $"[{DateTime.Now:HH:mm:ss.fff}] Received audio frame {receivedFrameCount}: {frame.SampleRate}Hz, {frame.NumChannels}ch, {frame.SamplesPerChannel} samples"
                        );

                        if (receivedFrameCount >= 10)
                        {
                            _output.WriteLine(
                                $"[{DateTime.Now:HH:mm:ss.fff}] Received enough frames, stopping"
                            );
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] AudioStream read cancelled (this is expected)"
                    );
                }
            });

            // Wait for capture to complete
            await captureTask;
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Captured {frameCount} frames");

            // Wait for stream to receive frames
            await streamTask;
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Received {receivedFrameCount} frames via AudioStream"
            );

            Assert.True(receivedFrameCount > 0, "Should have received at least some audio frames");

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed successfully!");
        }

        [Fact]
        public async Task VideoStream_ReceivesFrames_FromRemoteTrack()
        {
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Starting VideoStream test");

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var videoSource = new VideoSource(640, 480);
            var videoTrack = LocalVideoTrack.Create("test-video", videoSource);

            var trackSubscribedTcs = new TaskCompletionSource<RemoteVideoTrack>();

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteVideoTrack remoteVideo)
                {
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber: Received RemoteVideoTrack: {args.Track.Sid}"
                    );
                    trackSubscribedTcs.TrySetResult(remoteVideo);
                }
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken("video-publisher", "test-room");
            var subscriberToken = _fixture.CreateToken("video-subscriber", "test-room");

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Both participants connected");

            // Publish video track
            var options = new TrackPublishOptions();
            await publisherRoom.LocalParticipant!.PublishTrackAsync(videoTrack, options);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Video track published");

            // Start capturing video frames
            var frameCount = 0;
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    var frame = GenerateTestFrame(640, 480, i);
                    videoSource.CaptureFrame(frame);
                    frameCount++;
                    await Task.Delay(33); // ~30fps
                }
            });

            // Wait for track subscription
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteVideoTrack = await trackSubscribedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteVideoTrack);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Remote video track received");

            // Create VideoStream from remote track
            using var videoStream = new VideoStream(remoteVideoTrack);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VideoStream created");

            var receivedFrameCount = 0;
            var streamTask = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await foreach (var evt in videoStream.WithCancellation(cts.Token))
                    {
                        var frame = evt.Frame;
                        receivedFrameCount++;
                        _output.WriteLine(
                            $"[{DateTime.Now:HH:mm:ss.fff}] Received video frame {receivedFrameCount}: {frame.Width}x{frame.Height}"
                        );

                        if (receivedFrameCount >= 10)
                        {
                            _output.WriteLine(
                                $"[{DateTime.Now:HH:mm:ss.fff}] Received enough frames, stopping"
                            );
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] VideoStream read cancelled (this is expected)"
                    );
                }
            });

            // Wait for capture to complete
            await captureTask;
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Captured {frameCount} frames");

            // Wait for stream to receive frames
            await streamTask;
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Received {receivedFrameCount} frames via VideoStream"
            );

            Assert.True(receivedFrameCount > 0, "Should have received at least some video frames");

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed successfully!");
        }

        private static short[] GenerateSineWave(
            int samples,
            double frequency,
            int sampleRate,
            int offset
        )
        {
            var data = new short[samples];
            for (int i = 0; i < samples; i++)
            {
                var t = (offset * samples + i) / (double)sampleRate;
                data[i] = (short)(Math.Sin(2 * Math.PI * frequency * t) * short.MaxValue * 0.3);
            }
            return data;
        }

        private static VideoFrame GenerateTestFrame(int width, int height, int frameNumber)
        {
            var dataSize = width * height * 4; // RGBA
            var data = new byte[dataSize];

            var colorOffset = (frameNumber * 5) % 256;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    data[index + 0] = (byte)((x + colorOffset) % 256); // R
                    data[index + 1] = (byte)((y + colorOffset) % 256); // G
                    data[index + 2] = (byte)((x + y + colorOffset) % 256); // B
                    data[index + 3] = 255; // A
                }
            }

            return new VideoFrame(width, height, Proto.VideoBufferType.Rgba, data);
        }
    }
}
