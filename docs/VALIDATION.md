# Validation history

This document distinguishes automated and synthetic checks from live game
observations. Commands are described in [Development](DEVELOPMENT.md) and
[Distribution](DISTRIBUTION.md). Local checks do not establish successful
GitHub Actions execution or publication to a package repository.

## Version 0.2.0: local checks on 2026-09-26

- The pinned .NET 10.0.401 SDK restored and built the solution in Release mode
  with zero warnings or errors. All **194 application tests passed**: 129
  logic/integration tests and 65 Avalonia/rendering/import tests. Whitespace
  formatting verification passed.
- **34 release/license/Windows packaging tests passed**. Distribution tooling
  ran **90 tests**, with 87 passing and three symlink fixtures skipped on
  Windows. Actionlint 1.7.12 accepted all five workflows; external ShellCheck
  and Pyflakes integration was disabled. PowerShell 5.1 and Bash syntax checks
  also passed.
- SVG regression coverage includes bundled icon colors, viewBox alignment,
  root and nested transformation order, zero-size shapes, inherited paint,
  opacity, rejected external/active content, bounded input, portal streams,
  PNG preservation, and fallback diagnostics. An independent review reproduced
  and verified fixes for root transforms and zero-size stroked shapes.
- Windows x64 and ARM64 MSI/ZIP packages and Linux x64 and ARM64 archives were
  built. Every archived file matched its publish directory. PE/ELF checks
  confirmed the selected architecture of the application host, .NET runtime,
  Skia, and HarfBuzz. Both MSI databases passed payload, version, language,
  per-user scope, upgrade identity, shortcut, and embedded-icon checks.
- The extracted **Windows x64 ZIP** and **Linux x64 archive** both started in
  explicit Demo mode with fresh test profiles, exited successfully after
  capture, and produced no application error log. Screenshots were visually
  reviewed for English text, version 0.2.0, icons, controls, and explicit
  synthetic-data labels. Linux used Ubuntu 22.04 under WSLg/XWayland; this does
  not establish native Wayland support.
- The dependency license gate passed for all four native publishes. Full
  dependency notices and the adapted SVG renderer's MIT notice were present;
  `Svg.Skia`, `Svg.Custom`, and their former rendering shim were absent.
- The Linux x64/ARM64 offline NuGet feeds generated with the pinned Windows
  and Linux SDKs matched byte-for-byte after excluding apphost packs supplied
  by each native SDK. The merged feed contains 30 verified archives.
- A native Linux x64 publish with the pinned SDK succeeded from fresh caches
  inside an isolated network namespace (`unshare -Urn`), using only the local
  feed. Its dependency license gate passed for 28 resolved packages. This
  verifies the offline .NET build inputs, not the complete Flatpak sandbox.
  The notice collector also verified and collected license texts for all 30
  archives in the merged feed. Candidate metadata pins screenshot URLs to the
  public source commit so AppStream does not need the future release tag.
- The actual `winget validate` command accepted all three generated WinGet
  manifests using the local MSI ProductCodes and SHA-256 hashes. Their URLs
  describe a future release; this was a local schema check, without uploading
  packages, submitting a pull request, or confirming download availability.

Local evidence is under `artifacts/tests/final-0.2.0/`,
`artifacts/archive-verification.json`, the two
`artifacts/msi-verification-0.2.0-win-*.json` reports, and
`artifacts/{windows,linux}-smoke-0.2.0.json`. Screenshots are
`artifacts/screenshots/{windows,linux}-package-0.2.0.png`.

The following remain outside this local validation: native ARM64 execution,
MSI installation/uninstallation or an actual upgrade, a complete Flatpak
sandbox build and installed-app test, hosted GitHub Actions, WinGet/Flathub
acceptance, and new live-game measurements. Native runners and Flatpak builds
are configured in CI, but have not been claimed as executed here. See
[Distribution](DISTRIBUTION.md) for repository creation, credentials, and the
Flathub submission boundary.

## Historical records through 0.1.5

The remaining sections preserve the original validation record through
version 0.1.5, dated 2026-09-24. They do not substitute for current checks.

Paths below refer to local artifacts from the original validation session.
They are not guaranteed to exist in a fresh checkout or a release archive.
Checked-in regression fixtures remain under `tests/MapHelper.Tests/Fixtures`.

## Historical scope

Version 0.1.3 added camera filters, squad colors and course lines, API target
markers, optional ally visibility, right-button distance measurements, hover
information cards, and icon images in dropdowns. In addition to an earlier
`f_16xl` test flight, responses from a live `su_30sm2` battle were inspected.

