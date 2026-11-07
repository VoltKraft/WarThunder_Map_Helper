"""Regression tests for signed Flatpak repository input and publication boundaries."""

from __future__ import annotations

import base64
import configparser
import hashlib
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import flatpak_pages as pages

REPOSITORY = "VoltKraft/WarThunder_Map_Helper"
BASE_URL = "https://voltkraft.github.io/WarThunder_Map_Helper"
PAYLOAD = b"verified Flatpak bundle bytes"
FINGERPRINT = "A" * 40


def release(version="1.2.3", **overrides):
    tag = f"v{version}"
    result = {"tag_name": tag, "draft": False, "prerelease": False, "assets": []}
    for name in sorted(pages.expected_assets(version)):
        result["assets"].append({
            "name": name,
            "state": "uploaded",
            "size": len(PAYLOAD),
            "digest": "sha256:" + hashlib.sha256(PAYLOAD).hexdigest(),
            "browser_download_url": f"https://github.com/{REPOSITORY}/releases/download/{tag}/{name}",
        })
    result.update(overrides)
    return result


class ReleaseSelectionTests(unittest.TestCase):
    def test_selects_two_newest_semantic_versions_in_import_order(self):
        candidates = [release("1.9.0"), release("1.10.0"), release("1.2.0")]
        self.assertEqual(["v1.9.0", "v1.10.0"],
                         [r["tag_name"] for r in pages.select_releases(candidates)])

    def test_excludes_drafts_prereleases_and_noncanonical_tags(self):
        candidates = [release(), release("2.0.0", draft=True), release("3.0.0", prerelease=True)]
        candidates.extend(release(tag_name=tag) for tag in ("v4.0.0-rc.1", "v04.0.0", "4.0.0", "v4.0.0+meta"))
        self.assertEqual(["v1.2.3"], [r["tag_name"] for r in pages.select_releases(candidates)])

    def test_first_public_release_is_supported(self):
        only = release()
        self.assertEqual([only], pages.select_releases([only], "v1.2.3"))

    def test_stale_dispatch_cannot_downgrade_repository(self):
        with self.assertRaisesRegex(ValueError, "downgrade"):
            pages.select_releases([release("1.2.3"), release("1.2.4")], "v1.2.3")

    def test_no_stable_release_fails(self):
        with self.assertRaisesRegex(ValueError, "No public stable"):
            pages.select_releases([release(draft=True)])

    def test_release_listing_reads_all_pages(self):
        first = [release()] * 100
        last = [release("1.2.4")]
        client = Mock()
        client.request.side_effect = [first, last]
        self.assertEqual(first + last, pages.releases_from_github(client))
        self.assertEqual([("GET", "/releases?per_page=100&page=1"),
                          ("GET", "/releases?per_page=100&page=2")],
                         [call.args for call in client.request.call_args_list])


class ReleaseAssetTests(unittest.TestCase):
    def test_returns_only_both_flatpak_architectures(self):
        assets = pages.release_assets(release(), REPOSITORY)
        self.assertEqual({"WarThunderMapHelper-1.2.3-linux-x64.flatpak",
                          "WarThunderMapHelper-1.2.3-linux-arm64.flatpak"},
                         {a["name"] for a in assets})

    def test_rejects_missing_extra_or_duplicate_installer(self):
        for change in ("missing", "extra", "duplicate"):
            candidate = release()
            if change == "missing":
                candidate["assets"].pop()
            elif change == "extra":
                candidate["assets"].append({"name": "unexpected.flatpak"})
            else:
                candidate["assets"].append(candidate["assets"][0].copy())
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, "four expected"):
                pages.release_assets(candidate, REPOSITORY)

    def test_rejects_incomplete_or_untrusted_flatpak_metadata(self):
        changes = (
            ("state", "starter"), ("digest", None), ("digest", "sha256:bad"),
            ("digest", "sha512:" + "a" * 64), ("size", 0), ("size", -1),
            ("size", pages.MAX_ASSET_BYTES + 1), ("size", "42"),
            ("browser_download_url", "https://example.com/bundle.flatpak"),
            ("browser_download_url", "https://github.com/other/repo/releases/download/v1.2.3/app.flatpak"),
        )
        for key, value in changes:
            candidate = release()
            candidate["assets"][0][key] = value
            with self.subTest(key=key, value=value), self.assertRaisesRegex(ValueError, "Invalid Flatpak"):
                pages.release_assets(candidate, REPOSITORY)

    def test_rejects_draft_and_prerelease_even_when_asset_set_is_complete(self):
        for flag in ("draft", "prerelease"):
            with self.subTest(flag=flag), self.assertRaisesRegex(ValueError, "public stable"):
                pages.release_assets(release(**{flag: True}), REPOSITORY)


