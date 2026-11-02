// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        /// Whether to subscribe automatically to every track the remote participants publish. Defaults to
        /// true.
        /// </summary>
        public bool AutoSubscribe { get; set; } = true;

        /// <summary>
        /// Whether to enable dynacast, which pauses the video layers no subscriber receives. Defaults to
        /// false.
        /// </summary>
        public bool Dynacast { get; set; } = false;

        /// <summary>
        /// Whether to enable adaptive stream, which adapts the quality of subscribed video to how it is
        /// consumed. Defaults to false.
        /// </summary>
        public bool AdaptiveStream { get; set; } = false;

        /// <summary>
        /// Number of times the connection is retried before <see cref="Room.ConnectAsync"/> fails. Defaults
        /// to 3.
        /// </summary>
        public uint JoinRetries { get; set; } = 3;

        /// <summary>
        /// End-to-end encryption options. Null, the default, disables encryption.
        /// </summary>
        public E2EEOptions? E2EE { get; set; }

        internal Proto.RoomOptions ToProto()
        {
            var proto = new Proto.RoomOptions
            {
                AutoSubscribe = AutoSubscribe,
                Dynacast = Dynacast,
                AdaptiveStream = AdaptiveStream,
                JoinRetries = JoinRetries,
            };

            // Without this the FFI ConnectRequest carries no E2EE configuration, the Rust SDK
            // never attaches frame cryptors, and media is silently sent/received in plaintext.
            if (E2EE != null)
            {
                proto.Encryption = E2EE.ToProto();
            }

            return proto;
        }
    }

    /// <summary>
    /// A connection to a LiveKit room: joins it with <see cref="ConnectAsync"/>, exposes the local and remote
    /// participants and their tracks, and raises an event for every change.
    /// </summary>
    /// <example>
    /// <code language="csharp">
    /// using var room = new Room();
    /// room.ParticipantConnected += (sender, participant) =>
    ///     Console.WriteLine($"{participant.Identity} joined");
    /// room.TrackSubscribed += (sender, e) =>
    ///     Console.WriteLine($"Subscribed to {e.Track.Name} of {e.Participant.Identity}");
    ///
    /// await room.ConnectAsync("wss://my.livekit.instance", token);
    ///
    /// // Publish an audio track and feed it 10 ms frames of 16-bit PCM
    /// var source = new AudioSource(48000, 1);
    /// var track = LocalAudioTrack.Create("microphone", source);
    /// await room.LocalParticipant!.PublishTrackAsync(track);
    /// await source.CaptureFrameAsync(new AudioFrame(new short[480], 48000, 1, 480));
    ///
    /// await room.DisconnectAsync();
    /// </code>
    /// </example>
    public class Room : IDisposable
    {
        private FfiHandle? _roomHandle;
        private readonly Dictionary<string, RemoteParticipant> _remoteParticipants;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _ffiEventLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        // CleanupOnDisconnect runs at most once per connection: DisconnectAsync and the FFI's
        // Disconnected room event both reach it
        private int _cleanedUp;
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

        // Remote data tracks by SID, delivered by DataTrackPublished. Protected by _lock.
        private readonly Dictionary<string, RemoteDataTrack> _remoteDataTracks =
            new Dictionary<string, RemoteDataTrack>();

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
        /// Raised when the encryption state of a participant changes: <c>Ok</c> once its frames encrypt or
        /// decrypt, or a failure such as <c>MissingKey</c> or <c>DecryptionFailed</c>.
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

        /// <summary>
        /// Raised when the permissions of a participant (local or remote) change.
        /// <see cref="Participant.Permissions"/> is already updated when the event fires.
        /// </summary>
        public event EventHandler<ParticipantPermissionsChangedEventArgs>? ParticipantPermissionsChanged;

        /// <summary>
        /// Raised when a remote participant becomes active and is ready to receive data messages.
        /// </summary>
        public event EventHandler<RemoteParticipant>? ParticipantActive;

        /// <summary>
        /// Raised when the SDK re-published a local track during a full reconnect. The publication
        /// object is updated in place with the new server-assigned SIDs; the previous SID is passed
        /// so state keyed by it can be reconciled.
        /// </summary>
        public event EventHandler<LocalTrackRepublishedEventArgs>? LocalTrackRepublished;

        /// <summary>
        /// Raised when a remote participant publishes a data track. Subscribe to it with
        /// <see cref="RemoteDataTrack.Subscribe"/>.
        /// </summary>
        public event EventHandler<RemoteDataTrack>? DataTrackPublished;

        /// <summary>
        /// Raised when a remote data track is unpublished.
        /// </summary>
        public event EventHandler<DataTrackUnpublishedEventArgs>? DataTrackUnpublished;

        /// <summary>
        /// Raised after <see cref="LocalParticipant.SetDataChannelBufferedAmountLowThreshold"/>
        /// changed the threshold of a data channel.
        /// </summary>
        public event EventHandler<DataChannelBufferedAmountLowThresholdChangedEventArgs>? DataChannelBufferedAmountLowThresholdChanged;

        /// <summary>
        /// Data tracks currently published by remote participants, by SID.
        /// </summary>
        public IReadOnlyDictionary<string, RemoteDataTrack> RemoteDataTracks
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<string, RemoteDataTrack>(_remoteDataTracks);
                }
            }
        }

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
                _eventTaskChain = _eventTaskChain.ContinueWith(
                    _ =>
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[ERROR] Room event handler exception: {ex}");
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
                                    $"[ERROR] Room event handler exception: {ex}"
                                );
                            }
                        },
                        TaskScheduler.Default
                    )
                    .Unwrap();
            }
        }

        /// <summary>
        /// Creates a new Room instance.
        /// </summary>
        public Room()
        {
            _remoteParticipants = new Dictionary<string, RemoteParticipant>();
        }

        // Reports a state once per change, with the value at the time of the change (the
        // handler may run later, when the state has already moved on)
        private void UpdateConnectionState(Proto.ConnectionState newState)
        {
            if (ConnectionState == newState)
                return;
            ConnectionState = newState;
            var state = newState;
            DispatchEvent(() => ConnectionStateChanged?.Invoke(this, state));
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

            // Listen to FFI events for the lifetime of this connection (CleanupOnDisconnect
            // unsubscribes): a Room that is never disposed must not stay in the process-wide
            // FfiClient forever
            FfiClient.Instance.EventReceived -= OnFfiEvent;
            FfiClient.Instance.EventReceived += OnFfiEvent;
            Interlocked.Exchange(ref _cleanedUp, 0);

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
            FfiEvent connectEvent;
            try
            {
                connectEvent = await FfiClient.Instance.WaitForEventAsync(
                    e =>
                        e.MessageCase == FfiEvent.MessageOneofCase.Connect
                        && e.Connect?.AsyncId == asyncId,
                    TimeSpan.FromSeconds(30),
                    cancellationToken
                );
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TimeoutException)
            {
                // The FFI may still complete the connection. It then waits for ReadyForRoomEvent
                // and, if it never comes, panics, which terminates the process. Finish the
                // handshake and leave the room in the background.
                FfiClient.Instance.EventReceived -= OnFfiEvent;
                _ = AbandonConnectAsync(asyncId);
                throw;
            }

            var callback = connectEvent.Connect!;

            if (!string.IsNullOrEmpty(callback.Error))
            {
                FfiClient.Instance.EventReceived -= OnFfiEvent;
                UpdateConnectionState(Proto.ConnectionState.ConnDisconnected);
                throw new RoomException($"Failed to connect: {callback.Error}");
            }

            // Get the result from the callback
            var result = callback.Result;
            if (result == null)
            {
                FfiClient.Instance.EventReceived -= OnFfiEvent;
                throw new RoomException("Connect callback has no result");
            }

            if (_disposed)
            {
                // Disposed while connecting: nothing may keep the native room alive
                FfiClient.Instance.EventReceived -= OnFfiEvent;
                _ = ReleaseAbandonedConnectionAsync(result);
                throw new ObjectDisposedException(nameof(Room));
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

            UpdateConnectionState(Proto.ConnectionState.ConnConnected);

            // Release the FFI room event gate after our listener and local state are ready.
            var readyRequest = new FfiRequest
            {
                ReadyForRoomEvent = new ReadyForRoomEventRequest
                {
                    RoomHandle = _roomHandle!.HandleId,
                },
            };
            FfiClient.Instance.SendRequest(readyRequest);

            DispatchEvent(() => Connected?.Invoke(this, EventArgs.Empty));
        }

        // A connect that the caller gave up on (cancellation, timeout) but that the FFI may still
        // complete: wait for its callback, then release the room properly.
        private async Task AbandonConnectAsync(ulong asyncId)
        {
            try
            {
                var connectEvent = await FfiClient.Instance.WaitForEventAsync(
                    e =>
                        e.MessageCase == FfiEvent.MessageOneofCase.Connect
                        && e.Connect?.AsyncId == asyncId,
                    TimeSpan.FromSeconds(60)
                );
                var result = connectEvent.Connect?.Result;
                if (result != null && string.IsNullOrEmpty(connectEvent.Connect?.Error))
                {
                    await ReleaseAbandonedConnectionAsync(result);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[WARN] Could not release an abandoned room connection: {ex.Message}"
                );
            }
        }

        private static async Task ReleaseAbandonedConnectionAsync(
            ConnectCallback.Types.Result result
        )
        {
            var handles = new List<ulong>();
            if (result.Room?.Handle != null)
                handles.Add(result.Room.Handle.Id);
            if (result.LocalParticipant?.Handle != null)
                handles.Add(result.LocalParticipant.Handle.Id);
            foreach (var participant in result.Participants)
            {
                if (participant.Participant?.Handle != null)
                    handles.Add(participant.Participant.Handle.Id);
                foreach (var publication in participant.Publications)
                {
                    if (publication.Handle != null)
                        handles.Add(publication.Handle.Id);
                }
            }

            try
            {
                var roomHandleId = result.Room?.Handle?.Id ?? 0;
                if (roomHandleId != 0)
                {
                    // The FFI forwards nothing (and panics after 15 s) until it gets this
                    FfiClient.Instance.SendRequest(
                        new FfiRequest
                        {
                            ReadyForRoomEvent = new ReadyForRoomEventRequest
                            {
                                RoomHandle = roomHandleId,
                            },
                        }
                    );
                    var response = FfiClient.Instance.SendRequest(
                        new FfiRequest
                        {
                            Disconnect = new DisconnectRequest
                            {
                                RoomHandle = roomHandleId,
                                Reason = Proto.DisconnectReason.ClientInitiated,
                            },
                        }
                    );
                    var asyncId = response.Disconnect?.AsyncId ?? 0;
                    await FfiClient.Instance.WaitForEventAsync(
                        e =>
                            e.MessageCase == FfiEvent.MessageOneofCase.Disconnect
                            && e.Disconnect?.AsyncId == asyncId,
                        TimeSpan.FromSeconds(10)
                    );
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[WARN] Could not release an abandoned room connection: {ex.Message}"
                );
            }
            finally
            {
                foreach (var id in handles)
                {
                    FfiHandle.FromId(id).Dispose();
                }
            }
        }

        /// <summary>
        /// Disconnects from the room.
        /// </summary>
        /// <returns>A task that completes once the room is disconnected and its resources are released.</returns>
        public async Task DisconnectAsync()
        {
            if (_roomHandle == null)
                return;

            var request = new FfiRequest
            {
                Disconnect = new DisconnectRequest
                {
                    RoomHandle = _roomHandle.HandleId,
                    Reason = Proto.DisconnectReason.ClientInitiated,
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.Disconnect == null)
                throw new InvalidOperationException("Invalid disconnect response");

            var asyncId = response.Disconnect.AsyncId;

            try
            {
                // Wait for disconnect callback
                await FfiClient.Instance.WaitForEventAsync(
                    e =>
                        e.MessageCase == FfiEvent.MessageOneofCase.Disconnect
                        && e.Disconnect?.AsyncId == asyncId,
                    TimeSpan.FromSeconds(10)
                );
            }
            finally
            {
                // Even if the callback never came: the room must not stay "connected"
                CleanupOnDisconnect(Proto.DisconnectReason.ClientInitiated);
            }
        }

        /// <summary>
        /// Forces a reconnection scenario, for tests. With
        /// <see cref="Proto.SimulateScenarioKind.SimulateFullReconnect"/> the SDK rebuilds its session
        /// and republishes the local tracks: <see cref="Reconnecting"/> fires, then one
        /// <see cref="LocalTrackRepublished"/> per published track, then <see cref="Reconnected"/>.
        /// With <see cref="Proto.SimulateScenarioKind.SimulateSignalReconnect"/> only the signaling
        /// connection is re-established (a resume) and the publications are kept. See
        /// <see cref="Proto.SimulateScenarioKind"/> for the other variants. The returned task completes
        /// once the SDK has started the scenario, not when the reconnection has finished: observe that
        /// through the room events.
        /// </summary>
        /// <param name="scenario">The scenario to simulate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes once the SDK has started the scenario, not when the reconnection has finished.</returns>
        /// <exception cref="InvalidOperationException">Thrown when not connected to a room.</exception>
        /// <exception cref="RoomException">Thrown when the SDK reports that the scenario failed.</exception>
        public async Task SimulateScenarioAsync(
            Proto.SimulateScenarioKind scenario,
            CancellationToken cancellationToken = default
        )
        {
            if (!IsConnected || _roomHandle == null)
                throw new InvalidOperationException("Not connected to a room");

            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    SimulateScenario = new SimulateScenarioRequest
                    {
                        RoomHandle = _roomHandle.HandleId,
                        Scenario = scenario,
                    },
                }
            );

            if (response.SimulateScenario == null)
                throw new InvalidOperationException("Invalid simulate scenario response");

            var asyncId = response.SimulateScenario.AsyncId;

            var callbackEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.SimulateScenario
                    && e.SimulateScenario?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            var callback = callbackEvent.SimulateScenario!;
            if (callback.HasError && !string.IsNullOrEmpty(callback.Error))
                throw new RoomException($"Simulate scenario failed: {callback.Error}");
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
        /// Disconnects from the room, blocking the calling thread until it is done. Prefer
        /// <see cref="DisconnectAsync"/> in asynchronous code.
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
                var localParticipant = LocalParticipant; // cleared by CleanupOnDisconnect meanwhile
                if (invocation == null || localParticipant == null)
                    return;

                // Check if this event is for our local participant
                if (invocation.LocalParticipantHandle != localParticipant.Handle.HandleId)
                    return;

                // Fire and forget immediately. Do NOT use DispatchEvent.
                // RPCs are independent request/response cycles and should not be blocked by the event queue.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await localParticipant.HandleRpcMethodInvocationAsync(
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
                        Console.Error.WriteLine($"[ERROR] Error handling RPC invocation: {ex}");
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

                case RoomEvent.MessageOneofCase.ParticipantPermissionChanged:
                    HandleParticipantPermissionChanged(roomEvent.ParticipantPermissionChanged);
                    break;

                case RoomEvent.MessageOneofCase.ParticipantActive:
                    HandleParticipantActive(roomEvent.ParticipantActive);
                    break;

                case RoomEvent.MessageOneofCase.ParticipantsUpdated:
                    HandleParticipantsUpdated(roomEvent.ParticipantsUpdated);
                    break;

                case RoomEvent.MessageOneofCase.LocalTrackRepublished:
                    HandleLocalTrackRepublished(roomEvent.LocalTrackRepublished);
                    break;

                case RoomEvent.MessageOneofCase.DataTrackPublished:
                    HandleDataTrackPublished(roomEvent.DataTrackPublished);
                    break;

                case RoomEvent.MessageOneofCase.DataTrackUnpublished:
                    HandleDataTrackUnpublished(roomEvent.DataTrackUnpublished);
                    break;

                case RoomEvent.MessageOneofCase.DataChannelLowThresholdChanged:
                    HandleDataChannelLowThresholdChanged(roomEvent.DataChannelLowThresholdChanged);
                    break;

                case RoomEvent.MessageOneofCase.Disconnected:
                    CleanupOnDisconnect(
                        roomEvent.Disconnected?.Reason ?? Proto.DisconnectReason.UnknownReason
                    );
                    break;

                case RoomEvent.MessageOneofCase.Reconnecting:
                    UpdateConnectionState(Proto.ConnectionState.ConnReconnecting);
                    DispatchEvent(() => Reconnecting?.Invoke(this, EventArgs.Empty));
                    break;

                case RoomEvent.MessageOneofCase.Reconnected:
                    UpdateConnectionState(Proto.ConnectionState.ConnConnected);
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
            var textReaders = new List<TextStreamReader>();
            var byteReaders = new List<ByteStreamReader>();
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                    return;

                _remoteParticipants.Remove(evt.ParticipantIdentity);

                // Its data streams will never get their trailer
                foreach (var reader in _textStreamReaders.Values)
                {
                    if (reader.SenderIdentity == evt.ParticipantIdentity)
                        textReaders.Add(reader);
                }
                foreach (var reader in _byteStreamReaders.Values)
                {
                    if (reader.SenderIdentity == evt.ParticipantIdentity)
                        byteReaders.Add(reader);
                }
                foreach (var reader in textReaders)
                    _textStreamReaders.Remove(reader.Info.StreamId);
                foreach (var reader in byteReaders)
                    _byteStreamReaders.Remove(reader.Info.StreamId);
            }

            participant._info.DisconnectReason = evt.DisconnectReason;

            var error = new StreamException(
                $"Participant {evt.ParticipantIdentity} disconnected before closing the stream"
            );
            foreach (var reader in textReaders)
                reader.Fail(error);
            foreach (var reader in byteReaders)
                reader.Fail(error);

            DispatchEvent(() => ParticipantDisconnected?.Invoke(this, participant));
        }

        private void HandleLocalTrackPublished(Proto.LocalTrackPublished evt)
        {
            if (LocalParticipant == null || string.IsNullOrEmpty(evt?.TrackSid))
                return;

            var localParticipant = LocalParticipant;
            DispatchEvent(async () =>
            {
                // PublishTrackAsync registers the publication from its own continuation; wait
                // for it instead of polling
                var publication = await localParticipant.WaitForPublicationAsync(
                    evt.TrackSid,
                    TimeSpan.FromSeconds(10)
                );
                if (publication == null)
                {
                    Console.Error.WriteLine(
                        $"[WARN] RoomEvent.LocalTrackPublished: Publication {evt.TrackSid} not found"
                    );
                    return;
                }
                LocalTrackPublished?.Invoke(
                    this,
                    new LocalTrackPublishedEventArgs(publication, localParticipant)
                );
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
                    var localParticipant = LocalParticipant;
                    if (localParticipant == null)
                        throw new InvalidOperationException("Local participant not available");

                    // Retrieve the publication directly from the local participant's unpublished tracks.
                    // Protected by Room.FfiEventLock.
                    LocalTrackPublication? publication;
                    await _ffiEventLock.WaitAsync();
                    try
                    {
                        publication = localParticipant.TakeUnpublishedTrackPublication(
                            evt.PublicationSid
                        );
                    }
                    finally
                    {
                        _ffiEventLock.Release();
                    }

                    if (publication == null)
                        throw new InvalidOperationException(
                            $"Publication {evt.PublicationSid} not found"
                        );

                    LocalTrackUnpublished?.Invoke(
                        this,
                        new LocalTrackPublishedEventArgs(publication, localParticipant)
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

            var localParticipant = LocalParticipant;
            DispatchEvent(async () =>
            {
                var publication = await localParticipant.WaitForPublicationAsync(
                    evt.TrackSid,
                    TimeSpan.FromSeconds(10)
                );
                if (publication == null)
                {
                    Console.Error.WriteLine(
                        $"[WARN] RoomEvent.LocalTrackSubscribed: Publication {evt.TrackSid} not found"
                    );
                    return;
                }
                publication.ResolveFirstSubscription();
                LocalTrackSubscribed?.Invoke(this, new LocalTrackSubscribedEventArgs(publication));
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

            // Add publication to participant (synchronously, so later events can rely on it)
            participant.AddPublication(evt.Publication);
            var publication =
                participant.GetTrackPublication(evt.Publication.Info?.Sid ?? "")
                as RemoteTrackPublication;
            if (publication == null)
            {
                Console.Error.WriteLine(
                    $"[WARN] RoomEvent.TrackPublished: Publication {evt.Publication.Info?.Sid} not found after AddPublication"
                );
                return;
            }

            DispatchEvent(() =>
                TrackPublished?.Invoke(this, new TrackPublishedEventArgs(publication, participant))
            );
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

            var trackInfo = evt.Track.Info;
            if (trackInfo == null)
                return;

            // The participant and the publication are resolved here, on the callback thread:
            // a ParticipantDisconnected that follows would remove them before the chain runs
            var publication =
                participant.GetTrackPublication(trackInfo.Sid ?? "") as RemoteTrackPublication;
            if (publication == null)
            {
                Console.Error.WriteLine(
                    $"[WARN] RoomEvent.TrackSubscribed: Publication {trackInfo.Sid} not found for participant {evt.ParticipantIdentity}"
                );
                FfiHandle.FromId(evt.Track.Handle?.Id ?? 0).Dispose();
                return;
            }

            var handle = FfiHandle.FromId(evt.Track.Handle?.Id ?? 0);
            RemoteTrack? track = null;
            if (trackInfo.Kind == Proto.TrackKind.KindAudio)
            {
                track = new RemoteAudioTrack(handle, evt.Track);
            }
            else if (trackInfo.Kind == Proto.TrackKind.KindVideo)
            {
                track = new RemoteVideoTrack(handle, evt.Track);
            }
            if (track == null)
            {
                handle.Dispose();
                return;
            }

            publication.Track = track;
            DispatchEvent(() =>
                TrackSubscribed?.Invoke(
                    this,
                    new TrackSubscribedEventArgs(track, publication, participant)
                )
            );
        }

        private void HandleTrackUnsubscribed(Proto.TrackUnsubscribed evt)
        {
            if (
                string.IsNullOrEmpty(evt?.ParticipantIdentity) || string.IsNullOrEmpty(evt.TrackSid)
            )
                return;

            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                {
                    Console.Error.WriteLine(
                        $"[WARN] RoomEvent.TrackUnsubscribed: Participant {evt.ParticipantIdentity} not found"
                    );
                    return;
                }
            }

            var publication =
                participant.GetTrackPublication(evt.TrackSid) as RemoteTrackPublication;
            var track = publication?.Track;
            if (publication == null || track == null)
            {
                Console.Error.WriteLine(
                    $"[WARN] RoomEvent.TrackUnsubscribed: Track {evt.TrackSid} of participant {evt.ParticipantIdentity} is not subscribed"
                );
                return;
            }

            publication.Track = null;
            DispatchEvent(() =>
                TrackUnsubscribed?.Invoke(
                    this,
                    new TrackSubscribedEventArgs(track, publication, participant)
                )
            );
        }

        private void HandleTrackMuted(Proto.TrackMuted evt)
        {
            HandleTrackMuteChanged(evt?.ParticipantIdentity, evt?.TrackSid, muted: true);
        }

        private void HandleTrackUnmuted(Proto.TrackUnmuted evt)
        {
            HandleTrackMuteChanged(evt?.ParticipantIdentity, evt?.TrackSid, muted: false);
        }

        private void HandleTrackMuteChanged(
            string? participantIdentity,
            string? trackSid,
            bool muted
        )
        {
            var eventName = muted ? "TrackMuted" : "TrackUnmuted";
            if (string.IsNullOrEmpty(participantIdentity) || string.IsNullOrEmpty(trackSid))
                return;

            var participant = GetParticipantByIdentity(participantIdentity);
            if (participant == null)
            {
                Console.Error.WriteLine(
                    $"[WARN] RoomEvent.{eventName}: Participant {participantIdentity} not found"
                );
                return;
            }

            DispatchEvent(async () =>
            {
                var publication = participant.GetTrackPublication(trackSid);
                if (publication == null && participant is LocalParticipant localParticipant)
                {
                    // PublishTrackAsync may not have registered the publication yet
                    publication = await localParticipant.WaitForPublicationAsync(
                        trackSid,
                        TimeSpan.FromSeconds(10)
                    );
                }
                if (publication == null)
                {
                    Console.Error.WriteLine(
                        $"[WARN] RoomEvent.{eventName}: Publication {trackSid} not found for participant {participantIdentity}"
                    );
                    return;
                }

                var updatedInfo = publication.Info;
                updatedInfo.Muted = muted;
                publication.UpdateInfo(updatedInfo);

                var args = new TrackMutedEventArgs(publication, participant);
                if (muted)
                    TrackMuted?.Invoke(this, args);
                else
                    TrackUnmuted?.Invoke(this, args);
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

                // Copy data from native memory (block copy: this is the FFI callback thread)
                var bufferInfo = evt.User.Data?.Data;
                if (bufferInfo != null && bufferInfo.DataLen > 0)
                {
                    data = new byte[bufferInfo.DataLen];
                    Marshal.Copy((IntPtr)bufferInfo.DataPtr, data, 0, (int)bufferInfo.DataLen);
                }

                // Release the native buffer whether or not it had data
                if (evt.User.Data?.Handle != null)
                {
                    FfiHandle.FromId(evt.User.Data.Handle.Id).Dispose();
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

                // Keep Participant.Attributes current, like the Node and Python SDKs
                participant._info.Attributes.Clear();
                participant._info.Attributes.Add(attributes);

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
                var reader = new TextStreamReader(header) { SenderIdentity = participantIdentity };
                lock (_lock)
                {
                    _textStreamReaders[header.StreamId] = reader;
                }
                RunStreamHandler(() => _streamHandlers.Dispatch(reader, participantIdentity));
            }
            else if (header.ByteHeader != null)
            {
                // Byte stream
                var reader = new ByteStreamReader(header) { SenderIdentity = participantIdentity };
                lock (_lock)
                {
                    _byteStreamReaders[header.StreamId] = reader;
                }
                RunStreamHandler(() => _streamHandlers.Dispatch(reader, participantIdentity));
            }
        }

        // Stream handlers get their chunks from this same FFI callback thread, so they run on
        // the thread pool (like RPC invocations): a handler that blocks on ReadAllAsync would
        // otherwise deadlock and stall every FFI event in the process
        private static void RunStreamHandler(Action dispatch)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    dispatch();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] Data stream handler exception: {ex}");
                }
            });
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

        private void HandleParticipantPermissionChanged(Proto.ParticipantPermissionChanged evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            var participant = GetParticipantByIdentity(evt.ParticipantIdentity);
            if (participant == null)
                return;

            var permissions = evt.Permission;
            participant._info.Permission = permissions;
            DispatchEvent(() =>
                ParticipantPermissionsChanged?.Invoke(
                    this,
                    new ParticipantPermissionsChangedEventArgs(participant, permissions)
                )
            );
        }

        private void HandleParticipantActive(Proto.ParticipantActive evt)
        {
            if (string.IsNullOrEmpty(evt?.ParticipantIdentity))
                return;

            RemoteParticipant? participant;
            lock (_lock)
            {
                if (!_remoteParticipants.TryGetValue(evt.ParticipantIdentity, out participant))
                    return;
            }

            participant._info.State = Proto.ParticipantState.Active;
            DispatchEvent(() => ParticipantActive?.Invoke(this, participant));
        }

        private void HandleParticipantsUpdated(Proto.ParticipantsUpdated evt)
        {
            if (evt == null)
                return;

            // Bulk refresh of participant infos; no public event, like the Node and Python SDKs
            foreach (var info in evt.Participants)
            {
                if (info == null)
                    continue;
                GetParticipantByIdentity(info.Identity)?.UpdateInfo(info);
            }
        }

        private void HandleLocalTrackRepublished(Proto.LocalTrackRepublished evt)
        {
            if (
                LocalParticipant == null
                || string.IsNullOrEmpty(evt?.PreviousSid)
                || evt.Info == null
            )
                return;

            DispatchEvent(async () =>
            {
                var localParticipant = LocalParticipant;
                if (localParticipant == null)
                    return;

                // Protected by Room.FfiEventLock, like the unpublish bookkeeping.
                LocalTrackPublication? publication;
                await _ffiEventLock.WaitAsync();
                try
                {
                    publication = localParticipant.RepublishTrackPublication(
                        evt.PreviousSid,
                        evt.Info
                    );
                }
                finally
                {
                    _ffiEventLock.Release();
                }

                if (publication == null)
                {
                    Console.Error.WriteLine(
                        $"[WARN] RoomEvent.LocalTrackRepublished: previous publication not found: {evt.PreviousSid}"
                    );
                    return;
                }

                LocalTrackRepublished?.Invoke(
                    this,
                    new LocalTrackRepublishedEventArgs(
                        publication,
                        evt.PreviousSid,
                        localParticipant
                    )
                );
            });
        }

        private void HandleDataTrackPublished(Proto.DataTrackPublished evt)
        {
            if (evt?.Track?.Handle == null || evt.Track.Info == null)
                return;

            var track = new RemoteDataTrack(evt.Track);
            lock (_lock)
            {
                _remoteDataTracks[track.Info.Sid] = track;
            }

            DispatchEvent(() => DataTrackPublished?.Invoke(this, track));
        }

        private void HandleDataTrackUnpublished(Proto.DataTrackUnpublished evt)
        {
            if (string.IsNullOrEmpty(evt?.Sid))
                return;

            RemoteDataTrack? track;
            lock (_lock)
            {
                _remoteDataTracks.Remove(evt.Sid, out track);
            }

            DispatchEvent(() =>
                DataTrackUnpublished?.Invoke(
                    this,
                    new DataTrackUnpublishedEventArgs(evt.Sid, track)
                )
            );
        }

        private void HandleDataChannelLowThresholdChanged(
            Proto.DataChannelBufferedAmountLowThresholdChanged evt
        )
        {
            if (evt == null)
                return;

            DispatchEvent(() =>
                DataChannelBufferedAmountLowThresholdChanged?.Invoke(
                    this,
                    new DataChannelBufferedAmountLowThresholdChangedEventArgs(
                        evt.Kind,
                        evt.Threshold
                    )
                )
            );
        }

        private void CleanupOnDisconnect(Proto.DisconnectReason reason)
        {
            if (Interlocked.Exchange(ref _cleanedUp, 1) == 1)
                return;

            // From here on IsConnected is false and OnFfiEvent ignores events for the old handle
            var roomHandle = _roomHandle;
            _roomHandle = null;

            UpdateConnectionState(Proto.ConnectionState.ConnDisconnected);
            DispatchEvent(() => Disconnected?.Invoke(this, reason));

            // Clear the participant state only after the handlers above ran, so a Disconnected
            // handler can still read LocalParticipant and RemoteParticipants
            DispatchEvent(() =>
            {
                var textReaders = new List<TextStreamReader>();
                var byteReaders = new List<ByteStreamReader>();
                lock (_lock)
                {
                    _remoteParticipants.Clear();
                    textReaders.AddRange(_textStreamReaders.Values);
                    byteReaders.AddRange(_byteStreamReaders.Values);
                    _textStreamReaders.Clear();
                    _byteStreamReaders.Clear();
                    _remoteDataTracks.Clear();
                }

                // Readers waiting for more chunks would otherwise wait forever
                var error = new StreamException(
                    "The room disconnected before the stream was closed"
                );
                foreach (var reader in textReaders)
                    reader.Fail(error);
                foreach (var reader in byteReaders)
                    reader.Fail(error);

                LocalParticipant?.CancelPendingSubscriptionWaits();
                LocalParticipant = null;

                FfiClient.Instance.EventReceived -= OnFfiEvent;
            });

            roomHandle?.Dispose();
        }

        /// <summary>
        /// Disconnects from the room if it is still connected, raising the usual teardown events, and
        /// releases the native resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Disconnect while still listening, so the teardown events (LocalTrackUnpublished,
            // TrackUnsubscribed, ParticipantDisconnected...) are delivered like DisconnectAsync does
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

            FfiClient.Instance.EventReceived -= OnFfiEvent;
            _roomHandle?.Dispose();
        }
    }

    /// <summary>
    /// Statistics of the two WebRTC peer connections of the session, returned by
    /// <see cref="Room.GetRtcStatsAsync"/>.
    /// </summary>
    public class RtcStats
    {
        /// <summary>
        /// Statistics of the publishing peer connection, one <see cref="Proto.RtcStats"/> per WebRTC stats report.
        /// </summary>
        public List<Proto.RtcStats> PublisherStats { get; set; } = new List<Proto.RtcStats>();

        /// <summary>
        /// Statistics of the subscribing peer connection, one <see cref="Proto.RtcStats"/> per WebRTC stats report.
        /// </summary>
        public List<Proto.RtcStats> SubscriberStats { get; set; } = new List<Proto.RtcStats>();
    }
}
