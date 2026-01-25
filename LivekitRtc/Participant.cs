// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Base class for participants in a room.
    /// </summary>
    public abstract class Participant
    {
        internal FfiHandle Handle { get; }
        internal Room Room { get; }

        internal ParticipantInfo _info;

        /// <summary>
        /// Participant session ID.
        /// </summary>
        public string Sid => _info.Sid;

        /// <summary>
        /// Participant identity.
        /// </summary>
        public string Identity => _info.Identity;

        /// <summary>
        /// Participant display name.
        /// </summary>
        public string Name => _info.Name;

        /// <summary>
        /// Participant metadata.
        /// </summary>
        public string Metadata => _info.Metadata;

        /// <summary>
        /// Kind of participant.
        /// </summary>
        public Proto.ParticipantKind Kind => _info.Kind;

        /// <summary>
        /// Track publications by this participant.
        /// </summary>
        protected readonly Dictionary<string, TrackPublication> _trackPublications;

        /// <summary>
        /// Gets all track publications by this participant.
        /// </summary>
        public IReadOnlyDictionary<string, TrackPublication> TrackPublications =>
            _trackPublications;

        internal Participant(FfiHandle handle, ParticipantInfo? info, Room room)
        {
            Handle = handle;
            Room = room;
            _info = info ?? new ParticipantInfo();
            _trackPublications = new Dictionary<string, TrackPublication>();
        }

        internal void UpdateInfo(ParticipantInfo info)
        {
            _info = info;
        }

        /// <summary>
        /// Gets a track publication by SID.
        /// </summary>
        /// <param name="sid">Track SID.</param>
        /// <returns>The track publication, or null if not found.</returns>
        public TrackPublication? GetTrackPublication(string sid)
        {
            return _trackPublications.TryGetValue(sid, out var pub) ? pub : null;
        }
    }

    /// <summary>
    /// Represents the local participant in the room.
    /// </summary>
    public class LocalParticipant : Participant
    {
        private readonly Dictionary<string, RpcMethodHandler> _rpcHandlers =
            new Dictionary<string, RpcMethodHandler>();

        internal LocalParticipant(FfiHandle handle, ParticipantInfo? info, Room room)
            : base(handle, info, room) { }

        /// <summary>
        /// Publishes a track to the room.
        /// </summary>
        /// <param name="track">The track to publish.</param>
        /// <param name="options">Publication options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The track publication.</returns>
        public async Task<LocalTrackPublication> PublishTrackAsync(
            LocalTrack track,
            TrackPublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options ??= new TrackPublishOptions();

            var protoOptions = new Proto.TrackPublishOptions
            {
                Source = (Proto.TrackSource)options.Source,
                Simulcast = options.Simulcast,
            };

            if (options.AudioEncoding != null)
            {
                protoOptions.AudioEncoding = new Proto.AudioEncoding
                {
                    MaxBitrate = options.AudioEncoding.MaxBitrate,
                };
            }

            if (options.VideoEncoding != null)
            {
                protoOptions.VideoEncoding = new Proto.VideoEncoding
                {
                    MaxBitrate = options.VideoEncoding.MaxBitrate,
                    MaxFramerate = options.VideoEncoding.MaxFramerate,
                };
            }

            var request = new FfiRequest
            {
                PublishTrack = new PublishTrackRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    TrackHandle = track.Handle.HandleId,
                    Options = protoOptions,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.PublishTrack == null)
                throw new InvalidOperationException("Invalid publish track response");

            var asyncId = response.PublishTrack.AsyncId;

            // Wait for publish callback (don't hold lock during wait)
            var publishEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.PublishTrack
                    && e.PublishTrack?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            var callback = publishEvent.PublishTrack!;

            if (callback.HasError)
            {
                throw new RoomException($"Failed to publish track: {callback.Error}");
            }

            var publication = callback.Publication;
            if (publication == null || publication.Info == null)
            {
                throw new RoomException("Publish callback has no publication");
            }

            var localPublication = new LocalTrackPublication(
                FfiHandle.FromId(publication.Handle.Id),
                publication.Info,
                this
            );

            localPublication.Track = track;

            // Acquire lock only for modifying state (matches Node SDK pattern)
            await Room.FfiEventLock.WaitAsync(cancellationToken);
            try
            {
                _trackPublications[localPublication.Sid] = localPublication;
                return localPublication;
            }
            finally
            {
                // Release lock - now FFI events can be processed
                Room.FfiEventLock.Release();
            }
        }

        /// <summary>
        /// Unpublishes a track from the room.
        /// </summary>
        /// <param name="trackSid">The SID of the track to unpublish.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task UnpublishTrackAsync(
            string trackSid,
            CancellationToken cancellationToken = default
        )
        {
            var request = new FfiRequest
            {
                UnpublishTrack = new UnpublishTrackRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    TrackSid = trackSid,
                    StopOnUnpublish = true,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.UnpublishTrack == null)
                throw new InvalidOperationException("Invalid unpublish track response");

            var asyncId = response.UnpublishTrack.AsyncId;

            // Wait for unpublish callback (don't hold lock during wait)
            var unpublishEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.UnpublishTrack
                    && e.UnpublishTrack?.AsyncId == asyncId,
                TimeSpan.FromSeconds(10),
                cancellationToken
            );

            var callback = unpublishEvent.UnpublishTrack!;

            if (!string.IsNullOrEmpty(callback.Error))
            {
                throw new RoomException($"Failed to unpublish track: {callback.Error}");
            }

            // Acquire lock only for modifying state (matches Node SDK pattern)
            await Room.FfiEventLock.WaitAsync(cancellationToken);
            try
            {
                _trackPublications.Remove(trackSid);
            }
            finally
            {
                // Release lock - now FFI events can be processed
                Room.FfiEventLock.Release();
            }
        }

        /// <summary>
        /// Publishes data to the room.
        /// </summary>
        /// <param name="data">The data to publish.</param>
        /// <param name="options">Publish options.</param>
        public async Task PublishDataAsync(byte[] data, DataPublishOptions? options = null)
        {
            options ??= new DataPublishOptions();

            ulong asyncId;

            unsafe
            {
                fixed (byte* dataPtr = data)
                {
                    var request = new FfiRequest
                    {
                        PublishData = new PublishDataRequest
                        {
                            LocalParticipantHandle = Handle.HandleId,
                            DataPtr = (ulong)dataPtr,
                            DataLen = (ulong)data.Length,
                            Reliable = options.Reliable,
                            Topic = options.Topic ?? string.Empty,
                        },
                    };

                    if (options.DestinationIdentities != null)
                    {
                        request.PublishData.DestinationIdentities.AddRange(
                            options.DestinationIdentities
                        );
                    }

                    var response = FfiClient.Instance.SendRequest(request);
                    asyncId = response.PublishData?.AsyncId ?? 0;
                }
            }

            // Wait for publish callback (outside unsafe context)
            var publishEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.PublishData
                    && e.PublishData?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (publishEvent.PublishData?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to publish data: {publishEvent.PublishData.Error}"
                );
            }
        }

        /// <summary>
        /// Publishes SIP DTMF (Dual-Tone Multi-Frequency) tones to the room.
        /// </summary>
        /// <param name="code">The DTMF code.</param>
        /// <param name="digit">The DTMF digit as a string.</param>
        /// <param name="destinationIdentities">Optional destination participant identities.</param>
        public async Task PublishSipDtmfAsync(
            uint code,
            string digit,
            string[]? destinationIdentities = null
        )
        {
            var request = new FfiRequest
            {
                PublishSipDtmf = new PublishSipDtmfRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    Code = code,
                    Digit = digit ?? string.Empty,
                },
            };

            if (destinationIdentities != null)
            {
                request.PublishSipDtmf.DestinationIdentities.AddRange(destinationIdentities);
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.PublishSipDtmf?.AsyncId ?? 0;

            var dtmfEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.PublishSipDtmf
                    && e.PublishSipDtmf?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (dtmfEvent.PublishSipDtmf?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to publish SIP DTMF: {dtmfEvent.PublishSipDtmf.Error}"
                );
            }
        }

        /// <summary>
        /// Publishes transcription data to the room.
        /// </summary>
        /// <param name="transcription">The transcription to publish.</param>
        public async Task PublishTranscriptionAsync(Transcription transcription)
        {
            if (transcription == null)
                throw new ArgumentNullException(nameof(transcription));

            var request = new FfiRequest
            {
                PublishTranscription = new PublishTranscriptionRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    ParticipantIdentity = transcription.ParticipantIdentity,
                    TrackId = transcription.TrackSid,
                },
            };

            foreach (var segment in transcription.Segments)
            {
                request.PublishTranscription.Segments.Add(
                    new Proto.TranscriptionSegment
                    {
                        Id = segment.Id,
                        Text = segment.Text,
                        StartTime = (ulong)segment.StartTime,
                        EndTime = (ulong)segment.EndTime,
                        Final = segment.IsFinal,
                        Language = segment.Language,
                    }
                );
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.PublishTranscription?.AsyncId ?? 0;

            var transcriptionEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.PublishTranscription
                    && e.PublishTranscription?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (transcriptionEvent.PublishTranscription?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to publish transcription: {transcriptionEvent.PublishTranscription.Error}"
                );
            }
        }

        /// <summary>
        /// Sets the local participant's metadata.
        /// </summary>
        /// <param name="metadata">The new metadata.</param>
        public async Task SetMetadataAsync(string metadata)
        {
            var request = new FfiRequest
            {
                SetLocalMetadata = new SetLocalMetadataRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    Metadata = metadata,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SetLocalMetadata?.AsyncId ?? 0;

            // Wait for metadata update callback
            var metadataEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SetLocalMetadata
                    && e.SetLocalMetadata?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (metadataEvent.SetLocalMetadata?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to set metadata: {metadataEvent.SetLocalMetadata.Error}"
                );
            }
        }

        /// <summary>
        /// Sets the local participant's name.
        /// </summary>
        /// <param name="name">The new name.</param>
        public async Task SetNameAsync(string name)
        {
            var request = new FfiRequest
            {
                SetLocalName = new SetLocalNameRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    Name = name,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SetLocalName?.AsyncId ?? 0;

            // Wait for name update callback
            var nameEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SetLocalName
                    && e.SetLocalName?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (nameEvent.SetLocalName?.HasError == true)
            {
                throw new RoomException($"Failed to set name: {nameEvent.SetLocalName.Error}");
            }
        }

        /// <summary>
        /// Sets custom attributes for the local participant.
        /// </summary>
        /// <param name="attributes">The attributes to set.</param>
        /// <remarks>
        /// This requires `canUpdateOwnMetadata` permission.
        /// Existing attributes that are not overridden will remain unchanged.
        /// </remarks>
        public async Task SetAttributesAsync(Dictionary<string, string> attributes)
        {
            if (attributes == null)
                throw new ArgumentNullException(nameof(attributes));

            var request = new FfiRequest
            {
                SetLocalAttributes = new SetLocalAttributesRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                },
            };

            foreach (var kvp in attributes)
            {
                request.SetLocalAttributes.Attributes.Add(
                    new Proto.AttributesEntry { Key = kvp.Key, Value = kvp.Value }
                );
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SetLocalAttributes?.AsyncId ?? 0;

            var attributesEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SetLocalAttributes
                    && e.SetLocalAttributes?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (attributesEvent.SetLocalAttributes?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to set attributes: {attributesEvent.SetLocalAttributes.Error}"
                );
            }
        }

        /// <summary>
        /// Sends a chat message to participants in the room.
        /// </summary>
        /// <param name="message">The message text to send.</param>
        /// <param name="destinationIdentities">Optional array of destination participant identities. If null, broadcasts to all.</param>
        /// <param name="senderIdentity">Optional sender identity override.</param>
        /// <returns>The sent chat message.</returns>
        public async Task<ChatMessage> SendChatMessageAsync(
            string message,
            string[]? destinationIdentities = null,
            string? senderIdentity = null
        )
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentException("Message cannot be null or empty", nameof(message));

            var request = new FfiRequest
            {
                SendChatMessage = new SendChatMessageRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    Message = message,
                },
            };

            if (destinationIdentities != null)
            {
                request.SendChatMessage.DestinationIdentities.AddRange(destinationIdentities);
            }

            if (!string.IsNullOrEmpty(senderIdentity))
            {
                request.SendChatMessage.SenderIdentity = senderIdentity;
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SendChatMessage?.AsyncId ?? 0;

            var chatEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.ChatMessage
                    && e.ChatMessage?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (chatEvent.ChatMessage?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to send chat message: {chatEvent.ChatMessage.Error}"
                );
            }

            return ChatMessage.FromProto(chatEvent.ChatMessage!.ChatMessage!);
        }

        /// <summary>
        /// Edits a previously sent chat message.
        /// </summary>
        /// <param name="editText">The new text for the message.</param>
        /// <param name="originalMessage">The original message to edit.</param>
        /// <param name="destinationIdentities">Optional array of destination participant identities.</param>
        /// <param name="senderIdentity">Optional sender identity override.</param>
        /// <returns>The edited chat message.</returns>
        public async Task<ChatMessage> EditChatMessageAsync(
            string editText,
            ChatMessage originalMessage,
            string[]? destinationIdentities = null,
            string? senderIdentity = null
        )
        {
            if (string.IsNullOrEmpty(editText))
                throw new ArgumentException("Edit text cannot be null or empty", nameof(editText));
            if (originalMessage == null)
                throw new ArgumentNullException(nameof(originalMessage));

            var request = new FfiRequest
            {
                EditChatMessage = new EditChatMessageRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    EditText = editText,
                    OriginalMessage = originalMessage.ToProto(),
                },
            };

            if (destinationIdentities != null)
            {
                request.EditChatMessage.DestinationIdentities.AddRange(destinationIdentities);
            }

            if (!string.IsNullOrEmpty(senderIdentity))
            {
                request.EditChatMessage.SenderIdentity = senderIdentity;
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SendChatMessage?.AsyncId ?? 0;

            var chatEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.ChatMessage
                    && e.ChatMessage?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (chatEvent.ChatMessage?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to edit chat message: {chatEvent.ChatMessage.Error}"
                );
            }

            return ChatMessage.FromProto(chatEvent.ChatMessage!.ChatMessage!);
        }

        /// <summary>
        /// Streams text incrementally to participants in the room.
        /// </summary>
        /// <param name="topic">Optional topic for the stream.</param>
        /// <param name="attributes">Optional attributes for the stream.</param>
        /// <param name="destinationIdentities">Optional destination participant identities.</param>
        /// <param name="streamId">Optional custom stream ID (auto-generated if not provided).</param>
        /// <param name="senderIdentity">Optional sender identity override.</param>
        /// <param name="totalSize">Optional total size hint.</param>
        /// <returns>A text stream writer for sending text chunks.</returns>
        public async Task<TextStreamWriter> StreamTextAsync(
            string? topic = null,
            Dictionary<string, string>? attributes = null,
            string[]? destinationIdentities = null,
            string? streamId = null,
            string? senderIdentity = null,
            long? totalSize = null
        )
        {
            streamId ??= Guid.NewGuid().ToString();
            senderIdentity ??= Identity;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var header = new DataStream.Types.Header
            {
                StreamId = streamId,
                MimeType = "text/plain",
                Topic = topic ?? string.Empty,
                Timestamp = timestamp,
                TextHeader = new DataStream.Types.TextHeader
                {
                    OperationType = DataStream.Types.OperationType.Create,
                    Version = 0,
                    ReplyToStreamId = string.Empty,
                    Generated = false,
                },
            };

            if (totalSize.HasValue)
            {
                header.TotalLength = (ulong)totalSize.Value;
            }

            if (attributes != null)
            {
                foreach (var kvp in attributes)
                {
                    header.Attributes.Add(kvp.Key, kvp.Value);
                }
            }

            await SendStreamHeaderAsync(header, senderIdentity, destinationIdentities);

            return new TextStreamWriter(
                this,
                streamId,
                topic ?? string.Empty,
                "text/plain",
                (long)timestamp,
                totalSize,
                attributes,
                destinationIdentities,
                senderIdentity
            );
        }

        /// <summary>
        /// Sends text to participants in the room.
        /// </summary>
        /// <param name="text">The text to send.</param>
        /// <param name="topic">Optional topic for the message.</param>
        /// <param name="attributes">Optional attributes.</param>
        /// <param name="destinationIdentities">Optional destination participant identities.</param>
        /// <param name="streamId">Optional custom stream ID.</param>
        /// <returns>The text stream info.</returns>
        public async Task<TextStreamInfo> SendTextAsync(
            string text,
            string? topic = null,
            Dictionary<string, string>? attributes = null,
            string[]? destinationIdentities = null,
            string? streamId = null
        )
        {
            var totalSize = Encoding.UTF8.GetByteCount(text);
            var writer = await StreamTextAsync(
                topic,
                attributes,
                destinationIdentities,
                streamId,
                null,
                totalSize
            );

            await writer.WriteAsync(text);
            await writer.CloseAsync();

            return writer.Info;
        }

        /// <summary>
        /// Streams bytes incrementally to participants in the room.
        /// </summary>
        /// <param name="name">The stream name (e.g., filename).</param>
        /// <param name="topic">Optional topic for the stream.</param>
        /// <param name="mimeType">MIME type (default: application/octet-stream).</param>
        /// <param name="attributes">Optional attributes for the stream.</param>
        /// <param name="destinationIdentities">Optional destination participant identities.</param>
        /// <param name="streamId">Optional custom stream ID.</param>
        /// <param name="senderIdentity">Optional sender identity override.</param>
        /// <param name="totalSize">Optional total size hint.</param>
        /// <returns>A byte stream writer for sending byte chunks.</returns>
        public async Task<ByteStreamWriter> StreamBytesAsync(
            string name,
            string? topic = null,
            string? mimeType = null,
            Dictionary<string, string>? attributes = null,
            string[]? destinationIdentities = null,
            string? streamId = null,
            string? senderIdentity = null,
            long? totalSize = null
        )
        {
            streamId ??= Guid.NewGuid().ToString();
            senderIdentity ??= Identity;
            mimeType ??= "application/octet-stream";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var header = new DataStream.Types.Header
            {
                StreamId = streamId,
                MimeType = mimeType,
                Topic = topic ?? string.Empty,
                Timestamp = timestamp,
                ByteHeader = new DataStream.Types.ByteHeader { Name = name },
            };

            if (totalSize.HasValue)
            {
                header.TotalLength = (ulong)totalSize.Value;
            }

            if (attributes != null)
            {
                foreach (var kvp in attributes)
                {
                    header.Attributes.Add(kvp.Key, kvp.Value);
                }
            }

            await SendStreamHeaderAsync(header, senderIdentity, destinationIdentities);

            return new ByteStreamWriter(
                this,
                streamId,
                topic ?? string.Empty,
                mimeType,
                (long)timestamp,
                totalSize,
                attributes,
                name,
                destinationIdentities,
                senderIdentity
            );
        }

        /// <summary>
        /// Sends a file to participants in the room.
        /// </summary>
        /// <param name="filePath">The path to the file to send.</param>
        /// <param name="topic">Optional topic for the file.</param>
        /// <param name="mimeType">Optional MIME type (auto-detected if not provided).</param>
        /// <param name="attributes">Optional attributes.</param>
        /// <param name="destinationIdentities">Optional destination participant identities.</param>
        /// <param name="streamId">Optional custom stream ID.</param>
        /// <returns>The byte stream info.</returns>
        public async Task<ByteStreamInfo> SendFileAsync(
            string filePath,
            string? topic = null,
            string? mimeType = null,
            Dictionary<string, string>? attributes = null,
            string[]? destinationIdentities = null,
            string? streamId = null
        )
        {
            if (!System.IO.File.Exists(filePath))
                throw new System.IO.FileNotFoundException("File not found", filePath);

            var fileInfo = new System.IO.FileInfo(filePath);
            var fileName = fileInfo.Name;
            var fileSize = fileInfo.Length;

            // Auto-detect MIME type if not provided
            if (string.IsNullOrEmpty(mimeType))
            {
                var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                mimeType = extension switch
                {
                    ".txt" => "text/plain",
                    ".html" => "text/html",
                    ".json" => "application/json",
                    ".xml" => "application/xml",
                    ".pdf" => "application/pdf",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".mp4" => "video/mp4",
                    ".mp3" => "audio/mpeg",
                    ".wav" => "audio/wav",
                    ".zip" => "application/zip",
                    _ => "application/octet-stream",
                };
            }

            var writer = await StreamBytesAsync(
                fileName,
                topic,
                mimeType,
                attributes,
                destinationIdentities,
                streamId,
                null,
                fileSize
            );

            await using (var fileStream = System.IO.File.OpenRead(filePath))
            {
                var buffer = new byte[DataStreamConstants.StreamChunkSize];
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    var chunk = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                    await writer.WriteAsync(chunk);
                }
            }

            await writer.CloseAsync();
            return writer.Info;
        }

        /// <summary>
        /// Sets track subscription permissions for this local participant.
        /// </summary>
        /// <param name="allParticipantsAllowed">Whether all participants are allowed to subscribe.</param>
        /// <param name="participantPermissions">Specific permissions for individual participants.</param>
        public void SetTrackSubscriptionPermissions(
            bool allParticipantsAllowed,
            Proto.ParticipantTrackPermission[]? participantPermissions = null
        )
        {
            var request = new FfiRequest
            {
                SetTrackSubscriptionPermissions = new SetTrackSubscriptionPermissionsRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    AllParticipantsAllowed = allParticipantsAllowed,
                },
            };

            if (participantPermissions != null)
            {
                request.SetTrackSubscriptionPermissions.Permissions.AddRange(
                    participantPermissions
                );
            }

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Performs an RPC call to a remote participant.
        /// </summary>
        /// <param name="destinationIdentity">The identity of the destination participant.</param>
        /// <param name="method">The RPC method name to call.</param>
        /// <param name="payload">The payload to send with the request.</param>
        /// <param name="responseTimeout">Optional timeout in seconds for receiving a response (default: 10).</param>
        /// <returns>The response payload from the remote participant.</returns>
        /// <exception cref="RpcError">Thrown when the RPC call fails.</exception>
        public async Task<string> PerformRpcAsync(
            string destinationIdentity,
            string method,
            string payload,
            double? responseTimeout = null
        )
        {
            if (string.IsNullOrEmpty(destinationIdentity))
                throw new ArgumentNullException(nameof(destinationIdentity));
            if (string.IsNullOrEmpty(method))
                throw new ArgumentNullException(nameof(method));

            var request = new FfiRequest
            {
                PerformRpc = new PerformRpcRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    DestinationIdentity = destinationIdentity,
                    Method = method,
                    Payload = payload ?? string.Empty,
                },
            };

            if (responseTimeout.HasValue)
            {
                request.PerformRpc.ResponseTimeoutMs = (uint)(responseTimeout.Value * 1000);
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.PerformRpc?.AsyncId ?? 0;

            var callback = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.PerformRpc
                    && e.PerformRpc?.AsyncId == asyncId,
                TimeSpan.FromSeconds(responseTimeout ?? 30)
            );

            if (callback.PerformRpc?.Error != null)
            {
                throw RpcError.FromProto(callback.PerformRpc.Error);
            }

            return callback.PerformRpc?.Payload ?? string.Empty;
        }

        /// <summary>
        /// Registers a handler for incoming RPC method calls.
        /// Replaces any existing handler for the same method name.
        /// </summary>
        /// <param name="method">The RPC method name to handle.</param>
        /// <param name="handler">The handler function to invoke when this method is called.</param>
        /// <remarks>
        /// The handler should return a string response or throw an RpcError.
        /// Other exceptions will be converted to ApplicationError responses.
        /// </remarks>
        public void RegisterRpcMethod(string method, RpcMethodHandler handler)
        {
            if (string.IsNullOrEmpty(method))
                throw new ArgumentNullException(nameof(method));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _rpcHandlers[method] = handler;

            var request = new FfiRequest
            {
                RegisterRpcMethod = new RegisterRpcMethodRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    Method = method,
                },
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Unregisters a previously registered RPC method handler.
        /// </summary>
        /// <param name="method">The RPC method name to unregister.</param>
        public void UnregisterRpcMethod(string method)
        {
            if (string.IsNullOrEmpty(method))
                return;

            _rpcHandlers.Remove(method);

            var request = new FfiRequest
            {
                UnregisterRpcMethod = new UnregisterRpcMethodRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    Method = method,
                },
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Internal handler for processing incoming RPC method invocations.
        /// </summary>
        internal async Task HandleRpcMethodInvocationAsync(
            ulong invocationId,
            string method,
            string requestId,
            string callerIdentity,
            string payload,
            uint responseTimeoutMs
        )
        {
            RpcError? responseError = null;
            string? responsePayload = null;

            if (!_rpcHandlers.TryGetValue(method, out var handler))
            {
                responseError = RpcError.BuiltIn(
                    RpcErrorCode.UnsupportedMethod,
                    $"Method '{method}' is not registered"
                );
            }
            else
            {
                try
                {
                    var invocationData = new RpcInvocationData(
                        requestId,
                        callerIdentity,
                        payload,
                        responseTimeoutMs / 1000.0
                    );

                    responsePayload = await handler(invocationData);
                    responsePayload ??= string.Empty;
                }
                catch (RpcError rpcError)
                {
                    responseError = rpcError;
                }
                catch (Exception ex)
                {
                    responseError = new RpcError(
                        RpcErrorCode.ApplicationError,
                        $"Application error: {ex.Message}",
                        ex.StackTrace
                    );
                }
            }

            var request = new FfiRequest
            {
                RpcMethodInvocationResponse = new RpcMethodInvocationResponseRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    InvocationId = invocationId,
                    Payload = responsePayload ?? string.Empty, // Always set payload, even for errors
                },
            };

            if (responseError != null)
            {
                request.RpcMethodInvocationResponse.Error = responseError.ToProto();
            }

            var response = FfiClient.Instance.SendRequest(request);

            if (!string.IsNullOrEmpty(response.RpcMethodInvocationResponse?.Error))
            {
                throw new RoomException(
                    $"Failed to send RPC invocation response: {response.RpcMethodInvocationResponse.Error}"
                );
            }
        }

        private async Task SendStreamHeaderAsync(
            DataStream.Types.Header header,
            string senderIdentity,
            string[]? destinationIdentities
        )
        {
            var request = new FfiRequest
            {
                SendStreamHeader = new SendStreamHeaderRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    SenderIdentity = senderIdentity,
                    Header = header,
                },
            };

            if (destinationIdentities != null && destinationIdentities.Length > 0)
            {
                request.SendStreamHeader.DestinationIdentities.AddRange(destinationIdentities);
            }

            var response = FfiClient.Instance.SendRequest(request);
            var asyncId = response.SendStreamHeader?.AsyncId ?? 0;

            var headerEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SendStreamHeader
                    && e.SendStreamHeader?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30)
            );

            if (headerEvent.SendStreamHeader?.HasError == true)
            {
                throw new RoomException(
                    $"Failed to send stream header: {headerEvent.SendStreamHeader.Error}"
                );
            }
        }
    }

    /// <summary>
    /// Represents a remote participant in the room.
    /// </summary>
    public class RemoteParticipant : Participant
    {
        internal RemoteParticipant(FfiHandle handle, ParticipantInfo? info, Room room)
            : base(handle, info, room) { }

        internal void AddPublication(OwnedTrackPublication pub)
        {
            if (pub.Handle == null || pub.Info == null)
                return;

            var publication = new RemoteTrackPublication(
                FfiHandle.FromId(pub.Handle.Id),
                pub.Info,
                this
            );

            _trackPublications[publication.Sid] = publication;
        }

        internal void RemovePublication(string sid)
        {
            _trackPublications.Remove(sid);
        }
    }

    /// <summary>
    /// Options for publishing a track.
    /// </summary>
    public class TrackPublishOptions
    {
        /// <summary>
        /// Video encoding settings.
        /// </summary>
        public VideoEncodingOptions? VideoEncoding { get; set; }

        /// <summary>
        /// Audio encoding settings.
        /// </summary>
        public AudioEncodingOptions? AudioEncoding { get; set; }

        /// <summary>
        /// Whether to enable simulcast for video tracks.
        /// </summary>
        public bool Simulcast { get; set; } = true;

        /// <summary>
        /// Track source.
        /// </summary>
        public Proto.TrackSource Source { get; set; } = Proto.TrackSource.SourceUnknown;
    }

    /// <summary>
    /// Video encoding options.
    /// </summary>
    public class VideoEncodingOptions
    {
        /// <summary>
        /// Maximum bitrate in bits per second.
        /// </summary>
        public uint MaxBitrate { get; set; }

        /// <summary>
        /// Maximum framerate.
        /// </summary>
        public uint MaxFramerate { get; set; }
    }

    /// <summary>
    /// Audio encoding options.
    /// </summary>
    public class AudioEncodingOptions
    {
        /// <summary>
        /// Maximum bitrate in bits per second.
        /// </summary>
        public uint MaxBitrate { get; set; }
    }

    /// <summary>
    /// Options for publishing data.
    /// </summary>
    public class DataPublishOptions
    {
        /// <summary>
        /// Whether to send the data reliably.
        /// </summary>
        public bool Reliable { get; set; } = true;

        /// <summary>
        /// Topic for the data message.
        /// </summary>
        public string? Topic { get; set; }

        /// <summary>
        /// Destination participant identities. If empty, sends to all.
        /// </summary>
        public string[]? DestinationIdentities { get; set; }
    }
}
