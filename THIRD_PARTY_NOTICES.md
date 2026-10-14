# Third-party notices

Project-authored code is licensed under **AGPL-3.0-only**; see [LICENSE](LICENSE).
Third-party components retain their own copyright and license terms. This
overview does not replace the complete license texts included with packages.

## Application dependencies

Exact versions are declared in the project files and resolved by NuGet. The
release license inventory records exact input versions, archive hashes,
declared licenses, upstream sources, and the copied notice filenames.

| Component | License and authoritative source | Use |
| --- | --- | --- |
| .NET runtime | [MIT and bundled third-party notices](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | Self-contained application runtime |
| Reduced Avalonia 12.1.4 and official companion packages | [MIT declaration](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/licence.md) and [third-party notices](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/NOTICE.md); affected MS-PL implementations removed in our build | Desktop UI, windowing, headless tests |
| SkiaSharp and HarfBuzzSharp | [MIT and bundled native notices](https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt) | Graphics and text shaping |
| ANGLE, through Avalonia's Windows native package | [BSD-3-Clause](https://www.nuget.org/packages/Avalonia.Angle.Windows.Natives/2.1.27548.20260419/License) and included upstream notices | Windows graphics translation |
| Inter font | [SIL Open Font License 1.1](https://github.com/rsms/inter/blob/master/LICENSE.txt) | Embedded UI font; the Avalonia wrapper is MIT |
| MicroCom.Runtime | [MIT](https://github.com/AvaloniaUI/MicroCom/blob/master/LICENSE) | Native interop |
| Tmds.DBus.Protocol | [MIT](https://github.com/tmds/Tmds.DBus/blob/main/COPYING) | Linux desktop integration |

The application uses locally rebuilt Controls and Fluent assemblies, packaged
as `Avalonia` and `Avalonia.Themes.Fluent` 12.1.4.1. This is a project-specific
variant, not an official Avalonia release. The affected controls, templates,
and Silverlight-derived layout transform are excluded. Upstream NOTICE is
preserved verbatim even where it describes removed components. See
[Reduced Avalonia](docs/REDUCED_AVALONIA.md) for build provenance, limitations,
and the source-removal inventory.

The reduced package reuses official Avalonia.Base and other unaffected
assemblies. Retained source attributions include HandyControl RelativePanel
(MIT), historical SixLabors Fonts and LottieSharp code (Apache-2.0), Windows
Community Toolkit (MIT), and foliojs linebreak/unicode-trie (MIT via the
RichTextKit port). Their unmodified license texts are preserved alongside
Avalonia's complete upstream NOTICE. Color interpolation credits Romain Guy
and Francois Blavoet as recorded in the upstream source. These supplements
cover both retained Controls source and reused official Base binaries; they
do not imply that all Avalonia assemblies were rebuilt. See
`packaging/flatpak/licenses/sources.json` for the exact revisions and checksums.

The English SVG icon renderer adapts MIT-licensed code from
[SkiaSharp.Extended](https://github.com/mono/SkiaSharp.Extended). Its vendored
source includes the upstream license and revision-specific provenance. It
uses the existing SkiaSharp runtime, so it does not add a separate native
engine. The former `Svg.Skia` / `Svg.Custom` dependency is removed in 0.2.0;
`Svg.Custom`'s MS-PL license was incompatible with the chosen project license.

## Build, test, and automation tools

| Tool | License/source | Distribution boundary |
| --- | --- | --- |
| xUnit.net and its Visual Studio runner | [Apache-2.0](https://github.com/xunit/xunit/blob/main/LICENSE) | Test-only; not in application packages |
| Microsoft.NET.Test.Sdk | [MIT](https://github.com/microsoft/vstest/blob/main/LICENSE) | Test-only |
| WiX Toolset | [MS-RL and component licenses](https://github.com/wixtoolset/wix/blob/main/LICENSE.TXT) | Installer authoring tool, not linked into the application |
| Pillow | [HPND and bundled component licenses](https://github.com/python-pillow/Pillow/blob/main/LICENSE) | Optional branding export; not a runtime dependency |
| GitHub's Actions repositories | [MIT](https://github.com/actions/checkout/blob/main/LICENSE) with each action's own notices | CI runner tooling; pinned revisions in workflows |
| Flatpak GitHub Actions | [MIT](https://github.com/flatpak/flatpak-github-actions/blob/master/LICENSE) | CI runner tooling |

Release, offline NuGet-feed, license-inventory, and store-preparation tooling
is adapted from **VoltKraft/immich-folder-watch**, under
[AGPL-3.0-only](https://github.com/VoltKraft/immich-folder-watch/blob/main/LICENSE).
The reference checkout was at commit
`3d41ef5` when inspected. Attribution applies to adapted source and tests;
application names, metadata, paths, and package contracts are changed for this
project. Each upstream dependency's license remains independent of that
tooling attribution.

## Maintaining notices

Package builders include this file, the project `LICENSE`, and generated
third-party license texts. The inventory includes the inputs actually
examined; it is not a complete runtime software bill of materials. Preserve
native component notices in addition to the managed wrapper's license.

Before changing a dependency, inspect its official package metadata and the
upstream license at the relevant version, including transitive/native
components. Update the compatibility policy, supplemental notice catalog,
and offline Flatpak inputs together. Missing notice text is an error, not an
invitation to substitute an SPDX identifier for the license.

The project logo and bundled map icons are project assets. Their origin is
documented in `src/MapHelper.Desktop/Assets/branding/README.md`. War Thunder,
its game assets, and trademarks belong to their respective owners. Icons
optionally imported from the user's running game are not part of the source
repository or release payload.