Version 0.1.4 displayed distance and time by default on the API-marked enemy.
**Marked target** settings took priority over course-target and category
settings, with independent switches for the two values. Older profiles
received the new defaults while preserving existing choices. These changes
were checked with automated and synthetic tests; API target-association
limitations remained.

Version 0.1.5 enabled all five map options by default, added application
branding to the interface, window, executable, and shortcuts, and displayed
the version. The MSI also added a desktop shortcut. Explicitly saved settings
were preserved.

## Automated coverage recorded for 0.1.5

**133 tests passed: 127 logic/integration tests and six Avalonia UI tests**,
with no failures or skipped tests under Windows and .NET 10. Coverage included
rectangular-map coordinates, two-second predictions, contact re-identification,
ambiguous crossings, repeated position samples, destruction-marker duration,
connection loss, session changes, consumption and range, missing readings,
rounded fuel gauges, refueling, and independent HTTP requests. A late API
failure from an earlier map session was discarded.

Zoom tests covered maximum fitting zoom, contacts clustered in map corners,
immediate zoom-out, gradual zoom-in, disappearing contacts, window sizes, and
range circles beyond the map image. Recorded API responses guarded contact
colors and the distinction between unclassified weapon readings and
ammunition counters.

Target tests covered forward course-line selection, ties, map-edge limits,
direct distance and time based on own speed, stationary units, missing or
invalid speed, stale and destroyed contacts, separate category switches,
base/ship classification, airfield midpoints, and saving/loading target
options. Recording and replay had been removed, including the CLI entry
point and replay data source.

Additional 0.1.3 coverage included AI/base filters without invented
classification, the last actual enemy position after the prediction expired,
session/disconnection resets, squad colors, hidden allies while preserving
squad/own-player visibility, independent course lines, priority for one API
target marker, and geometric fallback for zero or multiple markers.
New recorded samples covered `#39D921`, `#134AFF`,
`MediumTankTarget`, and ship variants.

UI tests sent real Avalonia pointer events. Right-button dragging updated the
distance, release retained it, zoom preserved its world distance, and a right
click cleared it. Automatic framing remained enabled after measurement, and a
new session cleared the line. Further checks opened the icon dropdown and
verified each image source, detected contacts on hover with target labels
disabled, and exercised settings persistence and older-profile defaults.

Additional 0.1.4 coverage included marked air/ground/naval/base targets behind
the player, marked-setting precedence, avoiding duplicate labels for a marked
course target, target changes, stale markers, missing direction/speed, and
disconnection. A UI test loaded an older profile, checked both new defaults,
used **All off**, saved only marked-target time, and reloaded it.

Additional 0.1.5 coverage included enabled squad/target course lines and all
five map options when an older profile omitted those fields. Explicitly
disabling, saving, and reloading all five options retained the choices.

Historical result files:

- `artifacts/tests/tests-0.1.5_net10.0_20260924232217.trx`
- `artifacts/tests/tests-0.1.5_net10.0_20260924232231.trx`

## Live comparison recorded for 0.1.3

- The battle snapshot contained 374 map objects, the own `su_30sm2`, a green
  aircraft contact with `#39D921`, blue variants `#174DFF`/`#134AFF`,
  and red enemies with `#fa0C00`. Green contacts were displayed as squad;
  comparison with the actual squad roster remained open.
- An enemy tank had `icon_bg: MediumTankTarget`. The background marker was
  displayed and, with exactly one current marked enemy, preferred for its
  course line. A synchronized in-game target change and the interpretation as
  personal selection were not confirmed. No radar lock was inferred.
- Objects contained ordinary position/display fields, without a general
  AI/player flag. `Wheeled` was classified as AI, while generic aircraft
  and tank icons remained unknown. The camera's AI filter therefore could
  not exclude every actual computer-controlled unit.
- During the Windows live run, own-player visibility changed. The application
  continued showing supplied contacts and the appropriate status without the
  own-player icon. This did not validate range estimates for `su_30sm2`.
- Raw responses were stored in `artifacts/api-surface-0.1.3.json`.
  Name-free regression examples are in
  `tests/MapHelper.Tests/Fixtures/battle-su30sm2-map_obj.json`.
  Historical screenshots were `artifacts/screenshots/live-0.1.3.png` and
  `live-vectors-0.1.3.png` in the same directory.

## Earlier live observations recorded for 0.1.1

- The original map image and contacts loaded from the running game. Enemy
  aircraft using `#f00C00` appeared red after that palette value was added.
- The map used the full window width. Recording and the side information
  panels had been removed.
- Automatic framing included all contacts with a margin. The own course line
  and the range circle derived from fuel decrease and movement were visible.
