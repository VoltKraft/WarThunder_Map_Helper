# Security policy

## Supported versions

Security fixes target the latest published release and `main`. Older `0.x`
versions do not receive a separate maintenance branch. Upgrade to the latest
release before reporting a defect that is already fixed.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting on the repository's
[Security page](https://github.com/VoltKraft/WarThunder_Map_Helper/security/advisories/new).
The maintainer must enable private reporting during repository setup. If that
form is unavailable, use the contact method on the
[maintainer's GitHub profile](https://github.com/VoltKraft) to request a private
channel without sharing exploit details publicly.

Include the affected version, operating system, architecture, reproducible
steps, and impact. Remove tokens, user names, addresses, and unrelated logs.
Do not open a public issue containing a working exploit before coordination
with the maintainer. This volunteer project does not promise a fixed response
deadline.

## Boundaries

- The application reads a configurable HTTP(S) endpoint, defaulting to
  `http://127.0.0.1:8111/`. It does not read game memory, inject code, run a
  listener, or bypass information hidden by the game. Use a trusted endpoint;
  ordinary HTTP traffic is unencrypted.
- Endpoint URLs containing credentials, query strings, or fragments are
  rejected. Network requests and icon inputs are bounded; SVG scripts,
  external resources, and document type declarations are rejected.
- Custom icons and settings are local files. Diagnostics may contain endpoint
  errors or game-provided text. Review `app.log` before sharing it.
- Flatpak needs network access to reach the local game API and graphics access
  to draw the window. Its exact permissions are declared in the package
  manifest. No home-directory access is required for normal operation.
- Release workflows accept artifacts only from successful, same-repository
  `main` push builds. Pull-request workflows have no publishing credentials.
  Store submission credentials belong in GitHub Actions secrets, never files.

Builds include dependency auditing. License review and vulnerability scanning
are separate checks; a permissive license does not establish that a package
is safe.
