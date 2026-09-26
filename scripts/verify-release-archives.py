"""Verify release archive paths, architectures, modes and bytes against publish output."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import struct
import tarfile
import zipfile

from release_metadata import ROOT, read_version, validate_version

RUNTIMES = ("win-x64", "win-arm64", "linux-x64", "linux-arm64")


def normalized_name(name: str) -> str:
    while name.startswith("./"):
        name = name[2:]
    path = PurePosixPath(name)
    if not name or path.is_absolute() or ".." in path.parts or "\\" in name or ":" in name:
        raise ValueError(f"Unsafe archive member: {name!r}")
    return path.as_posix()


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


def verify_archive(root: Path, version: str, runtime: str) -> dict[str, object]:
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
    is_zip = runtime.startswith("win-")
    suffix = "-portable.zip" if is_zip else ".tar.gz"
    archive_path = root / "artifacts" / "release" / f"WarThunderMapHelper-{version}-{runtime}{suffix}"
    opener = zipfile.ZipFile if is_zip else tarfile.open
    with opener(archive_path, "r") as archive:
        entries = {}
        for entry in archive.infolist() if is_zip else archive.getmembers():
            name = entry.filename if is_zip else entry.name
            if not is_zip and name in (".", "./") and entry.isdir():
                continue
            name = normalized_name(name)
            if (is_zip and entry.is_dir()) or (not is_zip and entry.isdir()):
                continue
            if (is_zip and (entry.external_attr >> 16) & 0o170000 == 0o120000) or (not is_zip and not entry.isfile()):
                raise ValueError(f"Archive contains a link or special file: {name}")
            if name in entries:
                raise ValueError(f"Duplicate archive member: {name}")
            entries[name] = entry
        if entries.keys() != expected.keys():
            raise ValueError(f"Archive file list differs from {runtime} publish output.")
        if not is_zip and not entries[executable].mode & 0o111:
            raise ValueError("Linux executable bit is missing.")
        for name, entry in entries.items():
            with (archive.open(entry) if is_zip else archive.extractfile(entry)) as content:
                actual = hashlib.file_digest(content, "sha256").digest()
            with expected[name].open("rb") as content:
                if actual != hashlib.file_digest(content, "sha256").digest():
                    raise ValueError(f"Packaged file differs from publish output: {name}")
    with archive_path.open("rb") as content:
        digest = hashlib.file_digest(content, "sha256").hexdigest()
    return {"files": len(expected), "allFilesMatchPublish": True, "sha256": digest}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version", nargs="?")
    parser.add_argument("--runtime", action="append", choices=RUNTIMES)
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    version = validate_version(args.version) if args.version else read_version(args.root)
    result = {runtime: verify_archive(args.root, version, runtime) for runtime in args.runtime or RUNTIMES}
    output = args.root / "artifacts" / "archive-verification.json"
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(output.read_text(encoding="utf-8"))


if __name__ == "__main__":
    main()
