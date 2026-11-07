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

        private readonly int _queueSizeMs;

        // Playout bookkeeping, shared by CaptureFrameAsync, the drain timer and Dispose
        private readonly object _playoutLock = new object();
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
            _queueSizeMs = queueSizeMs;
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
        /// <param name="cancellationToken">
        /// Cancels the wait for the native source to accept the frame, not the capture itself: the source
        /// keeps consuming the frame, and a capture made before it is done fails with
        /// <see cref="InvalidOperationException"/>. Await each capture before the next one.
        /// </param>
        /// <returns>A task that completes once the native source has accepted the frame.</returns>
        public async Task CaptureFrameAsync(
            AudioFrame frame,
            CancellationToken cancellationToken = default
        )
        {
            if (frame.SamplesPerChannel == 0 || _disposed)
                return;

            var frameDuration = (double)frame.SamplesPerChannel / _sampleRate;
            CancellationTokenSource playoutCts;
            double drainDelaySeconds;
            lock (_playoutLock)
            {
                double now = _stopwatch.Elapsed.TotalSeconds;
                double elapsed = _lastCapture == 0.0 ? 0.0 : now - _lastCapture;
                _queueSize += frameDuration - elapsed;
                _lastCapture = now;

                // Restart the drain timer; anyone already waiting for the playout keeps waiting on
                // the same task (a new one per capture would strand earlier waiters)
                _playoutCts?.Cancel();
                _playoutCts?.Dispose();
                _playoutTcs ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                playoutCts = _playoutCts = new CancellationTokenSource();
                drainDelaySeconds = Math.Max(0.0, Math.Min(_queueSize, int.MaxValue / 1000.0 - 1));
            }

            _ = Task.Delay(TimeSpan.FromSeconds(drainDelaySeconds), playoutCts.Token)
                .ContinueWith(_ => ReleaseWaiter(), TaskContinuationOptions.OnlyOnRanToCompletion);

            // The FFI copies the samples before SendRequest returns. Keep them pinned
            // through request serialization and the native call, but not the callback wait.
            var response = frame.WithPinnedBuffer(buffer =>
                FfiClient.Instance.SendRequest(
                    new FfiRequest
                    {
                        CaptureAudioFrame = new CaptureAudioFrameRequest
                        {
                            SourceHandle = _handle.HandleId,
                            Buffer = buffer,
                        },
                    }
                )
            );
            var asyncId = response.CaptureAudioFrame.AsyncId;

            // The callback only arrives once the whole frame has been accepted into the native
            // queue, which for a long frame takes about its duration minus the queue size: wait
            // for that plus a margin instead of a fixed 10 s (which rejected 15 s clips).
            var timeout =
                TimeSpan.FromSeconds(10)
                + TimeSpan.FromSeconds(frameDuration)
                + TimeSpan.FromMilliseconds(_queueSizeMs);
            // Not resumed on the caller's context: CaptureFrame blocks on this method, which
            // would deadlock a UI thread
            var callbackEvent = await FfiClient
                .Instance.WaitForEventAsync(
                    e => e.CaptureAudioFrame?.AsyncId == asyncId,
                    timeout,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(callbackEvent.CaptureAudioFrame?.Error))
            {
                throw new InvalidOperationException(
                    $"Audio capture failed: {callbackEvent.CaptureAudioFrame.Error}"
                );
            }
        }

        /// <summary>
        /// Captures an AudioFrame and blocks the calling thread until the native source accepts it, which
        /// can take a while when the queue is full. Prefer <see cref="CaptureFrameAsync"/> in asynchronous
        /// code.
        /// </summary>
        /// <param name="frame">The audio frame to capture and queue.</param>
        public void CaptureFrame(AudioFrame frame)
        {
            CaptureFrameAsync(frame).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Waits for the audio source to finish playing out all audio data.
        /// </summary>
        /// <param name="cancellationToken">Cancels this wait only; the queued audio keeps playing.</param>
        /// <returns>A task that completes when all queued audio has been played out.</returns>
        public async Task WaitForPlayoutAsync(CancellationToken cancellationToken = default)
        {
            Task playout;
            lock (_playoutLock)
            {
                if (_playoutTcs == null)
                    return;
                playout = _playoutTcs.Task;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                await playout.ConfigureAwait(false);
                return;
            }

            // Cancel this wait only, not the shared playout task of the other waiters
            var cancelled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            using (cancellationToken.Register(() => cancelled.TrySetCanceled(cancellationToken)))
            {
                var completed = await Task.WhenAny(playout, cancelled.Task).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
        }

        private void ReleaseWaiter()
        {
            TaskCompletionSource<bool>? waiter;
            lock (_playoutLock)
            {
                waiter = _playoutTcs;
                _playoutTcs = null;
                _lastCapture = 0.0;
                _queueSize = 0.0;
            }
            waiter?.TrySetResult(true);
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

        /// <summary>
        /// Releases the native source. Queued audio is dropped and pending
        /// <see cref="WaitForPlayoutAsync"/> calls complete.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Nothing will play out anymore: do not leave WaitForPlayoutAsync callers hanging
            lock (_playoutLock)
            {
                _playoutCts?.Cancel();
                _playoutCts?.Dispose();
                _playoutCts = null;
            }
            ReleaseWaiter();
            _handle.Dispose();
        }
    }
}
