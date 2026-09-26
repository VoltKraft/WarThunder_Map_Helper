"""Windows-only checks for stable x64 and disjoint ARM64 MSI component identities."""

from __future__ import annotations

import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET


@unittest.skipUnless(sys.platform == "win32", "MSI harvesting runs on Windows")
class HarvestIdentityTests(unittest.TestCase):
    def test_x64_identity_is_preserved_and_arm64_is_separate(self):
        script = Path(__file__).resolve().parents[2] / "packaging/windows/harvest.ps1"
        powershell = shutil.which("powershell.exe")
        self.assertIsNotNone(powershell)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            publish = root / "publish"
            publish.mkdir()
            (publish / "WarThunderMapHelper.exe").write_bytes(b"test-only payload")
            outputs = {}
            for platform in ("x64", "arm64"):
                output = root / f"{platform}.wxs"
                result = subprocess.run([
                    powershell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
                    "-PublishDir", str(publish), "-OutputFile", str(output), "-Platform", platform,
                ], capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                outputs[platform] = ET.parse(output).getroot()
            ns = {"w": "http://wixtoolset.org/schemas/v4/wxs"}
            component = {platform: xml.find(".//w:Component", ns) for platform, xml in outputs.items()}
            original_hash = hashlib.sha256(b"warthundermaphelper/component/warthundermaphelper.exe").hexdigest()[:32]
            self.assertEqual(str(uuid.UUID(original_hash)), component["x64"].get("Guid"))
            self.assertNotEqual(component["x64"].get("Guid"), component["arm64"].get("Guid"))
            self.assertEqual(r"Software\WarThunderMapHelper\Files", component["x64"].find("w:RegistryValue", ns).get("Key"))
            self.assertEqual(r"Software\WarThunderMapHelper\arm64\Files", component["arm64"].find("w:RegistryValue", ns).get("Key"))
            for item in component.values():
                self.assertEqual("INSTALLFOLDER", item.get("Directory"))


if __name__ == "__main__":
    unittest.main()
