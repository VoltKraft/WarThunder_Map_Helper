"""Build a signed Pages repository from the two newest public stable releases.

Requires Python 3.11+, flatpak, ostree, GnuPG, GH_TOKEN, and an isolated GNUPGHOME
containing the signing secret key. Downloads use public URLs without credentials.
The output must be empty/new. Failure leaves diagnostic output but must never be
deployed. Existing releases and their application commits are never modified.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import html
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
from urllib.parse import urlsplit
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET

from github_release import GitHub, expected_assets

APP_ID = "io.github.voltkraft.WarThunder_Map_Helper"
REMOTE = "war-thunder-map-helper"
ARCHITECTURES = {"x64": "x86_64", "arm64": "aarch64"}
RUNTIME_REPO = "https://flathub.org/repo/flathub.flatpakrepo"
MAX_ASSET_BYTES = 256 * 1024 * 1024
MAX_SITE_BYTES = 950_000_000
TAG = re.compile(r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")


def select_releases(releases: list[dict], requested: str = "") -> list[dict]:
    """Select newest two stable versions, oldest first; reject stale dispatches."""
    stable = [r for r in releases if not r["draft"] and not r["prerelease"]
              and TAG.fullmatch(r["tag_name"])]
    stable.sort(key=lambda r: tuple(map(int, TAG.fullmatch(r["tag_name"]).groups())))
    if not stable:
        raise ValueError("No public stable release is available.")
    if requested and requested != stable[-1]["tag_name"]:
        raise ValueError("Only the newest stable version may be deployed; refusing a downgrade.")
    return stable[-2:]


def releases_from_github(client: GitHub) -> list[dict]:
    """List all releases so publication dates cannot reorder semantic versions."""
    releases = []
    page = 1
    while True:
        batch = client.request("GET", f"/releases?per_page=100&page={page}")
        releases.extend(batch)
        if len(batch) < 100:
            return releases
        page += 1


def release_assets(release: dict, repository: str) -> list[dict]:
    """Validate the complete immutable installer set before trusting any bytes."""
    tag = release["tag_name"]
    if release["draft"] or release["prerelease"] or not TAG.fullmatch(tag):
        raise ValueError("A public stable release is required.")
    assets = release["assets"]
    names = [asset["name"] for asset in assets]
    if len(names) != len(set(names)) or set(names) != expected_assets(tag[1:]):
        raise ValueError("Release must contain exactly the four expected installers.")
    selected = [a for a in assets if a["name"].endswith(".flatpak")]
    for asset in selected:
        expected_url = f"https://github.com/{repository}/releases/download/{tag}/{asset['name']}"
        if (asset.get("state") != "uploaded" or asset.get("browser_download_url") != expected_url
                or not re.fullmatch(r"sha256:[0-9a-f]{64}", asset.get("digest") or "")
                or not isinstance(asset.get("size"), int)
                or not 0 < asset["size"] <= MAX_ASSET_BYTES):
            raise ValueError("Invalid Flatpak asset URL, state, SHA-256 digest, or size.")
    return selected


def download_asset(asset: dict, destination: Path) -> None:
    """Stream a size-bounded public asset and verify GitHub's SHA-256 digest."""
    request = Request(asset["browser_download_url"], headers={"User-Agent": "MapHelper-Flatpak-Pages"})
    digest = hashlib.sha256()
    size = 0
    with urlopen(request, timeout=180) as response, destination.open("xb") as output:
        while chunk := response.read(1024 * 1024):
            size += len(chunk)
            if size > asset["size"] or size > MAX_ASSET_BYTES:
                raise ValueError("Download exceeds its declared size.")
            digest.update(chunk)
            output.write(chunk)
    if size != asset["size"] or f"sha256:{digest.hexdigest()}" != asset["digest"]:
        raise ValueError("Release asset failed SHA-256/size verification.")


def run(*args: str) -> str:
    """Run bounded packaging tooling; propagate failures without deploying output."""
    return subprocess.run(args, check=True, stdout=subprocess.PIPE, text=True, timeout=600).stdout


