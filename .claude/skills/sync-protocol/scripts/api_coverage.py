#!/usr/bin/env python3
"""
Report how much of the livekit/protocol Twirp API surface is generated and wrapped
by Livekit.Server.Sdk.Dotnet.

Three layers are compared:
  1. proto    - every `service` / `rpc` declared in protocol/protobufs/*.proto
  2. Twirp.cs - the generated stubs (LivekitApi/proto/Twirp.cs), keyed by their
                "twirp/<package>.<Service>/<Method>" route
  3. clients  - the hand-written LivekitApi/*ServiceClient.cs wrappers (the
                "twirp/<package>.<Service>/<Method>" route each one passes to SendAsync)

Usage (from the repo root):
  python3 .claude/skills/sync-protocol/scripts/api_coverage.py [--json]

Exit code is always 0: this is a report, not a gate. Entries listed in
coverage_ignore.txt (next to this script) are reported separately as "ignored".
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path.cwd()
PROTO_DIR = ROOT / "protocol" / "protobufs"
TWIRP_CS = ROOT / "LivekitApi" / "proto" / "Twirp.cs"
CLIENTS_DIR = ROOT / "LivekitApi"
IGNORE_FILE = Path(__file__).with_name("coverage_ignore.txt")

RE_PACKAGE = re.compile(r"^\s*package\s+([\w.]+)\s*;", re.M)
RE_SERVICE = re.compile(r"^\s*service\s+(\w+)\s*\{", re.M)
RE_RPC = re.compile(r"\brpc\s+(\w+)\s*\(\s*(stream\s+)?([\w.]+)\s*\)\s*returns\s*\(\s*(stream\s+)?([\w.]+)\s*\)")
RE_TWIRP_METHOD = re.compile(r"public static async Task<[^>]+>\s+(\w+)\s*\(HttpClient")
RE_TWIRP_ROUTE = re.compile(r'"twirp/([\w.]+)/(\w+)"')
RE_CLIENT_CALL = re.compile(r'"twirp/[\w.]+/(\w+)"')
RE_DEPRECATED = re.compile(r"option\s*\(\s*deprecated\s*\)|deprecated\s*=\s*true")


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


def parse_protos():
    """{ 'livekit.RoomService': {'file': ..., 'rpcs': {name: {...}}} }"""
    services = {}
    if not PROTO_DIR.is_dir():
        sys.exit(f"protocol submodule not found at {PROTO_DIR} (git submodule update --init protocol)")
    for proto in sorted(PROTO_DIR.glob("*.proto")):
        text = strip_comments(proto.read_text(encoding="utf-8"))
        pkg_m = RE_PACKAGE.search(text)
        pkg = pkg_m.group(1) if pkg_m else ""
        for svc_m in RE_SERVICE.finditer(text):
            body = block_after(text, svc_m.start())
            rpcs = {}
            for rpc_m in RE_RPC.finditer(body):
                # rpc options block (if any) directly follows the signature
                tail = body[rpc_m.end() : rpc_m.end() + 400]
                opts = tail.split("rpc ", 1)[0]
                rpcs[rpc_m.group(1)] = {
                    "request": rpc_m.group(3),
                    "response": rpc_m.group(5),
                    "streaming": bool(rpc_m.group(2) or rpc_m.group(4)),
                    "deprecated": bool(RE_DEPRECATED.search(opts)),
                }
            services[f"{pkg}.{svc_m.group(1)}" if pkg else svc_m.group(1)] = {
                "file": proto.name,
                "rpcs": rpcs,
            }
    return services


def parse_twirp():
    """{ 'livekit.RoomService/CreateRoom': 'CreateRoom' } (route -> C# method name)"""
    routes = {}
    if not TWIRP_CS.exists():
        return routes
    current = None
    for line in TWIRP_CS.read_text(encoding="utf-8").splitlines():
        m = RE_TWIRP_METHOD.search(line)
        if m:
            current = m.group(1)
        r = RE_TWIRP_ROUTE.search(line)
        if r and current:
            routes[f"{r.group(1)}/{r.group(2)}"] = current
            current = None
    return routes


def parse_clients():
    """{ 'CreateRoom': ['RoomServiceClient.cs', ...] }"""
    calls = {}
    for cs in sorted(CLIENTS_DIR.glob("*ServiceClient.cs")):
        for m in RE_CLIENT_CALL.finditer(cs.read_text(encoding="utf-8")):
            calls.setdefault(m.group(1), set()).add(cs.name)
    return {k: sorted(v) for k, v in calls.items()}


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
    services = parse_protos()
    routes = parse_twirp()
    clients = parse_clients()
    ignores = load_ignores()
    ignored = lambda key: any(p.match(key) for p in ignores)

    report = {
        "services_not_generated": [],   # whole service absent from Twirp.cs
        "rpcs_not_generated": [],       # service generated, rpc missing (stale Twirp.cs?)
        "rpcs_not_wrapped": [],         # in Twirp.cs, no *ServiceClient calls it
        "stale_twirp_routes": [],       # in Twirp.cs but gone from the protos
        "ignored": [],
        "summary": {},
    }
    generated_services = {r.split("/")[0] for r in routes}
    proto_routes = set()

    for svc, info in services.items():
        for rpc, meta in info["rpcs"].items():
            key = f"{svc}/{rpc}"
            proto_routes.add(key)
            entry = {"route": key, "file": info["file"], **meta}
            if ignored(key):
                report["ignored"].append(entry)
            elif svc not in generated_services:
                pass  # aggregated below
            elif key not in routes:
                report["rpcs_not_generated"].append(entry)
            elif routes[key] not in clients:
                entry["twirp_method"] = routes[key]
                report["rpcs_not_wrapped"].append(entry)
        if svc not in generated_services and not ignored(f"{svc}/*"):
            live = [r for r in info["rpcs"] if not ignored(f"{svc}/{r}")]
            if live:
                report["services_not_generated"].append(
                    {"service": svc, "file": info["file"], "rpcs": live}
                )

    report["stale_twirp_routes"] = sorted(set(routes) - proto_routes)
    total = sum(len(i["rpcs"]) for i in services.values())
    report["summary"] = {
        "proto_rpcs": total,
        "twirp_routes": len(routes),
        "wrapped_twirp_methods": len(clients),
        "not_wrapped": len(report["rpcs_not_wrapped"]),
        "services_not_generated": len(report["services_not_generated"]),
    }

    if as_json:
        print(json.dumps(report, indent=2))
        return

    s = report["summary"]
    print(f"proto RPCs: {s['proto_rpcs']} | Twirp routes: {s['twirp_routes']} | "
          f"wrapped Twirp methods: {s['wrapped_twirp_methods']}\n")
    if report["services_not_generated"]:
        print("== Services declared in protos but NOT generated into Twirp.cs ==")
        print("   (add the .proto to the Twirp protoc call in generate_proto.sh, or to coverage_ignore.txt)")
        for e in report["services_not_generated"]:
            print(f"  {e['service']}  ({e['file']}): {', '.join(e['rpcs'])}")
        print()
    if report["rpcs_not_generated"]:
        print("== RPCs missing from Twirp.cs although their service is generated (re-run generate_proto.sh?) ==")
        for e in report["rpcs_not_generated"]:
            print(f"  {e['route']}")
        print()
    if report["rpcs_not_wrapped"]:
        print("== Generated but NOT wrapped by any *ServiceClient ==")
        for e in report["rpcs_not_wrapped"]:
            dep = "  [deprecated]" if e["deprecated"] else ""
            print(f"  {e['route']}  ({e['request']} -> {e['response']}){dep}")
        print()
    if report["stale_twirp_routes"]:
        print("== Routes in Twirp.cs no longer present in the protos (breaking removal) ==")
        for r in report["stale_twirp_routes"]:
            print(f"  {r}")
        print()
    if report["ignored"]:
        print(f"({len(report['ignored'])} RPCs ignored via {IGNORE_FILE.name})")


if __name__ == "__main__":
    main()
