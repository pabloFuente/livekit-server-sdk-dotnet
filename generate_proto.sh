#!/bin/bash

set -e

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

API_PROTOCOL=$SCRIPT_FOLDER/protocol/protobufs
API_OUT_CSHARP=$SCRIPT_FOLDER/LivekitApi/proto

rm -rf "$API_OUT_CSHARP"
mkdir -p "$API_OUT_CSHARP"

protoc \
    -I="$API_PROTOCOL" \
    --csharp_out="$API_OUT_CSHARP" \
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

# Twirp (generated with https://github.com/seanpfeifer/twirp-gen)

protoc -I "$API_PROTOCOL" --twirpcs_out="$API_OUT_CSHARP" \
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

# Patch the proto stubs
# 1. Change "Livekit.Empty" to "Google.Protobuf.WellKnownTypes.Empty"
# 2. Modify the namespace from "Livekit." to "global::LiveKit.Proto."
# 3. Rename the class name and file from "GeneratedAPI" to "Twirp"

sed -i 's|Livekit.Empty|Google.Protobuf.WellKnownTypes.Empty|g' "$API_OUT_CSHARP"/GeneratedAPI.cs
sed -i 's|Livekit.|global::LiveKit.Proto.|g' "$API_OUT_CSHARP"/GeneratedAPI.cs
sed -i 's|GeneratedAPI|Twirp|g' "$API_OUT_CSHARP"/GeneratedAPI.cs
mv "$API_OUT_CSHARP"/GeneratedAPI.cs "$API_OUT_CSHARP"/Twirp.cs
