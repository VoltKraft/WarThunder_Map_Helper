#!/usr/bin/env python3
"""Install license texts from the offline NuGet build feed without network access.

Every archive is inventoried, including build-only and other-platform inputs.
This is an attribution inventory, not a runtime SBOM or a legal compatibility
assessment. Missing license texts require an exact-version source supplement;
an SPDX expression or an inventory entry never substitutes for license text.
The output directory must be empty or absent. All inputs are validated before
writing any output. Only selected regular notice files are copied from archives.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import sys
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath
from zipfile import BadZipFile, ZipFile, ZipInfo


MAX_TEXT_BYTES = 8 * 1024 * 1024
COMPONENT_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._+-]*")
NOTICE_RE = re.compile(r"(?:licen[cs]e|notice|copying|copyright|ofl)", re.IGNORECASE)
LICENSE_RE = re.compile(r"(?:licen[cs]e|copying|ofl)", re.IGNORECASE)


class NoticeError(ValueError):
    """A build input cannot supply safe, traceable license notices."""


def safe_relative_path(value: str) -> PurePosixPath:
    """Reject archive and catalog paths that could escape their destination."""
    if (
        not value or "\\" in value or ":" in value
        or any(ord(character) < 32 for character in value)
        or value.startswith("/")
        or any(part in ("", ".", "..") for part in value.split("/"))
    ):
        raise NoticeError(f"unsafe notice path: {value!r}")
    return PurePosixPath(value)


def archive_text(archive: ZipFile, item: ZipInfo) -> bytes:
    """Read a bounded regular file; never follow archive symlinks."""
    safe_relative_path(item.filename)
    # ZipInfo normalizes Windows separators and truncates NULs in filename.
    # Validate the original central-directory spelling as well on every host.
    safe_relative_path(item.orig_filename)
    mode = item.external_attr >> 16
    if item.is_dir() or stat.S_IFMT(mode) not in (0, stat.S_IFREG):
        raise NoticeError(f"notice is not a regular file: {item.filename}")
    if item.file_size > MAX_TEXT_BYTES:
        raise NoticeError(f"notice exceeds {MAX_TEXT_BYTES} bytes: {item.filename}")
    data = archive.read(item)
    if not data.strip() or b"\x00" in data:
        raise NoticeError(f"notice is empty or binary: {item.filename}")
    return data


def read_supplements(directory: Path) -> tuple[dict, dict[str, bytes], dict]:
    """Load exact package versions and hash-verified upstream notice sources."""
    catalog = json.loads((directory / "catalog.json").read_text(encoding="utf-8"))
    sources = json.loads((directory / "sources.json").read_text(encoding="utf-8"))
    if not isinstance(catalog, dict) or not isinstance(sources, dict):
        raise NoticeError("supplement catalogs must be JSON objects")
    data: dict[str, bytes] = {}
    for name, source in sources.items():
        relative = safe_relative_path(name)
        path = directory.joinpath(*relative.parts)
        if path.is_symlink() or not path.resolve().is_relative_to(directory.resolve()):
            raise NoticeError(f"unsafe supplemental file: {name}")
        content = path.read_bytes()
        if len(content) > MAX_TEXT_BYTES or not content.strip() or b"\x00" in content:
            raise NoticeError(f"invalid supplemental notice text: {name}")
        if not isinstance(source, dict) or not str(source.get("url", "")).startswith("https://"):
            raise NoticeError(f"supplement lacks an HTTPS provenance URL: {name}")
        if hashlib.sha256(content).hexdigest() != source.get("sha256"):
            raise NoticeError(f"supplement checksum mismatch: {name}")
        data[name] = content
    for package, versions in catalog.items():
        if not COMPONENT_RE.fullmatch(package) or package != package.lower() or not isinstance(versions, dict):
            raise NoticeError(f"invalid supplemental package entry: {package}")
        for version, names in versions.items():
            if not COMPONENT_RE.fullmatch(version) or not isinstance(names, list) or not names:
                raise NoticeError(f"invalid supplemental version entry: {package}/{version}")
            if any(not isinstance(name, str) or name not in data for name in names):
                raise NoticeError(f"unknown supplemental source: {package}/{version}")
    return catalog, data, sources


def collect_package(path: Path, catalog: dict, supplemental_data: dict, sources: dict) -> tuple[dict, dict[str, bytes]]:
    """Return metadata and notice bytes after checking one complete archive."""
    if path.is_symlink():
        raise NoticeError(f"source archive must not be a symlink: {path.name}")
    with ZipFile(path) as archive:
        entries = archive.infolist()
        names = [item.filename for item in entries]
        if len(names) != len(set(names)):
            raise NoticeError(f"duplicate archive entries: {path.name}")
        nuspecs = [item for item in entries if "/" not in item.filename and item.filename.lower().endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise NoticeError(f"expected one root nuspec: {path.name}")
        root = ET.fromstring(archive_text(archive, nuspecs[0]))
        metadata = next((element for element in root if element.tag.split("}")[-1] == "metadata"), None)
        if metadata is None:
            raise NoticeError(f"nuspec metadata is absent: {path.name}")
        fields = {element.tag.split("}")[-1]: element for element in metadata}

        def value(name: str) -> str:
            element = fields.get(name)
            return "" if element is None else (element.text or "").strip()

        package_id, version = value("id"), value("version")
        if not COMPONENT_RE.fullmatch(package_id) or not COMPONENT_RE.fullmatch(version):
            raise NoticeError(f"invalid package identity: {path.name}")
        package_key = package_id.lower()
        license_element = fields.get("license")
        license_type = "" if license_element is None else license_element.get("type", "")
        declared_license_file = value("license") if license_type == "file" else ""
        if declared_license_file:
            safe_relative_path(declared_license_file)
            if declared_license_file not in names:
                raise NoticeError(f"declared license file is missing: {path.name}: {declared_license_file}")

        selected: dict[str, bytes] = {}
        has_license_text = False
        for item in entries:
            if item.is_dir():
                continue
            if NOTICE_RE.search(PurePosixPath(item.filename).name) or item.filename == declared_license_file:
                relative = safe_relative_path(item.filename)
                selected[f"{package_key}/{version}/archive/{relative}"] = archive_text(archive, item)
                has_license_text |= bool(LICENSE_RE.search(relative.name)) or item.filename == declared_license_file

        supplements = []
        if package_key in catalog:
            if version not in catalog[package_key]:
                raise NoticeError(f"review supplemental notices for new package version: {package_id}/{version}")
            supplements = catalog[package_key][version]
            for name in supplements:
                selected[f"{package_key}/{version}/upstream/{name}"] = supplemental_data[name]
                has_license_text |= bool(LICENSE_RE.search(name))
        if not has_license_text:
            raise NoticeError(f"license text missing; add an upstream supplement: {package_id}/{version}")

        repository = fields.get("repository")
        with path.open("rb") as source:
            archive_sha256 = hashlib.file_digest(source, "sha256").hexdigest()
        return {
            "id": package_id,
            "version": version,
            "authors": value("authors"),
            "copyright": value("copyright"),
            "license": {"type": license_type, "value": value("license"), "url": value("licenseUrl")},
            "repository": {} if repository is None else dict(repository.attrib),
            "source_archive": path.name,
            "source_sha256": archive_sha256,
            "notice_files": sorted(selected),
            "supplemental_sources": [{"file": name, **sources[name]} for name in supplements],
        }, selected


def install_archives(archives: list[Path], output_dir: Path, supplemental_dir: Path, scope: str) -> list[dict]:
    """Validate selected archives before installing notices and their index."""
    if output_dir.is_symlink() or (output_dir.exists() and (not output_dir.is_dir() or any(output_dir.iterdir()))):
        raise NoticeError("output directory must be empty or absent")
    catalog, supplemental_data, sources = read_supplements(supplemental_dir)
    if not archives:
        raise NoticeError("NuGet source directory contains no .nupkg archives")
    packages: list[dict] = []
    output: dict[str, bytes] = {}
    identities = set()
    for archive in archives:
        metadata, files = collect_package(archive, catalog, supplemental_data, sources)
        identity = (metadata["id"].lower(), metadata["version"].lower())
        if identity in identities:
            raise NoticeError(f"duplicate package identity: {identity[0]}/{identity[1]}")
        identities.add(identity)
        packages.append(metadata)
        output.update(files)
    packages.sort(key=lambda package: (package["id"].lower(), package["version"]))
    index = {
        "schema_version": 1,
        "scope": scope,
        "packages": packages,
    }
    output["index.json"] = (json.dumps(index, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    for name, content in sorted(output.items()):
        target = output_dir.joinpath(*safe_relative_path(name).parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)
    return packages


def install_notices(source_dir: Path, output_dir: Path, supplemental_dir: Path) -> list[dict]:
    """Collect all archives in a Flatpak offline feed, including build inputs."""
    if not source_dir.is_dir():
        raise NoticeError(f"NuGet source directory does not exist: {source_dir}")
    return install_archives(sorted(source_dir.glob("*.nupkg")), output_dir, supplemental_dir,
                            "All NuGet archives in the offline build feed, including build-only and other-platform inputs; not a runtime SBOM.")


def install_from_assets(assets_file: Path, output_dir: Path, supplemental_dir: Path) -> list[dict]:
    """Resolve exact package archives from a publish restore graph and its caches.

    SDK downloadDependencies are included even when NuGet omits them from the
    libraries table. Missing cached archives fail; unrelated cache packages are
    never inventoried. This includes build tools and is not a runtime SBOM.
    """
    assets = json.loads(assets_file.read_text(encoding="utf-8"))
    identities = set()
    for identity, details in assets.get("libraries", {}).items():
        if details.get("type") == "package":
            identities.add(identity.lower())
    for framework in assets.get("project", {}).get("frameworks", {}).values():
        for dependency in framework.get("downloadDependencies", []):
            match = re.fullmatch(r"\[([^,\[\]]+),\s*([^,\[\]]+)\]", dependency.get("version", ""))
            if match is None or match[1] != match[2]:
                raise NoticeError("downloadDependencies must use exact versions")
            identities.add(f"{dependency['name']}/{match[1]}".lower())
    folders = [Path(folder) for folder in assets.get("packageFolders", {})]
    archives = []
    for identity in sorted(identities):
        parts = identity.split("/")
        if len(parts) != 2 or any(not COMPONENT_RE.fullmatch(part) for part in parts):
            raise NoticeError(f"unsafe package identity: {identity}")
        package, version = parts
        candidates = [folder / package / version / f"{package}.{version}.nupkg" for folder in folders]
        archive = next((candidate for candidate in candidates if candidate.is_file()), None)
        if archive is None:
            raise NoticeError(f"restored package archive is missing: {identity}")
        archives.append(archive)
    return install_archives(archives, output_dir, supplemental_dir,
                            "All package and SDK download inputs in the application restore graph; not a runtime SBOM.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    inputs = parser.add_mutually_exclusive_group(required=True)
    inputs.add_argument("--source-dir", type=Path)
    inputs.add_argument("--assets-file", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--supplemental-dir", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.assets_file is not None:
            packages = install_from_assets(args.assets_file, args.output_dir, args.supplemental_dir)
        else:
            packages = install_notices(args.source_dir, args.output_dir, args.supplemental_dir)
    except (NoticeError, OSError, BadZipFile, ET.ParseError, json.JSONDecodeError) as exc:
        print(f"License notice installation failed: {exc}", file=sys.stderr)
        return 1
    print(f"Installed license texts and notices for {len(packages)} NuGet archives.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
