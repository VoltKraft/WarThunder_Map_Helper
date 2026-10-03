# User guide

## Connect to the game

Start the application, then start War Thunder normally and enter a test flight
or battle. The default endpoint is `http://127.0.0.1:8111/`. Under **Connection**,
you can enter another HTTP or HTTPS address, such as the address of the game
computer on your local network. Credentials, query parameters, and URL
fragments are not accepted. The game computer's browser map must be reachable.

The endpoint is optional at application startup. Without game data, the
application waits; it never switches to sample data automatically. Select
**Demo** explicitly to see a synthetic scenario.

The version is shown below the application name and in the window title. It
comes from the same build version used by the installer.

All application labels and generated messages are in English, with English
numeric formatting. Raw API values, player names, icon names, vehicle
identifiers, and game-provided HUD messages retain their original content.

## Navigate and measure

| Control | Behavior |
| --- | --- |
| Mouse wheel | Zoom around the pointer. |
| Left-button drag | Pan the map. |
| Right-button drag | Draw a straight measurement with live distance and start-to-end compass bearing. |
| Release the right button | Keep the measurement anchored to its map positions while panning and zooming. |
| Right click | Clear the measurement. A new right-button drag replaces it. |
| Hover over a unit | Open its information card, even when its target label is disabled. |
| **Full map** | Reset zoom and view to the whole map. |
| **Follow player** | Keep your own unit in view. |
| **All units** | Automatically frame contacts according to the camera filters. |
| **Range circle** | Fit the complete fuel-range circle when an estimate is available. |

Automatic camera movement pauses while measuring. A new session clears the
measurement. An information card can be entered with the pointer and scrolled;
**All supplied API values** expands the raw data. Your fuel, range, altitude,
consumption, weapon readings, and recent HUD events appear on your own unit.

**All units** is enabled by default. It includes displayed estimated contacts
and mission objects and leaves a small margin around their icons. Distant
contacts trigger immediate zoom-out; the camera zooms in gradually as contacts
approach. Manual panning or zooming ends automatic framing.

## Map options

Display and camera toggles default to enabled, including the compass. The compass
starts with four lines and intermediate degree labels disabled. Missing fields in
older profiles use these defaults; explicitly saved choices are preserved.

| Option | Effect |
| --- | --- |
| **Show allies (excluding squad)** | Show ordinary allies. Your unit and squad remain visible when this is off; hidden allies do not influence automatic framing. |
| **Squad member course lines** | Draw each squad member's movement direction to the map edge. |
| **Marked / nearest target course line** | Prefer one uniquely API-marked enemy; otherwise use the enemy nearest your course line. |
| **Show compass** | Draw thin north-oriented rays from your current player marker to the visible map viewport edge, with N/E/S/W labels. |
| **Compass lines** | Choose 4–72 evenly spaced rays, in steps of four; the four cardinal directions are always included. |
| **Show degrees on additional lines** | Label intermediate rays with their compass bearings; cardinal labels stay visible independently. |
| **Include AI units** | Include positively identified AI units in automatic framing. Unknown AI/player identities remain included. |
| **Include bases / mission objects** | Include bases and mission objects in automatic framing. |

The two camera filters affect automatic framing, not whether a contact can be
drawn. When valid `/indicators` data reports `army: "tank"` and the live own
marker is a ground vehicle, automatic framing excludes airfields, fighter/bomber
spawns, and all aircraft, including aircraft over the ground map and squad
aircraft. Aircraft markers remain visible but do not affect the **All units**
zoom or last-enemy camera anchor. Ground contacts and capture zones remain
eligible under the filters. Tank spawn positions always anchor the ground
camera, including when allied units or
bases/mission objects are excluded. Flying, missing tank indicators, or an
unavailable own marker restores normal framing; manual navigation is unaffected.
This detects the current vehicle context, not the mission's game mode.

On a battlefield with tank spawn points, or while a tank player is confirmed,
all spawn markers are hidden, including their hover cards and target readouts.
Their original API data remains available to camera planning. Capture zones
remain visible. Tank spawn evidence keeps markers hidden during respawn and
when switching to an aircraft within the same ground battlefield.

With both filters off, after the relevant enemies disappear and their
brief predictions expire, framing includes the last actual position of the
last observed enemy. It is labeled **Last enemy position**. This is a camera
reference, not a continuing contact. A map/session change or disconnection
clears it.

The thin yellow line is your ground course. Moving icons have a direction tip.
Squad and target course lines are independent of distance/time labels.
No line is invented when a direction is unknown.

Compass bearings increase clockwise: north is 0°, east 90°, south 180°, and
west 270°. They refer to map north, not your vehicle's heading. The compass
requires a current, live own marker inside the viewport; it is hidden for
missing, stale, destroyed, or off-screen players. Pan back or use **Follow player**
to restore it. Demo uses its explicitly synthetic player marker.

