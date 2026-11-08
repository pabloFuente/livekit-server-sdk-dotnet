// author: https://github.com/pabloFuente

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        /// The participant's permissions in the room, or null if the server has not sent them.
        /// Updated before <see cref="Room.ParticipantPermissionsChanged"/> is raised.
        /// </summary>
        public Proto.ParticipantPermission? Permissions => _info.Permission;

        /// <summary>
        /// Participant attributes, as last reported by the server.
        /// </summary>
        public IReadOnlyDictionary<string, string> Attributes
        {
            get
            {
                var info = _info;
                return new Dictionary<string, string>(info.Attributes);
            }
        }

        /// <summary>
        /// Connection state of the participant.
        /// </summary>
        public Proto.ParticipantState State => _info.State;

        /// <summary>
        /// Why the participant left, set before <see cref="Room.ParticipantDisconnected"/> is raised.
        /// </summary>
        public Proto.DisconnectReason DisconnectReason => _info.DisconnectReason;

        /// <summary>
        /// Track publications by this participant. The FFI callback thread mutates it, so every
        /// access goes through <see cref="PublicationsLock"/>.
        /// </summary>
        protected readonly Dictionary<string, TrackPublication> _trackPublications;

        internal readonly object PublicationsLock = new object();

        /// <summary>
        /// Gets all track publications by this participant (a snapshot).
        /// </summary>
        public IReadOnlyDictionary<string, TrackPublication> TrackPublications
        {
            get
            {
                lock (PublicationsLock)
                {
                    return new Dictionary<string, TrackPublication>(_trackPublications);
                }
            }
        }

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
            lock (PublicationsLock)
            {
                return _trackPublications.TryGetValue(sid, out var pub) ? pub : null;
            }
        }
    }

    /// <summary>
    /// Represents the local participant in the room.
    /// </summary>
    public class LocalParticipant : Participant
    {
        // Registered on user threads, read from the Task.Run that serves each invocation
        private readonly ConcurrentDictionary<string, RpcMethodHandler> _rpcHandlers =
            new ConcurrentDictionary<string, RpcMethodHandler>();

        // LocalTrackPublished/Subscribed room events can be processed before PublishTrackAsync
        // has registered the publication (the FFI sends the callback first, but its continuation
        // runs on the thread pool). The event handlers wait here instead of polling.
        // Protected by Room.FfiEventLock.
        private readonly Dictionary<
            string,
            TaskCompletionSource<LocalTrackPublication>
        > _pendingPublicationWaiters =
            new Dictionary<string, TaskCompletionSource<LocalTrackPublication>>();

        // Publications that UnpublishTrackAsync already removed, but whose
        // Room.LocalTrackUnpublished event has not arrived yet. The FFI may deliver the event after
        // UnpublishTrackAsync has already removed the publication from _trackPublications. This
        // collection allows the event to be raised with the proper LocalTrackPublication.
        // Protected by Room.FfiEventLock.
        private readonly Dictionary<
            string,
            LocalTrackPublication
        > _publicationsPendingUnpublishedEvent = new Dictionary<string, LocalTrackPublication>();

        // Publications whose PublishTrackAsync was cancelled or timed out after the FFI published
        // them. ReconcileAbandonedPublishAsync unpublishes them; their room events are not raised
        // (the caller never got the publication), and nothing waits for their registration.
        // Protected by Room.FfiEventLock.
        private readonly HashSet<string> _abandonedPublications = new HashSet<string>();

        internal LocalParticipant(FfiHandle handle, ParticipantInfo? info, Room room)
            : base(handle, info, room) { }

        /// <summary>
        /// Resolves and removes the publication a LocalTrackUnpublished room event refers to,
        /// whether it is still in <see cref="Participant.TrackPublications"/> or was already
        /// removed by <see cref="UnpublishTrackAsync"/>. The caller must hold Room.FfiEventLock.
        /// </summary>
        /// <param name="trackSid">The SID of the unpublished track.</param>
        /// <returns>The publication, or null if the SID is unknown.</returns>
        internal LocalTrackPublication? TakeUnpublishedTrackPublication(string trackSid)
        {
            lock (PublicationsLock)
            {
                if (_trackPublications.Remove(trackSid, out var publication))
                    return publication as LocalTrackPublication;
            }

            return _publicationsPendingUnpublishedEvent.Remove(trackSid, out var pending)
                ? pending
                : null;
        }

        /// <summary>
        /// Returns the local publication with the given SID, waiting for
        /// <see cref="PublishTrackAsync"/> to register it if it has not yet. Null after the timeout.
        /// </summary>
        internal async Task<LocalTrackPublication?> WaitForPublicationAsync(
            string trackSid,
            TimeSpan timeout
        )
        {
            TaskCompletionSource<LocalTrackPublication> waiter;
            await Room.FfiEventLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_abandonedPublications.Contains(trackSid))
                    return null;
                lock (PublicationsLock)
                {
                    if (
                        _trackPublications.TryGetValue(trackSid, out var existing)
                        && existing is LocalTrackPublication found
                    )
                        return found;
                }
                if (!_pendingPublicationWaiters.TryGetValue(trackSid, out waiter))
                {
                    waiter = new TaskCompletionSource<LocalTrackPublication>(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    _pendingPublicationWaiters[trackSid] = waiter;
                }
            }
            finally
            {
                Room.FfiEventLock.Release();
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            using (timeoutCts.Token.Register(() => waiter.TrySetCanceled()))
            {
                try
                {
                    return await waiter.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await Room.FfiEventLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        _pendingPublicationWaiters.Remove(trackSid);
                    }
                    finally
                    {
                        Room.FfiEventLock.Release();
                    }
                    return null;
                }
            }
        }

        /// <summary>
        /// Records a publication whose <see cref="PublishTrackAsync"/> was abandoned, and wakes the
        /// room event handler waiting for its registration, which would otherwise stall the room's
        /// event chain until its timeout.
        /// </summary>
        private async Task MarkPublicationAbandonedAsync(string trackSid)
        {
            await Room.FfiEventLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _abandonedPublications.Add(trackSid);
                if (_pendingPublicationWaiters.Remove(trackSid, out var waiter))
                    waiter.TrySetCanceled();
            }
            finally
            {
                Room.FfiEventLock.Release();
            }
        }

        /// <summary>
        /// Whether a room event refers to a publication whose <see cref="PublishTrackAsync"/> was
        /// abandoned: such events are dropped silently. <paramref name="forget"/> removes the record,
        /// for the last event of the publication (its unpublication).
        /// </summary>
        internal async Task<bool> IsAbandonedPublicationAsync(string trackSid, bool forget = false)
        {
            await Room.FfiEventLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return forget
                    ? _abandonedPublications.Remove(trackSid)
                    : _abandonedPublications.Contains(trackSid);
            }
            finally
            {
                Room.FfiEventLock.Release();
            }
        }

        /// <summary>
        /// The room disconnected: nobody will subscribe to the local tracks anymore.
        /// </summary>
        internal void CancelPendingSubscriptionWaits()
        {
            foreach (var publication in TrackPublications.Values.OfType<LocalTrackPublication>())
            {
                publication.CancelFirstSubscription();
            }
        }

        /// <summary>
        /// Applies a LocalTrackRepublished room event: the SDK re-published the track during a full
        /// reconnect and the server issued new SIDs. The existing publication object is updated in
        /// place (so cached references stay valid) and re-keyed under its new SID. The caller must
        /// hold Room.FfiEventLock.
        /// </summary>
        /// <param name="previousSid">The SID the publication had before the reconnect.</param>
        /// <param name="info">The new publication info.</param>
        /// <returns>The updated publication, or null if the previous SID is unknown.</returns>
        internal LocalTrackPublication? RepublishTrackPublication(
            string previousSid,
            TrackPublicationInfo info
        )
        {
            lock (PublicationsLock)
            {
                if (!_trackPublications.Remove(previousSid, out var existing))
                    return null;
                if (!(existing is LocalTrackPublication publication))
                    return null;

                publication.UpdateInfo(info);
                _trackPublications[publication.Sid] = publication;
                // Keep the local-track invariant (track.Sid == publication.Sid) across the republish
                publication.Track?.UpdateSid(publication.Sid);
                return publication;
            }
        }

        /// <summary>
        /// Publishes a track to the room.
        /// </summary>
        /// <param name="track">The track to publish.</param>
        /// <param name="options">Publication options.</param>
        /// <param name="cancellationToken">
        /// Cancels the publication. A track the native side publishes after the cancellation is unpublished
        /// in the background; a token that is already cancelled throws before anything is published.
        /// </param>
        /// <returns>The track publication.</returns>
        public async Task<LocalTrackPublication> PublishTrackAsync(
            LocalTrack track,
            TrackPublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options ??= new TrackPublishOptions();
            // Before any side effect: once requested, a publication that the cancellation abandons
            // has to be unpublished in the background
            cancellationToken.ThrowIfCancellationRequested();

            var request = new FfiRequest
            {
                PublishTrack = new PublishTrackRequest
                {
                    LocalParticipantHandle = Handle.HandleId,
                    TrackHandle = track.Handle.HandleId,
                    Options = options.ToProto(),
                },
            };

            var response = FfiClient.Instance.SendRequest(request);

            if (response.PublishTrack == null)
                throw new InvalidOperationException("Invalid publish track response");

            var asyncId = response.PublishTrack.AsyncId;

            // Wait for publish callback (don't hold lock during wait)
            FfiEvent publishEvent;
            try
            {
                publishEvent = await FfiClient.Instance.WaitForEventAsync(
                    e =>
                        e.MessageCase == FfiEvent.MessageOneofCase.PublishTrack
                        && e.PublishTrack?.AsyncId == asyncId,
                    TimeSpan.FromSeconds(30),
                    cancellationToken
                );
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TimeoutException)
            {
                // The FFI may still publish the track; it would then be live on the server but
                // unknown to this SDK. Undo it when its callback arrives.
                _ = ReconcileAbandonedPublishAsync(asyncId);
                throw;
            }

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
            // A freshly created local track reports "TR_unknown" until it is published
            track.UpdateSid(localPublication.Sid);

            // Acquire lock only for modifying state (matches Node SDK pattern). Not cancellable:
            // the track is published, so it must be registered.
            await Room.FfiEventLock.WaitAsync();
            try
            {
                lock (PublicationsLock)
                {
                    _trackPublications[localPublication.Sid] = localPublication;
                }
                if (_pendingPublicationWaiters.Remove(localPublication.Sid, out var waiter))
                {
                    waiter.TrySetResult(localPublication);
                }
                return localPublication;
            }
            finally
            {
                // Release lock - now FFI events can be processed
                Room.FfiEventLock.Release();
            }
        }

        private async Task ReconcileAbandonedPublishAsync(ulong asyncId)
        {
            try
            {
                var publishEvent = await FfiClient.Instance.WaitForEventAsync(
                    e =>
                        e.MessageCase == FfiEvent.MessageOneofCase.PublishTrack
                        && e.PublishTrack?.AsyncId == asyncId,
                    TimeSpan.FromSeconds(60)
                );
                var publication = publishEvent.PublishTrack?.Publication;
                if (publishEvent.PublishTrack?.HasError == true || publication?.Info == null)
                    return;

                using var handle = FfiHandle.FromId(publication.Handle.Id);
                Console.Error.WriteLine(
                    $"[WARN] PublishTrackAsync was cancelled after track {publication.Info.Sid} was published: unpublishing it"
                );
                await MarkPublicationAbandonedAsync(publication.Info.Sid).ConfigureAwait(false);
                await UnpublishTrackAsync(publication.Info.Sid).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[WARN] Could not reconcile a cancelled PublishTrackAsync: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Unpublishes a track from the room.
        /// </summary>
        /// <param name="trackSid">The SID of the track to unpublish.</param>
        /// <param name="cancellationToken">
        /// Cancels the wait for the unpublication. A token that is already cancelled throws before the
        /// unpublication is requested.
        /// </param>
        /// <returns>A task that completes once the track is unpublished.</returns>
        public async Task UnpublishTrackAsync(
            string trackSid,
            CancellationToken cancellationToken = default
        )
        {
            // The FFI unwraps the SID conversion: anything but a "TR_..." SID panics the native
            // side, which terminates the process
            if (
                string.IsNullOrEmpty(trackSid)
                || !trackSid.StartsWith("TR_", StringComparison.Ordinal)
            )
                throw new ArgumentException(
                    $"'{trackSid}' is not a track SID (expected 'TR_...'), use publication.Sid",
                    nameof(trackSid)
                );
            cancellationToken.ThrowIfCancellationRequested();

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
                // If the LocalTrackUnpublished room event has not been processed yet, hand the
                // publication over to it instead of dropping it, so the event can still be raised
                // with the publication after it leaves TrackPublications. If the room event was
                // processed first, it has already removed the publication and there is nothing to do.
                TrackPublication? removed;
                lock (PublicationsLock)
                {
                    _trackPublications.Remove(trackSid, out removed);
                }
                if (removed is LocalTrackPublication localPublication)
                {
                    _publicationsPendingUnpublishedEvent[trackSid] = localPublication;
                }
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
        /// <returns>A task that completes once the data has been sent.</returns>
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
        /// Publishes a data track. Push frames on the returned track with
        /// <see cref="LocalDataTrack.TryPush"/>; remote participants receive it through
        /// <see cref="Room.DataTrackPublished"/>.
        /// </summary>
        /// <param name="name">
        /// The track name, used by other participants to identify it. Must not be empty and must be
        /// unique per publisher.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the wait for the publication. A token that is already cancelled throws before the track
        /// is published.
        /// </param>
        /// <returns>The published data track.</returns>
        /// <exception cref="PublishDataTrackException">The track could not be published.</exception>
        public async Task<LocalDataTrack> PublishDataTrackAsync(
            string name,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Data track name must not be empty", nameof(name));
            cancellationToken.ThrowIfCancellationRequested();

            var response = FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    PublishDataTrack = new PublishDataTrackRequest
                    {
                        LocalParticipantHandle = Handle.HandleId,
                        Options = new DataTrackOptions { Name = name },
                    },
                }
            );

            if (response.PublishDataTrack == null)
                throw new InvalidOperationException("Invalid publish data track response");

            var asyncId = response.PublishDataTrack.AsyncId;

            var publishEvent = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.PublishDataTrack
                    && e.PublishDataTrack?.AsyncId == asyncId,
                TimeSpan.FromSeconds(30),
                cancellationToken
            );

            var callback = publishEvent.PublishDataTrack!;
            if (callback.ResultCase == PublishDataTrackCallback.ResultOneofCase.Error)
            {
                throw new PublishDataTrackException(callback.Error.Code, callback.Error.Message);
            }
            if (callback.Track?.Handle == null || callback.Track.Info == null)
            {
                throw new RoomException("Publish data track callback has no track");
            }

            return new LocalDataTrack(callback.Track);
        }

        /// <summary>
        /// Sets the buffered-amount-low threshold of the lossy or reliable data channel. The room
        /// raises <see cref="Room.DataChannelBufferedAmountLowThresholdChanged"/> once applied.
        /// </summary>
        /// <param name="threshold">The new threshold in bytes.</param>
        /// <param name="kind">The data channel to configure.</param>
        public void SetDataChannelBufferedAmountLowThreshold(
            ulong threshold,
            Proto.DataPacketKind kind
        )
        {
            FfiClient.Instance.SendRequest(
                new FfiRequest
                {
                    SetDataChannelBufferedAmountLowThreshold =
                        new SetDataChannelBufferedAmountLowThresholdRequest
                        {
                            LocalParticipantHandle = Handle.HandleId,
                            Threshold = threshold,
                            Kind = kind,
                        },
                }
            );
        }

        /// <summary>
        /// Publishes SIP DTMF (Dual-Tone Multi-Frequency) tones to the room.
        /// </summary>
        /// <param name="code">The DTMF code.</param>
        /// <param name="digit">The DTMF digit as a string.</param>
        /// <param name="destinationIdentities">Optional destination participant identities.</param>
        /// <returns>A task that completes once the tones have been sent.</returns>
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
        /// <returns>A task that completes once the transcription has been sent.</returns>
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
                request.PublishTranscription.Segments.Add(segment);
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
        /// <returns>A task that completes once the update has been sent.</returns>
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
        /// <returns>A task that completes once the update has been sent.</returns>
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
        /// <returns>A task that completes once the update has been sent.</returns>
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

            return chatEvent.ChatMessage!.ChatMessage!;
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
                    OriginalMessage = originalMessage,
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

            return chatEvent.ChatMessage!.ChatMessage!;
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
        /// <param name="responseTimeout">Seconds to wait for the response; 15 when null.</param>
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

            // The FFI reports its own ResponseTimeout error after responseTimeout (15 s by
            // default); wait longer than that so callers get the RpcError, not a TimeoutException
            var callback = await FfiClient.Instance.WaitForEventAsync(
                e =>
                    e.MessageCase == FfiEvent.MessageOneofCase.PerformRpc
                    && e.PerformRpc?.AsyncId == asyncId,
                TimeSpan.FromSeconds((responseTimeout ?? 15) + 15)
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

            _rpcHandlers.TryRemove(method, out _);

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
                    // Like the Node and Python SDKs, the caller only learns that the handler
                    // failed: the message and the stack trace stay in this process
                    Console.Error.WriteLine($"[ERROR] RPC handler for '{method}' threw: {ex}");
                    responseError = RpcError.BuiltIn(RpcErrorCode.ApplicationError);
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
        // Tracks whose TrackUnsubscribed arrived although the room had not delivered their
        // TrackSubscribed, by SID, with the time it arrived. The Rust SDK dispatches the two from
        // different threads (the media path sets the track, the unpublication clears it), with no
        // lock spanning the change and the dispatch: a participant that unpublishes while its track
        // is being subscribed can have the unsubscription, or a second one, delivered first. The
        // TrackSubscribed that follows within milliseconds is stale. Protected by PublicationsLock.
        private readonly Dictionary<string, long> _unsubscribedBeforeSubscribed =
            new Dictionary<string, long>();

        // A TrackSubscribed later than this is a new subscription, not the stale half of a race
        private static readonly long UnsubscribedBeforeSubscribedWindow = Stopwatch.Frequency * 10;

        internal RemoteParticipant(FfiHandle handle, ParticipantInfo? info, Room room)
            : base(handle, info, room) { }

        /// <summary>
        /// Records an unsubscription of a track the room has no subscription for.
        /// </summary>
        internal void MarkUnsubscribedBeforeSubscribed(string trackSid)
        {
            lock (PublicationsLock)
            {
                _unsubscribedBeforeSubscribed[trackSid] = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Whether a TrackSubscribed is the stale half of an unsubscription delivered before it.
        /// Consumes the record.
        /// </summary>
        internal bool TakeUnsubscribedBeforeSubscribed(string trackSid)
        {
            lock (PublicationsLock)
            {
                if (!_unsubscribedBeforeSubscribed.Remove(trackSid, out var at))
                    return false;
                return Stopwatch.GetTimestamp() - at <= UnsubscribedBeforeSubscribedWindow;
            }
        }

        internal void AddPublication(OwnedTrackPublication pub)
        {
            if (pub.Handle == null || pub.Info == null)
                return;

            var publication = new RemoteTrackPublication(
                FfiHandle.FromId(pub.Handle.Id),
                pub.Info,
                this
            );

            lock (PublicationsLock)
            {
                _trackPublications[publication.Sid] = publication;
            }
        }

        internal void RemovePublication(string sid)
        {
            lock (PublicationsLock)
            {
                _trackPublications.Remove(sid);
            }
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
        /// Preferred codec for video tracks (VP8, H264, AV1, VP9...). When
        /// null, the SDK default codec (VP8) is used.
        /// </summary>
        public Proto.VideoCodec? VideoCodec { get; set; }

        /// <summary>
        /// Whether to enable DTX (discontinuous transmission) for audio
        /// tracks. When null, the SDK default (enabled) is used.
        /// </summary>
        public bool? Dtx { get; set; }

        /// <summary>
        /// Whether to enable RED (redundant encoding) for audio tracks. When
        /// null, the SDK default (enabled) is used.
        /// </summary>
        public bool? Red { get; set; }

        /// <summary>
        /// Whether to publish video in several quality layers, so each subscriber receives the one it can
        /// handle. Defaults to true; must be false for pre-encoded tracks.
        /// </summary>
        public bool Simulcast { get; set; } = true;

        /// <summary>
        /// Track source.
        /// </summary>
        public Proto.TrackSource Source { get; set; } = Proto.TrackSource.SourceUnknown;

        /// <summary>
        /// Name of the stream the track belongs to, used to group tracks
        /// together (e.g. camera and screen share with their audios). When
        /// null, the server infers it from the track source.
        /// </summary>
        public string? Stream { get; set; }

        /// <summary>
        /// Whether to use the preconnect audio buffer for this track. When
        /// null, the SDK default (disabled) is used.
        /// </summary>
        public bool? PreconnectBuffer { get; set; }

        /// <summary>
        /// Per-frame metadata features to enable for this track (user
        /// timestamps, frame IDs, user data...).
        /// </summary>
        public IList<Proto.FrameMetadataFeature>? FrameMetadataFeatures { get; set; }

        /// <summary>
        /// RTP scalability mode (e.g. "L3T3_KEY"). When set, a single RTP
        /// encoding is produced with this mode, enabling true SVC for codecs
        /// that support it (VP9, AV1). Has no effect for VP8/H264.
        /// </summary>
        public string? ScalabilityMode { get; set; }

        /// <summary>
        /// Preferred encoder backend to use when publishing a video track.
        /// When null, the backend is chosen automatically.
        /// </summary>
        public Proto.VideoEncoderBackend? VideoEncoder { get; set; }

        /// <summary>
        /// Controls how the encoder trades off between resolution and
        /// framerate when bandwidth is constrained. When null, the SDK default
        /// (maintain resolution) is used.
        /// </summary>
        public Proto.DegradationPreference? DegradationPreference { get; set; }

        internal Proto.TrackPublishOptions ToProto()
        {
            var proto = new Proto.TrackPublishOptions { Source = Source, Simulcast = Simulcast };

            if (VideoCodec != null)
            {
                proto.VideoCodec = VideoCodec.Value;
            }

            if (Dtx != null)
            {
                proto.Dtx = Dtx.Value;
            }

            if (Red != null)
            {
                proto.Red = Red.Value;
            }

            if (Stream != null)
            {
                proto.Stream = Stream;
            }

            if (PreconnectBuffer != null)
            {
                proto.PreconnectBuffer = PreconnectBuffer.Value;
            }

            if (FrameMetadataFeatures != null)
            {
                proto.FrameMetadataFeatures.AddRange(FrameMetadataFeatures);
            }

            if (ScalabilityMode != null)
            {
                proto.ScalabilityMode = ScalabilityMode;
            }

            if (VideoEncoder != null)
            {
                proto.VideoEncoder = VideoEncoder.Value;
            }

            if (DegradationPreference != null)
            {
                proto.DegradationPreference = DegradationPreference.Value;
            }

            if (AudioEncoding != null)
            {
                proto.AudioEncoding = new Proto.AudioEncoding
                {
                    MaxBitrate = AudioEncoding.MaxBitrate,
                };
            }

            if (VideoEncoding != null)
            {
                proto.VideoEncoding = new Proto.VideoEncoding
                {
                    MaxBitrate = VideoEncoding.MaxBitrate,
                    MaxFramerate = VideoEncoding.MaxFramerate,
                };
            }

            return proto;
        }
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
        /// Whether to send the data on the reliable channel, which retransmits lost packets and keeps their
        /// order. Defaults to true; false uses the lossy channel.
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
