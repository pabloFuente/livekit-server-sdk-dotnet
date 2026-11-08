---
name: sync-protocol
description: Update the livekit/protocol submodule of Livekit.Server.Sdk.Dotnet to a new version and carry the change all the way through - regenerate the C# protos and Twirp stubs, fix the build, wrap new RPCs in the service clients, add tests and README docs, detect breaking changes with package validation and write the CHANGELOG entry. Use this whenever the user wants to bump, update, upgrade or sync livekit/protocol, regenerate the LivekitApi protos, pick up new LiveKit server APIs, or asks what changed upstream in the protocol, even if they don't say "sync".
argument-hint: "[protocol version, e.g. 1.54.0] (default: latest @livekit/protocol release)"
---

# Sync livekit/protocol into LivekitApi

Goal: one reviewable change set that moves `protocol` to a new `vX.Y.Z` tag (upstream tags the same commit as `@livekit/protocol@X.Y.Z` too) and leaves `LivekitApi` building, tested, documented and with every breaking change explained. Only `LivekitApi` is in scope; the RTC SDK uses a different submodule (`LivekitRtc/rust-sdks`).

Work through the phases in order and stop at the checkpoints to show the user what you found. Don't commit, tag or push: leave the result as uncommitted changes for the maintainer.

Scripts used below live next to this file. Run them from the repo root:
- `scripts/proto_inventory.py`: which `.proto` files `generate_proto.sh` compiles vs. what the submodule has, including imports that are missing.
- `scripts/api_coverage.py [--json]`: proto RPCs vs. generated Twirp routes vs. RPCs wrapped by `*ServiceClient.cs`. RPCs listed in `scripts/coverage_ignore.txt` are reported as ignored.

## Phase 1: Preflight

1. `git status --porcelain` must be clean apart from untracked noise. If not, ask before continuing.
2. `git submodule update --init protocol` if `protocol/protobufs` is missing.
3. Check tools: `protoc --version`, `go version`, `dotnet --version`, `dotnet tool restore`. `proto-tools.env` pins `protoc` at the version that reproduces the committed code (another `protoc` can rewrite every generated file), and `generate_proto.sh` builds the Twirp plugin from `tools/protoc-gen-twirpcs` with Go; it stops before deleting anything when `protoc` or `go` is missing or `protoc` is another version. If so, stop and tell the user what to install: `protoc` at `PROTOC_VERSION` from github.com/protocolbuffers/protobuf/releases, or Go from go.dev/dl. Never move the pin inside a sync.
4. Record the starting point:
   ```bash
   OLD=$(git -C protocol describe --tags --exact-match 2>/dev/null || git -C protocol rev-parse --short HEAD)
   python3 .claude/skills/sync-protocol/scripts/api_coverage.py --json > /tmp/coverage_before.json
   ```

## Phase 2: Move the submodule

