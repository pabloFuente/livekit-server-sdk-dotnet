#!/usr/bin/env python3
"""Scans the dependencies of the repository for known vulnerabilities and applies the severity
policy and the exceptions of security/policy.json.

Sources (--sources, comma separated; the default is nuget,tools,actions and "all" adds native):

- nuget: the NuGet packages of every project, examples included, direct and transitive, from
  `dotnet package list --vulnerable --include-transitive` (advisories from nuget.org, which
  mirrors the GitHub Advisory Database).
- tools: the .NET tools pinned in .config/dotnet-tools.json (GitHub Advisory Database).
- actions: the actions the workflows pin as `@<sha> # vX.Y.Z` (GitHub Advisory Database; Dependabot
  raises no alerts for actions pinned by SHA).
- native: the Rust crates compiled into livekit_ffi, resolved by `cargo tree` in the rust-sdks
  submodule (advisories from OSV: RustSec and GitHub). Needs the submodule checked out and cargo.
  The prebuilt libwebrtc linked into livekit_ffi carries no metadata a scanner can match.

policy.json: a finding whose severity is in "fail_on" fails unless an exception covers it; one in
"warn_on" is reported for review. An advisory that publishes no severity counts as
"unknown_severity", an informational RustSec notice (unmaintained, unsound) as
"informational_severity". An exception names one "package" and one "advisory" (a GHSA, CVE or
RUSTSEC id; no wildcards), a "justification" and an "expires" date (YYYY-MM-DD) at most
"max_exception_days" ahead. An invalid or expired exception fails the check; an unused one is
reported when every source was scanned.

Exit status: 0 pass, 1 policy violation, 2 a source could not be scanned (never reported as a
pass). GH_TOKEN or GITHUB_TOKEN raises the GitHub API rate limit.
    python3 security/check_vulnerabilities.py [--sources all] [--project PATH ...]
    python3 security/check_vulnerabilities.py --dependency-review-outputs
"""

import argparse
import datetime
import json
import math
import os
import pathlib
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
POLICY = ROOT / "security" / "policy.json"
SEVERITIES = ["low", "moderate", "high", "critical"]
SOURCES = ["nuget", "tools", "actions", "native"]
NUGET_ORG = "https://api.nuget.org/v3/index.json"
OSV = "https://api.osv.dev/v1"
ADVISORY_ID = re.compile(r"GHSA(-[0-9a-z]{4}){3}|CVE-\d{4}-\d{4,}|RUSTSEC-\d{4}-\d{4}", re.I)
# `uses: owner/repo[/path]@ref  # vX.Y.Z`; local workflows (./...) and docker:// are not packages
USES = re.compile(r"^\s*(?:-\s*)?uses:\s*(?!\.|docker:)([\w.-]+/[\w.-]+)\S*@(\S+)(?:\s+#\s*(\S+))?", re.M)
CRATE = re.compile(r"^(\S+) v(\S+)(.*)$")


class ScanError(Exception):
    """A source that could not be scanned: its result must never read as a pass."""


class Finding:
    def __init__(self, source, package, version, advisory, severity, url, relation, where,
                 aliases=(), fixed=None, note=""):
        self.source = source
        self.package = package
        self.version = version
        self.advisory = canonical(advisory)
        self.ids = {self.advisory, *(canonical(a) for a in aliases if a)}
        self.severity = severity
        self.url = url
        self.relation = relation
        self.where = {where}
        self.fixed = fixed
        self.note = note
        self.status = ""


def canonical(advisory_id):
    advisory_id = advisory_id.strip()
    if advisory_id.upper().startswith("GHSA-"):
        return "GHSA-" + advisory_id[5:].lower()
    return advisory_id.upper()


def severity_name(value):
    value = {"medium": "moderate"}.get((value or "").lower(), (value or "").lower())
    return value if value in SEVERITIES else None


def rel(path):
    try:
        return str(pathlib.Path(path).resolve().relative_to(ROOT))
    except ValueError:
        return str(path)


def run(cmd, cwd=ROOT):
    try:
        return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    except FileNotFoundError:
        raise ScanError(f"{cmd[0]} is not installed") from None


