"""Read and validate the single release version and its Keep a Changelog entry.

Stable releases use major.minor.patch within Windows Installer's numeric limits.
This module performs no network access and does not modify project metadata.
"""

from __future__ import annotations

import argparse
from datetime import date
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
VERSION_PATTERN = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")


def validate_version(version: str) -> str:
    """Reject ambiguous SemVer and versions that MSI cannot represent."""
    match = VERSION_PATTERN.fullmatch(version)
    if not match or any(int(part) > limit for part, limit in zip(match.groups(), (255, 255, 65535))):
        raise ValueError("Version must be stable major.minor.patch within MSI limits (255.255.65535).")
    return version


def read_version(root: Path = ROOT) -> str:
    nodes = ET.parse(root / "Directory.Build.props").findall("./PropertyGroup/Version")
    if len(nodes) != 1 or nodes[0].text is None:
        raise ValueError("Directory.Build.props must contain exactly one Version element.")
    return validate_version(nodes[0].text.strip())


def release_notes(changelog: str, version: str) -> str:
    """Return the exact first released section, without its heading or link definitions."""
    validate_version(version)
    headings = list(re.finditer(r"^## \[([^]\r\n]+)\](.*)$", changelog, re.MULTILINE))
    released = [heading for heading in headings if heading.group(1) != "Unreleased"]
    matches = [heading for heading in released if heading.group(1) == version]
    if len(matches) != 1 or not released or matches[0] != released[0]:
        raise ValueError(f"Changelog must contain exactly one latest release entry for {version}.")
    heading = matches[0]
    date_match = re.fullmatch(r" - ([0-9]{4}-[0-9]{2}-[0-9]{2})\s*", heading.group(2))
    if not date_match:
        raise ValueError("Release heading must use '## [version] - YYYY-MM-DD'.")
    date.fromisoformat(date_match.group(1))
    end = next((item.start() for item in headings if item.start() > heading.start()), len(changelog))
    notes = re.split(r"^\[[^]\n]+\]:", changelog[heading.end():end], maxsplit=1, flags=re.MULTILINE)[0].strip()
    if not re.search(r"^### (Added|Changed|Deprecated|Removed|Fixed|Security)\s*$", notes, re.MULTILINE):
        raise ValueError("Release notes need at least one Keep a Changelog category.")
    if not re.search(r"^- \S", notes, re.MULTILINE):
        raise ValueError("Release notes must contain a nonempty change entry.")
    return notes + "\n"


def metadata(root: Path = ROOT) -> dict[str, str]:
    version = read_version(root)
    return {"version": version, "tag": f"v{version}", "notes": release_notes((root / "CHANGELOG.md").read_text(encoding="utf-8-sig"), version)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--format", choices=("version", "tag", "json"), default="json")
    parser.add_argument("--notes-output", type=Path)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()
    result = metadata(args.root)
    if args.notes_output:
        args.notes_output.parent.mkdir(parents=True, exist_ok=True)
        args.notes_output.write_text(result["notes"], encoding="utf-8")
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as output:
            output.write(f"version={result['version']}\ntag={result['tag']}\n")
    print(json.dumps(result) if args.format == "json" else result[args.format])


if __name__ == "__main__":
    main()
