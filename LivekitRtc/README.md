[![NuGet Version](https://img.shields.io/nuget/v/Livekit.Rtc.Dotnet)](https://www.nuget.org/packages/Livekit.Rtc.Dotnet)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Rtc.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Rtc.Dotnet?groupby=Version)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/pabloFuente/livekit-server-sdk-dotnet/dotnet.yml)](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml)
[![License badge](https://img.shields.io/badge/license-Apache2-orange.svg)](http://www.apache.org/licenses/LICENSE-2.0)

# Livekit.Rtc.Dotnet <!-- omit in toc -->

.NET SDK to integrate LiveKit's real-time video, audio, and data capabilities into your .NET applications using WebRTC. Access the same powerful API offered by client SDKs, but directly from your .NET server: connect to rooms as a participant, publish and subscribe to audio/video tracks, send and receive data messages, perform RPC calls, and more.

> [!NOTE]
> This SDK does not provide Server APIs to manage LiveKit resources such as Rooms, Egress, Ingress or SIP. If you want to manage LiveKit APIs from your .NET backend, please check [Livekit.Server.Sdk.Dotnet](../LivekitApi/README.md) instead.

- [Design principles](#design-principles)
- [Features](#features)
- [Installation](#installation)
- [Usage](#usage)
  - [Basic Connection](#basic-connection)
  - [Publishing Audio](#publishing-audio)
  - [Publishing Video](#publishing-video)
  - [Receiving Media](#receiving-media)
  - [Event Handling](#event-handling)
  - [End-to-End Encryption](#end-to-end-encryption)
  - [RPC Communication](#rpc-communication)
  - [Text and Byte Streams](#text-and-byte-streams)
  - [Data Tracks](#data-tracks)
  - [Publishing Pre-encoded Video](#publishing-pre-encoded-video)
  - [Controlling Remote Tracks](#controlling-remote-tracks)
  - [Simulating Reconnections](#simulating-reconnections)
- [Example apps](#example-apps)
- [For developers of the SDK](#for-developers-of-the-sdk)
  - [Clone repository](#clone-repository)
  - [Run tests](#run-tests)
  - [Building from source](#building-from-source)
  - [Upgrade version of `rust-sdks`](#upgrade-version-of-rust-sdks)
  - [Perform release](#perform-release)
  - [AI coding agents](#ai-coding-agents)

> [!TIP]
> API reference: https://pablofuente.github.io/livekit-server-sdk-dotnet/api-rtc/LiveKit.Rtc.html

## Design principles

This library is crafted based on three core principles:

1. Livekit.Rtc.Dotnet uses the **C# auto-generated protobuf implementation of LiveKit RTC protocol**. This ensures compatibility and ease of maintenance as the API evolves.
2. Livekit.Rtc.Dotnet uses **C# FFI bindings to the LiveKit Rust SDK**. This allows us to leverage existing, well-tested code for WebRTC functionality that is already in use in other LiveKit SDKs. It brings the benefits of native performance for any OS platform while providing a common .NET interface. The library includes precompiled native binaries for Windows, Linux, and macOS, for both x64 and ARM64 architectures.
3. Livekit.Rtc.Dotnet maintains **full feature parity with other LiveKit RTC SDKs** for the server side, especially the two more mature ones: [Node.js](https://github.com/livekit/node-sdks/tree/main/packages/livekit-rtc) and [Python](https://github.com/livekit/python-sdks/tree/main/livekit-rtc).

## Features

### Real-Time Media

Publish and subscribe to audio/video tracks with full programmatic control. Process frames in real-time, mix audio sources, publish pre-encoded video without re-encoding it, and build custom media pipelines. Perfect for server-side processing.

### Data & Communication

Send reliable or lossy data messages, stream application frames over data tracks, implement RPC request-response patterns, and build real-time chat with topic-based channels. Full control over data flow between participants.

### Room & Participant Management

Active speaker detection, transcription support, connection quality monitoring, permission changes, and 30+ event types for comprehensive room state management.

### Security & Encryption

Built-in end-to-end encryption (E2EE) with AES-GCM, key rotation, and per-participant key management. Keep your sensitive communications secure, also in your .NET server.

### Production-Ready

Automatic reconnection, multi-room support, isolated contexts, and battle-tested WebRTC implementation from the LiveKit Rust SDK.

## Installation

```bash
dotnet add package Livekit.Rtc.Dotnet
```

## Usage

### Basic Connection

```csharp
using LiveKit.Rtc;

var room = new Room();
await room.ConnectAsync("wss://your-livekit-server.com", "your-access-token",
    new RoomOptions { AutoSubscribe = true });

Console.WriteLine($"Connected to {room.Name}");
await room.DisconnectAsync();
```

### Publishing Audio

```csharp
var audioSource = new AudioSource(48000, 1); // 48kHz, mono
var audioTrack = LocalAudioTrack.Create("my-audio", audioSource);
var publication = await room.LocalParticipant!.PublishTrackAsync(audioTrack);

// Capture audio frames
var audioData = new short[480]; // 10ms at 48kHz
var audioFrame = new AudioFrame(audioData, 48000, 1, 480);
await audioSource.CaptureFrameAsync(audioFrame);
```

### Publishing Video

```csharp
var videoSource = new VideoSource(1920, 1080);
var videoTrack = LocalVideoTrack.Create("my-video", videoSource);
await room.LocalParticipant!.PublishTrackAsync(videoTrack, new TrackPublishOptions
{
    VideoCodec = Proto.VideoCodec.H264, // VP8 (default), H264, AV1, VP9...
    Simulcast = true,
    Source = Proto.TrackSource.SourceCamera,
});

// Capture video frames
var videoData = new byte[1920 * 1080 * 4]; // RGBA
var videoFrame = new VideoFrame(1920, 1080, Proto.VideoBufferType.Rgba, videoData);
videoSource.CaptureFrame(videoFrame);
```

### Receiving Media

```csharp
room.TrackSubscribed += async (sender, e) => {
    if (e.Track is RemoteVideoTrack videoTrack) {
        using var videoStream = new VideoStream(videoTrack);
        await foreach (var frame in videoStream.WithCancellation(cts.Token)) {
            // Process video frame
            Console.WriteLine($"Frame: {frame.Frame.Width}x{frame.Frame.Height}");
        }
    }
};
```

### Event Handling

```csharp
room.ParticipantConnected += (sender, participant) => {
    Console.WriteLine($"{participant.Identity} joined");
};

room.ActiveSpeakersChanged += (sender, e) => {
    Console.WriteLine($"Active speakers: {e.Speakers.Count}");
};

room.DataReceived += (sender, e) => {
    var message = Encoding.UTF8.GetString(e.Data);
    Console.WriteLine($"Data from {e.Participant.Identity}: {message}");
};

room.ParticipantPermissionsChanged += (sender, e) => {
    Console.WriteLine($"{e.Participant.Identity} can publish: {e.Permissions?.CanPublish}");
};

room.LocalTrackRepublished += (sender, e) => {
    // After a full reconnect the publication keeps its object but gets a new SID
    Console.WriteLine($"{e.PreviousSid} is now {e.Publication.Sid}");
};

room.ParticipantDisconnected += (sender, participant) => {
    Console.WriteLine($"{participant.Identity} left: {participant.DisconnectReason}");
};

room.Disconnected += (sender, reason) => {
    // ClientInitiated, RoomDeleted, DuplicateIdentity, ParticipantRemoved...
    // LocalParticipant and RemoteParticipants are still readable here
    Console.WriteLine($"Disconnected: {reason}");
};
```

### End-to-End Encryption

```csharp
var key = new byte[32]; // AES-256 key
var options = new RoomOptions {
    E2EE = new E2EEOptions {
        KeyProviderOptions = new KeyProviderOptions { SharedKey = key }
    }
};

await room.ConnectAsync(url, token, options);
// All tracks automatically encrypted/decrypted
```

### RPC Communication

```csharp
// Register RPC method handler
room.LocalParticipant!.RegisterRpcMethod("greet", async (data) => {
    return $"Hello, {data.CallerIdentity}!";
});

// Call RPC method on another participant
var response = await room.LocalParticipant!.PerformRpcAsync(
    destinationIdentity: "other-participant",
    method: "greet",
    payload: "{}",
    responseTimeout: 5
);
```

### Text and Byte Streams

```csharp
// Receiver: handlers run on the thread pool, so they may also read synchronously
room.RegisterTextStreamHandler("chat", async (reader, senderIdentity) => {
    try {
        var text = await reader.ReadAllAsync();
        Console.WriteLine($"{senderIdentity}: {text}");
    } catch (StreamException) {
        // The sender or the room disconnected before the stream was closed
    }
});

// Sender: multi-byte characters are never split across chunks
await using var writer = await room.LocalParticipant!.StreamTextAsync(topic: "chat");
await writer.WriteAsync("Hello 🎉"); // closed (trailer sent) when disposed, or with CloseAsync()
```

### Data Tracks

Data tracks stream application frames (telemetry, sensor data, game state...) from one publisher to any number of subscribers. Unlike data messages they are a named, subscribable track; frames are not buffered for subscribers that join later.

```csharp
// Publisher
var dataTrack = await room.LocalParticipant!.PublishDataTrackAsync("telemetry");
dataTrack.TryPush(new DataTrackFrame(payload, userTimestamp: 42)); // PushFrameException if it cannot be sent
dataTrack.Unpublish();

// Subscriber
room.DataTrackPublished += async (sender, remoteTrack) => {
    Console.WriteLine($"{remoteTrack.PublisherIdentity} published {remoteTrack.Info.Name}");
    using var stream = remoteTrack.Subscribe();
    await foreach (var frame in stream) { // ends when the track is unpublished
        Console.WriteLine($"{frame.Payload.Length} bytes, timestamp {frame.UserTimestamp}");
    }
};
room.DataTrackUnpublished += (sender, e) => Console.WriteLine($"{e.Sid} unpublished");
```

### Publishing Pre-encoded Video

Publish video that is already encoded (for example H.264 from a hardware encoder or a demuxed file) without decoding and re-encoding it:

```csharp
using var source = new EncodedVideoSource(1280, 720);
var track = LocalVideoTrack.Create("camera", source);
await room.LocalParticipant!.PublishTrackAsync(track, new TrackPublishOptions {
    VideoCodec = Proto.VideoCodec.H264,
    VideoEncoder = Proto.VideoEncoderBackend.EncoderBackendPreEncoded,
    Simulcast = false,
});

// Feed complete access units at the playback rate (Annex B for H.264/H.265)
source.CaptureFrame(new EncodedVideoFrame(
    accessUnit, 1280, 720, Proto.VideoCodec.H264, Proto.EncodedFrameType.EncodedFrameKey, timestampUs));

// Forward encoder feedback (key frame requests, bitrate targets) to your encoder
var feedback = source.TakeFeedback();
if (feedback.KeyframeRequested) encoder.RequestKeyFrame();
```

### Controlling Remote Tracks

```csharp
room.TrackSubscribed += (sender, e) => {
    var publication = e.Publication;
    publication.SetVideoQuality(Proto.VideoQuality.Low); // simulcast layer (video only)
    publication.SetVideoDimensions(640, 360);            // let the server pick the layer for this size
    publication.SetEnabled(false);                       // pause receiving without unsubscribing
};
```

### Simulating Reconnections

`Room.SimulateScenarioAsync` forces, from a test, the reconnection paths that are otherwise only reachable through network failures. The task completes once the SDK has started the scenario; the reconnection itself is observed through the room events.

```csharp
// Full reconnect: the SDK rebuilds its session and republishes the local tracks.
// Raises Reconnecting, then one LocalTrackRepublished per local track, then Reconnected.
await room.SimulateScenarioAsync(Proto.SimulateScenarioKind.SimulateFullReconnect);

// Resume: only the signaling connection is re-established and the publications are kept.
await room.SimulateScenarioAsync(Proto.SimulateScenarioKind.SimulateSignalReconnect);
```

See `Proto.SimulateScenarioKind` for the other variants (server-initiated leave, node failure, migration...).

# Example apps

At [LivekitRtc.Example](https://github.com/pabloFuente/livekit-server-sdk-dotnet/tree/main/LivekitRtc.Example) you can find several example applications demonstrating the usage of LiveKit.Rtc.Dotnet SDK. See the [examples README](https://github.com/pabloFuente/livekit-server-sdk-dotnet/tree/main/LivekitRtc.Example/README.md) for more details.

# For developers of the SDK

## Clone repository

Make sure to clone with submodule option:

```bash
git clone --recurse-submodules https://github.com/pabloFuente/livekit-server-sdk-dotnet.git
```

## Run tests

All E2E tests automatically launch necessary services as Docker containers with [Testcontainers](https://testcontainers.com/).

```bash
dotnet test LivekitRtc.Tests
```

## Building from source

Prerequisites:

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` pins it; the package itself targets `netstandard2.1`)
- [protoc](https://github.com/protocolbuffers/protobuf/releases/latest)
- [Rust](https://www.rust-lang.org/tools/install) (only if also building native libraries from source)

### 1. Get native FFI libraries

#### Option 1: download pre-compiled binaries from LiveKit Rust SDK releases

The easiest way to get the latest FFI binaries without building from source:

```bash
# From LivekitRtc directory
./download_ffi.sh          # Downloads latest version
./download_ffi.sh 0.12.44  # Or specify a version
```

This downloads pre-compiled binaries from [LiveKit Rust SDK releases](https://github.com/livekit/rust-sdks/releases) for all supported platforms (Windows, Linux, macOS on x64 and ARM64). It needs `curl`, `unzip` and `jq`. Each archive is checked against the SHA-256 digest that GitHub lists for that release asset, and the binaries are copied only when all six match: otherwise the script fails and leaves the previous ones in place.

#### Option 2: building native libraries from source

If you want to modify the Rust/FFI code or use unreleased features. You will need:

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` pins it; the package itself targets `netstandard2.1`)
- [Rust](https://www.rust-lang.org/tools/install) installed with rustup. No specific version needs to be installed beforehand: `rust-sdks` pins its toolchain in file `rust-toolchain.toml` (e.g. Rust 1.97.1 for `livekit-ffi/v0.13.4`), and rustup installs it automatically the first time `cargo` runs inside `rust-sdks`.
- [protoc](https://github.com/protocolbuffers/protobuf/releases/latest)
- The platform-specific build dependencies listed in the [rust-sdks README](https://github.com/livekit/rust-sdks#building) (e.g. clang 21+ and some system libraries on Linux).

The Rust build needs the nested submodules of `rust-sdks` (`livekit-protocol/protocol` and `yuv-sys/libyuv`). Option `--checkout` makes sure they are checked out, even if they have been disabled locally (see the note below):

```bash
# From the root directory (livekit-server-sdk-dotnet/.)
git submodule update --init --recursive --checkout LivekitRtc/rust-sdks
cd LivekitRtc/rust-sdks/livekit-ffi
cargo build --release
```

The native library is generated in the workspace target folder `LivekitRtc/rust-sdks/target/release/` (`liblivekit_ffi.so` on Linux, `liblivekit_ffi.dylib` on macOS, `livekit_ffi.dll` on Windows). Copy it to the appropriate `runtimes/{rid}/native/` folder. For example, on Linux x64:

```bash
# From LivekitRtc directory
cp rust-sdks/target/release/liblivekit_ffi.so runtimes/linux-x64/native/
```

Take into account that Rust builds for the host platform by default, so you may need to cross-compile for other platforms.

### 2. Generate Protocol Buffers

```bash
# From the root directory (livekit-server-sdk-dotnet/.)
./generate_proto.sh
```

### 3. Build the .NET SDK

```bash
# From LivekitRtc directory
dotnet build
```

## Upgrade version of `rust-sdks`

> [!TIP]
> The `/sync-rust-sdks` skill runs this whole procedure, including exposing the new FFI features, and the [sync-rust-sdks workflow](../.github/workflows/sync-rust-sdks.yml) runs it on every new GA `livekit-ffi` release and opens a pull request. See [AI coding agents](#ai-coding-agents).

To upgrade the version of the `rust-sdks` Git submodule:

```bash
cd LivekitRtc/rust-sdks
git fetch --all
git checkout <VERSION_TAG> # e.g. git checkout livekit-ffi/v0.12.76
cd ../..
./generate_proto.sh
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
./download_ffi.sh <VERSION_TAG> # e.g. ./download_ffi.sh livekit-ffi/v0.12.76
```

`dotnet pack` runs [package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview) against the last release (`PackageValidationBaselineVersion` in `LivekitRtc.csproj`), and so does the `build` check of every pull request. The generated `LiveKit.Proto` types are public, so a new FFI version that removes or changes one of their members makes it fail. Record those breaks with `dotnet pack LivekitRtc -c Debug -p:GenerateCompatibilitySuppressionFile=true`, which writes `LivekitRtc/CompatibilitySuppressions.xml`, commit that file and explain each break in the changelog under `Breaking changes coming from rust-sdks`. The [publish workflow](../.github/workflows/publish.yml) moves the baseline and deletes the file after the release.

The `build` check also downloads the binaries of the `livekit-ffi` tag the submodule points to and fails unless the committed ones are identical, so commit the submodule pointer and the six binaries of the same version together.

Then commit:

```bash
git commit -m "Update rust-sdks to <VERSION>"
git push
```

## Perform release

1. Create a commit [like this](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/c3c813904cb45745249294450fda406c56f293e5) updating the version.
2. Make sure to run script `./build_local.sh` to properly format all files with csharpier. Commit any changes in the repo after the script finishes.
3. Create a [new release in GitHub](https://github.com/pabloFuente/livekit-server-sdk-dotnet/releases/new) with:
   - Release title `rtc-X.Y.Z`
   - A new tag `rtc-X.Y.Z`
   - Description with relevant changes (e.g., `Update to LiveKit Rust SDK vA.B.C`)

> [!IMPORTANT]
> The **`rtc-`** prefix in the tag is mandatory for the publish workflow to identify that it is a release for package **Livekit.Rtc.Dotnet** specifically.
>
> After creating the release, workflow [publish.yml](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/publish.yml) will automatically publish the new version to NuGet and will perform the necessary post-release tasks.

## AI coding agents

[CLAUDE.md](../CLAUDE.md) briefs AI coding agents on this repository, including how the FFI layer works. For this package the relevant tool is the `/sync-rust-sdks` [Claude Code](https://claude.com/claude-code) skill ([SKILL.md](../.claude/skills/sync-rust-sdks/SKILL.md)), which carries out the [`rust-sdks` upgrade](#upgrade-version-of-rust-sdks) end to end:

1. Start from a clean working tree with `protoc` 36.2, `protoc-gen-twirpcs` v1.1.0 (`generate_proto.sh` also regenerates the server package), `gh`, `curl`, `unzip` and `jq` installed, and Docker running for the e2e tests.
2. Open Claude Code at the repository root and run the skill with the target `livekit-ffi` version, or without one to take the latest release:

   ```text
   /sync-rust-sdks 0.13.4
   ```

3. The skill checks that the GitHub release carries the six native binaries, moves the submodule, reads the upstream changelog and the FFI proto diff, regenerates `LivekitRtc/Proto` with `./generate_proto.sh`, downloads the binaries with `download_ffi.sh` (it asks before overwriting them), fixes the build and runs the parity guards. It then stops at a checkpoint listing the relevant upstream changes, the proto members that forced fixes, and every new FFI request, callback, room event and option field with a proposal to expose it (with its Node and Python name) or to ignore it. Strike what you do not want and confirm.
4. It exposes the confirmed features following the Node and Python RTC SDKs, with their mapping or e2e tests, README sections and changelog bullets under `## Unreleased`, then runs the no-Docker tests and the e2e classes for the areas the upstream diff touched, and builds and checks the API reference. Breaking changes forced by the FFI are recorded in `CompatibilitySuppressions.xml` and explained in the changelog under `Breaking changes coming from rust-sdks`.
5. It ends with the test results (any `FFI Panic` or `[WARN] RoomEvent` line means the binaries and the protos disagree), the open decisions and the suggested commits, and never commits. Review the diff, commit as `Update "rust-sdks" to livekit-ffi/vX.Y.Z` (the submodule pointer, `LivekitRtc/Proto`, the six binaries and any `generate_proto.sh` change) and `Adapt LivekitRtc to livekit-ffi/vX.Y.Z` (everything else), and follow [Perform release](#perform-release) when ready.

The [sync-rust-sdks workflow](../.github/workflows/sync-rust-sdks.yml) runs the same skill unattended: every day it looks for a GA `livekit-ffi` release newer than the submodule whose six binaries are published and, when it finds one, downloads the binaries, runs the skill and opens a pull request with those two commits and the skill's report as description. Instead of stopping at the checkpoint, it exposes the features that the Node or Python SDK exposes and lists the rest as open decisions. Its header lists the secrets and repository settings it needs, and it can be started by hand from the Actions tab to choose the Claude model and effort level.

A hook keeps `Proto/*.g.cs`, `runtimes/` and the submodule out of reach of agent edits. The coverage script also works on its own, from the repository root. It lists the `FfiRequest`, `FfiEvent` and `RoomEvent` cases that the hand-written sources do not send, read or handle yet:

```bash
python3 .claude/skills/sync-rust-sdks/scripts/ffi_coverage.py
```
