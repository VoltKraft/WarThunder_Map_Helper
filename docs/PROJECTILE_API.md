# Projectile API investigation

Historical observation date: 2026-09-24, during a live `f_16xl` test flight via
`http://127.0.0.1:8111`. This is a record of that investigation, not a new
verification of the current application release.

**Result:** the inspected responses contained no usable positions, trajectories,
or identities for fired missiles, dropped bombs, or other projectiles.
Consequently, no separate projectile display was added. This finding applies
to the observed flight and inspected interfaces; it does not establish the
behavior of every vehicle or game mode.

## Historical live observations

The user confirmed firing or releasing a weapon during the flight.
Map objects were polled 1,689 times between 17:12:29 and 17:15:29 CEST on
2026-09-24. The sequence of object types and icon names did not change.
A second check from 17:16:37 to 17:17:37 CEST inspected 528 responses
individually; all contained the same 13 type/icon combinations. Both runs
completed without HTTP errors. The precise weapon-release time was not
synchronized with the API queries.

Observed objects included the own player, four other aircraft, vehicles,
ships, an airfield, four bombing targets, and a `point_of_interest`.
These names do not establish the presence of flying weapons:

- `bombing_point`: a bombing target on the map.
- `ground_model` / `SAM`: an air-defense vehicle.
- `ground_model` / `MissileDestroyer`: a ship.
- `point_of_interest`: a map marker with no demonstrated association to a
  projectile.

`/state` supplied flight and engine data. `/indicators` contained
`weapon2 = 0` and `weapon4 = 0`, unchanged in the second run's 102
instrument queries. No `ammo_counter*`, identified weapon models, or
projectile coordinates appeared. Zero values for these unclassified
instruments do not establish the aircraft's loadout.

`/hudmsg` returned 34 damage/kill messages and no `events` entries.
Messages had `id`, `msg`, `sender`, `enemy`, `mode`, and `time`,
but no position fields or projectile identifier. Their text therefore cannot
locate a flying weapon on the map.

The game's browser page queried the known map, mission, instrument, and HUD
endpoints. It contained no separate projectile feed. The reviewed community
references did not document one either:

- [Endpoints and fields](https://github.com/CreeperUX/WT-8111-Neo/blob/main/WT_8111_API_REFERENCE.md)
- [Documented map objects](https://github.com/lucasvmx/WarThunder-localhost-documentation/blob/master/MapObjects/MapObjects.md)

## Reproduce the diagnostic collection

`scripts/probe-projectile-api.ps1` polls map objects and instruments for a
bounded period. It writes initial object examples, observed types/field names,
and measurement statistics to `artifacts/projectile-api-probe`. This is a
development script, not an application recording feature.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/probe-projectile-api.ps1 -Seconds 180
```

The original investigation stored its first measurement in
`artifacts/projectile-api-probe/summary.json`. Windows PowerShell 5.1
aggregated the complete map list into one type/icon sequence in that run;
object examples were under `ObjectKinds[].Example.value`. The corrected
per-object analysis was stored in
`artifacts/projectile-api-probe/verification/summary.json`, alongside the
inspected browser page and HUD field structure. These are local historical
diagnostic artifacts, not files distributed in releases or guaranteed to
exist in a fresh checkout.

A future projectile display requires confirmed samples with positions and
established meanings for their type/icon fields. Trajectories are not
constructed from ammunition decreases or HUD text alone.
