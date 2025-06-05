[![NuGet Version](https://img.shields.io/nuget/v/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Server.Sdk.Dotnet?groupby=Version)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/pabloFuente/livekit-server-sdk-dotnet/dotnet.yml)](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml)
[![License badge](https://img.shields.io/badge/license-Apache2-orange.svg)](http://www.apache.org/licenses/LICENSE-2.0)

# livekit-server-sdk-dotnet <!-- omit in toc -->

.NET APIs to manage [LiveKit](https://livekit.io) Access Tokens, Webhook and Server APIs. Use it with a .NET backend. It is build with **`netstandard2.0`** as target framework, so the SDK can be used in all versions of .NET.

- [Installation](#installation)
- [Usage](#usage)
  - [Creating Access Tokens](#creating-access-tokens)
  - [Room Service](#room-service)
  - [Egress Service](#egress-service)
  - [Ingress Service](#ingress-service)
  - [SIP Service](#sip-service)
  - [Agent Dispatch Service](#agent-dispatch-service)
  - [Receiving Webhooks](#receiving-webhooks)
  - [Environment Variables](#environment-variables)
- [Example app](#example-app)
- [For developers of the SDK](#for-developers-of-the-sdk)
  - [Clone repository](#clone-repository)
  - [Compile](#compile)
  - [Run tests](#run-tests)
  - [Perform release](#perform-release)
  - [GitHub Actions](#github-actions)
  - [Install as a local NuGet package](#install-as-a-local-nuget-package)
  - [Upgrade version of `livekit/protocol`](#upgrade-version-of-livekitprotocol)

# Installation

```bash
dotnet add package Livekit.Server.Sdk.Dotnet
```

# Usage

## Creating Access Tokens

```csharp
using Livekit.Server.Sdk.Dotnet;

var token = new AccessToken("yourkey", "yoursecret")
  .WithIdentity("participant-identity")
  .WithName("participant-name")
  .WithGrants(new VideoGrants{ RoomJoin = true, Room = "room-name" })
  .WithAttributes(new Dictionary<string, string> { { "mykey", "myvalue" } })
  .WithTtl(TimeSpan.FromHours(1));

var jwt = token.ToJwt();
```

By default, a token expires after 6 hours. You may override this by calling method `WithTtl` in the `AccessToken` object, just as shown in the example above.

> It's possible to customize the permissions of each participant. See more details at [access tokens guide](https://docs.livekit.io/home/get-started/authentication/#room-permissions).

## Room Service

`RoomServiceClient` is a Twirp-based client that provides management APIs to LiveKit. You can connect it to your LiveKit endpoint. See [service apis](https://docs.livekit.io/reference/server/server-apis/) for a list of available APIs.

```csharp
using Livekit.Server.Sdk.Dotnet;

RoomServiceClient client = new RoomServiceClient(
    "https://my.livekit.instance",
    "yourkey",
    "yoursecret"
);

// List rooms
var listRoomsRequest = new ListRoomsRequest();
var listRoomsResponse = await client.ListRooms(listRoomsRequest);
Room[] rooms = listRoomsResponse.Rooms.ToArray();

// List participants
var listParticipantsRequest = new ListParticipantsRequest { Room = "room-name" };
var listParticipantsResponse = await client.ListParticipants(listParticipantsRequest);
ParticipantInfo[] participants = listParticipantsResponse.Participants.ToArray();

// Mute published track
var mutePublishedTrackRequest = new MuteRoomTrackRequest
{
    Room = "room-name",
    Identity = "participant-identity",
    TrackSid = "track-sid",
    Muted = true,
};
await client.MutePublishedTrack(mutePublishedTrackRequest);

// Remove participant
var removeParticipantRequest = new RoomParticipantIdentity
{
    Room = "room-name",
    Identity = "participant-identity",
};
await client.RemoveParticipant(removeParticipantRequest);

// Delete room
var deleteRoomRequest = new DeleteRoomRequest { Room = "room-name" };
await client.DeleteRoom(deleteRoomRequest);

// Send data to room
var sendDataRequest = new SendDataRequest
{
    Room = "room-name",
    Data = ByteString.CopyFromUtf8("test-data"),
    Kind = DataPacket.Types.Kind.Reliable,
};
await client.SendData(sendDataRequest);
```

## Egress Service

`EgressServiceClient` is a .NET client to EgressService. Refer to [docs](https://docs.livekit.io/home/egress/overview/) for more usage examples.

```csharp
using Livekit.Server.Sdk.Dotnet;

EgressServiceClient client = new EgressServiceClient(
    "https://my.livekit.instance",
    "yourkey",
    "yoursecret"
);

// Starting a room composite to S3
var s3request = new RoomCompositeEgressRequest { RoomName = "room-name" };
s3request.FileOutputs.Add(
    new EncodedFileOutput
    {
        FileType = EncodedFileType.Mp4,
        Filepath = "my-recording.mp4",
        S3 = new S3Upload
        {
            AccessKey = "aws-access-key",
            Secret = "aws-access-secret",
            Region = "bucket-region",
            Bucket = "my-bucket",
        },
    }
);
EgressInfo s3Egress = await client.StartRoomCompositeEgress(s3request);

// Starting a track composite to RTMP
var rtmpRequest = new TrackCompositeEgressRequest
{
    RoomName = "room-name",
    AudioTrackId = "TR_XXXXXXXXXXXX",
    VideoTrackId = "TR_XXXXXXXXXXXX",
    StreamOutputs =
    {
        new StreamOutput
        {
            Protocol = StreamProtocol.Rtmp,
            Urls = { "rtmp://url1", "rtmps://url2" },
        },
    },
};
EgressInfo rtmpEgress = await client.StartTrackCompositeEgress(rtmpRequest);
```

## Ingress Service

`IngressServiceClient` is a .NET client to IngressService. Refer to [docs](https://docs.livekit.io/home/ingress/overview/) for more usage examples.

```csharp
using Livekit.Server.Sdk.Dotnet;

IngressServiceClient client = new IngressServiceClient(
    "https://my.livekit.instance",
    "yourkey",
    "yoursecret"
);

// Publish an ingress participant from URL
var urlRequest = new CreateIngressRequest
{
    RoomName = "room-name",
    ParticipantIdentity = "ingress-participant",
    ParticipantMetadata = "ingress-metadata",
    ParticipantName = "ingress-name",
    InputType = IngressInput.UrlInput,
    Url = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4",
};
IngressInfo ingress = await client.CreateIngress(urlRequest);

// Publish an ingress participant from RTMP
var rtmpRequest = new CreateIngressRequest
{
    RoomName = "room-name",
    ParticipantIdentity = "ingress-participant",
    ParticipantMetadata = "ingress-metadata",
    ParticipantName = "ingress-name",
    InputType = IngressInput.RtmpInput,
    Video = new IngressVideoOptions
    {
        Preset = IngressVideoEncodingPreset.H2641080P30Fps3LayersHighMotion,
    },
};
ingress = await client.CreateIngress(rtmpRequest);

// List ingresses
var response = await client.ListIngress(new ListIngressRequest());
IngressInfo[] ingresses = response.Items.ToArray();

// Delete an ingress
await client.DeleteIngress(new DeleteIngressRequest { IngressId = ingress.IngressId });
```

## SIP Service

`SipServiceClient` is a .NET client to SipService. Refer to [docs](https://docs.livekit.io/sip/) for more usage examples.

```csharp
using Livekit.Server.Sdk.Dotnet;

SipServiceClient client = new SipServiceClient(
    "https://my.livekit.instance",
    "yourkey",
    "yoursecret"
);

// Create SIP inbound trunk
var inRequest = new CreateSIPInboundTrunkRequest
{
    Trunk = new SIPInboundTrunkInfo
    {
        Name = "Demo inbound trunk",
        Numbers = { "+1234567890" },
        AllowedNumbers = { "+11111111", "+22222222" },
        AllowedAddresses = { "1.1.1.0/24" },
    },
};
SIPInboundTrunkInfo inboundTrunk = await client.CreateSIPInboundTrunk(inRequest);

// Create SIP outbound trunk
var outRequest = new CreateSIPOutboundTrunkRequest
{
    Trunk = new SIPOutboundTrunkInfo
    {
        Name = "Demo outbound trunk",
        Address = "my-test-trunk.com",
        Numbers = { "+1234567890" },
        AuthUsername = "username",
        AuthPassword = "password",
    },
};
SIPOutboundTrunkInfo outboundTrunk = await client.CreateSIPOutboundTrunk(outRequest);

// Create dispatch rule
var ruleRequest = new CreateSIPDispatchRuleRequest
{
    Name = "Demo dispatch rule",
    Metadata = "Demo dispatch rule metadata",
    Rule = new SIPDispatchRule
    {
        DispatchRuleDirect = new SIPDispatchRuleDirect { RoomName = "room-name", Pin = "1234" },
    },
};
SIPDispatchRuleInfo dispatchRule = await client.CreateSIPDispatchRule(ruleRequest);
```

## Agent Dispatch Service

`AgentDispatchServiceClient` is a .NET client to AgentDispatchService. Refer to [docs](https://docs.livekit.io/agents/build/dispatch/#explicit-agent-dispatch) for more usage examples.

```csharp
using Livekit.Server.Sdk.Dotnet;

AgentDispatchServiceClient agentDispatchClient = new AgentDispatchServiceClient(
    "https://my.livekit.instance",
    "yourkey",
    "yoursecret"
);

// Dispatch an agent
var dispatchAgentRequest = new CreateAgentDispatchRequest
{
    AgentName = "agent-name",
    Room = "toom-name",
    Metadata = "my-job-metadata",
};
AgentDispatch agentDispatch = await agentDispatchClient.CreateDispatch(dispatchAgentRequest);

// List agent dispatches
var listAgentDispatchesRequest = new ListAgentDispatchRequest { Room = "room-name" };
ListAgentDispatchResponse agentDispatches = await agentDispatchClient.ListDispatch(listAgentDispatchesRequest);

// Delete an agent dispatch
var deleteAgentDispatchRequest = new DeleteAgentDispatchRequest
{
    DispatchId = agentDispatch.Id,
    Room = "room-name",
};
AgentDispatch deletedAgentDispatch = await agentDispatchClient.DeleteDispatch(deleteAgentDispatchRequest);
```

## Receiving Webhooks

The .NET SDK also provides helper functions to decode and verify webhook callbacks. While verification is optional, it ensures the authenticity of the message. See [webhooks guide](https://docs.livekit.io/home/server/webhooks/) for details.

LiveKit POSTs to webhook endpoints with `Content-Type: application/webhook+json`. Please ensure your server is able to receive POST body with that MIME.

```csharp
using Livekit.Server.Sdk.Dotnet;

var webhookReceiver = new WebhookReceiver("yourkey", "yoursecret");

// In order to use the validator, WebhookReceiver must have access to the raw POSTed string
// This example uses "ASP.NET Core Web SDK" to handle the webhook request from LiveKit Server
// (https://learn.microsoft.com/en-us/aspnet/core/razor-pages/web-sdk?view=aspnetcore-8.0)

app.MapPost("/webhook-endpoint", async (HttpRequest request) => {

  var body = new StreamReader(request.Body);
  string postData = await body.ReadToEndAsync();

  var authHeader = request.Headers["Authorization"].First();

  WebhookEvent webhookEvent = webhookReceiver.Receive(postData, authHeader);

  // Here you have available the WebhookEvent object
  Console.Out.WriteLine(webhookEvent);

  return Results.Ok();
});
```

## Environment Variables

You may store credentials in environment variables. If api-key or api-secret is not passed in when creating an `AccessToken`, `RoomServiceClient` (or any other service client) the values in the following env vars will be used:

- `LIVEKIT_API_KEY`
- `LIVEKIT_API_SECRET`

# Example app

At [LivekitApi.Example](https://github.com/pabloFuente/livekit-server-sdk-dotnet/tree/main/LivekitApi.Example) you can find a sample application using livekit-server-sdk-dotnet.

# For developers of the SDK

## Clone repository

Make sure to clone with submodule option:

```bash
git clone --recurse-submodules https://github.com/pabloFuente/livekit-server-sdk-dotnet.git
```

## Compile

Pre-requisites:

- [.NET SDK](https://dotnet.microsoft.com/download/dotnet). Make sure to install the version defined in [`global.json`](./LivekitApi/global.json) file, or for minor/patch discrepancies, you can modify the `global.json` file to match your installed version.
- [protoc](https://github.com/protocolbuffers/protobuf/releases/latest)
- `go install github.com/seanpfeifer/twirp-gen/cmd/protoc-gen-twirpcs@latest`

Generate proto files:

```bash
./generate_proto.sh
```

Build the SDK:

```bash
dotnet build
```

## Run tests

Run all tests:

```bash
dotnet test
```

Run unit tests:

```bash
dotnet test --filter "Category=Unit"
```

Run integration tests (they automatically launch necessary services as Docker containers with [Testcontainers](https://testcontainers.com/)):

```bash
dotnet test --filter "Category=Integration"
```

## Perform release

1. Create a commit [like this](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a01453be9d50a29e7244ab38dac3939d285c84bb) for the new version.
2. Create a [new release in GitHub](https://github.com/pabloFuente/livekit-server-sdk-dotnet/releases/new) with:
   - Release title `X.Y.Z`
   - A new tag `X.Y.Z`
   - Description (change `A.B.C` with the proper version of livekit/protocol): `Update livekit/protocol to [vA.B.C](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%40A.B.C)`

> [!NOTE]
> After creating the release, workflow [publish.yml](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/publish.yml) will automatically publish the new version to NuGet and will perform the necessary post-release tasks.

## GitHub Actions

- Tests are automatically run in GitHub Actions after each commit, thanks to [this workflow](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml).
- A new version is published to NuGet after a new release is created in GitHub, thanks to [this workflow](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/publish.yml).

## Install as a local NuGet package

It is necessary the [NuGet tool](https://learn.microsoft.com/en-us/nuget/install-nuget-client-tools?tabs=macos).

First of all, configure the local NuGet repository in the NuGet configuration file. This is by default `%appdata%\NuGet\NuGet.Config` in Windows and `~/.nuget/NuGet/NuGet.Config` Mac/Linux. Simply add line `<add key="Local" value="/home/YOUR_USER/.private_nuget" />` if it does not already exist:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="Local" value="/home/YOUR_USER/.private_nuget" />
  </packageSources>
</configuration>
```

In this way we are configuring our local NuGet repository in the `~/.private_nuget` directory.

Then pack and install the package in the local repository. Run these commands at the root of the project `livekit-server-sdk-dotnet`:

```bash
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg

# Delete existing previous version just in case
nuget delete Livekit.Server.Sdk.Dotnet 1.0.0 -Source ~/.private_nuget/ -np
# Install the package in the local NuGet repository
nuget add "$PWD"/bin/Debug/Livekit.Server.Sdk.Dotnet.1.0.0.nupkg -Source ~/.private_nuget/
```

Once the NuGet package is installed in the local repository, you can add it to any local dotnet project with:

```bash
# Run this command at the root of the project where you want to add the NuGet package
dotnet add package Livekit.Server.Sdk.Dotnet
```

You can reset the local repository at any time with these commands:

```bash
dotnet nuget locals all --clear
dotnet restore
```

> **Note**: all this process is automated in the `build_local.sh` script. Tested in Unix systems.

## Upgrade version of `livekit/protocol`

To upgrade the version of the `livekit/protocol` Git submodule:

```bash
cd protocol
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

<!--BEGIN_REPO_NAV-->
<br/><table>
<thead><tr><th colspan="2">LiveKit Ecosystem</th></tr></thead>
<tbody>
<tr><td>LiveKit SDKs</td><td><a href="https://github.com/livekit/client-sdk-js">Browser</a> · <a href="https://github.com/livekit/client-sdk-swift">iOS/macOS/visionOS</a> · <a href="https://github.com/livekit/client-sdk-android">Android</a> · <a href="https://github.com/livekit/client-sdk-flutter">Flutter</a> · <a href="https://github.com/livekit/client-sdk-react-native">React Native</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <a href="https://github.com/livekit/client-sdk-unity">Unity</a> · <a href="https://github.com/livekit/client-sdk-unity-web">Unity (WebGL)</a></td></tr><tr></tr>
<tr><td>Server APIs</td><td><a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/server-sdk-go">Golang</a> · <a href="https://github.com/livekit/server-sdk-ruby">Ruby</a> · <a href="https://github.com/livekit/server-sdk-kotlin">Java/Kotlin</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/agence104/livekit-server-sdk-php">PHP (community)</a> · <a href="https://github.com/pabloFuente/livekit-server-sdk-dotnet">.NET (community)</a></td></tr><tr></tr>
<tr><td>UI Components</td><td><a href="https://github.com/livekit/components-js">React</a> · <a href="https://github.com/livekit/components-android">Android Compose</a> · <a href="https://github.com/livekit/components-swift">SwiftUI</a></td></tr><tr></tr>
<tr><td>Agents Frameworks</td><td><a href="https://github.com/livekit/agents">Python</a> · <a href="https://github.com/livekit/agents-js">Node.js</a> · <a href="https://github.com/livekit/agent-playground">Playground</a></td></tr><tr></tr>
<tr><td>Services</td><td><b>LiveKit server</b> · <a href="https://github.com/livekit/egress">Egress</a> · <a href="https://github.com/livekit/ingress">Ingress</a> · <a href="https://github.com/livekit/sip">SIP</a></td></tr><tr></tr>
<tr><td>Resources</td><td><a href="https://docs.livekit.io">Docs</a> · <a href="https://github.com/livekit-examples">Example apps</a> · <a href="https://livekit.io/cloud">Cloud</a> · <a href="https://docs.livekit.io/home/self-hosting/deployment">Self-hosting</a> · <a href="https://github.com/livekit/livekit-cli">CLI</a></td></tr>
</tbody>
</table>
<!--END_REPO_NAV-->