#!/bin/bash

# author: https://github.com/pabloFuente

# Downloads pre-built LiveKit FFI native libraries for different platforms.
# The libraries are built from https://github.com/livekit/rust-sdks
#
# Every archive is checked against the SHA-256 digest that GitHub recorded for that release asset
# when it was uploaded. runtimes/ is only written once all six archives have downloaded and
# matched. Any failure (no such release, a missing digest or archive, a failed download, a
# mismatch, a library missing from its archive) exits with a non-zero code and leaves the previous
# binaries of every platform.
#
# --verify downloads and checks a release the same way but writes nothing: it exits with a
# non-zero code unless runtimes/ holds exactly its six binaries. Without a version it checks the
# livekit-ffi tag that the rust-sdks submodule points to, as the build check of every pull request
# and of every rtc- release does.
#
# Requires bash 4 or newer, curl, unzip, jq, and sha256sum or shasum. GH_TOKEN or GITHUB_TOKEN,
# when set, authenticates the GitHub API calls, whose anonymous rate limit shared CI runners can
# exhaust.
#
# Usage:
#   ./download_ffi.sh [version]
#   ./download_ffi.sh --verify [version]
#
# Example:
#   ./download_ffi.sh 0.12.44
#   ./download_ffi.sh --verify

set -e

if ((BASH_VERSINFO[0] < 4)); then
    echo "Error: bash 4 or newer is required (macOS ships 3.2: brew install bash)" >&2
    exit 1
fi
for tool in curl unzip jq; do
    command -v "$tool" >/dev/null || { echo "Error: $tool is required" >&2; exit 1; }
done
if ! command -v sha256sum >/dev/null && ! command -v shasum >/dev/null; then
    echo "Error: sha256sum or shasum is required" >&2
    exit 1
fi

# SHA-256 of a file, with the tool each platform has
sha256() {
    if command -v sha256sum >/dev/null; then sha256sum "$1"; else shasum -a 256 "$1"; fi | cut -d ' ' -f 1
}

TOKEN="${GH_TOKEN:-${GITHUB_TOKEN:-}}"
AUTH=()
if [ -n "$TOKEN" ]; then
    AUTH=(-H "Authorization: Bearer $TOKEN")
fi

VERIFY=false
if [ "${1:-}" = "--verify" ]; then
    VERIFY=true
    shift
fi
VERSION="${1:-}"
if [[ "$VERSION" == -* ]]; then
    echo "Error: unknown option $VERSION. Usage: $0 [--verify] [version]" >&2
    exit 1
fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/runtimes"

# GitHub API URL
GITHUB_API="https://api.github.com/repos/livekit/rust-sdks/releases"
GITHUB_RELEASE_BASE="https://github.com/livekit/rust-sdks/releases/download"

