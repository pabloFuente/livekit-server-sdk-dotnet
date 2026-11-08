#!/usr/bin/env python3
"""Checks the merged coverage report that coverage.sh builds against coverage/thresholds.json.

It fails (exit status 1) when:

1. an SDK (a Cobertura package: LivekitApi, LivekitRtc) is missing from the report, or its line
   or branch coverage is below the minimum of its "sdks" entry;
2. a critical component is below its own, stricter minimum;
3. a critical component names code or test files that no longer exist, so the map of critical
   code to its tests cannot silently go stale.

A component's "code" lists classes ("AccessToken", or a full name) and methods ("Room.ConnectAsync",
every overload); its coverage counts each source line once. Minimums are percentages. An optional
"swing" gives the points by which a component's coverage varies between runs (e2e paths that
depend on timing): its minimum sits under the low end, and the raise hint allows for the swing.

Run it from the repository root after coverage.sh has produced the report:
    python3 coverage/check_coverage.py [coverage/report/Cobertura.xml]
It prints a Markdown table, which CI also appends to the job summary.

It also writes, next to the report, badges/<SDK>.json for each SDK: a shields.io endpoint badge
(https://shields.io/badges/endpoint-badge) with its line coverage, rounded down. docs.yml publishes
the report of main on GitHub Pages, where the coverage badges of the READMEs read them.
"""

import json
import math
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
CONFIG = ROOT / "coverage" / "thresholds.json"
DEFAULT_REPORT = ROOT / "coverage" / "report" / "Cobertura.xml"
# A minimum this many points (plus the component's "swing") below the measured value is worth
# raising (coverage ratchet). It is wider than the margins of thresholds.json, so it only fires
# when coverage has really grown.
RAISE_HINT = 10.0
# Color of a coverage badge: the first whose floor the line coverage reaches
BADGE_COLORS = ((90, "brightgreen"), (80, "green"), (70, "yellowgreen"), (60, "yellow"), (50, "orange"), (0, "red"))


class Coverage:
    """Line and branch counters of a set of source lines, each counted once."""

    def __init__(self):
        self.lines = {}  # (file, line number) -> (hit, branches covered, branches total)

    def add(self, filename, element):
        for line in element.iterfind("lines/line"):
            key = (filename, int(line.get("number")))
            hit = int(line.get("hits", "0")) > 0
            covered = total = 0
            match = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage", ""))
            if line.get("branch", "").lower() == "true" and match:
                covered, total = int(match.group(1)), int(match.group(2))
            old = self.lines.get(key, (False, 0, 0))
            self.lines[key] = (old[0] or hit, max(old[1], covered), max(old[2], total))

    def line_rate(self):
        if not self.lines:
            return None
        return 100.0 * sum(1 for hit, _, _ in self.lines.values() if hit) / len(self.lines)

    def branch_rate(self):
        total = sum(t for _, _, t in self.lines.values())
        return 100.0 * sum(c for _, c, _ in self.lines.values()) / total if total else None


def short_name(class_name):
    return re.split(r"[./]", class_name)[-1]


def find_classes(classes, name):
    """Classes whose full name is `name`, or whose last segment is `name`."""
    return [c for c in classes if c.get("name") == name or short_name(c.get("name")) == name]


def select(classes, selector):
    """(filename, element) pairs a "Class" or "Class.Method" selector names, or an error."""
    found = find_classes(classes, selector)
    if len({c.get("name") for c in found}) > 1:
        return None, f"'{selector}' is ambiguous: use the full class name"
    if found:
        return [(c.get("filename"), c) for c in found], None
    class_name, _, method = selector.rpartition(".")
    owners = find_classes(classes, class_name) if class_name else []
    methods = [
        (c.get("filename"), m)
        for c in owners
        for m in c.iterfind("methods/method")
        if m.get("name") == method
    ]
    if not methods:
        return None, f"'{selector}' matches no class or method in the report"
    return methods, None


def fmt(rate, minimum):
    if rate is None:
        return "n/a"
    text = f"{rate:.1f}%"
    if minimum is not None:
        text += f" (min {minimum})"
    return text


def check(label, cov, entry, rows, errors, hints):
    ok = True
    for kind, rate in (("line", cov.line_rate()), ("branch", cov.branch_rate())):
        minimum = entry.get(kind)
        if minimum is None or rate is None:
            continue
        if rate + 1e-9 < minimum:
            ok = False
            errors.append(f"{label}: {kind} coverage {rate:.1f}% is below the minimum {minimum}%")
        elif rate - minimum >= RAISE_HINT + entry.get("swing", 0):
            hints.append(f"{label}: {kind} coverage is {rate:.1f}%, its minimum {minimum}% can be raised")
    rows.append(
        f"| {label} | {fmt(cov.line_rate(), entry.get('line'))} "
        f"| {fmt(cov.branch_rate(), entry.get('branch'))} | {'pass' if ok else '**FAIL**'} |"
    )


def write_badge(directory, sdk, rate):
    """Writes the shields.io endpoint badge of an SDK's line coverage."""
    percent = math.floor(rate + 1e-9)
    color = next(color for floor, color in BADGE_COLORS if percent >= floor)
    badge = {"schemaVersion": 1, "label": "coverage", "message": f"{percent}%", "color": color}
    directory.mkdir(parents=True, exist_ok=True)
    (directory / f"{sdk}.json").write_text(json.dumps(badge) + "\n")


def main():
    report = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_REPORT
    if not report.is_file():
        print(f"No coverage report at {report}: run ./coverage.sh first", file=sys.stderr)
        return 1
    config = json.loads(CONFIG.read_text())
    packages = {p.get("name"): p for p in ET.parse(report).getroot().iter("package")}

    rows = ["| Scope | Line | Branch | Gate |", "| --- | --- | --- | --- |"]
    errors, hints = [], []

    for sdk, entry in config["sdks"].items():
        package = packages.get(sdk)
        if package is None:
            errors.append(f"{sdk}: no coverage data (did its test project run with coverage?)")
            rows.append(f"| **{sdk}** | missing | missing | **FAIL** |")
            continue
        cov = Coverage()
        for cls in package.iter("class"):
            cov.add(cls.get("filename"), cls)
        check(f"**{sdk}**", cov, entry, rows, errors, hints)
        if cov.line_rate() is not None:
            write_badge(report.parent / "badges", sdk, cov.line_rate())

    for component in config["critical"]:
        label = f"{component['sdk']}: {component['name']}"
        problems = [
            f"{label}: test file {t} does not exist"
            for t in component["tests"]
            if not (ROOT / t).is_file()
        ]
        package = packages.get(component["sdk"])
        cov = Coverage()
        if package is not None:
            classes = list(package.iter("class"))
            for selector in component["code"]:
                selected, problem = select(classes, selector)
                if problem:
                    problems.append(f"{label}: {problem}")
                    continue
                for filename, element in selected:
                    cov.add(filename, element)
        if problems or package is None:
            errors.extend(problems)
            rows.append(f"| {label} | – | – | **FAIL** |")
            continue
        check(label, cov, component, rows, errors, hints)

    print("### Coverage gate\n")
    print("\n".join(rows))
    if errors:
        print("\nFailed:\n")
        print("\n".join(f"- {e}" for e in errors))
    if hints:
        print(f"\nAbove their minimum by {RAISE_HINT:g} points or more (raise them in coverage/thresholds.json):\n")
        print("\n".join(f"- {h}" for h in hints))
    print()
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
