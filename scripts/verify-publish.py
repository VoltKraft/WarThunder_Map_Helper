"""Verify native publish architecture and required license files before packaging."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import struct

from release_metadata import ROOT, read_version, validate_version

RUNTIMES = ("win-x64", "win-arm64", "linux-x64", "linux-arm64")


def verify_architecture(path: Path, runtime: str) -> None:
    """Validate the native apphost rather than trusting a publish directory name."""
    with path.open("rb") as content:
        header = content.read(64)
        if runtime.startswith("win-"):
            if len(header) < 64 or header[:2] != b"MZ":
                raise ValueError("Application does not have a valid PE header.")
            offset = struct.unpack_from("<I", header, 60)[0]
            content.seek(offset)
            pe_header = content.read(6)
            expected = 0x8664 if runtime == "win-x64" else 0xAA64
            if len(pe_header) != 6 or pe_header[:4] != b"PE\x00\x00" or struct.unpack_from("<H", pe_header, 4)[0] != expected:
                raise ValueError(f"Application architecture does not match {runtime}.")
        else:
            expected = 62 if runtime == "linux-x64" else 183
            if len(header) < 20 or header[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", header, 18)[0] != expected:
                raise ValueError(f"Application architecture does not match {runtime}.")


def verify_publish(root: Path, version: str, runtime: str) -> dict[str, object]:
    validate_version(version)
    if runtime not in RUNTIMES:
        raise ValueError(f"Unsupported runtime: {runtime}")
    publish = root / "artifacts" / "publish" / f"{runtime}-{version}"
    executable = "WarThunderMapHelper.exe" if runtime.startswith("win-") else "WarThunderMapHelper"
    verify_architecture(publish / executable, runtime)
    expected = {path.relative_to(publish).as_posix(): path for path in publish.rglob("*") if path.is_file()}
    required = {executable, "LICENSE", "THIRD_PARTY_NOTICES.md", "README.md"}
    if not required <= expected.keys() or not any(name.startswith("THIRD_PARTY_LICENSES/") for name in expected):
        raise ValueError(f"Required application or license files missing from {runtime}.")
    if any(path.is_symlink() for path in publish.rglob("*")):
        raise ValueError("Publish output must not contain symbolic links.")
    if runtime.startswith("linux-") and os.name != "nt" and not (publish / executable).stat().st_mode & 0o111:
        raise ValueError("Linux executable bit is missing.")
    return {"files": len(expected), "architecture": runtime, "licensesPresent": True}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version", nargs="?")
    parser.add_argument("--runtime", action="append", choices=RUNTIMES)
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    version = validate_version(args.version) if args.version else read_version(args.root)
    result = {runtime: verify_publish(args.root, version, runtime) for runtime in args.runtime or RUNTIMES}
    output = args.root / "artifacts" / "publish-verification.json"
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(output.read_text(encoding="utf-8"))


if __name__ == "__main__":
    main()
