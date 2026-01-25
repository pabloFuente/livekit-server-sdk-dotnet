// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Interface for audio streams that can be mixed.
    /// </summary>
    public interface IAudioStreamSource
    {
        /// <summary>
        /// Reads the next audio frame from the stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The next audio frame, or null if the stream is complete.</returns>
        ValueTask<AudioFrame?> ReadAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Mixes multiple audio streams into a single output stream.
    /// Implements IAsyncEnumerable for modern async iteration patterns.
    /// </summary>
    public class AudioMixer : IAsyncDisposable, IDisposable, IAsyncEnumerable<AudioFrame>
    {
        private readonly uint _sampleRate;
        private readonly uint _numChannels;
        private readonly int _chunkSize;
        private readonly int _streamTimeoutMs;
        private readonly List<IAudioStreamSource> _streams;
        private readonly Dictionary<IAudioStreamSource, short[]> _buffers;
        private readonly Channel<AudioFrame> _outputChannel;
        private readonly CancellationTokenSource _cts;
        private readonly Task _mixerTask;
        private readonly object _lock = new object();
        private bool _ending;
        private bool _disposed;

        /// <summary>
        /// Initializes a new AudioMixer.
        /// </summary>
        /// <param name="sampleRate">The audio sample rate in Hz.</param>
        /// <param name="numChannels">The number of audio channels.</param>
        /// <param name="blockSize">The size of the audio block (in samples) for mixing. If 0, defaults to sampleRate/10.</param>
        /// <param name="streamTimeoutMs">The maximum wait time in milliseconds for each stream to provide audio data.</param>
        /// <param name="capacity">The maximum number of mixed frames to store in the output channel.</param>
        public AudioMixer(
            uint sampleRate,
            uint numChannels,
            int blockSize = 0,
            int streamTimeoutMs = 100,
            int capacity = 100
        )
        {
            _sampleRate = sampleRate;
            _numChannels = numChannels;
            _chunkSize = blockSize > 0 ? blockSize : (int)(sampleRate / 10);
            _streamTimeoutMs = streamTimeoutMs;
            _streams = new List<IAudioStreamSource>();
            _buffers = new Dictionary<IAudioStreamSource, short[]>();

            _outputChannel =
                capacity > 0
                    ? Channel.CreateBounded<AudioFrame>(
                        new BoundedChannelOptions(capacity)
                        {
                            FullMode = BoundedChannelFullMode.Wait,
                            SingleReader = false,
                            SingleWriter = true,
                        }
                    )
                    : Channel.CreateUnbounded<AudioFrame>(
                        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
                    );

            _cts = new CancellationTokenSource();
            _mixerTask = Task.Run(MixerLoopAsync);
        }

        /// <summary>
        /// Gets the sample rate.
        /// </summary>
        public uint SampleRate => _sampleRate;

        /// <summary>
        /// Gets the number of channels.
        /// </summary>
        public uint NumChannels => _numChannels;

        /// <summary>
        /// Gets the chunk size in samples.
        /// </summary>
        public int ChunkSize => _chunkSize;

        /// <summary>
        /// Gets the current number of active streams.
        /// </summary>
        public int StreamCount
        {
            get
            {
                lock (_lock)
                {
                    return _streams.Count;
                }
            }
        }

        /// <summary>
        /// Adds an audio stream to the mixer.
        /// </summary>
        /// <param name="stream">The audio stream to add.</param>
        public void AddStream(IAudioStreamSource stream)
        {
            if (_ending)
                throw new InvalidOperationException(
                    "Cannot add stream after mixer has been closed"
                );

            lock (_lock)
            {
                if (!_streams.Contains(stream))
                {
                    _streams.Add(stream);
                    _buffers[stream] = Array.Empty<short>();
                }
            }
        }

        /// <summary>
        /// Removes an audio stream from the mixer.
        /// </summary>
        /// <param name="stream">The audio stream to remove.</param>
        public void RemoveStream(IAudioStreamSource stream)
        {
            lock (_lock)
            {
                _streams.Remove(stream);
                _buffers.Remove(stream);
            }
        }

        /// <summary>
        /// Reads the next mixed audio frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The next mixed audio frame, or null if the mixer is complete.</returns>
        public async ValueTask<AudioFrame?> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _cts.Token
                );
                if (
                    await _outputChannel
                        .Reader.WaitToReadAsync(linkedCts.Token)
                        .ConfigureAwait(false)
                )
                {
                    if (_outputChannel.Reader.TryRead(out var frame))
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
        /// Tries to read the next mixed audio frame without blocking.
        /// </summary>
        /// <param name="frame">The frame if available.</param>
        /// <returns>True if a frame was available.</returns>
        public bool TryRead(out AudioFrame? frame)
        {
            if (_outputChannel.Reader.TryRead(out var f))
            {
                frame = f;
                return true;
            }
            frame = null;
            return false;
        }

        /// <summary>
        /// Returns an async enumerator for iterating over mixed audio frames.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async enumerator of audio frames.</returns>
        public async IAsyncEnumerator<AudioFrame> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cts.Token
            );

            await foreach (
                var frame in _outputChannel
                    .Reader.ReadAllAsync(linkedCts.Token)
                    .ConfigureAwait(false)
            )
            {
                yield return frame;
            }
        }

        /// <summary>
        /// Signals that no more streams will be added.
        /// Existing streams will still be processed until exhausted.
        /// </summary>
        public void EndInput()
        {
            _ending = true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _ending = true;
            _cts.Cancel();

            try
            {
                _mixerTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // Expected
            }

            _outputChannel.Writer.TryComplete();
            _cts.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            _ending = true;
            _cts.Cancel();

            try
            {
                await _mixerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            _outputChannel.Writer.TryComplete();
            _cts.Dispose();

            GC.SuppressFinalize(this);
        }

        private async Task MixerLoopAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    List<IAudioStreamSource> currentStreams;
                    lock (_lock)
                    {
                        if (_ending && _streams.Count == 0)
                            break;

                        currentStreams = new List<IAudioStreamSource>(_streams);
                    }

                    if (currentStreams.Count == 0)
                    {
                        await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    // Gather contributions from all streams
                    var contributions = new List<short[]>();
                    var streamsToRemove = new List<IAudioStreamSource>();
                    bool anyData = false;

                    foreach (var stream in currentStreams)
                    {
                        var (data, buffer, hadData, exhausted) = await GetContributionAsync(stream)
                            .ConfigureAwait(false);

                        lock (_lock)
                        {
                            if (_buffers.ContainsKey(stream))
                            {
                                _buffers[stream] = buffer;
                            }
                        }

                        contributions.Add(data);
                        if (hadData)
                            anyData = true;

                        if (exhausted && buffer.Length == 0)
                            streamsToRemove.Add(stream);
                    }

                    foreach (var stream in streamsToRemove)
                    {
                        RemoveStream(stream);
                    }

                    if (!anyData)
                    {
                        await Task.Delay(1, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    // Mix all contributions
                    var mixed = MixAudio(contributions);
                    var frame = new AudioFrame(
                        mixed,
                        (int)_sampleRate,
                        (int)_numChannels,
                        _chunkSize
                    );

                    await _outputChannel.Writer.WriteAsync(frame, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            finally
            {
                _outputChannel.Writer.TryComplete();
            }
        }

        private async ValueTask<(
            short[] data,
            short[] buffer,
            bool hadData,
            bool exhausted
        )> GetContributionAsync(IAudioStreamSource stream)
        {
            short[] buffer;
            lock (_lock)
            {
                buffer = _buffers.TryGetValue(stream, out var b) ? b : Array.Empty<short>();
            }

            bool hadData = buffer.Length > 0;
            bool exhausted = false;
            int samplesNeeded = _chunkSize * (int)_numChannels;

            while (buffer.Length < samplesNeeded && !exhausted)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(_streamTimeoutMs);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        timeoutCts.Token,
                        _cts.Token
                    );

                    var frame = await stream.ReadAsync(linkedCts.Token).ConfigureAwait(false);
                    if (frame == null)
                    {
                        exhausted = true;
                        break;
                    }

                    var newData = frame.DataArray;
                    var combined = new short[buffer.Length + newData.Length];
                    Array.Copy(buffer, 0, combined, 0, buffer.Length);
                    Array.Copy(newData, 0, combined, buffer.Length, newData.Length);
                    buffer = combined;
                    hadData = true;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            short[] contribution;
            if (buffer.Length >= samplesNeeded)
            {
                contribution = new short[samplesNeeded];
                Array.Copy(buffer, 0, contribution, 0, samplesNeeded);

                var remaining = new short[buffer.Length - samplesNeeded];
                Array.Copy(buffer, samplesNeeded, remaining, 0, remaining.Length);
                buffer = remaining;
            }
            else
            {
                // Pad with zeros
                contribution = new short[samplesNeeded];
                Array.Copy(buffer, 0, contribution, 0, buffer.Length);
                buffer = Array.Empty<short>();
            }

            return (contribution, buffer, hadData, exhausted);
        }

        private short[] MixAudio(List<short[]> contributions)
        {
            if (contributions.Count == 0)
                return new short[_chunkSize * _numChannels];

            int length = contributions[0].Length;
            var mixed = new int[length];

            foreach (var contribution in contributions)
            {
                for (int i = 0; i < Math.Min(length, contribution.Length); i++)
                {
                    mixed[i] += contribution[i];
                }
            }

            var result = new short[length];
            for (int i = 0; i < length; i++)
            {
                // Clip to int16 range
                result[i] = (short)Math.Max(-32768, Math.Min(32767, mixed[i]));
            }

            return result;
        }
    }
}