def public_key(public_path: Path) -> tuple[str, bytes]:
    """Match exactly one checked-in public identity to the imported secret key."""
    listing = run("gpg", "--batch", "--with-colons", "--show-keys", str(public_path))
    if sum(line.startswith("pub:") for line in listing.splitlines()) != 1:
        raise ValueError("Exactly one repository signing public key is required.")
    fingerprint = next(line.split(":")[9] for line in listing.splitlines() if line.startswith("fpr:"))
    secret = run("gpg", "--batch", "--with-colons", "--list-secret-keys", fingerprint)
    if not any(line.startswith("sec:") for line in secret.splitlines()):
        raise ValueError("The matching repository signing secret key is unavailable.")
    exported = subprocess.run(["gpg", "--batch", "--export", fingerprint], check=True,
                              stdout=subprocess.PIPE, timeout=30).stdout
    if not exported:
        raise ValueError("Could not export the repository public key.")
    return fingerprint, exported


def validate_base_url(value: str) -> str:
    """Require an HTTPS deployment root with no credentials, queries, or controls."""
    parsed = urlsplit(value)
    if (parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password
            or parsed.query or parsed.fragment or re.search(r"[\s\x00-\x1f\x7f\\]", value)):
        raise ValueError("Pages URL must be a plain HTTPS URL.")
    return value.rstrip("/")


def write_descriptors(output: Path, base_url: str, key: bytes, tag: str, fingerprint: str) -> None:
    """Write GUI/CLI install descriptors with the same trusted signing identity."""
    base_url = validate_base_url(base_url)
    encoded = base64.b64encode(key).decode("ascii")
    (output / "repository-signing-key.gpg").write_bytes(key)
    (output / f"{REMOTE}.flatpakrepo").write_text(
        "[Flatpak Repo]\nTitle=War Thunder Map Helper\n"
        f"Url={base_url}/repo/\nHomepage={base_url}/\nDefaultBranch=stable\n"
        f"Comment=Unofficial War Thunder tactical map companion\nGPGKey={encoded}\n", encoding="utf-8")
    # An architecture-neutral ref lets Flatpak select the host architecture.
    (output / f"{APP_ID}.flatpakref").write_text(
        f"[Flatpak Ref]\nName={APP_ID}\nBranch=stable\nTitle=War Thunder Map Helper\n"
        f"Url={base_url}/repo/\nIsRuntime=false\nSuggestRemoteName={REMOTE}\n"
        f"RuntimeRepo={RUNTIME_REPO}\nGPGKey={encoded}\n", encoding="utf-8")
    (output / ".nojekyll").touch()
    (output / "index.html").write_text(f"""<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>War Thunder Map Helper — Flatpak repository</title>
<style>body{{font:18px system-ui;max-width:54rem;margin:3rem auto;padding:0 1rem;line-height:1.6}}
code,pre{{font-size:.85em;overflow-wrap:anywhere}}pre{{white-space:pre-wrap}}</style>
<h1>War Thunder Map Helper</h1>
<p>Unofficial tactical map companion. Not affiliated with or endorsed by Gaijin Entertainment.</p>
<p>Stable release: <strong>{html.escape(tag)}</strong>. Linux x86_64 and ARM64.</p>
<p><a href="{APP_ID}.flatpakref">Install with Flatpak</a> ·
<a href="{REMOTE}.flatpakrepo">Add the Flatpak repository</a> ·
<a href="https://github.com/VoltKraft/WarThunder_Map_Helper/releases">GitHub Releases</a></p>
<pre>flatpak install --user {html.escape(base_url)}/{APP_ID}.flatpakref
flatpak update --user {APP_ID}</pre>
<p>Requires Flatpak; the Freedesktop runtime is obtained from Flathub.
The application itself is distributed by this repository.</p>
<p>Signing key fingerprint: <code>{fingerprint}</code>.
<a href="repository-signing-key.gpg">Download public key</a>.</p>
<p>For migration from an existing bundle and troubleshooting, see the
<a href="https://github.com/VoltKraft/WarThunder_Map_Helper/blob/main/docs/FLATPAK.md">Flatpak documentation</a>.</p>
</html>
""", encoding="utf-8")