Measurement bearings use the same convention and run from the drag's start
to its end; reversing the drag reverses the bearing by 180°. Measurements
retain their world positions and bearings when the camera moves. A zero-length
measurement has no bearing. Measurement bearings remain visible when the compass
or its intermediate degree labels are disabled.

## Contacts and target labels

| Appearance | Meaning |
| --- | --- |
| Yellow | Your own unit. |
| Green with a ring | Your squad. |
| Blue | Other allies. |
| Red | Enemies. |
| Gray | Unknown affiliation. |
| **AI** label | Positively identified computer-controlled unit. |
| Blinking, dashed outline | Briefly predicted lost enemy. |
| Brief flash, then an X | Confirmed, uniquely attributable destruction event. |

The API does not consistently identify AI versus human players. A lost enemy
is extrapolated for up to two seconds after the last observation; without a
reliable speed, its position remains fixed. A destruction marker lasts five
seconds and requires confirmed data with an unambiguous object association.
Disappearance alone is never treated as a kill.

Bright corner brackets display target backgrounds supplied by the API, even
with course lines disabled. The observed example is
`icon_bg: MediumTankTarget`; matching `<IconName>Target` backgrounds are
recognized. Multiple marked enemies retain their markers, while the course
line uses geometric selection. Neither `blink` nor disappearance means
target selection. An API marker is not proof of your personal selection or a
radar lock.

Under **Target labels**, distance and time can be selected independently:

- **Marked target** defaults to both values, including behind your aircraft.
  Its settings take priority over course-line and unit-category settings.
  Older profiles receive these defaults without losing their other choices.
- **Course-line target** also defaults to both values. It selects the enemy
  ahead of you nearest the drawn course line; equal lateral distances are
  resolved by direct distance. Enemies behind you are excluded from this
  geometric selection. If the same enemy is API-marked, only one label appears
  and its marked-target settings apply.
- **Other air units**, **Other ground units**, **Other bases / airfields**, and
  **Other naval units** each have separate distance/time switches, initially
  off.
- **All off** disables every target label. Saved choices survive a restart.

Distance is the direct map distance. Time is that distance divided by your
current ground speed, to the target's currently displayed position. It is not
an interception calculation and does not account for altitude differences.
When stationary or without a reliable speed, the label shows **Time —**.
Estimated contacts use **≈** and a muted label. Target values disappear when
the connection is lost.

## Fuel range

The range circle shows one-way distance without a return reserve, assuming
unchanged fuel consumption and speed. Decreasing `Mfuel, kg` values provide
consumption; changes in map position provide ground speed. An estimate normally
needs approximately five seconds. Coarse fuel gauges may require up to
30 seconds. An absent circle means the necessary measurements are missing or
not yet sufficient.

**Range circle** fits the entire circle to the view. Especially for jets, it
can be larger than the game map, and is drawn to scale beyond the map image.
**All units** frames contacts only, so the circle's edge can lie outside that
view.

## Icons

**Icons** shows a preview in every dropdown entry. Import SVG or PNG images
and map them to your own player, a unit class, or an API icon name already
observed in the session. Custom icons should point upward, with their center
representing the unit position. SVG `currentColor` is replaced by the unit's
color. Scripts, active SVG content, and external SVG resources are rejected.
Imported files must not exceed 4 MB.

### SVG compatibility in 0.2.0

Version 0.2.0 replaces the former SVG dependency with a bounded, MIT-derived
icon renderer compatible with the application's AGPL license. **Some complex
SVG files accepted by earlier versions now require conversion to PNG.** Existing
files and mappings are preserved. An unsupported import produces an error;
an unsupported saved icon uses the normal fallback and records the reason in
`app.log`. Export the original image to PNG in your image editor, then import
and map that PNG under **Icons**.

Supported SVGs contain `svg`, `g`, `path`, `rect`, `circle`, `ellipse`, `line`,
`polygon`, and `polyline`, with optional `title`/`desc`. The renderer supports
solid named/hexadecimal/RGB colors, `currentColor`, inherited paint properties,
inline paint styles, fill rules, strokes, dashes, opacity, and matrix,
translate, scale, rotate, and skew transforms. A `viewBox`, nonzero origins,
and `preserveAspectRatio` alignment with `meet`, `slice`, or `none` are supported.
Numeric lengths may use pixels or `pt`, `pc`, `in`, `cm`, and `mm`; percentages
are accepted only for root dimensions with a `viewBox`.

Gradients, patterns, masks, filters, clipping paths, text, embedded images,
referenced elements (`use`/`defs`), nested SVG viewports, stylesheet rules,
CSS classes, and other unsupported elements, attributes, or style properties
are explicitly rejected. Scripts, event handlers, DTDs/entities, processing
instructions, and resource references are not allowed. This is an icon subset,
not a complete SVG document renderer.

