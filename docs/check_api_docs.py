#!/usr/bin/env python3
"""Checks the API reference after `dotnet docfx docs/docfx.json` has built it.

DocFX itself does not notice three ways the reference can rot, so this script fails on them:

1. A hand-written public type of LivekitApi is missing from the list in
   docs/template/public/main.css that shows those types in bold among the protobuf messages.
2. A LiveKit.Rtc member uses a LiveKit.Proto type that the allow-list in docs/filter.yml
   leaves out, so the type has no page and its name is not linked.
3. A link between pages of the generated site points to a page that does not exist.

Run it from the repository root: python3 docs/check_api_docs.py
"""

import html
import pathlib
import re
import sys
import urllib.parse

DOCS = pathlib.Path(__file__).resolve().parent
SERVER_META = DOCS / "api-server"
RTC_META = DOCS / "api-rtc"
SERVER_CSS = DOCS / "template" / "public" / "main.css"
SITE = DOCS / "_site"


def first_item(raw):
    """The YAML block of the type a metadata file describes (its first item)."""
    return raw.split("\n- uid: ")[1] if "\n- uid: " in raw else ""


def check_server_bold_types():
    listed = set(re.findall(r'\[href\*="(Livekit\.Server\.Sdk\.Dotnet\.\w+)\.html"\]', SERVER_CSS.read_text()))
    errors = []
    documented = set()
    for f in SERVER_META.glob("*.yml"):
        if f.name == "toc.yml":
            continue
        documented.add(f.stem)
        item = first_item(f.read_text())
        kind = re.search(r"^  type: (\w+)$", item, re.M)
        path = re.search(r"^    path: (\S+)$", item, re.M)
        if not kind or kind.group(1) == "Namespace" or not path:
            continue
        hand_written = "/proto/" not in path.group(1).replace("\\", "/")
        top_level = "." not in re.search(r"^  id: (\S+)$", item, re.M).group(1)
        if hand_written and top_level and f.stem not in listed:
            errors.append(f"{f.stem} ({path.group(1)}) is not listed in {SERVER_CSS.relative_to(DOCS.parent)}")
    for uid in sorted(listed - documented):
        errors.append(f"{SERVER_CSS.relative_to(DOCS.parent)} lists {uid}, which has no page")
    return errors


def check_rtc_proto_types():
    documented = {f.stem for f in RTC_META.glob("*.yml")}
    missing = {}
    for f in RTC_META.glob("*.yml"):
        if not f.stem.startswith(("LiveKit.Rtc.", "LiveKit.Proto.")):
            continue
        body = f.read_text().split("\nreferences:")[0]
        for name in re.findall(r"LiveKit\.Proto\.([A-Za-z_]\w*)", body):
            if f"LiveKit.Proto.{name}" not in documented:
                missing.setdefault(name, f.stem)
    return [
        f"LiveKit.Proto.{name} is used by {user} but has no page: add {name} to the include rule of docs/filter.yml"
        for name, user in sorted(missing.items())
    ]


def check_site_links():
    errors = []
    for page in SITE.rglob("*.html"):
        text = page.read_text(errors="replace")
        for href in re.findall(r'<a\b[^>]*?\bhref="([^"]*)"', text):
            href = html.unescape(href)
            if href.startswith(("http://", "https://", "#", "mailto:", "javascript:")) or not href:
                continue
            path = urllib.parse.unquote(urllib.parse.urlparse(href).path)
            if not path:
                continue
            target = (page.parent / path).resolve()
            if target.is_dir():
                target = target / "index.html"
            if not target.exists():
                errors.append(f"{page.relative_to(SITE)} links to missing {href}")
    return sorted(set(errors))


def main():
    for required in (SERVER_META, RTC_META, SITE):
        if not required.is_dir():
            sys.exit(f"{required} not found: run `dotnet docfx docs/docfx.json` first")
    failed = False
    for title, check in (
        ("server types in bold", check_server_bold_types),
        ("LiveKit.Proto allow-list", check_rtc_proto_types),
        ("links between pages", check_site_links),
    ):
        errors = check()
        print(f"{title}: {'OK' if not errors else f'{len(errors)} problem(s)'}")
        for error in errors[:50]:
            print(f"  {error}")
        failed |= bool(errors)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
