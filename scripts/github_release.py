"""Prepare or publish complete, retryable GitHub releases from verified CI artifacts.

The token is read only from GH_TOKEN. Plan performs only reads; publish creates or
resumes a draft for the same source commit and never edits a published release.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

from release_metadata import ROOT, metadata, validate_version


def expected_assets(version: str) -> set[str]:
    validate_version(version)
    prefix = f"WarThunderMapHelper-{version}"
    return {
        *(f"{prefix}-win-{arch}{suffix}" for arch in ("x64", "arm64") for suffix in (".msi", "-portable.zip")),
        *(f"{prefix}-linux-{arch}{suffix}" for arch in ("x64", "arm64") for suffix in (".tar.gz", ".flatpak")),
    }


def checksums(directory: Path, version: str) -> dict[str, str]:
    """Require all eight artifacts, no additional files, and no empty payloads."""
    expected = expected_assets(version)
    checksum_name = f"SHA256SUMS-{version}.txt"
    files = list(directory.iterdir())
    if any(not path.is_file() or path.is_symlink() for path in files):
        raise ValueError("Release directory must contain regular files only.")
    actual = {path.name for path in files} - {checksum_name}
    if actual != expected:
        raise ValueError(f"Incorrect release assets; missing={sorted(expected - actual)}, unexpected={sorted(actual - expected)}")
    result = {}
    for name in sorted(expected):
        path = directory / name
        if path.stat().st_size == 0:
            raise ValueError(f"Empty release asset: {name}")
        with path.open("rb") as content:
            result[name] = hashlib.file_digest(content, "sha256").hexdigest()
    (directory / checksum_name).write_text("".join(f"{digest}  {name}\n" for name, digest in result.items()), encoding="ascii")
    return result


def plan_state(release: dict | None, tag_sha: str | None, sha: str) -> str:
    """An existing public version is immutable; only this commit's draft may resume."""
    if release and not release["draft"]:
        return "published"
    if tag_sha and tag_sha != sha:
        raise ValueError("The release tag already points to a different commit; never move it.")
    if release and release["target_commitish"] != sha:
        raise ValueError("An unfinished draft exists for a different commit; resolve it before reusing this version.")
    return "resume" if release else "new"


def upload_plan(remote: list[dict], local: dict[str, str]) -> tuple[list[str], list[int]]:
    """Retry only unpublished missing/incomplete assets; reject unrecognized assets."""
    names = [asset["name"] for asset in remote]
    if len(names) != len(set(names)) or set(names) - local.keys():
        raise ValueError("Draft contains unexpected or duplicate assets.")
    existing = {asset["name"]: asset for asset in remote}
    uploads, deletes = [], []
    for name, digest in local.items():
        asset = existing.get(name)
        if asset and asset.get("state") == "uploaded" and asset.get("digest") == f"sha256:{digest}":
            continue
        if asset:
            deletes.append(asset["id"])
        uploads.append(name)
    return uploads, deletes


class GitHub:
    """Minimal GitHub REST client; HTTP errors are fatal except explicit missing resources."""

    def __init__(self, repository: str, token: str) -> None:
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise ValueError("Invalid GitHub repository.")
        if not token:
            raise ValueError("Set GH_TOKEN for GitHub release operations.")
        self.prefix = f"/repos/{repository}"
        self.token = token

    def request(self, method: str, path: str, body=None, *, missing=False, asset: Path | None = None):
        base = "https://uploads.github.com" if asset else "https://api.github.com"
        headers = {
            "Authorization": f"Bearer {self.token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "WarThunder-Map-Helper-release",
        }
        payload = None
        if asset:
            payload = asset.read_bytes()
            headers["Content-Type"] = "application/octet-stream"
        elif body is not None:
            payload = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        request = Request(base + self.prefix + path, data=payload, headers=headers, method=method)
        try:
            with urlopen(request, timeout=180) as response:
                data = response.read()
                return json.loads(data) if data else None
        except HTTPError as error:
            if error.code == 404 and missing:
                return None
            raise RuntimeError(f"GitHub {method} {path} failed with HTTP {error.code}.") from error

    def release(self, tag: str) -> dict | None:
        release = self.request("GET", "/releases/tags/" + quote(tag, safe=""), missing=True)
        if release:
            return release
        # Draft releases are not consistently included by the public tag endpoint.
        page = 1
        while True:
            releases = self.request("GET", f"/releases?per_page=100&page={page}")
            match = next((item for item in releases if item["tag_name"] == tag), None)
            if match or len(releases) < 100:
                return match
            page += 1

    def tag_sha(self, tag: str) -> str | None:
        ref = self.request("GET", "/git/ref/tags/" + quote(tag, safe=""), missing=True)
        if not ref:
            return None
        obj = ref["object"]
        for _ in range(8):
            if obj["type"] == "commit":
                return obj["sha"]
            if obj["type"] != "tag":
                break
            obj = self.request("GET", "/git/tags/" + obj["sha"])["object"]
        raise ValueError("Release tag does not resolve to a commit.")


