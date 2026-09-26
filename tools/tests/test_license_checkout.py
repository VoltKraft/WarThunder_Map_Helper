"""Exercise Git checkout conversion against the pinned upstream notice hashes."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


@unittest.skipUnless(shutil.which("git"), "Git is required for checkout verification")
class LicenseCheckoutTests(unittest.TestCase):
    def test_upstream_hashes_survive_windows_and_linux_checkout(self) -> None:
        root = Path(__file__).resolve().parents[2]
        notices = Path("packaging/flatpak/licenses")
        sources = json.loads((root / notices / "sources.json").read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as directory:
            checkout = Path(directory)

            def git(*args: str, content: bytes | None = None) -> bytes:
                return subprocess.run(
                    ["git", "-C", str(checkout), *args], input=content,
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
                    timeout=30,
                ).stdout

            git("init", "--quiet", "--template=")
            (checkout / ".gitattributes").write_bytes((root / ".gitattributes").read_bytes())
            for name, source in sources.items():
                relative = (notices / name).as_posix()
                original = (root / relative).read_bytes()
                self.assertEqual(source["sha256"], hashlib.sha256(original).hexdigest())
                blob = git("hash-object", "-w", "--stdin", content=original).decode().strip()
                for autocrlf in ("true", "false"):
                    with self.subTest(notice=name, autocrlf=autocrlf):
                        converted = git(
                            "-c", f"core.autocrlf={autocrlf}",
                            "cat-file", "--filters", f"--path={relative}", blob,
                        )
                        self.assertEqual(source["sha256"], hashlib.sha256(converted).hexdigest())


if __name__ == "__main__":
    unittest.main()
