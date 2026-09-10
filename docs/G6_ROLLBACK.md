# G6 UI and lobby rollback

The user reported repeated game-entry errors and a substantial usability regression
in the G6 shell, and approved restoring the pre-G6 launcher and match admission.
This rollback reverses `1c8df59` (client shell), `fc5b7dc` (authoritative lobby), and
`581fe2d` (completion documentation). The supplied plan remains a historical
reference, not a statement that G6 is accepted or enabled.

The original Home, settings, account, server browser, host/join and Practice
entry points are restored on desktop and Android. Matches again use automatic
waiting/countdown rather than the G6 owner-ready lobby. Rebuild and deploy client
and server together: protocol 8 alone does not distinguish these unreleased builds.

Backend production security, secure account sessions, career storage and the
versioned rating ledger remain. The shared report reader still accepts schemas 1
and 2, but the restored server emits schema 1 because it does not capture the new
forfeit/outcome evidence. Backend policy classifies these reports as `LegacyReport`
and does not award new rating points. Existing stored ratings are preserved.
The patched desktop D-Bus dependency and Android secure-store source inclusion
are retained across the rollback.

## Validation

- Release solution and local desktop builds: passed, zero warnings/errors.
- Android arm64 Release build: passed, zero warnings/errors; no physical device test.
- The first full main run passed 971/972. The late-join failure passed all 16 tests
  in isolation; its helper polled UDP immediately after sending. A bounded snapshot
  receipt wait now removes that scheduling assumption without changing game code.
- Final full main suite: 972/972 passed. Backend: 198/198 passed with the isolated
  PostgreSQL fixture. Imaging: 18/18 passed. The test database was stopped afterward.
- Rendered checks did not pass: macOS GLFW crashed in `_glfwGetVideoModeCocoa`
  before gameplay evidence, and Avalonia could not initialize its RenderTimer
  (native error -6661). No visual or end-to-end playability claim is made.
- No endurance/observer soak or physical Android/high-refresh acceptance was run.

Rebuilt local desktop entry point: `src/Client/bin/Release/net10.0/ProjectPrime.dll`;
its matching hosted server is in the adjacent `server/` directory. Existing
installed or downloaded G6 packages are not updated by this source rollback.
Unrelated map changes and the pre-existing LICENSE deletion are excluded.
