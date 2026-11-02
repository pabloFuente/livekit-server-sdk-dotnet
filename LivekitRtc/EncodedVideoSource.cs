// author: https://github.com/pabloFuente

using System;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// One complete encoded video access unit, without container or RTP headers.
    /// </summary>
    public class EncodedVideoFrame
    {
        /// <summary>
        /// A single frame encoded with <see cref="Codec"/> (Annex B for H.264/H.265).
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// Frame width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Frame height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// The codec of <see cref="Data"/>. It must match the publication and stay fixed for the
        /// lifetime of the track.
        /// </summary>
        public Proto.VideoCodec Codec { get; }

        /// <summary>
        /// Whether this is a key frame or a delta frame.
        /// </summary>
        public Proto.EncodedFrameType FrameType { get; }

        /// <summary>
        /// Capture time in microseconds, increasing along the stream.
        /// </summary>
        public long TimestampUs { get; }

        /// <summary>
        /// Optional metadata delivered with the frame.
        /// </summary>
        public Proto.FrameMetadata? Metadata { get; }

        /// <summary>
        /// Creates an encoded frame to capture with <see cref="EncodedVideoSource.CaptureFrame"/>.
        /// </summary>
        /// <param name="data">Encoded payload of the frame.</param>
        /// <param name="width">Frame width in pixels.</param>
        /// <param name="height">Frame height in pixels.</param>
        /// <param name="codec">Codec the payload is encoded with.</param>
        /// <param name="frameType">Key frame or delta frame.</param>
        /// <param name="timestampUs">Capture time in microseconds, increasing along the stream.</param>
        /// <param name="metadata">Optional metadata delivered with the frame.</param>
        public EncodedVideoFrame(
            byte[] data,
            int width,
            int height,
            Proto.VideoCodec codec,
            Proto.EncodedFrameType frameType,
            long timestampUs,
            Proto.FrameMetadata? metadata = null
        )
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length == 0)
                throw new ArgumentException("Encoded frame data must not be empty", nameof(data));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(
                    width <= 0 ? nameof(width) : nameof(height),
                    "Encoded frame dimensions must be positive"
                );

            Data = data;
            Width = width;
            Height = height;
            Codec = codec;
            FrameType = frameType;
            TimestampUs = timestampUs;
            Metadata = metadata;
        }
    }

    /// <summary>
    /// The latest bitrate and frame-rate targets requested by WebRTC for a pre-encoded track.
    /// </summary>
    public class EncodedRateControl
    {
        /// <summary>
        /// Target bitrate in bits per second.
        /// </summary>
        public ulong TargetBitrateBps { get; }

        /// <summary>
        /// Target frame rate in frames per second.
        /// </summary>
        public double FramerateFps { get; }

        internal EncodedRateControl(ulong targetBitrateBps, double framerateFps)
        {
            TargetBitrateBps = targetBitrateBps;
            FramerateFps = framerateFps;
        }
    }

    /// <summary>
    /// Encoder feedback accumulated since the previous call to
    /// <see cref="EncodedVideoSource.TakeFeedback"/>.
    /// </summary>
    public class EncodedVideoSourceFeedback
    {
        /// <summary>
        /// Whether a key frame was requested (for example by a late subscriber). Forward it to
        /// the upstream encoder.
        /// </summary>
        public bool KeyframeRequested { get; }

        /// <summary>
        /// The latest rate-control targets, or null if none were received.
        /// </summary>
        public EncodedRateControl? RateControl { get; }

        internal EncodedVideoSourceFeedback(bool keyframeRequested, EncodedRateControl? rateControl)
        {
            KeyframeRequested = keyframeRequested;
            RateControl = rateControl;
        }
    }

    /// <summary>
    /// Publishes pre-encoded video without decoding or re-encoding it.
    /// </summary>
    /// <remarks>
    /// Create a <see cref="LocalVideoTrack"/> from this source and publish it with a
    /// <see cref="TrackPublishOptions"/> whose <c>VideoCodec</c> matches the frames,
    /// <c>VideoEncoder</c> is <c>Proto.VideoEncoderBackend.EncoderBackendPreEncoded</c> and
    /// <c>Simulcast</c> is false. Feed complete access units at the intended playback rate from a
    /// single producer; the source does not pace or buffer. Poll <see cref="TakeFeedback"/> and
    /// forward key-frame and rate-control requests to the upstream encoder, since a demuxer alone
    /// cannot produce a new key frame or adapt the bitrate.
    /// </remarks>
    public class EncodedVideoSource : IDisposable
    {
        private readonly FfiHandle _handle;
        private bool _disposed;

        /// <summary>
        /// Initial frame width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Initial frame height in pixels.
        /// </summary>
        public int Height { get; }

        internal FfiHandle Handle => _handle;

        /// <summary>
        /// Creates a source with the initial encoded frame dimensions.
        /// </summary>
        /// <param name="width">Initial frame width in pixels.</param>
        /// <param name="height">Initial frame height in pixels.</param>
        public EncodedVideoSource(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(
                    width <= 0 ? nameof(width) : nameof(height),
                    "Encoded source dimensions must be positive"
                );

            Width = width;
            Height = height;

            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    NewVideoSource = new NewVideoSourceRequest
                    {
                        Type = VideoSourceType.VideoSourceEncoded,
                        Resolution = new VideoSourceResolution
                        {
                            Width = (uint)width,
                            Height = (uint)height,
                        },
                    },
                }
            );

            _handle = FfiHandle.FromId(response.NewVideoSource.Source.Handle.Id);
        }

        /// <summary>
        /// Submits a frame and returns whether the native source accepted it. The payload is copied
        /// before this method returns; acceptance does not guarantee delivery to subscribers.
        /// </summary>
        /// <param name="frame">The encoded frame to submit.</param>
        /// <exception cref="InvalidOperationException">The FFI rejected the frame.</exception>
        /// <returns>True when the native source accepted the frame, false when it was dropped.</returns>
        public bool CaptureFrame(EncodedVideoFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            ThrowIfDisposed();

            FfiResponse response;
            unsafe
            {
                // The FFI reads the payload through the pointer while the request is served: keep
                // it pinned through request serialization and the native call.
                fixed (byte* dataPtr = frame.Data)
                {
                    var request = new CaptureEncodedVideoFrameRequest
                    {
                        SourceHandle = _handle.HandleId,
                        Buffer = new EncodedVideoBufferInfo
                        {
                            DataPtr = (ulong)dataPtr,
                            DataLen = (ulong)frame.Data.Length,
                        },
                        Codec = frame.Codec,
                        FrameType = frame.FrameType,
                        Width = (uint)frame.Width,
                        Height = (uint)frame.Height,
                        TimestampUs = frame.TimestampUs,
                    };
                    if (frame.Metadata != null)
                    {
                        request.Metadata = frame.Metadata;
                    }

                    response = FfiClient.Instance.SendRequest(
                        new FfiRequest { CaptureEncodedVideoFrame = request }
                    );
                }
            }

            return response.CaptureEncodedVideoFrame?.Accepted == true;
        }

        /// <summary>
        /// Consumes the feedback accumulated since the previous call, including key-frame requests
        /// from late subscribers.
        /// </summary>
        /// <returns>The feedback collected since the previous call: key-frame request and rate control.</returns>
        public EncodedVideoSourceFeedback TakeFeedback()
        {
            ThrowIfDisposed();

            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    TakeEncodedVideoSourceFeedback = new TakeEncodedVideoSourceFeedbackRequest
                    {
                        SourceHandle = _handle.HandleId,
                    },
                }
            );

            var feedback = response.TakeEncodedVideoSourceFeedback;
            EncodedRateControl? rateControl = null;
            if (feedback?.RateControl != null)
            {
                rateControl = new EncodedRateControl(
                    feedback.RateControl.TargetBitrateBps,
                    feedback.RateControl.FramerateFps
                );
            }

            return new EncodedVideoSourceFeedback(feedback?.KeyframeRequested == true, rateControl);
        }

        /// <summary>
        /// Creates a local video track from this source.
        /// </summary>
        /// <param name="name">The name of the track.</param>
        /// <returns>A local video track fed by this source.</returns>
        public LocalVideoTrack CreateTrack(string name = "video")
        {
            ThrowIfDisposed();
            return LocalVideoTrack.Create(name, this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EncodedVideoSource));
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
