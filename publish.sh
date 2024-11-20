#!/bin/bash
set -e

if [ -z "$NUGET_API_KEY" ]; then
    echo "Set env var NUGET_API_KEY"
    exit 1
fi

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

source "$SCRIPT_FOLDER"/build_local.sh || exit 1

pushd "$SCRIPT_FOLDER"/LivekitApi || exit 1
dotnet build -c Release /p:ContinuousIntegrationBuild=true || exit 1
dotnet pack -c Release --no-build || exit 1

pushd "$SCRIPT_FOLDER"/LivekitApi/bin/Release || exit 1

NUPKG=(Livekit.Server.Sdk.Dotnet.*.nupkg)
if [ ${#NUPKG[@]} -ne 1 ]; then
    echo "Expected one nupkg, got ${#NUPKG[@]}"
    exit 1
fi
NUPKG_FILENAME=${NUPKG[0]}

dotnet nuget push "$NUPKG_FILENAME" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json || exit 1
