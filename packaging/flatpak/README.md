# Flatpak packaging

Application ID: `io.github.voltkraft.WarThunder_Map_Helper`.

- [`flathub/io.github.voltkraft.WarThunder_Map_Helper.yml`](flathub/io.github.voltkraft.WarThunder_Map_Helper.yml)
  is the shared source-build manifest for GitHub Flatpak bundles.
- `flathub/nuget-sources.json` is the generated, hash-pinned offline NuGet feed.
- The desktop entry, AppStream metadata, and 512px icon use the same app ID.
- `licenses/` contains exact-version upstream supplements.

See [Flatpak builds](../../docs/FLATPAK.md) for reproducible local builds and
[distribution](../../docs/DISTRIBUTION.md#flathub) for the Flathub policy boundary.
The `flathub` directory follows the reference project's layout; it does not mean
its AI-assisted manifest can be submitted to Flathub.
