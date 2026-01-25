// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests
{
    /// <summary>
    /// E2E tests for advanced AudioStream features: FromParticipant and FrameProcessor support.
    /// </summary>
    [Collection("LiveKit E2E Tests")]
    public class AudioStreamAdvancedTests : IClassFixture<RtcTestFixture>, IAsyncLifetime
    {
        private readonly RtcTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public AudioStreamAdvancedTests(RtcTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => Task.CompletedTask;

        #region FromParticipant Tests

        [Fact]
        public async Task AudioStream_FromParticipant_ReceivesFrames_WithMicrophoneSource()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting AudioStream.FromParticipant test with Microphone source"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio", audioSource);

            var participantConnectedTcs = new TaskCompletionSource<Participant>();
            var trackPublishedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            // Wait for remote participant to be visible
            subscriberRoom.ParticipantConnected += (sender, participant) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber: Remote participant connected: {participant.Identity}"
                );
                participantConnectedTcs.TrySetResult(participant);
            };

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] Subscriber: Track subscribed: {args.Track.Sid}"
                    );
                    trackPublishedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect subscriber first to receive ParticipantConnected event
            var publisherToken = _fixture.CreateToken("audio-publisher-fp", "test-room-fp");
            var subscriberToken = _fixture.CreateToken("audio-subscriber-fp", "test-room-fp");

            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Both participants connected");

            // Publish audio track with Microphone source
            var options = new TrackPublishOptions { Source = Proto.TrackSource.SourceMicrophone };
            await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack, options);
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Audio track published with Source.Microphone"
            );

            // Wait for remote participant to be visible
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteParticipant = await participantConnectedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteParticipant);
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Remote participant received: {remoteParticipant.Identity}"
            );

            // Wait for track subscription (to ensure track is available)
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteTrack = await trackPublishedTcs.Task.WaitAsync(cts2.Token);
            Assert.NotNull(remoteTrack);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Track subscription confirmed");

            // Give FFI layer time to fully set up the track
            await Task.Delay(100);

            // Create AudioStream from participant + track source
            using var audioStream = AudioStream.FromParticipant(
                remoteParticipant,
                Proto.TrackSource.SourceMicrophone,
                sampleRate: 48000,
                numChannels: 1
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created from participant with TrackSource.Microphone"
            );

            // Start capturing audio frames
            var frameCount = 0;
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        sampleRate: 48000,
                        numChannels: 1,
                        samplesPerChannel: 480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    frameCount++;
                    await Task.Delay(10);
                }
            });

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

                        // Verify frame properties
                        Assert.Equal(48000, frame.SampleRate);
                        Assert.Equal(1, frame.NumChannels);
                        Assert.True(frame.SamplesPerChannel > 0);

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
                    _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] AudioStream read cancelled");
                }
            });

            await captureTask;
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Captured {frameCount} frames");

            await streamTask;
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Received {receivedFrameCount} frames via AudioStream.FromParticipant"
            );

            Assert.True(
                receivedFrameCount > 0,
                "Should have received at least some audio frames from participant"
            );

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed successfully!");
        }

        [Fact]
        public async Task AudioStream_FromParticipant_VsFromTrack_BehaviorParity()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting AudioStream FromParticipant vs FromTrack parity test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio-parity", audioSource);

            var participantConnectedTcs = new TaskCompletionSource<Participant>();
            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.ParticipantConnected += (sender, participant) =>
            {
                _output.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] Remote participant connected: {participant.Identity}"
                );
                participantConnectedTcs.TrySetResult(participant);
            };

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] Track subscribed: {args.Track.Sid}"
                    );
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect subscriber first to receive ParticipantConnected event
            var publisherToken = _fixture.CreateToken("audio-publisher-parity", "test-room-parity");
            var subscriberToken = _fixture.CreateToken(
                "audio-subscriber-parity",
                "test-room-parity"
            );

            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);

            // Publish audio track with Microphone source
            var options = new TrackPublishOptions { Source = Proto.TrackSource.SourceMicrophone };
            await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack, options);
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Audio track published");

            // Wait for remote participant and track
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteParticipant = await participantConnectedTcs.Task.WaitAsync(cts1.Token);

            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteTrack = await trackSubscribedTcs.Task.WaitAsync(cts2.Token);
            Assert.NotNull(remoteTrack);

            // Give FFI layer time to fully set up the track
            await Task.Delay(100);

            // Create two streams: one from track, one from participant
            using var streamFromTrack = AudioStream.FromTrack(
                remoteTrack,
                sampleRate: 48000,
                numChannels: 1
            );
            using var streamFromParticipant = AudioStream.FromParticipant(
                remoteParticipant,
                Proto.TrackSource.SourceMicrophone,
                sampleRate: 48000,
                numChannels: 1
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Created both streams (FromTrack and FromParticipant)"
            );

            // Verify stream properties
            Assert.Equal(48000u, streamFromTrack.SampleRate);
            Assert.Equal(48000u, streamFromParticipant.SampleRate);
            Assert.Equal(1u, streamFromTrack.NumChannels);
            Assert.Equal(1u, streamFromParticipant.NumChannels);

            // Start capturing
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        48000,
                        1,
                        480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    await Task.Delay(10);
                }
            });

            var receivedFromTrack = 0;
            var receivedFromParticipant = 0;

            var task1 = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await foreach (var evt in streamFromTrack.WithCancellation(cts.Token))
                {
                    receivedFromTrack++;
                    if (receivedFromTrack >= 5)
                        break;
                }
            });

            var task2 = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await foreach (var evt in streamFromParticipant.WithCancellation(cts.Token))
                {
                    receivedFromParticipant++;
                    if (receivedFromParticipant >= 5)
                        break;
                }
            });

            await captureTask;
            await Task.WhenAll(task1, task2);

            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] FromTrack received: {receivedFromTrack}, FromParticipant received: {receivedFromParticipant}"
            );

            // Both should receive frames
            Assert.True(receivedFromTrack > 0, "FromTrack should receive frames");
            Assert.True(receivedFromParticipant > 0, "FromParticipant should receive frames");

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Parity test completed!");
        }

        #endregion

        #region FrameProcessor Tests

        [Fact]
        public async Task AudioStream_FrameProcessor_ProcessesFrames_WhenEnabled()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting FrameProcessor enabled test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio-processor", audioSource);

            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken("audio-publisher-proc", "test-room-proc");
            var subscriberToken = _fixture.CreateToken("audio-subscriber-proc", "test-room-proc");

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);

            // Publish audio track
            await publisherRoom.LocalParticipant!.PublishTrackAsync(
                audioTrack,
                new TrackPublishOptions()
            );

            // Wait for track subscription
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteTrack = await trackSubscribedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteTrack);

            // Create a test frame processor
            var processor = new TestAudioFrameProcessor();
            processor.IsEnabled = true;

            // Create AudioStream with frame processor
            using var audioStream = AudioStream.FromTrack(
                remoteTrack,
                sampleRate: 48000,
                numChannels: 1,
                frameProcessor: processor
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created with FrameProcessor (enabled)"
            );

            // Start capturing
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        48000,
                        1,
                        480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    await Task.Delay(10);
                }
            });

            var receivedFrameCount = 0;
            var streamTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await foreach (var evt in audioStream.WithCancellation(cts.Token))
                {
                    receivedFrameCount++;
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] Received processed frame {receivedFrameCount}"
                    );

                    if (receivedFrameCount >= 10)
                        break;
                }
            });

            await captureTask;
            await streamTask;

            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Processor called {processor.ProcessCallCount} times"
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Received {receivedFrameCount} frames"
            );

            // Verify processor was called
            Assert.True(
                processor.ProcessCallCount > 0,
                "FrameProcessor.Process() should have been called when enabled"
            );
            Assert.True(receivedFrameCount > 0, "Should have received processed frames");

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed!");
        }

        [Fact]
        public async Task AudioStream_FrameProcessor_BypassesProcessing_WhenDisabled()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting FrameProcessor disabled test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio-proc-disabled", audioSource);

            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken(
                "audio-publisher-proc-dis",
                "test-room-proc-dis"
            );
            var subscriberToken = _fixture.CreateToken(
                "audio-subscriber-proc-dis",
                "test-room-proc-dis"
            );

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);

            // Publish audio track
            await publisherRoom.LocalParticipant!.PublishTrackAsync(
                audioTrack,
                new TrackPublishOptions()
            );

            // Wait for track subscription
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteTrack = await trackSubscribedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteTrack);

            // Create processor but keep it disabled
            var processor = new TestAudioFrameProcessor();
            processor.IsEnabled = false;

            using var audioStream = AudioStream.FromTrack(
                remoteTrack,
                sampleRate: 48000,
                numChannels: 1,
                frameProcessor: processor
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created with FrameProcessor (disabled)"
            );

            // Start capturing
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        48000,
                        1,
                        480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    await Task.Delay(10);
                }
            });

            var receivedFrameCount = 0;
            var streamTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await foreach (var evt in audioStream.WithCancellation(cts.Token))
                {
                    receivedFrameCount++;
                    if (receivedFrameCount >= 10)
                        break;
                }
            });

            await captureTask;
            await streamTask;

            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Processor called {processor.ProcessCallCount} times"
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Received {receivedFrameCount} frames"
            );

            // Processor should NOT have been called when disabled
            Assert.Equal(0, processor.ProcessCallCount);
            Assert.True(
                receivedFrameCount > 0,
                "Should still receive frames even when processor is disabled"
            );

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed!");
        }

        [Fact]
        public async Task AudioStream_FrameProcessor_TogglesProcessing_Dynamically()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting FrameProcessor dynamic toggle test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio-proc-toggle", audioSource);

            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken("audio-publisher-toggle", "test-room-toggle");
            var subscriberToken = _fixture.CreateToken(
                "audio-subscriber-toggle",
                "test-room-toggle"
            );

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);

            // Publish audio track
            await publisherRoom.LocalParticipant!.PublishTrackAsync(
                audioTrack,
                new TrackPublishOptions()
            );

            // Wait for track subscription
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteTrack = await trackSubscribedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteTrack);

            // Create processor, start enabled
            var processor = new TestAudioFrameProcessor();
            processor.IsEnabled = true;

            using var audioStream = AudioStream.FromTrack(
                remoteTrack,
                sampleRate: 48000,
                numChannels: 1,
                frameProcessor: processor
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created with FrameProcessor (initially enabled)"
            );

            // Start capturing
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 60; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        48000,
                        1,
                        480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    await Task.Delay(10);
                }
            });

            var receivedFrameCount = 0;
            var callCountWhenEnabled = 0;
            var streamTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await foreach (var evt in audioStream.WithCancellation(cts.Token))
                {
                    receivedFrameCount++;

                    // After 5 frames, disable the processor
                    if (receivedFrameCount == 5)
                    {
                        callCountWhenEnabled = processor.ProcessCallCount;
                        processor.IsEnabled = false;
                        _output.WriteLine(
                            $"[{DateTime.Now:HH:mm:ss.fff}] Disabled processor at frame {receivedFrameCount}, call count: {callCountWhenEnabled}"
                        );
                    }

                    if (receivedFrameCount >= 15)
                        break;
                }
            });

            await captureTask;
            await streamTask;

            var finalCallCount = processor.ProcessCallCount;
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Processor calls while enabled: {callCountWhenEnabled}"
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Processor calls total: {finalCallCount}"
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Total frames received: {receivedFrameCount}"
            );

            // Processor should have been called when enabled but not after being disabled
            Assert.True(
                callCountWhenEnabled > 0,
                "Processor should have been called while enabled"
            );
            // Call count should stop increasing after disabling (or increase very minimally due to race conditions)
            Assert.True(
                finalCallCount - callCountWhenEnabled <= 2,
                "Processor should stop being called after being disabled"
            );

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed!");
        }

        [Fact]
        public async Task AudioStream_FrameProcessor_HandlesExceptions_Gracefully()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting FrameProcessor exception handling test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio-proc-error", audioSource);

            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken("audio-publisher-error", "test-room-error");
            var subscriberToken = _fixture.CreateToken("audio-subscriber-error", "test-room-error");

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);

            // Publish audio track
            await publisherRoom.LocalParticipant!.PublishTrackAsync(
                audioTrack,
                new TrackPublishOptions()
            );

            // Wait for track subscription
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteTrack = await trackSubscribedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteTrack);

            // Create processor that throws exceptions
            var processor = new ThrowingAudioFrameProcessor();
            processor.IsEnabled = true;

            using var audioStream = AudioStream.FromTrack(
                remoteTrack,
                sampleRate: 48000,
                numChannels: 1,
                frameProcessor: processor
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created with throwing FrameProcessor"
            );

            // Start capturing
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        48000,
                        1,
                        480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    await Task.Delay(10);
                }
            });

            var receivedFrameCount = 0;
            var streamTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await foreach (var evt in audioStream.WithCancellation(cts.Token))
                {
                    receivedFrameCount++;
                    _output.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] Received frame {receivedFrameCount} (processor threw {processor.ExceptionCount} exceptions)"
                    );

                    if (receivedFrameCount >= 10)
                        break;
                }
            });

            await captureTask;
            await streamTask;

            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Total exceptions thrown: {processor.ExceptionCount}"
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Total frames received: {receivedFrameCount}"
            );

            // Stream should still receive frames even when processor throws exceptions
            Assert.True(processor.ExceptionCount > 0, "Processor should have thrown exceptions");
            Assert.True(
                receivedFrameCount > 0,
                "Should still receive frames despite processor exceptions"
            );

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed!");
        }

        [Fact]
        public async Task AudioStream_FrameProcessor_Close_CalledOnDispose()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting FrameProcessor.Close() test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio-proc-close", audioSource);

            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect both participants
            var publisherToken = _fixture.CreateToken("audio-publisher-close", "test-room-close");
            var subscriberToken = _fixture.CreateToken("audio-subscriber-close", "test-room-close");

            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);
            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);

            // Publish audio track
            await publisherRoom.LocalParticipant!.PublishTrackAsync(
                audioTrack,
                new TrackPublishOptions()
            );

            // Wait for track subscription
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteTrack = await trackSubscribedTcs.Task.WaitAsync(cts1.Token);
            Assert.NotNull(remoteTrack);

            var processor = new TestAudioFrameProcessor();
            processor.IsEnabled = true;

            AudioStream? audioStream = AudioStream.FromTrack(
                remoteTrack,
                sampleRate: 48000,
                numChannels: 1,
                frameProcessor: processor
            );
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created");

            // Generate some frames
            for (int i = 0; i < 5; i++)
            {
                var frame = new AudioFrame(GenerateSineWave(480, 440.0, 48000, i), 48000, 1, 480);
                await audioSource.CaptureFrameAsync(frame);
                await Task.Delay(10);
            }

            // Give time for some frames to be processed
            await Task.Delay(100);

            Assert.False(processor.WasClosed, "Processor should not be closed yet");

            // Dispose the audio stream
            audioStream.Dispose();
            audioStream = null;
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] AudioStream disposed");

            // Verify Close() was called
            Assert.True(
                processor.WasClosed,
                "FrameProcessor.Close() should be called when AudioStream is disposed"
            );

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed!");
        }

        [Fact]
        public async Task AudioStream_FrameProcessor_WithFromParticipant()
        {
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Starting FrameProcessor with FromParticipant test"
            );

            using var publisherRoom = new Room();
            using var subscriberRoom = new Room();
            using var audioSource = new AudioSource(48000, 1);
            var audioTrack = LocalAudioTrack.Create("test-audio-proc-fp", audioSource);

            var participantConnectedTcs = new TaskCompletionSource<Participant>();
            var trackSubscribedTcs = new TaskCompletionSource<RemoteAudioTrack>();

            subscriberRoom.ParticipantConnected += (sender, participant) =>
            {
                participantConnectedTcs.TrySetResult(participant);
            };

            subscriberRoom.TrackSubscribed += (sender, args) =>
            {
                if (args.Track is RemoteAudioTrack remoteAudio)
                {
                    trackSubscribedTcs.TrySetResult(remoteAudio);
                }
            };

            // Connect subscriber first to receive ParticipantConnected event
            var publisherToken = _fixture.CreateToken(
                "audio-publisher-proc-fp",
                "test-room-proc-fp"
            );
            var subscriberToken = _fixture.CreateToken(
                "audio-subscriber-proc-fp",
                "test-room-proc-fp"
            );

            await subscriberRoom.ConnectAsync(_fixture.LiveKitUrl, subscriberToken);
            await publisherRoom.ConnectAsync(_fixture.LiveKitUrl, publisherToken);

            // Publish audio track with Microphone source
            var publishOptions = new TrackPublishOptions
            {
                Source = Proto.TrackSource.SourceMicrophone,
            };
            await publisherRoom.LocalParticipant!.PublishTrackAsync(audioTrack, publishOptions);

            // Wait for remote participant and track
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remoteParticipant = await participantConnectedTcs.Task.WaitAsync(cts1.Token);

            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await trackSubscribedTcs.Task.WaitAsync(cts2.Token);

            // Give FFI layer time to fully set up the track
            await Task.Delay(100);

            // Create processor
            var processor = new TestAudioFrameProcessor();
            processor.IsEnabled = true;

            // Create AudioStream from participant with processor
            using var audioStream = AudioStream.FromParticipant(
                remoteParticipant,
                Proto.TrackSource.SourceMicrophone,
                sampleRate: 48000,
                numChannels: 1,
                frameProcessor: processor
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] AudioStream created from participant with FrameProcessor"
            );

            // Start capturing
            var captureTask = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    var frame = new AudioFrame(
                        GenerateSineWave(480, 440.0, 48000, i),
                        48000,
                        1,
                        480
                    );
                    await audioSource.CaptureFrameAsync(frame);
                    await Task.Delay(10);
                }
            });

            var receivedFrameCount = 0;
            var streamTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await foreach (var evt in audioStream.WithCancellation(cts.Token))
                {
                    receivedFrameCount++;
                    if (receivedFrameCount >= 10)
                        break;
                }
            });

            await captureTask;
            await streamTask;

            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Processor called {processor.ProcessCallCount} times"
            );
            _output.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] Received {receivedFrameCount} frames"
            );

            // Verify processor was called with FromParticipant
            Assert.True(
                processor.ProcessCallCount > 0,
                "FrameProcessor should work with FromParticipant"
            );
            Assert.True(receivedFrameCount > 0, "Should receive processed frames from participant");

            await publisherRoom.DisconnectAsync();
            await subscriberRoom.DisconnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test completed!");
        }

        #endregion

        #region Helper Methods and Test Classes

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

        /// <summary>
        /// Test implementation of FrameProcessor that tracks calls.
        /// </summary>
        private class TestAudioFrameProcessor : FrameProcessor<AudioFrame>
        {
            private bool _isEnabled;
            public int ProcessCallCount { get; private set; }
            public bool WasClosed { get; private set; }
            public List<FrameProcessorStreamInfo> StreamInfoUpdates { get; } = new();
            public List<FrameProcessorCredentials> CredentialUpdates { get; } = new();

            public override bool IsEnabled
            {
                get => _isEnabled;
                set => _isEnabled = value;
            }

            public override AudioFrame Process(AudioFrame frame)
            {
                ProcessCallCount++;
                // Pass through the frame unmodified
                return frame;
            }

            public override void OnStreamInfoUpdated(FrameProcessorStreamInfo info)
            {
                StreamInfoUpdates.Add(info);
            }

            public override void OnCredentialsUpdated(FrameProcessorCredentials credentials)
            {
                CredentialUpdates.Add(credentials);
            }

            public override void Close()
            {
                WasClosed = true;
            }
        }

        /// <summary>
        /// Test processor that throws exceptions to test error handling.
        /// </summary>
        private class ThrowingAudioFrameProcessor : FrameProcessor<AudioFrame>
        {
            private bool _isEnabled;
            public int ExceptionCount { get; private set; }

            public override bool IsEnabled
            {
                get => _isEnabled;
                set => _isEnabled = value;
            }

            public override AudioFrame Process(AudioFrame frame)
            {
                ExceptionCount++;
                throw new InvalidOperationException("Test exception from frame processor");
            }

            public override void Close()
            {
                // Nothing to clean up
            }
        }

        #endregion
    }
}
