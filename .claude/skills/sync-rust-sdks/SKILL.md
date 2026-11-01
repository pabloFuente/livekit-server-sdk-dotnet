---
name: sync-rust-sdks
description: Update the livekit/rust-sdks submodule (livekit-ffi) of Livekit.Rtc.Dotnet to a new version and carry the change all the way through - regenerate the FFI C# protos, download the matching native livekit_ffi binaries for all six platforms, fix the build, surface new FFI requests, events and option fields in the SDK, run the parity guards and the e2e tests, and write the CHANGELOG entry. Use this whenever the user wants to bump, update, upgrade or sync rust-sdks or livekit-ffi, refresh the native FFI binaries, regenerate the LivekitRtc protos, pick up new LiveKit RTC features from the Rust SDK, or asks what changed upstream in livekit-ffi, even if they don't say "sync".
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
3. Check tools: `protoc --version`, `dotnet --version`, `dotnet tool restore`, `curl --version`, `unzip -v`, `gh --version`, and `docker info` (for the e2e tests later; if Docker is missing, note it for the final report).
4. Record the starting point:
   ```bash
   OLD=$(git -C LivekitRtc/rust-sdks describe --tags --exact-match --match 'livekit-ffi/*' 2>/dev/null || git -C LivekitRtc/rust-sdks rev-parse --short HEAD)
   python3 .claude/skills/sync-rust-sdks/scripts/ffi_coverage.py --json > /tmp/ffi_coverage_before.json
   ```

## Phase 2: Move the submodule

1. `git -C LivekitRtc/rust-sdks fetch --tags origin`
2. Resolve the target. FFI release tags look like `livekit-ffi/v0.13.3`; the other crates in the same repo have their own tags (`livekit/v*`, `libwebrtc/v*`, ...), ignore them. Without an argument take the highest one: `git -C LivekitRtc/rust-sdks tag -l 'livekit-ffi/v*' --sort=-v:refname | head -1`. With a bare `X.Y.Z`, prefix it with `livekit-ffi/v`.
3. Confirm the GitHub release for that tag exists and carries all six binaries. `download_ffi.sh` prints a `✗` per missing platform but still exits 0, so check first:
   ```bash
   gh release view "livekit-ffi/vX.Y.Z" -R livekit/rust-sdks --json assets --jq '.assets[].name'
   ```
   Expected: `ffi-linux-x86_64.zip`, `ffi-linux-arm64.zip`, `ffi-macos-x86_64.zip`, `ffi-macos-arm64.zip`, `ffi-windows-x86_64.zip`, `ffi-windows-arm64.zip`. If the release is missing or incomplete (upstream may still be building it), stop here and tell the user. Do not move the submodule to a tag without binaries.
4. `git -C LivekitRtc/rust-sdks checkout "livekit-ffi/vX.Y.Z"`. Sanity check: `grep -m1 '^version' LivekitRtc/rust-sdks/livekit-ffi/Cargo.toml` must print `X.Y.Z`.
5. Read what changed upstream, because it drives everything after this:
   - `LivekitRtc/rust-sdks/livekit-ffi/CHANGELOG.md`: every section newer than `$OLD`.
   - `gh release view "livekit-ffi/vX.Y.Z" -R livekit/rust-sdks` for the release notes.
   - `git -C LivekitRtc/rust-sdks diff --stat "$OLD"..HEAD -- livekit-ffi/protocol/`
   - `git -C LivekitRtc/rust-sdks diff "$OLD"..HEAD -- livekit-ffi/protocol/`, looking for: new `oneof` cases in `FfiRequest`, `FfiEvent` and `RoomEvent`; new or removed fields in the option messages the SDK maps (`ConnectRequest`/`RoomOptions`, `TrackPublishOptions`, `E2eeOptions`, `KeyProviderOptions`, `VideoEncoding`, `AudioEncoding`, the data-stream and RPC messages); renamed or removed messages, fields and enum values.
   - `git -C LivekitRtc/rust-sdks diff "$OLD"..HEAD -- rust-toolchain.toml`: if the pinned Rust channel changed, update the example in the "Building from source" section of `LivekitRtc/README.md`.

## Phase 3: Regenerate the protos

1. `./generate_proto.sh`. It regenerates both SDKs. If it fails on the FFI protos, the local `protoc` is probably too old for syntax upstream started using (`proto3 optional` needs 3.15+, editions need a recent 2x release). Install a newer `protoc`; never patch the `.proto` files or the generated output.
2. `git status --short LivekitApi/proto` must be empty. If it is not, the local `protoc` differs from the one used for the last protocol regeneration and the churn is unrelated to this bump: `git checkout -- LivekitApi/proto` and mention it in the final report (it belongs to a `/sync-protocol` run).
3. `git diff --stat LivekitRtc/Proto/`. A `.g.cs` that disappears or appears means upstream removed, added or renamed a proto file. `generate_proto.sh` globs `*.proto`, so there is no list to maintain, but the `Google.Protobuf` package in `LivekitRtc/LivekitRtc.csproj` must be at least as new as `protoc --version` or the output will not compile.

