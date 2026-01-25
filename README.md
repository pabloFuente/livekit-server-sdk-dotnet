[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/pabloFuente/livekit-server-sdk-dotnet/dotnet.yml)](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml)
[![License badge](https://img.shields.io/badge/license-Apache2-orange.svg)](http://www.apache.org/licenses/LICENSE-2.0)

[Livekit.Server.Sdk.Dotnet](#livekitserversdkdotnet) - [![NuGet Version](https://img.shields.io/nuget/v/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet) [![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Server.Sdk.Dotnet?groupby=Version)

[Livekit.Rtc.Dotnet](#livekitrtcdotnet) &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;- [![NuGet Version](https://img.shields.io/nuget/v/Livekit.Rtc.Dotnet)](https://www.nuget.org/packages/Livekit.Rtc.Dotnet) [![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Rtc.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Rtc.Dotnet?groupby=Version)

# LiveKit .NET SDKs <!-- omit in toc -->

Use this SDK to add realtime video, audio and data features to your .NET app. By connecting to LiveKit Cloud or a self-hosted server, you can quickly build applications such as multi-modal AI, live streaming, or video calls with just a few lines of code.

- [Packages](#packages)
  - [Livekit.Server.Sdk.Dotnet](#livekitserversdkdotnet)
  - [Livekit.Rtc.Dotnet](#livekitrtcdotnet)
- [Quick Start](#quick-start)
  - [Livekit.Server.Sdk.Dotnet - Access Tokens & Management APIs](#livekitserversdkdotnet---access-tokens--management-apis)
  - [Livekit.Rtc.Dotnet - Real-Time Media](#livekitrtcdotnet---real-time-media)
- [For Developers](#for-developers)
  - [Clone repository](#clone-repository)
  - [Build and test](#build-and-test)
  - [Perform release](#perform-release)

## Packages

This repository contains two complementary .NET packages for different LiveKit use cases:

### Livekit.Server.Sdk.Dotnet

[![NuGet Version](https://img.shields.io/nuget/v/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Server.Sdk.Dotnet?groupby=Version)

**Server-side management and authentication SDK** for LiveKit. Use it to interact with all LiveKit server APIs, create access tokens, and handle webhooks.

**Target Framework:** `netstandard2.0`

**[Full Documentation →](LivekitApi/README.md)**

### Livekit.Rtc.Dotnet

[![NuGet Version](https://img.shields.io/nuget/v/Livekit.Rtc.Dotnet)](https://www.nuget.org/packages/Livekit.Rtc.Dotnet)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Rtc.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Rtc.Dotnet?groupby=Version)

**Real-time communication SDK** for .NET server applications. Use it to connect to LiveKit as a server-side participant, and to publish and subscribe to audio, video, and data.

**Target Framework:** `netstandard2.1`

**Included Native Binaries:** Precompiled for Windows, Linux, macOS (x64 & ARM64)

**[Full Documentation →](LivekitRtc/README.md)**

## Quick Start

### Livekit.Server.Sdk.Dotnet - Access Tokens & Management APIs

```bash
dotnet add package Livekit.Server.Sdk.Dotnet
```

```csharp
using Livekit.Server.Sdk.Dotnet;
// Generate access token
var token = new AccessToken("api-key", "api-secret")
    .WithIdentity("user-123")
    .WithName("John Doe")
    .WithGrants(new VideoGrants { RoomJoin = true, Room = "my-room" });
var jwt = token.ToJwt();

// Manage rooms via API
var roomClient = new RoomServiceClient("https://my.livekit.instance", "api-key", "api-secret");
var rooms = await roomClient.ListRooms(new ListRoomsRequest());
```

### Livekit.Rtc.Dotnet - Real-Time Media

```bash
dotnet add package Livekit.Rtc.Dotnet
```

```csharp
using LiveKit.Rtc;

// Connect to room as a participant
var room = new Room();

// Add room event handlers
room.ParticipantConnected += (sender, participant) =>
{
    Console.WriteLine($"[ParticipantConnected] {participant.Identity}}");
};

await room.ConnectAsync("wss://my.livekit.instance", token);

// Publish audio track
var audioSource = new AudioSource(48000, 1);
var audioTrack = LocalAudioTrack.Create("audio", audioSource);
await room.LocalParticipant!.PublishTrackAsync(audioTrack);

// Handle events
room.TrackSubscribed += (sender, e) => {
    Console.WriteLine($"Subscribed to track: {e.Track.Sid}");
};
```

## For Developers

### Clone repository

```bash
git clone --recurse-submodules https://github.com/pabloFuente/livekit-server-sdk-dotnet.git
cd livekit-server-sdk-dotnet
```

> **Note:** The `--recurse-submodules` flag is required to clone the necessary LiveKit protocol submodules.

### Build and test

```bash
# Build both packages
dotnet build

# Run tests for Server SDK
dotnet test LivekitApi.Tests/LivekitApi.Tests.csproj

# Run tests for RTC SDK
dotnet test LivekitRtc.Tests/LivekitRtc.Tests.csproj

# Build and install both packages locally
./build_local.sh
```

### Perform release

Releases are automated through GitHub Actions:

1. **Livekit.Server.Sdk.Dotnet:** Create a tag with format `api-X.Y.Z`
   ```bash
   git tag api-1.2.0
   git push origin api-1.2.0
   ```

2. **Livekit.Rtc.Dotnet:** Create a tag with format `rtc-X.Y.Z`
   ```bash
   git tag rtc-0.1.0
   git push origin rtc-0.1.0
   ```

The [publish workflow](.github/workflows/publish.yml) will automatically build, pack, and publish the appropriate package to NuGet.org.

---

<div align="center">
  
**Built with ❤️ by [pabloFuente](https://github.com/pabloFuente)**

</div>

<!--BEGIN_REPO_NAV-->

<br/><table>

<thead><tr><th colspan="2">LiveKit Ecosystem</th></tr></thead>
<tbody>
<tr><td>LiveKit SDKs</td><td><a href="https://github.com/livekit/client-sdk-js">Browser</a> · <a href="https://github.com/livekit/client-sdk-swift">iOS/macOS/visionOS</a> · <a href="https://github.com/livekit/client-sdk-android">Android</a> · <a href="https://github.com/livekit/client-sdk-flutter">Flutter</a> · <a href="https://github.com/livekit/client-sdk-react-native">React Native</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <a href="https://github.com/livekit/client-sdk-unity">Unity</a> · <a href="https://github.com/livekit/client-sdk-unity-web">Unity (WebGL)</a> · <a href="https://github.com/livekit/client-sdk-esp32">ESP32</a> · <b>.NET (community)</b></td></tr><tr></tr>
<tr><td>Server APIs</td><td><a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/server-sdk-go">Golang</a> · <a href="https://github.com/livekit/server-sdk-ruby">Ruby</a> · <a href="https://github.com/livekit/server-sdk-kotlin">Java/Kotlin</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/agence104/livekit-server-sdk-php">PHP (community)</a> · <b>.NET (community)</b></td></tr><tr></tr>
<tr><td>UI Components</td><td><a href="https://github.com/livekit/components-js">React</a> · <a href="https://github.com/livekit/components-android">Android Compose</a> · <a href="https://github.com/livekit/components-swift">SwiftUI</a> · <a href="https://github.com/livekit/components-flutter">Flutter</a></td></tr><tr></tr>
<tr><td>Agents Frameworks</td><td><a href="https://github.com/livekit/agents">Python</a> · <a href="https://github.com/livekit/agents-js">Node.js</a> · <a href="https://github.com/livekit/agent-playground">Playground</a></td></tr><tr></tr>
<tr><td>Services</td><td><a href="https://github.com/livekit/livekit">LiveKit server</a> · <a href="https://github.com/livekit/egress">Egress</a> · <a href="https://github.com/livekit/ingress">Ingress</a> · <a href="https://github.com/livekit/sip">SIP</a></td></tr><tr></tr>
<tr><td>Resources</td><td><a href="https://docs.livekit.io">Docs</a> · <a href="https://github.com/livekit-examples">Example apps</a> · <a href="https://livekit.io/cloud">Cloud</a> · <a href="https://docs.livekit.io/home/self-hosting/deployment">Self-hosting</a> · <a href="https://github.com/livekit/livekit-cli">CLI</a></td></tr>
</tbody>
</table>
<!--END_REPO_NAV-->
