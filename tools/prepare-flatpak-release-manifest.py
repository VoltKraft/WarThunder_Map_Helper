#!/usr/bin/env python3
"""Pin the Flatpak manifest's Git source to an immutable release commit."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


GIT_SOURCE_RE = re.compile(r"^(?P<indent>[ \t]*)-[ \t]+type:[ \t]*git[ \t]*(?:#.*)?$")
COMMIT_RE = re.compile(r"^(?P<indent>[ \t]*)commit:[ \t]*.*$")
TAG_RE = re.compile(r"^[ \t]*tag:[ \t]*.*$")
SHA_RE = re.compile(r"^[0-9a-fA-F]{40}$")
LOCAL_SOURCE_SKIPS = (
    ".git", ".build-cache", "artifacts", ".flatpak-builder", ".vs", ".venv",
    "build-dir", "repo", "TestResults",
    "src/MapHelper.Core/bin", "src/MapHelper.Core/obj",
    "src/MapHelper.Desktop/bin", "src/MapHelper.Desktop/obj",
    "src/MapHelper.Telemetry/bin", "src/MapHelper.Telemetry/obj",
    "tests/MapHelper.Tests/bin", "tests/MapHelper.Tests/obj",
    "tests/MapHelper.UiTests/bin", "tests/MapHelper.UiTests/obj",
    "packaging/windows/bin", "packaging/windows/obj",
    "tools/__pycache__", "tools/tests/__pycache__",
    "scripts/__pycache__", "scripts/tests/__pycache__",
)
LOCAL_SOURCE = (
    "      - type: dir\n"
    "        path: ../../..\n"
    "        # Directory-source exclusions are literal relative paths, not globs.\n"
    "        skip:\n"
) + "".join(f"          - {path}\n" for path in LOCAL_SOURCE_SKIPS)
SOURCE_URL = "https://github.com/VoltKraft/WarThunder_Map_Helper.git"


class ManifestError(ValueError):
    """Raised when the source manifest cannot be pinned safely."""


def pin_git_source(text: str, commit: str) -> str:
    """Return *text* with its single Git source pinned to *commit*."""
    if not SHA_RE.fullmatch(commit):
        raise ManifestError("commit must be a full 40-character hexadecimal SHA")

    # The checked-in manifest must build before the first GitHub repository/tag
    # exists. Replace only its exact local-source block, never a generic path.
    if LOCAL_SOURCE in text:
        if text.count(LOCAL_SOURCE) != 1 or re.search(r"(?m)^\s+- type: git\s*$", text):
            raise ManifestError("expected exactly one application source")
        text = text.replace(LOCAL_SOURCE, (
            "      - type: git\n"
            f"        url: {SOURCE_URL}\n"
            f"        commit: {commit.lower()}\n"
        ))

    lines = text.splitlines(keepends=True)
    git_sources: list[tuple[int, str]] = []
    for index, line in enumerate(lines):
        match = GIT_SOURCE_RE.match(line.rstrip("\r\n"))
        if match:
            git_sources.append((index, match.group("indent")))

    if len(git_sources) != 1:
        raise ManifestError(
            f"expected exactly one Git source, found {len(git_sources)}"
        )

    source_start, source_indent = git_sources[0]
    source_end = len(lines)
    next_source_re = re.compile(rf"^{re.escape(source_indent)}-[ \t]+")
    for index in range(source_start + 1, len(lines)):
        if next_source_re.match(lines[index]):
            source_end = index
            break

    commit_indexes: list[int] = []
    tag_indexes: list[int] = []
    for index in range(source_start + 1, source_end):
        stripped = lines[index].rstrip("\r\n")
        if COMMIT_RE.match(stripped):
            commit_indexes.append(index)
        if TAG_RE.match(stripped):
            tag_indexes.append(index)

    if len(commit_indexes) != 1:
        raise ManifestError(
            "the Git source must contain exactly one commit field; "
            f"found {len(commit_indexes)}"
        )
    if len(tag_indexes) > 1:
        raise ManifestError(
            f"the Git source must contain at most one tag field; found {len(tag_indexes)}"
        )

    commit_index = commit_indexes[0]
    commit_match = COMMIT_RE.match(lines[commit_index].rstrip("\r\n"))
    assert commit_match is not None
    newline = "\r\n" if lines[commit_index].endswith("\r\n") else "\n"
    if not lines[commit_index].endswith(("\n", "\r")):
        newline = ""
    lines[commit_index] = f"{commit_match.group('indent')}commit: {commit.lower()}{newline}"

    for index in reversed(tag_indexes):
        del lines[index]

    return "".join(lines)


def prepare_manifest(source: Path, output: Path, commit: str) -> None:
    """Read *source*, pin it to *commit*, and write it to *output*."""
    if source.resolve() == output.resolve():
        raise ManifestError("source and output must be different files")

    text = source.read_text(encoding="utf-8")
    updated = pin_git_source(text, commit)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(updated, encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--commit", required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        prepare_manifest(args.source, args.output, args.commit)
    except (ManifestError, OSError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    print(f"Wrote commit-pinned Flatpak manifest to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
