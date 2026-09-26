"""Package a Linux publish on Windows when WSL cannot read Nextcloud reparse directories.

This does not build or execute Linux binaries. Publish the selected runtime first;
the resulting archive must still pass a native Linux startup check.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from release_metadata import read_version, validate_version  # noqa: E402


def package(version: str, runtime: str) -> Path:
    validate_version(version)
    if runtime not in ("linux-x64", "linux-arm64"):
        raise ValueError("Unsupported Linux runtime.")
    if version != read_version(ROOT):
        raise ValueError("Version must match Directory.Build.props.")
    publish = ROOT / "artifacts" / "publish" / f"{runtime}-{version}"
    if not (publish / "WarThunderMapHelper").is_file():
        raise ValueError(f"Publish {runtime} before packaging.")
    assets_file = ROOT / "src/MapHelper.Desktop/obj/project.assets.json"
    assets = json.loads(assets_file.read_text(encoding="utf-8"))
    if not any(target.endswith("/" + runtime) for target in assets.get("targets", {})):
        raise ValueError(f"Restore graph does not include {runtime}; publish that runtime immediately before packaging.")
    subprocess.run([sys.executable, str(ROOT / "scripts/check_dependency_licenses.py")], check=True)
    for document in [ROOT / "LICENSE", *ROOT.glob("*.md")]:
        shutil.copy2(document, publish / document.name)
    shutil.copytree(ROOT / "docs", publish / "docs", dirs_exist_ok=True)
    screenshot = Path("packaging/flatpak/screenshots/map.png")
    (publish / screenshot).parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / screenshot, publish / screenshot)
    notices = publish / "THIRD_PARTY_LICENSES"
    if notices.exists():
        if notices.is_symlink() or not notices.resolve().is_relative_to(publish.resolve()):
            raise ValueError("License refresh must remain inside this publish directory.")
        shutil.rmtree(notices)
    subprocess.run([
        sys.executable, str(ROOT / "tools/install-flatpak-license-notices.py"),
        "--assets-file", str(assets_file),
        "--output-dir", str(notices),
        "--supplemental-dir", str(ROOT / "packaging/flatpak/licenses"),
    ], check=True)
    release = ROOT / "artifacts" / "release"
    release.mkdir(parents=True, exist_ok=True)
    output = release / f"WarThunderMapHelper-{version}-{runtime}.tar.gz"
    temporary = output.with_suffix(output.suffix + ".tmp")

    def metadata(info: tarfile.TarInfo) -> tarfile.TarInfo:
        if not (info.isfile() or info.isdir()):
            raise ValueError("Publish directory must contain only files and directories.")
        info.uid = info.gid = 0
        info.uname = info.gname = "root"
        info.mode = 0o755 if info.isdir() or info.name in ("WarThunderMapHelper", "createdump") else 0o644
        return info

    with tarfile.open(temporary, "w:gz") as archive:
        for path in sorted(publish.rglob("*")):
            archive.add(path, arcname=path.relative_to(publish).as_posix(), recursive=False, filter=metadata)
    temporary.replace(output)
    subprocess.run([sys.executable, str(ROOT / "scripts/verify-release-archives.py"), version, "--runtime", runtime], check=True)
    with output.open("rb") as content:
        digest = hashlib.file_digest(content, "sha256").hexdigest()
    line = f"{digest}  {output.name}\n"
    (release / f"SHA256SUMS-{version}-{runtime}.txt").write_text(line, encoding="ascii")
    print(line, end="")
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version", nargs="?", default=read_version(ROOT))
    parser.add_argument("--runtime", choices=("linux-x64", "linux-arm64"), default="linux-x64")
    args = parser.parse_args()
    package(args.version, args.runtime)
