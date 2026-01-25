#!/bin/bash

# author: https://github.com/pabloFuente

# Downloads pre-built LiveKit FFI native libraries for different platforms.
# The libraries are built from https://github.com/livekit/rust-sdks
#
# Usage:
#   ./download_ffi.sh [version]
#
# Example:
#   ./download_ffi.sh 0.12.44

set -e

VERSION="${1:-}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/runtimes"

# GitHub API URL
GITHUB_API="https://api.github.com/repos/livekit/rust-sdks/releases"
GITHUB_RELEASE_BASE="https://github.com/livekit/rust-sdks/releases/download"

# If no version specified, get latest
if [ -z "$VERSION" ]; then
    echo "Fetching latest version..."
    VERSION=$(curl -sL "$GITHUB_API/latest" | grep -o '"tag_name": *"[^"]*"' | head -1 | sed 's/.*"tag_name": *"\([^"]*\)".*/\1/')
    echo "Latest version: $VERSION"
else
    # If version is just a number like "0.12.44", construct the full tag
    if [[ ! "$VERSION" =~ ^rust-sdks ]]; then
        VERSION="rust-sdks/livekit-ffi@${VERSION}"
    fi
fi

# URL encode the version (/ becomes %2F, @ becomes %40)
VERSION_ENCODED=$(echo "$VERSION" | sed 's/\//%2F/g; s/@/%40/g')

# Platform-specific library names and paths
declare -A PLATFORMS=(
    ["win-x64"]="livekit_ffi.dll"
    ["win-arm64"]="livekit_ffi.dll"
    ["linux-x64"]="liblivekit_ffi.so"
    ["linux-arm64"]="liblivekit_ffi.so"
    ["osx-x64"]="liblivekit_ffi.dylib"
    ["osx-arm64"]="liblivekit_ffi.dylib"
)

# Archive names matching the actual GitHub release assets
declare -A ARCHIVE_NAMES=(
    ["win-x64"]="ffi-windows-x86_64"
    ["win-arm64"]="ffi-windows-arm64"
    ["linux-x64"]="ffi-linux-x86_64"
    ["linux-arm64"]="ffi-linux-arm64"
    ["osx-x64"]="ffi-macos-x86_64"
    ["osx-arm64"]="ffi-macos-arm64"
)

echo "Downloading LiveKit FFI libraries (version: $VERSION)..."

for platform in "${!PLATFORMS[@]}"; do
    lib_name="${PLATFORMS[$platform]}"
    archive_name="${ARCHIVE_NAMES[$platform]}"
    output_path="$OUTPUT_DIR/$platform/native"
    
    echo "  Downloading for $platform..."
    
    mkdir -p "$output_path"
    
    archive_url="$GITHUB_RELEASE_BASE/$VERSION_ENCODED/$archive_name.zip"
    temp_zip=$(mktemp)
    temp_dir=$(mktemp -d)
    
    echo "    URL: $archive_url"
    
    if curl -sL -f "$archive_url" -o "$temp_zip" 2>/dev/null; then
        unzip -q "$temp_zip" -d "$temp_dir"
        
        # Find the library file in the extracted contents
        lib_file=$(find "$temp_dir" -name "$lib_name" -type f | head -1)
        
        if [ -n "$lib_file" ]; then
            cp "$lib_file" "$output_path/$lib_name"
            echo "    ✓ Downloaded $lib_name"
        else
            echo "    ✗ Library file not found in archive"
        fi
    else
        echo "    ✗ Failed to download archive (HTTP error)"
    fi
    
    rm -f "$temp_zip"
    rm -rf "$temp_dir"
done

echo ""
echo "Download complete. Libraries saved to: $OUTPUT_DIR"
