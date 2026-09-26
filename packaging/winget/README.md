# WinGet packaging

`package.metadata.json` owns the stable package identity
`VoltKraft.WarThunderMapHelper` and the two MSI asset name templates.
Version-specific manifests are generated from actual released MSI files; no
placeholder ProductCodes or checksums are committed.

See [distribution](../../docs/DISTRIBUTION.md#winget) for setup, preparation,
validation, submission, and retry instructions. The package is prepared for the
Microsoft community repository; these files do not establish a published listing.