def http_json(url, payload=None, github=False, text=False):
    """GET (or POST payload) as JSON, or as text; None when the resource does not exist."""
    headers = {"Accept": "application/json", "User-Agent": "livekit-server-sdk-dotnet-security-scan"}
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if github:
        headers["Accept"] = "application/vnd.github+json"
        if token:
            headers["Authorization"] = f"Bearer {token}"
    data = None
    if payload is not None:
        data = json.dumps(payload).encode()
        headers["Content-Type"] = "application/json"
    try:
        with urllib.request.urlopen(urllib.request.Request(url, data, headers), timeout=60) as r:
            return r.read().decode() if text else json.load(r)
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return None
        raise ScanError(f"{url}: HTTP {e.code} {e.reason}") from None
    except (urllib.error.URLError, OSError, ValueError) as e:
        raise ScanError(f"{url}: {e}") from None


def add(findings, finding):
    key = (finding.source, finding.package.lower(), finding.version, finding.advisory)
    if key in findings:
        known = findings[key]
        known.where |= finding.where
        if finding.relation == "direct":
            known.relation = "direct"
    else:
        findings[key] = finding


def patched_version(advisory, package):
    for vulnerability in (advisory or {}).get("vulnerabilities") or []:
        name = (vulnerability.get("package") or {}).get("name") or ""
        if name.lower() == package.lower() and vulnerability.get("first_patched_version"):
            return vulnerability["first_patched_version"]
    return None


def github_advisories(ecosystem, package, version, cache={}):
    """The reviewed, not withdrawn GitHub advisories affecting package@version."""
    key = (ecosystem, package, version)  # exact: the database matches names case-sensitively
    if key not in cache:
        query = urllib.parse.urlencode(
            {"ecosystem": ecosystem, "affects": f"{package}@{version}", "per_page": 100}
        )
        found = http_json(f"https://api.github.com/advisories?{query}", github=True) or []
        cache[key] = [a for a in found if not a.get("withdrawn_at")]
    return cache[key]


def scan_nuget(projects, findings):
    for project in projects:
        name = rel(project)
        restore = run(["dotnet", "restore", str(project), "--verbosity", "quiet"])
        if restore.returncode:
            raise ScanError(f"dotnet restore {name} failed:\n{restore.stdout}{restore.stderr}")
        listing = run(["dotnet", "package", "list", "--project", str(project), "--vulnerable",
                       "--include-transitive", "--format", "json"])
        try:
            data = json.loads(listing.stdout)
        except ValueError:
            data = None
        if listing.returncode or data is None:
            raise ScanError(f"dotnet package list failed for {name}:\n{listing.stdout}{listing.stderr}")
        # A source without vulnerability data (a local folder, a private feed) reports nothing
        if NUGET_ORG not in data.get("sources", []):
            raise ScanError(f"{name}: nuget.org, which provides the vulnerability data, is not "
                            f"among the sources {data.get('sources')}")
        problems = data.get("problems", []) + [
            p for project_data in data.get("projects", []) for p in project_data.get("problems", [])
        ]
        if problems:
            raise ScanError(f"{name}: {problems}")
        for project_data in data.get("projects", []):
            for framework in project_data.get("frameworks", []):
                for key, relation in (("topLevelPackages", "direct"), ("transitivePackages", "transitive")):
                    for package in framework.get(key, []):
                        for vulnerability in package.get("vulnerabilities", []):
                            url = vulnerability.get("advisoryurl", "")
                            add(findings, Finding(
                                "nuget", package["id"], package["resolvedVersion"],
                                url.rstrip("/").rsplit("/", 1)[-1],
                                severity_name(vulnerability.get("severity")), url, relation,
                                f"{name} ({framework['framework']})",
                            ))
    for finding in findings.values():
        if finding.source == "nuget" and finding.advisory.startswith("GHSA-"):
            try:
                advisory = http_json(f"https://api.github.com/advisories/{finding.advisory}", github=True)
            except ScanError:
                advisory = None  # the fixed version and the CVE alias are hints, not the verdict
            finding.fixed = patched_version(advisory, finding.package)
            if (advisory or {}).get("cve_id"):
                finding.ids.add(canonical(advisory["cve_id"]))
    return f"nuget: {len(projects)} projects, direct and transitive packages"


