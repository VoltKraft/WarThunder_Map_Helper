# Distribution and repository setup

The release and distribution tooling is adapted from
[Immich Folder Watch](https://github.com/VoltKraft/immich-folder-watch).
The intended public repository is `VoltKraft/WarThunder_Map_Helper` and the
application is licensed under AGPL-3.0-only.

## Initial GitHub setup

For first publication, create an empty **public** GitHub repository named
`VoltKraft/WarThunder_Map_Helper`, without adding another README or license.
If the repository, initial commit, or `origin` already exists, inspect and reuse
it instead of recreating it. Preparing these files alone does not establish a
GitHub Release, hosted Actions result, WinGet listing, or Flathub listing.

Review the local diff and generated packaging inputs. For an empty local `main`
branch with no `origin`, run from the root:

```bash
git add .
git commit -m "Prepare English open-source release and distribution workflows"
git remote add origin https://github.com/VoltKraft/WarThunder_Map_Helper.git
git push -u origin main
```

Before enabling the initial push, configure these repository settings:

- Enable Actions and allow the SHA-pinned actions from `actions` and
  `flatpak/flatpak-github-actions`. Default workflow permissions can remain
  read-only; release and dispatch jobs request their own limited write scopes.
- After the first CI run establishes its check name, create a `main` ruleset
  requiring pull requests and the **CI passed** check. Allow release automation
  to create version tags under any tag rules. Do not require a nonexistent check
  before the first run has registered it.
- Enable dependency graph, Dependabot alerts/security updates, and private
  vulnerability reporting. Follow [SECURITY.md](../SECURITY.md) for reports.
- Review successful native x64/ARM64 CI results and the published asset set.
  Correct failed jobs before declaring a distribution channel available.

No personal access token is needed for publishing this repository's GitHub
Releases. The built-in `GITHUB_TOKEN` cannot write to Microsoft's community
repository, so WinGet has the separate opt-in settings below.

## GitHub Releases

Pushing a new version to `main` starts CI. Release automation accepts a successful
CI run for the exact source commit, reads `Directory.Build.props` and its matching
`CHANGELOG.md` entry, and publishes `vMAJOR.MINOR.PATCH` after all packages pass.
Keep the AppStream release metadata and offline feed current in the same change.
A push that keeps an already published version does not overwrite that release.

For each `x64` and `arm64` architecture, the release contains:

- `WarThunderMapHelper-<version>-win-<arch>.msi`
- `WarThunderMapHelper-<version>-linux-<arch>.flatpak`

A draft release is published only after all four installers are present and
their SHA-256 hashes match GitHub asset digests. No separate checksum text file
or portable archive is uploaded. GitHub still adds its automatic **Source code**
ZIP and tar.gz links; these contain source code, not application packages. Release notes come from the matching
changelog entry. Packages include the project license, third-party notices, and
the collected dependency license texts. Windows MSI files are unsigned until a
separately managed signing identity and signing workflow are configured.

Both Windows installers are per-user. x64 keeps the existing install location
and upgrade identity and upgrades the earlier German-language MSI. ARM64 uses a
separate install folder and product identity. Both architectures use the same
default profile; avoid running them concurrently against that profile, or provide
separate `--data-dir` paths. Uninstalling does not delete user profile data.

The release workflow explicitly dispatches the Flatpak Pages repository, WinGet,
and Flathub preparation after publication. This is necessary because releases created by `GITHUB_TOKEN` do not
start other `release` workflows. Store preparation/submission failures do not
delete the already published GitHub release. Rerun the affected workflow with the
same `release_tag` after fixing its cause.

## GitHub Pages Flatpak repository

The application update repository is served at
[`https://voltkraft.github.io/WarThunder_Map_Helper/`](https://voltkraft.github.io/WarThunder_Map_Helper/).
It provides a signed OSTree repository under `repo/`, an architecture-neutral
`io.github.voltkraft.WarThunder_Map_Helper.flatpakref`, and
`war-thunder-map-helper.flatpakrepo`. User installation and bundle migration
commands live in [FLATPAK.md](FLATPAK.md#install-from-the-update-repository).

The **Flatpak repository on GitHub Pages** workflow runs on `main` through an explicit
release dispatch or a manual dispatch. An empty `release_tag` selects the newest
public stable semantic version. An explicit tag must be that version; a retry
of an older release fails instead of rolling users back. Both architectures
must be present. The helper verifies the complete four-installer release set,
the immutable tag/source identity, each downloaded Flatpak's size and GitHub
SHA-256 digest, and the embedded AppStream ID/version before publication.
It imports the already tested release commits without rebuilding the app.

The repository contains the two newest stable releases' objects, with `stable`
pointing at the newest for both architectures. Retaining the preceding release
helps downloads crossing a deployment; it is not an indefinite rollback archive.
Static deltas reduce HTTP request counts. Each deployment is rebuilt from public
release assets, so artifact/cache expiry cannot lose the repository. A missing
or invalid preceding release also fails the deployment; repair the release
source rather than silently bypassing verification. GitHub Releases remain
unchanged. No repository binaries or private keys are committed to Git.

### One-time setup and signing identity

Enable **Settings → Pages → Source: GitHub Actions**. With repository admin
access, the equivalent first-time command is:

```bash
gh api --method POST repos/VoltKraft/WarThunder_Map_Helper/pages -f build_type=workflow
```

Reuse an existing Pages site instead of recreating it; this workflow publishes
the whole site. Review the `github-pages` environment's deployment restrictions
and allow `main`. The build job needs `contents: read` and `pages: read`; the
deployment job needs `pages: write` and `id-token: write`. No deployment PAT is
required. Existing SHA-pinned upload-artifact and github-script actions implement
the Pages artifact/API protocol without another action dependency.

The checked-in public key is
[`repository-signing-key.asc`](../packaging/flatpak/repository-signing-key.asc),
with fingerprint `9400819F0FA21004F261A4C8867E33A7207D8F25`.
Store its matching ASCII-armored private key in the repository Actions secret
`FLATPAK_GPG_PRIVATE_KEY`. The key is dedicated to this repository, has no
passphrase for unattended signing, and must have a protected offline backup.
To configure the secret from a secure existing GnuPG home:

```bash
gpg --homedir /secure/flatpak-signing --armor --export-secret-keys 9400819F0FA21004F261A4C8867E33A7207D8F25 | gh secret set FLATPAK_GPG_PRIVATE_KEY --repo VoltKraft/WarThunder_Map_Helper
```

Never print or commit the private export. The workflow imports it into an
ephemeral GnuPG home outside the public artifact and removes it after use.
The helper requires the secret to match the committed public fingerprint;
both commits and repository metadata are signed. The descriptors embed that
same public key, preserving signature verification on installation and update.
Do not regenerate the identity on each run. Key replacement needs an explicit
trust migration for installed clients before old signing material is retired;
replacing only the website descriptor does not update existing clients' trust.

### Manual publication and diagnosis

After the signing secret and Pages source are configured, publish the existing
latest release without altering it:

```bash
gh workflow run flatpak-pages.yml --ref main -f release_tag=v0.3.2
gh run list --workflow flatpak-pages.yml --limit 5
```

For later releases omit `release_tag` to select the newest stable version.
The workflow serializes build and deployment, checks for a newer release again
before deployment, and fails on a missing secret or invalid signature source.
Failed builds leave the last successful Pages deployment available. A successful
deployment must also be checked through the public URL and a Flatpak client.

For local preparation, use Python 3.11+, `flatpak`, `ostree`, GnuPG, network access,
`GH_TOKEN` with repository read access, and `GNUPGHOME` pointing at the isolated
signing keyring. From the repository root:

```bash
python3 scripts/flatpak_pages.py --repository VoltKraft/WarThunder_Map_Helper --base-url https://voltkraft.github.io/WarThunder_Map_Helper --public-key packaging/flatpak/repository-signing-key.asc --output-dir artifacts/flatpak-pages
```

The output directory must be new or empty; `GNUPGHOME` must stay outside it. The helper
writes the public site, descriptors, and `release.json` containing release commit
IDs and the public fingerprint; it prints a JSON summary including total bytes.
It does not deploy locally. Downloads have a 180-second I/O timeout and a
256 MiB per-bundle cap; packaging commands have a 10-minute timeout. Failures
leave diagnostic partial output that must not be published. Choose a new output
directory when retrying.

The generated site is rejected above 950 MB, below GitHub Pages' 1 GB published
site limit. Monitor bandwidth as usage grows; Pages has a soft 100 GB monthly
limit. These constraints can require another static host in future. See the
official [Pages limits](https://docs.github.com/en/pages/getting-started-with-github-pages/github-pages-limits)
and [Flatpak repository hosting](https://docs.flatpak.org/en/latest/hosting-a-repository.html).

## Local release builds and retries

Run the [development checks](DEVELOPMENT.md) first. Native Windows packaging
uses the version in `Directory.Build.props`:

```powershell
./packaging/windows/build.ps1 -Runtime win-x64
./packaging/windows/build.ps1 -Runtime win-arm64
```

Use `-Python <executable>` when Python is not on PATH. `-SkipTests` is intended
for CI or a build immediately following successful relevant tests. The x64 host
can cross-publish ARM64 packages, but actual ARM64 launch is validated on the
native Windows ARM runner.

Validate a Windows MSI on a host with the same architecture:

```powershell
./scripts/verify-windows-package.ps1 -Runtime win-x64
```

This administratively extracts the MSI without installing it, compares every
extracted application file with the publish output, then starts it in demo mode
with a separate profile. Logs, screenshots, and validation reports remain under
`artifacts/`. Extracted files remain in a unique system temporary directory,
recorded in the report, to avoid Windows Installer path-length limits. MSI
extraction and application startup each time out after two minutes. Cross-built
ARM64 installers require a native ARM64 host for this step.

On Linux, prepare and smoke-test a native validation payload:

```bash
bash packaging/linux/build.sh --runtime linux-x64
xvfb-run -a bash scripts/verify-linux-package.sh
```

Use `linux-arm64` on an ARM64 host. The build writes an unpacked validation
payload under `artifacts/publish/`; it does not create a release archive.
`python scripts/verify-publish.py --runtime linux-x64` checks its architecture
and required license files. User-facing Linux releases use the Flatpak build
commands in [FLATPAK.md](FLATPAK.md).

Existing Windows archive users can install the MSI and retain the default
profile location. If a custom `--data-dir` was used, continue to supply that
path. Linux archive users moving to Flatpak should copy their profile as
described in [Flatpak data migration](FLATPAK.md#data-migration).

A manual **Release** run requires `ci_run_id`, the ID of a successful CI run
triggered by a `main` push in this repository. The artifacts must still exist
(the retention period is 14 days). An unfinished draft for the same SHA can be
resumed; conflicting drafts or tags fail instead of being overwritten. Versions
older than the latest published version are skipped. Changes after publication
need a new version, changelog entry, and matching AppStream metadata; version
numbers are deliberately not incremented automatically.

## WinGet

Package identity: `VoltKraft.WarThunderMapHelper`. The **WinGet** workflow always
prepares reviewable manifests from a published stable release. It requires the
complete four-installer release, checks downloaded MSI SHA-256 against the
GitHub asset digests,
and reads ProductCode, version, architecture, publisher, language, product name,
and install scope from each actual MSI. Both native architectures are required.

The generated three-file manifest is uploaded as `winget-v<version>`. To prepare
locally on Windows with Python 3.11+ and network access:

```powershell
./tools/prepare-winget-release.ps1 -ReleaseTag v0.2.1 -OutputRoot artifacts/winget-review
winget validate --manifest artifacts/winget-review/manifests
```

The release must already exist; output must be empty or new. `GH_TOKEN` is
optional for local GitHub API authentication and is never sent with public asset
downloads. Do not execute this command against an unpublished version expecting
placeholder manifests.

To enable automated submission after release preparation succeeds, configure:

| Repository setting | Value/purpose |
| --- | --- |
| Secret `WINGETCREATE_TOKEN` | Maintainer GitHub token that can create public PRs through the maintainer's `winget-pkgs` fork; the official tool documents the supported token scopes. |
| Variable `WINGET_AUTOMATION_ENABLED` | `true` |

The existing `VoltKraft/winget-pkgs` fork can be reused. Tokens from another
repository cannot be read or copied by this automation. Run **WinGet** manually
with `release_tag=v0.2.1` and `submit=true` to submit a prepared first release, or
let the release dispatch do so after enabling the settings. `submit=false` only
prepares the artifact. The official Microsoft manifest creator is pinned by
version and SHA-256; the token is passed through its supported environment
variable rather than a command-line argument.

Matching existing submissions or already published versions are reused/skipped.
A closed rejected PR requires maintainer attention. Microsoft still validates
and reviews the submission, and the maintainer may need to complete its first
contributor agreement. A PR is not a published WinGet package. Announce
`winget install --id VoltKraft.WarThunderMapHelper --exact` only after it works
against the community source.

Official references: [manifest requirements](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest),
[manifest creator](https://github.com/microsoft/winget-create), and
[submission process](https://learn.microsoft.com/en-us/windows/package-manager/package/repository).

## Flathub

The **Flathub preparation** workflow produces immutable, checksummed technical
build inputs from a published stable release. Its artifact is named
`flatpak-release-inputs-v<version>`. It has read-only permissions and does not
create PRs, configure tokens, or publish an app. The shared manifest is fully
usable for GitHub Flatpak bundles; it is **not eligible for Flathub submission**.

On 2026-09-26 the official [generative AI policy](https://docs.flathub.org/docs/for-app-authors/requirements#generative-ai-policy)
prohibits AI-generated or AI-assisted Flathub manifests even with disclosure.
It also prohibits agents from opening/automating submission PRs or producing the
submission and review interactions. This project's prepared manifest and
automation include AI assistance. A human maintainer must independently author
compliant Flathub packaging and personally complete the submission and review.
Do not submit or relabel the generated artifact as human-authored packaging.

Before a future human submission:

- Recheck the current [requirements](https://docs.flathub.org/docs/for-app-authors/requirements)
  and [submission procedure](https://docs.flathub.org/docs/for-app-authors/submission).
  Disclose the affected AI-assisted application/documentation portions and their
  approximate extent in the maintainer's own words.
- Establish the public source repository and tagged release history. Flathub
  requires meaningful development history and ongoing maintenance; a new project
  is not automatically eligible.
- Resolve the app name and artwork requirements with Flathub. The current name
  contains a third-party game trademark; an unofficial-app disclaimer alone
  does not establish store approval.
- Validate native offline builds, minimal permissions, live desktop behavior,
  English screenshots, AppStream metadata, and all dependency licenses. Use the
  exact published source and current supported runtime.
- Perform initial submission through the documented human process, then accept
  the per-app repository invitation and complete ownership verification.

`VoltKraft/flathub` is only a fork of the submission repository; its existence
does not grant an accepted app repository or store publication. Future automatic
maintenance updates must be designed against the then-current Flathub policy
and the human-authored accepted packaging. No Flathub submission token or
automatic PR publisher is configured in this project.

Build commands and sandbox/data behavior have one source of truth in
[FLATPAK.md](FLATPAK.md).
