# Contributing

War Thunder Map Helper is an unofficial desktop companion that displays only
information supplied by the game's HTTP interface. Contributions must preserve
that boundary and the distinction between live data, estimates, and demo data.

Read [AGENTS.md](AGENTS.md) for repository agreements and
[Development](docs/DEVELOPMENT.md) for setup, commands, and test requirements.
The [architecture guide](docs/ARCHITECTURE.md) describes module boundaries.

## Making a change

1. Discuss substantial behavior changes in an issue before implementing them.
2. Branch from `main` and keep the change focused. Write code, UI text,
   comments, documentation, and commit messages in English.
3. Add regression tests for changed behavior. Keep external API fixtures
   faithful to the original response and remove personal information before
   contributing a new recording.
4. Update documentation and `CHANGELOG.md` in the same pull request. Follow
   [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) and
   [Semantic Versioning 2.0.0](https://semver.org/); verify their current
   published versions when updating release metadata.
5. Run the relevant tests and the checks in the development guide. Describe
   what you verified and what still needs real hardware or a live match.

Every new dependency, including a transitive dependency, needs an authoritative
license source and a compatibility review before it is added. Do not suppress
NuGet vulnerability warnings to make a release pass. Keep license notices and
offline Flatpak inputs in sync with dependency changes; see
[Third-party notices](THIRD_PARTY_NOTICES.md).

## Pull requests and releases

Provide the problem, resulting behavior, and relevant validation in the pull
request description. Do not include credentials, machine-specific paths,
personal logs, build output, or downloaded game assets. A maintainer reviews
changes before merging.

The version in `Directory.Build.props` identifies the release. A successful
`main` build can publish a new version automatically. An existing published
version is immutable; prepare a new version and matching changelog entry when
a new release is intended. Store submission and repository setup are described
in [Distribution](docs/DISTRIBUTION.md).

Report vulnerabilities privately using [SECURITY.md](SECURITY.md), and follow
the [Code of Conduct](CODE_OF_CONDUCT.md) in all project discussions.
