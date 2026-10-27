using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Google.Protobuf;
using LiveKit.Proto;
using Xunit;

namespace LiveKit.Rtc.Tests;

public class AudioFramePinningTests
{
    [Theory]
    [InlineData(48000, 1, 480)]
    [InlineData(48000, 2, 480)]
    [InlineData(16000, 1, 160)]
    public void WithPinnedBuffer_KeepsSamplesValidThroughSerializationAndCompactingGc(
        int sampleRate,
        int channels,
        int samplesPerChannel
    )
    {
        var frame = CreateMovableFrame(sampleRate, channels, samplesPerChannel);

        var copiedSamples = frame.WithPinnedBuffer(buffer =>
        {
            var request = new FfiRequest
            {
                CaptureAudioFrame = new CaptureAudioFrameRequest
                {
                    SourceHandle = 42,
                    Buffer = buffer,
                },
            };
            var serialized = request.ToByteArray();
            var nativeBuffer = FfiRequest.Parser.ParseFrom(serialized).CaptureAudioFrame.Buffer;

            // Simulate GC between serializing DataPtr and the native sample copy.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            Assert.Equal((uint)sampleRate, nativeBuffer.SampleRate);
            Assert.Equal((uint)channels, nativeBuffer.NumChannels);
            Assert.Equal((uint)samplesPerChannel, nativeBuffer.SamplesPerChannel);
            // Check the address before reading it so a regression fails safely.
            Assert.Equal(GetDataPointer(frame.DataArray), nativeBuffer.DataPtr);

            var samples = new short[channels * samplesPerChannel];
            Marshal.Copy((IntPtr)(long)nativeBuffer.DataPtr, samples, 0, samples.Length);
            return samples;
        });

        Assert.Equal(frame.DataArray, copiedSamples);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithPinnedBuffer_DoesNotRootSamplesAfterCallbackReturns(bool throwInCallback)
    {
        var samples = UseFrameAndRelease(throwInCallback);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.False(samples.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AudioFrame CreateMovableFrame(
        int sampleRate,
        int channels,
        int samplesPerChannel
    )
    {
        // Leave a reclaimable allocation before the samples so compacting GC can move them.
        var padding = new byte[64 * 1024];
        var frame = AudioFrame.Create(sampleRate, channels, samplesPerChannel);
        for (int i = 0; i < frame.TotalSamples; i++)
            frame.DataArray[i] = (short)(i % 32767);
        GC.KeepAlive(padding);
        return frame;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe ulong GetDataPointer(short[] samples)
    {
        fixed (short* ptr = samples)
            return (ulong)ptr;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference UseFrameAndRelease(bool throwInCallback)
    {
        var frame = AudioFrame.Create(48000, 1, 480);
        var samples = new WeakReference(frame.DataArray);
        var expected = new InvalidOperationException("Simulated FFI request failure");

        int SendRequest(AudioFrameBufferInfo buffer)
        {
            if (throwInCallback)
                throw expected;
            return 42;
        }

        if (throwInCallback)
        {
            var actual = Assert.Throws<InvalidOperationException>(() =>
                frame.WithPinnedBuffer(SendRequest)
            );
            Assert.Same(expected, actual);
        }
        else
        {
            Assert.Equal(42, frame.WithPinnedBuffer(SendRequest));
        }

        return samples;
    }
}
