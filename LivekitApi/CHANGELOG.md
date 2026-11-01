# Changelog (Release Notes)

## 1.2.4

- Update livekit/protocol to [v1.53.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.53.1) ([commit 032d6c0](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/032d6c0ebe81b49ad44f6b421b5af409b4dd68e7), [commit 1c623d5](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/1c623d53d701aae5520d24dfb24077bb8ac57ee2))
- New `ConnectorServiceClient` to connect [WhatsApp and Twilio](https://docs.livekit.io/telephony/connectors/) calls to LiveKit rooms (LiveKit Cloud only), with methods `DialWhatsAppCall`, `AcceptWhatsAppCall`, `ConnectWhatsAppCall`, `DisconnectWhatsAppCall` and `ConnectTwilioCall`. See [Connector Service](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitApi/README.md#connector-service). [commit 40e46b7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/40e46b72e6bae652fc3c27c6ed167c1c0a5ab131)
- New method `EgressServiceClient.StartEgress` to start any type of egress with the unified egress API. [commit 40e46b7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/40e46b72e6bae652fc3c27c6ed167c1c0a5ab131)
- New method `AgentDispatchServiceClient.GetDispatch` to get an agent dispatch by its ID. [commit 40e46b7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/40e46b72e6bae652fc3c27c6ed167c1c0a5ab131)
- Fix base URLs with a subpath (e.g. `https://my.server.com/livekit`) losing the subpath when they did not end with `/`: all requests were sent to `https://my.server.com/twirp/...`. `BaseService` now appends the trailing slash itself, so the subpath is kept either way. [commit 51235a3](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/51235a3e8388be968db145d058aff62be6d79146)
- Breaking changes coming from livekit/protocol ([commit 1c623d5](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/1c623d53d701aae5520d24dfb24077bb8ac57ee2)):
  - `SipServiceClient.TransferSIPParticipant` now returns a `TransferSIPParticipantResponse` with the outcome of the transfer (`TransferId`, `Status`, `Reason` and `SipStatus`), instead of `Google.Protobuf.WellKnownTypes.Empty`.
  - Type `UpdateEgressRequest` has been removed. It was not used by any API.
  - The value of enum member `DataTrackExtensionID.DteiParticipantSid` changed from `1` to `255`.

## 1.2.3

- Update livekit/protocol to [v1.50.4](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.50.4) ([commit c68108a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/c68108aba7b95a11581c936d5cd8eb565e4dfd73), [commit ca1447c](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/ca1447c851f5aa5cc46391ba58dac91288c61a2c))

## 1.2.2

- Update livekit/protocol to [v1.46.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.46.0) ([commit 57b9b63](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/57b9b63e6cead508502189e831a7d812c6cc2bbe), [commit 017e93a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/017e93a64f81382ddc1d8459d5997938e44007df))

## 1.2.1

- Update livekit/protocol to [v1.45.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.45.1) ([commit b6f0054](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/b6f00545ef4f9c87f4b6e1915f140d7e0f6e512d))

## 1.2.0

- Update livekit/protocol to [v1.44.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.44.0) ([commit 4c00774](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/4c00774c1d5abf129ff7b3b6549661a1847e4926), [commit 4234473](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/4234473916df326ff80426251845472a1e6fc652))
- Support for [Phone Number APIs](https://docs.livekit.io/reference/telephony/phone-numbers-api/), [custom Token Sources](https://docs.livekit.io/frontends/authentication/tokens/custom/#use-a-custom-tokensource) and **Connectors for WhatsApp and Twilio**. [commit 4234473](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/4234473916df326ff80426251845472a1e6fc652)

## 1.1.0

- Allow configuring a custom HttpClient (of package System.Net.Http) when creating any service client. This allows setting custom headers, proxy configuration, timeouts, or other HttpClient settings. See [Using a custom HttpClient](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/d522cedc64d6b0ec873a9fbecd6189a512ae0a10/README.md#using-a-custom-httpclient). [commit bbb94fe](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/bbb94fe3b59ffa2c6ccd091c35056b9557a77cd3)

## 1.0.14

- Update livekit/protocol to [v1.43.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.43.0) ([commit e7925de](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e7925de93cd757d34755168b88c8dd2f2c2f3657), [commit 7d2f28d](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/7d2f28df868a28ffda1a28644143b1b538a15c75))
- Allow subpaths in the base URL when connecting to LiveKit server API. Before, all petitions were routed to the root path /twirp (e.g., https://my-livekit-server.com/twirp). Now, subpaths are also supported (e.g., https://my-livekit-server.com/my-subpath/twirp). [PR #32](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/32)

## 1.0.13

- Update livekit/protocol to [v1.42.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.42.0) ([commit c2800f1](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/c2800f17c3facb17e3a6ed3796bfe93dd2520e66), [commit 2682f5f](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/2682f5f841b3d65ddd6688576981ed8479a8fac3))

## 1.0.12

- Update livekit/protocol to [v1.41.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.41.0) ([commit f7e94d3](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f7e94d315faad9aa1cc05f0749cc8e535956b0da), [commit 9a70b07](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/9a70b07ba3070603f009bf537cf6052c4c9818c8))

## 1.0.11

- Update livekit/protocol to [v1.40.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.40.0) ([commit 1ef7e55](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/1ef7e55d3f82cedb03dfec63924f9ed5cf3881e4), [commit d33976b](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/d33976b6b87cd13dc3305e802274865a7598b2ed))

## 1.0.10

- Update livekit/protocol to [v1.39.2](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.39.2) ([commit 6569eda](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/6569edadb797295c191a53d35f05452e05a1eafb), [commit 95040f7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/95040f79d00ef195d642ea2cd99caa9b1d5e6858))

## 1.0.9

- Update livekit/protocol to [v1.39.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.39.0) ([commit 8f8636f](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/8f8636f6108295b83b2f26e8817d15843f513235), [commit 45dc9ec](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/45dc9eccf09b9ccc4b4594680e280b68285872c0))

## 1.0.8

- Update livekit/protocol to [v1.38.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.38.0) ([commit 8155ad6](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/8155ad6eb7acca5b5c3a7741a7e9f8a8a70e5d71), [commit 65703e4](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/65703e4c2c436c05dd7f930f1d790497af0a5e88), [commit a75a2e8](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a75a2e850a46fa184711054243d0f0d49d236d1f))

## 1.0.7

- Update livekit/protocol to [v1.34.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.34.0) ([commit 002330b](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/002330b035ef01625efcd7844a2fd717802018b8), [commit a8d1f2d](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/a8d1f2d864d8b950b91fba51d5c3700d90f5a34a))

## 1.0.6

- Update livekit/protocol to [v1.33.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.33.0) ([commit 6ffd848](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/6ffd8483367a3ae42f89723ab7ea0882bb14dc0c), [commit 605659d](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/605659dc63a77d8a386a33359c53b7d9e67050d8))

## 1.0.5

- Update livekit/protocol to [v1.32.1](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.32.1) ([commit 59c8b30](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/59c8b30b9ff1985096764a39771c1102ea279cb4), [commit 7d66404](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/7d66404cfa99245c1e2223ccab00b0a009948837))

## 1.0.4

- Update livekit/protocol to [v1.32.0](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.32.0) ([commit 09284cd](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/09284cd1ede94c00001604690632761f46c24e9b), [commit 7f06890](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/7f06890f95befddf601bdd419d7a9a2413d9a9a0))

## 1.0.3

- Update livekit/protocol to [v1.29.3](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.29.3) ([commit c7483b7](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/c7483b7a940079a0c5aae9b59912b1ad4ef94598), [commit 66083a6](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/66083a623b30e59450005a5b01e539f850084a90), [commit f59cbcb](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f59cbcb363a287c3472769446e3ce77573ff3b94))
