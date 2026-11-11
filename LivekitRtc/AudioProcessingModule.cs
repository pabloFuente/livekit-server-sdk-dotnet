// author: https://github.com/pabloFuente

using System;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// WebRTC audio processing (echo cancellation, noise suppression, high-pass filter and automatic
    /// gain control) applied in place to 16-bit audio, in frames of 10 ms (or a whole number of 10 ms
    /// periods).
    /// </summary>
    /// <example>
    /// Clean the audio captured from a microphone before publishing it, cancelling the echo of what
    /// the speaker plays:
    /// <code language="csharp">
    /// using var apm = new AudioProcessingModule(echoCancellation: true, noiseSuppression: true);
    /// apm.SetStreamDelayMs(40);
    ///
    /// apm.ProcessReverseStream(playedFrame); // the 10 ms just sent to the speaker
    /// apm.ProcessStream(capturedFrame); // the 10 ms just captured, processed in place
    /// await source.CaptureFrameAsync(capturedFrame);
    /// </code>
    /// </example>
    public class AudioProcessingModule : IDisposable
    {
        private readonly FfiHandle _handle;

        // The module writes into the frame during the request: a request and the release of the
        // handle must not interleave between threads
        private readonly object _lock = new object();
        private bool _disposed;

        /// <summary>
        /// Creates an audio processing module with the given processing enabled.
        /// </summary>
        /// <param name="echoCancellation">Whether to cancel the echo of the reverse stream.</param>
        /// <param name="noiseSuppression">Whether to suppress background noise.</param>
        /// <param name="highPassFilter">Whether to remove low-frequency content, such as hum.</param>
        /// <param name="autoGainControl">Whether to adjust the level automatically.</param>
        public AudioProcessingModule(
            bool echoCancellation = false,
            bool noiseSuppression = false,
            bool highPassFilter = false,
            bool autoGainControl = false
        )
        {
            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    NewApm = new NewApmRequest
                    {
                        EchoCancellerEnabled = echoCancellation,
                        NoiseSuppressionEnabled = noiseSuppression,
                        HighPassFilterEnabled = highPassFilter,
                        GainControllerEnabled = autoGainControl,
                    },
                }
            );
            _handle = FfiHandle.FromId(response.NewApm.Apm.Handle.Id);
        }

        /// <summary>
        /// Processes the near-end audio, the one captured locally, in place.
        /// </summary>
        /// <param name="frame">10 ms of audio, overwritten with the processed audio.</param>
        /// <exception cref="ArgumentException">The frame is not a whole number of 10 ms periods.</exception>
        /// <exception cref="ObjectDisposedException">The module was disposed.</exception>
        /// <exception cref="InvalidOperationException">The module rejected the frame, for example
        /// because of its sample rate.</exception>
        public void ProcessStream(AudioFrame frame) => Process(frame, reverse: false);

        /// <summary>
        /// Processes the far-end audio, the one played locally, which the echo canceller removes from
        /// the near-end audio. Call it with each frame before it is played.
        /// </summary>
        /// <param name="frame">10 ms of audio, which the module may modify in place.</param>
        /// <exception cref="ArgumentException">The frame is not a whole number of 10 ms periods.</exception>
        /// <exception cref="ObjectDisposedException">The module was disposed.</exception>
        /// <exception cref="InvalidOperationException">The module rejected the frame, for example
        /// because of its sample rate.</exception>
        public void ProcessReverseStream(AudioFrame frame) => Process(frame, reverse: true);

        /// <summary>
        /// Sets the delay between a far-end frame passed to <see cref="ProcessReverseStream"/> and the
        /// near-end frame passed to <see cref="ProcessStream"/> that contains its echo:
        /// <c>(t_render - t_analyze) + (t_process - t_capture)</c>, where <c>t_analyze</c> is when the
        /// frame was passed to <see cref="ProcessReverseStream"/> and <c>t_render</c> when the audio
        /// hardware played its first sample, <c>t_capture</c> when the hardware captured the first
        /// sample of a frame and <c>t_process</c> when it was passed to <see cref="ProcessStream"/>.
        /// Call it if and only if echo cancellation is enabled.
        /// </summary>
        /// <param name="delayMs">The delay in milliseconds.</param>
        /// <exception cref="ObjectDisposedException">The module was disposed.</exception>
        /// <exception cref="InvalidOperationException">The module rejected the delay.</exception>
        public void SetStreamDelayMs(int delayMs)
        {
            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(AudioProcessingModule));
                var response = FfiClient.Instance.SendRequest(
                    new FfiRequest
                    {
                        ApmSetStreamDelay = new ApmSetStreamDelayRequest
                        {
                            ApmHandle = _handle.HandleId,
                            DelayMs = delayMs,
                        },
                    }
                );
                ThrowIfError(response.ApmSetStreamDelay.Error);
            }
        }

        private unsafe void Process(AudioFrame frame, bool reverse)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            // The native module aborts the process (a Rust panic) on a buffer that is not a whole
            // number of 10 ms periods, so it must never receive one
            var data = frame.DataArray;
            var totalSamples = frame.TotalSamples;
            var samplesPer10Ms = (long)(frame.SampleRate / 100) * frame.NumChannels;
            if (samplesPer10Ms <= 0 || totalSamples <= 0 || totalSamples % samplesPer10Ms != 0)
                throw new ArgumentException(
                    $"The frame must hold a whole number of 10 ms periods; it holds {frame.SamplesPerChannel} samples per channel at {frame.SampleRate} Hz with {frame.NumChannels} channels.",
                    nameof(frame)
                );

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(AudioProcessingModule));

                string error;

                // The module reads and writes the samples during the request: they stay pinned
                // until it returned
                fixed (short* samples = data)
                {
                    var dataPtr = (ulong)samples;
                    var size = (uint)(totalSamples * sizeof(short));
                    var sampleRate = (uint)frame.SampleRate;
                    var numChannels = (uint)frame.NumChannels;
                    if (reverse)
                    {
                        error = FfiClient
                            .Instance.SendRequest(
                                new FfiRequest
                                {
                                    ApmProcessReverseStream = new ApmProcessReverseStreamRequest
                                    {
                                        ApmHandle = _handle.HandleId,
                                        DataPtr = dataPtr,
                                        Size = size,
                                        SampleRate = sampleRate,
                                        NumChannels = numChannels,
                                    },
                                }
                            )
                            .ApmProcessReverseStream.Error;
                    }
                    else
                    {
                        error = FfiClient
                            .Instance.SendRequest(
                                new FfiRequest
                                {
                                    ApmProcessStream = new ApmProcessStreamRequest
                                    {
                                        ApmHandle = _handle.HandleId,
                                        DataPtr = dataPtr,
                                        Size = size,
                                        SampleRate = sampleRate,
                                        NumChannels = numChannels,
                                    },
                                }
                            )
                            .ApmProcessStream.Error;
                    }
                }

                ThrowIfError(error);
            }
        }

        private static void ThrowIfError(string error)
        {
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException($"Audio processing failed: {error}");
        }

        /// <summary>
        /// Releases the native module. A processing call in progress on another thread finishes
        /// first.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _handle.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
