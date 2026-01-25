[![NuGet Version](https://img.shields.io/nuget/v/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/packages/Livekit.Server.Sdk.Dotnet)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Livekit.Server.Sdk.Dotnet)](https://www.nuget.org/stats/packages/Livekit.Server.Sdk.Dotnet?groupby=Version)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/pabloFuente/livekit-server-sdk-dotnet/dotnet.yml)](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml)
[![License badge](https://img.shields.io/badge/license-Apache2-orange.svg)](http://www.apache.org/licenses/LICENSE-2.0)

# Livekit.Server.Sdk.Dotnet <!-- omit in toc -->

.NET APIs to manage [LiveKit](https://livekit.io) Access Tokens, Webhook and Server APIs. Use it with a .NET backend. It is build with **`netstandard2.0`** as target framework, so the SDK can be used in all versions of .NET.

> [!NOTE]
> This SDK does not provide real-time communication capabilities. If you want to add real-time audio, video and data features to your .NET server application, please check [Livekit.Rtc.Dotnet](../LivekitRtc/README.md) instead.

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
  - [Using a custom HttpClient](#using-a-custom-httpclient)
- [Example app](#example-app)
- [For developers of the SDK](#for-developers-of-the-sdk)
  - [Clone repository](#clone-repository)
  - [Compile](#compile)
  - [Run tests](#run-tests)
  - [Perform release](#perform-release)
  - [GitHub Actions](#github-actions)
  - [Install as a local NuGet package](#install-as-a-local-nuget-package)

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

## Using a custom HttpClient

You can provide a custom `HttpClient` when creating any of the service clients. This allows you to customize settings such as timeouts, headers, or proxy configurations.

```csharp
using System;
using System.Net;
using System.Net.Http;
using Livekit.Server.Sdk.Dotnet;

// Custom proxy
var handler = new HttpClientHandler
{
    Proxy = new WebProxy("http://myproxy.example.com:8080"),
    UseProxy = true
};

// Custom timeout and custom header
var httpClient = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(10)
};
httpClient.DefaultRequestHeaders.Add("X-Custom-Header", "my-value");

// Pass your custom HttpClient to the service client
var roomService = new RoomServiceClient(
    "https://your-livekit-server",
    "your-api-key",
    "your-api-secret",
    httpClient
);
```

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

- [.NET 8.0+ SDK](https://dotnet.microsoft.com/download/dotnet)
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
dotnet test LivekitApi.Tests
```

Run unit tests:

```bash
dotnet test LivekitApi.Tests --filter "Category=Unit"
```

Run integration tests (they automatically launch necessary services as Docker containers with [Testcontainers](https://testcontainers.com/)):

```bash
dotnet test LivekitApi.Tests --filter "Category=Integration"
```

## Perform release

1. Create a commit [like this](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a01453be9d50a29e7244ab38dac3939d285c84bb) for the new version.
2. Make sure to run script `./build_local.sh` to properly format all files with csharpier. Commit any changes in the repo after the script finishes.
3. Create a [new release in GitHub](https://github.com/pabloFuente/livekit-server-sdk-dotnet/releases/new) with:
   - Release title `api-X.Y.Z`
   - A new tag `api-X.Y.Z`
   - Description (change `A.B.C` with the proper version of livekit/protocol): `Update livekit/protocol to [vA.B.C](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%40A.B.C)`

> [!IMPORTANT]
> The **`api-`** prefix in the tag is mandatory for the publish workflow to identify that it is a release for package **Livekit.Server.Sdk.Dotnet** specifically.
>
> After creating the release, workflow [publish.yml](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/publish.yml) will automatically publish the new version to NuGet and will perform the necessary post-release tasks.

## GitHub Actions

- Tests are automatically run in GitHub Actions after each commit, thanks to [this workflow](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/dotnet.yml).
- A new version is published to NuGet after a new release with a tag with prefix **`api-`** is created in GitHub, thanks to [this workflow](https://github.com/pabloFuente/livekit-server-sdk-dotnet/actions/workflows/publish.yml).

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
