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
            : this(width, height, false) { }

        /// <summary>
        /// Initializes a new instance of the video source, for camera-like or screen content.
        /// </summary>
        /// <param name="width">The width of the video source in pixels.</param>
        /// <param name="height">The height of the video source in pixels.</param>
        /// <param name="isScreencast">
        /// Whether the frames are screen content, for which WebRTC optimizes its pipeline (sharpness
        /// over frame rate).
        /// </param>
        public VideoSource(int width, int height, bool isScreencast)
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
                    IsScreencast = isScreencast,
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
        ) => CaptureFrame(frame, null, timestampUs, rotation);

        /// <summary>
        /// Captures a video frame with metadata, which subscribers read from
        /// <see cref="VideoFrameEvent.Metadata"/>.
        /// </summary>
        /// <param name="frame">The video frame to capture.</param>
        /// <param name="metadata">
        /// Metadata delivered with the frame (user timestamp, frame ID, user data), or null for none.
        /// Only the features enabled in <see cref="TrackPublishOptions.FrameMetadataFeatures"/> when
        /// the track was published reach the subscribers.
        /// </param>
        /// <param name="timestampUs">Optional timestamp in microseconds. If 0, current time is used.</param>
        /// <param name="rotation">Optional rotation to apply to the frame.</param>
        public void CaptureFrame(
            VideoFrame frame,
            Proto.FrameMetadata? metadata,
            long timestampUs = 0,
            Proto.VideoRotation rotation = Proto.VideoRotation._0
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VideoSource));

            // The FFI copies the pixels into its own buffer before SendRequest returns. Keep them
            // pinned through request serialization and the native call.
            frame.WithPinnedBuffer(buffer =>
            {
                var request = new CaptureVideoFrameRequest
                {
                    SourceHandle = _handle.HandleId,
                    Buffer = buffer,
                    Rotation = rotation,
                    TimestampUs = timestampUs,
                };
                if (metadata != null)
                    request.Metadata = metadata;
                return FfiClient.Instance.SendRequest(
                    new FfiRequest { CaptureVideoFrame = request }
                );
            });
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

        /// <summary>
        /// Releases the native video source.
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
