"""Regression tests for release identity, interrupted uploads and complete artifact publication."""

from __future__ import annotations

import hashlib
import importlib.util
import io
from pathlib import Path
import struct
import sys
import tarfile
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import github_release as release
from release_metadata import metadata, release_notes, validate_version

spec = importlib.util.spec_from_file_location("verify_archives", Path(__file__).resolve().parents[1] / "verify-release-archives.py")
archives = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archives)

SHA = "1" * 40
OTHER_SHA = "2" * 40
VERSION = "1.2.3"
DETAILS = {"version": VERSION, "tag": f"v{VERSION}", "notes": "### Added\n- Native packages.\n"}


class MetadataTests(unittest.TestCase):
    def test_stable_msi_compatible_semver(self):
        for valid in ("0.2.0", "1.2.3", "255.255.65535"):
            self.assertEqual(valid, validate_version(valid))
        for invalid in ("01.2.3", "1.2", "1.2.3.4", "256.1.1", "1.256.1", "1.1.65536", "1.2.3-beta", "1.2.3+build", "../1.2.3"):
            with self.subTest(version=invalid), self.assertRaises(ValueError):
                validate_version(invalid)

    def test_notes_exact_version_not_regex_prefix(self):
        text = "# Changelog\n\n## [Unreleased]\n\n## [1.2.3] - 2026-09-26\n\n### Added\n- Useful change.\n\n## [1.2.2] - 2026-09-25\n\n### Fixed\n- Old.\n"
        self.assertEqual("### Added\n- Useful change.\n", release_notes(text, VERSION))
        with self.assertRaises(ValueError):
            release_notes(text.replace("[1.2.3]", "[1.2.30]"), VERSION)

    def test_notes_reject_empty_duplicate_stale_or_invalid_date(self):
        section = "## [1.2.3] - 2026-09-26\n\n### Added\n- Change.\n"
        for text in (
            "## [1.2.3] - 2026-09-26\n",
            section + section,
            "## [1.2.4] - 2026-09-27\n\n### Added\n- New.\n" + section,
            section.replace("2026-09-26", "2026-02-30"),
            section.replace("### Added", "### Changes"),
        ):
            with self.subTest(text=text), self.assertRaises(ValueError):
                release_notes(text, VERSION)

    def test_metadata_rejects_ambiguous_version_sources(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>1.2.3</Version><Version>1.2.4</Version></PropertyGroup></Project>")
            with self.assertRaisesRegex(ValueError, "exactly one"):
                metadata(root)


class ReleasePolicyTests(unittest.TestCase):
    def test_new_and_resumable_draft(self):
        self.assertEqual("new", release.plan_state(None, None, SHA))
        self.assertEqual("new", release.plan_state(None, SHA, SHA))
        self.assertEqual("resume", release.plan_state({"draft": True, "target_commitish": SHA}, SHA, SHA))

    def test_conflicting_tag_or_draft_is_never_replaced(self):
        with self.assertRaisesRegex(ValueError, "never move"):
            release.plan_state(None, OTHER_SHA, SHA)
        with self.assertRaisesRegex(ValueError, "different commit"):
            release.plan_state({"draft": True, "target_commitish": OTHER_SHA}, None, SHA)

    def test_published_version_is_skipped_after_later_main_push(self):
        self.assertEqual("published", release.plan_state({"draft": False, "target_commitish": OTHER_SHA}, OTHER_SHA, SHA))

    def test_retry_preserves_matching_assets_and_replaces_incomplete_upload(self):
        digest = "a" * 64
        uploads, deletes = release.upload_plan([
            {"id": 1, "name": "ok", "state": "uploaded", "digest": f"sha256:{digest}"},
            {"id": 2, "name": "partial", "state": "starter", "digest": None},
            {"id": 3, "name": "changed", "state": "uploaded", "digest": "sha256:" + "b" * 64},
        ], {"ok": digest, "partial": digest, "changed": digest, "missing": digest})
        self.assertEqual(["partial", "changed", "missing"], uploads)
        self.assertEqual([2, 3], deletes)

    def test_unrecognized_remote_assets_fail_closed(self):
        with self.assertRaises(ValueError):
            release.upload_plan([{"name": "unexpected"}], {"known": "a"})
        with self.assertRaises(ValueError):
            release.upload_plan([{"name": "known"}, {"name": "known"}], {"known": "a"})

    def test_checksums_require_all_architectures_and_no_extra_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in release.expected_assets(VERSION):
                (root / name).write_bytes(name.encode())
            checksums = release.checksums(root, VERSION)
            self.assertEqual(8, len(checksums))
            self.assertEqual(8, len((root / f"SHA256SUMS-{VERSION}.txt").read_text().splitlines()))
            missing = root / sorted(checksums)[0]
            missing.unlink()
            with self.assertRaisesRegex(ValueError, "missing="):
                release.checksums(root, VERSION)
            missing.write_bytes(b"")
            with self.assertRaisesRegex(ValueError, "Empty"):
                release.checksums(root, VERSION)
            missing.write_bytes(b"valid")
            (root / "unexpected.wixpdb").write_bytes(b"extra")
            with self.assertRaisesRegex(ValueError, "unexpected="):
                release.checksums(root, VERSION)


class FakeGitHub:
    def __init__(self):
        self.current = None
        self.tag = None
        self.assets = []
        self.mutations = []
        self.fail_upload_number = None
        self.upload_attempts = 0
        self.corrupt_digest = False
        self.latest = None

    def release(self, tag):
        return self.current

    def tag_sha(self, tag):
        return self.tag

    def request(self, method, path, body=None, *, missing=False, asset=None):
        if method != "GET":
            self.mutations.append((method, path))
        if path == "/releases/latest":
            return self.latest
        if method == "POST" and path == "/releases":
            self.current = dict(body, id=10)
            return self.current
        if method == "GET" and path == "/releases/10/assets?per_page=100":
            return self.assets
        if method == "DELETE":
            asset_id = int(path.rsplit("/", 1)[1])
            self.assets = [item for item in self.assets if item["id"] != asset_id]
            return None
        if method == "POST" and asset:
            self.upload_attempts += 1
            if self.upload_attempts == self.fail_upload_number:
                raise RuntimeError("Simulated interrupted upload.")
            digest = hashlib.sha256(asset.read_bytes()).hexdigest() if not self.corrupt_digest else "bad"
            self.assets.append({"id": self.upload_attempts, "name": asset.name, "state": "uploaded", "digest": "sha256:" + digest})
            return self.assets[-1]
        if method == "GET" and path == "/releases/10":
            return self.current
        if method == "PATCH" and path == "/releases/10":
            self.current.update(body)
            self.tag = self.current["target_commitish"]
            return self.current
        raise AssertionError((method, path))


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.assets = Path(self.temporary.name)
        for name in release.expected_assets(VERSION):
            (self.assets / name).write_bytes(name.encode())
        self.client = FakeGitHub()

    def test_publication_is_last_mutation_after_all_nine_verified_assets(self):
        self.assertEqual("published", release.publish(self.client, DETAILS, SHA, self.assets))
        self.assertEqual(9, len(self.client.assets))
        self.assertEqual(("PATCH", "/releases/10"), self.client.mutations[-1])
        self.assertEqual(SHA, self.client.tag)

    def test_interruption_leaves_draft_and_retry_reuses_completed_uploads(self):
        self.client.fail_upload_number = 3
        with self.assertRaisesRegex(RuntimeError, "interrupted"):
            release.publish(self.client, DETAILS, SHA, self.assets)
        self.assertTrue(self.client.current["draft"])
        self.assertIsNone(self.client.tag)
        self.assertEqual(2, len(self.client.assets))
        self.client.fail_upload_number = None
        release.publish(self.client, DETAILS, SHA, self.assets)
        self.assertEqual(9, len(self.client.assets))
        self.assertFalse(self.client.current["draft"])
        self.assertEqual(1, self.client.mutations.count(("POST", "/releases")))

    def test_bad_uploaded_checksum_never_publishes(self):
        self.client.corrupt_digest = True
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            release.publish(self.client, DETAILS, SHA, self.assets)
        self.assertTrue(self.client.current["draft"])
        self.assertNotIn(("PATCH", "/releases/10"), self.client.mutations)

    def test_missing_local_asset_does_not_even_create_draft(self):
        next(self.assets.iterdir()).unlink()
        with self.assertRaises(ValueError):
            release.publish(self.client, DETAILS, SHA, self.assets)
        self.assertEqual([], self.client.mutations)

    def test_rerun_published_version_has_no_mutations(self):
        self.client.current = {"draft": False, "target_commitish": OTHER_SHA}
        self.client.tag = OTHER_SHA
        self.assertEqual("published", release.publish(self.client, DETAILS, SHA, self.assets))
        self.assertEqual([], self.client.mutations)

    def test_out_of_order_ci_does_not_publish_an_older_version_as_latest(self):
        self.client.latest = {"tag_name": "v1.2.4"}
        self.assertEqual("obsolete", release.publish(self.client, DETAILS, SHA, self.assets))
        self.assertEqual([], self.client.mutations)


class ArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.publish = self.root / "artifacts/publish/linux-arm64-1.2.3"
        self.publish.mkdir(parents=True)
        (self.publish / "THIRD_PARTY_LICENSES").mkdir()
        for name in ("LICENSE", "THIRD_PARTY_NOTICES.md", "README.md", "THIRD_PARTY_LICENSES/MIT.txt"):
            (self.publish / name).write_text("license text", encoding="utf-8")
        elf = bytearray(64)
        elf[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<H", elf, 18, 183)
        (self.publish / "WarThunderMapHelper").write_bytes(elf)
        self.archive = self.root / "artifacts/release/WarThunderMapHelper-1.2.3-linux-arm64.tar.gz"
        self.archive.parent.mkdir(parents=True)

    def create_archive(self, *, mode=0o755, duplicate=False, extra=None):
        with tarfile.open(self.archive, "w:gz") as archive:
            for path in self.publish.rglob("*"):
                if not path.is_file():
                    continue
                info = tarfile.TarInfo("./" + path.relative_to(self.publish).as_posix())
                data = path.read_bytes()
                info.size, info.mode = len(data), mode
                archive.addfile(info, io.BytesIO(data))
                if duplicate:
                    archive.addfile(info, io.BytesIO(data))
            if extra:
                archive.addfile(extra, io.BytesIO(b""))

    def test_tar_dot_prefix_matches_linux_tar_output(self):
        self.create_archive()
        self.assertEqual(5, archives.verify_archive(self.root, VERSION, "linux-arm64")["files"])

    def test_duplicate_and_missing_execute_bit_are_rejected(self):
        self.create_archive(duplicate=True)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            archives.verify_archive(self.root, VERSION, "linux-arm64")
        self.create_archive(mode=0o644)
        with self.assertRaisesRegex(ValueError, "executable bit"):
            archives.verify_archive(self.root, VERSION, "linux-arm64")

    def test_unsafe_paths_and_links_are_rejected(self):
        for name in ("../../escape", "/absolute", "C:/absolute", "dir\\file"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                archives.normalized_name(name)
        link = tarfile.TarInfo("unexpected-link")
        link.type, link.linkname = tarfile.SYMTYPE, "../../escape"
        self.create_archive(extra=link)
        with self.assertRaisesRegex(ValueError, "link"):
            archives.verify_archive(self.root, VERSION, "linux-arm64")

    def test_wrong_native_architecture_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "architecture"):
            archives.verify_architecture(self.publish / "WarThunderMapHelper", "linux-x64")

    def test_changed_packaged_file_is_rejected(self):
        self.create_archive()
        (self.publish / "LICENSE").write_text("changed after packaging")
        with self.assertRaisesRegex(ValueError, "differs"):
            archives.verify_archive(self.root, VERSION, "linux-arm64")

    def test_pe_machine_is_checked_for_both_windows_runtimes(self):
        path = self.root / "app.exe"
        header = bytearray(70)
        header[:2] = b"MZ"
        struct.pack_into("<I", header, 60, 64)
        header[64:68] = b"PE\x00\x00"
        for runtime, machine in (("win-x64", 0x8664), ("win-arm64", 0xAA64)):
            struct.pack_into("<H", header, 68, machine)
            path.write_bytes(header)
            archives.verify_architecture(path, runtime)
            with self.assertRaises(ValueError):
                archives.verify_architecture(path, "win-arm64" if runtime == "win-x64" else "win-x64")


if __name__ == "__main__":
    unittest.main()
