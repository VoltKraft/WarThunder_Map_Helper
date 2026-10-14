from __future__ import annotations

import copy
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("prepare_reduced_avalonia", ROOT / "scripts/prepare_reduced_avalonia.py")
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class PreparationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.lock = MODULE.read_lock(ROOT)

    def test_source_archive_and_every_compiler_input_are_locked(self) -> None:
        sources = MODULE.official_sources(self.lock)
        self.assertEqual(3, len(sources))
        self.assertEqual(MODULE.PACKAGE_IDS, {item["id"].lower() for item in self.lock["packages"]})
        manifest = (ROOT / "packaging/flatpak/flathub/io.github.voltkraft.WarThunder_Map_Helper.yml").read_text()
        self.assertIn(self.lock["source"]["url"], manifest)
        self.assertIn(self.lock["source"]["sha512"], manifest)

    def test_rejects_alternate_hosts_traversal_duplicates_and_missing_hashes(self) -> None:
        invalid = []
        for field, value in [("filename", "../escape"), ("url", "https://example.org/source"), ("sha512", "")]:
            lock = copy.deepcopy(self.lock)
            lock["source"][field] = value
            invalid.append(lock)
        lock = copy.deepcopy(self.lock)
        lock["packages"][1] = lock["packages"][0]
        invalid.append(lock)
        lock = copy.deepcopy(self.lock)
        lock["packages"][0]["filename"] = "../escape"
        invalid.append(lock)
        path = self.root / MODULE.LOCK_PATH
        path.parent.mkdir(parents=True)
        for lock in invalid:
            with self.subTest(lock=lock):
                path.write_text(json.dumps(lock))
                with self.assertRaises(MODULE.PreparationError):
                    MODULE.read_lock(self.root)

    def test_download_verifies_bytes_and_reuses_only_matching_cache(self) -> None:
        content = b"verified archive"
        digest = hashlib.sha512(content).hexdigest()
        destination = self.root / "input.nupkg"
        with patch.object(MODULE.urllib.request, "urlopen", return_value=io.BytesIO(content)) as request:
            MODULE.download_verified("https://example.invalid/input", digest, destination)
            MODULE.download_verified("https://example.invalid/input", digest, destination)
            self.assertEqual(1, request.call_count)
        destination.write_bytes(b"modified")
        with self.assertRaisesRegex(MODULE.PreparationError, "Cached input checksum mismatch"):
            MODULE.download_verified("https://example.invalid/input", digest, destination)

    def test_bad_or_oversized_download_never_publishes_input(self) -> None:
        destination = self.root / "input.nupkg"
        for limit, message in [(100, "checksum mismatch"), (1, "size limit")]:
            with self.subTest(limit=limit), patch.object(MODULE, "MAX_INPUT_BYTES", limit), \
                    patch.object(MODULE.urllib.request, "urlopen", return_value=io.BytesIO(b"bad")):
                with self.assertRaisesRegex(MODULE.PreparationError, message):
                    MODULE.download_verified("https://example.invalid/input", "a" * 128, destination)
            self.assertEqual([], list(self.root.iterdir()))


if __name__ == "__main__":
    unittest.main()
