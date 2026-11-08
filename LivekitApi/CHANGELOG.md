# Changelog (Release Notes)

## Unreleased

- Fix the methods of the generated `Twirp` class (`Twirp.CreateRoom(httpClient, request)` and the like, for code that calls the server without the service clients) deadlocking when the caller blocks on the returned task from a UI thread or another single-threaded `SynchronizationContext`, and reporting errors badly: messages were cut at the first quote, the error code `dataloss` became `Unknown`, and the HTTP status and `meta` map were lost. They now await with `ConfigureAwait(false)` and raise a `TwirpException`, exactly like the service clients since 1.2.5 (still a `Twirp.Exception`, so existing `catch` blocks keep working; a test asserting the exact type with xUnit's `Assert.ThrowsAsync<Twirp.Exception>` needs `Assert.ThrowsAnyAsync`). See [Handling errors](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitApi/README.md#handling-errors).

## 1.2.6

- Update livekit/protocol to [v1.53.2](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%401.53.2) [PR #147](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/147)
- New `Dialtone` property (enum `SIPDialtone`) on `CreateSIPParticipantRequest` and `TransferSIPParticipantRequest`, to choose the dial tone that `SipServiceClient.CreateSIPParticipant` and `TransferSIPParticipant` play while the call is placed or transferred: `Disabled`, `Etsi` (the tone `PlayDialtone` played), `Auto` (chosen by the country of the called number), `Us`, `Uk`, `Jp` or `Au`. `PlayDialtone` is now `[Obsolete]` on both requests, so code that sets it gets warning `CS0612` (an error with `TreatWarningsAsErrors`). It still works: when `Dialtone` is not set, `PlayDialtone = true` means `Dialtone = SIPDialtone.Etsi`. [PR #147](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/147)
- New `SIPMediaConfig.DropAudioDuringDtmf`, settable on the `Media` of SIP trunks, dispatch rules and `CreateSIPParticipantRequest`, to stop sending audio while DTMF digits are sent. [PR #147](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/147)
- New `AgentDispatchState.Trigger` (enum `AgentDispatchTrigger`) in the `AgentDispatch.State` returned by `AgentDispatchServiceClient`, telling what created the dispatch: a participant joining (`AdtJoin`), `RoomServiceClient.CreateRoom` (`AdtRoomApi`) or `AgentDispatchServiceClient.CreateDispatch` (`AdtDispatchApi`). New timestamps in Unix nanoseconds on the agent jobs listed in that state and sent in the `agent_job_*` webhooks: `Job.CreatedAt`, `JobState.AcceptedAt` and `JobState.AssignedAt`. [PR #147](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/147)
- Breaking changes coming from livekit/protocol ([PR #147](https://github.com/pabloFuente/livekit-server-sdk-dotnet/pull/147)):
  - `NodeStats.ForwardSchedulingDelay` and its constant `NodeStats.ForwardSchedulingDelayFieldNumber` have been removed. The field number now carries `NodeStats.ForwardProcessingPerWrite`, the mean forwarding work per write to a subscriber in nanoseconds, which measures something else. `NodeStats` describes LiveKit server nodes and is not returned by any API of this SDK.
- XML documentation comments for every public member, with parameter and return value descriptions on every method, for IntelliSense and for the new [API reference](https://pablofuente.github.io/livekit-server-sdk-dotnet/) generated with DocFX. [commit f5660c2](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/f5660c2f0225936951568c0c3ab403175521063a)
- Fix wrong or incomplete XML documentation: `SipServiceClient.TransferSIPParticipant` said it moved the participant to another room, while it transfers the call to a phone number or SIP URI; `TokenVerifier.Verify`, `WebhookReceiver.Receive` and the constructors of `AccessToken`, `TokenVerifier` and `WebhookReceiver` now list the exceptions they actually throw. `AccessToken`, `RoomServiceClient` and `WebhookReceiver` gain usage examples, and the API reference groups the clients, tokens and webhooks in its sidebar and no longer lists the protobuf plumbing members of the generated messages.

## 1.2.5

- Fix concurrent calls on the same service client, or on service clients sharing one `HttpClient`, sometimes being sent with another call's token and failing with a `Twirp.Exception` (`Permission_Denied` or `Unauthenticated`): the bearer token was written to the shared `HttpClient.DefaultRequestHeaders` right before each request. The host, the token and the user agent now travel with each request, so a caller-supplied `HttpClient` is never modified anymore: it can be shared between service clients (even for different servers) and with the rest of the application, and it may already have sent requests, which used to make the constructor throw `InvalidOperationException`. See [Using a custom HttpClient](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitApi/README.md#using-a-custom-httpclient). [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- Fix `new AccessToken()`, `new TokenVerifier()` and `new WebhookReceiver()` throwing `ArgumentException: apiKey and apiSecret must be set` even with `LIVEKIT_API_KEY` and `LIVEKIT_API_SECRET` set: the constructors validated their arguments instead of the resolved values. The service clients now honor the same variables when `apiKey` and `apiSecret` are omitted, as the README already stated. See [Environment Variables](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitApi/README.md#environment-variables). [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- Fix tokens built on a thread whose culture uses a decimal comma (`es-ES`, `de-DE`, `fr-FR`...) carrying a malformed `exp` or `nbf` claim (formatted as `1762000000,123`) when `WithExpiration`, `WithNotBefore` or a fractional `WithTtl` was used, and under Turkish cultures `WithKind(Kind.Ingress)` and the `IngressAdmin` grant being written with a dotless `ı` and ignored by the server. Time claims are now whole seconds, as in the other LiveKit SDKs, and all conversions are culture-invariant. [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- Fix `AccessToken.ToJwt()` throwing `NullReferenceException` when `WithName`, `WithMetadata` or `WithSha256` received `null`. The optional claims (`name`, `metadata`, `sha256`, `kind`, `roomConfig`) are now only written when set, as the other LiveKit SDKs do. [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- Fix `WebhookReceiver.Receive` failing with `InvalidProtocolBufferException: Unknown field` for every webhook as soon as the server sends a field the compiled protocol does not know (for example after a LiveKit upgrade): the `ignoreUnknownFields` parameter had no effect. It is honored now (unknown fields are ignored by default). New overloads: `Receive(byte[] body, ...)` to hash the raw bytes the server hashed, and `WebhookReceiver(apiKey, apiSecret, TimeSpan? leeway)` to set the clock tolerance. [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- Fix `TokenVerifier.Verify` (and so `WebhookReceiver`) throwing `JsonSerializationException` for tokens whose `roomConfig` contains an enum (an egress preset, a file type...): the claim is written with the protobuf JSON formatter and was read back with Newtonsoft. `Verify` also no longer throws for tokens carrying a JSON array claim, and only accepts HS256 signatures, like the Python SDK. [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- Errors returned by the server are now raised as `TwirpException`, a subclass of the generated `Twirp.Exception` (existing `catch` blocks keep working; a test asserting the exact type with xUnit's `Assert.ThrowsAsync<Twirp.Exception>` needs `Assert.ThrowsAnyAsync`) that parses the Twirp error properly (messages with quotes used to be truncated) and exposes the HTTP `Status` and the `Meta` map, for example `sip_status_code` for SIP calls. See [Handling errors](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitApi/README.md#handling-errors). [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- Service clients accept `ws://` and `wss://` hosts (the usual `LIVEKIT_URL` value) and convert them to `http`/`https`, as the Node and Python SDKs do. Clients created without an `HttpClient` now share one connection pool instead of each opening its own, and the user agent is `livekit-server-sdk-dotnet/<version>`, appended after the user agent of a caller-supplied `HttpClient` instead of replacing it. [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- New grants, reaching parity with the Node and Python SDKs: `VideoGrants.CanManageAgentSession`, `AccessToken.WithInferenceGrants(InferenceGrants)` and `AccessToken.WithObservabilityGrants(ObservabilityGrants)` (read back by `TokenVerifier.Verify` as `ClaimsModel.Inference` and `ClaimsModel.Observability`). Empty grant values (`room: ""`, `canPublishSources: []`...) are no longer written to the token, as in the other SDKs. New `AccessToken.WithExpiration(DateTimeOffset)` and `WithNotBefore(DateTimeOffset)` overloads, since a `DateTime` of kind `Unspecified` is read as local time. [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)
- All service client methods now await with `ConfigureAwait(false)`, so callers that block on the returned task from a thread with a `SynchronizationContext` (UI threads, classic ASP.NET) no longer deadlock. [commit e68fa9a](https://github.com/pabloFuente/livekit-server-sdk-dotnet/commit/e68fa9a914eed9095decfb635075d9081e25b5a6)

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
