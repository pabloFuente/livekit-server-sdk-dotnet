package main

import (
	"strings"
	"testing"

	"google.golang.org/protobuf/encoding/prototext"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protodesc"
	"google.golang.org/protobuf/types/descriptorpb"
	"google.golang.org/protobuf/types/known/emptypb"
	"google.golang.org/protobuf/types/pluginpb"
)

// shopProto uses a nested message, a message of another package and the default namespace. protogen
// requires a go_package in every file (every livekit proto has one).
const shopProto = `
name: "shop.proto"
package: "acme.shop_v2"
dependency: "google/protobuf/empty.proto"
syntax: "proto3"
options { go_package: "acme/shop" }
message_type { name: "Item" }
message_type {
  name: "Order"
  nested_type { name: "Line" nested_type { name: "Price" } }
}
service {
  name: "Shop"
  method { name: "GetItem" input_type: ".acme.shop_v2.Item" output_type: ".acme.shop_v2.Item" }
  method { name: "Price" input_type: ".acme.shop_v2.Order.Line.Price" output_type: ".google.protobuf.Empty" }
}
`

// importedProto declares a service, but is only a dependency of the file to generate.
const importedProto = `
name: "imported.proto"
package: "acme.imported"
syntax: "proto3"
options { go_package: "acme/imported" csharp_namespace: "Acme.Imported.Proto" }
message_type { name: "Thing" }
service {
  name: "Imported"
  method { name: "Get" input_type: ".acme.imported.Thing" output_type: ".acme.imported.Thing" }
}
`

func parseFile(t *testing.T, text string) *descriptorpb.FileDescriptorProto {
	t.Helper()
	file := &descriptorpb.FileDescriptorProto{}
	if err := prototext.Unmarshal([]byte(text), file); err != nil {
		t.Fatalf("bad test proto: %v", err)
	}
	return file
}

// run generates the C# file for the last of files, with the others as its dependencies, and returns
// the response.
func run(t *testing.T, parameter string, files ...string) *pluginpb.CodeGeneratorResponse {
	t.Helper()
	req := &pluginpb.CodeGeneratorRequest{
		Parameter: proto.String(parameter),
		ProtoFile: []*descriptorpb.FileDescriptorProto{protodesc.ToFileDescriptorProto(emptypb.File_google_protobuf_empty_proto)},
	}
	for _, text := range files {
		req.ProtoFile = append(req.ProtoFile, parseFile(t, text))
	}
	req.FileToGenerate = []string{req.ProtoFile[len(req.ProtoFile)-1].GetName()}

	var cfg config
	plugin, err := options(&cfg).New(req)
	if err != nil {
		t.Fatalf("options: %v", err)
	}
	if err := generate(plugin, cfg); err != nil {
		plugin.Error(err)
	}
	return plugin.Response()
}

func generated(t *testing.T, parameter string, files ...string) string {
	t.Helper()
	resp := run(t, parameter, files...)
	if resp.Error != nil {
		t.Fatalf("generation failed: %s", resp.GetError())
	}
	if len(resp.File) != 1 {
		t.Fatalf("expected one file, got %d", len(resp.File))
	}
	return resp.File[0].GetContent()
}

func failure(t *testing.T, files ...string) string {
	t.Helper()
	resp := run(t, "", files...)
	if resp.Error == nil {
		t.Fatalf("generation succeeded, expected an error")
	}
	return resp.GetError()
}

func assertContains(t *testing.T, content string, wants ...string) {
	t.Helper()
	for _, want := range wants {
		if !strings.Contains(content, want) {
			t.Errorf("generated code does not contain\n  %s\n\n%s", want, content)
		}
	}
}

func TestTypesAreNamedAfterTheirOwnFile(t *testing.T) {
	content := generated(t, "", shopProto)
	assertContains(t, content,
		// The package in PascalCase, as protoc's C# generator names it without csharp_namespace
		"public static async Task<global::Acme.ShopV2.Item> GetItem(HttpClient client, global::Acme.ShopV2.Item req) {",
		// Nested messages live in the Types class of their parent
		"Price(HttpClient client, global::Acme.ShopV2.Order.Types.Line.Types.Price req)",
		// A message of another package keeps that package's csharp_namespace
		"Task<global::Google.Protobuf.WellKnownTypes.Empty>",
		"global::Google.Protobuf.WellKnownTypes.Empty.Parser.ParseFrom",
	)
}

func TestCsharpNamespaceOptionWins(t *testing.T) {
	file := strings.Replace(shopProto, `go_package: "acme/shop"`, `go_package: "acme/shop" csharp_namespace: "Acme.Shop.Proto"`, 1)
	assertContains(t, generated(t, "", file), "Task<global::Acme.Shop.Proto.Item> GetItem(HttpClient client, global::Acme.Shop.Proto.Item req)")
}

func TestUnderscoresToPascalCase(t *testing.T) {
	for in, want := range map[string]string{
		"":                "",
		"livekit":         "Livekit",
		"google.protobuf": "Google.Protobuf",
		"foo_bar.baz2qux": "FooBar.Baz2Qux",
		"already.Pascal":  "Already.Pascal",
	} {
		if got := underscoresToPascalCase(in); got != want {
			t.Errorf("underscoresToPascalCase(%q) = %q, want %q", in, got, want)
		}
	}
}

