#!/usr/bin/env python3
"""
Report how much of the livekit-ffi surface Livekit.Rtc.Dotnet actually uses.

The FFI protos (LivekitRtc/rust-sdks/livekit-ffi/protocol) expose three oneofs that define
what the SDK can ask the native library for and what it can receive back:

  FfiRequest.message  (ffi.proto)   requests the SDK sends    -> `new FfiRequest { X = ... }`
  FfiEvent.message    (ffi.proto)   callbacks/events it reads -> `FfiEvent.MessageOneofCase.X`
                                                            or `e.X?.AsyncId` in a waiter
  RoomEvent.message   (room.proto)  room events it surfaces   -> `RoomEvent.MessageOneofCase.X`

Each case is looked up in the hand-written sources (LivekitRtc/*.cs and LivekitRtc/Internal/*.cs;
the generated Proto/*.g.cs and the tests are excluded). Proto field names are converted the way
protoc's C# generator does it: snake_case to PascalCase, and the letter after a digit is
capitalised (e2ee -> E2Ee, e2ee_state_changed -> E2EeStateChanged).

Usage (from the repo root):
  python3 .claude/skills/sync-rust-sdks/scripts/ffi_coverage.py [--json]

Exit code is always 0: this is a report, not a gate. Cases listed in coverage_ignore.txt
(next to this script) are reported separately as "ignored".
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path.cwd()
PROTO_DIR = ROOT / "LivekitRtc" / "rust-sdks" / "livekit-ffi" / "protocol"
SDK_DIR = ROOT / "LivekitRtc"
IGNORE_FILE = Path(__file__).with_name("coverage_ignore.txt")

# (report key, proto file, message holding `oneof message`, regexes that capture a used C# case)
ONEOFS = [
    (
        "FfiRequest",
        "ffi.proto",
        "FfiRequest",
        [r"new\s+FfiRequest\s*\{\s*(\w+)\s*=", r"\bFfiRequest\.MessageOneofCase\.(\w+)"],
    ),
    (
        "FfiEvent",
        "ffi.proto",
        "FfiEvent",
        # The second form is the waiter lambda `e => e.X?.AsyncId == asyncId`; `response.X?.AsyncId`
        # reads the FfiResponse, whose case names differ from the FfiEvent ones, so exclude it.
        [r"\bFfiEvent\.MessageOneofCase\.(\w+)", r"\b(?!response\b)\w+\.(\w+)\?\.AsyncId\b"],
    ),
    ("RoomEvent", "room.proto", "RoomEvent", [r"\bRoomEvent\.MessageOneofCase\.(\w+)"]),
]

RE_FIELD = re.compile(r"^\s*([\w.]+)\s+(\w+)\s*=\s*(\d+)\s*(\[[^\]]*\])?\s*;", re.M)


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def block_after(text: str, start: int) -> str:
    """Return the brace-balanced block starting at the '{' at/after `start`."""
    i = text.index("{", start)
    depth = 0
    for j in range(i, len(text)):
        if text[j] == "{":
            depth += 1
        elif text[j] == "}":
            depth -= 1
            if depth == 0:
                return text[i + 1 : j]
    return text[i + 1 :]


def pascal(snake: str) -> str:
    """protoc C# naming: underscores removed, next letter upper-cased, also after a digit."""
    out, cap = [], True
    for ch in snake:
        if ch == "_":
            cap = True
            continue
        out.append(ch.upper() if cap else ch)
        cap = ch.isdigit()
    return "".join(out)


def oneof_cases(proto_file: str, message: str):
    """[(csharp_case, proto_field, proto_type, field_number)] of `oneof message` in `message`."""
    path = PROTO_DIR / proto_file
    if not path.exists():
        sys.exit(f"{path} not found (git submodule update --init LivekitRtc/rust-sdks)")
    text = strip_comments(path.read_text(encoding="utf-8"))
    m = re.search(r"^message\s+" + re.escape(message) + r"\s*\{", text, re.M)
    if not m:
        sys.exit(f"message {message} not found in {proto_file}")
    body = block_after(text, m.start())
    o = re.search(r"\boneof\s+message\s*\{", body)
    if not o:
        sys.exit(f"{message} in {proto_file} has no `oneof message`")
    inner = block_after(body, o.start())
    return [(pascal(name), name, ftype, int(num)) for ftype, name, num, _ in RE_FIELD.findall(inner)]


