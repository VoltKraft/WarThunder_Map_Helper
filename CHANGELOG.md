# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning 2.0.0](https://semver.org/).
Both specifications were checked on their official websites for this update.

## [Unreleased]

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

[Unreleased]: https://github.com/VoltKraft/WarThunder_Map_Helper/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/VoltKraft/WarThunder_Map_Helper/releases/tag/v0.2.0
