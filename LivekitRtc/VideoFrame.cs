// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents a video frame with associated metadata and pixel data.
    /// </summary>
    public class VideoFrame
    {
        private readonly int _width;
        private readonly int _height;
        private readonly Proto.VideoBufferType _type;
        private readonly byte[] _data;
        private readonly Dictionary<string, object> _userdata;

        /// <summary>
        /// Initializes a new VideoFrame instance.
        /// </summary>
        /// <param name="width">The width of the video frame in pixels.</param>
        /// <param name="height">The height of the video frame in pixels.</param>
        /// <param name="type">The format type of the video frame data.</param>
        /// <param name="data">The raw pixel data for the video frame.</param>
        /// <param name="userdata">Optional user data dictionary.</param>
        public VideoFrame(
            int width,
            int height,
            Proto.VideoBufferType type,
            byte[] data,
            Dictionary<string, object>? userdata = null
        )
        {
            int requiredSize = GetBufferSize(type, width, height);
            if (data.Length < requiredSize)
            {
                throw new ArgumentException(
                    $"Data length ({data.Length}) must be >= required size ({requiredSize}) for {type} format",
                    nameof(data)
                );
            }

            _width = width;
            _height = height;
            _type = type;
            _data = data;
            _userdata = userdata ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// Creates a new empty VideoFrame with the specified parameters.
        /// </summary>
        public static VideoFrame Create(int width, int height, VideoBufferType type)
        {
            int size = GetBufferSize(type, width, height);
            var data = new byte[size];
            return new VideoFrame(width, height, type, data);
        }

        /// <summary>
        /// Creates a VideoFrame from an owned FFI buffer.
        /// </summary>
        internal static VideoFrame FromOwnedInfo(OwnedVideoBuffer ownedInfo)
        {
            var info = ownedInfo.Info;
            int dataLen = GetBufferSize(
                (VideoBufferType)info.Type,
                (int)info.Width,
                (int)info.Height
            );

            // Copy data from native memory
            var data = new byte[dataLen];
            unsafe
            {
                var ptr = (byte*)info.DataPtr;
                for (int i = 0; i < dataLen; i++)
                {
                    data[i] = ptr[i];
                }
            }

            // Dispose the FFI handle
            using var handle = FfiHandle.FromId(ownedInfo.Handle.Id);

            return new VideoFrame(
                (int)info.Width,
                (int)info.Height,
                (VideoBufferType)info.Type,
                data
            );
        }

        /// <summary>
        /// Creates a proto info object for FFI communication.
        /// </summary>
        internal VideoBufferInfo ToProtoInfo()
        {
            unsafe
            {
                fixed (byte* ptr = _data)
                {
                    var info = new VideoBufferInfo
                    {
                        Width = (uint)_width,
                        Height = (uint)_height,
                        Type = (LiveKit.Proto.VideoBufferType)_type,
                        DataPtr = (ulong)ptr,
                        Stride = (uint)GetStride(_type, _width),
                    };

                    // Add component infos
                    var components = GetPlaneInfos((ulong)ptr, _type, _width, _height);
                    foreach (var component in components)
                    {
                        info.Components.Add(component);
                    }

                    return info;
                }
            }
        }

        /// <summary>Gets the width of the video frame in pixels.</summary>
        public int Width => _width;

        /// <summary>Gets the height of the video frame in pixels.</summary>
        public int Height => _height;

        /// <summary>Gets the format type of the video frame.</summary>
        public VideoBufferType Type => _type;

        /// <summary>Gets the raw pixel data.</summary>
        public ReadOnlySpan<byte> Data => _data;

        /// <summary>Gets the raw pixel data as a mutable span.</summary>
        public Span<byte> DataMutable => _data;

        /// <summary>Gets the raw pixel data as a byte array.</summary>
        public byte[] DataBytes => _data;

        /// <summary>Gets the user data associated with the frame.</summary>
        public Dictionary<string, object> Userdata => _userdata;

        /// <summary>
        /// Gets a specific plane from the video frame data.
        /// </summary>
        /// <param name="planeIndex">The index of the plane (0-based).</param>
        /// <returns>The plane data, or null if the index is out of bounds.</returns>
        public byte[]? GetPlane(int planeIndex)
        {
            var planeInfos = GetPlaneInfosLocal(_type, _width, _height);
            if (planeIndex >= planeInfos.Count)
                return null;

            var planeInfo = planeInfos[planeIndex];
            var planeData = new byte[planeInfo.Size];
            Array.Copy(_data, planeInfo.Offset, planeData, 0, planeInfo.Size);
            return planeData;
        }

        /// <summary>
        /// Converts the video frame to a different format type.
        /// </summary>
        /// <param name="targetType">The target format type.</param>
        /// <param name="flipY">If true, flip the frame vertically.</param>
        /// <returns>A new VideoFrame in the target format.</returns>
        public VideoFrame Convert(VideoBufferType targetType, bool flipY = false)
        {
            var request = new FfiRequest
            {
                VideoConvert = new VideoConvertRequest
                {
                    FlipY = flipY,
                    DstType = (LiveKit.Proto.VideoBufferType)targetType,
                    Buffer = ToProtoInfo(),
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (!string.IsNullOrEmpty(response.VideoConvert?.Error))
            {
                throw new InvalidOperationException(
                    $"Video conversion failed: {response.VideoConvert.Error}"
                );
            }

            return FromOwnedInfo(response.VideoConvert!.Buffer);
        }

        /// <summary>
        /// Creates a copy of this video frame.
        /// </summary>
        public VideoFrame Clone()
        {
            var dataCopy = new byte[_data.Length];
            Array.Copy(_data, dataCopy, _data.Length);
            return new VideoFrame(
                _width,
                _height,
                _type,
                dataCopy,
                new Dictionary<string, object>(_userdata)
            );
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"VideoFrame(width={_width}, height={_height}, type={_type})";
        }

        #region Static Helpers

        /// <summary>
        /// Gets the buffer size in bytes for a video buffer type.
        /// </summary>
        public static int GetBufferSize(Proto.VideoBufferType type, int width, int height)
        {
            switch (type)
            {
                case Proto.VideoBufferType.Argb:
                case Proto.VideoBufferType.Abgr:
                case Proto.VideoBufferType.Rgba:
                case Proto.VideoBufferType.Bgra:
                    return width * height * 4;

                case Proto.VideoBufferType.Rgb24:
                    return width * height * 3;

                case Proto.VideoBufferType.I420:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;
                    return width * height + chromaWidth * chromaHeight * 2;
                }

                case VideoBufferType.I420A:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;
                    return width * height * 2 + chromaWidth * chromaHeight * 2;
                }

                case VideoBufferType.I422:
                {
                    int chromaWidth = (width + 1) / 2;
                    return width * height + chromaWidth * height * 2;
                }

                case VideoBufferType.I444:
                    return width * height * 3;

                case VideoBufferType.I010:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;
                    return width * height * 2 + chromaWidth * chromaHeight * 4;
                }

                case VideoBufferType.Nv12:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;
                    return width * height + chromaWidth * chromaHeight * 2;
                }

                default:
                    throw new ArgumentException($"Unsupported video buffer type: {type}");
            }
        }

        /// <summary>
        /// Gets the stride for a video buffer type.
        /// </summary>
        public static int GetStride(VideoBufferType type, int width)
        {
            switch (type)
            {
                case VideoBufferType.Argb:
                case VideoBufferType.Abgr:
                case VideoBufferType.Rgba:
                case VideoBufferType.Bgra:
                    return width * 4;

                case VideoBufferType.Rgb24:
                    return width * 3;

                default:
                    return 0; // Planar formats don't have a single stride
            }
        }

        private static List<VideoBufferInfo.Types.ComponentInfo> GetPlaneInfos(
            ulong addr,
            VideoBufferType type,
            int width,
            int height
        )
        {
            var result = new List<VideoBufferInfo.Types.ComponentInfo>();

            switch (type)
            {
                case VideoBufferType.I420:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;

                    ulong ySize = (ulong)(width * height);
                    ulong uvSize = (ulong)(chromaWidth * chromaHeight);

                    result.Add(
                        new VideoBufferInfo.Types.ComponentInfo
                        {
                            DataPtr = addr,
                            Stride = (uint)width,
                            Size = (uint)ySize,
                        }
                    );
                    result.Add(
                        new VideoBufferInfo.Types.ComponentInfo
                        {
                            DataPtr = addr + ySize,
                            Stride = (uint)chromaWidth,
                            Size = (uint)uvSize,
                        }
                    );
                    result.Add(
                        new VideoBufferInfo.Types.ComponentInfo
                        {
                            DataPtr = addr + ySize + uvSize,
                            Stride = (uint)chromaWidth,
                            Size = (uint)uvSize,
                        }
                    );
                    break;
                }

                case VideoBufferType.Nv12:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;

                    ulong ySize = (ulong)(width * height);
                    ulong uvSize = (ulong)(chromaWidth * chromaHeight * 2);

                    result.Add(
                        new VideoBufferInfo.Types.ComponentInfo
                        {
                            DataPtr = addr,
                            Stride = (uint)width,
                            Size = (uint)ySize,
                        }
                    );
                    result.Add(
                        new VideoBufferInfo.Types.ComponentInfo
                        {
                            DataPtr = addr + ySize,
                            Stride = (uint)(chromaWidth * 2),
                            Size = (uint)uvSize,
                        }
                    );
                    break;
                }
            }

            return result;
        }

        private struct PlaneInfo
        {
            public int Offset;
            public int Size;
            public int Stride;
        }

        private static List<PlaneInfo> GetPlaneInfosLocal(
            VideoBufferType type,
            int width,
            int height
        )
        {
            var result = new List<PlaneInfo>();

            switch (type)
            {
                case VideoBufferType.I420:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;

                    int ySize = width * height;
                    int uvSize = chromaWidth * chromaHeight;

                    result.Add(
                        new PlaneInfo
                        {
                            Offset = 0,
                            Size = ySize,
                            Stride = width,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize,
                            Size = uvSize,
                            Stride = chromaWidth,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize + uvSize,
                            Size = uvSize,
                            Stride = chromaWidth,
                        }
                    );
                    break;
                }

                case VideoBufferType.I420A:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;

                    int ySize = width * height;
                    int uvSize = chromaWidth * chromaHeight;

                    result.Add(
                        new PlaneInfo
                        {
                            Offset = 0,
                            Size = ySize,
                            Stride = width,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize,
                            Size = uvSize,
                            Stride = chromaWidth,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize + uvSize,
                            Size = uvSize,
                            Stride = chromaWidth,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize + uvSize * 2,
                            Size = ySize,
                            Stride = width,
                        }
                    );
                    break;
                }

                case VideoBufferType.I422:
                {
                    int chromaWidth = (width + 1) / 2;

                    int ySize = width * height;
                    int uvSize = chromaWidth * height;

                    result.Add(
                        new PlaneInfo
                        {
                            Offset = 0,
                            Size = ySize,
                            Stride = width,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize,
                            Size = uvSize,
                            Stride = chromaWidth,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize + uvSize,
                            Size = uvSize,
                            Stride = chromaWidth,
                        }
                    );
                    break;
                }

                case VideoBufferType.I444:
                {
                    int planeSize = width * height;
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = 0,
                            Size = planeSize,
                            Stride = width,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = planeSize,
                            Size = planeSize,
                            Stride = width,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = planeSize * 2,
                            Size = planeSize,
                            Stride = width,
                        }
                    );
                    break;
                }

                case VideoBufferType.Nv12:
                {
                    int chromaWidth = (width + 1) / 2;
                    int chromaHeight = (height + 1) / 2;

                    int ySize = width * height;
                    int uvSize = chromaWidth * chromaHeight * 2;

                    result.Add(
                        new PlaneInfo
                        {
                            Offset = 0,
                            Size = ySize,
                            Stride = width,
                        }
                    );
                    result.Add(
                        new PlaneInfo
                        {
                            Offset = ySize,
                            Size = uvSize,
                            Stride = chromaWidth * 2,
                        }
                    );
                    break;
                }
            }

            return result;
        }

        #endregion
    }
}
