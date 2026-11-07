---
name: sync-rust-sdks
description: Update the livekit/rust-sdks submodule (livekit-ffi) of Livekit.Rtc.Dotnet to a new version and carry the change all the way through - regenerate the FFI C# protos, download the matching native livekit_ffi binaries for all six platforms, fix the build, surface new FFI requests, events and option fields in the SDK, detect breaking changes with package validation, run the parity guards and the e2e tests, and write the CHANGELOG entry. Use this whenever the user wants to bump, update, upgrade or sync rust-sdks or livekit-ffi, refresh the native FFI binaries, regenerate the LivekitRtc protos, pick up new LiveKit RTC features from the Rust SDK, or asks what changed upstream in livekit-ffi, even if they don't say "sync".
argument-hint: "[livekit-ffi version, e.g. 0.13.4 or livekit-ffi/v0.13.4] (default: latest livekit-ffi release)"
---

# Sync rust-sdks (livekit-ffi) into LivekitRtc

Goal: one reviewable change set that moves `LivekitRtc/rust-sdks` to a new `livekit-ffi/vX.Y.Z` tag and leaves `LivekitRtc` building against regenerated protos **and** native binaries of that same version, tested, documented, with every user-visible change in the changelog. Only `LivekitRtc` is in scope; the server API SDK uses the root `protocol` submodule and `/sync-protocol`.

The one rule that must never be broken: `LivekitRtc/Proto/*.g.cs` and the binaries in `LivekitRtc/runtimes/*/native/` must come from the same `livekit-ffi` tag. A mismatch compiles fine and fails at runtime (unknown fields, wrong defaults, FFI panics). If any step below cannot give you both for the target version, stop and report instead of leaving a half-moved state.

Work through the phases in order and stop at the checkpoint to show the user what you found. Don't commit, tag or push: leave the result as uncommitted changes for the maintainer.

The script used below lives next to this file. Run it from the repo root:
- `scripts/ffi_coverage.py [--json]`: the `FfiRequest`, `FfiEvent` and `RoomEvent` oneof cases in the FFI protos vs. the ones the hand-written SDK actually sends, reads and handles. Cases listed in `scripts/coverage_ignore.txt` are reported as ignored.

## Phase 1: Preflight

1. `git status --porcelain` must be clean apart from untracked noise. If not, ask before continuing.
2. `git submodule update --init LivekitRtc/rust-sdks` if `LivekitRtc/rust-sdks/livekit-ffi/protocol` is missing. `--recursive` is not needed for the protos: the nested submodules of rust-sdks are only used to build the FFI from source, and the superproject hides their state on purpose (`ignore = dirty` in `.gitmodules`).
3. Check tools: `protoc --version`, `command -v protoc-gen-twirpcs`, `dotnet --version`, `dotnet tool restore`, `curl --version`, `unzip -v`, `jq --version` (`download_ffi.sh` reads the release digests with it), `gh --version`, and `docker info` (for the e2e tests later; if Docker is missing, note it for the final report). `generate_proto.sh` regenerates both packages and stops at the server ones without the Twirp plugin, so the versions that reproduce the committed code apply here too: `protoc` 36.2 and `protoc-gen-twirpcs` v1.1.0 (`go install github.com/seanpfeifer/twirp-gen/cmd/protoc-gen-twirpcs@v1.1.0`). Warn the user if `protoc` differs.
4. Record the starting point:
   ```bash
   OLD=$(git -C LivekitRtc/rust-sdks describe --tags --exact-match --match 'livekit-ffi/*' 2>/dev/null || git -C LivekitRtc/rust-sdks rev-parse --short HEAD)
   python3 .claude/skills/sync-rust-sdks/scripts/ffi_coverage.py --json > /tmp/ffi_coverage_before.json
   ```

## Phase 2: Move the submodule

1. `git -C LivekitRtc/rust-sdks fetch --tags origin`
2. Resolve the target. FFI release tags look like `livekit-ffi/v0.13.3`; the other crates in the same repo have their own tags (`livekit/v*`, `libwebrtc/v*`, ...), ignore them. Without an argument take the highest one: `git -C LivekitRtc/rust-sdks tag -l 'livekit-ffi/v*' --sort=-v:refname | head -1`. With a bare `X.Y.Z`, prefix it with `livekit-ffi/v`.
3. Confirm the GitHub release for that tag exists and carries all six binaries before moving the submodule; `download_ffi.sh` would fail later anyway. Check through `gh api`, because the project settings deny `gh release`:
   ```bash
   gh api "repos/livekit/rust-sdks/releases/tags/livekit-ffi/vX.Y.Z" --jq '.assets[].name'
   ```
   Expected: `ffi-linux-x86_64.zip`, `ffi-linux-arm64.zip`, `ffi-macos-x86_64.zip`, `ffi-macos-arm64.zip`, `ffi-windows-x86_64.zip`, `ffi-windows-arm64.zip`. If the release is missing or incomplete (upstream may still be building it), stop here and tell the user. Do not move the submodule to a tag without binaries.