def scan_tools(findings):
    tools = json.loads((ROOT / ".config" / "dotnet-tools.json").read_text()).get("tools", {})
    for name, tool in tools.items():
        # The manifest stores ids in lower case, the advisory database matches them exactly
        lower, version = name.lower(), tool["version"].lower()
        nuspec = http_json(f"https://api.nuget.org/v3-flatcontainer/{lower}/{version}/{lower}.nuspec", text=True)
        match = re.search(r"<id>\s*([^<\s]+)\s*</id>", nuspec or "")
        if not match:
            raise ScanError(f"{name} {tool['version']} is not on nuget.org")
        for advisory in github_advisories("nuget", match.group(1), tool["version"]):
            add(findings, Finding(
                "tools", match.group(1), tool["version"], advisory["ghsa_id"],
                severity_name(advisory.get("severity")), advisory.get("html_url"), "pinned",
                ".config/dotnet-tools.json", aliases=[advisory.get("cve_id")],
                fixed=patched_version(advisory, match.group(1)),
            ))
    return f"tools: {len(tools)} .NET tools"


def scan_actions(findings):
    pins, names = set(), {}
    for workflow in sorted((ROOT / ".github" / "workflows").glob("*.y*ml")):
        for action, ref, comment in USES.findall(workflow.read_text()):
            version = (comment if re.fullmatch(r"[0-9a-f]{40}", ref) else ref).lstrip("v")
            if not re.match(r"\d", version):
                raise ScanError(f"{rel(workflow)}: {action}@{ref} has no version to look up "
                                "(pin it as `@<sha> # vX.Y.Z`)")
            pins.add((action, version))
            # The advisory database matches names exactly: try them as written, as GitHub spells
            # the repository and in lower case
            if action not in names:
                repository = http_json(f"https://api.github.com/repos/{action}", github=True) or {}
                names[action] = {action, action.lower(), repository.get("full_name", action)}
            for name in sorted(names[action]):
                for advisory in github_advisories("actions", name, version):
                    add(findings, Finding(
                        "actions", action, version, advisory["ghsa_id"],
                        severity_name(advisory.get("severity")), advisory.get("html_url"), "pinned",
                        rel(workflow), aliases=[advisory.get("cve_id")],
                        fixed=patched_version(advisory, name),
                    ))
    return f"actions: {len(pins)} pinned actions"


def cvss3_score(vector):
    metrics = dict(part.split(":", 1) for part in vector.split("/")[1:])
    changed = metrics["S"] == "C"
    weight = {"H": 0.56, "L": 0.22, "N": 0.0}
    impact_base = 1 - (1 - weight[metrics["C"]]) * (1 - weight[metrics["I"]]) * (1 - weight[metrics["A"]])
    impact = 7.52 * (impact_base - 0.029) - 3.25 * (impact_base - 0.02) ** 15 if changed else 6.42 * impact_base
    if impact <= 0:
        return 0.0
    exploitability = (8.22 * {"N": 0.85, "A": 0.62, "L": 0.55, "P": 0.2}[metrics["AV"]]
                      * {"L": 0.77, "H": 0.44}[metrics["AC"]]
                      * {"N": 0.85, "L": 0.68 if changed else 0.62, "H": 0.5 if changed else 0.27}[metrics["PR"]]
                      * {"N": 0.85, "R": 0.62}[metrics["UI"]])
    score = min((1.08 if changed else 1.0) * (impact + exploitability), 10.0)
    scaled = round(score * 100000)  # CVSS "round up" to one decimal
    return scaled / 100000.0 if scaled % 10000 == 0 else (math.floor(scaled / 10000) + 1) / 10.0


