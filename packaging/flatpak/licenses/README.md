# Supplemental license texts

`catalog.json` maps exact NuGet package versions to original upstream license
and notice files. `sources.json` records immutable source URLs and SHA-256 hashes
for those files. These supplements cover packages whose archives omit the full
license text, including Avalonia and the bundled Inter font.

The collector rejects changed supplements and unreviewed versions of a listed
package. Keep the original texts byte-for-byte. When dependencies change, verify
their licenses at the versioned upstream source, update the catalog and hashes,
then run the collection and compatibility checks in [development](../../../docs/DEVELOPMENT.md).

`tools/install-flatpak-license-notices.py` installs an attribution inventory;
it does not determine license compatibility. The separate
`scripts/check_dependency_licenses.py` gate checks the approved dependency policy.

The collector and the initial compatible supplements were adapted from
[Immich Folder Watch](https://github.com/VoltKraft/immich-folder-watch).