- The circle could exceed the map image. **Range circle** showed the entire
  radius beyond the map boundary.
- For `f_16xl`, `/indicators` supplied `weapon2 = 0` and
  `weapon4 = 0`. The inspected responses contained no `ammo_counter*`,
  weapon names, missile models, or identifiable loadout. Zero values were not
  interpreted as an absence of weapons.
- A subsequent projectile investigation found no usable flying-weapon data.
  The user confirmed firing/releasing a weapon, but its exact time was not
  synchronized with the measurements. See [Projectile API](PROJECTILE_API.md).
- Historical screenshots were `artifacts/screenshots/live-0.1.1.png` and
  `live-range-0.1.1.png`. Recorded responses are in
  `tests/MapHelper.Tests/Fixtures/testflight-f16xl-*.json`.

## Application and package checks recorded for 0.1.5 and earlier

- **Windows 0.1.5:** the native application started with a fresh profile and
  received a visual check. The logo, **Version 0.1.5**, both course lines,
  and all five enabled map options were visible. Screenshots were
  `artifacts/screenshots/branding-0.1.5.png` and
  `defaults-0.1.5.png`.
- **Linux 0.1.5:** the self-contained archive was extracted under Ubuntu 22.04
  in WSL, started with X11/XWayland, and rendered. The logo, version, and
  enabled course lines were visible. The screenshot was
  `artifacts/screenshots/linux-package-0.1.5.png`. A native Wayland backend
  and a separate physical Linux installation were not tested.
- The **0.1.5 MSI build** inspected the completed MSI database for desktop and
  Start Menu shortcuts, the correct executable path, working directory,
  embedded icon, shortcut feature membership, and matching executable/MSI
  versions. The record was `artifacts/msi-verification-0.1.5.json`.
  This inspection left the existing 0.1.4 installation unchanged and did not
  establish a successful 0.1.5 installation/uninstallation.
- The **0.1.4 target label and settings row** were visually inspected on
  Windows. Screenshots were `artifacts/screenshots/marked-target-0.1.4.png`
  and `target-settings-0.1.4.png`. Windows and Linux packages used the same
  calculation logic.
- **Windows 0.1.3:** the release build completed without warnings or errors.
  Measurement, hover data, green squad styling, course lines, hidden allies,
  and icon dropdown previews were visually inspected. Screenshots in
  `artifacts/screenshots` were `interactions-0.1.3.png`,
  `squad-vectors-0.1.3.png`, `symbol-dropdown-0.1.3.png`, and
  `map-options-0.1.3.png`. Synthetic target backgrounds appeared during
  seconds 14–18 of the Demo cycle.
- **Linux 0.1.3:** the same application from the self-contained
  `linux-x64` archive started and rendered under Ubuntu 22.04 in WSL with
  X11/XWayland. The screenshot was
  `artifacts/screenshots/linux-package-0.1.3.png`. Neither a native
  Wayland backend nor a separate physical Linux installation was tested.
- **MSI lifecycle:** WiX validation ran during package builds. Installation,
  installed-executable startup, and uninstallation passed for 0.1.0, preserving
  user files and custom icons. Version 0.1.5 added a desktop shortcut to that
  same per-user installation design.
- The MSI and portable ZIP came from the same Windows publish directory and
  included the runtime. The packages were not digitally signed.
- WiX hardlinks were disabled. ICE91 was specifically suppressed because the
  installer targeted only the current user's application directory; other
  installer checks remained enabled.
- The then-existing GitHub Actions workflow covered Windows and Linux tests
  and a Linux Xvfb startup check. It had not been run on a GitHub runner in the
  recorded session. Automated tests ran locally under Windows; the additional
  Linux package startup ran under WSL.

The synthetic Demo supports repeatable rendering checks without recording or
replay. It does not confirm data availability in every game mode.

## Remaining live and platform validation

- Compare target selection and distance/time labels with the game map during
  flight, including nearby enemies and own-course changes.
- Compare course lines and API target backgrounds with deliberate in-game
  target changes; compare squad colors with the actual member roster.
- Compare a full battle with the built-in browser map. Snapshots establish
  available fields and rendering, not correctness throughout an entire match.
- Observe AI versus human units and contacts appearing/disappearing; document
  each mode's limitations.
- Check fuel mass across aircraft, afterburner changes, landing/refueling, and
  respawns.
- The browser page exposed `icons.ttf`; complete import of its icon mappings
  remained unverified. Inspect weapon/ammunition fields for more aircraft.
- Confirm a kill at a location only when the API provides a reliable object
  association.
- Check multiple physical monitors and their scaling settings.

The application does not start War Thunder or access game memory.