def osv_severity(vuln, policy):
    label = severity_name((vuln.get("database_specific") or {}).get("severity"))
    if label:
        return label, ""
    vectors = [s.get("score") for s in vuln.get("severity", []) if s.get("type") == "CVSS_V3"]
    vectors += [(a.get("database_specific") or {}).get("cvss") for a in vuln.get("affected", [])]
    vectors = [v for v in vectors if v and v.startswith("CVSS:3")]
    if vectors:
        score = cvss3_score(vectors[0])
        severity = "critical" if score >= 9 else "high" if score >= 7 else "moderate" if score >= 4 else "low"
        return severity, f"CVSS {score}"
    for alias in vuln.get("aliases", []):
        if alias.upper().startswith("GHSA-"):
            record = http_json(f"{OSV}/vulns/{alias}") or {}
            label = severity_name((record.get("database_specific") or {}).get("severity"))
            if label:
                return label, f"severity of {alias}"
    kinds = {(a.get("database_specific") or {}).get("informational") for a in vuln.get("affected", [])}
    kinds.discard(None)
    if kinds:
        return policy["informational_severity"], f"informational: {', '.join(sorted(kinds))}"
    return policy["unknown_severity"], "no severity published"


def scan_native(findings, policy):
    sdk = ROOT / "LivekitRtc" / "rust-sdks"
    if not (sdk / "Cargo.lock").is_file():
        raise ScanError("LivekitRtc/rust-sdks is not checked out: "
                        "git submodule update --init --depth 1 LivekitRtc/rust-sdks")
    tag = (run(["git", "-C", str(sdk), "describe", "--tags", "--match", "livekit-ffi/*"]).stdout.strip()
           or run(["git", "-C", str(sdk), "rev-parse", "--short", "HEAD"]).stdout.strip())
    # The crates linked into the library on every target, not the whole workspace lockfile
    tree = run(["cargo", "tree", "--locked", "-p", "livekit-ffi", "--target", "all", "-e", "normal",
                "--prefix", "none", "--format", "{p}"], cwd=sdk)
    if tree.returncode:
        raise ScanError(f"cargo tree failed:\n{tree.stderr[-3000:]}")
    crates, unscannable = set(), set()
    for line in tree.stdout.splitlines():
        match = CRATE.match(line.strip())
        if not match:
            continue
        name, version, rest = match.groups()
        rest = rest.replace("(*)", "").replace("(proc-macro)", "").strip()
        if not rest:
            crates.add((name, version))
        elif not rest.startswith("(/"):
            unscannable.add(f"{name} {version} {rest}")  # git dependencies have no OSV versions
    if not crates:
        raise ScanError("cargo tree listed no crates for livekit-ffi")
    if unscannable:
        raise ScanError(f"crates outside crates.io cannot be matched by version: {sorted(unscannable)}")
    ordered = sorted(crates)
    results = []
    for start in range(0, len(ordered), 1000):
        queries = [{"package": {"name": n, "ecosystem": "crates.io"}, "version": v}
                   for n, v in ordered[start:start + 1000]]
        results += (http_json(f"{OSV}/querybatch", {"queries": queries}) or {}).get("results", [])
    if len(results) != len(ordered):
        raise ScanError(f"OSV answered {len(results)} of {len(ordered)} queries")
    records = {}
    for (name, version), result in zip(ordered, results):
        for reference in result.get("vulns", []):
            if reference["id"] not in records:
                records[reference["id"]] = http_json(f"{OSV}/vulns/{reference['id']}")
            vuln = records[reference["id"]]
            if vuln is None:
                raise ScanError(f"OSV has no record of {reference['id']}")
            if vuln.get("withdrawn"):
                continue
            severity, note = osv_severity(vuln, policy)
            fixed = [event["fixed"] for affected in vuln.get("affected", [])
                     if (affected.get("package") or {}).get("name") == name
                     for r in affected.get("ranges", []) for event in r.get("events", []) if "fixed" in event]
            add(findings, Finding(
                "native", name, version, vuln["id"], severity, f"https://osv.dev/vulnerability/{vuln['id']}",
                "linked into livekit_ffi", f"livekit-ffi {tag}", aliases=vuln.get("aliases", []),
                fixed=", ".join(fixed) or None, note=note,
            ))
    return f"native: {len(ordered)} crates linked into livekit_ffi at {tag}"


