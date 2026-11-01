#!/usr/bin/env python3
"""
Compare the .proto files available in the protocol submodule with the ones
generate_proto.sh actually compiles for LivekitApi.

Reports:
  - files compiled by generate_proto.sh that no longer exist (the script will fail)
  - imports of compiled files that are NOT compiled themselves (C# will not build:
    the generated code references types that were never generated)
  - top-level protobufs/*.proto not compiled at all (candidates to add, or to ignore)

Usage (from the repo root):
  python3 .claude/skills/sync-protocol/scripts/proto_inventory.py
"""
import re
import sys
from pathlib import Path

ROOT = Path.cwd()
PROTO_DIR = ROOT / "protocol" / "protobufs"
GEN_SCRIPT = ROOT / "generate_proto.sh"

# Imports that come from Google.Protobuf itself and never need generating
WELL_KNOWN = re.compile(r"^google/protobuf/")
RE_IMPORT = re.compile(r'^\s*import\s+(?:public\s+|weak\s+)?"([^"]+)"\s*;', re.M)


def compiled_files():
    """Proto paths (relative to protobufs/) listed in the --csharp_out protoc call."""
    text = GEN_SCRIPT.read_text(encoding="utf-8")
    # Only the first protoc call (the --csharp_out one for LivekitApi) matters here;
    # the Twirp call is a subset and the RTC call uses another directory.
    api_section = text.split("--twirpcs_out", 1)[0]
    return sorted(set(re.findall(r'"\$API_PROTOCOL"/([\w/.-]+\.proto)', api_section)))


def imports_of(rel):
    path = PROTO_DIR / rel
    if not path.exists():
        return []
    return [i for i in RE_IMPORT.findall(path.read_text(encoding="utf-8")) if not WELL_KNOWN.match(i)]


def main():
    if not PROTO_DIR.is_dir():
        sys.exit(f"protocol submodule not found at {PROTO_DIR} (git submodule update --init protocol)")
    compiled = compiled_files()
    compiled_set = set(compiled)

    missing = [f for f in compiled if not (PROTO_DIR / f).exists()]

    # Transitive closure of imports starting from the compiled set
    needed, stack = set(), list(compiled)
    while stack:
        f = stack.pop()
        for imp in imports_of(f):
            if imp not in needed:
                needed.add(imp)
                stack.append(imp)
    uncompiled_imports = sorted(i for i in needed if i not in compiled_set)

    top_level = sorted(p.name for p in PROTO_DIR.glob("*.proto"))
    not_compiled = [f for f in top_level if f not in compiled_set and f not in uncompiled_imports]

    ok = True
    if missing:
        ok = False
        print("== Listed in generate_proto.sh but missing from protocol/protobufs (script will FAIL) ==")
        for f in missing:
            print(f"  {f}")
        print()
    if uncompiled_imports:
        ok = False
        print("== Imported by compiled protos but NOT compiled (LivekitApi will not build) ==")
        for f in uncompiled_imports:
            importers = [c for c in compiled if f in imports_of(c)]
            print(f"  {f}   <- imported by {', '.join(importers) or '(transitively)'}")
        print()
    if not_compiled:
        print("== Top-level protos not compiled (decide: add to generate_proto.sh, or leave out on purpose) ==")
        for f in not_compiled:
            print(f"  {f}")
        print()
    if ok:
        print(f"OK: {len(compiled)} protos compiled, all present, all imports satisfied.")


if __name__ == "__main__":
    main()
