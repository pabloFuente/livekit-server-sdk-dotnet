// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Information about a published data track.
    /// </summary>
    public class DataTrackInfo
    {
        /// <summary>
        /// Unique track identifier assigned by the SFU. It may change after a reconnect; use
        /// <see cref="Name"/> when a stable identifier is needed.
        /// </summary>
        public string Sid { get; }

        /// <summary>
        /// Name of the track, assigned by the publisher.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Whether frames sent on the track use end-to-end encryption.
        /// </summary>
        public bool UsesE2EE { get; }

        internal DataTrackInfo(Proto.DataTrackInfo info)
        {
            Sid = info.Sid ?? string.Empty;
            Name = info.Name ?? string.Empty;
            UsesE2EE = info.UsesE2Ee;
        }
    }

    /// <summary>
    /// A frame published on a data track: a payload and an optional user timestamp.
    /// </summary>
    public class DataTrackFrame
    {
        /// <summary>
        /// The frame payload.
        /// </summary>
        public byte[] Payload { get; }

        /// <summary>
        /// The user timestamp attached to the frame, if any.
        /// </summary>
        public ulong? UserTimestamp { get; }

        /// <summary>
        /// Creates a frame to push on a <see cref="LocalDataTrack"/>.
        /// </summary>
        /// <param name="payload">The frame payload.</param>
        /// <param name="userTimestamp">Optional user timestamp delivered with the frame.</param>
        public DataTrackFrame(byte[] payload, ulong? userTimestamp = null)
        {
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            UserTimestamp = userTimestamp;
        }

        internal static DataTrackFrame FromProto(Proto.DataTrackFrame frame)
        {
            return new DataTrackFrame(
                frame.Payload.ToByteArray(),
                frame.HasUserTimestamp ? frame.UserTimestamp : (ulong?)null
            );
        }

        internal Proto.DataTrackFrame ToProto()
        {
            var proto = new Proto.DataTrackFrame { Payload = ByteString.CopyFrom(Payload) };
            if (UserTimestamp.HasValue)
            {
                proto.UserTimestamp = UserTimestamp.Value;
            }
            return proto;
        }
    }

    /// <summary>
    /// Thrown when a data track cannot be published.
    /// </summary>
    public class PublishDataTrackException : Exception
    {
        /// <summary>
        /// The error code reported by the SDK.
        /// </summary>
        public Proto.PublishDataTrackErrorCode Code { get; }

        internal PublishDataTrackException(Proto.PublishDataTrackErrorCode code, string message)
            : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// Thrown when a frame cannot be pushed on a <see cref="LocalDataTrack"/>: the track was
    /// unpublished (by the local participant or the SFU), the room is no longer connected, or
    /// frames are being pushed faster than they can be sent.
    /// </summary>
    public class PushFrameException : Exception
    {
        /// <summary>
        /// The error code reported by the SDK.
        /// </summary>
        public Proto.LocalDataTrackTryPushErrorCode Code { get; }

        internal PushFrameException(Proto.LocalDataTrackTryPushErrorCode code, string message)
            : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// Thrown by <see cref="DataTrackStream.ReadAsync"/> when the subscription to a remote data
    /// track could not be established.
    /// </summary>
    public class SubscribeDataTrackException : Exception
    {
        /// <summary>
        /// The error code reported by the SDK.
        /// </summary>
        public Proto.SubscribeDataTrackErrorCode Code { get; }

        internal SubscribeDataTrackException(Proto.SubscribeDataTrackErrorCode code, string message)
            : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// A data track published by the local participant. Obtained from
    /// <see cref="LocalParticipant.PublishDataTrackAsync"/>.
    /// </summary>
    public class LocalDataTrack : IDisposable
    {
        private readonly FfiHandle _handle;
        private bool _disposed;

        /// <summary>
        /// Information about the data track.
        /// </summary>
        public DataTrackInfo Info { get; }

        internal LocalDataTrack(OwnedLocalDataTrack owned)
        {
            _handle = FfiHandle.FromId(owned.Handle.Id);
            Info = new DataTrackInfo(owned.Info);
        }

        /// <summary>
        /// Tries to push a frame to the subscribers of the track.
        /// </summary>
        /// <param name="frame">The frame to send.</param>
        /// <exception cref="PushFrameException">The frame could not be pushed.</exception>
        public void TryPush(DataTrackFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            ThrowIfDisposed();

            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    LocalDataTrackTryPush = new LocalDataTrackTryPushRequest
                    {
                        TrackHandle = _handle.HandleId,
                        Frame = frame.ToProto(),
                    },
                }
            );

            var error = response.LocalDataTrackTryPush?.Error;
            if (error != null)
            {
                throw new PushFrameException(error.Code, error.Message);
            }
        }

        /// <summary>
        /// Whether the track is still published.
        /// </summary>
        /// <returns>True while the track is published.</returns>
        public bool IsPublished()
        {
            ThrowIfDisposed();
            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    LocalDataTrackIsPublished = new LocalDataTrackIsPublishedRequest
                    {
                        TrackHandle = _handle.HandleId,
                    },
                }
            );
            return response.LocalDataTrackIsPublished?.IsPublished == true;
        }

        /// <summary>
        /// Unpublishes the track.
        /// </summary>
        public void Unpublish()
        {
            ThrowIfDisposed();
            FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    LocalDataTrackUnpublish = new LocalDataTrackUnpublishRequest
                    {
                        TrackHandle = _handle.HandleId,
                    },
                }
            );
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LocalDataTrack));
        }

        /// <summary>
        /// Releases the native handle of the track, which also unpublishes it if it is still published.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _handle.Dispose();
        }
    }

    /// <summary>
    /// A data track published by a remote participant. Delivered by
    /// <see cref="Room.DataTrackPublished"/>; call <see cref="Subscribe"/> to receive its frames.
    /// </summary>
    public class RemoteDataTrack : IDisposable
    {
        private readonly FfiHandle _handle;
        private bool _disposed;

        /// <summary>
        /// Information about the data track.
        /// </summary>
        public DataTrackInfo Info { get; }

        /// <summary>
        /// Identity of the participant who published the track.
        /// </summary>
        public string PublisherIdentity { get; }

        internal RemoteDataTrack(OwnedRemoteDataTrack owned)
        {
            _handle = FfiHandle.FromId(owned.Handle.Id);
            Info = new DataTrackInfo(owned.Info);
            PublisherIdentity = owned.PublisherIdentity ?? string.Empty;
        }

        /// <summary>
        /// Subscribes to the track. The returned stream yields the frames as they arrive; dispose
        /// it to unsubscribe.
        /// </summary>
        /// <param name="bufferSize">
        /// Maximum number of received frames buffered internally. Null keeps the SDK default. Zero is
        /// not valid and is clamped to one.
        /// </param>
        /// <returns>The stream of frames; dispose it to unsubscribe.</returns>
        public DataTrackStream Subscribe(uint? bufferSize = null)
        {
            ThrowIfDisposed();

            var options = new DataTrackSubscribeOptions();
            if (bufferSize.HasValue)
            {
                options.BufferSize = bufferSize.Value;
            }

            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    SubscribeDataTrack = new SubscribeDataTrackRequest
                    {
                        TrackHandle = _handle.HandleId,
                        Options = options,
                    },
                }
            );

            return new DataTrackStream(response.SubscribeDataTrack.Stream);
        }

        /// <summary>
        /// Whether the track is still published.
        /// </summary>
        /// <returns>True while the publisher keeps the track published.</returns>
        public bool IsPublished()
        {
            ThrowIfDisposed();
            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    RemoteDataTrackIsPublished = new RemoteDataTrackIsPublishedRequest
                    {
                        TrackHandle = _handle.HandleId,
                    },
                }
            );
            return response.RemoteDataTrackIsPublished?.IsPublished == true;
        }

        /// <summary>
        /// Configures how incoming packets are reassembled into frames for this track. The options
        /// apply to all current and future subscriptions and take effect with the next packet.
        /// </summary>
        /// <param name="maxPartialFrames">
        /// Maximum number of partial frames tracked concurrently (default 1). Higher values tolerate
        /// more reordering for high-frequency senders at the cost of buffering. Zero is clamped to
        /// one; null keeps the current value.
        /// </param>
        public void SetPipelineOptions(uint? maxPartialFrames = null)
        {
            ThrowIfDisposed();

            var options = new RemoteDataTrackPipelineOptions();
            if (maxPartialFrames.HasValue)
            {
                options.MaxPartialFrames = maxPartialFrames.Value;
            }

            FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    RemoteDataTrackSetPipelineOptions = new RemoteDataTrackSetPipelineOptionsRequest
                    {
                        TrackHandle = _handle.HandleId,
                        Options = options,
                    },
                }
            );
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RemoteDataTrack));
        }

        /// <summary>
        /// Releases the native handle of the track. The streams returned by <see cref="Subscribe"/> are
        /// disposed separately.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _handle.Dispose();
        }
    }

    /// <summary>
    /// An active subscription to a <see cref="RemoteDataTrack"/>. Read frames with
    /// <see cref="ReadAsync"/> or <c>await foreach</c>; the stream ends when the track is
    /// unpublished. Disposing the stream unsubscribes from the track.
    /// </summary>
    public class DataTrackStream : IAsyncEnumerable<DataTrackFrame>, IAsyncDisposable, IDisposable
    {
        private readonly FfiHandle _handle;
        private readonly ulong _handleId;
        private readonly Channel<DataTrackStreamEvent> _events;
        private readonly SemaphoreSlim _readLock = new SemaphoreSlim(1, 1);
        private SubscribeDataTrackException? _pendingError;
        private bool _ended;
        private bool _disposed;

        internal DataTrackStream(OwnedDataTrackStream owned)
        {
            _handleId = owned.Handle.Id;
            _handle = FfiHandle.FromId(_handleId);
            _events = Channel.CreateUnbounded<DataTrackStreamEvent>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
            );

            // Subscribe before the first read request so no event can be missed
            FfiClient.Instance.AddStreamHandler(_handleId, OnFfiEvent);
        }

        /// <summary>
        /// Whether the stream has ended (track unpublished, subscription failed, or disposed).
        /// </summary>
        public bool IsEnded => _ended || _disposed;

        private void OnFfiEvent(FfiEvent e)
        {
            if (e.MessageCase != FfiEvent.MessageOneofCase.DataTrackStreamEvent)
                return;

            var evt = e.DataTrackStreamEvent;
            if (evt == null || evt.StreamHandle != _handleId)
                return;

            _events.Writer.TryWrite(evt);
        }

        /// <summary>
        /// Reads the next frame, or returns null once the stream has ended.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait for the next frame.</param>
        /// <exception cref="SubscribeDataTrackException">
        /// The subscription could not be established. Thrown once, when the stream ends.
        /// </exception>
        /// <returns>The next frame, or null once the stream has ended.</returns>
        public async Task<DataTrackFrame?> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DataTrackStream));

            await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_ended)
                {
                    ThrowPendingError();
                    return null;
                }

                // The FFI delivers exactly one stream event per read request (backpressure). If the
                // stream already ended, the response says so instead.
                var response = FfiClient.Instance.SendRequest(
                    new FfiRequest
                    {
                        DataTrackStreamRead = new DataTrackStreamReadRequest
                        {
                            StreamHandle = _handleId,
                        },
                    }
                );

                var eos = response.DataTrackStreamRead?.EosEvent;
                if (eos != null)
                {
                    End(eos);
                    ThrowPendingError();
                    return null;
                }

                while (true)
                {
                    DataTrackStreamEvent evt;
                    try
                    {
                        evt = await _events
                            .Reader.ReadAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (ChannelClosedException)
                    {
                        // Disposed from another thread while waiting: the stream is over
                        _ended = true;
                        return null;
                    }

                    switch (evt.DetailCase)
                    {
                        case DataTrackStreamEvent.DetailOneofCase.FrameReceived:
                            return DataTrackFrame.FromProto(evt.FrameReceived.Frame);

                        case DataTrackStreamEvent.DetailOneofCase.Eos:
                            End(evt.Eos);
                            ThrowPendingError();
                            return null;
                    }
                }
            }
            finally
            {
                _readLock.Release();
            }
        }

        /// <inheritdoc />
        /// <returns>The frames, until the stream ends.</returns>
        public async IAsyncEnumerator<DataTrackFrame> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            while (true)
            {
                var frame = await ReadAsync(cancellationToken).ConfigureAwait(false);
                if (frame == null)
                    yield break;
                yield return frame;
            }
        }

        private void End(DataTrackStreamEOS eos)
        {
            _ended = true;
            if (eos.Error != null)
            {
                _pendingError = new SubscribeDataTrackException(eos.Error.Code, eos.Error.Message);
            }
            FfiClient.Instance.RemoveStreamHandler(_handleId);
            _events.Writer.TryComplete();
        }

        private void ThrowPendingError()
        {
            var error = _pendingError;
            if (error != null)
            {
                _pendingError = null;
                throw error;
            }
        }

        /// <summary>
        /// Unsubscribes from the track and releases the native resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            FfiClient.Instance.RemoveStreamHandler(_handleId);
            _events.Writer.TryComplete();
            _handle.Dispose();
        }

        /// <summary>
        /// Unsubscribes from the track and releases the native resources, like <see cref="Dispose"/>.
        /// </summary>
        /// <returns>A completed task.</returns>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
