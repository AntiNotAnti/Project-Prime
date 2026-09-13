# Current Project Prime release gates

Status: authoritative gate ledger, 2026-09-12. Labels are deliberately narrow:

- **FOCUSED** — source plus relevant automated tests; no physical claim.
- **BLOCKED** — a reproducible local blocker prevents the intended validation.
- **OPEN** — validation requires an external environment or a future pass.
- **NOT RUN** — no evidence was produced in this checkout.

## Gate status

The roadmap header and the detailed stabilization plan use different labels for
QA8 and QA9. This ledger follows the detailed plan: QA8 is documentation, QA9
is gameplay fidelity, and the subsequent enhancement wave is tracked
separately.

| Gate | Current status | Boundary |
| --- | --- | --- |
| QA0 known-defect remediation | FOCUSED | framebuffer sizing/resize, exact actor identity, logical killcam commands, and quarantine are source-reviewed; the latest full test run passed 2664/2664 tests |
| QA1 replay/killcam/highlights/broadcast | FOCUSED | lifecycle, audio ownership guard, exact identity, bounded director history, and camera final sweep are implemented; no rendered/device acceptance claim |
| QA2 desktop transitions/session lifecycle | OPEN | existing coordinator remains the single owner; broader failure/reentrancy fuzzing is not a prerequisite for claiming QA0/QA1 |
| QA3 protocol/reconnect/WAN | FOCUSED / OPEN | protocol 17 codec/auth/rejoin tests are local evidence; geographic WAN and deployed transport remain open |
| QA4 map platform durability | FOCUSED | process-wide keyed acquisition ownership and resumable partial semantics have focused coverage; Android mount/unmount and long soak remain open |
| QA5 Backend/PostgreSQL durability | FOCUSED / OPEN | Backend tests provide local evidence; 226 passed and 7 PostgreSQL-only tests were skipped because `PRIME_TEST_POSTGRES_FILE` was unset. Deployed PostgreSQL durability remains open |
| QA6 protected release validation | OPEN | protected launch/symbol recovery matrix has not been established |
| QA7 physical platform acceptance | OPEN | Windows/macOS/Linux/Android devices, high-refresh displays, physical controllers, audio, and real touch remain open |
| QA8 documentation cleanup | FOCUSED | current architecture/protocol/release-gate documents and the evidence ledger are maintained locally; external acceptance is not implied |
| QA9 gameplay fidelity cleanup | NOT RUN | no AMHE1 fidelity or balance changes are promoted during stabilization |
| Next enhancement wave | BLOCKED | remains behind stabilization, QA9 fidelity, and release-gate acceptance |

## Evidence ledger

| Evidence | Result / interpretation |
| --- | --- |
| `GAME_DATA_DIRECTORY="$PWD/AMHE1" dotnet test tests/Tests/Tests.csproj -c Release` | PASS, 2664/2664 tests in 3 minutes 21 seconds |
| `dotnet build src/Client/Client.csproj -c Release --no-restore -m:1` | PASS, 0 warnings / 0 errors |
| `dotnet build src/Game/Game.csproj -c Release --no-restore -m:1` | PASS, 0 warnings / 0 errors |
| `dotnet test tests/Backend.Tests/Backend.Tests.csproj -c Release` | PASS, 226 passed; 7 PostgreSQL-only tests skipped because `PRIME_TEST_POSTGRES_FILE` was unset. Deployed PostgreSQL durability remains open |
| `dotnet build src/Server.Node/Server.Node.csproj -c Release --no-restore -m:1` | PASS, 0 warnings / 0 errors |
| `dotnet build src/Server.Worker/Server.Worker.csproj -c Release --no-restore -m:1` | PASS, 0 warnings / 0 errors |
| `dotnet build src/Backend/Backend.csproj -c Release --no-restore -m:1` | PASS, 0 warnings / 0 errors |
| `GAME_DATA_DIRECTORY="$PWD/AMHE1" dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --no-restore` | PASS, 227/227 tests in 1 minute 7 seconds; covers local Node/real-Worker integration only, not WAN, physical, or deployed acceptance |
| `python3 -m unittest discover -s tools/tests` | PASS, 213/213 tests; Python tests do not prove client rendering or device input |
| `python3 tools/check-project-boundaries.py` | PASS, 0 violations |
| Post-audit production build matrix in section 70 of the implementation plan | PASS, all 10 desktop/server projects; ordinary Android build also passed, followed by a second successful desktop Client build to prove restore-graph isolation |
| Post-audit content-free test matrix in section 71 | PASS after refreshing the stale protocol-15 authentication golden value for the existing protocol-16 implementation; no wire behavior changed. Seven focused projects passed 800/800 and aggregate `tests/Tests` passed 2543/2543 |
| Protocol-17 remote charge presentation remediation | PASS for protocol/authentication/replay regression coverage, Client 48/48, Server.Shared 76/76, and content-free Server.Worker 39/39. The current aggregate reached 2558/2559 before an unrelated, independently reproducible aim-assist assertion failed; a second run excluding it reached 2557/2558 before an unrelated allocation-sensitive collision assertion failed, which then passed alone. Live two-client visual confirmation remains open. |
| `dotnet run --project src/Client/Client.csproj -c Release --no-build -- --runtime-smoke --runtime-smoke-frames 1` | PASS on local Apple Silicon macOS, 2026-09-12: application path and macOS secure-session provider resolved, Avalonia bootstrapped, SDL GPU/Metal initialized, one hidden frame submitted, 32x32 drawable reported, clean exit. Content-free native lifetime evidence only; Windows/Linux scheduled jobs and rendered gameplay remain open |

The earlier hidden compatibility-OpenGL probe is historical evidence for the
retired probe. `ReplayPlaybackCheck` now uses the SDL `IRenderToolHost` path.
The content-free macOS runtime smoke proves native initialization, submission,
and disposal, but not replay, content-backed rendering, visual parity, input,
or sustained performance.

## Explicit open gates

Do not promote focused/static results to physical Windows/macOS/Linux/Android
acceptance, high-refresh traces, physical controller/touch ergonomics, audible
mix quality, geographic-WAN behavior, deployed PostgreSQL durability, protected
binary launch, or symbol recovery. Killcam touch skip currently has a logical
submission API, but no active Android killcam host is wired; Android touch
killcam acceptance is therefore OPEN rather than complete. QA9 remains blocked
until the stabilization boundaries above are independently accepted.
