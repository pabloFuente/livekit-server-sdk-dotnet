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
- [Example apps](#example-apps)
- [For developers of the SDK](#for-developers-of-the-sdk)
  - [Clone repository](#clone-repository)
  - [Run tests](#run-tests)
  - [Building from source](#building-from-source)
  - [Perform release](#perform-release)

## Design principles

This library is crafted based on three core principles:

1. Livekit.Rtc.Dotnet uses the **C# auto-generated protobuf implementation of LiveKit RTC protocol**. This ensures compatibility and ease of maintenance as the API evolves.
2. Livekit.Rtc.Dotnet uses **C# FFI bindings to the LiveKit Rust SDK**. This allows us to leverage existing, well-tested code for WebRTC functionality that is already in use in other LiveKit SDKs. It brings the benefits of native performance for any OS platform while providing a common .NET interface. The library includes precompiled native binaries for Windows, Linux, and macOS, for both x64 and ARM64 architectures.
3. Livekit.Rtc.Dotnet maintains **full feature parity with other LiveKit RTC SDKs** for the server side, especially the two more mature ones: [Node.js](https://github.com/livekit/node-sdks/tree/main/packages/livekit-rtc) and [Python](https://github.com/livekit/python-sdks/tree/main/livekit-rtc).

## Features

### Real-Time Media

Publish and subscribe to audio/video tracks with full programmatic control. Process frames in real-time, mix audio sources, and build custom media pipelines. Perfect for server-side processing.

### Data & Communication

Send reliable or lossy data messages, implement RPC request-response patterns, and build real-time chat with topic-based channels. Full control over data flow between participants.

### Room & Participant Management

Active speaker detection, transcription support, connection quality monitoring, and 25+ event types for comprehensive room state management.

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
await room.LocalParticipant!.PublishTrackAsync(videoTrack);

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

- [.NET 8.0+ SDK](https://dotnet.microsoft.com/download/dotnet)
- [protoc](https://github.com/protocolbuffers/protobuf/releases/latest)
- [Rust 1.85.0+](https://www.rust-lang.org/tools/install) (only if also building native libraries from source)

### 1. Get native FFI libraries

#### Option 1: download pre-compiled binaries from LiveKit Rust SDK releases

The easiest way to get the latest FFI binaries without building from source:

```bash
# From LivekitRtc directory
./download_ffi.sh          # Downloads latest version
./download_ffi.sh 0.12.44  # Or specify a version
```

This downloads pre-compiled binaries from [LiveKit Rust SDK releases](https://github.com/livekit/rust-sdks/releases) for all supported platforms (Windows, Linux, macOS on x64 and ARM64).

#### Option 2: building native libraries from source

If you want to modify the Rust/FFI code or use unreleased features. You will need:

- [.NET 8.0+ SDK](https://dotnet.microsoft.com/download/dotnet)
- [Rust 1.85.0+](https://www.rust-lang.org/tools/install)
- [protoc](https://github.com/protocolbuffers/protobuf/releases/latest)

```bash
# From LivekitRtc directory
git submodule update --init --recursive
cd rust-sdks/livekit-ffi
cargo build --release
```

After building, copy the native libraries from `target/release/` to the appropriate `runtimes/{rid}/native/` folders. Take into account that Rust builds for the host platform by default, so you may need to cross-compile for other platforms.

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

## Perform release
