# Repository Guidance

## Project context

War Thunder Map Helper is an unofficial, English-language tactical map
companion using .NET 10 and Avalonia 12. It reads the optional local game HTTP
API and starts offline when the game is unavailable. Synthetic demo data must
always remain distinguishable from live data.

- `WarThunderMapHelper.slnx`: cross-platform application and test solution.
- `src/MapHelper.Core`: platform-independent tracking, map geometry, camera,
  target navigation, and range estimation.
- `src/MapHelper.Telemetry`: bounded HTTP requests, parsing, and demo source.
- `src/MapHelper.Desktop`: Avalonia UI, settings, icon loading, and lifecycle;
  entry point `Program.Main`, application composition in `App`/`MainWindow`.
- `tests/MapHelper.Tests` and `tests/MapHelper.UiTests`: logic/integration tests
  and headless Avalonia interaction tests.
- `packaging/`: architecture-specific Windows, Linux, Flatpak, and WinGet
  packaging. Installer tooling must not enter the portable application core.
- `scripts/`, `tools/`, `.github/workflows/`: validation and release automation.
- `docs/`: architecture, development, usage, distribution, and validation.
- `.codex/`: project agent definitions; preserve the user's configuration.

`global.json` pins the SDK, `Directory.Build.props` owns the release version,
and project files own NuGet versions. Derive tooling from these manifests.
See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed boundaries.

## Working agreements

- Follow established repository conventions before introducing new patterns.
- Keep changes focused on the requested outcome.
- Preserve unrelated user changes.
- Avoid editing generated, vendored, or lock files manually unless the task
  explicitly requires it.
- Update tests and documentation when behavior or public interfaces change.

## Commands

Run from the repository root with the SDK selected by `global.json`:

```text
dotnet restore WarThunderMapHelper.slnx
dotnet run --project src/MapHelper.Desktop -- --demo
dotnet build WarThunderMapHelper.slnx -c Release --no-restore
dotnet test WarThunderMapHelper.slnx -c Release --no-build
python scripts/check_dependency_licenses.py
dotnet format whitespace WarThunderMapHelper.slnx --verify-no-changes --no-restore
dotnet format whitespace WarThunderMapHelper.slnx --no-restore
python -m unittest discover -s scripts/tests -p "test_*.py"
python -m unittest discover -s tools/tests -p "test_*.py"
```

The build runs the C# compiler, nullable analysis, and .NET analyzers with
warnings as errors; restore audits direct and transitive dependencies. Python
commands require Python 3.11 or newer (`python3` on Linux is also suitable).
Packaging and platform prerequisites are documented in
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) and
[docs/DISTRIBUTION.md](docs/DISTRIBUTION.md).

## Validation

- Run the smallest relevant validation first, followed by broader checks when
  justified by the change.
- Review the final diff for unintended changes.
- Report which checks were run and disclose checks that could not be run.
- Treat work as complete only when the requested behavior is implemented,
  relevant validation passes, and required documentation is updated.

## Constraints

- Support Windows x64/ARM64 and Linux x64/ARM64. Flatpak names these Linux
  architectures `x86_64` and `aarch64`. Do not assume a 32-bit target.
- Keep the HTTP API optional, cancellable, and bounded. Never introduce game
  memory access, injection, or fabricated telemetry. Preserve raw API fields
  and explicitly identify estimates and missing data.
- Retain persisted JSON field names and existing user-data directories unless
  an explicit, tested migration is part of the change.
- Preserve the x64 MSI upgrade identity and per-user installation semantics.
- Keep WiX hardlinks and symbolic output links disabled for Nextcloud Cloud
  Files compatibility. Generated harvest files belong under `obj/`.
- Do not commit `bin/`, `obj/`, `artifacts/`, `.build-cache/`, credentials,
  private logs, or user-imported game assets. Regenerate derived packaging
  inputs with their documented tools instead of changing hashes manually.
- Release only tested immutable commits; never overwrite a published version
  or use pull-request artifacts with publishing credentials.
- Keep all authored UI, code comments, and documentation in English. Raw
  external telemetry and original license texts must remain faithful to their
  sources.
- Project code is AGPL-3.0-only. Review the complete dependency chain and retain
  upstream notices; a top-level MIT label does not cover its dependencies.

- Before adding or updating a dependency, identify its license from an
  authoritative source and verify that it is compatible with the repository's
  license and intended distribution.
- Do not add dependencies with known-incompatible licenses or licenses that
  cannot be verified.
- If a task requires, or appears to require, an incompatible or unverifiable
  dependency, inform the user before proceeding. Identify the dependency, its
  license, the compatibility concern, and compatible alternatives when
  available.

## Documentation