def check_site(output: Path) -> int:
    """Reject symlinks and oversized Pages artifacts before uploading."""
    size = 0
    for path in output.rglob("*"):
        if path.is_symlink():
            raise ValueError(f"Pages output contains a symbolic link: {path}")
        if path.is_file():
            size += path.stat().st_size
    if size > MAX_SITE_BYTES:
        raise ValueError("Repository exceeds the 950 MB Pages safety limit.")
    return size


def build(client: GitHub, repository: str, requested: str, base_url: str,
          key_path: Path, output: Path) -> dict:
    """Import released commits, sign metadata, and produce a deployable static site."""
    base_url = validate_base_url(base_url)
    if output.exists() and any(output.iterdir()):
        raise ValueError("Output directory must be empty or new.")
    home = os.environ.get("GNUPGHOME")
    if not home or Path(home).resolve().is_relative_to(output.resolve()):
        raise ValueError("Set an isolated GNUPGHOME outside the public output directory.")
    fingerprint, key = public_key(key_path)
    releases = select_releases(releases_from_github(client), requested)
    output.mkdir(parents=True, exist_ok=True)
    repo = output / "repo"
    run("ostree", f"--repo={repo}", "init", "--mode=archive-z2")
    commits = {}
    with tempfile.TemporaryDirectory(prefix="map-helper-flatpak-") as temporary:
        for release in releases:
            tag = release["tag_name"]
            sha = client.tag_sha(tag)
            if not sha or not re.fullmatch(r"[0-9a-f]{40}", sha) or release["target_commitish"] != sha:
                raise ValueError("Release tag must match its immutable source commit.")
            for asset in release_assets(release, repository):
                bundle = Path(temporary) / asset["name"]
                download_asset(asset, bundle)
                run("flatpak", "build-import-bundle", "--no-update-summary",
                    f"--gpg-sign={fingerprint}", f"--gpg-homedir={home}", str(repo), str(bundle))
                rid = "arm64" if bundle.name.endswith("-arm64.flatpak") else "x64"
                ref = f"app/{APP_ID}/{ARCHITECTURES[rid]}/stable"
                commit = run("ostree", f"--repo={repo}", "rev-parse", ref).strip()
                metadata = run("ostree", f"--repo={repo}", "cat", ref,
                               f"/files/share/metainfo/{APP_ID}.metainfo.xml")
                root = ET.fromstring(metadata)
                latest = root.find("releases/release")
                if root.findtext("id") != APP_ID or latest is None or latest.get("version") != tag[1:]:
                    raise ValueError("Bundle AppStream identity/version does not match the release.")
                commits[f"{tag}/{ARCHITECTURES[rid]}"] = commit
                bundle.unlink()
    refs = set(run("ostree", f"--repo={repo}", "refs").splitlines())
    expected = {f"app/{APP_ID}/{arch}/stable" for arch in ARCHITECTURES.values()}
    if refs != expected:
        raise ValueError(f"Unexpected application refs: {refs}")
    # Keep the previous release's objects for clients crossing a deployment.
    # Rebuilding from two releases bounds storage without relying on CI caches.
    run("flatpak", "build-update-repo", "--generate-static-deltas", "--static-delta-jobs=2",
        "--title=War Thunder Map Helper", "--default-branch=stable",
        f"--gpg-sign={fingerprint}", f"--gpg-homedir={home}", str(repo))
    run("ostree", f"--repo={repo}", "fsck")
    if not (repo / "summary.sig").is_file():
        raise ValueError("Signed repository summary is missing.")
    newest = releases[-1]["tag_name"]
    write_descriptors(output, base_url, key, newest, fingerprint)
    result = {"tag": newest, "fingerprint": fingerprint, "commits": commits}
    (output / "release.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    result["bytes"] = check_site(output)
    # A release arriving during generation must not let an older run replace it.
    select_releases(releases_from_github(client), newest)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", default=os.environ.get("GITHUB_REPOSITORY"))
    parser.add_argument("--release-tag", default="")
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--public-key", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    client = GitHub(args.repository or "", os.environ.get("GH_TOKEN", ""))
    print(json.dumps(build(client, args.repository, args.release_tag, args.base_url,
                           args.public_key, args.output_dir)))


if __name__ == "__main__":
    main()