class DownloadTests(unittest.TestCase):
    def test_verified_download_does_not_send_authentication(self):
        asset = release()["assets"][0]
        with tempfile.TemporaryDirectory() as directory, patch.object(pages, "urlopen", return_value=io.BytesIO(PAYLOAD)) as fetch:
            destination = Path(directory) / "package.flatpak"
            pages.download_asset(asset, destination)
            self.assertEqual(PAYLOAD, destination.read_bytes())
        request = fetch.call_args.args[0]
        self.assertEqual(asset["browser_download_url"], request.full_url)
        self.assertIsNone(request.get_header("Authorization"))
        self.assertEqual(180, fetch.call_args.kwargs["timeout"])

    def test_rejects_truncated_and_corrupt_downloads(self):
        for content in (PAYLOAD[:-1], b"x" * len(PAYLOAD)):
            with self.subTest(content=content), tempfile.TemporaryDirectory() as directory:
                with patch.object(pages, "urlopen", return_value=io.BytesIO(content)):
                    with self.assertRaisesRegex(ValueError, "SHA-256/size"):
                        pages.download_asset(release()["assets"][0], Path(directory) / "package.flatpak")

    def test_aborts_download_above_declared_size(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(pages, "urlopen", return_value=io.BytesIO(PAYLOAD + b"extra")):
            with self.assertRaisesRegex(ValueError, "exceeds"):
                pages.download_asset(release()["assets"][0], Path(directory) / "package.flatpak")

    def test_refuses_to_overwrite_existing_download(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(pages, "urlopen", return_value=io.BytesIO(PAYLOAD)):
            destination = Path(directory) / "package.flatpak"
            destination.write_bytes(b"existing")
            with self.assertRaises(FileExistsError):
                pages.download_asset(release()["assets"][0], destination)
            self.assertEqual(b"existing", destination.read_bytes())


class DescriptorTests(unittest.TestCase):
    def test_ref_and_repo_use_same_public_key_and_stable_repository(self):
        key = b"binary public key"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pages.write_descriptors(root, BASE_URL + "/", key, "v1.2.3", FINGERPRINT)
            reference, repository = configparser.ConfigParser(), configparser.ConfigParser()
            reference.read(root / f"{pages.APP_ID}.flatpakref")
            repository.read(root / f"{pages.REMOTE}.flatpakrepo")
            ref, repo = reference["Flatpak Ref"], repository["Flatpak Repo"]
            for section in (ref, repo):
                self.assertEqual(BASE_URL + "/repo/", section["Url"])
                self.assertEqual(key, base64.b64decode(section["GPGKey"], validate=True))
            self.assertEqual(pages.APP_ID, ref["Name"])
            self.assertEqual("stable", ref["Branch"])
            self.assertEqual("stable", repo["DefaultBranch"])
            self.assertEqual(pages.REMOTE, ref["SuggestRemoteName"])
            self.assertEqual(pages.RUNTIME_REPO, ref["RuntimeRepo"])
            self.assertFalse(ref.getboolean("IsRuntime"))
            self.assertNotIn("Arch", ref)
            self.assertEqual(key, (root / "repository-signing-key.gpg").read_bytes())
            self.assertTrue((root / ".nojekyll").is_file())

    def test_key_export_is_bound_to_checked_in_fingerprint(self):
        listing = f"pub:::::::::\nfpr:::::::::{FINGERPRINT}:\n"
        with patch.object(pages, "run", side_effect=[listing, "sec:::::::::\n"]) as run:
            with patch.object(pages.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, b"public")) as export:
                self.assertEqual((FINGERPRINT, b"public"), pages.public_key(Path("signing.asc")))
        self.assertEqual(("gpg", "--batch", "--with-colons", "--list-secret-keys", FINGERPRINT), run.call_args.args)
        self.assertEqual(["gpg", "--batch", "--export", FINGERPRINT], export.call_args.args[0])

    def test_rejects_multiple_public_identities(self):
        with patch.object(pages, "run", return_value="pub:::::::::\npub:::::::::\n"):
            with self.assertRaisesRegex(ValueError, "Exactly one"):
                pages.public_key(Path("signing.asc"))

    def test_rejects_missing_secret_key(self):
        listing = f"pub:::::::::\nfpr:::::::::{FINGERPRINT}:\n"
        with patch.object(pages, "run", side_effect=[listing, ""]):
            with self.assertRaisesRegex(ValueError, "secret key"):
                pages.public_key(Path("signing.asc"))

    def test_rejects_empty_public_export(self):
        listing = f"pub:::::::::\nfpr:::::::::{FINGERPRINT}:\n"
        with patch.object(pages, "run", side_effect=[listing, "sec:::::::::\n"]):
            with patch.object(pages.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, b"")):
                with self.assertRaisesRegex(ValueError, "export"):
                    pages.public_key(Path("signing.asc"))


