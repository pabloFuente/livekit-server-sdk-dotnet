// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Represents a video frame received event.
    /// </summary>
    public readonly struct VideoFrameEvent
    {
        /// <summary>
        /// Gets the video frame.
        /// </summary>
        public VideoFrame Frame { get; }

        /// <summary>
        /// Gets the timestamp in microseconds.
        /// </summary>
        public long TimestampUs { get; }

        /// <summary>
        /// Gets the frame rotation.
        /// </summary>
        public Proto.VideoRotation Rotation { get; }

        /// <summary>
        /// Gets the metadata the publisher sent with the frame (user timestamp, frame ID, user data),
        /// or null when it sent none. The publisher sends it with
        /// <see cref="VideoSource.CaptureFrame(VideoFrame, Proto.FrameMetadata, long, Proto.VideoRotation)"/>
        /// on a track published with <see cref="TrackPublishOptions.FrameMetadataFeatures"/>.
        /// </summary>
        public Proto.FrameMetadata? Metadata { get; }

        /// <summary>
        /// Initializes a new VideoFrameEvent.
        /// </summary>
        /// <param name="frame">The video frame.</param>
        /// <param name="timestampUs">The timestamp in microseconds.</param>
        /// <param name="rotation">The frame rotation.</param>
        public VideoFrameEvent(VideoFrame frame, long timestampUs, Proto.VideoRotation rotation)
            : this(frame, timestampUs, rotation, null) { }

        /// <summary>
        /// Initializes a new VideoFrameEvent with frame metadata.
        /// </summary>
        /// <param name="frame">The video frame.</param>
        /// <param name="timestampUs">The timestamp in microseconds.</param>
        /// <param name="rotation">The frame rotation.</param>
        /// <param name="metadata">The metadata sent with the frame, or null.</param>
        public VideoFrameEvent(
            VideoFrame frame,
            long timestampUs,
            Proto.VideoRotation rotation,
            Proto.FrameMetadata? metadata
        )
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            TimestampUs = timestampUs;
            Rotation = rotation;
            Metadata = metadata;
        }
    }

    /// <summary>
    /// A video stream for receiving video frames from a remote track or participant.
    /// Implements IAsyncEnumerable for modern async iteration patterns.
    /// </summary>
    public class VideoStream : IAsyncDisposable, IDisposable, IAsyncEnumerable<VideoFrameEvent>
    {
        private readonly FfiHandle _handle;
        private readonly Track? _track;
        private readonly VideoBufferType? _format;
        private readonly Channel<VideoFrameEvent> _channel;
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        /// <summary>
        /// Initializes a new VideoStream from a track.
        /// </summary>
        /// <param name="track">The video track to stream from.</param>
        /// <param name="format">Optional video buffer format to convert to.</param>
        /// <param name="capacity">The capacity of the internal channel (0 for unbounded).</param>
        public VideoStream(Track track, VideoBufferType? format = null, int capacity = 0)
        {
            _track = track ?? throw new ArgumentNullException(nameof(track));
            _format = format;

            _channel =
                capacity > 0
                    ? Channel.CreateBounded<VideoFrameEvent>(
                        new BoundedChannelOptions(capacity)
                        {
                            FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest frames under backpressure
                            SingleReader = false,
                            SingleWriter = true,
                        }
                    )
                    : Channel.CreateUnbounded<VideoFrameEvent>(
                        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
                    );

            _cts = new CancellationTokenSource();

            var stream = CreateOwnedStream();
            _handle = FfiHandle.FromId(stream.Handle.Id);

            FfiClient.Instance.AddStreamHandler(_handle.HandleId, OnFfiEvent);
        }

        /// <summary>
        /// Used by the SDK to wrap a native video stream it already created.
        /// </summary>
        /// <param name="handle">Native handle of the stream.</param>
        /// <param name="format">Pixel format the frames are converted to, or null to keep the source format.</param>
        /// <param name="capacity">Capacity of the internal frame channel; 0 for unbounded, otherwise the oldest frame is dropped when it is full.</param>
        protected VideoStream(FfiHandle handle, VideoBufferType? format, int capacity)
        {
            _handle = handle;
            _track = null;
            _format = format;

            _channel =
                capacity > 0
                    ? Channel.CreateBounded<VideoFrameEvent>(
                        new BoundedChannelOptions(capacity)
                        {
                            FullMode = BoundedChannelFullMode.DropOldest,
                            SingleReader = false,
                            SingleWriter = true,
                        }
                    )
                    : Channel.CreateUnbounded<VideoFrameEvent>(
                        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
                    );

            _cts = new CancellationTokenSource();

            FfiClient.Instance.AddStreamHandler(_handle.HandleId, OnFfiEvent);
        }

        /// <summary>
        /// Creates a VideoStream from a participant's track source.
        /// </summary>
        /// <param name="participant">The participant.</param>
        /// <param name="trackSource">The track source type.</param>
        /// <param name="format">Optional video buffer format.</param>
        /// <param name="capacity">The capacity of the internal channel.</param>
        /// <returns>A new VideoStream instance.</returns>
        public static VideoStream FromParticipant(
            Participant participant,
            Proto.TrackSource trackSource,
            VideoBufferType? format = null,
            int capacity = 0
        )
        {
            _ = participant ?? throw new ArgumentNullException(nameof(participant));

            var handle = CreateStreamFromParticipant(participant, trackSource, format);
            return new VideoStreamFromHandle(handle, format, capacity);
        }

        /// <summary>
        /// Creates a VideoStream from an existing track.
        /// </summary>
        /// <param name="track">The track.</param>
        /// <param name="format">Optional video buffer format.</param>
        /// <param name="capacity">The capacity of the internal channel.</param>
        /// <returns>A new VideoStream instance.</returns>
        public static VideoStream FromTrack(
            Track track,
            VideoBufferType? format = null,
            int capacity = 0
        )
        {
            return new VideoStream(track, format, capacity);
        }

        /// <summary>
        /// Gets the internal handle.
        /// </summary>
        internal FfiHandle Handle => _handle;

        /// <summary>
        /// Gets the video format.
        /// </summary>
        public VideoBufferType? Format => _format;

        private OwnedVideoStream CreateOwnedStream()
        {
            if (_track == null)
                throw new InvalidOperationException("Track is required for creating video stream");

            var request = new FfiRequest
            {
                NewVideoStream = new NewVideoStreamRequest
                {
                    TrackHandle = _track.Handle.HandleId,
                    Type = VideoStreamType.VideoStreamNative,
                    NormalizeStride = true,
                },
            };

            if (_format.HasValue)
            {
                request.NewVideoStream.Format = (LiveKit.Proto.VideoBufferType)_format.Value;
            }

            var response = FfiClient.Instance.SendRequest(request);
            return response.NewVideoStream.Stream;
        }

        private static FfiHandle CreateStreamFromParticipant(
            Participant participant,
            Proto.TrackSource trackSource,
            VideoBufferType? format
        )
        {
            var request = new FfiRequest
            {
                VideoStreamFromParticipant = new VideoStreamFromParticipantRequest
                {
                    ParticipantHandle = participant.Handle.HandleId,
                    TrackSource = trackSource,
                    Type = VideoStreamType.VideoStreamNative,
                    NormalizeStride = true,
                },
            };

            if (format.HasValue)
            {
                request.VideoStreamFromParticipant.Format = (LiveKit.Proto.VideoBufferType)
                    format.Value;
            }

            var response = FfiClient.Instance.SendRequest(request);
            return FfiHandle.FromId(response.VideoStreamFromParticipant.Stream.Handle.Id);
        }

        private void OnFfiEvent(FfiEvent e)
        {
            if (e.MessageCase != FfiEvent.MessageOneofCase.VideoStreamEvent)
                return;

            var videoEvent = e.VideoStreamEvent;
            if (videoEvent.StreamHandle != _handle.HandleId)
                return;

            if (videoEvent.MessageCase == VideoStreamEvent.MessageOneofCase.FrameReceived)
            {
                var ownedBuffer = videoEvent.FrameReceived.Buffer;
                if (_disposed)
                {
                    // The native task can still emit a few frames after the stream was closed:
                    // release their buffers instead of leaking them
                    FfiHandle.FromId(ownedBuffer.Handle.Id).Dispose();
                    return;
                }

                var frame = VideoFrame.FromOwnedInfo(ownedBuffer);
                var received = videoEvent.FrameReceived;
                _channel.Writer.TryWrite(
                    new VideoFrameEvent(
                        frame,
                        received.TimestampUs,
                        received.Rotation,
                        received.Metadata
                    )
                );
            }
            else if (videoEvent.MessageCase == VideoStreamEvent.MessageOneofCase.Eos)
            {
                _channel.Writer.TryComplete();
                // Nothing more will come for this handle
                FfiClient.Instance.RemoveStreamHandler(_handle.HandleId);
            }
        }

        // Keep receiving (and releasing) the frames the native side may still emit after the
        // stream is closed, until its end-of-stream event; stop listening after a grace period in
        // case it never comes.
        private void StopListeningAfterEos()
        {
            _ = Task.Delay(TimeSpan.FromSeconds(5))
                .ContinueWith(
                    _ => FfiClient.Instance.RemoveStreamHandler(_handle.HandleId),
                    TaskScheduler.Default
                );
        }

        /// <summary>
        /// Reads the next video frame event from the stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The next video frame event, or null if the stream has ended.</returns>
        public async ValueTask<VideoFrameEvent?> ReadAsync(
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _cts.Token
                );
                if (await _channel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    if (_channel.Reader.TryRead(out var frame))
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
        /// Tries to read the next video frame event without blocking.
        /// </summary>
        /// <param name="frameEvent">The frame event if available.</param>
        /// <returns>True if a frame was available.</returns>
        public bool TryRead(out VideoFrameEvent frameEvent)
        {
            return _channel.Reader.TryRead(out frameEvent);
        }

        /// <summary>
        /// Returns an async enumerator for iterating over video frames.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async enumerator of video frame events.</returns>
        public async IAsyncEnumerator<VideoFrameEvent> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cts.Token
            );

            await foreach (
                var frame in _channel.Reader.ReadAllAsync(linkedCts.Token).ConfigureAwait(false)
            )
            {
                yield return frame;
            }
        }

        /// <summary>
        /// Stops the stream and releases the native stream. Pending reads return null and later reads throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cts.Cancel();
            StopListeningAfterEos();
            _channel.Writer.TryComplete();
            _handle.Dispose();
            _cts.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Stops the stream and releases the native stream. Pending reads return null and later reads throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        /// <returns>A task that completes once the stream is released.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cts.Cancel();
            StopListeningAfterEos();
            _channel.Writer.TryComplete();

            await Task.Yield();

            _handle.Dispose();
            _cts.Dispose();

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Video stream created from a handle.
    /// </summary>
    internal class VideoStreamFromHandle : VideoStream
    {
        internal VideoStreamFromHandle(FfiHandle handle, VideoBufferType? format, int capacity)
            : base(handle, format, capacity) { }
    }
}
