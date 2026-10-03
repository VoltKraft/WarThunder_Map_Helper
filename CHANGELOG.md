# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning 2.0.0](https://semver.org/).
Both specifications were checked on their official websites for this update.

## [Unreleased]

## [0.3.0] - 2026-10-03

### Added

- Default-on player compass with thin rays to the viewport edge, cardinal labels,
  4–72 evenly spaced lines in steps of four, and optional intermediate bearings.
- Start-to-end compass bearings and direction markers for right-drag measurements.

### Fixed

- Synchronize Flatpak AppStream release metadata and screenshot references with
  version 0.3.0 so package validation can proceed.
- Reject mismatched AppStream metadata during the initial release metadata check,
  before platform builds start.

## [0.2.4] - 2026-09-28

### Fixed

- Exclude every aircraft from automatic tank camera framing, including nearby
  and squad aircraft, as well as aviation bases, while keeping aircraft markers
  visible. Use valid tank indicators and a live own ground marker for detection.
- Hide spawn markers and their hover/readout targets on ground battlefields,
  while retaining tank spawn positions as automatic camera anchors even when
  allied units or mission objects are excluded.
- Recognize the observed `#67D756` squad palette so squad tanks use green symbols
  and remain visible when ordinary allies are hidden.
- Keep valid player indicators available when the optional state endpoint fails.

## [0.2.1] - 2026-09-26

### Changed

- Update Avalonia to 12.1.3 and SkiaSharp, including Linux native assets, to
  4.152.1; refresh reviewed licenses and both offline Flatpak package feeds.
- Update Microsoft.NET.Test.Sdk, xUnit 2, and the Visual Studio test runner.
  Retain xUnit 3.2.2 for compatibility with Avalonia's headless test adapter.
- Validate Windows MSI payload bytes and launch the extracted installer payload
  on each native architecture before publication.

### Fixed

- Adapt SVG path construction, font glyph checks, screenshot encoding, and
  test assertions to the updated library APIs.
- Group related graphics dependency updates and defer incompatible xUnit major
  updates until the Avalonia test adapter supports them.
- Verify WinGet downloads against GitHub's per-asset SHA-256 digests.

### Removed

- Windows portable ZIPs, Linux tar.gz packages, and uploaded checksum text
  files. Releases now contain Windows MSI and Linux Flatpak installers for
  x64 and ARM64. GitHub's automatic source-code archive links remain available.

## [0.2.0] - 2026-09-26

### Added

- AGPL-3.0-only licensing, dependency attribution, contributor guidance,
  security reporting, issue forms, and documented architectural boundaries.
- Automated releases after successful `main` builds, using the tested commit
  and complete release assets without overwriting published versions.
- Windows x64 and ARM64 MSI installers and portable archives, plus Linux x64
  and ARM64 archives and independently installable Flatpak bundles.
- WinGet manifest preparation and optional submission automation, and documented
  prerequisites for a future human-authored Flathub submission.
- Automated checks for release metadata, packaging tools, and dependency
  updates, with architecture-specific package validation.

### Changed

- The application interface, messages, numeric formatting, comments, and
  repository documentation now use English. Existing settings and custom icon
  mappings keep their storage format and locations.
- The SVG renderer no longer depends on the MS-PL-licensed `Svg.Custom`
  component, allowing distribution under the project's AGPL-3.0-only license.
  Complex SVG features outside the supported icon subset are rejected with
  guidance to import a PNG. See the icon migration notes in `docs/USAGE.md`.
- Application and packaging builds share a pinned .NET SDK and project
  version. Shell files have defined line endings for Windows and Linux.

### Fixed

- Preserve supplemental license-file checksums when Git checks out the source
  on Windows, allowing MSI and portable packaging to complete.
- Retain the extracted NuGet packages between Flatpak build commands so the
  dependency license check can inspect the complete restored graph.

## [0.1.5] - 2026-09-24

This entry summarizes the locally validated baseline before public release
automation; it does not claim an earlier GitHub release or tag exists.

### Added

- Cross-platform tactical map with live HTTP telemetry, explicit demo mode,
  contact tracking, short-term position estimates, and fuel/range estimates.
- Target navigation, camera filters, squad colors, course vectors, hover
  information, right-drag distance measurement, and customizable icons.
- Application branding, a visible version, per-user Windows MSI installation,
  Start Menu and desktop shortcuts, and portable Windows/Linux packages.
- Logic, integration, and Avalonia interaction tests, with recorded API
  fixtures and documented live-game validation limits.

[Unreleased]: https://github.com/VoltKraft/WarThunder_Map_Helper/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/VoltKraft/WarThunder_Map_Helper/compare/v0.2.4...v0.3.0
[0.2.4]: https://github.com/VoltKraft/WarThunder_Map_Helper/compare/v0.2.1...v0.2.4
[0.2.1]: https://github.com/VoltKraft/WarThunder_Map_Helper/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/VoltKraft/WarThunder_Map_Helper/releases/tag/v0.2.0