class PublicationBoundaryTests(unittest.TestCase):
    def test_normalizes_only_trailing_slashes_in_https_url(self):
        self.assertEqual(BASE_URL, pages.validate_base_url(BASE_URL + "/"))

    def test_rejects_non_https_credentials_queries_fragments_and_line_injection(self):
        for url in ("http://example.com", "https:///repo", "https://user:password@example.com/repo",
                    BASE_URL + "?token=secret", BASE_URL + "#fragment", BASE_URL + "\nGPGKey=bad",
                    "https://example.com\\repo", "https://example.com/a b"):
            with self.subTest(url=url), self.assertRaises(ValueError):
                pages.validate_base_url(url)

    def test_rejects_non_whitespace_control_characters_in_url(self):
        for control in ("\x00", "\x01", "\x1b", "\x7f"):
            with self.subTest(control=repr(control)), self.assertRaises(ValueError):
                pages.validate_base_url(BASE_URL + control)

    def test_site_size_includes_nested_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "repo").mkdir()
            (root / "index.html").write_bytes(b"123")
            (root / "repo" / "summary").write_bytes(b"4567")
            self.assertEqual(7, pages.check_site(root))

    def test_site_above_limit_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(pages, "MAX_SITE_BYTES", 3):
            root = Path(directory)
            (root / "summary").write_bytes(b"1234")
            with self.assertRaisesRegex(ValueError, "safety limit"):
                pages.check_site(root)

    def test_site_symlink_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "link").symlink_to("missing")
            with self.assertRaisesRegex(ValueError, "symbolic link"):
                pages.check_site(root)

    def test_build_rejects_existing_output_before_any_network_access(self):
        client = Mock()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "existing").touch()
            with self.assertRaisesRegex(ValueError, "empty or new"):
                pages.build(client, REPOSITORY, "", BASE_URL, Path("key.asc"), root)
        client.request.assert_not_called()

    def test_build_rejects_signing_home_inside_public_output(self):
        client = Mock()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "site"
            with patch.dict(pages.os.environ, {"GNUPGHOME": str(root / "private-key")}):
                with self.assertRaisesRegex(ValueError, "outside the public"):
                    pages.build(client, REPOSITORY, "", BASE_URL, Path("key.asc"), root)
        client.request.assert_not_called()


if __name__ == "__main__":
    unittest.main()
