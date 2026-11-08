// This is a protoc plugin that generates csharp code for operating with Twirp APIs.
package main

import (
	_ "embed"
	"flag"
	"strings"
	"text/template"

	"google.golang.org/protobuf/compiler/protogen"
	"google.golang.org/protobuf/reflect/protoreflect"
	pluginpb "google.golang.org/protobuf/types/pluginpb"
)

const (
	outFileName = "GeneratedAPI.cs"
)

//go:embed template.tmpl
var fileTemplate string

func main() {
	// Set up our flags. The only one we care about for now is the server path prefix.
	var flags flag.FlagSet
	prefix := flags.String("pathPrefix", "/twirp", "the server path prefix to use, if modified from the Twirp default")

	// No special options for this generator
	opts := protogen.Options{ParamFunc: flags.Set}
	opts.Run(func(plugin *protogen.Plugin) error {
		plugin.SupportedFeatures = uint64(pluginpb.CodeGeneratorResponse_FEATURE_PROTO3_OPTIONAL)
		out := plugin.NewGeneratedFile(outFileName, "")

		template, err := template.New("file").
			Funcs(template.FuncMap{"Tab": tabNewlines, "Title": title}).
			Parse(fileTemplate)
		if err != nil {
			return err
		}

		in := jsData{
			Files:      plugin.Files,
			PathPrefix: *prefix,
		}

		return template.Execute(out, in)
	})
}

type jsData struct {
	Files      []*protogen.File
	PathPrefix string
}

// tabNewlines adds tabs (as two spaces) to the beginning of each line in the input string.
func tabNewlines(lines string) string {
	return "  " + strings.Replace(lines, "\n", "\n  ", -1)
}

func title(name protoreflect.Name) string {
	return strings.Title(string(name))
}