Icons are limited to 2,048 elements, nesting depth 32, and 32,768 path points.
Inputs with non-finite or excessive dimensions/transforms are rejected.
The [renderer provenance](https://github.com/VoltKraft/WarThunder_Map_Helper/blob/v0.2.0/src/MapHelper.Desktop/Vendor/SkiaSharp.Extended.Svg/README.md)
records its exact upstream source, license, and implementation bounds.

File selection imports the chosen file's stream into the application's data
directory. It does not require broad access to the home directory, including
when the Linux file chooser uses a Flatpak portal.

**Automatic** prefers original icons exposed as recognizable image resources
or icon fonts by the local War Thunder web server. Missing or unknown
resources use the bundled fallback icons. Original game graphics are not
distributed with this application. Game archives are not modified or unpacked.

Use **Reload** after editing files or `icons/mapping.json` externally. Mapping
keys are `Player`, `Aircraft`, `Ground`, `Ship`, `Objective`, or an exact
API icon name. Values are filenames in the icon directory without directory
components. For example:

```json
{
  "Player": "my-aircraft.svg",
  "Fighter": "my-fighter.png"
}
```

The original application logo combines an aircraft, course line, and range
ring. Its source and export instructions are in the
[branding documentation](https://github.com/VoltKraft/WarThunder_Map_Helper/blob/v0.2.0/src/MapHelper.Desktop/Assets/branding/README.md).

## Settings and files

| Platform | Data directory |
| --- | --- |
| Windows | `%LOCALAPPDATA%\WarThunderMapHelper` |
| Linux | `$XDG_CONFIG_HOME/WarThunderMapHelper`, or `~/.config/WarThunderMapHelper` when unset |
| Flatpak | The application's sandbox-specific `XDG_CONFIG_HOME`; see [Distribution](DISTRIBUTION.md). |

The `--data-dir=<path>` option overrides the data directory and is useful for
isolated tests. The application creates the directory and an `icons/`
subdirectory when needed.

- `settings.json` stores the endpoint, color mappings, target labels, camera
  filters, and display choices.
- `icons/mapping.json` stores custom icon mappings. Reloading icons reads this
  file into the current settings.
- `icons/original-mapping.json` and `icons/original-*` cache successfully
  imported original icons.
- `icons/default_*.svg` are copies of the bundled fallback icons.
- `icons/README.txt` is created with icon instructions when absent.
- `app.log` stores diagnostics. At approximately 1 MB it rotates to
  `app.log.old` on the next write. Log-write failures do not stop the app.

Use the UI to edit settings. If editing JSON directly, close the application
first so that later UI saves do not overwrite the edit. Invalid JSON falls
back to defaults and is logged. An invalid saved endpoint falls back to the
loopback default.

The settings schema is unchanged by the English conversion:

| Setting | Values and default |
| --- | --- |
| `ApiAddress` | HTTP(S) URL; default `http://127.0.0.1:8111/`. |
| `ColorOverrides` | Color strings mapped to affiliation enum names. The UI accepts unique `#RRGGBB` colors for `Ally`, `Enemy`, and `Squad`; default empty. |
| `IconOverrides` | Same mappings as `icons/mapping.json`; default empty. |
| `TargetDisplay.MarkedTarget`, `TargetDisplay.CourseTarget` | `"Distance, Time"` by default; also accept `"None"`, `"Distance"`, or `"Time"`. |
| `TargetDisplay.Air`, `Ground`, `Bases`, `Sea` | Target-value flags; default `"None"`. |
| `Camera.IncludeAi`, `Camera.IncludeBases` | Boolean, both `true` by default. |
| `Display.ShowAllies`, `Display.SquadVectors`, `Display.TargetVector` | Boolean, all `true` by default. |
| `Display.ShowCompass` | Boolean; default `true`. |
| `Display.CompassLineCount` | Integer; default `4`. Clamped to 4–72, then rounded down to a multiple of four when loaded. |
| `Display.ShowCompassDegrees` | Boolean; default `false`. Only affects intermediate compass rays. |

Saving settings writes a temporary file, replaces `settings.json`, and then
updates `icons/mapping.json`. Write failures are shown in the relevant UI.
Camera changes that cannot be saved remain active for that session.

Windows MSI uninstall removes the application and its shortcuts but preserves
this data directory and custom icons. Existing `recordings/` files are left
untouched. Recording, replay, and replay command-line startup are not supported.

## Command-line options

Options that take a value use `--name=value`. Paths containing spaces must be
quoted as one argument. These options are primarily intended for reproducible
development checks; normal startup requires none.

| Option | Effect |
| --- | --- |
| `--demo` | Start with synthetic data. |
| `--data-dir=<path>` | Use an isolated settings, icon, and log directory. |
| `--range-view` | Fit the first available range estimate. |
| `--smoke-test` | Exit after a screenshot, or after about three seconds when no screenshot is requested. |
| `--screenshot=<path>` | Save a rendered PNG; creates the parent directory. |
| `--capture-delay=<seconds>` | Delay the screenshot; default 7 seconds. Use a non-negative value with a decimal point. |
| `--capture-measurement` | Add a sample measurement before the screenshot. |
| `--capture-contact=<source-id>` | Open a matching contact's information card; `self` works in Demo. |
| `--capture-target-settings` | Capture the target-label settings window. |
| `--capture-map-options` | Capture the map-options panel. |
| `--capture-symbols` | Capture the icons window. |
| `--capture-symbol-dropdown` | Capture its first open dropdown; combine with `--capture-symbols`. |

A minimal check from a source checkout is:

```text
dotnet run --project src/MapHelper.Desktop -- --demo --smoke-test --screenshot=artifacts/demo.png --data-dir=artifacts/test-profile
```

The screenshot check returns exit code 2 on capture failure; a fatal startup
exception returns 1. Details are written to the selected profile's log.
Platform prerequisites and automated checks are in
[Development](DEVELOPMENT.md).

## Data limitations

The application displays only what the HTTP interface exposes. Complete lists
of allies, enemies, and their weapons are not available in every mode.
Ordinary map objects often lack stable IDs and reliable AI/player flags.
Position association is therefore an estimate; ambiguous crossings do not
produce an assumed reliable velocity.

Known green map colors are interpreted as squad contacts, including
`#39D921`, observed in a historical battle sample, and `#67D756`, observed for
squad tanks on 2026-09-28. Additional squad, ally,
and enemy colors can be configured under **Connection**. Comparison against
the actual squad roster remains an open validation item.

The legacy ground icons `Wheeled` and `Tracked` are documented as AI in the
[API client reference](https://github.com/PowerBroker2/WarThunder/blob/master/WarThunder/mapinfo.py)
and are marked accordingly. Generic aircraft and tank icons remain unknown
without explicit evidence. Optional Boolean `is_human`, `is_ai`, and
`is_bot` fields are used only when consistent; their presence is not
confirmed for the standard game API.

The reviewed endpoint descriptions and responses did not establish a general
identifier for your personal target selection or radar lock. API target
backgrounds are shown as map markers without claiming a radar lock. The gold
course-target highlight refers to the geometric target nearest your course
line.

HUD kill messages usually contain text without a map-object ID. The app does
not invent a position for these messages. The core supports confirmed events
with object IDs, exercised in Demo and tests. The HTTP adapter does not treat
unknown `destroyed` or `blink` fields as kills.

`weapon1` and similar fields are retained as unclassified readings.
`ammo_counter*` values, when present, are preserved as ammunition counters
in `WeaponTelemetry`. Weapon types and ranges are not guessed from them.
A historical `f_16xl` test flight on 2026-09-24 returned only
`weapon2 = 0` and `weapon4 = 0`, with no ammunition counters or identifiable
loadout. Without a confirmed vehicle-specific unit, `fuel_consume` remains
a raw value.

That test flight yielded no usable projectile positions. `bombing_point`
is a bombing target. There is currently no separate projectile display; see
the [investigation and its limitations](PROJECTILE_API.md).

Community references used during development:

- [Endpoints and observed fields](https://github.com/CreeperUX/WT-8111-Neo/blob/main/WT_8111_API_REFERENCE.md)
- [Map objects](https://github.com/lucasvmx/WarThunder-localhost-documentation/blob/master/MapObjects/MapObjects.md)
- [Flight state](https://github.com/lucasvmx/WarThunder-localhost-documentation/blob/master/State/State.md)

The [validation history](VALIDATION.md) distinguishes historical live evidence
from automated and synthetic checks. The application does not start War Thunder
or access game memory.

## Troubleshooting

- **Waiting for War Thunder:** enter a mission and check whether the configured
  browser-map URL is reachable from this computer. Confirm the endpoint under
  **Connection**.
- **Disconnected / stale data:** the map-object feed is unavailable or no longer
  fresh. The application waits for new data; Demo is always a manual choice.
- **Missing range:** wait for movement and measurable fuel decrease. Coarse
  gauges can require up to 30 seconds; missing data intentionally leaves the
  value unavailable.
- **Missing or unexpected contact:** compare with the game's browser map and
  review color mappings and visibility settings. Some modes expose fewer
  contacts than the main game view.
- **Icon import failed:** use an SVG or PNG no larger than 4 MB. Remove external
  SVG resources and active content, or use a PNG.
- **Settings are not saved:** check write access to the data directory and
  consult `app.log`. Preserve the profile when collecting diagnostics, but
  review logs and raw values before sharing them publicly.
