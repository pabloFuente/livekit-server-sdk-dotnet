# Changelog (Release Notes)

## Unreleased

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

## 0.1.4

- Update rust-sdks to [livekit-ffi/v0.12.76](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.12.76)
- Add all the missing `TrackPublishOptions` fields, reaching parity with the Node/Python/Rust
  RTC SDKs: `VideoCodec` (select the codec of published video tracks: VP8, H264, AV1, VP9...),
  `Dtx`, `Red`, `Stream`, `PreconnectBuffer`, `FrameMetadataFeatures`, `ScalabilityMode`
  (SVC publishing for VP9/AV1), `VideoEncoder` (preferred encoder backend) and
  `DegradationPreference`. All new options are nullable and unset keeps the previous
  behavior (the SDK defaults). The `TrackPublishOptions` → `Proto.TrackPublishOptions`
  mapping is now covered by unit tests, including a parity guard that fails when a future
  FFI proto regeneration introduces fields not exposed by the SDK.

## 0.1.3

- Update rust-sdks to [livekit-ffi/v0.12.60](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.12.60)

## 0.1.2

- Update rust-sdks to [livekit-ffi/v0.12.50](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2Fv0.12.50)

## 0.1.1

- Update livekit/protocol to [v1.45.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.45.1)

## 0.1.0

- Initial release of Livekit.Rtc.Dotnet SDK.