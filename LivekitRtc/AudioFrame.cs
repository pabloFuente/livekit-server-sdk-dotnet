// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents a frame of audio data with specific properties such as sample rate,
    /// number of channels, and samples per channel.
    /// The format of the audio data is 16-bit signed integers (Int16) interleaved by channel.
    /// </summary>
    public class AudioFrame
    {
        private short[] _data;
        private readonly int _sampleRate;
        private readonly int _numChannels;
        private readonly int _samplesPerChannel;
        private readonly Dictionary<string, object> _userdata;

        /// <summary>
        /// Initialize an AudioFrame instance.
        /// </summary>
        /// <param name="data">The raw audio data as Int16 samples.</param>
        /// <param name="sampleRate">The sample rate of the audio in Hz.</param>
        /// <param name="numChannels">The number of audio channels (e.g., 1 for mono, 2 for stereo).</param>
        /// <param name="samplesPerChannel">The number of samples per channel.</param>
        /// <param name="userdata">Optional user data dictionary.</param>
        public AudioFrame(
            short[] data,
            int sampleRate,
            int numChannels,
            int samplesPerChannel,
            Dictionary<string, object>? userdata = null
        )
        {
            if (data.Length < numChannels * samplesPerChannel)
            {
                throw new ArgumentException(
                    "Data length must be >= numChannels * samplesPerChannel",
                    nameof(data)
                );
            }

            _data = data;
            _sampleRate = sampleRate;
            _numChannels = numChannels;
            _samplesPerChannel = samplesPerChannel;
            _userdata = userdata ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// Initialize an AudioFrame instance from raw bytes.
        /// </summary>
        /// <param name="data">The raw audio data as bytes (must be Int16 samples).</param>
        /// <param name="sampleRate">The sample rate of the audio in Hz.</param>
        /// <param name="numChannels">The number of audio channels.</param>
        /// <param name="samplesPerChannel">The number of samples per channel.</param>
        /// <param name="userdata">Optional user data dictionary.</param>
        public AudioFrame(
            byte[] data,
            int sampleRate,
            int numChannels,
            int samplesPerChannel,
            Dictionary<string, object>? userdata = null
        )
        {
            int requiredBytes = numChannels * samplesPerChannel * sizeof(short);
            if (data.Length < requiredBytes)
            {
                throw new ArgumentException(
                    $"Data length ({data.Length}) must be >= numChannels * samplesPerChannel * sizeof(short) ({requiredBytes})",
                    nameof(data)
                );
            }

            if (data.Length % sizeof(short) != 0)
            {
                throw new ArgumentException(
                    "Data length must be a multiple of sizeof(short)",
                    nameof(data)
                );
            }

            int numSamples = data.Length / sizeof(short);
            _data = new short[numSamples];
            Buffer.BlockCopy(data, 0, _data, 0, data.Length);

            _sampleRate = sampleRate;
            _numChannels = numChannels;
            _samplesPerChannel = samplesPerChannel;
            _userdata = userdata ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// Create a new empty AudioFrame instance with specified parameters.
        /// </summary>
        /// <param name="sampleRate">The sample rate of the audio in Hz.</param>
        /// <param name="numChannels">The number of audio channels.</param>
        /// <param name="samplesPerChannel">The number of samples per channel.</param>
        /// <returns>A new AudioFrame instance with zeroed data.</returns>
        public static AudioFrame Create(int sampleRate, int numChannels, int samplesPerChannel)
        {
            var data = new short[numChannels * samplesPerChannel];
            return new AudioFrame(data, sampleRate, numChannels, samplesPerChannel);
        }

        /// <summary>
        /// Creates an AudioFrame from an owned FFI buffer.
        /// </summary>
        internal static AudioFrame FromOwnedInfo(OwnedAudioFrameBuffer ownedInfo)
        {
            var info = ownedInfo.Info;
            int size = (int)(info.NumChannels * info.SamplesPerChannel);

            // Copy data from native memory
            var data = new short[size];
            unsafe
            {
                var ptr = (short*)info.DataPtr;
                for (int i = 0; i < size; i++)
                {
                    data[i] = ptr[i];
                }
            }

            // Dispose the FFI handle
            using var handle = FfiHandle.FromId(ownedInfo.Handle.Id);

            return new AudioFrame(
                data,
                (int)info.SampleRate,
                (int)info.NumChannels,
                (int)info.SamplesPerChannel
            );
        }

        /// <summary>
        /// Creates a proto info object for FFI communication.
        /// </summary>
        internal AudioFrameBufferInfo ToProtoInfo()
        {
            unsafe
            {
                fixed (short* ptr = _data)
                {
                    return new AudioFrameBufferInfo
                    {
                        DataPtr = (ulong)ptr,
                        SampleRate = (uint)_sampleRate,
                        NumChannels = (uint)_numChannels,
                        SamplesPerChannel = (uint)_samplesPerChannel,
                    };
                }
            }
        }

        /// <summary>
        /// Gets the raw audio data as Int16 samples.
        /// </summary>
        public ReadOnlySpan<short> Data => _data;

        /// <summary>
        /// Gets the raw audio data as an array (for async operations).
        /// </summary>
        public short[] DataArray => _data;

        /// <summary>
        /// Gets the raw audio data as a mutable span.
        /// </summary>
        public Span<short> DataMutable => _data;

        /// <summary>
        /// Gets the raw audio data as bytes.
        /// </summary>
        public byte[] DataBytes
        {
            get
            {
                var bytes = new byte[_data.Length * sizeof(short)];
                Buffer.BlockCopy(_data, 0, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        /// <summary>
        /// Gets the sample rate of the audio frame in Hz.
        /// </summary>
        public int SampleRate => _sampleRate;

        /// <summary>
        /// Gets the number of channels in the audio frame.
        /// </summary>
        public int NumChannels => _numChannels;

        /// <summary>
        /// Gets the number of samples per channel.
        /// </summary>
        public int SamplesPerChannel => _samplesPerChannel;

        /// <summary>
        /// Gets the total number of samples (all channels).
        /// </summary>
        public int TotalSamples => _numChannels * _samplesPerChannel;

        /// <summary>
        /// Gets the duration of the audio frame in seconds.
        /// </summary>
        public double Duration => (double)_samplesPerChannel / _sampleRate;

        /// <summary>
        /// Gets the user data associated with the audio frame.
        /// </summary>
        public Dictionary<string, object> Userdata => _userdata;

        /// <summary>
        /// Converts the audio frame to a WAV file byte array.
        /// </summary>
        /// <returns>The audio data encoded in WAV format.</returns>
        public byte[] ToWavBytes()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            int dataSize = _data.Length * sizeof(short);
            int fileSize = 44 + dataSize;

            // RIFF header
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(fileSize - 8);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });

            // fmt chunk
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16); // chunk size
            writer.Write((short)1); // audio format (PCM)
            writer.Write((short)_numChannels);
            writer.Write(_sampleRate);
            writer.Write(_sampleRate * _numChannels * sizeof(short)); // byte rate
            writer.Write((short)(_numChannels * sizeof(short))); // block align
            writer.Write((short)16); // bits per sample

            // data chunk
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            // Write audio data
            foreach (var sample in _data)
            {
                writer.Write(sample);
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Gets a sample at a specific index.
        /// </summary>
        /// <param name="channel">The channel index (0-based).</param>
        /// <param name="sampleIndex">The sample index within the channel.</param>
        /// <returns>The sample value.</returns>
        public short GetSample(int channel, int sampleIndex)
        {
            if (channel < 0 || channel >= _numChannels)
                throw new ArgumentOutOfRangeException(nameof(channel));
            if (sampleIndex < 0 || sampleIndex >= _samplesPerChannel)
                throw new ArgumentOutOfRangeException(nameof(sampleIndex));

            // Interleaved format
            return _data[sampleIndex * _numChannels + channel];
        }

        /// <summary>
        /// Sets a sample at a specific index.
        /// </summary>
        /// <param name="channel">The channel index (0-based).</param>
        /// <param name="sampleIndex">The sample index within the channel.</param>
        /// <param name="value">The sample value to set.</param>
        public void SetSample(int channel, int sampleIndex, short value)
        {
            if (channel < 0 || channel >= _numChannels)
                throw new ArgumentOutOfRangeException(nameof(channel));
            if (sampleIndex < 0 || sampleIndex >= _samplesPerChannel)
                throw new ArgumentOutOfRangeException(nameof(sampleIndex));

            _data[sampleIndex * _numChannels + channel] = value;
        }

        /// <summary>
        /// Creates a copy of this audio frame.
        /// </summary>
        public AudioFrame Clone()
        {
            var dataCopy = new short[_data.Length];
            Array.Copy(_data, dataCopy, _data.Length);
            return new AudioFrame(
                dataCopy,
                _sampleRate,
                _numChannels,
                _samplesPerChannel,
                new Dictionary<string, object>(_userdata)
            );
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"AudioFrame(sampleRate={_sampleRate}, numChannels={_numChannels}, "
                + $"samplesPerChannel={_samplesPerChannel}, duration={Duration:F3}s)";
        }
    }

    /// <summary>
    /// Utility methods for working with audio frames.
    /// </summary>
    public static class AudioFrameExtensions
    {
        /// <summary>
        /// Combines multiple audio frames into a single frame.
        /// All frames must have the same sample rate and number of channels.
        /// </summary>
        /// <param name="frames">The frames to combine.</param>
        /// <returns>A new AudioFrame containing all the data.</returns>
        public static AudioFrame Combine(this IEnumerable<AudioFrame> frames)
        {
            var frameList = new List<AudioFrame>(frames);

            if (frameList.Count == 0)
                throw new ArgumentException("At least one frame is required", nameof(frames));

            if (frameList.Count == 1)
                return frameList[0].Clone();

            int sampleRate = frameList[0].SampleRate;
            int numChannels = frameList[0].NumChannels;
            int totalSamplesPerChannel = 0;

            foreach (var frame in frameList)
            {
                if (frame.SampleRate != sampleRate)
                    throw new ArgumentException(
                        $"Sample rate mismatch: expected {sampleRate}, got {frame.SampleRate}"
                    );

                if (frame.NumChannels != numChannels)
                    throw new ArgumentException(
                        $"Channel mismatch: expected {numChannels}, got {frame.NumChannels}"
                    );

                totalSamplesPerChannel += frame.SamplesPerChannel;
            }

            var combinedData = new short[numChannels * totalSamplesPerChannel];
            int offset = 0;

            foreach (var frame in frameList)
            {
                Array.Copy(frame.Data.ToArray(), 0, combinedData, offset, frame.TotalSamples);
                offset += frame.TotalSamples;
            }

            return new AudioFrame(combinedData, sampleRate, numChannels, totalSamplesPerChannel);
        }
    }
}