4. `git -C LivekitRtc/rust-sdks checkout "livekit-ffi/vX.Y.Z"`. Sanity check: `grep -m1 '^version' LivekitRtc/rust-sdks/livekit-ffi/Cargo.toml` must print `X.Y.Z`.
5. Read what changed upstream, because it drives everything after this:
   - `LivekitRtc/rust-sdks/livekit-ffi/CHANGELOG.md`: every section newer than `$OLD`.
   - `gh api "repos/livekit/rust-sdks/releases/tags/livekit-ffi/vX.Y.Z" --jq .body` for the release notes.
   - `git -C LivekitRtc/rust-sdks diff --stat "$OLD"..HEAD -- livekit-ffi/protocol/`
   - `git -C LivekitRtc/rust-sdks diff "$OLD"..HEAD -- livekit-ffi/protocol/`, looking for: new `oneof` cases in `FfiRequest`, `FfiEvent` and `RoomEvent`; new or removed fields in the option messages the SDK maps (`ConnectRequest`/`RoomOptions`, `TrackPublishOptions`, `E2eeOptions`, `KeyProviderOptions`, `VideoEncoding`, `AudioEncoding`, the data-stream and RPC messages); renamed or removed messages, fields and enum values.
   - `git -C LivekitRtc/rust-sdks diff "$OLD"..HEAD -- rust-toolchain.toml`: if the pinned Rust channel changed, update the example in the "Building from source" section of `LivekitRtc/README.md`.

## Phase 3: Regenerate the protos

1. `./generate_proto.sh`. It regenerates both SDKs. If it fails on the FFI protos, the local `protoc` is probably too old for syntax upstream started using (`proto3 optional` needs 3.15+, editions need a recent 2x release). Install a newer `protoc`; never patch the `.proto` files or the generated output.
2. `git status --short LivekitApi/proto` must be empty. If it is not, the local `protoc` differs from the one used for the last protocol regeneration and the churn is unrelated to this bump: `git checkout -- LivekitApi/proto` and mention it in the final report (it belongs to a `/sync-protocol` run).
3. `git diff --stat LivekitRtc/Proto/`. A `.g.cs` that disappears or appears means upstream removed, added or renamed a proto file. `generate_proto.sh` globs `*.proto`, so there is no list to maintain, but the `Google.Protobuf` package in `LivekitRtc/LivekitRtc.csproj` must be at least as new as `protoc --version` or the output will not compile.

## Phase 4: Native binaries

