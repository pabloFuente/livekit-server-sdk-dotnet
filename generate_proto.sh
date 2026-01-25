#!/bin/bash

set -e

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

echo "=== Generating proto files for LivekitApi (Server SDK) ==="

API_PROTOCOL=$SCRIPT_FOLDER/protocol/protobufs
API_OUT_CSHARP=$SCRIPT_FOLDER/LivekitApi/proto

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

# Patch proto stubs
# 1. Modify the namespace from "Livekit.Proto" to "Livekit.Server.Sdk.Dotnet"
sed -i 's|namespace LiveKit.Proto|namespace Livekit.Server.Sdk.Dotnet|g' "$API_OUT_CSHARP"/*.cs
sed -i 's|global::LiveKit.Proto.|global::Livekit.Server.Sdk.Dotnet.|g' "$API_OUT_CSHARP"/*.cs

# Twirp (generated with https://github.com/seanpfeifer/twirp-gen)

protoc -I "$API_PROTOCOL" --twirpcs_out=pathPrefix=twirp:"$API_OUT_CSHARP" \
    "$API_PROTOCOL"/livekit_room.proto \
    "$API_PROTOCOL"/livekit_egress.proto \
    "$API_PROTOCOL"/livekit_ingress.proto \
    "$API_PROTOCOL"/livekit_sip.proto \
    "$API_PROTOCOL"/livekit_agent.proto \
    "$API_PROTOCOL"/livekit_agent_dispatch.proto \
    "$API_PROTOCOL"/livekit_webhook.proto \
    "$API_PROTOCOL"/livekit_models.proto \
    "$API_PROTOCOL"/livekit_metrics.proto \
    "$API_PROTOCOL"/livekit_analytics.proto

# Patch Twirp proto stubs
# 1. Change "Livekit.Empty" to "Google.Protobuf.WellKnownTypes.Empty"
# 2. Modify the namespace from "Livekit." to "global::Livekit.Server.Sdk.Dotnet."
# 3. Rename the class name and file from "GeneratedAPI" to "Twirp"
sed -i 's|Livekit.Empty|Google.Protobuf.WellKnownTypes.Empty|g' "$API_OUT_CSHARP"/GeneratedAPI.cs
sed -i 's|Livekit.|global::Livekit.Server.Sdk.Dotnet.|g' "$API_OUT_CSHARP"/GeneratedAPI.cs
sed -i 's|GeneratedAPI|Twirp|g' "$API_OUT_CSHARP"/GeneratedAPI.cs
mv "$API_OUT_CSHARP"/GeneratedAPI.cs "$API_OUT_CSHARP"/Twirp.cs

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

# Check if protoc is available
if ! command -v protoc &> /dev/null; then
    echo "Error: protoc not found. Please install Protocol Buffers compiler:"
    echo "https://github.com/protocolbuffers/protobuf/releases/latest"
    exit 1
fi

# Create output directory
mkdir -p "$RTC_OUT_CSHARP"

# Generate C# files from FFI proto definitions
protoc \
    -I="$RTC_PROTOCOL" \
    --csharp_out="$RTC_OUT_CSHARP" \
    --csharp_opt=file_extension=.g.cs \
    "$RTC_PROTOCOL/audio_frame.proto" \
    "$RTC_PROTOCOL/ffi.proto" \
    "$RTC_PROTOCOL/handle.proto" \
    "$RTC_PROTOCOL/participant.proto" \
    "$RTC_PROTOCOL/room.proto" \
    "$RTC_PROTOCOL/track.proto" \
    "$RTC_PROTOCOL/track_publication.proto" \
    "$RTC_PROTOCOL/video_frame.proto" \
    "$RTC_PROTOCOL/e2ee.proto" \
    "$RTC_PROTOCOL/stats.proto" \
    "$RTC_PROTOCOL/rpc.proto" \
    "$RTC_PROTOCOL/data_stream.proto"

echo "✓ LivekitRtc proto files generated successfully"
echo ""
echo "All proto generation complete!"