## Phase 4: Native binaries

1. `LivekitRtc/download_ffi.sh livekit-ffi/vX.Y.Z` (from the repo root; it writes into `LivekitRtc/runtimes/` wherever it is run from). It downloads and overwrites binaries, so expect a permission prompt.
2. Verify, do not assume: the script must have printed `✓ Downloaded` six times, and
   ```bash
   git status --short LivekitRtc/runtimes
   ```
   must list the six libraries as modified (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`). One missing `✓` means that platform still has the old binary: stop and report, never leave a mixed set.
3. Never copy a binary built locally from `LivekitRtc/rust-sdks/target/` into `runtimes/` during a sync; releases ship the upstream builds only.

## Phase 5: Make it build

`dotnet build LivekitRtc`. Compile errors here come from hand-written code using something that changed upstream: a removed or renamed proto field, a changed enum member, a message that moved.

- Fix them in the hand-written files (`Room.cs`, `Participant.cs`, `Track*.cs`, `*Stream.cs`, `*Source.cs`, `E2EE.cs`, `DataStream.cs`, `Rpc.cs`, `Internal/*.cs`), never in `Proto/`.
- Keep the public C# API stable where the FFI allows it. `LivekitRtc` has no package validation (it is 0.x), so nothing flags a break for you: if a public signature has to change, note it for the changelog.
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

## Phase 8: Tests and formatting

```bash
# No Docker. These load the new native library through the FFI, so they also prove that the
# binary for this host platform initialises and answers requests.
dotnet test LivekitRtc.Tests --filter "FullyQualifiedName~FfiClientTest|FullyQualifiedName~SdkVersionTests|FullyQualifiedName~AudioCaptureMinimalTest|FullyQualifiedName~PinningTests|FullyQualifiedName~VideoFramePlanesTests|FullyQualifiedName~MappingTests"

# Docker (Testcontainers). RoomEventsTests connects two participants through the new binary and
# protos end to end and is the minimum; add the classes for the areas the upstream diff touched
# (AudioPublishTests, VideoPublishTests, AudioReceiveTests, DataStreamTests, RpcTests, E2EETests).
dotnet test LivekitRtc.Tests --filter "FullyQualifiedName~RoomEventsTests" --logger "console;verbosity=detailed"

dotnet csharpier check .
```

- If Testcontainers cannot pull its `ryuk` image, set `TESTCONTAINERS_RYUK_DISABLED=true`.
- Read the output, not just the pass count: an `FFI Panic:` line, or `[WARN] RoomEvent.<Name>: ... not found` lines, mean the binary and the SDK disagree. Report them with the evidence instead of editing tests to make them pass.
- If Docker is unavailable, say so explicitly in the final report; the no-Docker set is not sufficient proof for a native bump.

## Phase 9: CHANGELOG

Edit `LivekitRtc/CHANGELOG.md`. Write under the `## Unreleased` heading at the top, creating it if it does not exist. Never write under a version number: the release commit is what renames `Unreleased`. Use this structure, matching existing entries, wrapped at about 100 columns like the rest of the file:

```markdown
## Unreleased

- Update rust-sdks to [livekit-ffi/vX.Y.Z](https://github.com/livekit/rust-sdks/releases/tag/livekit-ffi%2FvX.Y.Z)
- New `Room.FooReceived` event raised when ... (parity with the Node/Python RTC SDKs).
- New `TrackPublishOptions.Bar` option to ... Unset keeps the previous behavior.
- Upstream change users will notice: ...
- Breaking change: `X.Y` now ... instead of ... (what users must do).
```

Only list user-visible changes. Skip upstream items about parts this package does not ship (uniffi bindings, platform audio devices, capture sources) unless they change behavior for .NET users. Don't touch `<Version>` in `LivekitRtc.csproj`: version bumps happen at release time, and `FfiClient.SdkVersion` is read from the assembly, so there is nothing to update by hand.

## Final report

End with:
- `livekit-ffi` `$OLD` → `vX.Y.Z`
- Files changed, grouped: submodule pointer, generated (count only), binaries (must be 6), generator script, hand-written, tests, docs
- New public API
- Breaking changes in the public C# API, if any (no package validation covers this package)
- Test results (no-Docker set, Docker classes run, skipped and why), plus any `FFI Panic` or `[WARN] RoomEvent` lines seen
- Open decisions left to the maintainer (unexposed requests, callbacks, room events, option fields)
- Suggested single commit, following the repo convention: `Update "rust-sdks" to livekit-ffi/vX.Y.Z` (submodule pointer, regenerated protos, the six binaries, any `generate_proto.sh` change, SDK code, tests, docs and changelog together). Leave it to the maintainer.
