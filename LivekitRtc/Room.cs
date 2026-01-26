// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Room options for connecting to a LiveKit room.
    /// </summary>
    public class RoomOptions
    {
        /// <summary>
        /// Automatically subscribe to tracks when they are published.
        /// </summary>
        public bool AutoSubscribe { get; set; } = true;

        /// <summary>
        /// Enable dynacast for adaptive streaming.
        /// </summary>
        public bool Dynacast { get; set; } = false;

        /// <summary>
        /// Enable adaptive stream quality.
        /// </summary>
        public bool AdaptiveStream { get; set; } = false;

        /// <summary>
        /// Number of connection retry attempts.
        /// </summary>
        public uint JoinRetries { get; set; } = 3;

        /// <summary>
        /// E2EE options (optional).
        /// </summary>
        public E2EEOptions? E2EE { get; set; }

        internal Proto.RoomOptions ToProto()
        {
            return new Proto.RoomOptions
            {
                AutoSubscribe = AutoSubscribe,
                Dynacast = Dynacast,
                AdaptiveStream = AdaptiveStream,
                JoinRetries = JoinRetries,
            };
        }
    }

    /// <summary>
    /// Represents a LiveKit room for real-time communication.
    /// </summary>
    public class Room : IDisposable
    {
        private FfiHandle? _roomHandle;
        private readonly Dictionary<string, RemoteParticipant> _remoteParticipants;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _ffiEventLock = new SemaphoreSlim(1, 1);
        private bool _disposed;
        private E2EEManager? _e2eeManager;

        // Serial Task Queue for event dispatching to ensure ordering and prevent deadlocks
        private Task _eventTaskChain = Task.CompletedTask;
        private readonly object _eventLock = new object();

        // Stream handling
        private readonly Dictionary<string, TextStreamReader> _textStreamReaders =
            new Dictionary<string, TextStreamReader>();
        private readonly Dictionary<string, ByteStreamReader> _byteStreamReaders =
            new Dictionary<string, ByteStreamReader>();
        private readonly StreamHandlerRegistry _streamHandlers = new StreamHandlerRegistry();

        /// <summary>
        /// Room session ID (assigned by the server).
        /// </summary>
        public string? Sid { get; private set; }

        /// <summary>
        /// Internal FFI event lock for synchronization.
        /// </summary>
        internal SemaphoreSlim FfiEventLock => _ffiEventLock;

        /// <summary>
        /// Room name.
        /// </summary>
        public string? Name { get; private set; }

        /// <summary>
        /// Room metadata.
        /// </summary>
        public string? Metadata { get; private set; }

        /// <summary>
        /// Number of participants in the room.
        /// </summary>
        public uint NumParticipants { get; private set; }

        /// <summary>
        /// Number of publishers in the room.
        /// </summary>
        public uint NumPublishers { get; private set; }

        /// <summary>
        /// Whether the room has an active recording.
        /// </summary>
        public bool ActiveRecording { get; private set; }

        /// <summary>
        /// The time when the room was created.
        /// </summary>
        public DateTime CreationTime { get; private set; }

        /// <summary>
        /// The time in seconds after which a room will be closed after the last participant has disconnected.
        /// </summary>
        public double DepartureTimeout { get; private set; }

        /// <summary>
        /// The time in seconds after which an empty room will be automatically closed.
        /// </summary>
        public double EmptyTimeout { get; private set; }

        /// <summary>
        /// The E2EE manager for this room. Null if E2EE is not enabled.
        /// </summary>
        public E2EEManager? E2EEManager => _e2eeManager;

        /// <summary>
        /// The local participant in this room.
        /// </summary>
        public LocalParticipant? LocalParticipant { get; private set; }

        /// <summary>
        /// Current connection state.
        /// </summary>
        public Proto.ConnectionState ConnectionState { get; private set; } =
            Proto.ConnectionState.ConnDisconnected;

        /// <summary>
        /// Whether the room is currently connected.
        /// </summary>
        public bool IsConnected =>
            _roomHandle != null && ConnectionState != Proto.ConnectionState.ConnDisconnected;

        /// <summary>
        /// Remote participants in the room (keyed by identity).
        /// </summary>
        public IReadOnlyDictionary<string, RemoteParticipant> RemoteParticipants
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<string, RemoteParticipant>(_remoteParticipants);
                }
            }
        }

        #region Events

        /// <summary>
        /// Event raised when a participant connects to the room.
        /// </summary>
        public event EventHandler<Participant>? ParticipantConnected;

        /// <summary>
        /// Event raised when a participant disconnects from the room.
        /// </summary>
        public event EventHandler<Participant>? ParticipantDisconnected;

        /// <summary>
        /// Event raised when a local track is published.
        /// </summary>
        public event EventHandler<LocalTrackPublishedEventArgs>? LocalTrackPublished;

        /// <summary>
        /// Event raised when a local track is unpublished.
        /// </summary>
        public event EventHandler<LocalTrackPublishedEventArgs>? LocalTrackUnpublished;

        /// <summary>
        /// Event raised when a remote track is published.
        /// </summary>
        public event EventHandler<TrackPublishedEventArgs>? TrackPublished;

        /// <summary>
        /// Event raised when a remote track is unpublished.
        /// </summary>
        public event EventHandler<TrackPublishedEventArgs>? TrackUnpublished;

        /// <summary>
        /// Event raised when a remote track is subscribed.
        /// </summary>
        public event EventHandler<TrackSubscribedEventArgs>? TrackSubscribed;

        /// <summary>
        /// Event raised when a remote track is unsubscribed.
        /// </summary>
        public event EventHandler<TrackSubscribedEventArgs>? TrackUnsubscribed;

        /// <summary>
        /// Event raised when a track is muted.
        /// </summary>
        public event EventHandler<TrackMutedEventArgs>? TrackMuted;

        /// <summary>
        /// Event raised when a track is unmuted.
        /// </summary>
        public event EventHandler<TrackMutedEventArgs>? TrackUnmuted;

        /// <summary>
        /// Event raised when active speakers change.
        /// </summary>
        public event EventHandler<ActiveSpeakersChangedEventArgs>? ActiveSpeakersChanged;

        /// <summary>
        /// Event raised when connection quality changes for a participant.
        /// </summary>
        public event EventHandler<ConnectionQualityChangedEventArgs>? ConnectionQualityChanged;

        /// <summary>
        /// Event raised when data is received.
        /// </summary>
        public event EventHandler<DataReceivedEventArgs>? DataReceived;

        /// <summary>
        /// Event raised when connection state changes.
        /// </summary>
        public event EventHandler<Proto.ConnectionState>? ConnectionStateChanged;

        /// <summary>
        /// Event raised when room is connected.
        /// </summary>
        public event EventHandler? Connected;

        /// <summary>
        /// Event raised when room is disconnected.
        /// </summary>
        public event EventHandler<Proto.DisconnectReason>? Disconnected;

        /// <summary>
        /// Event raised when room is reconnecting.
        /// </summary>
        public event EventHandler? Reconnecting;

        /// <summary>
        /// Event raised when room has reconnected.
        /// </summary>
        public event EventHandler? Reconnected;

        /// <summary>
        /// Event raised when room metadata changes.
        /// </summary>
        public event EventHandler<string>? RoomMetadataChanged;

        /// <summary>
        /// Event raised when a participant's metadata changes.
        /// </summary>
        public event EventHandler<Participant>? ParticipantMetadataChanged;

        /// <summary>
        /// Event raised when a participant's name changes.
        /// </summary>
        public event EventHandler<Participant>? ParticipantNameChanged;

        /// <summary>
        /// Event raised when a participant's attributes change.
        /// </summary>
        public event EventHandler<ParticipantAttributesChangedEventArgs>? ParticipantAttributesChanged;

        /// <summary>
        /// Event raised when a participant's encryption status changes.
        /// </summary>
        public event EventHandler<ParticipantEncryptionStatusChangedEventArgs>? ParticipantEncryptionStatusChanged;

        /// <summary>
        /// Event raised when a local track is subscribed by a remote participant.
        /// </summary>
        public event EventHandler<LocalTrackSubscribedEventArgs>? LocalTrackSubscribed;

        /// <summary>
        /// Event raised when track subscription fails.
        /// </summary>
        public event EventHandler<TrackSubscriptionFailedEventArgs>? TrackSubscriptionFailed;

        /// <summary>
        /// Event raised when room information is updated.
        /// </summary>
        public event EventHandler<RoomInfo>? RoomUpdated;

        /// <summary>
        /// Event raised when the room SID changes.
        /// </summary>
        public event EventHandler<string>? RoomSidChanged;

        /// <summary>
        /// Event raised when the local participant is moved to a new room.
        /// </summary>
        public event EventHandler<RoomInfo>? Moved;

        /// <summary>
        /// Event raised when a chat message is received.
        /// </summary>
        public event EventHandler<ChatMessageReceivedEventArgs>? ChatMessageReceived;

        /// <summary>
        /// Event raised when a SIP DTMF tone is received.
        /// </summary>
        public event EventHandler<SipDtmfReceivedEventArgs>? SipDtmfReceived;

        /// <summary>
        /// Event raised when an E2EE (encryption) error occurs.
        /// </summary>
        public event EventHandler<E2EEStateChangedEventArgs>? E2EEStateChanged;

        /// <summary>
        /// Event raised when a transcription is received.
        /// </summary>
        public event EventHandler<TranscriptionReceivedEventArgs>? TranscriptionReceived;

        /// <summary>
        /// Event raised when the access token is refreshed.
        /// </summary>
        public event EventHandler<string>? TokenRefreshed;

        #endregion

        /// <summary>
        /// Registers a handler for incoming text data streams on a specific topic.
        /// </summary>
        /// <param name="topic">The topic to listen for text streams on.</param>
        /// <param name="handler">Handler to process incoming text streams.</param>
        /// <exception cref="StreamException">Thrown when a handler for this topic is already registered.</exception>
        public void RegisterTextStreamHandler(string topic, TextStreamHandler handler)
        {
            _streamHandlers.RegisterTextStreamHandler(topic, handler);
        }

        /// <summary>
        /// Unregisters a text stream handler for a specific topic.
        /// </summary>
        /// <param name="topic">The topic to unregister.</param>
        public void UnregisterTextStreamHandler(string topic)
        {
            _streamHandlers.UnregisterTextStreamHandler(topic);
        }

        /// <summary>
        /// Registers a handler for incoming byte data streams on a specific topic.
        /// </summary>
        /// <param name="topic">The topic to listen for byte streams on.</param>
        /// <param name="handler">Handler to process incoming byte streams.</param>
        /// <exception cref="StreamException">Thrown when a handler for this topic is already registered.</exception>
        public void RegisterByteStreamHandler(string topic, ByteStreamHandler handler)
        {
            _streamHandlers.RegisterByteStreamHandler(topic, handler);
        }

        /// <summary>
        /// Unregisters a byte stream handler for a specific topic.
        /// </summary>
        /// <param name="topic">The topic to unregister.</param>
        public void UnregisterByteStreamHandler(string topic)
        {
            _streamHandlers.UnregisterByteStreamHandler(topic);
        }

        /// <summary>
        /// Dispatches events asynchronously while maintaining strict FIFO order.
        /// This prevents deadlocks and protects the FFI thread from user-code exceptions.
        /// </summary>
        private void DispatchEvent(Action action)
        {
            lock (_eventLock)
            {
                _eventTaskChain = _eventTaskChain
                    .ContinueWith(
                        _ =>
                        {
                            try
                            {
                                action();
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine(
                                    $"[ERROR] Room event handler exception: {ex.Message}"
                                );
                            }
                        },
                        TaskScheduler.Default
                    );
            }
        }

        // Overload to handle async delegates directly
        private void DispatchEvent(Func<Task> action)
        {
            lock (_eventLock)
            {
                _eventTaskChain = _eventTaskChain
                    .ContinueWith(
                        async _ =>
                        {
                            try
                            {
                                await action();
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine(
                                    $"[ERROR] Room event handler exception: {ex.Message}"
                                );
                            }
                        },
                        TaskScheduler.Default
                    )
                    .Unwrap();
            }
        }

        private async Task<T?> RetryUntilFound<T>(
            Func<T?> retrievalFunc,
            int maxRetries = 20,
            int delayMs = 25
        )
            where T : class
        {
            for (int i = 0; i < maxRetries; i++)
            {
                var result = retrievalFunc();
                if (result != null)
                    return result;

                await Task.Delay(delayMs);
            }

            return null;
        }

        private async Task<LocalTrackPublication> RequireLocalTrackPublication(string trackSid)
        {
            if (LocalParticipant == null)
                throw new InvalidOperationException("Local participant not available");

            var publication = await RetryUntilFound(() =>
            {
                _ffiEventLock.Wait();
                try
                {
                    return LocalParticipant.GetTrackPublication(trackSid) as LocalTrackPublication;
                }
                finally
                {
                    _ffiEventLock.Release();
                }
            });

            if (publication == null)
                throw new InvalidOperationException($"Publication {trackSid} not found");

            return publication;
        }

        private async Task<(
            RemoteParticipant participant,
            RemoteTrackPublication publication
        )> RequireRemoteTrackPublication(string participantIdentity, string trackSid)
        {
            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(participantIdentity, out participant))
                    throw new InvalidOperationException(
                        $"Participant {participantIdentity} not found"
                    );
            }

            var publication = await RetryUntilFound(() =>
                participant.GetTrackPublication(trackSid) as RemoteTrackPublication
            );

            if (publication == null)
                throw new InvalidOperationException(
                    $"Publication {trackSid} not found for participant {participantIdentity}"
                );

            return (participant, publication);
        }

        private async Task<(
            Participant participant,
            TrackPublication publication
        )> RequireTrackPublication(string participantIdentity, string trackSid)
        {
            Participant? participant = GetParticipantByIdentity(participantIdentity);
            if (participant == null)
                throw new InvalidOperationException($"Participant {participantIdentity} not found");

            var publication = await RetryUntilFound(() =>
                participant.GetTrackPublication(trackSid)
            );

            if (publication == null)
                throw new InvalidOperationException(
                    $"Publication {trackSid} not found for participant {participantIdentity}"
                );

            return (participant, publication);
        }

        /// <summary>
        /// Creates a new Room instance.
        /// </summary>
        public Room()
        {
            _remoteParticipants = new Dictionary<string, RemoteParticipant>();

            // Subscribe to FFI events
            FfiClient.Instance.EventReceived += OnFfiEvent;
        }

        /// <summary>
        /// Connects to a LiveKit room.
        /// </summary>
        /// <param name="url">The LiveKit server URL (e.g., wss://your-server.livekit.cloud).</param>
        /// <param name="token">The access token for authentication.</param>
        /// <param name="options">Optional room options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the connection is established.</returns>
        public async Task ConnectAsync(
            string url,
            string token,
            RoomOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Room));

            if (IsConnected)
                throw new InvalidOperationException("Room is already connected");

            options ??= new RoomOptions();

            // Proto only has 3 states: ConnDisconnected, ConnConnected, ConnReconnecting
            // Application-level "connecting" is handled by being != ConnDisconnected
            ConnectionState = Proto.ConnectionState.ConnDisconnected;
            DispatchEvent(() => ConnectionStateChanged?.Invoke(this, ConnectionState));

            // Send connect request
            var request = new FfiRequest
            {
                Connect = new ConnectRequest
                {
                    Url = url,
                    Token = token,
                    Options = options.ToProto(),
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.Connect == null)
                throw new InvalidOperationException("Invalid connect response");

            var asyncId = response.Connect.AsyncId;

            // Wait for connect callback
            var connectEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.Connect
                    && e.Connect?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            var callback = connectEvent.Connect!;

            if (!string.IsNullOrEmpty(callback.Error))
            {
                ConnectionState = Proto.ConnectionState.ConnDisconnected;
                DispatchEvent(() => ConnectionStateChanged?.Invoke(this, ConnectionState));
                throw new RoomException($"Failed to connect: {callback.Error}");
            }

            // Get the result from the callback
            var result = callback.Result;
            if (result == null)
            {
                throw new RoomException("Connect callback has no result");
            }

            // Store room handle and info
            if (result.Room?.Handle != null)
            {
                _roomHandle = FfiHandle.FromId(result.Room.Handle.Id);
                UpdateFromInfo(result.Room.Info);

                // Initialize E2EE manager if E2EE options were provided
                if (options.E2EE != null)
                {
                    _e2eeManager = new E2EEManager(_roomHandle.HandleId, options.E2EE);
                }
            }

            // Create local participant
            if (result.LocalParticipant?.Handle != null)
            {
                LocalParticipant = new LocalParticipant(
                    FfiHandle.FromId(result.LocalParticipant.Handle.Id),
                    result.LocalParticipant.Info,
                    this
                );
            }

            // Create remote participants
            foreach (var participantWithTracks in result.Participants)
            {
                if (participantWithTracks.Participant?.Info != null)
                {
                    var remoteParticipant = new RemoteParticipant(
                        FfiHandle.FromId(participantWithTracks.Participant.Handle!.Id),
                        participantWithTracks.Participant.Info,
                        this
                    );

                    // Add publications
                    foreach (var pub in participantWithTracks.Publications)
                    {
                        if (pub.Handle != null && pub.Info != null)
                        {
                            remoteParticipant.AddPublication(pub);
                        }
                    }

                    lock (_lock)
                    {
                        _remoteParticipants[remoteParticipant.Identity] = remoteParticipant;
                    }
                }
            }

            ConnectionState = Proto.ConnectionState.ConnConnected;
            DispatchEvent(() => ConnectionStateChanged?.Invoke(this, ConnectionState));
            DispatchEvent(() => Connected?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>
        /// Disconnects from the room.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_roomHandle == null)
                return;

            var request = new FfiRequest
            {
                Disconnect = new DisconnectRequest { RoomHandle = _roomHandle.HandleId },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.Disconnect == null)
                throw new InvalidOperationException("Invalid disconnect response");

            var asyncId = response.Disconnect.AsyncId;

            // Wait for disconnect callback
            await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.Disconnect
                    && e.Disconnect?.AsyncId == asyncId,
                TimeSpan.FromSeconds(10)
            );

            CleanupOnDisconnect();
        }

        /// <summary>
        /// Gets RTC statistics for the current session.
        /// </summary>
        /// <returns>RTC statistics containing publisher and subscriber stats.</returns>
        /// <exception cref="InvalidOperationException">Thrown when not connected to a room.</exception>
        public async Task<RtcStats> GetRtcStatsAsync()
        {
            if (!IsConnected || _roomHandle == null)
                throw new InvalidOperationException("Not connected to a room");

            var request = new FfiRequest
            {
                GetSessionStats = new GetSessionStatsRequest { RoomHandle = _roomHandle.HandleId },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.GetSessionStats == null)
                throw new InvalidOperationException("Invalid get session stats response");

            var asyncId = response.GetSessionStats.AsyncId;

            // Wait for stats callback
            var statsEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.GetSessionStats
                    && e.GetSessionStats?.AsyncId == asyncId,
                TimeSpan.FromSeconds(10)
            );

            var callback = statsEvent.GetSessionStats!;

            if (!string.IsNullOrEmpty(callback.Error))
            {
                throw new RoomException($"Failed to get RTC stats: {callback.Error}");
            }

            var result = callback.Result;
            if (result == null)
            {
                throw new RoomException("Get session stats callback has no result");
            }

            return new RtcStats
            {
                PublisherStats = new List<Proto.RtcStats>(result.PublisherStats),
                SubscriberStats = new List<Proto.RtcStats>(result.SubscriberStats),
            };
        }

        /// <summary>
        /// Disconnects from the room synchronously.
        /// </summary>
        public void Disconnect()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }

        private void UpdateFromInfo(RoomInfo? info)
        {
            if (info == null)
                return;

            Sid = info.Sid;
            Name = info.Name;
            Metadata = info.Metadata;
            NumParticipants = info.NumParticipants;
            NumPublishers = info.NumPublishers;
            ActiveRecording = info.ActiveRecording;

            // Convert creation time from nanoseconds to DateTime
            // Note: Rust SDK may send seconds or ms, we handle both cases
            var creationTimeValue = info.CreationTime;
            if (creationTimeValue > 0)
            {
                // If value looks like seconds (less than year 3000 in ms), convert to ms
                if (creationTimeValue < 1_000_000_000_000L)
                {
                    creationTimeValue *= 1000;
                }
                CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(creationTimeValue).DateTime;
            }
            else
            {
                CreationTime = DateTime.MinValue;
            }

            DepartureTimeout = info.DepartureTimeout;
            EmptyTimeout = info.EmptyTimeout;
        }

        private void OnFfiEvent(object? sender, FfiEvent e)
        {
            if (e.MessageCase == FfiEvent.MessageOneofCase.RoomEvent)
            {
                var roomEvent = e.RoomEvent;
                if (roomEvent == null || _roomHandle == null)
                    return;

                // Check if this event is for this room
                if (roomEvent.RoomHandle != _roomHandle.HandleId)
                    return;

                HandleRoomEvent(roomEvent);
            }
            else if (e.MessageCase == FfiEvent.MessageOneofCase.RpcMethodInvocation)
            {
                var invocation = e.RpcMethodInvocation;
                if (invocation == null || LocalParticipant == null)
                    return;

                // Check if this event is for our local participant
                if (invocation.LocalParticipantHandle != LocalParticipant.Handle.HandleId)
                    return;

                // Fire and forget immediately. Do NOT use DispatchEvent.
                // RPCs are independent request/response cycles and should not be blocked by the event queue.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LocalParticipant.HandleRpcMethodInvocationAsync(
                            invocation.InvocationId,
                            invocation.Method,
                            invocation.RequestId,
                            invocation.CallerIdentity,
                            invocation.Payload,
                            invocation.ResponseTimeoutMs
                        );
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't crash
                        Console.Error.WriteLine($"Error handling RPC invocation: {ex.Message}");
                    }
                });
            }
        }

        private void HandleRoomEvent(RoomEvent roomEvent)
        {
            switch (roomEvent.MessageCase)
            {
                case RoomEvent.MessageOneofCase.ParticipantConnected:
                    HandleParticipantConnected(roomEvent.ParticipantConnected);
                    break;

                case RoomEvent.MessageOneofCase.ParticipantDisconnected:
                    HandleParticipantDisconnected(roomEvent.ParticipantDisconnected);
                    break;

                case RoomEvent.MessageOneofCase.LocalTrackPublished:
                    HandleLocalTrackPublished(roomEvent.LocalTrackPublished);
                    break;

                case RoomEvent.MessageOneofCase.LocalTrackUnpublished:
                    HandleLocalTrackUnpublished(roomEvent.LocalTrackUnpublished);
                    break;

                case RoomEvent.MessageOneofCase.LocalTrackSubscribed:
                    HandleLocalTrackSubscribed(roomEvent.LocalTrackSubscribed);
                    break;

                case RoomEvent.MessageOneofCase.TrackPublished:
                    HandleTrackPublished(roomEvent.TrackPublished);
                    break;

                case RoomEvent.MessageOneofCase.TrackUnpublished:
                    HandleTrackUnpublished(roomEvent.TrackUnpublished);
                    break;

                case RoomEvent.MessageOneofCase.TrackSubscribed:
                    HandleTrackSubscribed(roomEvent.TrackSubscribed);
                    break;

                case RoomEvent.MessageOneofCase.TrackUnsubscribed:
                    HandleTrackUnsubscribed(roomEvent.TrackUnsubscribed);
                    break;

                case RoomEvent.MessageOneofCase.TrackMuted:
                    HandleTrackMuted(roomEvent.TrackMuted);
                    break;

                case RoomEvent.MessageOneofCase.TrackUnmuted:
                    HandleTrackUnmuted(roomEvent.TrackUnmuted);
                    break;

                case RoomEvent.MessageOneofCase.ActiveSpeakersChanged:
                    HandleActiveSpeakersChanged(roomEvent.ActiveSpeakersChanged);
                    break;

                case RoomEvent.MessageOneofCase.ConnectionQualityChanged:
                    HandleConnectionQualityChanged(roomEvent.ConnectionQualityChanged);
                    break;

                case RoomEvent.MessageOneofCase.DataPacketReceived:
                    HandleDataPacketReceived(roomEvent.DataPacketReceived);
                    break;

                case RoomEvent.MessageOneofCase.RoomMetadataChanged:
                    HandleRoomMetadataChanged(roomEvent.RoomMetadataChanged);
                    break;

                case RoomEvent.MessageOneofCase.ParticipantMetadataChanged:
                    HandleParticipantMetadataChanged(roomEvent.ParticipantMetadataChanged);
                    break;

                case RoomEvent.MessageOneofCase.ParticipantNameChanged:
                    HandleParticipantNameChanged(roomEvent.ParticipantNameChanged);
                    break;

                case RoomEvent.MessageOneofCase.ParticipantAttributesChanged:
                    HandleParticipantAttributesChanged(roomEvent.ParticipantAttributesChanged);
                    break;

                case RoomEvent.MessageOneofCase.ParticipantEncryptionStatusChanged:
                    HandleParticipantEncryptionStatusChanged(
                        roomEvent.ParticipantEncryptionStatusChanged
                    );
                    break;

                case RoomEvent.MessageOneofCase.TrackSubscriptionFailed:
                    HandleTrackSubscriptionFailed(roomEvent.TrackSubscriptionFailed);
                    break;

                case RoomEvent.MessageOneofCase.RoomUpdated:
                    HandleRoomUpdated(roomEvent.RoomUpdated);
                    break;

                case RoomEvent.MessageOneofCase.RoomSidChanged:
                    HandleRoomSidChanged(roomEvent.RoomSidChanged);
                    break;

                case RoomEvent.MessageOneofCase.Moved:
                    HandleMoved(roomEvent.Moved);
                    break;

                case RoomEvent.MessageOneofCase.ChatMessage:
                    HandleChatMessage(roomEvent.ChatMessage);
                    break;

                case RoomEvent.MessageOneofCase.E2EeStateChanged:
                    HandleE2EEStateChanged(roomEvent.E2EeStateChanged);
                    break;

                case RoomEvent.MessageOneofCase.TranscriptionReceived:
                    HandleTranscriptionReceived(roomEvent.TranscriptionReceived);
                    break;

                case RoomEvent.MessageOneofCase.TokenRefreshed:
                    HandleTokenRefreshed(roomEvent.TokenRefreshed);
                    break;

                case RoomEvent.MessageOneofCase.StreamHeaderReceived:
                    if (
                        roomEvent.StreamHeaderReceived?.Header != null
                        && !string.IsNullOrEmpty(roomEvent.StreamHeaderReceived.ParticipantIdentity)
                    )
                    {
                        HandleStreamHeader(
                            roomEvent.StreamHeaderReceived.Header,
                            roomEvent.StreamHeaderReceived.ParticipantIdentity
                        );
                    }
                    break;

                case RoomEvent.MessageOneofCase.StreamChunkReceived:
                    if (roomEvent.StreamChunkReceived?.Chunk != null)
                    {
                        HandleStreamChunk(roomEvent.StreamChunkReceived.Chunk);
                    }
                    break;

                case RoomEvent.MessageOneofCase.StreamTrailerReceived:
                    if (roomEvent.StreamTrailerReceived?.Trailer != null)
                    {
                        HandleStreamTrailer(roomEvent.StreamTrailerReceived.Trailer);
                    }
                    break;

                case RoomEvent.MessageOneofCase.Disconnected:
                    CleanupOnDisconnect();
                    break;

                case RoomEvent.MessageOneofCase.Reconnecting:
                    ConnectionState = Proto.ConnectionState.ConnReconnecting;
                    DispatchEvent(() => ConnectionStateChanged?.Invoke(this, ConnectionState));
                    DispatchEvent(() => Reconnecting?.Invoke(this, EventArgs.Empty));
                    break;

                case RoomEvent.MessageOneofCase.Reconnected:
                    ConnectionState = Proto.ConnectionState.ConnConnected;
                    DispatchEvent(() => ConnectionStateChanged?.Invoke(this, ConnectionState));
                    DispatchEvent(() => Reconnected?.Invoke(this, EventArgs.Empty));
                    break;
            }
        }

        private void HandleParticipantConnected(Proto.ParticipantConnected evt)
        {
            if (evt?.Info?.Info == null)
                return;

            var participant = new RemoteParticipant(
                FfiHandle.FromId(evt.Info.Handle?.Id ?? 0),
                evt.Info.Info,
                this
            );

            lock (_lock)
            {
                _remoteParticipants[participant.Identity] = participant;
            }

            DispatchEvent(() => ParticipantConnected?.Invoke(this, participant));
        }

        private void HandleParticipantDisconnected(Proto.ParticipantDisconnected evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                    return;

                _remoteParticipants.Remove(evt.ParticipantIdentity);
            }

            DispatchEvent(() => ParticipantDisconnected?.Invoke(this, participant));
        }

        private void HandleLocalTrackPublished(Proto.LocalTrackPublished evt)
        {
            if (LocalParticipant == null || string.IsNullOrEmpty(evt?.TrackSid))
                return;

            DispatchEvent(async () =>
            {
                try
                {
                    var publication = await RequireLocalTrackPublication(evt.TrackSid);
                    LocalTrackPublished?.Invoke(
                        this,
                        new LocalTrackPublishedEventArgs(publication, LocalParticipant)
                    );
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] RoomEvent.LocalTrackPublished: {ex.Message}");
                }
            });
        }

        private void HandleLocalTrackUnpublished(Proto.LocalTrackUnpublished evt)
        {
            if (LocalParticipant == null || string.IsNullOrEmpty(evt?.PublicationSid))
                return;

            DispatchEvent(async () =>
            {
                try
                {
                    var publication = await RequireLocalTrackPublication(evt.PublicationSid);
                    LocalTrackUnpublished?.Invoke(
                        this,
                        new LocalTrackPublishedEventArgs(publication, LocalParticipant)
                    );
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[WARN] RoomEvent.LocalTrackUnpublished: {ex.Message}"
                    );
                }
            });
        }

        private void HandleLocalTrackSubscribed(Proto.LocalTrackSubscribed evt)
        {
            if (LocalParticipant == null || string.IsNullOrEmpty(evt?.TrackSid))
                return;

            DispatchEvent(async () =>
            {
                try
                {
                    var publication = await RequireLocalTrackPublication(evt.TrackSid);
                    publication.ResolveFirstSubscription();
                    LocalTrackSubscribed?.Invoke(
                        this,
                        new LocalTrackSubscribedEventArgs(publication)
                    );
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] RoomEvent.LocalTrackSubscribed: {ex.Message}");
                }
            });
        }

        private void HandleTrackPublished(Proto.TrackPublished evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity) || evt.Publication == null)
                return;

            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                    return;
            }

            // Add publication to participant
            participant.AddPublication(evt.Publication);

            DispatchEvent(async () =>
            {
                try
                {
                    // Retry logic: The FFI event might arrive before AddPublication
                    // has finished updating the internal dictionary.
                    var publication = await RetryUntilFound(() =>
                        participant.GetTrackPublication(evt.Publication.Info?.Sid ?? "")
                        as RemoteTrackPublication
                    );

                    if (publication == null)
                        throw new InvalidOperationException(
                            $"Publication {evt.Publication.Info?.Sid} not found after AddPublication"
                        );

                    TrackPublished?.Invoke(
                        this,
                        new TrackPublishedEventArgs(publication, participant)
                    );
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] RoomEvent.TrackPublished: {ex.Message}");
                }
            });
        }

        private void HandleTrackUnpublished(Proto.TrackUnpublished evt)
        {
            if (
                string.IsNullOrEmpty(evt?.ParticipantIdentity)
                || string.IsNullOrEmpty(evt.PublicationSid)
            )
                return;

            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                    return;
            }

            var publication =
                participant.GetTrackPublication(evt.PublicationSid) as RemoteTrackPublication;
            if (publication != null)
            {
                DispatchEvent(() =>
                    TrackUnpublished?.Invoke(
                        this,
                        new TrackPublishedEventArgs(publication, participant)
                    )
                );
                participant.RemovePublication(evt.PublicationSid);
            }
        }

        private void HandleTrackSubscribed(Proto.TrackSubscribed evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity) || evt.Track == null)
                return;

            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                    return;
            }

            // Create the remote track
            var trackInfo = evt.Track.Info;
            if (trackInfo == null)
                return;

            var handle = FfiHandle.FromId(evt.Track.Handle?.Id ?? 0);
            RemoteTrack? track = null;

            if (trackInfo.Kind == Proto.TrackKind.KindAudio)
            {
                track = new RemoteAudioTrack(handle);
            }
            else if (trackInfo.Kind == Proto.TrackKind.KindVideo)
            {
                track = new RemoteVideoTrack(handle);
            }

            if (track != null)
            {
                DispatchEvent(async () =>
                {
                    try
                    {
                        var (participant, publication) = await RequireRemoteTrackPublication(
                            evt.ParticipantIdentity,
                            trackInfo.Sid ?? ""
                        );

                        publication.Track = track;
                        TrackSubscribed?.Invoke(
                            this,
                            new TrackSubscribedEventArgs(track, publication, participant)
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARN] RoomEvent.TrackSubscribed: {ex.Message}");
                    }
                });
            }
        }

        private void HandleTrackUnsubscribed(Proto.TrackUnsubscribed evt)
        {
            if (
                string.IsNullOrEmpty(evt?.ParticipantIdentity) || string.IsNullOrEmpty(evt.TrackSid)
            )
                return;

            DispatchEvent(async () =>
            {
                try
                {
                    var (participant, publication) = await RequireRemoteTrackPublication(
                        evt.ParticipantIdentity,
                        evt.TrackSid
                    );

                    if (publication.Track == null)
                        throw new InvalidOperationException(
                            $"Track {evt.TrackSid} already unsubscribed"
                        );

                    var track = publication.Track;
                    TrackUnsubscribed?.Invoke(
                        this,
                        new TrackSubscribedEventArgs(track, publication, participant)
                    );
                    publication.Track = null;
                }
                catch (InvalidOperationException ex)
                {
                    // Silently ignore "not found for participant" errors during cleanup/disconnect
                    // The participant or publication may have already been removed
                    if (!ex.Message.Contains("not found for participant"))
                    {
                        Console.Error.WriteLine(
                            $"[WARN] RoomEvent.TrackUnsubscribed: {ex.Message}"
                        );
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] RoomEvent.TrackUnsubscribed: {ex.Message}");
                }
            });
        }

        private void HandleTrackMuted(Proto.TrackMuted evt)
        {
            if (
                string.IsNullOrEmpty(evt?.ParticipantIdentity) || string.IsNullOrEmpty(evt.TrackSid)
            )
                return;

            DispatchEvent(async () =>
            {
                try
                {
                    var (participant, publication) = await RequireTrackPublication(
                        evt.ParticipantIdentity,
                        evt.TrackSid
                    );

                    var updatedInfo = publication.Info;
                    updatedInfo.Muted = true;
                    publication.UpdateInfo(updatedInfo);

                    TrackMuted?.Invoke(this, new TrackMutedEventArgs(publication, participant));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] RoomEvent.TrackMuted: {ex.Message}");
                }
            });
        }

        private void HandleTrackUnmuted(Proto.TrackUnmuted evt)
        {
            if (
                string.IsNullOrEmpty(evt?.ParticipantIdentity) || string.IsNullOrEmpty(evt.TrackSid)
            )
                return;

            DispatchEvent(async () =>
            {
                try
                {
                    var (participant, publication) = await RequireTrackPublication(
                        evt.ParticipantIdentity,
                        evt.TrackSid
                    );

                    var updatedInfo = publication.Info;
                    updatedInfo.Muted = false;
                    publication.UpdateInfo(updatedInfo);

                    TrackUnmuted?.Invoke(this, new TrackMutedEventArgs(publication, participant));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] RoomEvent.TrackUnmuted: {ex.Message}");
                }
            });
        }

        private void HandleActiveSpeakersChanged(Proto.ActiveSpeakersChanged evt)
        {
            if (evt?.ParticipantIdentities == null)
                return;

            var speakers = new List<Participant>();
            foreach (var identity in evt.ParticipantIdentities)
            {
                var participant = GetParticipantByIdentity(identity ?? "");
                if (participant != null)
                {
                    speakers.Add(participant);
                }
            }

            DispatchEvent(() =>
                ActiveSpeakersChanged?.Invoke(this, new ActiveSpeakersChangedEventArgs(speakers))
            );
        }

        private void HandleConnectionQualityChanged(Proto.ConnectionQualityChanged evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity);
            if (participant == null)
                return;

            var quality = evt.Quality;
            DispatchEvent(() =>
                ConnectionQualityChanged?.Invoke(
                    this,
                    new ConnectionQualityChangedEventArgs(quality, participant)
                )
            );
        }

        private void HandleDataPacketReceived(Proto.DataPacketReceived evt)
        {
            if (evt == null)
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity ?? "");
            var kind = evt.Kind;
            byte[] data = Array.Empty<byte>();
            string topic = string.Empty;

            if (evt.ValueCase == Proto.DataPacketReceived.ValueOneofCase.User && evt.User != null)
            {
                topic = evt.User.Topic ?? string.Empty;

                // Copy data from native memory
                if (evt.User.Data?.Data != null)
                {
                    var bufferInfo = evt.User.Data.Data;
                    data = new byte[bufferInfo.DataLen];
                    unsafe
                    {
                        var ptr = (byte*)bufferInfo.DataPtr;
                        for (int i = 0; i < (int)bufferInfo.DataLen; i++)
                        {
                            data[i] = ptr[i];
                        }
                    }

                    // Dispose the FFI handle
                    if (evt.User.Data.Handle != null)
                    {
                        using var handle = FfiHandle.FromId(evt.User.Data.Handle.Id);
                    }
                }

                DispatchEvent(() =>
                    DataReceived?.Invoke(
                        this,
                        new DataReceivedEventArgs(data, participant, kind, topic)
                    )
                );
            }
            else if (
                evt.ValueCase == Proto.DataPacketReceived.ValueOneofCase.SipDtmf
                && evt.SipDtmf != null
            )
            {
                // Handle SIP DTMF
                if (participant is RemoteParticipant remoteParticipant)
                {
                    DispatchEvent(() =>
                        SipDtmfReceived?.Invoke(
                            this,
                            new SipDtmfReceivedEventArgs(
                                evt.SipDtmf.Code,
                                evt.SipDtmf.Digit,
                                remoteParticipant
                            )
                        )
                    );
                }
            }
        }

        private void HandleRoomMetadataChanged(Proto.RoomMetadataChanged evt)
        {
            if (evt == null)
                return;

            Metadata = evt.Metadata ?? string.Empty;
            DispatchEvent(() => RoomMetadataChanged?.Invoke(this, Metadata));
        }

        private void HandleParticipantMetadataChanged(Proto.ParticipantMetadataChanged evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity);
            if (participant != null)
            {
                var updatedInfo = participant._info;
                updatedInfo.Metadata = evt.Metadata ?? string.Empty;
                participant.UpdateInfo(updatedInfo);

                DispatchEvent(() => ParticipantMetadataChanged?.Invoke(this, participant));
            }
        }

        private void HandleParticipantNameChanged(Proto.ParticipantNameChanged evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity);
            if (participant != null)
            {
                var updatedInfo = participant._info;
                updatedInfo.Name = evt.Name ?? string.Empty;
                participant.UpdateInfo(updatedInfo);

                DispatchEvent(() => ParticipantNameChanged?.Invoke(this, participant));
            }
        }

        private void HandleParticipantAttributesChanged(Proto.ParticipantAttributesChanged evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity);
            if (participant != null)
            {
                var attributes = new Dictionary<string, string>();
                foreach (var attr in evt.Attributes)
                {
                    attributes[attr.Key] = attr.Value;
                }

                var changedAttributes = new Dictionary<string, string>();
                foreach (var attr in evt.ChangedAttributes)
                {
                    changedAttributes[attr.Key] = attr.Value;
                }

                DispatchEvent(() =>
                    ParticipantAttributesChanged?.Invoke(
                        this,
                        new ParticipantAttributesChangedEventArgs(
                            participant,
                            attributes,
                            changedAttributes
                        )
                    )
                );
            }
        }

        private void HandleParticipantEncryptionStatusChanged(
            Proto.ParticipantEncryptionStatusChanged evt
        )
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity);
            if (participant != null)
            {
                DispatchEvent(() =>
                    ParticipantEncryptionStatusChanged?.Invoke(
                        this,
                        new ParticipantEncryptionStatusChangedEventArgs(
                            participant,
                            evt.IsEncrypted
                        )
                    )
                );
            }
        }

        private void HandleTrackSubscriptionFailed(Proto.TrackSubscriptionFailed evt)
        {
            if (
                string.IsNullOrEmpty(evt?.ParticipantIdentity) || string.IsNullOrEmpty(evt.TrackSid)
            )
                return;

            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                    return;
            }

            DispatchEvent(() =>
                TrackSubscriptionFailed?.Invoke(
                    this,
                    new TrackSubscriptionFailedEventArgs(evt.TrackSid, participant, evt.Error)
                )
            );
        }

        private void HandleRoomUpdated(Proto.RoomInfo evt)
        {
            if (evt == null)
                return;

            UpdateFromInfo(evt);
            DispatchEvent(() => RoomUpdated?.Invoke(this, evt));
        }

        private void HandleRoomSidChanged(Proto.RoomSidChanged evt)
        {
            if (string.IsNullOrEmpty(evt?.Sid))
                return;

            Sid = evt.Sid;
            DispatchEvent(() => RoomSidChanged?.Invoke(this, evt.Sid));
        }

        private void HandleMoved(Proto.RoomInfo evt)
        {
            if (evt == null)
                return;

            UpdateFromInfo(evt);
            DispatchEvent(() => Moved?.Invoke(this, evt));
        }

        private void HandleChatMessage(Proto.ChatMessageReceived evt)
        {
            if (evt?.Message == null)
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity ?? "");

            DispatchEvent(() =>
                ChatMessageReceived?.Invoke(
                    this,
                    new ChatMessageReceivedEventArgs(evt.Message, participant)
                )
            );
        }

        private void HandleE2EEStateChanged(Proto.E2eeStateChanged evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity);
            if (participant != null)
            {
                DispatchEvent(() =>
                    E2EEStateChanged?.Invoke(
                        this,
                        new E2EEStateChangedEventArgs(participant, evt.State)
                    )
                );
            }
        }

        private void HandleTranscriptionReceived(Proto.TranscriptionReceived evt)
        {
            if (evt == null)
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity ?? "");
            var segments = new List<Proto.TranscriptionSegment>(evt.Segments);

            DispatchEvent(() =>
                TranscriptionReceived?.Invoke(
                    this,
                    new TranscriptionReceivedEventArgs(participant, evt.TrackSid, segments)
                )
            );
        }

        private void HandleTokenRefreshed(Proto.TokenRefreshed evt)
        {
            if (string.IsNullOrEmpty(evt?.Token))
                return;

            DispatchEvent(() => TokenRefreshed?.Invoke(this, evt.Token));
        }

        private Participant? GetParticipantByIdentity(string identity)
        {
            if (string.IsNullOrEmpty(identity))
                return null;

            // Check if it's the local participant
            if (LocalParticipant?.Identity == identity)
                return LocalParticipant;

            // Check remote participants
            lock (_lock)
            {
                return _remoteParticipants.TryGetValue(identity, out var participant)
                    ? participant
                    : null;
            }
        }

        private void HandleStreamHeader(DataStream.Types.Header header, string participantIdentity)
        {
            if (header.TextHeader != null)
            {
                // Text stream
                var reader = new TextStreamReader(header);
                lock (_lock)
                {
                    _textStreamReaders[header.StreamId] = reader;
                }
                _streamHandlers.Dispatch(reader, participantIdentity);
            }
            else if (header.ByteHeader != null)
            {
                // Byte stream
                var reader = new ByteStreamReader(header);
                lock (_lock)
                {
                    _byteStreamReaders[header.StreamId] = reader;
                }
                _streamHandlers.Dispatch(reader, participantIdentity);
            }
        }

        private void HandleStreamChunk(DataStream.Types.Chunk chunk)
        {
            lock (_lock)
            {
                // Try to find text stream reader
                if (_textStreamReaders.TryGetValue(chunk.StreamId, out var textReader))
                {
                    textReader.OnChunk(chunk);
                    return;
                }

                // Try to find byte stream reader
                if (_byteStreamReaders.TryGetValue(chunk.StreamId, out var byteReader))
                {
                    byteReader.OnChunk(chunk);
                }
            }
        }

        private void HandleStreamTrailer(DataStream.Types.Trailer trailer)
        {
            lock (_lock)
            {
                // Try to find and close text stream reader
                if (_textStreamReaders.TryGetValue(trailer.StreamId, out var textReader))
                {
                    textReader.OnClose(trailer);
                    _textStreamReaders.Remove(trailer.StreamId);
                    return;
                }

                // Try to find and close byte stream reader
                if (_byteStreamReaders.TryGetValue(trailer.StreamId, out var byteReader))
                {
                    byteReader.OnClose(trailer);
                    _byteStreamReaders.Remove(trailer.StreamId);
                }
            }
        }

        private void CleanupOnDisconnect()
        {
            ConnectionState = Proto.ConnectionState.ConnDisconnected;
            DispatchEvent(() => ConnectionStateChanged?.Invoke(this, ConnectionState));
            DispatchEvent(() => Disconnected?.Invoke(this, Proto.DisconnectReason.UnknownReason));

            lock (_lock)
            {
                _remoteParticipants.Clear();
                _textStreamReaders.Clear();
                _byteStreamReaders.Clear();
            }

            _roomHandle?.Dispose();
            _roomHandle = null;
            LocalParticipant = null;
        }

        /// <summary>
        /// Disposes the room and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            FfiClient.Instance.EventReceived -= OnFfiEvent;

            if (IsConnected)
            {
                try
                {
                    Disconnect();
                }
                catch
                {
                    // Ignore errors during disposal
                }
            }

            _roomHandle?.Dispose();
        }
    }

    /// <summary>
    /// RTC statistics containing publisher and subscriber stats.
    /// </summary>
    public class RtcStats
    {
        /// <summary>
        /// Publisher statistics.
        /// </summary>
        public List<Proto.RtcStats> PublisherStats { get; set; } = new List<Proto.RtcStats>();

        /// <summary>
        /// Subscriber statistics.
        /// </summary>
        public List<Proto.RtcStats> SubscriberStats { get; set; } = new List<Proto.RtcStats>();
    }
}
