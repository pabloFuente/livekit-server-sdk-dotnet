#!/bin/bash
set -e

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

# Format
dotnet csharpier LivekitApi || exit 1
dotnet csharpier LivekitApi.Tests || exit 1

# Run tests
dotnet test || exit 1

pushd "$SCRIPT_FOLDER"/LivekitApi || exit 1

rm -rf bin
rm -rf obj

# Clean
dotnet nuget locals all --clear
dotnet restore || exit 1
dotnet tool restore || exit 1

# Build
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg || exit 1

# Install
mono /usr/local/bin/nuget.exe delete Livekit.Server.Sdk.Dotnet 1.0.0 -Source ~/.private_nuget/ -np || true
mono /usr/local/bin/nuget.exe add "$SCRIPT_FOLDER"/LivekitApi/bin/Debug/Livekit.Server.Sdk.Dotnet.1.0.0.nupkg -Source ~/.private_nuget/ || exit 1

# At this point, it is possible to install the package in a local .NET project and run it with:
# $ dotnet add package Livekit.Server.Sdk.Dotnet && dotnet restore && dotnet run