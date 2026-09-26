# Flatpak builds

The Linux bundles target native `x86_64` and `aarch64` and share application ID
`io.github.voltkraft.WarThunder_Map_Helper`. The manifest compiles the application
from source using the Freedesktop 26.08 Platform/SDK. The exact Microsoft .NET
10.0.401 SDK archives are build-only inputs, pinned by their official SHA-512
checksums. Neither the SDK nor a host .NET installation is needed at runtime.

## Regenerate the offline feed

Use Python 3.11+ and the SDK pinned in `global.json`. Run from the repository root:

```bash
python3 tools/generate-flatpak-nuget-sources.py --source-root . --runtime linux-x64 --output artifacts/flatpak/nuget-x64.json
python3 tools/generate-flatpak-nuget-sources.py --source-root . --runtime linux-arm64 --output artifacts/flatpak/nuget-arm64.json
python3 tools/merge-flatpak-nuget-sources.py --x64 artifacts/flatpak/nuget-x64.json --arm64 artifacts/flatpak/nuget-arm64.json --output packaging/flatpak/flathub/nuget-sources.json
```

On Windows use `python` in place of `python3`. Each generator restores into a
fresh temporary package/HTTP cache with roll-forward disabled and checks every
archive against NuGet's archive checksum. Source checkout build outputs are not
used. SDK packs belonging only to the restore host are excluded from the Linux
target feed. Shared cross-platform NuGet packages remain intact.
The SDK archive supplies its native apphost pack; the manifest checks this before
publishing. This avoids different source lists when the restore host already
has an apphost pack installed.

Generation needs network access to the official NuGet feed. The subsequent
Flatpak build downloads the listed inputs first and restores exclusively from
those local packages inside the build sandbox. NuGet's online vulnerability audit
is disabled only for this offline publish; normal CI restore keeps auditing on.
The dependency license gate also runs inside the Flatpak build.
The manifest sets `NUGET_PACKAGES` to
`/run/build/map-helper/.build-cache/nuget-packages`, inside the persistent module
directory. Each build command starts a separate sandbox, so the default home
cache would disappear before the license gate reads the restored package graph.
This build-only cache is excluded from local source copies and the exported app.

## Build and install locally

On a Linux machine of the architecture being tested, install Flatpak and
`flatpak-builder` using the distribution's package manager. Add Flathub for the
runtime dependencies, then build from this checkout. The base manifest's
AppStream screenshot URL must already be reachable at its version tag for this
local command. Before that first tag exists, use the commit-pinned candidate
produced by CI from the public source commit instead.

```bash
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak-builder --user --install-deps-from=flathub --force-clean --sandbox --default-branch=stable --repo=artifacts/flatpak-repo artifacts/flatpak-build packaging/flatpak/flathub/io.github.voltkraft.WarThunder_Map_Helper.yml
flatpak build-bundle artifacts/flatpak-repo artifacts/WarThunderMapHelper.flatpak io.github.voltkraft.WarThunder_Map_Helper stable
flatpak install --user artifacts/WarThunderMapHelper.flatpak
flatpak run io.github.voltkraft.WarThunder_Map_Helper --demo
```

The local `type: dir` source compiles this checkout. CI replaces it with the full
tested Git commit. Release preparation records both the stable tag and full
commit, never a floating branch. Both preparation commands produce four files:
the manifest, offline feed, architecture configuration, and AppStream metadata.
The generated metadata pins screenshot URLs to the same source commit because
AppStream reads screenshot bytes during composition, before the release tag
exists. Version, date, and license remain unchanged. The source metadata retains
its matching release-tag URL. These builds require a public source commit; they
do not promise a complete Flatpak before the repository has been published.
Build output and caches are excluded from the local source copy. Flatpak's
directory source uses literal relative exclusion paths, so add each new project
directory's `bin` and `obj` paths to the manifest and the source-pinning helper.

CI regenerates both feeds, compares their merged content with the committed feed,
and builds both architectures on native runners. It checks exported license
files and prevents the build-only SDK from entering the app. Release automation
downloads the bundles from that successful CI run; it does not rebuild them
against newer dependency bytes.

## Sandbox and data

| Permission | Reason |
| --- | --- |
| Network | Read the game's optional HTTP telemetry on `127.0.0.1:8111`; Flatpak cannot grant network access to one port only. |
| X11 and IPC | Avalonia's current Linux desktop backend; XWayland is needed on a Wayland desktop. |
| DRI | Graphics acceleration. |

There is no broad home filesystem, session bus, system bus, or host-command
permission. The profile is inside
`~/.var/app/io.github.voltkraft.WarThunder_Map_Helper/config/WarThunderMapHelper/`.
Custom icon files belong in its `icons/` directory or are imported through the
desktop file picker. The app copies imported content into its own profile; it
does not require continuing access to the original host path.

Install a replacement bundle with `flatpak install --user` to update a GitHub
installation. These bundles do not configure an application update remote.
Preserve the application data directory when changing distribution channels.

## Data migration

When switching from a native Linux archive, close both app instances and back
up the existing profile. Copy its contents from
`${XDG_CONFIG_HOME:-$HOME/.config}/WarThunderMapHelper/` (or the previous custom
`--data-dir`) into the Flatpak profile directory listed above. Back up an
existing destination before replacing files. Settings and relative icon paths
keep their format; reimport icons that referred to absolute paths outside the
sandbox. Start the Flatpak and verify settings and icons before removing the
old profile. No automatic migration or deletion is performed.

## Maintenance and validation boundaries

Update SDK pins from [Microsoft's official release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json),
keep `global.json` consistent, regenerate both feeds, update exact-version license
supplements when required, and rerun CI. Update the AppStream release version,
date, and screenshot URL with each release.

An offline source build is distinct from a real desktop/game test. Test launch,
demo mode, offline status, local telemetry, file-picker imports, settings
persistence, and restart on both target architectures. Native ARM64 and actual
game telemetry require suitable hardware and a running game; unit tests or an
x64 cross-publish do not establish those results.

For Flathub's human authorship and submission requirements, see
[distribution](DISTRIBUTION.md#flathub).
