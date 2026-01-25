# LiveKit RTC Examples

Minimal standalone examples demonstrating the Livekit.Rtc.Dotnet package features.

## Prerequisites

- .NET SDK 8.0+
- Running LiveKit server (or use [LiveKit Cloud](https://livekit.io/cloud))

## Examples

1. **BasicConnection** - Connect to a room and display basic information
2. **PublishMedia** - Publish audio (440Hz sine) and video (colored frames) continuously
3. **ReceiveMedia** - Receive first audio track and save to WAV file
4. **EventHandling** - Demonstrate all major room events
5. **RpcExample** - RPC method registration and invocation

## Running Examples

Each example can be run independently:

```bash
cd BasicConnection
dotnet run -- <LIVEKIT_URL> <ACCESS_TOKEN>
```

Example:
```bash
cd BasicConnection
dotnet run -- wss://my-instance.livekit.cloud eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

You can also set environment variables instead of passing arguments:

```bash
export LIVEKIT_URL=wss://my-instance.livekit.cloud
export LIVEKIT_TOKEN=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
cd BasicConnection
dotnet run
```