- Always maintain changelogs according to the latest published version of
  [Keep a Changelog](https://keepachangelog.com/).
- Always assign and increment project versions according to the latest
  published version of [Semantic Versioning](https://semver.org/).
- Verify the latest versions of both standards on their official websites
  when updating changelogs or project versions.
- Treat documentation as part of the implementation, not as optional follow-up
  work.
- Update documentation in the same change whenever behavior, public interfaces,
  configuration, commands, dependencies, or operational procedures change.
- Document public APIs, configuration options, environment variables, expected
  inputs and outputs, side effects, failure modes, and compatibility
  requirements.
- Provide migration guidance for breaking changes, renamed options, changed
  defaults, or persistent data-format changes.
- Keep examples minimal, realistic, secure, and executable when practical.
- Verify documented commands and examples against the current repository.
- Do not duplicate detailed information across multiple documents. Establish
  one source of truth and link to it from overview documents.
- Keep the README focused on orientation, setup, common workflows, and links to
  detailed documentation.
- Record significant architectural decisions and tradeoffs in an ADR or
  equivalent architecture document when the repository uses that practice.
- Keep generated documentation reproducible and modify its source rather than
  generated output.
- Remove or correct obsolete documentation instead of leaving contradictory
  guidance.

## Code comments and API documentation

- Prefer clear names and straightforward structure over comments that explain
  obvious code.
- Use comments to explain why a decision exists, not to narrate what the code
  does.
- Document non-obvious invariants, assumptions, edge cases, units, concurrency
  behavior, security boundaries, performance tradeoffs, and external
  constraints.
- Explain workarounds with the underlying issue, affected dependency or
  platform, and the condition under which the workaround can be removed.
- Add docstrings or equivalent API documentation to public interfaces and
  complex internal abstractions.
- Document contracts, parameters, return values, errors, side effects,
  mutability, nullability, thread safety, and lifecycle requirements when
  relevant.
- Keep comments close to the behavior they describe and update them in the same
  change as the code.
- Treat stale or misleading comments as defects.
- Do not leave commented-out code; rely on version control.
- Avoid vague TODOs. A TODO must state the missing work, why it remains, and a
  tracking reference when one exists.
- Do not add large explanatory comments to compensate for unnecessarily complex
  code. Simplify the implementation first when practical.
- Do not generate documentation or comments merely to increase coverage or
  volume. Add them only when they clarify usage, contracts, constraints, or
  decisions that are not apparent from the implementation.

## Language and commits

- Write all repository documentation, code comments, docstrings, API
  documentation, TODOs, changelog entries, and explanatory text in examples in
  English, regardless of the language used in the conversation.
- Write commit messages in English, regardless of the language used in the
  conversation.

# Subagent Strategy

## Objective

For every task, automatically decide whether subagents would improve
correctness, coverage, independent verification, or delivery time. The user
does not need to request delegation explicitly.

Quality is the primary objective. Use additional agents and higher-capability
settings whenever they materially improve the expected result. Avoid
delegation only when coordination overhead, context loss, or edit conflicts
would outweigh the benefit.

## Agent selection

Before delegating work:

1. Inspect the currently available built-in and project-defined agents and
   their descriptions. Do not rely on a fixed list remembered from earlier
   tasks.
2. Decompose the request into useful, independently verifiable workstreams and
   identify dependencies, shared files, and integration points.
3. Select the existing specialist whose scope best matches each workstream.
4. Use coordination agents such as `orchestrator` or `workflow_manager` when a
   task has multiple dependent workstreams, complex handoffs, or meaningful
   integration risk. Use planning agents when the needed output is a plan
   rather than implementation.
5. If no available role fits a necessary workstream, start a purpose-built
   ad-hoc agent with a clear name, bounded objective, minimal required context,
   expected output, and validation criteria.

Choose the orchestration approach afresh for each task. Do not delegate merely
to use an available role, and do not force a complex task through one agent
when specialist or coordinating agents would produce a better result.

## Model and reasoning selection

Select the model and reasoning effort dynamically for every spawned agent
based on the assigned workstream's complexity, ambiguity, risk, context size,
and required confidence.

- Prefer faster, lower-effort settings only for well-bounded work where quality
  will not suffer.
- Use stronger models or higher reasoning for architecture, security,
  difficult debugging, cross-cutting changes, ambiguous requirements, and
  synthesis across multiple findings.
- Escalate when the initial assignment proves harder or less certain than
  expected.
- Do not keep a task on a weaker model or lower reasoning level when doing so
  could compromise correctness or completeness.

## Parallelism and coordination

Run independent investigations, reviews, tests, and isolated implementation
workstreams in parallel when this improves quality or reduces wall-clock time.
Sequence dependent work and coordinate tasks that may edit the same files or
make conflicting decisions.

Every delegated task must state its objective, scope, relevant context,
constraints, expected deliverable, and validation criteria. Require agents to
return concise conclusions and evidence rather than unnecessary raw output.

Wait for all results required by the execution plan, resolve disagreements,
integrate dependent outputs, and perform a final cross-cutting review.

## Primary agent responsibility

The primary agent remains responsible for understanding the complete request,
making architectural and cross-cutting decisions, selecting and coordinating
agents, validating important findings, resolving conflicts, and producing the
final result.

Treat subagent output as evidence, not truth. Verify important claims and use
independent review for high-risk or critical conclusions.
