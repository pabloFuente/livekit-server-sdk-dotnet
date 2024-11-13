#!/bin/bash

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

set -e

rm -rf "$SCRIPT_FOLDER"/bin
rm -rf "$SCRIPT_FOLDER"/obj

dotnet nuget locals all --clear
dotnet restore || exit 1
dotnet tool restore || exit 1
dotnet csharpier . || exit 1
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg || exit 1

mono /usr/local/bin/nuget.exe delete Livekit.Server.Sdk.Dotnet 1.0.0 -Source ~/.private_nuget/ -np || true
mono /usr/local/bin/nuget.exe add "$SCRIPT_FOLDER"/bin/Debug/Livekit.Server.Sdk.Dotnet.1.0.0.nupkg -Source ~/.private_nuget/ || exit 1

# At this point, it is possible to install the package in a local .NET project and run it with:
# $ dotnet add package Livekit.Server.Sdk.Dotnet && dotnet restore && dotnet run