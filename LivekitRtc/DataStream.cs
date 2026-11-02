// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Default stream chunk size.
    /// </summary>
    public static class DataStreamConstants
    {
        /// <summary>
        /// Maximum chunk size for data streams.
        /// </summary>
        public const int StreamChunkSize = 15000;
    }

    /// <summary>
    /// Base information for a data stream.
    /// </summary>
    public class BaseStreamInfo
    {
        /// <summary>
        /// Gets the stream ID.
        /// </summary>
        public string StreamId { get; }

        /// <summary>
        /// Gets the MIME type.
        /// </summary>
        public string MimeType { get; }

        /// <summary>
        /// Gets the topic.
        /// </summary>
        public string Topic { get; }

        /// <summary>
        /// Gets the timestamp in milliseconds.
        /// </summary>
        public long Timestamp { get; }

        /// <summary>
        /// Gets the total size if known.
        /// </summary>
        public long? Size { get; }

        /// <summary>
        /// Gets the stream attributes.
        /// </summary>
        public Dictionary<string, string> Attributes { get; internal set; }

        /// <summary>
        /// Initializes base stream info.
        /// </summary>
        /// <param name="streamId">Unique ID of the stream.</param>
        /// <param name="mimeType">MIME type of the content.</param>
        /// <param name="topic">Topic the stream was sent on.</param>
        /// <param name="timestamp">Send time, in milliseconds since the Unix epoch.</param>
        /// <param name="size">Total size in bytes, or null when unknown.</param>
        /// <param name="attributes">Custom attributes, or null.</param>
        protected BaseStreamInfo(
            string streamId,
            string mimeType,
            string topic,
            long timestamp,
            long? size,
            Dictionary<string, string>? attributes
        )
        {
            StreamId = streamId;
            MimeType = mimeType;
            Topic = topic;
            Timestamp = timestamp;
            Size = size;
            Attributes = attributes ?? new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Information for a text stream.
    /// </summary>
    public class TextStreamInfo : BaseStreamInfo
    {
        /// <summary>
        /// Gets the attached stream IDs.
        /// </summary>
        public IReadOnlyList<string> Attachments { get; }

        /// <summary>
        /// Initializes text stream info.
        /// </summary>
        /// <param name="streamId">Unique ID of the stream.</param>
        /// <param name="mimeType">MIME type of the content.</param>
        /// <param name="topic">Topic the stream was sent on.</param>
        /// <param name="timestamp">Send time, in milliseconds since the Unix epoch.</param>
        /// <param name="size">Total size in bytes, or null when unknown.</param>
        /// <param name="attributes">Custom attributes, or null.</param>
        /// <param name="attachments">IDs of the attached byte streams, or null.</param>
        public TextStreamInfo(
            string streamId,
            string mimeType,
            string topic,
            long timestamp,
            long? size,
            Dictionary<string, string>? attributes,
            IReadOnlyList<string>? attachments
        )
            : base(streamId, mimeType, topic, timestamp, size, attributes)
        {
            Attachments = attachments ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Information for a byte stream.
    /// </summary>
    public class ByteStreamInfo : BaseStreamInfo
    {
        /// <summary>
        /// Gets the stream name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes byte stream info.
        /// </summary>
        /// <param name="streamId">Unique ID of the stream.</param>
        /// <param name="mimeType">MIME type of the content.</param>
        /// <param name="topic">Topic the stream was sent on.</param>
        /// <param name="timestamp">Send time, in milliseconds since the Unix epoch.</param>
        /// <param name="size">Total size in bytes, or null when unknown.</param>
        /// <param name="attributes">Custom attributes, or null.</param>
        /// <param name="name">File name of the content; empty when null.</param>
        public ByteStreamInfo(
            string streamId,
            string mimeType,
            string topic,
            long timestamp,
            long? size,
            Dictionary<string, string>? attributes,
            string name
        )
            : base(streamId, mimeType, topic, timestamp, size, attributes)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>
    /// Reader for text data streams.
    /// Implements IAsyncEnumerable for modern async iteration patterns.
    /// </summary>
    public class TextStreamReader : IAsyncDisposable, IDisposable, IAsyncEnumerable<string>
    {
        private readonly Channel<string> _channel;
        private readonly TextStreamInfo _info;

        // Joins multi-byte characters that a sender split across two chunks
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private Exception? _error;
        private bool _disposed;

        /// <summary>
        /// Identity of the participant sending the stream.
        /// </summary>
        internal string SenderIdentity { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new text stream reader.
        /// </summary>
        /// <param name="header">The stream header.</param>
        internal TextStreamReader(DataStream.Types.Header header)
        {
            _channel = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
            );

            _info = new TextStreamInfo(
                header.StreamId,
                header.MimeType,
                header.Topic,
                header.Timestamp,
                header.HasTotalLength ? (long?)header.TotalLength : null,
                new Dictionary<string, string>(header.Attributes),
                header.TextHeader?.AttachedStreamIds != null
                    ? new List<string>(header.TextHeader.AttachedStreamIds)
                    : null
            );
        }

        /// <summary>
        /// Gets the stream info.
        /// </summary>
        public TextStreamInfo Info => _info;

        /// <summary>
        /// Called when a chunk is received.
        /// </summary>
        internal void OnChunk(DataStream.Types.Chunk chunk)
        {
            var bytes = chunk.Content.ToByteArray();
            var chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length, false)];
            var count = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0, false);
            if (count > 0)
            {
                _channel.Writer.TryWrite(new string(chars, 0, count));
            }
        }

        /// <summary>
        /// Called when the stream is closed.
        /// </summary>
        internal void OnClose(DataStream.Types.Trailer? trailer)
        {
            if (trailer != null)
            {
                foreach (var attr in trailer.Attributes)
                {
                    _info.Attributes[attr.Key] = attr.Value;
                }
            }

            // Flush an incomplete trailing character, if any
            var tail = new char[_decoder.GetCharCount(Array.Empty<byte>(), 0, 0, true)];
            var count = _decoder.GetChars(Array.Empty<byte>(), 0, 0, tail, 0, true);
            if (count > 0)
            {
                _channel.Writer.TryWrite(new string(tail, 0, count));
            }
            _channel.Writer.TryComplete();
        }

        /// <summary>
        /// Ends the stream abnormally (the sender or the room went away): pending and future reads
        /// throw <see cref="StreamException"/> once the buffered chunks are consumed.
        /// </summary>
        internal void Fail(Exception error)
        {
            _error ??= error;
            _channel.Writer.TryComplete();
        }

        private void ThrowIfFailed()
        {
            if (_error != null)
            {
                throw new StreamException(
                    $"Data stream {_info.StreamId} ended abnormally: {_error.Message}",
                    _error
                );
            }
        }

        /// <summary>
        /// Reads the next chunk of text.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The next text chunk, or null if the stream is complete.</returns>
        /// <exception cref="StreamException">The sender or the room went away before the stream was closed.</exception>
        public async ValueTask<string?> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_channel.Reader.TryRead(out var text))
                    {
                        return text;
                    }
                }
                ThrowIfFailed();
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ChannelClosedException)
            {
                ThrowIfFailed();
                return null;
            }
        }

        /// <summary>
        /// Reads all remaining text from the stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The complete text content.</returns>
        public async ValueTask<string> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            await foreach (
                var chunk in this.WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                sb.Append(chunk);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns an async enumerator for iterating over text chunks.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async enumerator of text chunks.</returns>
        public async IAsyncEnumerator<string> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            await foreach (
                var chunk in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                yield return chunk;
            }
            ThrowIfFailed();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _channel.Writer.TryComplete();

            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return default;
            _disposed = true;
            _channel.Writer.TryComplete();

            GC.SuppressFinalize(this);
            return default;
        }
    }

    /// <summary>
    /// Reader for byte data streams.
    /// Implements IAsyncEnumerable for modern async iteration patterns.
    /// </summary>
    public class ByteStreamReader
        : IAsyncDisposable,
            IDisposable,
            IAsyncEnumerable<ReadOnlyMemory<byte>>
    {
        private readonly Channel<byte[]> _channel;
        private readonly ByteStreamInfo _info;
        private Exception? _error;
        private bool _disposed;

        /// <summary>
        /// Identity of the participant sending the stream.
        /// </summary>
        internal string SenderIdentity { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new byte stream reader.
        /// </summary>
        /// <param name="header">The stream header.</param>
        /// <param name="capacity">The channel capacity (0 for unbounded).</param>
        internal ByteStreamReader(DataStream.Types.Header header, int capacity = 0)
        {
            _channel =
                capacity > 0
                    ? Channel.CreateBounded<byte[]>(
                        new BoundedChannelOptions(capacity)
                        {
                            FullMode = BoundedChannelFullMode.Wait,
                            SingleReader = false,
                            SingleWriter = true,
                        }
                    )
                    : Channel.CreateUnbounded<byte[]>(
                        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true }
                    );

            _info = new ByteStreamInfo(
                header.StreamId,
                header.MimeType,
                header.Topic,
                header.Timestamp,
                header.HasTotalLength ? (long?)header.TotalLength : null,
                new Dictionary<string, string>(header.Attributes),
                header.ByteHeader?.Name ?? string.Empty
            );
        }

        /// <summary>
        /// Gets the stream info.
        /// </summary>
        public ByteStreamInfo Info => _info;

        /// <summary>
        /// Called when a chunk is received.
        /// </summary>
        internal void OnChunk(DataStream.Types.Chunk chunk)
        {
            _channel.Writer.TryWrite(chunk.Content.ToByteArray());
        }

        /// <summary>
        /// Called when the stream is closed.
        /// </summary>
        internal void OnClose(DataStream.Types.Trailer? trailer)
        {
            if (trailer != null)
            {
                foreach (var attr in trailer.Attributes)
                {
                    _info.Attributes[attr.Key] = attr.Value;
                }
            }
            _channel.Writer.TryComplete();
        }

        /// <summary>
        /// Ends the stream abnormally (the sender or the room went away): pending and future reads
        /// throw <see cref="StreamException"/> once the buffered chunks are consumed.
        /// </summary>
        internal void Fail(Exception error)
        {
            _error ??= error;
            _channel.Writer.TryComplete();
        }

        private void ThrowIfFailed()
        {
            if (_error != null)
            {
                throw new StreamException(
                    $"Data stream {_info.StreamId} ended abnormally: {_error.Message}",
                    _error
                );
            }
        }

        /// <summary>
        /// Reads the next chunk of bytes.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The next byte chunk, or null if the stream is complete.</returns>
        /// <exception cref="StreamException">The sender or the room went away before the stream was closed.</exception>
        public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                if (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_channel.Reader.TryRead(out var data))
                    {
                        return data;
                    }
                }
                ThrowIfFailed();
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ChannelClosedException)
            {
                ThrowIfFailed();
                return null;
            }
        }

        /// <summary>
        /// Reads all remaining bytes from the stream.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The complete byte content.</returns>
        public async ValueTask<byte[]> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            var chunks = new List<byte[]>();
            int totalLength = 0;

            await foreach (
                var chunk in this.WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                var array = chunk.ToArray();
                chunks.Add(array);
                totalLength += array.Length;
            }

            var result = new byte[totalLength];
            int offset = 0;
            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }
            return result;
        }

        /// <summary>
        /// Returns an async enumerator for iterating over byte chunks.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async enumerator of byte chunks.</returns>
        public async IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        )
        {
            await foreach (
                var chunk in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                yield return chunk;
            }
            ThrowIfFailed();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _channel.Writer.TryComplete();

            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return default;
            _disposed = true;
            _channel.Writer.TryComplete();

            GC.SuppressFinalize(this);
            return default;
        }
    }

    /// <summary>
    /// Handler for text streams.
    /// </summary>
    /// <param name="reader">The text stream reader.</param>
    /// <param name="participantIdentity">The sender's identity.</param>
    public delegate void TextStreamHandler(TextStreamReader reader, string participantIdentity);

    /// <summary>
    /// Handler for byte streams.
    /// </summary>
    /// <param name="reader">The byte stream reader.</param>
    /// <param name="participantIdentity">The sender's identity.</param>
    public delegate void ByteStreamHandler(ByteStreamReader reader, string participantIdentity);

    /// <summary>
    /// Internal registry for managing stream handlers by topic.
    /// Provides a cleaner abstraction for handler registration and dispatch.
    /// </summary>
    internal sealed class StreamHandlerRegistry
    {
        private readonly Dictionary<string, TextStreamHandler> _textStreamHandlers =
            new Dictionary<string, TextStreamHandler>();
        private readonly Dictionary<string, ByteStreamHandler> _byteStreamHandlers =
            new Dictionary<string, ByteStreamHandler>();
        private readonly object _lock = new object();

        /// <summary>
        /// Registers a handler for incoming text streams on a specific topic.
        /// </summary>
        /// <param name="topic">The topic to listen for.</param>
        /// <param name="handler">The handler to invoke when a stream is received.</param>
        /// <exception cref="StreamException">Thrown when a handler for this topic is already registered.</exception>
        internal void RegisterTextStreamHandler(string topic, TextStreamHandler handler)
        {
            lock (_lock)
            {
                if (!_textStreamHandlers.TryAdd(topic, handler))
                {
                    throw new StreamException(
                        $"A text stream handler for topic '{topic}' has already been registered."
                    );
                }
            }
        }

        /// <summary>
        /// Registers a handler for incoming byte streams on a specific topic.
        /// </summary>
        /// <param name="topic">The topic to listen for.</param>
        /// <param name="handler">The handler to invoke when a stream is received.</param>
        /// <exception cref="StreamException">Thrown when a handler for this topic is already registered.</exception>
        internal void RegisterByteStreamHandler(string topic, ByteStreamHandler handler)
        {
            lock (_lock)
            {
                if (!_byteStreamHandlers.TryAdd(topic, handler))
                {
                    throw new StreamException(
                        $"A byte stream handler for topic '{topic}' has already been registered."
                    );
                }
            }
        }

        /// <summary>
        /// Unregisters a text stream handler for a specific topic.
        /// </summary>
        /// <param name="topic">The topic to unregister.</param>
        internal void UnregisterTextStreamHandler(string topic)
        {
            lock (_lock)
            {
                _textStreamHandlers.Remove(topic);
            }
        }

        /// <summary>
        /// Unregisters a byte stream handler for a specific topic.
        /// </summary>
        /// <param name="topic">The topic to unregister.</param>
        internal void UnregisterByteStreamHandler(string topic)
        {
            lock (_lock)
            {
                _byteStreamHandlers.Remove(topic);
            }
        }

        /// <summary>
        /// Dispatches a text stream to the registered handler for its topic.
        /// </summary>
        /// <param name="reader">The text stream reader.</param>
        /// <param name="participantIdentity">The identity of the participant who sent the stream.</param>
        /// <returns>True if a handler was found and invoked, false otherwise.</returns>
        internal bool Dispatch(TextStreamReader reader, string participantIdentity)
        {
            TextStreamHandler? handler;
            lock (_lock)
            {
                _textStreamHandlers.TryGetValue(reader.Info.Topic, out handler);
            }
            if (handler == null)
                return false;
            handler(reader, participantIdentity);
            return true;
        }

        /// <summary>
        /// Dispatches a byte stream to the registered handler for its topic.
        /// </summary>
        /// <param name="reader">The byte stream reader.</param>
        /// <param name="participantIdentity">The identity of the participant who sent the stream.</param>
        /// <returns>True if a handler was found and invoked, false otherwise.</returns>
        internal bool Dispatch(ByteStreamReader reader, string participantIdentity)
        {
            ByteStreamHandler? handler;
            lock (_lock)
            {
                _byteStreamHandlers.TryGetValue(reader.Info.Topic, out handler);
            }
            if (handler == null)
                return false;
            handler(reader, participantIdentity);
            return true;
        }
    }

    /// <summary>
    /// Exception thrown for stream-related errors.
    /// </summary>
    public class StreamException : Exception
    {
        /// <summary>
        /// Initializes a new stream exception.
        /// </summary>
        /// <param name="message">Description of the error.</param>
        public StreamException(string message)
            : base(message) { }

        /// <summary>
        /// Initializes a new stream exception with an inner exception.
        /// </summary>
        /// <param name="message">Description of the error.</param>
        /// <param name="innerException">The underlying error.</param>
        public StreamException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Writer for text data streams.
    /// </summary>
    public class TextStreamWriter : IAsyncDisposable, IDisposable
    {
        private readonly LocalParticipant _participant;
        private readonly TextStreamInfo _info;
        private readonly string[] _destinationIdentities;
        private readonly string _senderIdentity;
        private int _nextChunkIndex;
        private bool _closed;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        internal TextStreamWriter(
            LocalParticipant participant,
            string streamId,
            string topic,
            string mimeType,
            long timestamp,
            long? totalSize,
            Dictionary<string, string>? attributes,
            string[]? destinationIdentities,
            string? senderIdentity
        )
        {
            _participant = participant;
            _destinationIdentities = destinationIdentities ?? Array.Empty<string>();
            _senderIdentity = senderIdentity ?? participant.Identity;
            _info = new TextStreamInfo(
                streamId,
                mimeType,
                topic,
                timestamp,
                totalSize,
                attributes,
                null
            );
        }

        /// <summary>
        /// Gets the stream info.
        /// </summary>
        public TextStreamInfo Info => _info;

        /// <summary>
        /// Writes text to the stream.
        /// </summary>
        /// <param name="text">Text to send; long texts are split into chunks on character boundaries.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once every chunk has been sent.</returns>
        public async Task WriteAsync(string text, CancellationToken cancellationToken = default)
        {
            if (_closed)
                throw new InvalidOperationException("Cannot write to a closed stream");

            if (string.IsNullOrEmpty(text))
                return;

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                for (int offset = 0; offset < bytes.Length; )
                {
                    var length = Math.Min(
                        DataStreamConstants.StreamChunkSize,
                        bytes.Length - offset
                    );
                    // Never split a multi-byte character across chunks (receivers decode each chunk
                    // on its own): back off to the last character boundary, like the other SDKs
                    if (offset + length < bytes.Length)
                    {
                        var boundary = length;
                        while (boundary > 0 && (bytes[offset + boundary] & 0xC0) == 0x80)
                        {
                            boundary--;
                        }
                        if (boundary > 0)
                        {
                            length = boundary;
                        }
                    }
                    var chunk = new byte[length];
                    Buffer.BlockCopy(bytes, offset, chunk, 0, length);

                    await SendChunkAsync(ByteString.CopyFrom(chunk), cancellationToken);
                    offset += length;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task SendChunkAsync(ByteString content, CancellationToken cancellationToken)
        {
            var request = new FfiRequest
            {
                SendStreamChunk = new SendStreamChunkRequest
                {
                    LocalParticipantHandle = _participant.Handle.HandleId,
                    SenderIdentity = _senderIdentity,
                    Chunk = new DataStream.Types.Chunk
                    {
                        StreamId = _info.StreamId,
                        ChunkIndex = (ulong)_nextChunkIndex++,
                        Content = content,
                    },
                },
            };

            if (_destinationIdentities.Length > 0)
            {
                request.SendStreamChunk.DestinationIdentities.AddRange(_destinationIdentities);
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SendStreamChunk?.AsyncId ?? 0;

            var chunkEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SendStreamChunk
                    && e.SendStreamChunk?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            if (chunkEvent.SendStreamChunk?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to send stream chunk: {chunkEvent.SendStreamChunk.Error}"
                );
            }
        }

        /// <summary>
        /// Closes the stream.
        /// </summary>
        /// <param name="reason">Optional reason sent to the readers in the stream trailer.</param>
        /// <param name="attributes">Attributes to update on the stream, sent in the trailer.</param>
        /// <returns>A task that completes once the server has acknowledged the trailer.</returns>
        public Task CloseAsync(string? reason = null, Dictionary<string, string>? attributes = null)
        {
            return CloseAsync(reason, attributes, CancellationToken.None);
        }

        /// <summary>
        /// Closes the stream, with a token to abandon the wait for the server's acknowledgement.
        /// </summary>
        /// <param name="reason">Optional reason sent to the readers in the stream trailer.</param>
        /// <param name="attributes">Attributes to update on the stream, sent in the trailer.</param>
        /// <param name="cancellationToken">Abandons the wait for the server's acknowledgement; the stream is closed anyway.</param>
        /// <returns>A task that completes once the server has acknowledged the trailer.</returns>
        public async Task CloseAsync(
            string? reason,
            Dictionary<string, string>? attributes,
            CancellationToken cancellationToken
        )
        {
            if (_closed)
                return;

            _closed = true;

            var request = new FfiRequest
            {
                SendStreamTrailer = new SendStreamTrailerRequest
                {
                    LocalParticipantHandle = _participant.Handle.HandleId,
                    SenderIdentity = _senderIdentity,
                    Trailer = new DataStream.Types.Trailer
                    {
                        StreamId = _info.StreamId,
                        Reason = reason ?? string.Empty,
                    },
                },
            };

            if (_destinationIdentities.Length > 0)
            {
                request.SendStreamTrailer.DestinationIdentities.AddRange(_destinationIdentities);
            }

            if (attributes != null)
            {
                foreach (var kvp in attributes)
                {
                    request.SendStreamTrailer.Trailer.Attributes.Add(kvp.Key, kvp.Value);
                }
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SendStreamTrailer?.AsyncId ?? 0;

            var trailerEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SendStreamTrailer
                    && e.SendStreamTrailer?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            if (trailerEvent.SendStreamTrailer?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to send stream trailer: {trailerEvent.SendStreamTrailer.Error}"
                );
            }
        }

        /// <summary>
        /// Closes the stream if it is still open (the trailer is sent; a failure is logged) and releases the writer.
        /// </summary>
        public void Dispose()
        {
            if (!_closed)
            {
                // Send the trailer so the readers complete (the request itself goes out
                // synchronously); Dispose cannot throw, so a failure is only logged
                _ = CloseAsync()
                    .ContinueWith(
                        t =>
                            Console.Error.WriteLine(
                                $"[WARN] Data stream {_info.StreamId}: closing on Dispose failed: {t.Exception?.GetBaseException()}"
                            ),
                        TaskContinuationOptions.OnlyOnFaulted
                    );
            }
            _writeLock?.Dispose();
        }

        /// <summary>
        /// Closes the stream if it is still open and releases the writer.
        /// </summary>
        /// <returns>A task that completes once the stream is closed.</returns>
        public async ValueTask DisposeAsync()
        {
            if (!_closed)
            {
                await CloseAsync();
            }
            _writeLock?.Dispose();
        }
    }

    /// <summary>
    /// Writer for byte data streams.
    /// </summary>
    public class ByteStreamWriter : IAsyncDisposable, IDisposable
    {
        private readonly LocalParticipant _participant;
        private readonly ByteStreamInfo _info;
        private readonly string[] _destinationIdentities;
        private readonly string _senderIdentity;
        private int _nextChunkIndex;
        private bool _closed;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        internal ByteStreamWriter(
            LocalParticipant participant,
            string streamId,
            string topic,
            string mimeType,
            long timestamp,
            long? totalSize,
            Dictionary<string, string>? attributes,
            string name,
            string[]? destinationIdentities,
            string? senderIdentity
        )
        {
            _participant = participant;
            _destinationIdentities = destinationIdentities ?? Array.Empty<string>();
            _senderIdentity = senderIdentity ?? participant.Identity;
            _info = new ByteStreamInfo(
                streamId,
                mimeType,
                topic,
                timestamp,
                totalSize,
                attributes,
                name
            );
        }

        /// <summary>
        /// Gets the stream info.
        /// </summary>
        public ByteStreamInfo Info => _info;

        /// <summary>
        /// Writes bytes to the stream.
        /// </summary>
        /// <param name="data">Bytes to send; large arrays are split into chunks.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once every chunk has been sent.</returns>
        public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (_closed)
                throw new InvalidOperationException("Cannot write to a closed stream");

            if (data == null || data.Length == 0)
                return;

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                for (
                    int offset = 0;
                    offset < data.Length;
                    offset += DataStreamConstants.StreamChunkSize
                )
                {
                    var length = Math.Min(
                        DataStreamConstants.StreamChunkSize,
                        data.Length - offset
                    );
                    var chunk = new byte[length];
                    Buffer.BlockCopy(data, offset, chunk, 0, length);

                    await SendChunkAsync(ByteString.CopyFrom(chunk), cancellationToken);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task SendChunkAsync(ByteString content, CancellationToken cancellationToken)
        {
            var request = new FfiRequest
            {
                SendStreamChunk = new SendStreamChunkRequest
                {
                    LocalParticipantHandle = _participant.Handle.HandleId,
                    SenderIdentity = _senderIdentity,
                    Chunk = new DataStream.Types.Chunk
                    {
                        StreamId = _info.StreamId,
                        ChunkIndex = (ulong)_nextChunkIndex++,
                        Content = content,
                    },
                },
            };

            if (_destinationIdentities.Length > 0)
            {
                request.SendStreamChunk.DestinationIdentities.AddRange(_destinationIdentities);
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SendStreamChunk?.AsyncId ?? 0;

            var chunkEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SendStreamChunk
                    && e.SendStreamChunk?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            if (chunkEvent.SendStreamChunk?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to send stream chunk: {chunkEvent.SendStreamChunk.Error}"
                );
            }
        }

        /// <summary>
        /// Closes the stream.
        /// </summary>
        /// <param name="reason">Optional reason sent to the readers in the stream trailer.</param>
        /// <param name="attributes">Attributes to update on the stream, sent in the trailer.</param>
        /// <returns>A task that completes once the server has acknowledged the trailer.</returns>
        public Task CloseAsync(string? reason = null, Dictionary<string, string>? attributes = null)
        {
            return CloseAsync(reason, attributes, CancellationToken.None);
        }

        /// <summary>
        /// Closes the stream, with a token to abandon the wait for the server's acknowledgement.
        /// </summary>
        /// <param name="reason">Optional reason sent to the readers in the stream trailer.</param>
        /// <param name="attributes">Attributes to update on the stream, sent in the trailer.</param>
        /// <param name="cancellationToken">Abandons the wait for the server's acknowledgement; the stream is closed anyway.</param>
        /// <returns>A task that completes once the server has acknowledged the trailer.</returns>
        public async Task CloseAsync(
            string? reason,
            Dictionary<string, string>? attributes,
            CancellationToken cancellationToken
        )
        {
            if (_closed)
                return;

            _closed = true;

            var request = new FfiRequest
            {
                SendStreamTrailer = new SendStreamTrailerRequest
                {
                    LocalParticipantHandle = _participant.Handle.HandleId,
                    SenderIdentity = _senderIdentity,
                    Trailer = new DataStream.Types.Trailer
                    {
                        StreamId = _info.StreamId,
                        Reason = reason ?? string.Empty,
                    },
                },
            };

            if (_destinationIdentities.Length > 0)
            {
                request.SendStreamTrailer.DestinationIdentities.AddRange(_destinationIdentities);
            }

            if (attributes != null)
            {
                foreach (var kvp in attributes)
                {
                    request.SendStreamTrailer.Trailer.Attributes.Add(kvp.Key, kvp.Value);
                }
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SendStreamTrailer?.AsyncId ?? 0;

            var trailerEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SendStreamTrailer
                    && e.SendStreamTrailer?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            if (trailerEvent.SendStreamTrailer?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to send stream trailer: {trailerEvent.SendStreamTrailer.Error}"
                );
            }
        }

        /// <summary>
        /// Closes the stream if it is still open (the trailer is sent; a failure is logged) and releases the writer.
        /// </summary>
        public void Dispose()
        {
            if (!_closed)
            {
                // Send the trailer so the readers complete (the request itself goes out
                // synchronously); Dispose cannot throw, so a failure is only logged
                _ = CloseAsync()
                    .ContinueWith(
                        t =>
                            Console.Error.WriteLine(
                                $"[WARN] Data stream {_info.StreamId}: closing on Dispose failed: {t.Exception?.GetBaseException()}"
                            ),
                        TaskContinuationOptions.OnlyOnFaulted
                    );
            }
            _writeLock?.Dispose();
        }

        /// <summary>
        /// Closes the stream if it is still open and releases the writer.
        /// </summary>
        /// <returns>A task that completes once the stream is closed.</returns>
        public async ValueTask DisposeAsync()
        {
            if (!_closed)
            {
                await CloseAsync();
            }
            _writeLock?.Dispose();
        }
    }
}
