#!/bin/bash
set -e

SCRIPT_PATH=$(realpath "$0")
SCRIPT_FOLDER="$(dirname "$SCRIPT_PATH")"

# Format
dotnet tool restore || exit 1
dotnet csharpier format LivekitApi || exit 1
dotnet csharpier format LivekitApi.Tests || exit 1
dotnet csharpier format LivekitApi.Example || exit 1
dotnet csharpier format LivekitRtc || exit 1
dotnet csharpier format LivekitRtc.Tests || exit 1
dotnet csharpier format LivekitRtc.Example || exit 1

# Run tests
dotnet test || echo "Some tests failed" && exit 1

# Build and install Livekit.Server.Sdk.Dotnet
pushd "$SCRIPT_FOLDER"/LivekitApi || exit 1

rm -rf bin
rm -rf obj

dotnet nuget locals all --clear
dotnet restore || exit 1

dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg || exit 1

API_VERSION=$(grep -oPm1 "(?<=<Version>)[^<]+" LivekitApi.csproj)
if [ -z "$API_VERSION" ]; then
    echo "Could not find Livekit.Server.Sdk.Dotnet version"
    exit 1
fi
mono /usr/local/bin/nuget.exe delete Livekit.Server.Sdk.Dotnet "$API_VERSION" -Source ~/.private_nuget/ -np || true
mono /usr/local/bin/nuget.exe add "$SCRIPT_FOLDER"/LivekitApi/bin/Debug/Livekit.Server.Sdk.Dotnet."$API_VERSION".nupkg -Source ~/.private_nuget/ || exit 1

popd

# Build and install Livekit.Rtc.Dotnet
pushd "$SCRIPT_FOLDER"/LivekitRtc || exit 1

rm -rf bin
rm -rf obj

dotnet restore || exit 1

dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg || exit 1

RTC_VERSION=$(grep -oPm1 "(?<=<Version>)[^<]+" LivekitRtc.csproj)
if [ -z "$RTC_VERSION" ]; then
    echo "Could not find Livekit.Rtc.Dotnet version"
    exit 1
fi
mono /usr/local/bin/nuget.exe delete Livekit.Rtc.Dotnet "$RTC_VERSION" -Source ~/.private_nuget/ -np || true
mono /usr/local/bin/nuget.exe add "$SCRIPT_FOLDER"/LivekitRtc/bin/Debug/Livekit.Rtc.Dotnet."$RTC_VERSION".nupkg -Source ~/.private_nuget/ || exit 1

popd

# At this point, it is possible to install the packages in a local .NET project and run it with:
# $ dotnet add package Livekit.Server.Sdk.Dotnet && dotnet restore && dotnet run
# $ dotnet add package Livekit.Rtc.Dotnet && dotnet restore && dotnet run
