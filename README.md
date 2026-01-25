[Livekit.Server.Sdk.Dotnet](#livekitserversdkdotnet) [![NuGet Version](https://img.shields.io/nuget/v/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet) [![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Server.Sdk.Dotnet?groupby=Version) | [Livekit.Rtc.Dotnet](#livekitrtcdotnet) [![NuGet Version](https://img.shields.io/nuget/v/Livekit.Rtc.Dotnet)](https://www.nuget.org/packages/Livekit.Rtc.Dotnet) [![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Rtc.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Rtc.Dotnet?groupby=Version)

[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/pabloFuente/livekit-server-sdk-dotnet/dotnet.yml)](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml)
[![License badge](https://img.shields.io/badge/license-Apache2-orange.svg)](http://www.apache.org/licenses/LICENSE-2.0)

# LiveKit .NET SDKs <!-- omit in toc -->

Use this SDK to add realtime video, audio and data features to your .NET app. By connecting to LiveKit Cloud or a self-hosted server, you can quickly build applications such as multi-modal AI, live streaming, or video calls with just a few lines of code.

- [Packages](#packages)
  - [Livekit.Server.Sdk.Dotnet](#livekitserversdkdotnet)
  - [Livekit.Rtc.Dotnet](#livekitrtcdotnet)
- [Quick Start](#quick-start)
  - [Livekit.Server.Sdk.Dotnet - Access Tokens & Server APIs](#livekitserversdkdotnet---access-tokens--management-apis)
  - [Livekit.Rtc.Dotnet - Real-Time Media](#livekitrtcdotnet---real-time-media)
- [For Developers](#for-developers)
  - [Clone repository](#clone-repository)
  - [Build and test](#build-and-test)
  - [Perform release](#perform-release)
  - [Upgrade version of `livekit/protocol`](#upgrade-version-of-livekitprotocol)

## Packages

This repository contains two complementary .NET packages for different LiveKit use cases:

### Livekit.Server.Sdk.Dotnet

[![NuGet Version](https://img.shields.io/nuget/v/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Server.Sdk.Dotnet?groupby=Version)

**Server-side management and authentication SDK** for LiveKit. Use it to interact with all LiveKit server APIs, create access tokens, and handle webhooks.

- **Target Framework:** `netstandard2.0`

- **[Full Documentation →](LivekitApi/README.md)**

### Livekit.Rtc.Dotnet

[![NuGet Version](https://img.shields.io/nuget/v/Livekit.Rtc.Dotnet)](https://www.nuget.org/packages/Livekit.Rtc.Dotnet)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Rtc.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Rtc.Dotnet?groupby=Version)

**Real-time communication SDK** for .NET server applications. Use it to connect to LiveKit as a server-side participant, and to publish and subscribe to audio, video, and data.

- **Target Framework:** `netstandard2.1`

- **Included Native Binaries:** Precompiled for Windows, Linux, macOS (x64 & ARM64)

- **[Full Documentation →](LivekitRtc/README.md)**

## Quick Start

### Livekit.Server.Sdk.Dotnet - Access Tokens & Server APIs

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

Releases are automated through GitHub Actions thanks to the [publish workflow](.github/workflows/publish.yml). Both packages `Livekit.Server.Sdk.Dotnet` and `Livekit.Rtc.Dotnet` can be released independently. Checkout how to do so:

- [Release Livekit.Server.Sdk.Dotnet](LivekitApi/README.md#perform-release)
- [Release Livekit.Rtc.Dotnet](LivekitRtc/README.md#perform-release)

### Upgrade version of `livekit/protocol`

To upgrade the version of the `livekit/protocol` Git submodule:

```bash
cd protocol
git fetch --all
git checkout <COMMIT_HASH/TAG/BRANCH>
cd ..
git add protocol
git commit -m "Update livekit/protocol to <VERSION>"
git push
```

Then it may be necessary to re-generate the proto files to actually reflect the changes in livekit/protocol:

```bash
./generate_proto.sh
```

Then try packaging the SDK to test the validity of the changes in the protocol:

```bash
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
```

This command may throw an error if there are breaking changes in the protocol, as the SDK is configured in strict mode for [package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview). The way to overcome these breaking changes is running the package command with option `-p:GenerateCompatibilitySuppressionFile=true` to generate file `CompatibilitySuppressions.xml`:

```bash
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:GenerateCompatibilitySuppressionFile=true
```

This compatibility suppression file will allow packaging and publishing the SDK even with breaking changes. Once the new version is available in NuGet, the only thing left is to update in file `LivekitApi.csproj` property `<PackageValidationBaselineVersion>X.Y.Z</PackageValidationBaselineVersion>` to the new version (so the new reference for breaking changes is this new version), and delete `CompatibilitySuppressions.xml` (as it is no longer needed). Workflow [publish.yml](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/publish.yml) automatically does this as last step.

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
