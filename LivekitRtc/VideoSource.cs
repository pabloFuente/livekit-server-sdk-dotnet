// author: https://github.com/pabloFuente

using System;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents a video source for publishing video frames.
    /// </summary>
    public class VideoSource : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly FfiHandle _handle;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the video source.
        /// </summary>
        /// <param name="width">The width of the video source in pixels.</param>
        /// <param name="height">The height of the video source in pixels.</param>
        public VideoSource(int width, int height)
        {
            _width = width;
            _height = height;

            var request = new FfiRequest
            {
                NewVideoSource = new NewVideoSourceRequest
                {
                    Type = VideoSourceType.VideoSourceNative,
                    Resolution = new VideoSourceResolution
                    {
                        Width = (uint)width,
                        Height = (uint)height,
                    },
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var sourceInfo = response.NewVideoSource.Source;
            _handle = FfiHandle.FromId(sourceInfo.Handle.Id);
        }

        /// <summary>
        /// Gets the width of the video source in pixels.
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// Gets the height of the video source in pixels.
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// Gets the internal FFI handle.
        /// </summary>
        internal FfiHandle Handle => _handle;

        /// <summary>
        /// Captures a video frame and sends it to the video source.
        /// </summary>
        /// <param name="frame">The video frame to capture.</param>
        /// <param name="timestampUs">Optional timestamp in microseconds. If 0, current time is used.</param>
        /// <param name="rotation">Optional rotation to apply to the frame.</param>
        public void CaptureFrame(
            VideoFrame frame,
            long timestampUs = 0,
            Proto.VideoRotation rotation = Proto.VideoRotation._0
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VideoSource));

            var request = new FfiRequest
            {
                CaptureVideoFrame = new CaptureVideoFrameRequest
                {
                    SourceHandle = _handle.HandleId,
                    Buffer = frame.ToProtoInfo(),
                    Rotation = rotation,
                    TimestampUs = timestampUs,
                },
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Creates a local video track from this video source.
        /// </summary>
        /// <param name="name">The name of the track.</param>
        /// <returns>A new LocalVideoTrack.</returns>
        public LocalVideoTrack CreateTrack(string name = "video")
        {
            var request = new FfiRequest
            {
                CreateVideoTrack = new CreateVideoTrackRequest
                {
                    Name = name,
                    SourceHandle = _handle.HandleId,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var trackInfo = response.CreateVideoTrack.Track;

            return new LocalVideoTrack(FfiHandle.FromId(trackInfo.Handle.Id), trackInfo);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _handle.Dispose();
        }
    }
}