def inspect_release(client: GitHub, details: dict[str, str], sha: str) -> tuple[str, dict | None]:
    release = client.release(details["tag"])
    state = plan_state(release, client.tag_sha(details["tag"]), sha)
    if state != "published":
        latest = client.request("GET", "/releases/latest", missing=True)
        if latest:
            try:
                latest_version = validate_version(latest["tag_name"].removeprefix("v"))
            except ValueError:
                latest_version = None
            if latest_version and tuple(map(int, latest_version.split("."))) >= tuple(map(int, details["version"].split("."))):
                return "obsolete", release
    return state, release


def publish(client: GitHub, details: dict[str, str], sha: str, directory: Path) -> str:
    local = checksums(directory, details["version"])
    checksum_name = f"SHA256SUMS-{details['version']}.txt"
    local[checksum_name] = hashlib.sha256((directory / checksum_name).read_bytes()).hexdigest()
    state, release = inspect_release(client, details, sha)
    if state in ("published", "obsolete"):
        return state
    if release is None:
        release = client.request("POST", "/releases", {
            "tag_name": details["tag"], "target_commitish": sha, "name": details["tag"],
            "body": details["notes"], "draft": True, "prerelease": False,
        })
    release_id = release["id"]
    remote = client.request("GET", f"/releases/{release_id}/assets?per_page=100")
    uploads, deletes = upload_plan(remote, local)
    for asset_id in deletes:
        client.request("DELETE", f"/releases/assets/{asset_id}")
    for name in uploads:
        client.request("POST", f"/releases/{release_id}/assets?name={quote(name, safe='')}", asset=directory / name)
    # Verify the uploaded bytes before making any package publicly visible.
    remote = client.request("GET", f"/releases/{release_id}/assets?per_page=100")
    pending, _ = upload_plan(remote, local)
    if pending or {asset["name"] for asset in remote} != local.keys():
        raise ValueError("Draft assets did not pass their SHA-256 verification; draft remains unpublished.")
    current = client.request("GET", f"/releases/{release_id}")
    if plan_state(current, client.tag_sha(details["tag"]), sha) == "published":
        return "published"
    client.request("PATCH", f"/releases/{release_id}", {
        "draft": False, "body": details["notes"], "name": details["tag"], "make_latest": "true",
    })
    if client.tag_sha(details["tag"]) != sha:
        raise ValueError("Published release tag does not match the tested commit.")
    return "published"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("plan", "publish"))
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--repository", default=os.environ.get("GITHUB_REPOSITORY"))
    parser.add_argument("--sha", required=True)
    parser.add_argument("--assets", type=Path)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.sha):
        parser.error("--sha must be the exact tested 40-character commit SHA.")
    client = GitHub(args.repository or "", os.environ.get("GH_TOKEN", ""))
    details = metadata(args.root)
    state = (inspect_release(client, details, args.sha)[0] if args.command == "plan"
             else publish(client, details, args.sha, args.assets or args.root / "artifacts" / "release"))
    outputs = {"state": state, "version": details["version"], "tag": details["tag"]}
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as output:
            for key, value in outputs.items():
                output.write(f"{key}={value}\n")
    print(json.dumps(outputs))


if __name__ == "__main__":
    main()