def load_policy(path):
    policy = json.loads(pathlib.Path(path).read_text())
    errors = []
    for key in ("fail_on", "warn_on"):
        unknown = [s for s in policy.get(key, []) if s not in SEVERITIES]
        if unknown:
            errors.append(f"policy: {key} lists unknown severities {unknown}")
    if not policy.get("fail_on"):
        errors.append("policy: fail_on must list at least one severity")
    for key in ("unknown_severity", "informational_severity"):
        if policy.get(key) not in SEVERITIES:
            errors.append(f"policy: {key} must be one of {SEVERITIES}")
    today = datetime.datetime.now(datetime.timezone.utc).date()
    max_days = policy.get("max_exception_days", 90)
    valid = []
    for number, entry in enumerate(policy.get("exceptions", []), 1):
        exception = {k: str(entry.get(k, "")).strip()
                     for k in ("package", "advisory", "justification", "expires")}
        label = f"exception {number} ({exception['package'] or '?'} {exception['advisory'] or '?'})"
        missing = [k for k, v in exception.items() if not v]
        if missing:
            errors.append(f"{label}: missing {', '.join(missing)}")
            continue
        if not ADVISORY_ID.fullmatch(exception["advisory"]):
            errors.append(f"{label}: the advisory must be one GHSA, CVE or RUSTSEC id")
            continue
        if re.search(r"[*?,\s]", exception["package"]):
            errors.append(f"{label}: the package must be one exact package name")
            continue
        if len(exception["justification"]) < 20:
            errors.append(f"{label}: the justification must explain why the risk is accepted")
            continue
        try:
            expires = datetime.date.fromisoformat(exception["expires"])
        except ValueError:
            errors.append(f"{label}: expires must be a YYYY-MM-DD date")
            continue
        if expires < today:
            errors.append(f"{label}: expired on {expires}; fix the dependency, or review the risk "
                          "again and set a new date")
            continue
        if expires > today + datetime.timedelta(days=max_days):
            errors.append(f"{label}: expires on {expires}, more than {max_days} days ahead")
            continue
        valid.append(dict(exception, advisory=canonical(exception["advisory"]), expires=expires))
    return policy, valid, errors


def evaluate(findings, policy, exceptions, all_sources):
    errors, used = [], set()
    for finding in findings:
        exception = next((e for e in exceptions if e["package"].lower() == finding.package.lower()
                          and e["advisory"] in finding.ids), None)
        if exception is not None:
            used.add(id(exception))
            finding.status = f"excepted until {exception['expires']}: {exception['justification']}"
        elif finding.severity in policy["fail_on"]:
            finding.status = "FAIL"
            errors.append(f"{finding.package} {finding.version}: {finding.severity} {finding.advisory}")
        elif finding.severity in policy.get("warn_on", []):
            finding.status = "review"
        else:
            finding.status = "reported"
    unused = [f"{e['package']} {e['advisory']}" for e in exceptions if id(e) not in used] if all_sources else []
    return errors, unused


def annotate(level, title, message):
    if os.environ.get("GITHUB_ACTIONS") == "true":
        escape = lambda s: s.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::{level} title={escape(title).replace(',', '%2C').replace(':', '%3A')}::{escape(message)}")


