# protoc-gen-twirpcs

The `protoc` plugin that generates `LivekitApi/proto/Twirp.cs` (as `GeneratedAPI.cs`, which
`generate_proto.sh` patches and renames). `generate_proto.sh` builds it from this directory on every
run and passes it to `protoc` with `--plugin`, so a `protoc-gen-twirpcs` on `PATH` is never used.

## Origin

Vendored from [twirp-gen](https://pkg.go.dev/github.com/seanpfeifer/twirp-gen/cmd/protoc-gen-twirpcs)
by Sean Pfeifer, MIT licensed (see [LICENSE](LICENSE)): release v1.1.0, tag commit
`234b773d2281e3f7bc022ca1b237f4d10a15f8c5` (2022-11-13), its last release. The GitHub repository
`github.com/seanpfeifer/twirp-gen` no longer exists; the Go module proxy still serves the original
(`https://proxy.golang.org/github.com/seanpfeifer/twirp-gen/@v/v1.1.0.zip`).

`main.go`, `template.tmpl` and `LICENSE` are copied unchanged from `cmd/protoc-gen-twirpcs` of
that release. `go.mod` is new: it gives the module a path in this repository and requires a current
`google.golang.org/protobuf` instead of v1.28.0, which produces the same output. Dependabot keeps
that dependency up to date, and the `Generated code` workflow checks that every change here still
reproduces the committed `Twirp.cs` byte for byte.