def sdk_sources():
    files = sorted(SDK_DIR.glob("*.cs")) + sorted((SDK_DIR / "Internal").glob("*.cs"))
    if not files:
        sys.exit(f"no SDK sources under {SDK_DIR} (run from the repo root)")
    return {f.relative_to(ROOT).as_posix(): f.read_text(encoding="utf-8") for f in files}


def usages(sources, patterns):
    """{csharp_case: [files that reference it]}"""
    found = {}
    for rel, text in sources.items():
        for pat in patterns:
            for name in re.findall(pat, text):
                found.setdefault(name, set()).add(rel)
    return {k: sorted(v) for k, v in found.items()}


def load_ignores():
    if not IGNORE_FILE.exists():
        return []
    pats = []
    for line in IGNORE_FILE.read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            pats.append(re.compile("^" + re.escape(line).replace(r"\*", ".*") + "$"))
    return pats


def main():
    as_json = "--json" in sys.argv
    sources = sdk_sources()
    ignores = load_ignores()
    ignored = lambda key: any(p.match(key) for p in ignores)

    report = {"unused": {}, "stale": {}, "ignored": [], "summary": {}}
    unused_requests = set()

    for key, proto_file, message, patterns in ONEOFS:
        cases = oneof_cases(proto_file, message)
        used = usages(sources, patterns)
        known = {c[0] for c in cases}
        unused, stale = [], []
        for cs, field, ftype, num in cases:
            entry = {"case": cs, "field": field, "type": ftype, "number": num, "proto": proto_file}
            if ignored(f"{key}/{cs}"):
                report["ignored"].append({"key": f"{key}/{cs}", **entry})
            elif cs not in used:
                if key == "FfiEvent" and cs in unused_requests:
                    entry["note"] = "callback of an unused request"
                unused.append(entry)
        for cs, files in sorted(used.items()):
            if cs not in known:
                stale.append({"case": cs, "files": files})
        if key == "FfiRequest":
            unused_requests = {e["case"] for e in unused}
        report["unused"][key] = unused
        report["stale"][key] = stale
        report["summary"][key] = {
            "cases": len(cases),
            "used": len(known & set(used)),
            "unused": len(unused),
            "ignored": sum(1 for i in report["ignored"] if i["key"].startswith(key + "/")),
            "stale": len(stale),
        }

    if as_json:
        print(json.dumps(report, indent=2))
        return

    print(
        " | ".join(
            f"{k}: {s['cases']} cases, {s['used']} used, {s['unused']} unused"
            + (f", {s['ignored']} ignored" if s["ignored"] else "")
            for k, s in report["summary"].items()
        )
        + "\n"
    )
    titles = {
        "FfiRequest": "FfiRequest cases the SDK never sends (FFI capabilities not exposed)",
        "FfiEvent": "FfiEvent cases the SDK never reads",
        "RoomEvent": "RoomEvent cases Room.HandleRoomEvent does not handle (users cannot subscribe to them)",
    }
    for key in report["unused"]:
        if report["unused"][key]:
            print(f"== {titles[key]} ==")
            for e in report["unused"][key]:
                note = f"   [{e['note']}]" if "note" in e else ""
                print(f"  {e['case']:<48} ({e['field']}: {e['type']}){note}")
            print()
    for key in report["stale"]:
        if report["stale"][key]:
            print(f"== {key} cases referenced by the SDK that no longer exist in the protos (will not compile) ==")
            for e in report["stale"][key]:
                print(f"  {key}.{e['case']}   in {', '.join(e['files'])}")
            print()
    if report["ignored"]:
        print(f"({len(report['ignored'])} cases ignored via {IGNORE_FILE.name})")


if __name__ == "__main__":
    main()