func TestRoutesAreConstants(t *testing.T) {
	content := generated(t, "pathPrefix=twirp", shopProto)
	assertContains(t, content,
		"  internal static class Routes {\n    internal static class Shop {\n"+
			"      internal const string GetItem = \"twirp/acme.shop_v2.Shop/GetItem\";\n"+
			"      internal const string Price = \"twirp/acme.shop_v2.Shop/Price\";\n    }\n  }\n}\n",
		"DoRequest<global::Acme.ShopV2.Item, global::Acme.ShopV2.Item>(client, Routes.Shop.GetItem, req,",
	)
	if strings.Count(content, `"twirp/`) != 2 {
		t.Errorf("every route should be written once, in Routes:\n%s", content)
	}
}

func TestDefaultPathPrefix(t *testing.T) {
	assertContains(t, generated(t, "", shopProto), `GetItem = "/twirp/acme.shop_v2.Shop/GetItem";`)
}

func TestAwaitsDoNotCaptureTheContext(t *testing.T) {
	content := generated(t, "", shopProto)
	assertContains(t, content,
		"await client.PostAsync(address, content).ConfigureAwait(false)",
		"await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)",
		"req, global::Acme.ShopV2.Item.Parser.ParseFrom).ConfigureAwait(false);",
	)
	if awaits, configured := strings.Count(content, "await "), strings.Count(content, ".ConfigureAwait(false)"); awaits != configured {
		t.Errorf("%d awaits but %d ConfigureAwait(false)", awaits, configured)
	}
}

func TestClassName(t *testing.T) {
	resp := run(t, "className=Twirp", shopProto)
	if got := resp.File[0].GetName(); got != "Twirp.cs" {
		t.Errorf("file name = %q, want Twirp.cs", got)
	}
	assertContains(t, resp.File[0].GetContent(), "public class Twirp {", "private static Twirp.Exception createException(")
}

func TestErrorFactory(t *testing.T) {
	content := generated(t, "errorFactory=Acme.Errors.FromResponse", shopProto)
	assertContains(t, content, "throw global::Acme.Errors.FromResponse(errorJSON, (int)response.StatusCode);")
	for _, unused := range []string{"createException", "parseJSONString"} {
		if strings.Contains(content, unused) {
			t.Errorf("%s is generated although errorFactory replaces it", unused)
		}
	}
}

func TestBuiltInErrorParsingWithoutErrorFactory(t *testing.T) {
	assertContains(t, generated(t, "", shopProto), "throw createException(errorJSON);", "private static string parseJSONString(")
}

func TestOnlyTheFilesToGenerateAreGenerated(t *testing.T) {
	file := strings.Replace(shopProto, `dependency: "google/protobuf/empty.proto"`,
		`dependency: "google/protobuf/empty.proto" dependency: "imported.proto"`, 1)
	content := generated(t, "", importedProto, file)
	if strings.Contains(content, "Imported") {
		t.Errorf("the service of a dependency was generated:\n%s", content)
	}
}

func TestEditionsAreSupported(t *testing.T) {
	file := strings.Replace(shopProto, `syntax: "proto3"`, `syntax: "editions" edition: EDITION_2023`, 1)
	resp := run(t, "", file)
	if resp.Error != nil {
		t.Fatalf("generation failed: %s", resp.GetError())
	}
	// protoc refuses to run a plugin on an editions file unless the response declares it
	if resp.GetSupportedFeatures()&uint64(pluginpb.CodeGeneratorResponse_FEATURE_SUPPORTS_EDITIONS) == 0 {
		t.Error("FEATURE_SUPPORTS_EDITIONS is not declared")
	}
	if resp.GetMaximumEdition() != int32(descriptorpb.Edition_EDITION_2023) {
		t.Errorf("maximum edition = %d", resp.GetMaximumEdition())
	}
}

func TestStreamingRpcsAreRejected(t *testing.T) {
	file := strings.Replace(shopProto, `name: "GetItem" input_type`, `name: "GetItem" client_streaming: true input_type`, 1)
	if err := failure(t, file); !strings.Contains(err, "acme.shop_v2.Shop.GetItem: Twirp does not support streaming RPCs") {
		t.Errorf("unexpected error: %s", err)
	}
}

func TestClashingMethodsAreRejected(t *testing.T) {
	file := shopProto + `service {
  name: "Warehouse"
  method { name: "GetItem" input_type: ".acme.shop_v2.Item" output_type: ".acme.shop_v2.Order" }
}`
	if err := failure(t, file); !strings.Contains(err, "would both generate GetItem(global::Acme.ShopV2.Item)") {
		t.Errorf("unexpected error: %s", err)
	}
}

func TestMethodsNamedLikeAMemberAreRejected(t *testing.T) {
	for _, name := range []string{"Routes", "Exception", "ErrorCode", "GeneratedAPI", "Shop"} {
		file := strings.Replace(shopProto, `name: "GetItem"`, `name: "`+name+`"`, 1)
		if err := failure(t, file); !strings.Contains(err, "would clash with a member of the generated code") {
			t.Errorf("method %s: unexpected error: %s", name, err)
		}
	}
}