1. `LivekitRtc/download_ffi.sh livekit-ffi/vX.Y.Z` (from the repo root; it writes into `LivekitRtc/runtimes/` wherever it is run from). It downloads and overwrites binaries, so expect a permission prompt.
2. Verify, do not assume. The script checks each archive against the SHA-256 digest GitHub recorded for that release asset, and copies nothing unless all six download and match: a non-zero exit, with a `✗` line naming the platform, means `runtimes/` still holds the old set. Stop and report it, and never work around a digest mismatch. On success,
   ```bash
   git status --short LivekitRtc/runtimes
   ```
   must list the six libraries as modified (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`). The pull request's `build` check repeats the download for the tag the submodule points to and fails unless the committed binaries are exactly those.
3. Never copy a binary built locally from `LivekitRtc/rust-sdks/target/` into `runtimes/` during a sync; releases ship the upstream builds only.

## Phase 5: Make it build

`dotnet build LivekitRtc`. Compile errors here come from hand-written code using something that changed upstream: a removed or renamed proto field, a changed enum member, a message that moved.

- Fix them in the hand-written files (`Room.cs`, `Participant.cs`, `Track*.cs`, `*Stream.cs`, `*Source.cs`, `E2EE.cs`, `DataStream.cs`, `Rpc.cs`, `Internal/*.cs`), never in `Proto/`.
- Keep the public C# API stable where the FFI allows it: change a public signature in hand-written code only when the FFI forces it. Phase 8 lists every break against the last release, generated `LiveKit.Proto` types included.
- Respect the project limits: `netstandard2.1` BCL (no `Task.WaitAsync`, `ArgumentNullException.ThrowIfNull`, ...), `Nullable enable`, XML docs on every public member.
- Then build everything that consumes the SDK, since the examples are not in the solution:
  ```bash
  dotnet build
  for p in LivekitRtc.Example/*/*.csproj; do dotnet build "$p"; done
  ```

## Phase 6: Coverage of the new FFI surface

```bash
python3 .claude/skills/sync-rust-sdks/scripts/ffi_coverage.py --json > /tmp/ffi_coverage_after.json
python3 .claude/skills/sync-rust-sdks/scripts/ffi_coverage.py
dotnet test LivekitRtc.Tests --filter "FullyQualifiedName~MappingTests"
```

- Compare before and after. Only what this bump introduced is in scope: cases that are new in `unused` (requests, callbacks, room events) and anything in `stale`. Pre-existing gaps are mentioned at the checkpoint but not fixed unless the user asks.
- `TrackPublishOptionsMappingTests` iterates the proto descriptor and fails naming every `Proto.TrackPublishOptions` field the SDK does not expose. A failure here is a to-do item for Phase 7, not a test to weaken.
- The other mapped option messages have no descriptor guard: compare the Phase 2 diff of `RoomOptions`, `E2eeOptions`, `KeyProviderOptions`, `VideoEncoding` and `AudioEncoding` with the matching `ToProto()` in `Room.cs`, `Participant.cs` and `E2EE.cs`. New enum values need nothing: the SDK exposes the proto enums directly.

### Checkpoint

Show the user a short summary before changing SDK code:
- `livekit-ffi` version range and the 3-6 most relevant upstream changelog points for a server-side SDK.
- Proto diff summary: proto files added or removed, renamed or removed members that forced fixes in Phase 5.
- Binaries: six downloaded, all changed.
- New FFI requests, callbacks, room events and option fields, each with a proposal: expose (with the C# shape and the Node/Python name) or ignore (with the reason to record in `coverage_ignore.txt`).
- Upstream behavior changes that users of the package will notice even without SDK changes (defaults, codecs, reconnection, logging).

Wait for confirmation, or for the user to strike items, before Phase 7.

## Phase 7: Expose confirmed features

Match the Node (`livekit/node-sdks`, `packages/livekit-rtc`) and Python (`livekit/python-sdks`, `livekit-rtc`) RTC SDKs for names, defaults and semantics. Both wrap this same FFI, so their implementation of the feature is the reference; look it up on GitHub or in a local clone.

- **New option field:** a nullable property on the public options class, assigned in `ToProto()` only when set (unset keeps the FFI default), an XML doc comment, and a case in the matching `*MappingTests` (unit test, no Docker).
- **New room event:** a `case RoomEvent.MessageOneofCase.X` in `Room.HandleRoomEvent`, a `*EventArgs` class in `Events.cs`, a public `event EventHandler<...>` on `Room` raised through `DispatchEvent` (never directly on the FFI thread), and an e2e test in the matching `LivekitRtc.Tests` class that provokes the event through a second participant or the Chrome pages in `LivekitRtc.Tests/web/`.
- **New request:** follow `Room.ConnectAsync` / `LocalParticipant.PublishTrackAsync`: build the `FfiRequest`, `SendRequest`, then `WaitForEventAsync` on the callback with the same `AsyncId` and a timeout; wrap every handle the FFI returns in `FfiHandle`; keep any buffer whose pointer crosses the FFI pinned until the request has been sent; take `Room.FfiEventLock` only to mutate shared maps, never across the wait.
- **Callback plus room event:** they are independent messages and can arrive in either order; handle both orders.
- Document the feature in `LivekitRtc/README.md` (the Features list and, for a usage pattern, a Usage subsection in the same style as its neighbours).
- If the user decides a case should not be exposed, add it to `scripts/coverage_ignore.txt` with a reason comment so the next sync stays quiet about it.

## Phase 8: Breaking-change detection

```bash
dotnet pack LivekitRtc -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:GenerateCompatibilitySuppressionFile=true
git status --short LivekitRtc/CompatibilitySuppressions.xml   # no output: no break
```

Package validation compares against `PackageValidationBaselineVersion` (the last RTC release) and, with that property, writes every incompatibility into `LivekitRtc/CompatibilitySuppressions.xml` instead of failing. The publish workflow deletes that file after each release, so a file that shows up as new (`??`) holds only this sync's breaks. The generated `LiveKit.Proto` types are all public, so an FFI proto that removes or changes a member shows up here even when no hand-written code uses it.

For each `<Suppression>`:
- `CP0001` type removed, `CP0002` member removed or signature changed, `CP0011` enum/const value changed, others: see https://learn.microsoft.com/dotnet/fundamentals/package-validation/diagnostic-ids
- If it was caused by **hand-written code** that the FFI did not force, it is a bug in this change: restore compatibility (overload, `[Obsolete]` shim) and regenerate the file.
- If **the FFI** forced it (a generated type, or a hand-written member mapping a field that upstream removed), keep it and write a changelog bullet in plain terms: what changed, the old vs. new type or value, and what users must do.

Then run `dotnet pack LivekitRtc -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg` without the property; it must pass, as it must in the pull request's `build` check.

## Phase 9: Tests and formatting

```bash
# No Docker. These load the new native library through the FFI, so they also prove that the
# binary for this host platform initialises and answers requests.
dotnet test LivekitRtc.Tests --filter "FullyQualifiedName~FfiClientTest|FullyQualifiedName~SdkVersionTests|FullyQualifiedName~AudioCaptureMinimalTest|FullyQualifiedName~PinningTests|FullyQualifiedName~VideoFramePlanesTests|FullyQualifiedName~MappingTests|FullyQualifiedName~EncodedVideoSourceTests|FullyQualifiedName~DataTrackFrameTests|FullyQualifiedName~AudioSourcePlayoutTests|FullyQualifiedName~AudioResamplerTests|FullyQualifiedName~RpcErrorTests"

# Docker (Testcontainers). RoomEventsTests connects two participants through the new binary and
# protos end to end and is the minimum; add the classes for the areas the upstream diff touched
# (AudioPublishTests, VideoPublishTests, AudioReceiveTests, DataStreamTests, RpcTests, E2EETests).
dotnet test LivekitRtc.Tests --filter "FullyQualifiedName~RoomEventsTests" --logger "console;verbosity=detailed"

dotnet csharpier check .
dotnet docfx docs/docfx.json --warningsAsErrors && python3 docs/check_api_docs.py
```

- The last line is the API reference build that `docs.yml` publishes from `main` after the merge (the pull request's `build` check runs it too). A public member that starts using another `LiveKit.Proto` type needs that type in the allow-list of `docs/filter.yml`, and the check script names it. Fix what it reports, never by editing generated files or dropping `--warningsAsErrors`.
- If Testcontainers cannot pull its `ryuk` image, set `TESTCONTAINERS_RYUK_DISABLED=true`.
- Read the SDK's stderr, not just the pass count: an `FFI Panic:` line, or `[WARN] RoomEvent.<Name>: ... not found` lines, mean the binary and the SDK disagree. `dotnet test` does not show the test host's stderr: run with `LIVEKIT_TEST_STDERR_FILE=/tmp/sdk-stderr.log` (the test assembly tees stderr there) and `grep -c '\[WARN\]\|\[ERROR\]\|FFI Panic' /tmp/sdk-stderr.log` afterwards. Report findings with the evidence instead of editing tests to make them pass.
- If Docker is unavailable, say so explicitly in the final report; the no-Docker set is not sufficient proof for a native bump.

## Phase 10: CHANGELOG

Edit `LivekitRtc/CHANGELOG.md`. Write under the `## Unreleased` heading at the top, creating it if it does not exist. Never write under a version number: the release commit is what renames `Unreleased`. Use this structure, matching existing entries, wrapped at about 100 columns like the rest of the file:

```markdown
## Unreleased

- Update rust-sdks to [livekit-ffi/vX.Y.Z](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2FvX.Y.Z)
- New `Room.FooReceived` event raised when ... (parity with the Node/Python RTC SDKs).
- New `TrackPublishOptions.Bar` option to ... Unset keeps the previous behavior.
- Upstream change users will notice: ...
- Breaking changes coming from rust-sdks:
  - Type `LiveKit.Proto.Q` has been removed. It was not used by any API of the SDK.
  - `TrackPublishOptions.X` now ... instead of ... (what users must do).
```

Only list user-visible changes. Skip upstream items about parts this package does not ship (uniffi bindings, platform audio devices, capture sources) unless they change behavior for .NET users. Don't touch `<Version>` or `<PackageValidationBaselineVersion>` in `LivekitRtc.csproj`: version bumps happen at release time and the publish workflow moves the baseline, and `FfiClient.SdkVersion` is read from the assembly, so there is nothing to update by hand.

## Final report

End with:
- `livekit-ffi` `$OLD` → `vX.Y.Z`
- Files changed, grouped: submodule pointer, generated (count only), binaries (must be 6), generator script, hand-written, tests, docs
- New public API
- Breaking changes, each with its suppression ID
- Test results (no-Docker set, Docker classes run, API reference, skipped and why), plus any `FFI Panic` or `[WARN] RoomEvent` lines seen
- Open decisions left to the maintainer (unexposed requests, callbacks, room events, option fields)
- Suggested commits, in this order and left to the maintainer (`.github/workflows/sync-rust-sdks.yml` makes the same ones): `Update "rust-sdks" to livekit-ffi/vX.Y.Z` (submodule pointer, regenerated protos, the six binaries and any `generate_proto.sh` change, which must move together) and `Adapt LivekitRtc to livekit-ffi/vX.Y.Z` (SDK code, suppressions file, tests, docs, changelog).
