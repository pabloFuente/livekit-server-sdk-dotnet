using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Google.Protobuf;
using LiveKit.Proto;
using Xunit;

namespace LiveKit.Rtc.Tests;

public class VideoFramePinningTests
{
    [Theory]
    [InlineData(VideoBufferType.Rgba, 64, 48, 0)]
    [InlineData(VideoBufferType.I420, 64, 48, 3)]
    [InlineData(VideoBufferType.I420A, 64, 48, 4)]
    [InlineData(VideoBufferType.I422, 64, 48, 3)]
    [InlineData(VideoBufferType.I444, 64, 48, 3)]
    [InlineData(VideoBufferType.I010, 64, 48, 3)]
    [InlineData(VideoBufferType.Nv12, 64, 48, 2)]
    public void WithPinnedBuffer_KeepsPixelsValidThroughSerializationAndCompactingGc(
        VideoBufferType type,
        int width,
        int height,
        int planes
    )
    {
        var frame = CreateMovableFrame(type, width, height);

        var copiedPixels = frame.WithPinnedBuffer(buffer =>
        {
            var request = new FfiRequest
            {
                CaptureVideoFrame = new CaptureVideoFrameRequest
                {
                    SourceHandle = 42,
                    Buffer = buffer,
                },
            };
            var serialized = request.ToByteArray();
            var nativeBuffer = FfiRequest.Parser.ParseFrom(serialized).CaptureVideoFrame.Buffer;

            // Simulate GC between serializing the pointers and the native pixel copy.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            Assert.Equal(type, nativeBuffer.Type);
            Assert.Equal((uint)width, nativeBuffer.Width);
            Assert.Equal((uint)height, nativeBuffer.Height);
            Assert.Equal(planes, nativeBuffer.Components.Count);

            // Check every address before reading it so a regression fails safely.
            var start = GetDataPointer(frame.DataBytes);
            var end = start + (ulong)frame.DataBytes.Length;
            Assert.Equal(start, nativeBuffer.DataPtr);
            foreach (var component in nativeBuffer.Components)
                Assert.InRange(component.DataPtr, start, end - component.Size);

            for (int i = 0; i < nativeBuffer.Components.Count; i++)
            {
                var component = nativeBuffer.Components[i];
                var plane = new byte[component.Size];
                Marshal.Copy((IntPtr)(long)component.DataPtr, plane, 0, plane.Length);
                Assert.Equal(frame.GetPlane(i), plane);
            }

            var pixels = new byte[frame.DataBytes.Length];
            Marshal.Copy((IntPtr)(long)nativeBuffer.DataPtr, pixels, 0, pixels.Length);
            return pixels;
        });

        Assert.Equal(frame.DataBytes, copiedPixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithPinnedBuffer_DoesNotRootPixelsAfterCallbackReturns(bool throwInCallback)
    {
        var pixels = UseFrameAndRelease(throwInCallback);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.False(pixels.IsAlive);
    }

    [Fact]
    public void CompactingGc_MovesUnpinnedPixels()
    {
        // Precondition of the pinning test above: it can only detect a stale pointer if a
        // compacting GC really moves the pixels when they are not pinned.
        var frame = CreateMovableFrame(VideoBufferType.Rgba, 64, 48);
        var before = GetDataPointer(frame.DataBytes);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.NotEqual(before, GetDataPointer(frame.DataBytes));
        GC.KeepAlive(frame);
    }

    [Fact]
    public void Convert_ThroughNativeFfi_ProducesConvertedFrame()
    {
        var white = CreateSolidRgbaFrame(64, 48, 255);
        var black = CreateSolidRgbaFrame(64, 48, 0);

        var whiteI420 = white.Convert(VideoBufferType.I420);
        var blackI420 = black.Convert(VideoBufferType.I420);

        Assert.Equal(VideoBufferType.I420, whiteI420.Type);
        Assert.Equal(64, whiteI420.Width);
        Assert.Equal(48, whiteI420.Height);
        // Luma differs enough between white and black to prove the native side read the pixels.
        Assert.True(whiteI420.GetPlane(0)![0] > 200);
        Assert.True(blackI420.GetPlane(0)![0] < 50);
    }

    [Theory]
    [InlineData(VideoBufferType.Rgba)]
    [InlineData(VideoBufferType.I420)]
    [InlineData(VideoBufferType.I420A)]
    [InlineData(VideoBufferType.I422)]
    [InlineData(VideoBufferType.I444)]
    [InlineData(VideoBufferType.I010)]
    [InlineData(VideoBufferType.Nv12)]
    public void CaptureFrame_ThroughNativeFfi_DoesNotThrow(VideoBufferType type)
    {
        using var videoSource = new VideoSource(64, 48);
        var frame = CreateMovableFrame(type, 64, 48);

        videoSource.CaptureFrame(frame);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static VideoFrame CreateMovableFrame(VideoBufferType type, int width, int height)
    {
        // Leave a reclaimable allocation before the pixels so compacting GC can move them.
        var padding = new byte[64 * 1024];
        var frame = VideoFrame.Create(width, height, type);
        for (int i = 0; i < frame.DataBytes.Length; i++)
            frame.DataBytes[i] = (byte)(i % 251);
        GC.KeepAlive(padding);
        return frame;
    }

    private static VideoFrame CreateSolidRgbaFrame(int width, int height, byte value)
    {
        var frame = VideoFrame.Create(width, height, VideoBufferType.Rgba);
        for (int i = 0; i < frame.DataBytes.Length; i += 4)
        {
            frame.DataBytes[i] = value;
            frame.DataBytes[i + 1] = value;
            frame.DataBytes[i + 2] = value;
            frame.DataBytes[i + 3] = 255;
        }
        return frame;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe ulong GetDataPointer(byte[] pixels)
    {
        fixed (byte* ptr = pixels)
            return (ulong)ptr;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference UseFrameAndRelease(bool throwInCallback)
    {
        var frame = VideoFrame.Create(64, 48, VideoBufferType.I420);
        var pixels = new WeakReference(frame.DataBytes);
        var expected = new InvalidOperationException("Simulated FFI request failure");

        int SendRequest(VideoBufferInfo buffer)
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

        return pixels;
    }
}
