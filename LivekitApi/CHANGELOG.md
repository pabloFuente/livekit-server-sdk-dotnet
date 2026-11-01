# Changelog (Release Notes)

## Unreleased

- Update livekit/protocol to [v1.53.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.53.1) ([commit 032d6c0](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/032d6c0ebe81b49ad44f6b421b5af409b4dd68e7), [commit 1c623d5](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/1c623d53d701aae5520d24dfb24077bb8ac57ee2))
- New `ConnectorServiceClient` to connect [WhatsApp and Twilio](https://docs.livekit.io/telephony/connectors/) calls to LiveKit rooms (LiveKit Cloud only), with methods `DialWhatsAppCall`, `AcceptWhatsAppCall`, `ConnectWhatsAppCall`, `DisconnectWhatsAppCall` and `ConnectTwilioCall`. See [Connector Service](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitApi/README.md#connector-service). [commit 40e46b7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/40e46b72e6bae652fc3c27c6ed167c1c0a5ab131)
- New method `EgressServiceClient.StartEgress` to start any type of egress with the unified egress API. [commit 40e46b7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/40e46b72e6bae652fc3c27c6ed167c1c0a5ab131)
- New method `AgentDispatchServiceClient.GetDispatch` to get an agent dispatch by its ID. [commit 40e46b7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/40e46b72e6bae652fc3c27c6ed167c1c0a5ab131)
- Breaking changes coming from livekit/protocol ([commit 1c623d5](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/1c623d53d701aae5520d24dfb24077bb8ac57ee2)):
  - `SipServiceClient.TransferSIPParticipant` now returns a `TransferSIPParticipantResponse` with the outcome of the transfer (`TransferId`, `Status`, `Reason` and `SipStatus`), instead of `Google.Protobuf.WellKnownTypes.Empty`.
  - Type `UpdateEgressRequest` has been removed. It was not used by any API.
  - The value of enum member `DataTrackExtensionID.DteiParticipantSid` changed from `1` to `255`.

## 1.2.3

- Update livekit/protocol to [v1.50.4](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.50.4)

## 1.2.2

- Update livekit/protocol to [v1.46.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.46.0)

## 1.2.1

- Update livekit/protocol to [v1.45.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.45.1)

## 1.2.0

- Update livekit/protocol to [v1.44.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.44.0).
- Support for [Phone Number APIs](https://docs.livekit.io/reference/telephony/phone-numbers-api/), [custom Token Sources](https://docs.livekit.io/frontends/authentication/tokens/custom/#use-a-custom-tokensource) and **Connectors for WhatsApp and Twilio**.

## 1.1.0

- Allow configuring a custom HttpClient (of package System.Net.Http) when creating any service client. This allows setting custom headers, proxy configuration, timeouts, or other HttpClient settings. See [Using a custom HttpClient](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/d522cedc64d6b0ec873a9fbecd6189a512ae0a10/README.md#using-a-custom-httpclient).

## 1.0.14

- Update livekit/protocol to [v1.43.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.43.0)
- Allow subpaths in the base URL when connecting to LiveKit server API. Before, all petitions were routed to the root path /twirp (e.g., https://my-livekit-server.com/twirp). Now, subpaths are also supported (e.g., https://my-livekit-server.com/my-subpath/twirp). [PR #32](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/32)

## 1.0.13

- Update livekit/protocol to [v1.42.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.42.0)

## 1.0.12

- Update livekit/protocol to [v1.41.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.41.0)

## 1.0.11

- Update livekit/protocol to [v1.40.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.40.0)

## 1.0.10

- Update livekit/protocol to [v1.39.2](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.39.2)

## 1.0.9

- Update livekit/protocol to [v1.39.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.39.0)

## 1.0.8

- Update livekit/protocol to [v1.38.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.38.0)

## 1.0.7

- Update livekit/protocol to [v1.34.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.34.0)

## 1.0.6

- Update livekit/protocol to [v1.33.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.33.0)

## 1.0.5

- Update livekit/protocol to [v1.32.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.32.1)

## 1.0.4

- Update livekit/protocol to [v1.32.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.32.0)

## 1.0.3

- Update livekit/protocol to [v1.29.3](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.29.3)
