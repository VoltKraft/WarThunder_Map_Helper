"""Early release checks must catch stale Flatpak inputs before packaging."""

from pathlib import Path
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_metadata import metadata


class AppStreamMetadataTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        (self.root / "Directory.Build.props").write_text(
            "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>", encoding="utf-8")
        (self.root / "CHANGELOG.md").write_text(
            "## [Unreleased]\n\n## [1.2.3] - 2026-10-03\n\n### Fixed\n- Release metadata.\n", encoding="utf-8")
        self.image = self.root / "packaging/flatpak/screenshots/map.png"
        self.image.parent.mkdir(parents=True)
        self.image.write_bytes(b"PNG fixture")
        self.path = self.image.parent.parent / "io.github.voltkraft.WarThunder_Map_Helper.metainfo.xml"
        self.url = "https://raw.githubusercontent.com/VoltKraft/WarThunder_Map_Helper/v1.2.3/packaging/flatpak/screenshots/map.png"
        self.component = ET.fromstring(
            '<component><releases><release version="1.2.3" date="2026-10-03"/>'
            '<release version="1.2.2" date="2026-10-02"/></releases>'
            f'<screenshots><screenshot><image>{self.url}</image></screenshot></screenshots></component>')

    def read(self):
        ET.ElementTree(self.component).write(self.path, encoding="utf-8")
        return metadata(self.root)

    def test_matching_metadata_preserves_release_output(self):
        self.assertEqual({"version": "1.2.3", "tag": "v1.2.3", "notes": "### Fixed\n- Release metadata.\n"}, self.read())

    def test_rejects_stale_or_missing_latest_release(self):
        latest = self.component.find("releases/release")
        latest.set("version", "1.2.2")
        with self.assertRaisesRegex(ValueError, "Latest AppStream"):
            self.read()
        self.component.remove(self.component.find("releases"))
        with self.assertRaisesRegex(ValueError, "Latest AppStream"):
            self.read()

    def test_rejects_duplicate_current_release(self):
        ET.SubElement(self.component.find("releases"), "release", version="1.2.3", date="2026-10-03")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            self.read()

    def test_rejects_missing_invalid_or_mismatched_release_date(self):
        latest = self.component.find("releases/release")
        for value in ("", "2026-02-30", "2026-10-02", "20261003"):
            latest.set("date", value)
            with self.subTest(date=value), self.assertRaisesRegex(ValueError, "release date"):
                self.read()
        latest.attrib.pop("date")
        with self.assertRaisesRegex(ValueError, "release date"):
            self.read()

    def test_rejects_stale_foreign_or_unsafe_screenshot_urls(self):
        image = self.component.find("screenshots/screenshot/image")
        for url in (
            self.url.replace("v1.2.3", "v1.2.2"),
            self.url.replace("VoltKraft", "other"),
            self.url.replace("map.png", "../map.png"),
            self.url.replace("map.png", "%2e%2e/map.png"),
            self.url + "?download=1",
            "",
        ):
            image.text = url
            with self.subTest(url=url), self.assertRaisesRegex(ValueError, "screenshot"):
                self.read()

    def test_checks_every_screenshot(self):
        ET.SubElement(self.component.find("screenshots/screenshot"), "image").text = self.url.replace("v1.2.3", "v1.2.2")
        with self.assertRaisesRegex(ValueError, "matching repository"):
            self.read()

    def test_rejects_missing_screenshot_or_local_file(self):
        self.image.unlink()
        with self.assertRaisesRegex(ValueError, "file is missing"):
            self.read()
        self.component.remove(self.component.find("screenshots"))
        with self.assertRaisesRegex(ValueError, "at least one"):
            self.read()


if __name__ == "__main__":
    unittest.main()
