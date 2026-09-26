#!/usr/bin/env python3
"""Merge verified x64/ARM64 source lists into the committed offline build feed."""
from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("prepare_flathub_release", Path(__file__).with_name("prepare-flathub-release.py"))
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--x64", type=Path, required=True)
    parser.add_argument("--arm64", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.output.resolve() in (args.x64.resolve(), args.arm64.resolve()):
            raise MODULE.ManifestError("output must not overwrite an input feed")
        sources = MODULE.merge_nuget_sources(MODULE.read_json(args.x64), MODULE.read_json(args.arm64))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(sources, indent=4, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    except (MODULE.ManifestError, OSError) as exc:
        print(f"Cannot merge offline feeds: {exc}", file=sys.stderr)
        return 1
    print(f"Wrote {len(sources)} verified source entries to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
