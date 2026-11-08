# Changelog (Release Notes)

## Unreleased

- Fix a remote track leaking its native handle without any explanation when a `TrackSubscribed`
  event could not be applied: its participant was not found, or the track had no info or an
  unsupported kind. `Room.TrackSubscribed` was not raised, and the only trace was a later
  `[WARN] RoomEvent.TrackUnsubscribed: Track ... is not subscribed` line. The track is now
  released and the reason logged, as in
  `[WARN] RoomEvent.TrackSubscribed: Participant <identity> not found`. [PR #159](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/159)
- Fix every incoming text and byte data stream staying in native memory for the rest of the
  process: memory grew by the size of each stream received, whether or not a handler was
  registered for its topic. For every stream the native library also hands over a reader of its
  own, which the SDK neither used nor released. It is now released on arrival; streams are still
  delivered to `RegisterTextStreamHandler` and `RegisterByteStreamHandler` as before. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- Fix a `Room` connected again right after `DisconnectAsync` receiving no event at all, with
  `LocalParticipant` null and `RemoteParticipants` empty although `IsConnected` was true: the
  cleanup of the previous connection, which runs after `DisconnectAsync` returns, removed the new
  connection's state. It now only applies to the connection it belongs to. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- `Room.ConnectAsync` called while another call is connecting the same room now throws
  `InvalidOperationException`. Both calls used to join, as two connections with the same identity:
  the server dropped the earlier one, which the room could no longer reach, or a call timed out
  after 30 s. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- `Room.DisconnectAsync`, `Disconnect` and `Dispose` called concurrently now share one
  disconnection: a call that ran while another was finishing could throw
  `NullReferenceException` or `InvalidOperationException: FFI request failed: invalid handle`. The
  room is now also cleaned up when the native disconnect request fails. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- Fix a room event handler that throws hiding the event from the handlers subscribed after it,
  which were not called. Each handler now runs on its own, and a failure is logged as
  `[ERROR] Room.<Event> handler threw: <exception>` (it was `[ERROR] Room event handler exception`). [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- Fix `Room.Dispose`, `Room.Disconnect` and `AudioSource.CaptureFrame` deadlocking when called from
  a UI thread (WPF, WinForms, MAUI or any single-threaded `SynchronizationContext`): they block on
  asynchronous code that resumed on the blocked thread. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- Fix `AudioResampler.Dispose` racing with a `Push` or `Flush` on another thread: the output could
  be copied from native memory the release had just freed (garbage audio or a crash), or the call
  failed with `InvalidOperationException: FFI request failed: invalid handle`. `Dispose` now waits
  for the call in progress, and later calls throw `ObjectDisposedException`. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- Fix a `PublishTrackAsync` cancelled (or timed out) after the track was published stalling every
  later event of the room for 10 s, then logging
  `[WARN] RoomEvent.LocalTrackPublished: Publication ... not found` and
  `[WARN] RoomEvent.LocalTrackUnpublished: Publication ... not found`: the room waited for the
  cancelled call to register a publication it never would. The SDK still unpublishes such a track,
  and raises no `LocalTrackPublished`, `LocalTrackSubscribed` or `LocalTrackUnpublished` event for
  it, since the caller never received its publication. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- Fix `Room.TrackSubscribed` being raised, with no `TrackUnsubscribed` to follow, for a track
  that was already gone, after `[WARN] RoomEvent.TrackUnsubscribed: Track ... of participant
  ... is not subscribed`, when a participant left (or the room reconnected) while one of its
  tracks was being subscribed. The native library dispatches a track's subscription and
  unsubscription from different threads, so it can deliver the unsubscription first, twice, or
  after the participant left. The room now recognizes these orders: such a late subscription
  raises no event and its track is released, and the late events of a participant that left no
  longer log `[WARN] RoomEvent.TrackSubscribed: Participant ... not found`. [PR #163](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/163)
- An already cancelled `CancellationToken` now makes `Room.ConnectAsync`,
  `Room.SimulateScenarioAsync`, `AudioSource.CaptureFrameAsync`,
  `LocalParticipant.PublishTrackAsync`, `UnpublishTrackAsync` and `PublishDataTrackAsync` throw
  `OperationCanceledException` before doing anything, as is standard in .NET. They used to send
  their request first: `ConnectAsync` joined the room and then left it in the background,
  `PublishTrackAsync` published the track and then unpublished it, and the others went ahead
  (a cancelled capture even made the next captures fail while the source consumed it). A
  cancellation once the request is sent is unchanged. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- A room event that arrives while the native room is closing, after the `Room` disconnected, no
  longer leaks the native participant, publication, track or data buffer it carries: the room keeps
  releasing them until the native library ends the room's event stream. A `TrackPublished` event
  for an unknown participant releases its publication too, logged as
  `[WARN] RoomEvent.TrackPublished: Participant <identity> not found` like `TrackSubscribed`. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)
- The `cancellationToken` of `AudioSource.CaptureFrameAsync` is documented: it cancels the wait,
  not the capture, and a capture made before the source consumed the cancelled frame fails with
  `InvalidOperationException`. [PR #162](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/162)

## 0.1.7

- Update rust-sdks to [livekit-ffi/v0.13.4](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.13.4) [PR #148](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/148)
- Fix the process aborting when a remote participant publishes a transcription whose track SID
  is not a valid track SID (an empty string, for example): the native client panicked while
  handling it. The transcription is now dropped with a native warning, and
  `Room.TranscriptionReceived` is not raised for it. [PR #148](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/148)
- A failed native request no longer prints the whole request to stderr in its
  `failed to handle request` error line, so the key passed to a failing
  `KeyProvider.SetSharedKey` or `KeyProvider.SetKey` call stays out of the console and the
  logs. [PR #148](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/148)
- Publishing a video track with a `TrackPublishOptions.VideoCodec` that no encoder on the host
  can produce (H.265 without an H.265 encoder, for example) now publishes VP8 and logs the
  native warning `no video encoder on this host can produce H265; publishing VP8 instead`,
  instead of announcing a codec the host cannot encode. Pre-encoded publishing
  (`EncodedVideoSource` with `VideoEncoder = EncoderBackendPreEncoded`) no longer needs a local
  encoder for its codec, so pre-encoded H.265 works on any host. [PR #148](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/148)
- Fix `EncodedVideoFrame.Metadata` (sent when `TrackPublishOptions.FrameMetadataFeatures` is
  set) reaching subscribers on the wrong frame when frames share a timestamp or are captured
  within the same millisecond: the sender looked the metadata up by capture time truncated to
  the millisecond, so such frames carried another frame's metadata. [PR #148](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/148)
- Reconnection now spaces its ten attempts like the JavaScript client (waits of 0.3 s, 1.2 s,
  2.7 s, 4.8 s, then 7 s, plus up to 1 s of jitter), so a `Room` rides out a network outage of
  about 45 s before it gives up and raises `Disconnected`; the previous random waits could spend
  every attempt within a few seconds. No new attempt starts once reconnection has run for 60 s,
  so a room no longer stays in `Reconnecting` for minutes, and a join or reconnect whose
  response times out now closes its WebSocket instead of leaving it open. [PR #148](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/148)
- XML documentation comments for every public member, with parameter and return value
  descriptions on every method, for IntelliSense and for the new
  [API reference](https://pablofuente.github.io/livekit-server-sdk-dotnet/) generated with DocFX.
  [commit f5660c2](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f5660c2f0225936951568c0c3ab403175521063a)
- Fix wrong XML documentation: `LocalParticipant.PerformRpcAsync` waits 15 seconds for the response
  by default, not 10; `Room.E2EEStateChanged` fires on every encryption state change, not only on
  errors; this version never calls `FrameProcessor.OnStreamInfoUpdated` and `OnCredentialsUpdated`,
  and never throws `FfiException` or `TrackException` (a failed native request raises
  `InvalidOperationException`, a server error `RoomException`). The defaults of `RoomOptions`,
  `TrackPublishOptions.Simulcast`, `DataPublishOptions.Reliable`, `E2EEOptions` and
  `KeyProviderOptions` are now documented, as is what each `Dispose` releases, and `Room` gains a
  usage example. The API reference lists only the `LiveKit.Proto` types the public API uses.

## 0.1.6

- Fix the process terminating (`FFI Panic` followed by exit code 1) in two situations: calling
  `LocalParticipant.UnpublishTrackAsync` with something that is not a track SID (a track name,
  an empty string, a remote track's SID, which was always empty), which now throws
  `ArgumentException`; and cancelling or timing out `Room.ConnectAsync` once the FFI had
  connected, which left a handshake unanswered that the FFI waits 15 s for before panicking.
  The SDK now completes that handshake and leaves the room in the background. In the same
  spirit, a `PublishTrackAsync` cancelled after the track was published now unpublishes it
  instead of leaving a track on the server the SDK does not know about, and an FFI callback
  that races with a cancelled wait is kept for the reconciliation instead of being dropped. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `Room.Disconnected` and `ConnectionStateChanged` (with `ConnDisconnected`) being raised
  twice on every `DisconnectAsync` (once from the FFI's own disconnected event, once from the
  method), always with `UnknownReason`. They are raised once, with the real reason
  (`ClientInitiated`, `RoomDeleted`, `DuplicateIdentity`, `ParticipantRemoved`...), and
  `Room.LocalParticipant` and `Room.RemoteParticipants` stay readable inside the `Disconnected`
  handlers (they were cleared before the handlers ran). `ConnectAsync` no longer reports a
  spurious `ConnDisconnected` first, and `ConnectionStateChanged` carries the state at the time
  of the change: a slow handler used to make `ConnReconnecting` vanish from the sequence. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `Room.TrackUnsubscribed`, `TrackMuted` and `TrackUnmuted` being dropped (logged as
  `[WARN] RoomEvent.TrackUnsubscribed: Participant ... not found`) when the remote participant
  leaves the room: the lookup ran on the event chain after `ParticipantDisconnected` had already
  removed the participant. Remote track events are now resolved on arrival. `LocalTrackPublished`
  and `LocalTrackSubscribed` no longer poll for the publication inside the event chain (up to
  500 ms of stall per miss, and a dropped event under thread-pool starvation): they wait for
  `PublishTrackAsync` to register it. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `RemoteTrack.Sid`, `Name` and `IsMuted` never being set (the `TrackSubscribed` track
  always reported an empty SID), and local tracks reporting `TR_unknown` after
  `PublishTrackAsync` instead of their publication's SID. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix text data streams corrupting multi-byte characters (`€`, accented letters, CJK, emoji)
  that fell on a chunk boundary: chunks were cut every 15000 bytes regardless of UTF-8
  boundaries and decoded one by one, so a replacement character appeared at every boundary.
  Chunks are now split on character boundaries, and the reader joins a character split by
  another sender. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `TextStreamReader` and `ByteStreamReader` reads (`ReadAllAsync`, `await foreach`) waiting
  forever when the sender or the room disconnected before the stream was closed: they now throw
  `StreamException` once the buffered chunks are consumed. Text and byte stream handlers run on
  the thread pool instead of the FFI callback thread: a handler that blocked on `ReadAllAsync`
  deadlocked, and stalled every FFI event of the process. `TextStreamWriter.Dispose()` and
  `ByteStreamWriter.Dispose()` now send the trailer (`using var writer = ...` used to leave the
  remote reader open forever; only `DisposeAsync` closed it); new
  `CloseAsync(reason, attributes, CancellationToken)` overloads; `StreamInfo.Size` is reported
  for an announced size of 0. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `FrameCryptor.SetEnabled` and `SetKeyIndex` failing with
  `InvalidOperationException: FFI request failed: invalid handle`: the requests did not carry
  the track SID the FFI addresses the cryptor by. `FrameCryptor` now exposes `TrackSid` (set by
  `E2EEManager.GetFrameCryptors`) and has a constructor overload taking it. New
  `KeyProviderOptions.KeyDerivationFunction` (defaults to PBKDF2, as before); a null
  `RatchetSalt` now falls back to the default salt instead of throwing. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `AudioSource.CaptureFrameAsync` throwing `TimeoutException` for frames longer than about
  10 s (a TTS clip, a file): the FFI only acknowledges a frame once it fits its queue, so the
  wait is now proportional to the frame. Fix `WaitForPlayoutAsync` never completing when another
  frame was captured meanwhile, and hanging after `Dispose`; cancelling one wait no longer
  cancels the others. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `AudioResampler.Flush()` returning a frame with zero samples before any audio was pushed,
  and ignoring resampling errors. `Push(AudioFrame)` now rejects frames with another channel
  count or sample rate (the output used to be garbage of half the duration), and push and flush
  are safe to call from several threads. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix `DataTrackStream.ReadAsync` throwing `ChannelClosedException` when the stream is disposed
  while a read is pending: it returns null, like at the end of the stream. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Fix RPC: `RpcError.RpcData` was `""` instead of null when the remote error carried no data;
  the message and the full stack trace of an exception thrown by an RPC handler were sent to
  the remote caller, now only the built-in `Application error in method handler` crosses the
  wire and the exception is logged locally with `[ERROR]`, as the Node and Python SDKs do; and
  `PerformRpcAsync` surfaces the FFI's `RpcError` with `ResponseTimeout` instead of a generic
  `TimeoutException` when `responseTimeout` elapses. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Media delivery: `AudioStream` bounded buffers (`capacity > 0`) drop the oldest frame instead
  of the newest when the consumer falls behind, like `VideoStream` and the Python SDK; a frame
  processor that throws is logged with `[WARN]` (`Debug.WriteLine` was silent in Release
  builds); audio and video frames and data packets are block-copied out of native memory (a
  byte loop on the FFI callback thread delayed every other event, 3 MB per 1080p frame); the
  frames the native side still emits right after a stream is disposed are released instead of
  leaked; and `AudioMixer` reads its sources concurrently (N stalled sources cost one timeout,
  not N) and keeps a source that stalls for one round instead of removing it for good. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Participants: new `Participant.Attributes` (kept current by `ParticipantAttributesChanged`,
  which left it stale), `Participant.State` and `Participant.DisconnectReason` (set before
  `ParticipantDisconnected` is raised). `Participant.TrackPublications` returns a snapshot:
  enumerating it while a track event was handled could throw `Collection was modified`.
  `LocalTrackPublication.WaitForSubscriptionAsync` continuations no longer run inline on the
  room's event chain (delaying the following events), and the wait is cancelled when the room
  disconnects instead of hanging forever. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)
- Rooms and the FFI client: a connected `Room` that is never disposed no longer stays
  referenced by the process-wide FFI client (it listens from `ConnectAsync` to the disconnect);
  `Room.Dispose()` now delivers the teardown events like `DisconnectAsync` does; the FFI event
  queue no longer fills up with media frames, which made every request wait scan a thousand
  stale events and could evict a pending callback (spurious `TimeoutException` under media
  load); one throwing event subscriber no longer hides the event from the other rooms and
  streams; and handler exceptions are logged with their type and stack trace, not only the
  message. `FfiClient.CopyBuffer` threw `EntryPointNotFoundException` (the native library has
  no such export) and is now implemented in managed code; `FfiClient.RetrievePtr` has the same
  problem and cannot be fixed (a pointer to a managed array is only valid while pinned), so it
  is marked `[Obsolete]`. [commit f2765fc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f2765fc14a0a5325be47fae3501a8fc866d53970)

## 0.1.5

- Update rust-sdks to [livekit-ffi/v0.13.3](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.13.3) ([commit 1001476](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/10014763a5212503352f2a39477ae86acf8add87))
- Fix process crash (native panic `index out of bounds: the len is 0 but the index is 0`) when
  converting or capturing video frames in the `I420A`, `I422`, `I444` and `I010` formats.
  `VideoFrame` only described the planes of `I420` and `NV12` buffers to the FFI, so the native
  side found no planes for the other planar formats. All planar formats are now described from a
  single plane table that is shared with `VideoFrame.GetPlane`, which also makes `GetPlane` work for
  `I010`. [commit 026961e](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/026961e5fa1367fdac9d1954db456cd2f374b4e3)
- Fix a possible native access violation (process crash) when capturing audio or video frames.
  The frame's managed sample/pixel array was only pinned while the pointer handed to the FFI was
  built, not while the capture request was serialized and sent, so a compacting garbage
  collection in between could move the array and leave the native side reading a stale pointer.
  `AudioSource.CaptureFrameAsync`, `VideoSource.CaptureFrame` and `VideoFrame.Convert` now keep
  the buffer (and, for planar video formats, every plane pointer) pinned through request
  serialization and the native call. [PR #143](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/143) (audio) and
  [PR #144](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/144) (video).
- Fix `Room.LocalTrackUnpublished` being dropped (logged as
  `[WARN] RoomEvent.LocalTrackUnpublished: Publication ... not found`) when the FFI
  `UnpublishTrack` callback was processed before the matching `LocalTrackUnpublished` room
  event. `LocalParticipant.UnpublishTrackAsync` now hands the removed publication over to the
  room event instead of dropping it, and the event handler no longer retries the lookup, which
  used to stall the room's FIFO event chain for up to 500ms per miss and delay every following
  event. Most visible when unpublishing many tracks at once. [commit 18d3966](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/18d3966a9c742e0f99a659b0d40ec0b538dd43f2)
- The SDK version that the FFI sends to the LiveKit server as the client SDK version now
  follows the package version in `LivekitRtc.csproj`. It was hard-coded to `0.1.0`.
  [commit 8ce5332](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/8ce53323aedfa79d35091d3824e5ea982c7c3a7c)
- The NuGet package now embeds this changelog as its release notes, as `Livekit.Server.Sdk.Dotnet`
  already did. [commit 880a38e](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/880a38e82783fa5a797e80f82af94f0286f0cf66)
- Data tracks, reaching parity with the Python RTC SDK: `LocalParticipant.PublishDataTrackAsync`
  returns a `LocalDataTrack` to push `DataTrackFrame`s on. Remote tracks arrive through the new
  `Room.DataTrackPublished` and `Room.DataTrackUnpublished` events (and are listed in
  `Room.RemoteDataTracks`); `RemoteDataTrack.Subscribe()` returns a `DataTrackStream` that can
  be read with `await foreach` and ends when the track is unpublished. [commit a3147e8](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a3147e8923ed76611550bd38050ed18588f6164b)
- New `EncodedVideoSource` to publish pre-encoded video (H.264, VP8, ...) without re-encoding it:
  `LocalVideoTrack.Create(name, encodedSource)`, `CaptureFrame(EncodedVideoFrame)` and
  `TakeFeedback()` for the key-frame and bitrate requests to forward to the encoder. [commit a3147e8](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a3147e8923ed76611550bd38050ed18588f6164b)
- New subscriber-side controls on `RemoteTrackPublication`: `SetVideoQuality` (simulcast layer),
  `SetEnabled` (pause receiving without unsubscribing) and `SetVideoDimensions`. [commit a3147e8](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a3147e8923ed76611550bd38050ed18588f6164b)
- New room events `ParticipantPermissionsChanged` (with the new `Participant.Permissions`
  property), `ParticipantActive`, `LocalTrackRepublished` (local publications are now updated in
  place and re-keyed after a full reconnect instead of going stale) and
  `DataChannelBufferedAmountLowThresholdChanged`, raised after the new
  `LocalParticipant.SetDataChannelBufferedAmountLowThreshold`. Bulk participant updates sent by
  the server are now applied to the participant objects. [commit a3147e8](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a3147e8923ed76611550bd38050ed18588f6164b)
- New `Room.SimulateScenarioAsync(Proto.SimulateScenarioKind)` to force the reconnection paths
  from tests without touching the network: a full reconnect (the SDK rebuilds its session and
  republishes its local tracks, raising `LocalTrackRepublished`), a resume, a server-initiated
  leave... It mirrors `Room.simulate_scenario` in the Python SDK and `Room.simulateScenario` in
  the Node SDK. See
  [Simulating Reconnections](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitRtc/README.md#simulating-reconnections). [commit a3147e8](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a3147e8923ed76611550bd38050ed18588f6164b)

## 0.1.4

- Update rust-sdks to [livekit-ffi/v0.12.76](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.12.76) ([commit 36077cc](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/36077cc273bd2a8c0952aafc818ca65c77e2af53))
- Add all the missing `TrackPublishOptions` fields, reaching parity with the Node/Python/Rust
  RTC SDKs: `VideoCodec` (select the codec of published video tracks: VP8, H264, AV1, VP9...),
  `Dtx`, `Red`, `Stream`, `PreconnectBuffer`, `FrameMetadataFeatures`, `ScalabilityMode`
  (SVC publishing for VP9/AV1), `VideoEncoder` (preferred encoder backend) and
  `DegradationPreference`. All new options are nullable and unset keeps the previous
  behavior (the SDK defaults). The `TrackPublishOptions` → `Proto.TrackPublishOptions`
  mapping is now covered by unit tests, including a parity guard that fails when a future
  FFI proto regeneration introduces fields not exposed by the SDK. [commit 3e3cfb4](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/3e3cfb459794d2b031d69bc58933bc833c2cb8d5)

## 0.1.3

- Update rust-sdks to [livekit-ffi/v0.12.60](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.12.60) ([commit 65e5fc4](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/65e5fc4accdd8060cc8994c8e672161a9c4d9bd2))

## 0.1.2

- Update rust-sdks to [livekit-ffi/v0.12.50](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.12.50) ([commit 5225cc6](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/5225cc60c9acaf98fbced1f11193b50f9cc2a228))

## 0.1.1

- Update livekit/protocol to [v1.45.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.45.1) ([commit b6f0054](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/b6f00545ef4f9c87f4b6e1915f140d7e0f6e512d))

## 0.1.0

- Initial release of Livekit.Rtc.Dotnet SDK. [commit d110d6c](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/d110d6c21d40b58f4af7f7f68f12e4d80d5dde93)
