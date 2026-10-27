using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;
using Xunit;

namespace LiveKit.Rtc.Tests;

public class VideoFramePlanesTests
{
    // Plane layouts for a 5x3 frame (odd size, so chroma dimensions are rounded up), listed by hand
    // from the FFI contract: strides and sizes are in bytes, planes are contiguous, and I420A
    // appends the alpha plane.
    [Theory]
    [InlineData(VideoBufferType.I420, new[] { 5, 3, 3 }, new[] { 15, 6, 6 })]
    [InlineData(VideoBufferType.I420A, new[] { 5, 3, 3, 5 }, new[] { 15, 6, 6, 15 })]
    [InlineData(VideoBufferType.I422, new[] { 5, 3, 3 }, new[] { 15, 9, 9 })]
    [InlineData(VideoBufferType.I444, new[] { 5, 5, 5 }, new[] { 15, 15, 15 })]
    [InlineData(VideoBufferType.I010, new[] { 10, 6, 6 }, new[] { 30, 12, 12 })]
    [InlineData(VideoBufferType.Nv12, new[] { 5, 6 }, new[] { 15, 12 })]
    public void WithPinnedBuffer_DescribesEveryPlane(
        VideoBufferType type,
        int[] strides,
        int[] sizes
    )
    {
        var frame = VideoFrame.Create(5, 3, type);
        for (int i = 0; i < frame.DataBytes.Length; i++)
            frame.DataBytes[i] = (byte)(i + 1);
        Assert.Equal(sizes.Sum(), frame.DataBytes.Length);

        frame.WithPinnedBuffer(buffer =>
        {
            Assert.Equal(strides.Length, buffer.Components.Count);

            var expectedPtr = buffer.DataPtr;
            for (int i = 0; i < sizes.Length; i++)
            {
                var component = buffer.Components[i];
                Assert.Equal((uint)strides[i], component.Stride);
                Assert.Equal((uint)sizes[i], component.Size);
                Assert.Equal(expectedPtr, component.DataPtr);
                expectedPtr += component.Size;

                var plane = new byte[component.Size];
                Marshal.Copy((IntPtr)(long)component.DataPtr, plane, 0, plane.Length);
                Assert.Equal(frame.GetPlane(i), plane);
            }
            return 0;
        });
    }

    [Theory]
    [InlineData(VideoBufferType.I420A, 64, 48, new[] { 3072, 768, 768, 3072 }, false)]
    [InlineData(VideoBufferType.I422, 64, 48, new[] { 3072, 1536, 1536 }, false)]
    [InlineData(VideoBufferType.I444, 64, 48, new[] { 3072, 3072, 3072 }, false)]
    [InlineData(VideoBufferType.I010, 64, 48, new[] { 6144, 1536, 1536 }, true)]
    [InlineData(VideoBufferType.I420A, 5, 3, new[] { 15, 6, 6, 15 }, false)]
    [InlineData(VideoBufferType.I422, 5, 3, new[] { 15, 9, 9 }, false)]
    [InlineData(VideoBufferType.I444, 5, 3, new[] { 15, 15, 15 }, false)]
    [InlineData(VideoBufferType.I010, 5, 3, new[] { 30, 12, 12 }, true)]
    public void Convert_FromPlanarFormat_ToI420_KeepsWhiteFrame(
        VideoBufferType type,
        int width,
        int height,
        int[] planeSizes,
        bool tenBit
    )
    {
        var frame = CreateWhiteFrame(type, width, height, planeSizes, tenBit);

        var i420 = frame.Convert(VideoBufferType.I420);

        Assert.Equal(VideoBufferType.I420, i420.Type);
        Assert.Equal(width, i420.Width);
        Assert.Equal(height, i420.Height);
        Assert.All(i420.GetPlane(0)!, y => Assert.InRange(y, 232, 238));
        Assert.All(i420.GetPlane(1)!, u => Assert.InRange(u, 125, 131));
        Assert.All(i420.GetPlane(2)!, v => Assert.InRange(v, 125, 131));
    }

    // White in limited-range YUV: Y=235, U=V=128 (8 bit) or Y=940, U=V=512 (10 bit, little endian).
    private static VideoFrame CreateWhiteFrame(
        VideoBufferType type,
        int width,
        int height,
        int[] planeSizes,
        bool tenBit
    )
    {
        var frame = VideoFrame.Create(width, height, type);
        Assert.Equal(planeSizes.Sum(), frame.DataBytes.Length);

        int offset = 0;
        for (int plane = 0; plane < planeSizes.Length; plane++)
        {
            bool isAlpha = type == VideoBufferType.I420A && plane == 3;
            int value =
                isAlpha ? 255
                : plane == 0 ? (tenBit ? 940 : 235)
                : (tenBit ? 512 : 128);

            if (tenBit)
            {
                for (int i = 0; i < planeSizes[plane]; i += 2)
                {
                    frame.DataBytes[offset + i] = (byte)(value & 0xFF);
                    frame.DataBytes[offset + i + 1] = (byte)(value >> 8);
                }
            }
            else
            {
                Array.Fill(frame.DataBytes, (byte)value, offset, planeSizes[plane]);
            }
            offset += planeSizes[plane];
        }
        return frame;
    }
}
