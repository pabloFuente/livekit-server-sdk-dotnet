# livekit-server-sdk-dotnet

.NET APIs to manage LiveKit Server APIs. Use it with a .NET backend to manage access to LiveKit.

The SDK implements:

- Room Service
- Egress Service
- Ingress Service
- SIP Service
- Agent Service
- Access Tokens
- Webhooks

> It is compatible with [livekit/protocol <= 1.20.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.20.0). Methods and properties added to livekit/protocol after this version are not available in the SDK.

# For developers of the SDK

## Compile

Pre-requisites:

- [protoc](https://github.com/protocolbuffers/protobuf/releases/latest)
- `go install github.com/seanpfeifer/twirp-gen/cmd/protoc-gen-twirpcs@latest`

Generate proto files:

```bash
cd livekit-protocol
./generate_proto.sh
```

Build the SDK:

```bash
dotnet build
```

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

Then pack and install the package in the local repository. Run this commands at the root of the project `livekit-server-sdk-dotnet`:

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
