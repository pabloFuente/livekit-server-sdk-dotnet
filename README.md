# LiveKit .NET SDKs <!-- omit in toc -->

[![Build](https://img.shields.io/github/actions/workflow/status/pabloFuente/livekit-server-sdk-dotnet/dotnet.yml?branch=main&label=build)](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml) [![API](https://img.shields.io/badge/docs-API%20reference-blue)](https://pablofuente.github.io/livekit-server-sdk-dotnet/) [![License](https://img.shields.io/github/license/pabloFuente/livekit-server-sdk-dotnet)](LICENSE)

Use this SDK to add realtime video, audio and data features to your .NET app. By connecting to LiveKit Cloud or a self-hosted server, you can quickly build applications such as multi-modal AI, live streaming, or video calls with just a few lines of code.

| Package | NuGet | Downloads | Coverage | API reference |
| :-- | :-- | :-- | :-- | :-- |
| [Livekit.Server.Sdk.Dotnet](LivekitApi/README.md) | [![NuGet version](https://img.shields.io/nuget/v/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet) | [![NuGet downloads](https://img.shields.io/nuget/dt/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Server.Sdk.Dotnet?groupby=Version) | [![Line coverage](https://img.shields.io/endpoint?url=https%3A%2F%2Fpablofuente.github.io%2Flivekit-server-sdk-dotnet%2Fcoverage%2Fbadges%2FLivekitApi.json)](https://pablofuente.github.io/livekit-server-sdk-dotnet/coverage/) | [![API reference](https://img.shields.io/badge/docs-API%20reference-blue)](https://pablofuente.github.io/livekit-server-sdk-dotnet/api-server/Livekit.Server.Sdk.Dotnet.html) |
| [Livekit.Rtc.Dotnet](LivekitRtc/README.md) | [![NuGet version](https://img.shields.io/nuget/v/Livekit.Rtc.Dotnet)](https://www.nuget.org/packages/Livekit.Rtc.Dotnet) | [![NuGet downloads](https://img.shields.io/nuget/dt/Livekit.Rtc.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Rtc.Dotnet?groupby=Version) | [![Line coverage](https://img.shields.io/endpoint?url=https%3A%2F%2Fpablofuente.github.io%2Flivekit-server-sdk-dotnet%2Fcoverage%2Fbadges%2FLivekitRtc.json)](https://pablofuente.github.io/livekit-server-sdk-dotnet/coverage/) | [![API reference](https://img.shields.io/badge/docs-API%20reference-blue)](https://pablofuente.github.io/livekit-server-sdk-dotnet/api-rtc/LiveKit.Rtc.html) |

- [Packages](#packages)
  - [Livekit.Server.Sdk.Dotnet](#livekitserversdkdotnet)
  - [Livekit.Rtc.Dotnet](#livekitrtcdotnet)
- [Quick Start](#quick-start)
  - [Livekit.Server.Sdk.Dotnet - Access Tokens & Server APIs](#livekitserversdkdotnet---access-tokens--server-apis)
  - [Livekit.Rtc.Dotnet - Real-Time Media](#livekitrtcdotnet---real-time-media)
- [For Developers](#for-developers)
  - [Clone repository](#clone-repository)
  - [Build and test](#build-and-test)
  - [Code coverage](#code-coverage)
  - [Dependency security](#dependency-security)
  - [Perform release](#perform-release)
  - [Upgrade version of `livekit/protocol`](#upgrade-version-of-livekitprotocol)
  - [AI coding agents](#ai-coding-agents)

## Packages

This repository contains two complementary .NET packages for different LiveKit use cases:

### Livekit.Server.Sdk.Dotnet

**Server-side management and authentication SDK** for LiveKit. Use it to interact with all LiveKit server APIs, create access tokens, and handle webhooks.

- Target framework: `netstandard2.0`

- [Full documentation →](LivekitApi/README.md)

### Livekit.Rtc.Dotnet

**Real-time communication SDK** for .NET server applications. Use it to connect to LiveKit as a server-side participant, and to publish and subscribe to audio, video, and data.

- Target framework: `netstandard2.1`

- Included native binaries: precompiled for Windows, Linux, macOS (x64 & ARM64)

- [Full documentation →](LivekitRtc/README.md)

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
    Console.WriteLine($"[ParticipantConnected] {participant.Identity}");
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

# Build the API reference into docs/_site and check it (same as CI)
dotnet tool restore && dotnet docfx docs/docfx.json --warningsAsErrors && python3 docs/check_api_docs.py
```

Every public member of the two libraries needs a complete XML doc comment (summary, every
parameter, the return value): those comments are the published
[API reference](https://pablofuente.github.io/livekit-server-sdk-dotnet/), so the build fails
without them. A new public type of `Livekit.Server.Sdk.Dotnet` also needs an entry in the list of
SDK types of [docs/template/public/main.css](docs/template/public/main.css), which shows them in bold
with an "SDK" badge among the protobuf messages; the check script fails until it has one.

### Code coverage

```bash
# Every test with coverage (needs Docker), then the report and the coverage gate
./coverage.sh
# A subset: the report is built, but the gate (set for the full suite) fails
./coverage.sh LivekitApi.Tests --filter "Category=Unit"
```

The report is written to `coverage/report/index.html`, and the
[report of `main`](https://pablofuente.github.io/livekit-server-sdk-dotnet/coverage/) is published
next to the API reference. CI fails when coverage drops below the minimums in
[coverage/thresholds.json](coverage/thresholds.json): add tests rather than lowering them.

### Dependency security

`python3 security/check_vulnerabilities.py` (add `--sources all` for the native crates, which needs
the `rust-sdks` submodule and cargo) runs the vulnerability scan that CI runs. A critical or high
finding fails it until it is fixed or accepted in `security/policy.json`; see
[SECURITY.md](SECURITY.md) for what is scanned, the exception rules and how to report a
vulnerability.

### Perform release

Releases are automated through GitHub Actions thanks to the [publish workflow](.github/workflows/publish.yml). Both packages `Livekit.Server.Sdk.Dotnet` and `Livekit.Rtc.Dotnet` can be released independently. The workflow publishes with [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): nuget.org keeps a policy for this repository and `publish.yml`, scoped to the two package IDs, and the `NUGET_USER` repository secret holds the nuget.org user name; no API key is stored. After publishing, the workflow opens a pull request that moves the package validation baseline of the released package to the new version: approve its checks ("Approve workflows to run") and merge it before the next release of the same package. Checkout how to do so:

- [Release Livekit.Server.Sdk.Dotnet](LivekitApi/README.md#perform-release)
- [Release Livekit.Rtc.Dotnet](LivekitRtc/README.md#perform-release)

### Upgrade version of `livekit/protocol`

> [!TIP]
> The `/sync-protocol` skill runs this whole procedure, including wrapping the new RPCs, and the [sync-protocol workflow](.github/workflows/sync-protocol.yml) runs it on every new GA release and opens a pull request. See [AI coding agents](LivekitApi/README.md#ai-coding-agents) in the package README.

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

This command may throw an error if there are breaking changes in the protocol, as the SDK is configured in strict mode for [package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview). The `build` check of every pull request runs the same command, so it fails the same way. The way to overcome these breaking changes is running the package command with option `-p:GenerateCompatibilitySuppressionFile=true` to generate file `CompatibilitySuppressions.xml`:

```bash
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:GenerateCompatibilitySuppressionFile=true
```

This compatibility suppression file will allow packaging and publishing the SDK even with breaking changes. Once the new version is available in NuGet, the only thing left is to update in file `LivekitApi.csproj` property `<PackageValidationBaselineVersion>X.Y.Z</PackageValidationBaselineVersion>` to the new version (so the new reference for breaking changes is this new version), and delete `CompatibilitySuppressions.xml` (as it is no longer needed). Workflow [publish.yml](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/publish.yml) automatically opens a pull request that does this as last step.

### AI coding agents

The repository ships guidance and tooling for AI coding agents, so the procedures above can be delegated to them:

- [CLAUDE.md](CLAUDE.md) (also reachable as `AGENTS.md`) is the briefing an agent loads first: which files are generated and must never be edited by hand, how the submodules and the native binaries are coupled, the target framework constraints of each package, the build, format and test commands, and the changelog conventions.
- [.claude/settings.json](.claude/settings.json) pre-approves the routine commands (`dotnet build`, `test` and `pack`, CSharpier, `./generate_proto.sh`, read-only git commands on the submodules), asks before `git commit`, `git push`, `git tag` and `download_ffi.sh`, and denies `dotnet nuget push` and `gh release`. Two hooks block edits to generated code, native binaries and submodules, and run CSharpier on every edited `.cs` or `.csproj` file.
- Two [Claude Code](https://claude.com/claude-code) skills automate the upgrade procedures end to end: `/sync-protocol [version]` for `livekit/protocol` (step by step in [LivekitApi/README.md](LivekitApi/README.md#ai-coding-agents)) and `/sync-rust-sdks [version]` for `rust-sdks` (step by step in [LivekitRtc/README.md](LivekitRtc/README.md#ai-coding-agents)). Both regenerate, fix the build, surface the new upstream APIs, run the tests and write the changelog entry, then stop at a checkpoint for your decisions and leave the result uncommitted for review. Releases stay manual.
- The [sync-protocol](.github/workflows/sync-protocol.yml) and [sync-rust-sdks](.github/workflows/sync-rust-sdks.yml) workflows run those skills with Claude Code whenever `livekit/protocol` or `livekit-ffi` publishes a new GA release, and open a pull request whose checks run the full test suite and build the API reference.

---

<div align="center">
  
**Built with ❤️ by [pabloFuente](https://github.com/pabloFuente)**

</div>

<!--BEGIN_REPO_NAV-->
<br/><table>
<thead><tr><th colspan="2">LiveKit Ecosystem</th></tr></thead>
<tbody>
<tr><td>Agents SDKs</td><td><a href="https://github.com/livekit/agents">Python</a> · <a href="https://github.com/livekit/agents-js">Node.js</a></td></tr><tr></tr>
<tr><td>LiveKit SDKs</td><td><a href="https://github.com/livekit/client-sdk-js">Browser</a> · <a href="https://github.com/livekit/client-sdk-swift">Swift</a> · <a href="https://github.com/livekit/client-sdk-android">Android</a> · <a href="https://github.com/livekit/client-sdk-flutter">Flutter</a> · <a href="https://github.com/livekit/client-sdk-react-native">React Native</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <a href="https://github.com/livekit/client-sdk-unity">Unity</a> · <a href="https://github.com/livekit/client-sdk-unity-web">Unity (WebGL)</a> · <a href="https://github.com/livekit/client-sdk-esp32">ESP32</a> · <a href="https://github.com/livekit/client-sdk-cpp">C++</a> · <b>.NET (community)</b></td></tr><tr></tr>
<tr><td>Starter Apps</td><td><a href="https://github.com/livekit-examples/agent-starter-python">Python Agent</a> · <a href="https://github.com/livekit-examples/agent-starter-node">TypeScript Agent</a> · <a href="https://github.com/livekit-examples/agent-starter-react">React App</a> · <a href="https://github.com/livekit-examples/agent-starter-swift">SwiftUI App</a> · <a href="https://github.com/livekit-examples/agent-starter-android">Android App</a> · <a href="https://github.com/livekit-examples/agent-starter-flutter">Flutter App</a> · <a href="https://github.com/livekit-examples/agent-starter-react-native">React Native App</a> · <a href="https://github.com/livekit-examples/agent-starter-embed">Web Embed</a></td></tr><tr></tr>
<tr><td>UI Components</td><td><a href="https://github.com/livekit/components-js">React</a> · <a href="https://github.com/livekit/components-android">Android Compose</a> · <a href="https://github.com/livekit/components-swift">SwiftUI</a> · <a href="https://github.com/livekit/components-flutter">Flutter</a></td></tr><tr></tr>
<tr><td>Server APIs</td><td><a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/server-sdk-go">Golang</a> · <a href="https://github.com/livekit/server-sdk-ruby">Ruby</a> · <a href="https://github.com/livekit/server-sdk-kotlin">Java/Kotlin</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/agence104/livekit-server-sdk-php">PHP (community)</a> · <b>.NET (community)</b></td></tr><tr></tr>
<tr><td>Resources</td><td><a href="https://docs.livekit.io">Docs</a> · <a href="https://docs.livekit.io/mcp">Docs MCP Server</a> · <a href="https://github.com/livekit/livekit-cli">CLI</a> · <a href="https://cloud.livekit.io">LiveKit Cloud</a></td></tr><tr></tr>
<tr><td>LiveKit Server OSS</td><td><a href="https://github.com/livekit/livekit">LiveKit server</a> · <a href="https://github.com/livekit/egress">Egress</a> · <a href="https://github.com/livekit/ingress">Ingress</a> · <a href="https://github.com/livekit/sip">SIP</a></td></tr><tr></tr>
<tr><td>Community</td><td><a href="https://community.livekit.io">Developer Community</a> · <a href="https://livekit.io/join-slack">Slack</a> · <a href="https://x.com/livekit">X</a> · <a href="https://www.youtube.com/@livekit_io">YouTube</a></td></tr>
</tbody>
</table>
<!--END_REPO_NAV-->