# RTC reliability tests

Stress, concurrency, lifecycle and leak tests for `Livekit.Rtc.Dotnet`. They test the
**.NET layer** and its contract with the native `livekit_ffi` library, not the Rust SDK behind it:

- every handle the FFI hands over is wrapped once and released exactly once, including on error,
  cancellation, concurrent disposal and finalization paths;
- managed buffers stay pinned while the FFI reads them, and pointers the FFI returns are read only
  while their handle is alive;
- the dispatch of native callbacks to `Room` events (FIFO order, exactly once, isolation from
  throwing, slow and reentrant handlers, nothing after `Disconnected`);
- the connection state machine (connect, disconnect, dispose, cancellation, reconnection, server
  outages) and the thread safety of the public API.

How the FFI encodes media, reconnects or schedules its tasks is not asserted. Reconnections are
forced through `Room.SimulateScenarioAsync`, or with real outages of a LiveKit server in Docker.

## Layout

| File | Needs Docker | What it covers |
| --- | --- | --- |
| `NativeLifecycleTests` | no | Each resource wrapper (`AudioSource`, `VideoSource`, `EncodedVideoSource`, `AudioResampler`, local tracks): dispose once, repeatedly, from 8 threads at once, never (finalizer); use after dispose; failed native requests; dispose during a pending capture; dispose with audio queued (native release race); cancelled captures; owned buffers of frame conversions; the handle each kind of room event carries, released exactly once when the room does not apply the event |
| `NativeConcurrencyTests` | no | Dispose racing `AudioResampler.Push`, concurrent push/flush, pinning under compacting GC pressure (end to end through the FFI), sync wrappers on a UI-like single-threaded `SynchronizationContext`, wrappers used from many threads |
| `NativeResourceLeakTests` | no | Hundreds (extended: thousands) of lifecycle cycles per wrapper with bounded growth of handles, native heap, descriptors and threads |
| `RoomLifecycleTests` | yes | Connect, disconnect and dispose: repeated, concurrent, during a connect, cancelled at many points, failed, server-initiated, on a UI-like thread, one `Room` reused for many connections, ended rooms not retained by the SDK |
| `CallbackConcurrencyTests` | yes | Order and exactly-once delivery at high rate, throwing handlers, a blocked handler stalling only its own room, subscribing and unsubscribing during delivery, reentrant handlers, a track unpublished before the chain reaches its publication, the disposal boundary under a data flood, RPC served while the event chain is blocked |
| `ConcurrentOperationsTests` | yes | Many rooms in parallel, concurrent publish/unpublish/mute, concurrent data and text streams, cancelled `PublishTrackAsync`, invalid double publish, media streams read and disposed concurrently while frames flow |
| `RemoteTrackEventOrderTests` | yes | The out-of-order remote track events the FFI produces when a participant leaves while its tracks are subscribed (unsubscription first, twice, or after the departure), replayed into a connected room with `SyntheticRoomEvents`: balanced events, consistent state, every handle released, no warning |
| `ReconnectionTests` | yes | Repeated full reconnects and resumes of a publisher (publications kept and re-keyed, event order, no duplicated handlers, consistent remote view) and of a subscriber (its view of the remote tracks rebuilt), disconnect or dispose during a reconnection, cancelled scenario, handles across reconnect cycles |
| `ServerOutageTests` | yes | `docker pause` of the server: a short outage ends in `Reconnected`; extended: `DisconnectAsync` while the server does not answer, an outage longer than the reconnection budget ends in a clean, reusable room |
| `RoomResourceLeakTests` | yes | Connect, publish, subscribe and disconnect cycles on both sides of a session; incoming data streams must not stay in native memory |
| `SoakTests` | yes | Extended only: a seeded mix of all of the above by several participants for `LIVEKIT_SOAK_MINUTES` |
| `Infrastructure/` | | Shared helpers, described below |

The Docker tests share `ReliabilityServerFixture`: one `livekit/livekit-server` container without
Redis or browser, which starts in seconds and can be paused without affecting the e2e tests.

## Tiers

| Tier | Selected by | Where it runs | Duration (16-core machine) |
| --- | --- | --- | --- |
| Pull request | default | every RTC test run: `build` (linux-x64), `rtc-linux-arm64`, and the native classes in `rtc-native` (win-x64, win-arm64, osx-x64, osx-arm64) | about 4 min in all (1 min of it native) |
| Extended | `LIVEKIT_STRESS=extended` | nightly `rtc-reliability.yml` on all six platforms; before every `rtc-` release (`publish.yml`) without the soak | about 20 min plus the soak (10 min nightly); 3 min native only |

`LIVEKIT_STRESS=extended` multiplies the loop counts (`StressLevel.Scale`) and enables the tests
marked `[ExtendedFact]`, which are reported as skipped otherwise. The nightly workflow runs the soak in a test
process of its own, so a native crash during it cannot hide the other results, and uploads the
runtime's crash report and a minidump with the results when the test host crashes.

```bash
# Pull request tier
dotnet test LivekitRtc.Tests --filter "Category=Reliability"
dotnet test LivekitRtc.Tests --filter "FullyQualifiedName~Reliability.Native"      # no Docker
dotnet test LivekitRtc.Tests --filter "FullyQualifiedName~Reliability.ReconnectionTests"

# Extended tier, with a 2 minute soak and the SDK's stderr kept for inspection
LIVEKIT_STRESS=extended LIVEKIT_SOAK_MINUTES=2 LIVEKIT_TEST_STDERR_FILE=/tmp/sdk-stderr.log \
  dotnet test LivekitRtc.Tests -c Release --filter "Category=Reliability" --logger "console;verbosity=detailed"
```

