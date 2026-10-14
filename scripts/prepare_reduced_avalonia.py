#!/usr/bin/env python3
"""Download verified framework build inputs and invoke the offline source builder.

The checked-in input lock is the only download authority. Outputs are local
packages, never upstream package replacements in the global NuGet cache. Run
before restoring the application; Flatpak invokes the same builder offline.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path


LOCK_PATH = Path("packaging/avalonia-reduced/inputs.json")
PACKAGE_IDS = {"avalonia", "avalonia.themes.fluent", "avalonia.remote.protocol"}
CUSTOM_IDS = {"avalonia", "avalonia.themes.fluent"}
MAX_INPUT_BYTES = 256 * 1024 * 1024


class PreparationError(ValueError):
    """An input is untrusted, incomplete, or the source build failed."""


def read_lock(source_root: Path) -> dict:
    """Validate the narrow immutable upstream archive and official package set."""
    lock = json.loads((source_root / LOCK_PATH).read_text(encoding="utf-8"))
    if lock.get("schemaVersion") != 1 or lock.get("upstreamVersion") != "12.1.4" or lock.get("packageVersion") != "12.1.4.1":
        raise PreparationError("Unreviewed reduced Avalonia package version")
    source = lock.get("source", {})
    if source.get("filename") != "avalonia-source.tar.gz" or not re.fullmatch(
        r"https://codeload\.github\.com/AvaloniaUI/Avalonia/tar\.gz/[0-9a-f]{40}", source.get("url", "")
    ):
        raise PreparationError("Source must be the immutable upstream Avalonia archive")
    if source["url"].rsplit("/", 1)[-1] != lock.get("upstreamCommit"):
        raise PreparationError("Source URL must identify the locked upstream commit")
    packages = lock.get("packages", [])
    if not isinstance(packages, list) or len(packages) != len(PACKAGE_IDS):
        raise PreparationError("Expected exactly the reviewed upstream NuGet input set")
    seen = set()
    for item in [source, *packages]:
        if not re.fullmatch(r"[0-9a-f]{128}", item.get("sha512", "")):
            raise PreparationError("Each input needs a SHA-512 archive checksum")
    for package in packages:
        identity = package.get("id", "").lower()
        version = package.get("version")
        if identity not in PACKAGE_IDS or identity in seen or version != lock["upstreamVersion"]:
            raise PreparationError("Unexpected or duplicate upstream NuGet input")
        expected = f"https://api.nuget.org/v3-flatcontainer/{identity}/{version}/{identity}.{version}.nupkg"
        if package.get("url") != expected or package.get("filename") != f"{identity}.{version}.nupkg":
            raise PreparationError("Package input must use its official NuGet archive URL")
        seen.add(identity)
    return lock


def download_verified(url: str, digest: str, destination: Path) -> None:
    """Atomically cache a bounded download; existing input must also match its lock."""
    if destination.is_symlink():
        raise PreparationError(f"Input path is a symbolic link: {destination.name}")
    if destination.exists():
        with destination.open("rb") as stream:
            actual = hashlib.file_digest(stream, "sha512").hexdigest()
        if actual != digest:
            raise PreparationError(f"Cached input checksum mismatch: {destination.name}")
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        request = urllib.request.Request(url, headers={"User-Agent": "WarThunderMapHelper-source-builder"})
        with urllib.request.urlopen(request, timeout=60) as response, tempfile.NamedTemporaryFile(
            dir=destination.parent, delete=False
        ) as target:
            temporary = Path(target.name)
            checksum = hashlib.sha512()
            total = 0
            while chunk := response.read(1024 * 1024):
                total += len(chunk)
                if total > MAX_INPUT_BYTES:
                    raise PreparationError("Framework input exceeds the download size limit")
                checksum.update(chunk)
                target.write(chunk)
        if checksum.hexdigest() != digest:
            raise PreparationError(f"Downloaded input checksum mismatch: {destination.name}")
        temporary.replace(destination)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def official_sources(lock: dict) -> list[dict[str, str]]:
    """Return hash-pinned original packages required only as compiler inputs."""
    return [{
        "type": "file", "url": item["url"], "sha512": item["sha512"],
        "dest": "nuget-sources",
        "dest-filename": f"{item['id'].lower()}.{item['version']}.nupkg",
    } for item in lock["packages"]]


def prepare(source_root: Path, work_dir: Path, output: Path, dotnet: str = "dotnet") -> dict:
    """Verify source inputs, build the two local packages, and return their lock."""
    source_root, work_dir, output = source_root.resolve(), work_dir.resolve(), output.resolve()
    lock = read_lock(source_root)
    dotnet_path = shutil.which(dotnet)
    if dotnet_path is None:
        raise PreparationError("Pinned .NET SDK executable is not available")
    inputs = work_dir / "inputs"
    archive = inputs / lock["source"]["filename"]
    download_verified(lock["source"]["url"], lock["source"]["sha512"], archive)
    for item in official_sources(lock):
        download_verified(item["url"], item["sha512"], inputs / item["dest-filename"])
    with tempfile.TemporaryDirectory(prefix="build-", dir=work_dir) as temporary:
        result = subprocess.run([
            sys.executable, str(source_root / "scripts/build_reduced_avalonia.py"),
            "--source-archive", str(archive), "--upstream-packages", str(inputs),
            "--output", str(output), "--work-dir", temporary, "--dotnet", str(Path(dotnet_path).resolve()),
        ], cwd=source_root)
    if result.returncode:
        raise PreparationError(f"Reduced Avalonia source build failed (exit code {result.returncode})")
    for identity in CUSTOM_IDS:
        package = output / f"{identity}.{lock['packageVersion']}.nupkg"
        if package.is_symlink() or not package.is_file():
            raise PreparationError(f"Reduced package was not produced: {package.name}")
    return lock


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path)
    parser.add_argument("--work-dir", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    root = args.source_root.resolve()
    try:
        prepare(root, args.work_dir or root / ".build-cache/reduced-avalonia",
                args.output or root / ".build-cache/reduced-avalonia/feed", args.dotnet)
    except (PreparationError, OSError, ValueError) as exc:
        print(f"Reduced Avalonia preparation failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