if [ -z "$VERSION" ] && $VERIFY; then
    # The livekit-ffi tag on the commit that the rust-sdks submodule points to
    pinned=$(git -C "$SCRIPT_DIR" ls-tree HEAD rust-sdks | awk '{print $3}')
    refs=$(git ls-remote --tags https://github.com/livekit/rust-sdks 'livekit-ffi/*')
    VERSION=$(awk -v sha="$pinned" '$1 == sha {sub("refs/tags/", "", $2); sub(/\^\{\}$/, "", $2); print $2}' <<<"$refs" | sort -V | tail -1)
    if [ -z "$VERSION" ]; then
        echo "Error: the rust-sdks submodule points to ${pinned:-no commit}, which is not a livekit-ffi release tag" >&2
        exit 1
    fi
    echo "Pinned version: $VERSION"
elif [ -z "$VERSION" ]; then
    echo "Fetching latest version..."
    if ! RELEASES_JSON=$(curl -fsSL "${AUTH[@]}" "$GITHUB_API?per_page=100"); then
        echo "Error: could not list the releases from the GitHub API" >&2
        exit 1
    fi
    # rust-sdks releases every crate it holds: take the newest GA livekit-ffi/vX.Y.Z
    VERSION=$(jq -r '.[] | select(.prerelease | not) | .tag_name | select(test("^livekit-ffi/v[0-9]+\\.[0-9]+\\.[0-9]+$"))' <<<"$RELEASES_JSON" | sort -V | tail -1)
    if [ -z "$VERSION" ]; then
        echo "Error: could not find a livekit-ffi release" >&2
        exit 1
    fi
    echo "Latest version: $VERSION"
elif [[ "$VERSION" =~ ^[0-9] ]]; then
    # If version is just a number like "0.12.50", construct the full tag (new format: livekit-ffi/vX.Y.Z)
    VERSION="livekit-ffi/v${VERSION}"
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

# GitHub records the SHA-256 digest of every release asset ("sha256:<hex>") when it is uploaded
if ! RELEASE_JSON=$(curl -fsSL "${AUTH[@]}" "$GITHUB_API/tags/$VERSION_ENCODED"); then
    echo "Error: could not read release $VERSION from the GitHub API" >&2
    exit 1
fi

STAGING=$(mktemp -d)
trap 'rm -rf "$STAGING"' EXIT
declare -A LIB_FILES=()

echo "Downloading LiveKit FFI libraries (version: $VERSION)..."

for platform in "${!PLATFORMS[@]}"; do
    lib_name="${PLATFORMS[$platform]}"
    archive="${ARCHIVE_NAMES[$platform]}.zip"
    archive_url="$GITHUB_RELEASE_BASE/$VERSION_ENCODED/$archive"

    echo "  Downloading for $platform..."
    echo "    URL: $archive_url"

    expected=$(jq -r --arg name "$archive" '.assets[] | select(.name == $name) | .digest // empty' <<<"$RELEASE_JSON")
    if [[ "$expected" != sha256:* ]]; then
        echo "    ✗ The release has no SHA-256 digest for $archive"
        continue
    fi
    if ! curl -fsSL "$archive_url" -o "$STAGING/$archive"; then
        echo "    ✗ Failed to download archive (HTTP error)"
        continue
    fi
    actual=$(sha256 "$STAGING/$archive")
    if [ "$actual" != "${expected#sha256:}" ]; then
        echo "    ✗ SHA-256 mismatch: the release lists ${expected#sha256:}, the download is $actual"
        continue
    fi
    echo "    ✓ SHA-256 $actual"

    if ! unzip -q "$STAGING/$archive" -d "$STAGING/$platform"; then
        echo "    ✗ Could not extract $archive"
        continue
    fi
    lib_file=$(find "$STAGING/$platform" -name "$lib_name" -type f | head -1)
    if [ -z "$lib_file" ]; then
        echo "    ✗ Library file not found in archive"
        continue
    fi
    LIB_FILES[$platform]="$lib_file"
done

if [ "${#LIB_FILES[@]}" -ne "${#PLATFORMS[@]}" ]; then
    echo "" >&2
    if $VERIFY; then
        echo "Error: ${#LIB_FILES[@]} of ${#PLATFORMS[@]} platforms passed, so runtimes/ could not be verified" >&2
    else
        echo "Error: ${#LIB_FILES[@]} of ${#PLATFORMS[@]} platforms passed, so nothing was copied: $OUTPUT_DIR keeps its previous binaries" >&2
    fi
    exit 1
fi

if $VERIFY; then
    differ=0
    for platform in "${!LIB_FILES[@]}"; do
        installed="runtimes/$platform/native/${PLATFORMS[$platform]}"
        if cmp -s "${LIB_FILES[$platform]}" "$SCRIPT_DIR/$installed"; then
            echo "  ✓ $installed"
        else
            echo "  ✗ $installed is not the binary published for $VERSION"
            differ=1
        fi
    done
    if [ "$differ" -ne 0 ]; then
        echo "" >&2
        echo "Error: runtimes/ does not hold the binaries of $VERSION" >&2
        exit 1
    fi
    echo ""
    echo "Verified: runtimes/ holds exactly the six binaries of $VERSION"
    exit 0
fi

for platform in "${!LIB_FILES[@]}"; do
    mkdir -p "$OUTPUT_DIR/$platform/native"
    cp "${LIB_FILES[$platform]}" "$OUTPUT_DIR/$platform/native/${PLATFORMS[$platform]}"
done

echo ""
echo "Download complete. All six archives matched their SHA-256 digest. Libraries saved to: $OUTPUT_DIR"
