# Development

## Requirements

- Install the exact .NET SDK listed in `global.json` (10.0.401 for this release).
  Builds deliberately fail rather than silently selecting a different SDK.
- Windows x64/ARM64 or Linux x64/ARM64 for the application. MSI authoring and
  verification require Windows and Windows PowerShell 5.1 or newer.
- Python 3.11 or newer for packaging and automation tests. No Python package is
  needed for the application itself. The optional branding exporter requires
  [Pillow](https://python-pillow.github.io/) and is not part of a normal build.
- Git for source checkouts and release metadata, and Bash for native Linux
  validation builds. Flatpak prerequisites are
  listed in [Distribution](DISTRIBUTION.md).

The solution contains application/test projects; Windows-only WiX authoring
is invoked separately. `NuGet.Config` uses the public NuGet feed. SDK and
dependency downloads need network access on the first build.

## Common commands

Run these commands from the repository root. On systems that expose Python
as `python3`, use that name in place of `python`.

```sh
dotnet restore WarThunderMapHelper.slnx
dotnet build WarThunderMapHelper.slnx -c Release --no-restore
dotnet test WarThunderMapHelper.slnx -c Release --no-build
python scripts/check_dependency_licenses.py
dotnet run --project src/MapHelper.Desktop -- --demo
python -m unittest discover -s scripts/tests -p 'test_*.py'
python -m unittest discover -s tools/tests -p 'test_*.py'
```

`--demo` explicitly selects synthetic data. Starting without it waits for the
game API. For a focused regression run, supply a VSTest filter, for example:

```sh
dotnet test tests/MapHelper.Tests -c Release --filter FullyQualifiedName~RangeTests
```

The C# compiler, nullable checks, and .NET analyzers run during build, with
warnings treated as errors. Restore runs NuGet security auditing for direct
and transitive packages. A failed feed request is not a clean audit result.
There is no separate JavaScript, frontend, or type-checking toolchain.

The offline license check inspects every resolved desktop package, including
transitive dependencies. Unreviewed SPDX expressions and changed license files
fail the check. An exact-version review in
`scripts/dependency-license-policy.json` records the authoritative source and
hash for a package with a file-based license. The gate complements the notice
collector; it does not replace reviewing native libraries or new license
terms. Add a new policy entry only after that review.

Check or apply C# whitespace formatting:

```sh
dotnet format whitespace WarThunderMapHelper.slnx --verify-no-changes --no-restore
dotnet format whitespace WarThunderMapHelper.slnx --no-restore
```

## Dependency updates

Dependabot groups Avalonia and SkiaSharp updates because the managed rendering
API and native libraries must be tested together. The direct Linux native Skia
package must match the direct SkiaSharp version. Regenerate both architecture
feeds and review the supplemental license catalog after runtime updates; see
[Flatpak inputs](FLATPAK.md).

The UI tests retain `xunit.v3` 3.2.2. Avalonia.Headless.XUnit 12.1.3 calls the
3.x discovery API, which was removed in xUnit 4 and produces a
`MissingMethodException` during discovery. Dependabot excludes major updates
for this package until the Avalonia adapter supports that API. Recheck this
restriction when updating the adapter; all 65 UI tests must be discovered and
pass before removing it. Both test projects continue to use VSTest and the
commands above.

## UI validation

Headless Avalonia tests exercise interaction logic without a physical display.
For an actual rendered smoke test, use a desktop session or Linux Xvfb:

```sh
dotnet run --project src/MapHelper.Desktop -c Release -- --demo --smoke-test --screenshot=artifacts/screenshots/demo.png --data-dir=artifacts/smoke-data
```

The application exits after the capture. Confirm a successful exit, nonempty
image, and correct rendered controls. Command-line screenshot options are
documented in [Usage](USAGE.md). Synthetic rendering tests do not establish
that a particular game mode supplies a field; record live evidence separately
in [Validation](VALIDATION.md).

## Build environment

Normal builds use the usual SDK/NuGet caches. For a restricted Windows
workspace, these optional settings keep writable caches inside the checkout:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.build-cache/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.build-cache/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore WarThunderMapHelper.slnx
```

They change build-tool caches, not the application's data directory. Do not
commit their contents. `AvaloniaTelemetryEnabled=false` is set in the shared
build properties. WiX output hardlinks remain disabled because Nextcloud
Cloud Files cannot synchronize them reliably.

## Packaging and contribution checks

Use the commands in [Distribution](DISTRIBUTION.md) for architecture selection,
offline Flatpak inputs, checksums, installer validation, and release retries.
The version is set only in `Directory.Build.props`; release helpers verify its
changelog entry and other packaging metadata.

Before proposing a change, run the smallest relevant test, broaden validation
when the affected boundary requires it, and review the final diff. A Windows
cross-publish can verify ARM64 file architecture but cannot establish native
ARM64 startup. CI uses native runners for that distinction.
