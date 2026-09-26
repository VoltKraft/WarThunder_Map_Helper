#!/usr/bin/env python3
"""Check declared licenses of every resolved desktop NuGet dependency.

This offline gate deliberately accepts only reviewed SPDX expressions. A
license-file declaration needs an exact package/version and text checksum in
the policy. It does not replace the separate native-component notice review
or turn a package author's declaration into a legal warranty.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


class LicenseError(ValueError):
    """The dependency graph has missing or unreviewed license information."""


def contained_path(root: Path, relative: str) -> Path:
    """Resolve a package-local path without allowing escape from its root."""
    if not relative or "\\" in relative or ":" in relative:
        raise LicenseError(f"Unsafe package path: {relative!r}")
    parts = relative.split("/")
    if any(part in ("", ".", "..") for part in parts):
        raise LicenseError(f"Unsafe package path: {relative!r}")
    target = root.joinpath(*parts)
    if not target.resolve().is_relative_to(root.resolve()):
        raise LicenseError(f"Package path escapes its root: {relative!r}")
    return target


def check_graph(assets_path: Path, policy: dict) -> list[str]:
    """Validate a restored desktop graph; fail closed on missing cache inputs."""
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    roots = [Path(folder) for folder in assets.get("packageFolders", {})]
    if not roots:
        raise LicenseError("No NuGet package folders; restore the desktop project first.")
    accepted = set(policy["allowedExpressions"])
    reviewed = policy.get("reviewedLicenseFiles", {})
    verified = []
    libraries = dict(assets.get("libraries", {}))
    known = {identity.lower() for identity in libraries}
    for framework in assets.get("project", {}).get("frameworks", {}).values():
        for dependency in framework.get("downloadDependencies", []):
            version = re.fullmatch(r"\[([^,\[\]]+),\s*([^,\[\]]+)\]", dependency.get("version", ""))
            if version is None or version[1] != version[2]:
                raise LicenseError("SDK download dependencies must have exact versions.")
            identity = dependency["name"] + "/" + version[1]
            if identity.lower() not in known:
                libraries[identity] = {"type": "package", "path": identity.lower()}
                known.add(identity.lower())
    for identity, library in sorted(libraries.items()):
        if library.get("type") != "package":
            continue
        relative = library.get("path", identity.lower())
        candidates = [contained_path(root, relative) for root in roots]
        package_root = next((path for path in candidates if path.is_dir()), None)
        if package_root is None:
            raise LicenseError(f"NuGet package is missing: {identity}")
        nuspecs = list(package_root.glob("*.nuspec"))
        if len(nuspecs) != 1:
            raise LicenseError(f"Expected one NuGet manifest: {identity}")
        tree = ET.parse(nuspecs[0])
        metadata = next((node for node in tree.getroot() if node.tag.split("}")[-1] == "metadata"), None)
        if metadata is None:
            raise LicenseError(f"Missing package metadata: {identity}")
        fields = {node.tag.split("}")[-1]: node for node in metadata}
        identifier = (fields["id"].text or "") + "/" + (fields["version"].text or "")
        if identifier.lower() != identity.lower():
            raise LicenseError(f"Package identity mismatch: {identity} / {identifier}")
        license_node = fields.get("license")
        if license_node is None:
            raise LicenseError(f"No declared license; review authoritative upstream sources: {identity}")
        license_type = license_node.get("type")
        declaration = (license_node.text or "").strip()
        if license_type == "expression":
            if declaration not in accepted:
                raise LicenseError(f"Unreviewed or incompatible license: {identity}: {declaration}")
        elif license_type == "file":
            review = reviewed.get(identity.lower())
            if not review or review.get("expression") not in accepted:
                raise LicenseError(f"License file requires an exact-version review: {identity}")
            path = contained_path(package_root, declaration)
            if hashlib.sha256(path.read_bytes()).hexdigest() != review.get("sha256"):
                raise LicenseError(f"Reviewed license text changed: {identity}")
            if not review.get("source", "").startswith("https://"):
                raise LicenseError(f"Missing authoritative source for license review: {identity}")
            declaration = review["expression"]
        else:
            raise LicenseError(f"Unknown license declaration: {identity}: {license_type}")
        verified.append(f"{identity}: {declaration}")
    if not verified:
        raise LicenseError("The assets file contains no packages; expected the restored desktop project.")
    return verified


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, default=root / "src/MapHelper.Desktop/obj/project.assets.json")
    parser.add_argument("--policy", type=Path, default=root / "scripts/dependency-license-policy.json")
    args = parser.parse_args()
    try:
        policy = json.loads(args.policy.read_text(encoding="utf-8"))
        verified = check_graph(args.assets, policy)
    except (LicenseError, OSError, ValueError, KeyError, ET.ParseError) as error:
        print(f"Dependency license check failed: {error}", file=sys.stderr)
        return 1
    print(f"Verified declared licenses for {len(verified)} desktop dependencies.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