1. `git -C protocol fetch --tags origin`.
2. Resolve the target. Release tags look like `v1.53.1` (the same commit also carries `@livekit/protocol@1.53.1`; this repo's commits and changelog use the `v` form). Without an argument, take the highest one: `git -C protocol tag -l 'v*' --sort=-v:refname | head -1`. With a bare number, prefix it with `v`.
3. `git -C protocol checkout vX.Y.Z`.
4. Read what changed upstream, because it drives everything after this:
   - `protocol/CHANGELOG.md`: every section newer than `$OLD`.
   - `git -C protocol diff --stat "$OLD"..HEAD -- protobufs/`
   - `git -C protocol diff "$OLD"..HEAD -- protobufs/` for the files that matter to the API: `service` blocks, removed or renamed fields and messages, enum value changes.

## Phase 3: Proto inventory and regeneration

1. `python3 .claude/skills/sync-protocol/scripts/proto_inventory.py`
   - **Listed but missing**: upstream deleted or moved a file. Remove or fix it in `generate_proto.sh`.
   - **Imported but not compiled**: add it to the `--csharp_out` list in `generate_proto.sh`, otherwise LivekitApi will not compile.
   - **Top-level protos not compiled**: report them at the checkpoint. Don't add them on your own.
2. If a new or changed file declares a public `service` that should be callable from .NET, it also needs to be in the `--twirpcs_out` call. Report it at the checkpoint instead of deciding.
3. Run `./generate_proto.sh`. If it fails, fix the script (proto list, `sed` patches), never the output.
4. `git diff --stat LivekitApi/proto/` to sanity-check: files that disappear entirely or a huge `Twirp.cs` churn deserve a closer look.

## Phase 4: Make it build

`dotnet build LivekitApi`. Compile errors here come from hand-written code using something that changed upstream: a removed type, a renamed field, a changed return type.

- Fix them in the hand-written files (`*ServiceClient.cs`, `AccessToken.cs`, `Grants.cs`, `WebhookReceiver.cs`, ...), never in `proto/`.
- Keep the old public surface where the protocol allows it, by adding an overload or delegating to the new shape. If the break is unavoidable (the type itself is gone, a return type changed), accept it and note it for the changelog.
- Then build the whole solution (`dotnet build`), since tests and examples use the same types.

## Phase 5: Coverage of the new API surface

```bash
python3 .claude/skills/sync-protocol/scripts/api_coverage.py --json > /tmp/coverage_after.json
python3 .claude/skills/sync-protocol/scripts/api_coverage.py
```

Compare before and after. Only what this bump introduced is in scope: new entries in `rpcs_not_wrapped` and `services_not_generated`, and anything in `stale_twirp_routes`. Pre-existing gaps are mentioned at the checkpoint but not fixed unless the user asks.

### Checkpoint

Show the user a short summary before writing wrappers:
- Upstream version range and the 3-6 most relevant upstream changelog points for the server API.
- Proto files added or removed in `generate_proto.sh`, and candidates you didn't add.
- New RPCs to wrap, with the client class each one belongs to.
- Breaking changes found so far.

Wait for confirmation, or for the user to strike items, before Phase 6.

## Phase 6: Wrap new RPCs

For each confirmed RPC:

1. **Find the reference.** Look up the same RPC in node-sdks `packages/livekit-server-sdk/src/*ServiceClient.ts` (and python `livekit-api` if node lacks it) on GitHub, in a local clone, or through the LiveKit docs (https://docs.livekit.io; a docs MCP server exists at https://docs.livekit.io/mcp when one is configured). You need two things from it: the **grants** it signs the request with, and the **public method name and shape**.
2. **Write the wrapper** in the matching `LivekitApi/<Service>ServiceClient.cs`, following the existing pattern exactly: same grants as the reference SDK, and `return await SendAsync("twirp/<package>.<Service>/<Method>", request, <Response>.Parser, AuthHeader(...)).ConfigureAwait(false);` with the route copied from the generated `Twirp.cs` (the clients never call `Twirp` directly, and never write the token to `httpClient.DefaultRequestHeaders`). C# 7.3 only (netstandard2.0). Add an XML `<summary>` taken from the proto comment. For a whole new service, create `<Service>ServiceClient : BaseService` with the standard constructor `(string host, string apiKey = null, string apiSecret = null, HttpClient client = null)`, and list it under "Service clients" in `docs/api-server/toc.yml`.
3. **Unit test** in `LivekitApi.Tests/<Service>ServiceClient.Test.cs` with `[Trait("Category", "Unit")]`, using `TestHttpMessageHandler`. Assert the Twirp route in `LastRequest.RequestUri` and decode the bearer token to check the grants.
4. **Integration test** only if the RPC works against the OSS `livekit-server` started by `ServiceClientFixture`. Cloud-only APIs (connectors, phone numbers, cloud agents...) get unit tests only, and a README note saying they are LiveKit Cloud only.
5. **README:** add or extend the service section in `LivekitApi/README.md` with a minimal usage snippet in the same style as its neighbours.
6. If the user decides an RPC should not be wrapped, add it to `scripts/coverage_ignore.txt` with a reason comment, so the next sync stays quiet about it.

## Phase 7: Breaking-change detection

```bash
dotnet pack LivekitApi -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:GenerateCompatibilitySuppressionFile=true
git status --short LivekitApi/CompatibilitySuppressions.xml   # no output: no break
git diff LivekitApi/CompatibilitySuppressions.xml
```

Package validation compares against `PackageValidationBaselineVersion` (the last release) and, with that property, writes every incompatibility into `CompatibilitySuppressions.xml` instead of failing. The publish workflow deletes that file after each release, so it usually shows up as new (`??`), which `git diff` does not show: read the whole file then.

For each new `<Suppression>`:
- `CP0001` type removed, `CP0002` member removed or signature changed, `CP0011` enum/const value changed, others: see https://learn.microsoft.com/dotnet/fundamentals/package-validation/diagnostic-ids
- If it was caused by **hand-written code**, it is a bug in this change: restore compatibility (overload, `[Obsolete]` shim) and regenerate the file.
- If it was caused by **the protocol**, keep it and write a changelog bullet in plain terms: what changed, the old vs. new type or value, and what users must do.

Then run `dotnet pack LivekitApi -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg` without the property; it must pass, as it must in the pull request's `build` check.

## Phase 8: Tests and formatting

```bash
dotnet test LivekitApi.Tests --filter "Category=Unit"
dotnet test LivekitApi.Tests      # needs Docker; if unavailable, say so explicitly in the final report
dotnet csharpier check .
dotnet docfx docs/docfx.json --warningsAsErrors && python3 docs/check_api_docs.py
```

The last line is the API reference build that `docs.yml` publishes from `main` after the merge (the pull request's `build` check runs it too). Fix what it reports, such as a missing sidebar entry or an unresolved `cref`, never by editing generated files or dropping `--warningsAsErrors`.

The pull request's `build` check runs every test through `./coverage.sh`, which fails below the minimums of `coverage/thresholds.json` or when a class or test file listed there no longer exists. The unit test of every new client method keeps the service clients covered. Never lower a minimum to make the check pass.

Integration failures can come from the `livekit/livekit-server:master` image rather than from this change. If a failure looks unrelated, say so and show the evidence instead of editing tests to make them pass.

## Phase 9: CHANGELOG

Edit `LivekitApi/CHANGELOG.md`. Write under the `## Unreleased` heading at the top, creating it if it does not exist. Never write under a version number: the release commit is what renames `Unreleased`. Use this structure, matching existing entries:

```markdown
- Update livekit/protocol to [vX.Y.Z](https://github.com/livekit/protocol/releases/tag/%40livekit%2Fprotocol%40X.Y.Z)
- New `FooServiceClient` to ... (LiveKit Cloud only), with methods `A`, `B`. See [Foo Service](https://github.com/pabloFuente/livekit-server-sdk-dotnet/blob/main/LivekitApi/README.md#foo-service).
- New method `RoomServiceClient.Bar` to ...
- Breaking changes coming from livekit/protocol:
  - `X.Y` now returns `Z` with ..., instead of `W`.
  - Type `Q` has been removed. It was not used by any API.
  - The value of enum member `E.M` changed from `1` to `255`.
```

Only list user-visible changes. Don't copy upstream changelog lines about internal services, agent simulation or cloud-internal messages that this SDK doesn't expose.

Don't touch `<Version>` or `<PackageValidationBaselineVersion>` in the csproj: version bumps happen at release time and the baseline is bumped by the publish workflow.

## Final report

End with:
- Protocol `$OLD` → `X.Y.Z`
- Files changed, grouped: generator script, generated (count only), hand-written, tests, docs
- New public API
- Breaking changes, each with its suppression ID
- Test results (unit / integration / API reference / skipped and why)
- Open decisions left to the maintainer (unwrapped RPCs, protos not added)
- Suggested commits, in this order and left to the maintainer (`.github/workflows/sync-protocol.yml` makes the same ones): `Update livekit/protocol to vX.Y.Z` (submodule pointer), `Regenerate proto C# files` (`generate_proto.sh` and `LivekitApi/proto`) and `Adapt LivekitApi to livekit/protocol vX.Y.Z` (suppressions file, hand-written fixes, tests, docs, changelog).
