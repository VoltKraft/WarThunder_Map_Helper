# Reduced Avalonia build

## Decision and scope

The application remains AGPL-3.0-only. Its desktop build uses a maintained,
reduced Avalonia 12.1.4 variant instead of relying on a new upstream license
grant. This supersedes the release-hold decision of 2026-10-14. Release remains
conditional on validation of the actual packages on all supported platforms.

The upstream MIT package declaration does not describe every bundled source
file. Calendar, calendar date-picker, autocomplete, and several Fluent theme
templates carry Microsoft Public License notices. The source review also
identified the Silverlight-derived layout-transform implementation. The build
removes these implementations and their dependent resources; deleting license
headers or hiding unused controls is not sufficient.

The exclusion inventory is `packaging/avalonia-reduced/removals.json`.
Unused notification and progress templates that depend on the removed layout
transform are also excluded. Popup hosts use the standard `Decorator` with a
render transform. Ordinary untransformed application popups retain their
layout; transformed popup measurement is not a supported feature of this
variant. The application does not use transformed popup content.

Variant 12.1.4.2 pins assembly informational metadata to the upstream Avalonia
revision. Variant 12.1.4.1 inherited the enclosing application's Git revision,
so packages could differ across commits despite unchanged framework sources.
The bootstrap rebuilds first and removes only stale cached copies of the two
current private package versions before restore. The final-byte checks remain
mandatory. The historical application release 0.3.1 is not overwritten.
Do not run preparation concurrently with a restore that uses the same NuGet
cache. Unrelated packages and older private versions are not removed.

Only the `Avalonia` and `Avalonia.Themes.Fluent` packages are reconstructed.
Their local version `12.1.4.2` identifies this project's variant, not an official
Avalonia release. The root package retains reviewed upstream assemblies and
build tooling, while its Controls assembly and the Fluent theme are rebuilt
from the pinned source. Unsupported target frameworks and original copies of
the replaced binaries are not retained in the resulting packages. Assembly
identities remain compatible with the official 12.1.4 companion packages.

## Build and provenance

Run `python scripts/prepare_reduced_avalonia.py` before a normal solution
restore. The preparation command verifies pinned upstream inputs and builds
the local packages in `.build-cache/reduced-avalonia/feed`. Generated packages,
downloaded sources, compiler outputs, and caches are not committed. Python and
the SDK pinned by `global.json` are build prerequisites.

`NuGet.Config` maps the two package IDs exclusively to that local source.
The desktop project pins their exact versions. Other dependencies continue to
come from nuget.org. A missing preparation step must fail restore rather than
silently use the unmodified upstream packages.

The Flatpak builder performs the same compilation offline from hash-pinned
source and official NuGet inputs. No prebuilt custom package from a developer
machine or pull request is a publication input. See [Flatpak](FLATPAK.md) for
feed regeneration and sandbox details.

Upstream license and NOTICE texts are preserved unchanged, including notices
for removed components. They document the original inputs and are not a claim
that those components remain in the final application. The removal manifest
and generated package provenance record the changes separately.

## Maintenance and limitations

Avalonia updates require a fresh source-level review, regeneration of the
reduced packages and offline feed, UI interaction tests, and native packaging
checks. Do not advance the package pins solely because upstream NuGet metadata
declares MIT. Do not reuse `12.1.4.2` for a different upstream revision or a
changed released variant; allocate a new local package version and record the
new inputs instead.

The removed controls are not application features. Future work that requires
calendar, autocomplete, date/time theme resources, or the original layout
transform must first supply a reviewed compatible implementation. The reduced
packages are tailored to this application and are not offered as a general
replacement for Avalonia.

This process establishes which sources and binaries were included. It does
not replace reviewing the licenses of retained managed and native dependencies.
