# Windows Game + Studio installation qualification — 2026-10-19

The Windows x64 RmlUi portable package was built and exercised on Jarrett’s Windows machine. Clean extraction, both desktop applications, migration from the published legacy release, paired upgrade/downgrade, correct-role automatic relaunch, data preservation, and rejection of replacement while Studio is open passed.

## Delivered candidate

- File: `ProjectPrime-RmlUi-Windows-x64-rml100-20261019.zip` in Windows and Mac Downloads.
- Windows destination: `C:\Users\Jarrett\Downloads\ProjectPrime-RmlUi-Windows-x64-rml100-20261019.zip`.
- Both application versions: **0.35.1** (local qualification candidate; not a published release).
- ZIP: **180,441,262 bytes**; SHA-256 `a2cc3dba3d6b8f82f5084bdd318af72dacee09ec62aedf691db6b2c4bb662228`.
- Extract the whole ZIP into a folder. `ProjectPrime.exe` opens the game; `ProjectPrimeStudio.exe` opens the separate Studio application. Keep both executables and shared files together. Map Studio remains Coming Soon.
- Package checks verify both x64 GUI executables and matching versions, native runtime fingerprint, required resources/licenses, paired compatibility metadata, all 230 owned file hashes, ZIP inclusion of hidden manifests, and absence of game dumps/user configuration. No external .NET installation is required by these self-contained executables.

## Actual Windows evidence

Host `DESKTOP-94724L4`, Windows 11 build 26200.9711, x64, NVIDIA RTX 5080. Native runtime built with Visual Studio 2026 Build Tools/MSVC and the pinned RmlUi/FreeType sources. Managed applications built with a private .NET SDK 10.0.401 under the isolated QA root; system SDK/PATH configuration was not changed. Final managed packaging exited zero with no compiler warning lines.

GUI acceptance ran on the existing interactive desktop (session 1), through temporary one-shot tasks using the existing logged-in token. These were real Windows processes/windows, not SSH session-zero render attempts.

| Acceptance | Result |
| --- | --- |
| Clean ZIP extraction; complete ownership/hash check | Pass |
| Game and Studio each open/render, resize, and fully exit twice | Pass |
| Simultaneous distinct game/Studio executables and correctly titled windows | Pass |
| Both live processes load the exact packaged native RmlUi DLL | Pass |
| Studio owns its authenticated IPC endpoint; second launch forwards to original instance | Pass |
| Game closes while Studio remains alive and accepts another forwarded request | Pass |
| Studio closes cleanly; all package-owned files remain unchanged | Pass |

Final GUI receipt: **16 checks passed**. No ROM/gameplay test is implied by these launcher and process checks.

## Real update transactions

Every case used a fresh installation under a path containing spaces. The real updater waited for its parent before changing files. Successful transactions verified the incoming manifest and every installed owned file, both executable file versions, matching paired metadata, the actual auto-relaunched executable/window role, and clean process exits. Data fixtures covered settings, controls, game paths, saves, maps, replays, and unrelated files; all bytes were preserved. These are seeded fixtures, not a copy of the user’s real profile.

| Scenario | Result |
| --- | --- |
| Published legacy v0.1.46 → paired candidate v0.35.1 | Pass: actual legacy GUI closes; updated game automatically relaunches |
| Paired v0.35.0 → v0.35.1 initiated by the game | Pass: both application files update together; game relaunches |
| Paired v0.35.1 → v0.35.0 initiated by Studio | Pass: both application files downgrade together; actual Studio executable relaunches |
| Paired → older standalone payload | Pass: refused; original installation unchanged; original game restarts |
| Game closes for update while Studio is still running | Pass: refused without changing installation; Studio survives; original game restarts |

All **five scenarios / 44 assertions** passed. The v0.35.0 baseline was built from this work’s source and stamped separately for qualification; it is not a claim about a previously published paired build. The genuine legacy archive SHA-256 is `74a9abb0679f294ac00ea14ec76d5426b4badd520df3d360899fd593ea519324`.

Standalone releases predate the application split. Their in-place downgrade is deliberately blocked; extract one into a **separate folder** to retain a coherent paired installation. Current paired versions support upgrade and downgrade as a unit.

## Fixes and supporting checks

- Added a dedicated Windows Studio GUI apphost that invokes the shared entry point on the same STA thread and preserves arguments and exit handling.
- Game, Studio, and Windows detached exports hold a shared installation lifetime. An updater needs exclusive access. Companion-role preflight blocks staging/handoff with a clear close-the-other-app message.
- A companion launch no longer cleans the other application’s in-progress staging/helper. Routine recovery avoids opening an exclusive lock when no transaction exists.
- Pair metadata requires matching nonempty Game/Studio versions, supported metadata/IPC versions, bounded size, and ownership coverage. Downgrade relaunch selects the separate Studio executable when appropriate.
- Added a Windows-native paired packaging helper and wired CI/release packaging to retain both heads and hidden metadata. Workflow YAML parsed and a separate read-only review found no material path/version/artifact issues. GitHub Actions itself was not run or published in this task.
- Windows source-linked updater suite: **93 PASS, 0 FAIL**, exit 0. Includes archive staging/cancellation failures, transaction fault rollback/recovery, tamper rejection, user-file preservation, helper failure/hang, shared installation lifetime, both role probes, actual Windows Launch preflight, and malformed pair metadata.
- Mac source-linked updater suite: **95 PASS**, with the Windows-only Launch preflight explicitly deferred there. Release-manifest writer unit tests: **4 PASS**.

Network catalog/download selection was covered by source-linked tests. The actual desktop matrix invoked production updater helpers against local staged payloads; it does not claim an end-to-end download from a newly published GitHub release.

## Reproducible receipts and cleanup

Local receipts are under `artifacts/windows-install-verification-20261019/`: `windows-final-result.json`, `transactions-final-result.json`, `update-cpu-windows-final.log`, build result/log files, source hash manifests, transaction/window drivers, and delivery hashes. Candidate-build2 is the delivered package; candidate-build1 is historical diagnostic output only. `post-build-test-delta.json` records the test-only Windows preflight assertion copied after the production candidate was built.

Remote QA root: `C:\Users\Jarrett\Documents\ProjectPrime-Windows-QA-20261019-102314`. All tests used this isolated tree and owned processes. Temporary acceptance tasks were removed and zero QA-root processes remained at completion. Existing installations and real user data were not replaced. Private build tooling and receipts remain under the QA root for review. No release was uploaded, published, or committed by this task.
