"""Regression tests for transitive license and package path validation."""

import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_dependency_licenses import LicenseError, check_graph


class DependencyLicenseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.cache = self.root / "packages"
        self.assets = self.root / "project.assets.json"
        self.policy = {"allowedExpressions": ["MIT", "BSD-3-Clause"]}
        self.libraries = {}
        self.frameworks = {}

    def package(self, name, license_type="expression", declaration="MIT"):
        package = self.cache / name.lower() / "1.0.0"
        package.mkdir(parents=True)
        (package / f"{name}.nuspec").write_text(
            f'<package><metadata><id>{name}</id><version>1.0.0</version>'
            f'<license type="{license_type}">{declaration}</license>'
            '</metadata></package>', encoding="utf-8")
        self.libraries[f"{name}/1.0.0"] = {"type": "package", "path": f"{name.lower()}/1.0.0"}
        return package

    def check(self):
        self.assets.write_text(json.dumps({"packageFolders": {str(self.cache): {}}, "libraries": self.libraries,
                                         "project": {"frameworks": self.frameworks}}), encoding="utf-8")
        return check_graph(self.assets, self.policy)

    def test_compatible_dependency_is_accepted(self):
        self.package("Renderer")
        self.assertEqual(self.check(), ["Renderer/1.0.0: MIT"])

    def test_incompatible_transitive_dependency_is_rejected(self):
        self.package("TopLevel")
        self.package("Transitive", declaration="MS-PL")
        with self.assertRaisesRegex(LicenseError, "Transitive/1.0.0: MS-PL"):
            self.check()

    def test_unknown_expression_is_not_silently_accepted(self):
        self.package("Renderer", declaration="MIT AND LicenseRef-Unknown")
        with self.assertRaises(LicenseError):
            self.check()

    def test_sdk_download_dependency_is_also_checked(self):
        self.package("Application")
        self.package("NativeRuntime", declaration="MS-PL")
        del self.libraries["NativeRuntime/1.0.0"]
        self.frameworks["net10.0"] = {"downloadDependencies": [{"name": "NativeRuntime", "version": "[1.0.0, 1.0.0]"}]}
        with self.assertRaisesRegex(LicenseError, "NativeRuntime/1.0.0: MS-PL"):
            self.check()

    def test_sdk_download_range_is_rejected(self):
        self.package("Application")
        self.frameworks["net10.0"] = {"downloadDependencies": [{"name": "NativeRuntime", "version": "[1.0.0, 2.0.0]"}]}
        with self.assertRaisesRegex(LicenseError, "exact versions"):
            self.check()

    def test_missing_package_is_not_a_success(self):
        self.libraries["Missing/1.0.0"] = {"type": "package", "path": "missing/1.0.0"}
        with self.assertRaisesRegex(LicenseError, "missing"):
            self.check()

    def test_empty_graph_is_not_a_success(self):
        with self.assertRaisesRegex(LicenseError, "no packages"):
            self.check()

    def test_license_file_must_have_review_and_unchanged_hash(self):
        package = self.package("Native", "file", "LICENSE")
        content = b"A reviewed license text.\n"
        (package / "LICENSE").write_bytes(content)
        with self.assertRaisesRegex(LicenseError, "exact-version review"):
            self.check()
        self.policy["reviewedLicenseFiles"] = {"native/1.0.0": {
            "expression": "BSD-3-Clause", "sha256": hashlib.sha256(content).hexdigest(),
            "source": "https://example.org/project/LICENSE"}}
        self.assertEqual(len(self.check()), 1)
        (package / "LICENSE").write_bytes(b"Different terms")
        with self.assertRaisesRegex(LicenseError, "text changed"):
            self.check()

    def test_cache_traversal_is_rejected(self):
        self.libraries["Bad/1.0.0"] = {"type": "package", "path": "../outside"}
        with self.assertRaisesRegex(LicenseError, "Unsafe package path"):
            self.check()

    def test_license_path_traversal_is_rejected(self):
        self.package("Native", "file", "../LICENSE")
        self.policy["reviewedLicenseFiles"] = {"native/1.0.0": {"expression": "BSD-3-Clause"}}
        with self.assertRaisesRegex(LicenseError, "Unsafe package path"):
            self.check()

    def test_nuspec_identity_must_match_resolved_graph(self):
        self.package("Actual")
        self.libraries["Other/1.0.0"] = self.libraries.pop("Actual/1.0.0")
        with self.assertRaisesRegex(LicenseError, "identity mismatch"):
            self.check()


if __name__ == "__main__":
    unittest.main()
