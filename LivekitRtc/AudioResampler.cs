// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// A class for resampling audio data from one sample rate to another.
    /// Uses the Sox resampling library under the hood.
    /// </summary>
    public class AudioResampler : IDisposable
    {
        private readonly FfiHandle _handle;
        private readonly uint _inputRate;
        private readonly uint _outputRate;
        private readonly uint _numChannels;

        // The FFI returns a pointer into the resampler's own output buffer, which the next
        // Push/Flush overwrites: the request and the copy must not interleave between threads.
        private readonly object _lock = new object();
        private bool _disposed;

        /// <summary>
        /// Initializes a new AudioResampler instance.
        /// </summary>
        /// <param name="inputRate">The sample rate of the input audio data (in Hz).</param>
        /// <param name="outputRate">The desired sample rate of the output audio data (in Hz).</param>
        /// <param name="numChannels">The number of audio channels (default 1).</param>
        /// <param name="quality">The quality setting for the resampler (default Medium).</param>
        public AudioResampler(
            uint inputRate,
            uint outputRate,
            uint numChannels = 1,
            SoxQualityRecipe quality = SoxQualityRecipe.SoxrQualityMedium
        )
        {
            _inputRate = inputRate;
            _outputRate = outputRate;
            _numChannels = numChannels;

            var request = new FfiRequest
            {
                NewSoxResampler = new NewSoxResamplerRequest
                {
                    InputRate = inputRate,
                    OutputRate = outputRate,
                    NumChannels = numChannels,
                    QualityRecipe = quality,
                    InputDataType = SoxResamplerDataType.SoxrDatatypeInt16I,
                    OutputDataType = SoxResamplerDataType.SoxrDatatypeInt16I,
                    Flags = 0,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (!string.IsNullOrEmpty(response.NewSoxResampler.Error))
            {
                throw new InvalidOperationException(
                    $"Failed to create resampler: {response.NewSoxResampler.Error}"
                );
            }

            _handle = FfiHandle.FromId(response.NewSoxResampler.Resampler.Handle.Id);
        }

        /// <summary>
        /// Gets the input sample rate.
        /// </summary>
        public uint InputRate => _inputRate;

        /// <summary>
        /// Gets the output sample rate.
        /// </summary>
        public uint OutputRate => _outputRate;

        /// <summary>
        /// Gets the number of channels.
        /// </summary>
        public uint NumChannels => _numChannels;

        /// <summary>
        /// Push audio data into the resampler and retrieve any available resampled data.
        /// </summary>
        /// <param name="frame">The audio frame to resample.</param>
        /// <returns>A list of resampled audio frames.</returns>
        public List<AudioFrame> Push(AudioFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.NumChannels != _numChannels)
                throw new ArgumentException(
                    $"The resampler takes {_numChannels}-channel audio, the frame has {frame.NumChannels} channels",
                    nameof(frame)
                );
            if (frame.SampleRate != _inputRate)
                throw new ArgumentException(
                    $"The resampler takes {_inputRate} Hz audio, the frame is {frame.SampleRate} Hz",
                    nameof(frame)
                );
            return Push(frame.DataBytes);
        }

        /// <summary>
        /// Push audio data into the resampler and retrieve any available resampled data.
        /// </summary>
        /// <param name="data">The raw audio data in int16le format.</param>
        /// <returns>A list of resampled audio frames.</returns>
        public unsafe List<AudioFrame> Push(byte[] data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioResampler));

            if (data == null || data.Length == 0)
                return new List<AudioFrame>();

            lock (_lock)
                fixed (byte* dataPtr = data)
                {
                    var request = new FfiRequest
                    {
                        PushSoxResampler = new PushSoxResamplerRequest
                        {
                            ResamplerHandle = _handle.HandleId,
                            DataPtr = (ulong)dataPtr,
                            Size = (uint)data.Length,
                        },
                    };

                    var response = FfiClient.Instance.SendRequest(request);

                    if (!string.IsNullOrEmpty(response.PushSoxResampler.Error))
                    {
                        throw new InvalidOperationException(
                            $"Resampling failed: {response.PushSoxResampler.Error}"
                        );
                    }

                    if (response.PushSoxResampler.OutputPtr == 0)
                    {
                        return new List<AudioFrame>();
                    }

                    var outputData = new byte[response.PushSoxResampler.Size];
                    Marshal.Copy(
                        (IntPtr)response.PushSoxResampler.OutputPtr,
                        outputData,
                        0,
                        (int)response.PushSoxResampler.Size
                    );

                    var samplesPerChannel = outputData.Length / ((int)_numChannels * sizeof(short));
                    var frame = new AudioFrame(
                        outputData,
                        (int)_outputRate,
                        (int)_numChannels,
                        samplesPerChannel
                    );

                    return new List<AudioFrame> { frame };
                }
        }

        /// <summary>
        /// Flush any remaining audio data through the resampler.
        /// Call this when no more input data will be provided.
        /// </summary>
        /// <returns>A list of remaining resampled audio frames.</returns>
        public List<AudioFrame> Flush()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioResampler));

            var request = new FfiRequest
            {
                FlushSoxResampler = new FlushSoxResamplerRequest
                {
                    ResamplerHandle = _handle.HandleId,
                },
            };

            lock (_lock)
            {
                var response = FfiClient.Instance.SendRequest(request);

                if (!string.IsNullOrEmpty(response.FlushSoxResampler.Error))
                {
                    throw new InvalidOperationException(
                        $"Resampling failed: {response.FlushSoxResampler.Error}"
                    );
                }

                // An empty flush returns a valid pointer with a size of 0: no frame to return
                if (
                    response.FlushSoxResampler.OutputPtr == 0
                    || response.FlushSoxResampler.Size == 0
                )
                {
                    return new List<AudioFrame>();
                }

                var outputData = new byte[response.FlushSoxResampler.Size];
                Marshal.Copy(
                    (IntPtr)response.FlushSoxResampler.OutputPtr,
                    outputData,
                    0,
                    (int)response.FlushSoxResampler.Size
                );

                var samplesPerChannel = outputData.Length / ((int)_numChannels * sizeof(short));
                var frame = new AudioFrame(
                    outputData,
                    (int)_outputRate,
                    (int)_numChannels,
                    samplesPerChannel
                );

                return new List<AudioFrame> { frame };
            }
        }

        /// <summary>
        /// Releases the native resampler. Call <see cref="Flush"/> first to get the audio it still buffers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _handle.Dispose();
        }
    }
}