The two reliability collections are declared with `DisableParallelization`, so xUnit runs them
after every parallel test, one test at a time: the process-wide measurements below then belong to
the test that takes them. Repeat a class to look for intermittent failures with a shell loop
(`for i in $(seq 10); do dotnet test ... || break; done`).

## How leaks are detected

`ResourceSnapshot.Capture()` forces full blocking compacting collections and runs the finalizers,
then reads:

- **SDK handle accounting** (primary signal, exact): `FfiHandle.LiveCount` (handles wrapped and not
  released) and `FfiHandle.FailedReleaseCount` (releases the native library rejected: a double
  release or two owners). Both are `internal` counters in `LivekitRtc/Internal/FfiHandle.cs`, read
  through `InternalsVisibleTo`; they are not part of the public API.
- **Native heap**: bytes allocated through the C allocator used by both the Rust FFI and libwebrtc
  (glibc `mallinfo2` on Linux, `malloc_zone_statistics` on macOS). It catches what the handle
  accounting cannot see: an owned handle the SDK received but never wrapped keeps its native
  object allocated. Windows has no precise source, so the private bytes of the process are used
  with a much larger budget.
- **Open descriptors** (`/proc/self/fd`, `/dev/fd`; kernel handles on Windows) and **threads**:
  a room that was never closed keeps its sockets and threads.
- **Managed heap** after collection.

Assertions compare a snapshot taken after a warm-up with one taken after many cycles, against a
`LeakBudget` that one leaked handle, buffer or socket per cycle exceeds by far, and retry for a
few seconds because native teardown is partly asynchronous. A double release always fails.
Rooms that ended must also become unreachable (`RoomLifecycleTests.EndedRooms_AreNotRetainedByTheSdk`),
since a `Room` kept alive by the process-wide FFI client holds no native handle at all.

Each test also opens an `SdkLog` scope: the SDK reports what it drops or cannot deliver on stderr
(`[WARN] RoomEvent...`, `[ERROR] Room.X handler threw`, `FFI Panic`) instead of throwing, and a
reliability test fails on such a line unless it provokes it on purpose.

## Writing a new test

- Put it in one of the two collections (`NativeReliabilityCollection.Name` without Docker,
  `RoomReliabilityCollection.Name` with the server fixture) and tag the class
  `[Trait("Category", "Reliability")]`. A new native class must start with `Native` so the
  `rtc-native` filter of `dotnet.yml` (`FullyQualifiedName~Reliability.Native`) picks it up.
- Synchronize on events (`Eventually.Signal`, `Eventually.TrueAsync`, `Task.Within`), never on fixed
  delays, and bound every wait so a deadlock fails with its name instead of hanging the run. A bound
  only starts once the operation has returned its task, and every SDK call begins with a
  synchronous request to the native library: run an operation that may block there on the thread
  pool, as the soak's `StepAsync` does.
- Use unique room names (`ReliabilityServerFixture.NewRoomName`), and check the server's view
  (`IdentitiesAsync`, `TrackSidsAsync`) for what the SDK says it released.
- Scale loops with `StressLevel.Scale(pullRequest, extended)`; mark tests longer than about 20 s
  `[ExtendedFact]` with `[Trait("Tier", "Extended")]`.
- Prove that a new leak or race test fails without its fix (temporarily revert the fix): a test
  that only executes code does not count.

## Known limitations

- The FFI exposes no count of its live handles, so a handle the SDK never wraps is only visible
  through the native heap, which cannot see small leaks (an audio frame is about 1 KB).
- Native heap growth that no SDK handle explains is reported, not asserted, by the soak: when
  several participants subscribe to each other concurrently, a few hundred KB per session stay
  allocated after every handle was released and no event handed over a handle the SDK ignored.
  That memory is outside the .NET layer.
- Windows has no precise native heap measurement (private bytes only).
- Hosted Windows and macOS runners cannot start Linux containers: only the native tier runs there.
  The Docker tiers run on linux-x64 and linux-arm64.
- The soak can crash the test host with a native segmentation fault inside `liblivekit_ffi`
  (livekit-ffi v0.13.4), in about one run in three on a loaded machine: a use-after-free in
  an async task of the native library, on one of its own worker threads, at two recurring
  places. No .NET frame is in the faulting call chain, and it happens with and without the
  SDK changes made while it was investigated, so it is reported as an upstream issue. To
  analyze one, run the soak with `DOTNET_DbgEnableMiniDump=1 DOTNET_DbgMiniDumpType=4` and open
  the dump with `gdb /usr/lib/dotnet/dotnet <dump>` (`thread apply all bt`, then the thread
  with `<signal handler called>`). Until it is fixed the soak can fail nightly, and the release
  validation leaves it out.
- The native audio source frees its queue before it stops the 10 ms task that plays the queue
  out (webrtc-sys `AudioTrackSource`), so releasing a source with audio queued could leave that
  task spinning forever, which hung `Dispose`, or corrupt the heap. The SDK empties the queue
  before every release, but audio captured while the source is being disposed can still refill
  it.
- Paths that need a misbehaving server (a `TrackSubscribed` for an unknown participant, the release
  path fixed in PR #159) cannot be provoked from outside the SDK and are not covered.