def report(findings, scanned, incomplete, errors, unused):
    lines = ["Scanned:"] + [f"  {s}" for s in scanned]
    for finding in findings:
        fixed = f"fixed in {finding.fixed}" if finding.fixed else "no fixed version known"
        note = f" ({finding.note})" if finding.note else ""
        lines.append(f"[{finding.severity.upper()}{note}] {finding.source}: {finding.package} "
                     f"{finding.version}, {finding.relation}, {finding.advisory} {finding.url}, {fixed}; "
                     f"{', '.join(sorted(finding.where))}: {finding.status}")
        level = {"FAIL": "error", "review": "warning"}.get(finding.status, "notice")
        annotate(level, f"{finding.severity} vulnerability in {finding.package} {finding.version}",
                 f"{finding.advisory} ({finding.source}, {finding.relation}), {fixed}: {finding.url}")
    if not findings:
        lines.append("No known vulnerabilities.")
    for problem in incomplete:
        lines.append(f"INCOMPLETE {problem}")
        annotate("error", "Vulnerability scan incomplete", problem)
    for error in errors:
        lines.append(f"FAILED {error}")
    for exception in unused:
        lines.append(f"Unused exception, remove it from security/policy.json: {exception}")
        annotate("warning", "Unused vulnerability exception", exception)
    verdict = ("INCOMPLETE: a source could not be scanned" if incomplete
               else "FAILED" if errors else "passed")
    lines.append(f"Vulnerability scan {verdict}")
    print("\n".join(lines))

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        md = ["### Vulnerability scan: " + verdict, "", *[f"- {s}" for s in scanned], ""]
        if findings:
            md += ["| Severity | Source | Package | Version | Advisory | Fixed in | Where | Status |",
                   "| --- | --- | --- | --- | --- | --- | --- | --- |"]
            md += [f"| {f.severity}{' (' + f.note + ')' if f.note else ''} | {f.source} | {f.package} "
                   f"| {f.version} ({f.relation}) | [{f.advisory}]({f.url}) | {f.fixed or '?'} "
                   f"| {'<br>'.join(sorted(f.where))} | {f.status} |" for f in findings]
        md += [f"- **Incomplete** {p}" for p in incomplete] + [f"- **Failed** {e}" for e in errors]
        md += [f"- Unused exception: {e}" for e in unused]
        with open(summary, "a") as out:
            out.write("\n".join(md) + "\n\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--sources", default="nuget,tools,actions", help="comma separated, or all")
    parser.add_argument("--project", action="append", type=pathlib.Path,
                        help="scan this project instead of every project of the repository")
    parser.add_argument("--policy", type=pathlib.Path, default=POLICY)
    parser.add_argument("--dependency-review-outputs", action="store_true",
                        help="print fail-on-severity and allow-ghsas for dependency-review-action")
    args = parser.parse_args()

    policy, exceptions, errors = load_policy(args.policy)
    if args.dependency_review_outputs:
        if errors:
            print("\n".join(errors), file=sys.stderr)
            return 1
        outputs = [f"fail-on-severity={min(policy['fail_on'], key=SEVERITIES.index)}",
                   "allow-ghsas=" + ",".join(sorted({e["advisory"] for e in exceptions
                                                     if e["advisory"].startswith("GHSA-")}))]
        print("\n".join(outputs))
        if os.environ.get("GITHUB_OUTPUT"):
            with open(os.environ["GITHUB_OUTPUT"], "a") as out:
                out.write("\n".join(outputs) + "\n")
        return 0

    sources = SOURCES if args.sources == "all" else [s.strip() for s in args.sources.split(",")]
    if set(sources) - set(SOURCES):
        parser.error(f"unknown sources {sorted(set(sources) - set(SOURCES))}; use {SOURCES} or all")
    findings, scanned, incomplete = {}, [], []
    for source in sources:
        try:
            if source == "nuget":
                projects = args.project or [ROOT / p for p in run(["git", "ls-files", "*.csproj"]).stdout.split()]
                scanned.append(scan_nuget(projects, findings))
            elif source == "tools":
                scanned.append(scan_tools(findings))
            elif source == "actions":
                scanned.append(scan_actions(findings))
            else:
                scanned.append(scan_native(findings, policy))
        except ScanError as e:
            incomplete.append(f"{source}: {e}")
    for finding in findings.values():
        if finding.severity is None:
            finding.severity, finding.note = policy["unknown_severity"], "no severity published"
    ordered = sorted(findings.values(), key=lambda f: (-SEVERITIES.index(f.severity), f.source, f.package.lower()))
    violations, unused = evaluate(ordered, policy, exceptions, set(sources) == set(SOURCES) and not args.project)
    report(ordered, scanned, incomplete, errors + violations, unused)
    return 2 if incomplete else 1 if errors or violations else 0


if __name__ == "__main__":
    sys.exit(main())
