// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents an audio frame received event.
    /// </summary>
    public readonly struct AudioFrameEvent
    {
        /// <summary>
        /// Gets the audio frame.
        /// </summary>
        public AudioFrame Frame { get; }

        /// <summary>
        /// Initializes a new AudioFrameEvent.
        /// </summary>
        /// <param name="frame">The audio frame.</param>
        public AudioFrameEvent(AudioFrame frame)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }
    }

    /// <summary>
    /// Options for noise cancellation processing.
    /// </summary>
    public class NoiseCancellationOptions
    {
        /// <summary>
        /// Gets or sets the noise cancellation module ID.
        /// </summary>
        public string ModuleId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets additional options as JSON.
        /// </summary>
        public string OptionsJson { get; set; } = string.Empty;
    }

    /// <summary>
    /// An audio stream for receiving audio frames from a remote track or participant.
    /// Implements IAsyncEnumerable for modern async iteration patterns.
    /// </summary>
    public class AudioStream : IAsyncDisposable, IDisposable, IAsyncEnumerable<AudioFrameEvent>
    {
        private readonly FfiHandle _handle;
        private readonly Track? _track;
        private readonly Participant? _participant;
        private readonly Proto.TrackSource? _trackSource;
        private readonly uint _sampleRate;
        private readonly uint _numChannels;
        private readonly uint? _frameSizeMs;
        private readonly NoiseCancellationOptions? _noiseCancellation;
        private readonly FrameProcessor<AudioFrame>? _frameProcessor;
        private readonly Channel<AudioFrameEvent> _channel;
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        /// <summary>
        /// Initializes a new AudioStream from a track.
        /// </summary>
        /// <param name="track">The audio track to stream from.</param>
        /// <param name="sampleRate">The sample rate in Hz (default 48000).</param>
        /// <param name="numChannels">The number of channels (default 1).</param>
        /// <param name="frameSizeMs">Optional frame size in milliseconds.</param>
        /// <param name="capacity">The capacity of the internal channel (0 for unbounded).</param>
        /// <param name="noiseCancellation">Optional noise cancellation options.</param>
        /// <param name="frameProcessor">Optional frame processor for custom audio processing.</param>
        public AudioStream(
            Track track,
            uint sampleRate = 48000,
            uint numChannels = 1,
            uint? frameSizeMs = null,
            int capacity = 0,
            NoiseCancellationOptions? noiseCancellation = null,
            FrameProcessor<AudioFrame>? frameProcessor = null
        )
        {
            _track = track ?? throw new ArgumentNullException(nameof(track));
            _sampleRate = sampleRate;
            _numChannels = numChannels;
            _frameSizeMs = frameSizeMs;
            _noiseCancellation = noiseCancellation;
            _frameProcessor = frameProcessor;

            // Use bounded channel for backpressure or unbounded for high throughput
            _channel =
                capacity > 0
                    ? Channel.CreateBounded<AudioFrameEvent>(
                        new BoundedChannelOptions(capacity)
                        {
                            FullMode = BoundedChannelFullMode.Wait,
                            SingleReader = false,
                            SingleWriter = true,
                        }
                    )
                    : Channel.CreateUnbounded<AudioFrameEvent>(
                        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
                    );

            _cts = new CancellationTokenSource();

            OwnedAudioStream stream;
            if (_participant != null && _trackSource.HasValue)
            {
                stream = CreateOwnedStreamFromParticipant();
            }
            else
            {
                stream = CreateOwnedStream();
            }

            _handle = FfiHandle.FromId(stream.Handle.Id);

            // Subscribe to events
            FfiClient.Instance.EventReceived += OnFfiEvent;
        }

        /// <summary>
        /// Private constructor for creating AudioStream from participant.
        /// </summary>
        private AudioStream(
            Participant participant,
            Proto.TrackSource trackSource,
            uint sampleRate,
            uint numChannels,
            uint? frameSizeMs,
            int capacity,
            NoiseCancellationOptions? noiseCancellation,
            FrameProcessor<AudioFrame>? frameProcessor
        )
        {
            _participant = participant ?? throw new ArgumentNullException(nameof(participant));
            _trackSource = trackSource;
            _sampleRate = sampleRate;
            _numChannels = numChannels;
            _frameSizeMs = frameSizeMs;
            _noiseCancellation = noiseCancellation;
            _frameProcessor = frameProcessor;

            // Use bounded channel for backpressure or unbounded for high throughput
            _channel =
                capacity > 0
                    ? Channel.CreateBounded<AudioFrameEvent>(
                        new BoundedChannelOptions(capacity)
                        {
                            FullMode = BoundedChannelFullMode.Wait,
                            SingleReader = false,
                            SingleWriter = true,
                        }
                    )
                    : Channel.CreateUnbounded<AudioFrameEvent>(
                        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
                    );

            _cts = new CancellationTokenSource();

            OwnedAudioStream stream;
            if (_participant != null && _trackSource.HasValue)
            {
                stream = CreateOwnedStreamFromParticipant();
            }
            else
            {
                stream = CreateOwnedStream();
            }

            _handle = FfiHandle.FromId(stream.Handle.Id);

            // Subscribe to events
            FfiClient.Instance.EventReceived += OnFfiEvent;
        }

        /// <summary>
        /// Creates an AudioStream from a participant's track source.
        /// </summary>
        /// <param name="participant">The participant.</param>
        /// <param name="trackSource">The track source type.</param>
        /// <param name="sampleRate">The sample rate in Hz (default 48000).</param>
        /// <param name="numChannels">The number of channels (default 1).</param>
        /// <param name="frameSizeMs">Optional frame size in milliseconds.</param>
        /// <param name="capacity">The capacity of the internal channel.</param>
        /// <param name="noiseCancellation">Optional noise cancellation options.</param>
        /// <param name="frameProcessor">Optional frame processor for custom audio processing.</param>
        /// <returns>A new AudioStream instance.</returns>
        public static AudioStream FromParticipant(
            Participant participant,
            Proto.TrackSource trackSource,
            uint sampleRate = 48000,
            uint numChannels = 1,
            uint? frameSizeMs = null,
            int capacity = 0,
            NoiseCancellationOptions? noiseCancellation = null,
            FrameProcessor<AudioFrame>? frameProcessor = null
        )
        {
            return new AudioStream(
                participant,
                trackSource,
                sampleRate,
                numChannels,
                frameSizeMs,
                capacity,
                noiseCancellation,
                frameProcessor
            );
        }

        /// <summary>
        /// Creates an AudioStream from an existing track.
        /// </summary>
        /// <param name="track">The track.</param>
        /// <param name="sampleRate">The sample rate in Hz (default 48000).</param>
        /// <param name="numChannels">The number of channels (default 1).</param>
        /// <param name="frameSizeMs">Optional frame size in milliseconds.</param>
        /// <param name="capacity">The capacity of the internal channel.</param>
        /// <param name="noiseCancellation">Optional noise cancellation options.</param>
        /// <param name="frameProcessor">Optional frame processor for custom audio processing.</param>
        /// <returns>A new AudioStream instance.</returns>
        public static AudioStream FromTrack(
            Track track,
            uint sampleRate = 48000,
            uint numChannels = 1,
            uint? frameSizeMs = null,
            int capacity = 0,
            NoiseCancellationOptions? noiseCancellation = null,
            FrameProcessor<AudioFrame>? frameProcessor = null
        )
        {
            return new AudioStream(
                track,
                sampleRate,
                numChannels,
                frameSizeMs,
                capacity,
                noiseCancellation,
                frameProcessor
            );
        }

        /// <summary>
        /// Gets the internal handle.
        /// </summary>
        internal FfiHandle Handle => _handle;

        /// <summary>
        /// Gets the sample rate.
        /// </summary>
        public uint SampleRate => _sampleRate;

        /// <summary>
        /// Gets the number of channels.
        /// </summary>
        public uint NumChannels => _numChannels;

        private OwnedAudioStream CreateOwnedStream()
        {
            if (_track == null)
                throw new InvalidOperationException("Track is required for creating audio stream");

            var request = new FfiRequest
            {
                NewAudioStream = new NewAudioStreamRequest
                {
                    TrackHandle = _track.Handle.HandleId,
                    SampleRate = _sampleRate,
                    NumChannels = _numChannels,
                    Type = AudioStreamType.AudioStreamNative,
                },
            };

            if (_frameSizeMs.HasValue)
            {
                request.NewAudioStream.FrameSizeMs = _frameSizeMs.Value;
            }

            if (_noiseCancellation != null)
            {
                request.NewAudioStream.AudioFilterModuleId = _noiseCancellation.ModuleId;
                if (!string.IsNullOrEmpty(_noiseCancellation.OptionsJson))
                {
                    request.NewAudioStream.AudioFilterOptions = _noiseCancellation.OptionsJson;
                }
            }

            var response = FfiClient.Instance.SendRequest(request);
            return response.NewAudioStream.Stream;
        }

        private OwnedAudioStream CreateOwnedStreamFromParticipant()
        {
            if (_participant == null || !_trackSource.HasValue)
                throw new InvalidOperationException(
                    "Participant and track source are required for creating audio stream"
                );

            var request = new FfiRequest
            {
                AudioStreamFromParticipant = new AudioStreamFromParticipantRequest
                {
                    ParticipantHandle = _participant.Handle.HandleId,
                    TrackSource = (Proto.TrackSource)_trackSource.Value,
                    SampleRate = _sampleRate,
                    NumChannels = _numChannels,
                    Type = AudioStreamType.AudioStreamNative,
                },
            };

            if (_frameSizeMs.HasValue)
            {
                request.AudioStreamFromParticipant.FrameSizeMs = _frameSizeMs.Value;
            }

            if (_noiseCancellation != null)
            {
                request.AudioStreamFromParticipant.AudioFilterModuleId =
                    _noiseCancellation.ModuleId;
                if (!string.IsNullOrEmpty(_noiseCancellation.OptionsJson))
                {
                    request.AudioStreamFromParticipant.AudioFilterOptions =
                        _noiseCancellation.OptionsJson;
                }
            }

            var response = FfiClient.Instance.SendRequest(request);
            return response.AudioStreamFromParticipant.Stream;
        }

        private void OnFfiEvent(object? sender, FfiEvent e)
        {
            if (_disposed)
                return;

            if (e.MessageCase != FfiEvent.MessageOneofCase.AudioStreamEvent)
                return;

            var audioEvent = e.AudioStreamEvent;
            if (audioEvent.StreamHandle != _handle.HandleId)
                return;

            if (audioEvent.MessageCase == AudioStreamEvent.MessageOneofCase.FrameReceived)
            {
                var ownedBuffer = audioEvent.FrameReceived.Frame;
                var frame = AudioFrame.FromOwnedInfo(ownedBuffer);

                // Process frame through FrameProcessor if present and enabled
                if (_frameProcessor != null && _frameProcessor.IsEnabled)
                {
                    try
                    {
                        frame = _frameProcessor.Process(frame);
                    }
                    catch (Exception ex)
                    {
                        // Log warning but pass through original frame
                        System.Diagnostics.Debug.WriteLine(
                            $"Frame processing failed, passing through original frame: {ex.Message}"
                        );
                    }
                }

                // TryWrite returns false if channel is full (for bounded) - frame is dropped
                _channel.Writer.TryWrite(new AudioFrameEvent(frame));
            }
            else if (audioEvent.MessageCase == AudioStreamEvent.MessageOneofCase.Eos)
            {
                _channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Reads the next audio frame event from the stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The next audio frame event, or null if the stream has ended.</returns>
        public async ValueTask<AudioFrameEvent?> ReadAsync(
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _cts.Token
                );
                if (await _channel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    if (_channel.Reader.TryRead(out var frame))
                    {
                        return frame;
                    }
                }
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        /// <summary>
        /// Tries to read the next audio frame event without blocking.
        /// </summary>
        /// <param name="frameEvent">The frame event if available.</param>
        /// <returns>True if a frame was available.</returns>
        public bool TryRead(out AudioFrameEvent frameEvent)
        {
            return _channel.Reader.TryRead(out frameEvent);
        }

        /// <summary>
        /// Returns an async enumerator for iterating over audio frames.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async enumerator of audio frame events.</returns>
        public async IAsyncEnumerator<AudioFrameEvent> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cts.Token
            );

            await foreach (
                var frame in _channel.Reader.ReadAllAsync(linkedCts.Token).ConfigureAwait(false)
            )
            {
                yield return frame;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cts.Cancel();
            FfiClient.Instance.EventReceived -= OnFfiEvent;
            _channel.Writer.TryComplete();
            _frameProcessor?.Close();
            _handle.Dispose();
            _cts.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cts.Cancel();
            FfiClient.Instance.EventReceived -= OnFfiEvent;
            _channel.Writer.TryComplete();

            // Wait for any pending reads to complete
            await Task.Yield();

            _frameProcessor?.Close();
            _handle.Dispose();
            _cts.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
