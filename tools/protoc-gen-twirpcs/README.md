# protoc-gen-twirpcs

The `protoc` plugin that generates `LivekitApi/proto/Twirp.cs`. `generate_proto.sh` builds it from
this directory on every run and passes it to `protoc` with `--plugin`, so a `protoc-gen-twirpcs` on
`PATH` is never used.

For every RPC of the files it is asked to generate, the class gets a public static method that
sends the call with a caller-supplied `HttpClient`, and an internal route constant,
`<class>.Routes.<Service>.<Method>`, which the service clients of `Livekit.Server.Sdk.Dotnet` use.
Request and response types are named as protoc's C# generator names them: the `csharp_namespace`
of their own file (or its package in PascalCase), with nested messages under `Types`.

## Options

Passed as `--twirpcs_opt=key=value,key=value`:

| Option | Default | |
| --- | --- | --- |
| `pathPrefix` | `/twirp` | Start of every route. `generate_proto.sh` passes `twirp`, so routes are relative to the host. |
| `className` | `GeneratedAPI` | Name of the class and of its file. |
| `errorFactory` | (none) | C# method, without `global::`, that builds the exception of a failed call: `Exception F(string body, int status)`. Without it, the class parses the Twirp error itself (badly: see Changes). |

Streaming RPCs (Twirp has none) and RPCs whose generated members would clash fail the generation.

## Origin

Vendored from [twirp-gen](https://pkg.go.dev/github.com/seanpfeifer/twirp-gen/cmd/protoc-gen-twirpcs)
by Sean Pfeifer, MIT licensed (see [LICENSE](LICENSE)): release v1.1.0, tag commit
`234b773d2281e3f7bc022ca1b237f4d10a15f8c5` (2022-11-13), its last release. The GitHub repository
`github.com/seanpfeifer/twirp-gen` no longer exists; the Go module proxy still serves the original
(`https://proxy.golang.org/github.com/seanpfeifer/twirp-gen/@v/v1.1.0.zip`).

## Changes from v1.1.0

- Types come from their own file's `csharp_namespace`. The original qualified every type with the
  service's proto package in title case (`Livekit.X`), wrong for a message of another package or a
  nested one, and `generate_proto.sh` fixed its output with `sed`.
- Only the files `protoc` asks for are generated, not the services of their imports.
- `className` and `errorFactory` options, and route constants used by the methods.
- Every `await` uses `ConfigureAwait(false)`: callers blocking on a call from a UI thread or another
  single-threaded `SynchronizationContext` deadlocked.
- The built-in error parsing (kept when `errorFactory` is unset) cuts messages at the first escaped
  quote, ignores `meta` and the HTTP status, and maps the wire code `dataloss` to `Unknown`.
  `Livekit.Server.Sdk.Dotnet` passes `TwirpException.FromResponse` instead.
- Declares support for editions up to 2023, so `protoc` accepts editions files.
- Routes use the service and method names as written in the proto (the Twirp specification), not
  their Go names, and unit tests (`main_test.go`).
- `go.mod` is new: a module path in this repository and a current `google.golang.org/protobuf`,
  which Dependabot keeps up to date. The `Generated code` workflow runs `gofmt`, `go vet` and the
  tests, and checks that `generate_proto.sh` still reproduces the committed code byte for byte.
