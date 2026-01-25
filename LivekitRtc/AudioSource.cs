// author: https://github.com/pabloFuente

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents a real-time audio source with an internal audio queue.
    /// The AudioSource class allows you to push audio frames into a real-time audio
    /// source, managing an internal queue of audio data.
    /// </summary>
    public class AudioSource : IDisposable
    {
        private readonly int _sampleRate;
        private readonly int _numChannels;
        private readonly FfiHandle _handle;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private double _lastCapture;
        private double _queueSize;
        private TaskCompletionSource<bool>? _playoutTcs;
        private CancellationTokenSource? _playoutCts;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the audio source.
        /// </summary>
        /// <param name="sampleRate">The sample rate of the audio source in Hz.</param>
        /// <param name="numChannels">The number of audio channels.</param>
        /// <param name="queueSizeMs">The buffer size of the audio queue in milliseconds. Defaults to 1000 ms.</param>
        public AudioSource(int sampleRate, int numChannels, int queueSizeMs = 1000)
        {
            _sampleRate = sampleRate;
            _numChannels = numChannels;
            _stopwatch.Start();

            var request = new FfiRequest
            {
                NewAudioSource = new NewAudioSourceRequest
                {
                    Type = AudioSourceType.AudioSourceNative,
                    SampleRate = (uint)sampleRate,
                    NumChannels = (uint)numChannels,
                    QueueSizeMs = (uint)queueSizeMs,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var sourceInfo = response.NewAudioSource.Source;
            _handle = FfiHandle.FromId(sourceInfo.Handle.Id);
        }

        /// <summary>
        /// Gets the sample rate of the audio source in Hz.
        /// </summary>
        public int SampleRate => _sampleRate;

        /// <summary>
        /// Gets the number of audio channels.
        /// </summary>
        public int NumChannels => _numChannels;

        /// <summary>
        /// Gets the internal FFI handle.
        /// </summary>
        internal FfiHandle Handle => _handle;

        /// <summary>
        /// Gets the current duration (in seconds) of audio data queued for playback.
        /// </summary>
        public double QueuedDuration
        {
            get
            {
                double elapsed = _stopwatch.Elapsed.TotalSeconds;
                return Math.Max(_queueSize - elapsed + _lastCapture, 0.0);
            }
        }

        /// <summary>
        /// Clears the internal audio queue, discarding all buffered audio data.
        /// </summary>
        public void ClearQueue()
        {
            var request = new FfiRequest
            {
                ClearAudioBuffer = new ClearAudioBufferRequest { SourceHandle = _handle.HandleId },
            };

            FfiClient.Instance.SendRequest(request);
            ReleaseWaiter();
        }

        /// <summary>
        /// Captures an AudioFrame and queues it for playback.
        /// </summary>
        /// <param name="frame">The audio frame to capture and queue.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public async Task CaptureFrameAsync(
            AudioFrame frame,
            CancellationToken cancellationToken = default
        )
        {
            if (frame.SamplesPerChannel == 0 || _disposed)
                return;

            double now = _stopwatch.Elapsed.TotalSeconds;
            double elapsed = _lastCapture == 0.0 ? 0.0 : now - _lastCapture;
            _queueSize += (double)frame.SamplesPerChannel / _sampleRate - elapsed;
            _lastCapture = now;

            // Cancel any existing playout timer
            _playoutCts?.Cancel();

            // Set up new playout waiter
            _playoutTcs = new TaskCompletionSource<bool>();
            _playoutCts = new CancellationTokenSource();

            // Schedule release after queue empties
            _ = Task.Delay(TimeSpan.FromSeconds(_queueSize), _playoutCts.Token)
                .ContinueWith(_ => ReleaseWaiter(), TaskContinuationOptions.OnlyOnRanToCompletion);

            // Send capture request
            var request = new FfiRequest
            {
                CaptureAudioFrame = new CaptureAudioFrameRequest
                {
                    SourceHandle = _handle.HandleId,
                    Buffer = frame.ToProtoInfo(),
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.CaptureAudioFrame.AsyncId;

            // Wait for the capture callback
            var callbackEvent = await FfiClient.Instance.WaitForEventAsync(
                e => e.CaptureAudioFrame?.AsyncId == asyncId,
                TimeSpan.FromSeconds(10),
                cancellationToken
            );

            if (!string.IsNullOrEmpty(callbackEvent.CaptureAudioFrame?.Error))
            {
                throw new InvalidOperationException(
                    $"Audio capture failed: {callbackEvent.CaptureAudioFrame.Error}"
                );
            }
        }

        /// <summary>
        /// Captures an AudioFrame synchronously.
        /// </summary>
        /// <param name="frame">The audio frame to capture and queue.</param>
        public void CaptureFrame(AudioFrame frame)
        {
            CaptureFrameAsync(frame).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Waits for the audio source to finish playing out all audio data.
        /// </summary>
        public async Task WaitForPlayoutAsync(CancellationToken cancellationToken = default)
        {
            if (_playoutTcs == null)
                return;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            linkedCts.Token.Register(() => _playoutTcs?.TrySetCanceled());

            await _playoutTcs.Task.ConfigureAwait(false);
        }

        private void ReleaseWaiter()
        {
            _playoutTcs?.TrySetResult(true);
            _lastCapture = 0.0;
            _queueSize = 0.0;
            _playoutTcs = null;
        }

        /// <summary>
        /// Creates a local audio track from this audio source.
        /// </summary>
        /// <param name="name">The name of the track.</param>
        /// <returns>A new LocalAudioTrack.</returns>
        public LocalAudioTrack CreateTrack(string name = "audio")
        {
            var request = new FfiRequest
            {
                CreateAudioTrack = new CreateAudioTrackRequest
                {
                    Name = name,
                    SourceHandle = _handle.HandleId,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var trackInfo = response.CreateAudioTrack.Track;

            return new LocalAudioTrack(FfiHandle.FromId(trackInfo.Handle.Id), trackInfo);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _playoutCts?.Cancel();
            _playoutCts?.Dispose();
            _handle.Dispose();
        }
    }
}
