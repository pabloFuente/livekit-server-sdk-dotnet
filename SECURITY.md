# Security policy

## Reporting a vulnerability

Report vulnerabilities privately, never in a public issue or pull request: open a
[private vulnerability report](https://github.com/pabloFuente/livekit-server-sdk-dotnet/security/advisories/new)
from the **Security** tab of this repository. Include the package (`Livekit.Server.Sdk.Dotnet` or
`Livekit.Rtc.Dotnet`) and its version, what an attacker can achieve and how to reproduce it. The fix
ships in a new release of the affected package, followed by a GitHub security advisory. Security
fixes are released for the latest version of each package.

Vulnerabilities in LiveKit itself (the server, `livekit/protocol`, the Rust SDK and the native
`livekit_ffi` library that `Livekit.Rtc.Dotnet` bundles) belong to LiveKit: report them privately in
[livekit/livekit](https://github.com/livekit/livekit/security/advisories/new). Once LiveKit releases
a fixed `livekit_ffi`, a new release of `Livekit.Rtc.Dotnet` bundles it.

## How dependencies are checked

- `security/check_vulnerabilities.py` looks for known vulnerabilities in the NuGet packages of every
  project (direct and transitive, tests and examples included), the .NET tools, the actions pinned in
  the workflows and the Rust crates compiled into `livekit_ffi`. It runs in the `build` check of
  every pull request and push to `main` (all but the crates), daily, and before every release.
- Following `security/policy.json`, a critical or high finding fails the scan, a moderate one is
  flagged for review and a low one is reported. A finding that cannot be fixed yet is only accepted
  through an exception in that file, for one package and one advisory, with a justification and an
  expiry date at most 90 days ahead; an expired exception fails the scan again.
- Dependabot opens security updates at once and version updates weekly, once a release is a week
  old. Dependency review, CodeQL and secret scanning run on GitHub. The Go module of the Twirp code
  generator (`tools/protoc-gen-twirpcs`), a build-time tool that no package ships, is covered only
  by Dependabot and dependency review.
- The native binaries are checked against the SHA-256 digests GitHub recorded for the upstream
  release assets (`LivekitRtc/download_ffi.sh --verify`). Their Rust crates are scanned as `cargo
  tree` resolves them at the pinned `livekit-ffi` tag. The prebuilt libwebrtc inside them has no
  metadata a scanner can match, so it is kept current by following every `livekit-ffi` release.
- Every release carries a signed build provenance attestation for the `.nupkg` and the assemblies in
  it: `gh attestation verify <file> --repo pabloFuente/livekit-server-sdk-dotnet`.
