---
uid: Livekit.Server.Sdk.Dotnet
summary: *content
---
Access tokens, webhooks and clients of the LiveKit server APIs, for a .NET backend.

- [AccessToken](xref:Livekit.Server.Sdk.Dotnet.AccessToken) signs the tokens participants join rooms with; [VideoGrants](xref:Livekit.Server.Sdk.Dotnet.VideoGrants) and [SIPGrants](xref:Livekit.Server.Sdk.Dotnet.SIPGrants) say what they may do.
- [RoomServiceClient](xref:Livekit.Server.Sdk.Dotnet.RoomServiceClient), [EgressServiceClient](xref:Livekit.Server.Sdk.Dotnet.EgressServiceClient), [IngressServiceClient](xref:Livekit.Server.Sdk.Dotnet.IngressServiceClient), [SipServiceClient](xref:Livekit.Server.Sdk.Dotnet.SipServiceClient), [AgentDispatchServiceClient](xref:Livekit.Server.Sdk.Dotnet.AgentDispatchServiceClient), [ConnectorServiceClient](xref:Livekit.Server.Sdk.Dotnet.ConnectorServiceClient) and [PhoneNumberServiceClient](xref:Livekit.Server.Sdk.Dotnet.PhoneNumberServiceClient) call the server APIs.
- [WebhookReceiver](xref:Livekit.Server.Sdk.Dotnet.WebhookReceiver) verifies and parses the webhooks LiveKit posts.
- [TwirpException](xref:Livekit.Server.Sdk.Dotnet.TwirpException) is the error of a failed service call.

The other types of this namespace are the protobuf messages of the requests, responses and events, generated from [livekit/protocol](https://github.com/livekit/protocol). In the sidebar, the list below and the search results, the classes of the SDK itself carry an **SDK** badge.
