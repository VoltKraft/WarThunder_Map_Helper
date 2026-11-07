'use strict';

/**
 * Deploy this workflow's Pages artifact with an OIDC identity and await its result.
 * Receives the existing actions/github-script clients; never logs token-bearing
 * request objects. Throws on failure and cancels pending deployments on timeout.
 * Clock and sleep injection allow bounded polling to be tested without waiting.
 */
module.exports = async function deployPages({
  github, context, core,
  artifactId = process.env.PAGES_ARTIFACT_ID,
  now = Date.now,
  sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)),
  timeout = 600000
}) {
  if (!/^[1-9][0-9]*$/.test(artifactId || '') || !Number.isSafeInteger(Number(artifactId))) {
    throw new Error('A valid Pages artifact ID from this workflow is required.');
  }
  const oidcToken = await core.getIDToken();
  let deployment;
  try {
    ({ data: deployment } = await github.request('POST /repos/{owner}/{repo}/pages/deployments', {
      ...context.repo,
      artifact_id: Number(artifactId),
      pages_build_version: context.sha,
      oidc_token: oidcToken
    }));
  } catch (error) {
    throw new Error(`GitHub Pages deployment creation failed (HTTP ${error.status || 'unknown'}).`);
  }
  const deploymentId = deployment.id || deployment.status_url?.split('/').pop();
  if (!deploymentId) throw new Error('GitHub did not return a Pages deployment ID.');

  const started = now();
  const finalFailures = new Set([
    'deployment_failed', 'deployment_content_failed', 'deployment_cancelled', 'deployment_lost'
  ]);
  let pending = true;
  let failures = 0;
  try {
    while (now() - started < timeout) {
      await sleep(10000);
      let result;
      try {
        ({ data: result } = await github.request('GET /repos/{owner}/{repo}/pages/deployments/{deployment_id}', {
          ...context.repo, deployment_id: deploymentId
        }));
        failures = 0;
      } catch (error) {
        // Retry transient status reads without exposing request/token details.
        if (++failures >= 5) throw new Error('Pages status could not be read after five attempts.');
        continue;
      }
      if (result.status === 'succeed') {
        pending = false;
        core.info('GitHub Pages deployment succeeded.');
        return;
      }
      if (finalFailures.has(result.status)) {
        pending = false;
        throw new Error(`GitHub Pages reported ${result.status}.`);
      }
    }
    throw new Error('GitHub Pages deployment exceeded ten minutes.');
  } finally {
    if (pending) {
      try {
        await github.request('POST /repos/{owner}/{repo}/pages/deployments/{deployment_id}/cancel', {
          ...context.repo, deployment_id: deploymentId
        });
      } catch {
        core.warning('Could not cancel the pending Pages deployment; inspect its status before retrying.');
      }
    }
  }
};
