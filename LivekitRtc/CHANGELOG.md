# Changelog (Release Notes)

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