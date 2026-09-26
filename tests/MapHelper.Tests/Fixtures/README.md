# Recorded API regression fixtures

The `testflight-f16xl-*.json` files were read on 2026-09-24 from a running,
user-provided War Thunder test flight at `http://127.0.0.1:8111`. They
preserve the original responses from `map_info.json`, `map_obj.json`,
`state`, `indicators`, and `mission.json`.

They primarily guard the enemy color `#f00C00` and the `f_16xl`
instrument fields `weapon2` and `weapon4`, whose presence does not
identify a concrete loadout. No HUD or chat messages were recorded.

`battle-su30sm2-map_obj.json` contains name-free regression examples from
the subsequent battle observation. It covers additional squad/ally colors,
the `MediumTankTarget` background, and ship variants. See the
[historical validation record](../../../docs/VALIDATION.md) for the scope
and limits of the live comparison.

Raw field names and values are intentionally not translated. These fixed
reference files are development inputs; the application does not provide
recording or replay.
