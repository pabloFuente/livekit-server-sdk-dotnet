// This is a protoc plugin that generates csharp code for operating with Twirp APIs.
//
// Vendored from github.com/seanpfeifer/twirp-gen v1.1.0 and changed since: see README.md.
package main

import (
	_ "embed"
	"flag"
	"fmt"
	"strings"
	"text/template"

	"google.golang.org/protobuf/compiler/protogen"
	"google.golang.org/protobuf/reflect/protoreflect"
	"google.golang.org/protobuf/types/descriptorpb"
	"google.golang.org/protobuf/types/pluginpb"
)

//go:embed template.tmpl
var fileTemplate string

var fileTmpl = template.Must(template.New("file").
	Funcs(template.FuncMap{"Tab": tabNewlines}).
	Parse(fileTemplate))

// config holds the plugin parameters, passed as --twirpcs_opt=key=value,key=value.
type config struct {
	// PathPrefix starts every route, "/twirp" (the Twirp default) unless set.
	PathPrefix string
	// ClassName names the generated class and its file.
	ClassName string
	// ErrorFactory, when set, is the fully qualified C# method, without "global::", that builds the
	// exception thrown for a non-2xx response from the body and the HTTP status:
	// Exception Factory(string body, int status). Unset, the response is parsed by the generated code.
	ErrorFactory string
}

func main() {
	var cfg config
	options(&cfg).Run(func(plugin *protogen.Plugin) error {
		return generate(plugin, cfg)
	})
}

func options(cfg *config) protogen.Options {
	var flags flag.FlagSet
	flags.StringVar(&cfg.PathPrefix, "pathPrefix", "/twirp", "the server path prefix to use, if modified from the Twirp default")
	flags.StringVar(&cfg.ClassName, "className", "GeneratedAPI", "the name of the generated class and file")
	flags.StringVar(&cfg.ErrorFactory, "errorFactory", "", "the C# method that builds the exception of a failed call from (body, status)")
	return protogen.Options{ParamFunc: flags.Set}
}

type fileData struct {
	config
	Services []service
}

type service struct {
	// Name is the C# name of the service's class in Routes.
	Name    string
	Methods []method
}

type method struct {
	// Name is the C# name of the method and of its route constant.
	Name     string
	Service  string
	Route    string
	Request  string
	Response string
	Comments string
}

// Members of the generated class that an RPC method cannot share a name with.
var reservedNames = map[string]bool{
	"CONTENT_TYPE_PROTOBUF": true, "ErrorCode": true, "Exception": true, "Routes": true, "doParsing": true,
}

func generate(plugin *protogen.Plugin, cfg config) error {
	plugin.SupportedFeatures = uint64(pluginpb.CodeGeneratorResponse_FEATURE_PROTO3_OPTIONAL |
		pluginpb.CodeGeneratorResponse_FEATURE_SUPPORTS_EDITIONS)
	// The generator reads names, comments and csharp_namespace, which no edition changes
	plugin.SupportedEditionsMinimum = descriptorpb.Edition_EDITION_PROTO2
	plugin.SupportedEditionsMaximum = descriptorpb.Edition_EDITION_2023

	if cfg.ClassName == "" {
		return fmt.Errorf("className must not be empty")
	}
	data := fileData{config: cfg}
	services := map[string]protoreflect.FullName{}
	signatures := map[string]protoreflect.FullName{}
	for _, file := range plugin.Files {
		if !file.Generate {
			continue
		}
		for _, svc := range file.Services {
			s := service{Name: svc.GoName}
			if prev, ok := services[s.Name]; ok {
				return fmt.Errorf("services %s and %s would both generate Routes.%s", prev, svc.Desc.FullName(), s.Name)
			}
			services[s.Name] = svc.Desc.FullName()
			if s.Name == "Routes" {
				return fmt.Errorf("service %s would generate the class Routes.Routes", svc.Desc.FullName())
			}
			for _, m := range svc.Methods {
				if m.Desc.IsStreamingClient() || m.Desc.IsStreamingServer() {
					return fmt.Errorf("%s: Twirp does not support streaming RPCs", m.Desc.FullName())
				}
				name := m.GoName
				if reservedNames[name] || name == cfg.ClassName || name == s.Name {
					return fmt.Errorf("%s: a method named %s would clash with a member of the generated code", m.Desc.FullName(), name)
				}
				request := csharpType(m.Input.Desc)
				signature := name + "(" + request + ")"
				if prev, ok := signatures[signature]; ok {
					return fmt.Errorf("%s and %s would both generate %s", prev, m.Desc.FullName(), signature)
				}
				signatures[signature] = m.Desc.FullName()
				s.Methods = append(s.Methods, method{
					Name:    name,
					Service: s.Name,
					// The route names the service and the method as written in the proto
					Route:    fmt.Sprintf("%s/%s/%s", cfg.PathPrefix, svc.Desc.FullName(), m.Desc.Name()),
					Request:  request,
					Response: csharpType(m.Output.Desc),
					Comments: m.Comments.Leading.String(),
				})
			}
			if len(s.Methods) > 0 {
				data.Services = append(data.Services, s)
			}
		}
	}

	out := plugin.NewGeneratedFile(cfg.ClassName+".cs", "")
	return fileTmpl.Execute(out, data)
}

// csharpType is the name protoc's C# generator gives a message: the namespace of its own file, then
// its enclosing messages, each followed by ".Types".
func csharpType(msg protoreflect.MessageDescriptor) string {
	name := string(msg.Name())
	for parent, ok := msg.Parent().(protoreflect.MessageDescriptor); ok; parent, ok = parent.Parent().(protoreflect.MessageDescriptor) {
		name = string(parent.Name()) + ".Types." + name
	}
	if ns := csharpNamespace(msg.ParentFile()); ns != "" {
		name = ns + "." + name
	}
	return "global::" + name
}

// csharpNamespace is the csharp_namespace option of the file or, without one, its package in
// PascalCase, as protoc's C# generator does.
func csharpNamespace(file protoreflect.FileDescriptor) string {
	if opts, ok := file.Options().(*descriptorpb.FileOptions); ok && opts.CsharpNamespace != nil {
		return opts.GetCsharpNamespace()
	}
	return underscoresToPascalCase(string(file.Package()))
}

// underscoresToPascalCase mirrors UnderscoresToCamelCase(name, true, true) of protoc's C# generator:
// letters after a separator or a digit are capitalized, and separators other than '.' are dropped.
func underscoresToPascalCase(name string) string {
	var b strings.Builder
	capNext := true
	for _, c := range name {
		switch {
		case 'a' <= c && c <= 'z':
			if capNext {
				c -= 'a' - 'A'
			}
			b.WriteRune(c)
			capNext = false
		case 'A' <= c && c <= 'Z':
			b.WriteRune(c)
			capNext = false
		case '0' <= c && c <= '9':
			b.WriteRune(c)
			capNext = true
		default:
			if c == '.' {
				b.WriteRune(c)
			}
			capNext = true
		}
	}
	return b.String()
}

// tabNewlines adds tabs (as two spaces) to the beginning of each line in the input string.
func tabNewlines(lines string) string {
	return "  " + strings.ReplaceAll(lines, "\n", "\n  ")
}
