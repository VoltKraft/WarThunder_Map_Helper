"""Exercise Pages API publication and bounded failure handling without credentials."""

from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "deploy_pages.cjs"
HARNESS = r"""
const deploy = require(process.argv[1]);
const scenario = JSON.parse(process.argv[2]);
const calls = [];
let clock = 0;
let reads = 0;
const github = { request: async (route, body) => {
  calls.push({ route, body });
  if (route.endsWith('/cancel')) return { data: {} };
  if (route.startsWith('POST')) return { data: { id: 'deployment-42' } };
  const status = scenario.statuses[Math.min(reads++, scenario.statuses.length - 1)];
  if (status === 'http-error') throw new Error('HTTP failure');
  return { data: { status } };
}};
deploy({
  github, context: { repo: { owner: 'owner', repo: 'repo' }, sha: 'a'.repeat(40) },
  core: { getIDToken: async () => 'oidc-identity', info: () => {}, warning: () => {} },
  artifactId: scenario.artifact || '123', now: () => clock,
  sleep: async milliseconds => { clock += milliseconds; },
  timeout: scenario.timeout || 600000
}).then(() => console.log(JSON.stringify({ calls, error: null })))
  .catch(error => console.log(JSON.stringify({ calls, error: error.message })));
"""


@unittest.skipUnless(shutil.which("node"), "Node.js is required to validate the GitHub Actions deployment script")
class PagesDeploymentTests(unittest.TestCase):
    def run_scenario(self, statuses, **options):
        result = subprocess.run(
            ["node", "-e", HARNESS, str(SCRIPT), json.dumps({"statuses": statuses, **options})],
            check=True, capture_output=True, text=True, timeout=10,
        )
        return json.loads(result.stdout)

    def test_success_uses_current_artifact_commit_and_oidc(self):
        result = self.run_scenario(["queued", "succeed"])
        self.assertIsNone(result["error"])
        self.assertEqual(3, len(result["calls"]))
        self.assertEqual({
            "owner": "owner", "repo": "repo", "artifact_id": 123,
            "pages_build_version": "a" * 40, "oidc_token": "oidc-identity",
        }, result["calls"][0]["body"])
        self.assertFalse(any(call["route"].endswith("/cancel") for call in result["calls"]))

    def test_terminal_failure_is_not_reported_as_success(self):
        result = self.run_scenario(["deployment_content_failed"])
        self.assertIn("deployment_content_failed", result["error"])
        self.assertEqual(2, len(result["calls"]))

    def test_timeout_cancels_pending_deployment(self):
        result = self.run_scenario(["queued"], timeout=20000)
        self.assertIn("exceeded", result["error"])
        self.assertTrue(result["calls"][-1]["route"].endswith("/cancel"))

    def test_transient_status_failure_is_retried(self):
        result = self.run_scenario(["http-error", "succeed"])
        self.assertIsNone(result["error"])
        self.assertEqual(3, len(result["calls"]))

    def test_repeated_status_failure_cancels(self):
        result = self.run_scenario(["http-error"])
        self.assertIn("five attempts", result["error"])
        self.assertEqual(7, len(result["calls"]))
        self.assertTrue(result["calls"][-1]["route"].endswith("/cancel"))

    def test_invalid_artifact_never_creates_deployment(self):
        for artifact in ("-1", "123xyz", "9007199254740992"):
            with self.subTest(artifact=artifact):
                result = self.run_scenario(["succeed"], artifact=artifact)
                self.assertIn("valid Pages artifact ID", result["error"])
                self.assertEqual([], result["calls"])


if __name__ == "__main__":
    unittest.main()
