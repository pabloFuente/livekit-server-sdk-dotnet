#!/bin/bash
set -e

if [ -z "$NUGET_API_KEY" ]; then
    echo "Set env var NUGET_API_KEY"
    exit 1
fi

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

source "$SCRIPT_FOLDER"/build-local.sh || exit 1

pushd "$SCRIPT_FOLDER"/LivekitApi || exit 1
dotnet build -c Release /p:ContinuousIntegrationBuild=true || exit 1
dotnet pack -c Release --no-build || exit 1

pushd "$SCRIPT_FOLDER"/LivekitApi/bin/Release || exit 1

FILENAME_WILDCARD=(Livekit.Server.Sdk.Dotnet.*.nupkg)
if [ ${#FILENAME_WILDCARD[@]} -ne 1 ]; then
    echo "Expected one nupkg, got ${#FILENAME_WILDCARD[@]}"
    exit 1
fi
FILENAME=${FILENAME_WILDCARD[0]}

dotnet nuget push "$FILENAME" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json