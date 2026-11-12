#!/bin/bash

set -e

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

# The protoc version that reproduces the committed code. The tools are checked, and the Twirp plugin
# built, before anything is deleted
# shellcheck source=proto-tools.env
source "$SCRIPT_FOLDER/proto-tools.env"

if ! command -v protoc &>/dev/null; then
    echo "Error: protoc not found. Install protoc $PROTOC_VERSION:" >&2
    echo "https://github.com/protocolbuffers/protobuf/releases/tag/v$PROTOC_VERSION" >&2
    exit 1
fi
installed_protoc=$(protoc --version | awk '{print $2}')
if [ "$installed_protoc" != "$PROTOC_VERSION" ]; then
    echo "Error: found protoc $installed_protoc, but proto-tools.env pins $PROTOC_VERSION." >&2
    echo "Another protoc can rewrite every generated file. Install protoc $PROTOC_VERSION" >&2
    echo "(https://github.com/protocolbuffers/protobuf/releases/tag/v$PROTOC_VERSION), or move the" >&2
    echo "pin in proto-tools.env in a change of its own." >&2
    exit 1
fi

# The Twirp plugin, protoc-gen-twirp_csharp (github.com/pabloFuente/twirp-csharp), is built at the
# version tools/go.mod pins and passed to protoc by path, so a protoc-gen-twirp_csharp on PATH is
# never used
if ! command -v go &>/dev/null; then
    echo "Error: go not found. Install Go (https://go.dev/dl/) to build the Twirp plugin that tools/go.mod pins." >&2
    exit 1
fi
TOOLS_BIN=$(mktemp -d)
trap 'rm -rf "$TOOLS_BIN"' EXIT
TWIRP_PLUGIN="$TOOLS_BIN/protoc-gen-twirp_csharp"
(cd "$SCRIPT_FOLDER/tools" && go build -o "$TWIRP_PLUGIN" github.com/pabloFuente/twirp-csharp/cmd/protoc-gen-twirp_csharp)

echo "=== Generating proto files for LivekitApi (Server SDK) ==="

API_PROTOCOL=$SCRIPT_FOLDER/protocol/protobufs
API_OUT_CSHARP=$SCRIPT_FOLDER/LivekitApi/proto

# Check if protocol submodule exists
if [ ! -d "$API_PROTOCOL" ]; then
    echo "Error: protocol submodule not found at $API_PROTOCOL"
    echo "Please run: git submodule update --init protocol"
    exit 1
fi

rm -rf "$API_OUT_CSHARP"
mkdir -p "$API_OUT_CSHARP"

protoc \
    -I="$API_PROTOCOL" \
    --csharp_out="$API_OUT_CSHARP" \
    "$API_PROTOCOL"/livekit_agent_dispatch.proto \
    "$API_PROTOCOL"/livekit_agent.proto \
    "$API_PROTOCOL"/livekit_analytics.proto \
    "$API_PROTOCOL"/livekit_cloud_agent.proto \
    "$API_PROTOCOL"/livekit_connector_twilio.proto \
    "$API_PROTOCOL"/livekit_connector_whatsapp.proto \
    "$API_PROTOCOL"/livekit_connector.proto \
    "$API_PROTOCOL"/livekit_egress.proto \
    "$API_PROTOCOL"/livekit_ingress.proto \
    "$API_PROTOCOL"/livekit_internal.proto \
    "$API_PROTOCOL"/livekit_metrics.proto \
    "$API_PROTOCOL"/livekit_models.proto \
    "$API_PROTOCOL"/livekit_phone_number.proto \
    "$API_PROTOCOL"/livekit_room.proto \
    "$API_PROTOCOL"/livekit_rtc.proto \
    "$API_PROTOCOL"/livekit_sip.proto \
    "$API_PROTOCOL"/livekit_token_source.proto \
    "$API_PROTOCOL"/livekit_webhook.proto \
    "$API_PROTOCOL"/logger/options.proto

# The Twirp client class, Twirp.cs. Routes are relative to the host, and failed calls throw
# TwirpException
protoc -I "$API_PROTOCOL" --plugin=protoc-gen-twirp_csharp="$TWIRP_PLUGIN" \
    --twirp_csharp_opt=pathPrefix=twirp,className=Twirp,errorFactory=Livekit.Server.Sdk.Dotnet.TwirpException.FromResponse \
    --twirp_csharp_out="$API_OUT_CSHARP" \
    "$API_PROTOCOL"/livekit_room.proto \
    "$API_PROTOCOL"/livekit_egress.proto \
    "$API_PROTOCOL"/livekit_ingress.proto \
    "$API_PROTOCOL"/livekit_sip.proto \
    "$API_PROTOCOL"/livekit_agent.proto \
    "$API_PROTOCOL"/livekit_agent_dispatch.proto \
    "$API_PROTOCOL"/livekit_connector.proto \
    "$API_PROTOCOL"/livekit_webhook.proto \
    "$API_PROTOCOL"/livekit_models.proto \
    "$API_PROTOCOL"/livekit_metrics.proto \
    "$API_PROTOCOL"/livekit_analytics.proto \
    "$API_PROTOCOL"/livekit_phone_number.proto

# Move the code of both calls from the protos' csharp_namespace, "LiveKit.Proto", to
# "Livekit.Server.Sdk.Dotnet"
sed -i 's|namespace LiveKit\.Proto|namespace Livekit.Server.Sdk.Dotnet|g' "$API_OUT_CSHARP"/*.cs
sed -i 's|global::LiveKit\.Proto\.|global::Livekit.Server.Sdk.Dotnet.|g' "$API_OUT_CSHARP"/*.cs

echo "✓ LivekitApi proto files generated successfully"
echo ""
echo "=== Generating proto files for LivekitRtc (RTC SDK) ==="

RTC_PROTOCOL="$SCRIPT_FOLDER/LivekitRtc/rust-sdks/livekit-ffi/protocol"
RTC_OUT_CSHARP="$SCRIPT_FOLDER/LivekitRtc/Proto"

# Check if rust-sdks submodule exists
if [ ! -d "$RTC_PROTOCOL" ]; then
    echo "Error: rust-sdks submodule not found at $RTC_PROTOCOL"
    echo "Please run: git submodule update --init --recursive"
    exit 1
fi

# Create output directory if not exists and clean existing generated files
mkdir -p "$RTC_OUT_CSHARP"
rm -f "${RTC_OUT_CSHARP:?}"/*.g.cs

# Generate C# files from FFI proto definitions
protoc \
    -I="$RTC_PROTOCOL" \
    --csharp_out="$RTC_OUT_CSHARP" \
    --csharp_opt=file_extension=.g.cs \
    "$RTC_PROTOCOL"/*.proto

echo "✓ LivekitRtc proto files generated successfully"
echo ""
echo "All proto generation complete!"
