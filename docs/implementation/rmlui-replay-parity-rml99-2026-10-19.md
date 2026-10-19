# RML99 replay parity tracker

Date: 2026-10-19  
Status: **accepted and delivered for the tested Mac RmlUi restoration scope.**

This work follows the [accepted and delivered RML98 restoration](rmlui-regression-restoration-rml98-2026-10-19.md). Its frozen source, acceptance scope and delivered ZIP remain unchanged. RML99 acceptance is limited to the evidence below and does not establish blanket restoration of every old feature, hardware or performance coverage, or remote-service qualification.

## Following B playback

B follows A's playback and seek targets under the original recording source and stable replay owner. It has no independent clock. Coalesced private seeks use at most 120 simulation steps and a 4 ms update budget. Stale edit reviews retire separately from actual player teardown, allowing Play and seeks to retain the same private B owner while publishing only the exact reviewed frame pair.

Build1 inputs:

| Input | SHA-256 |
| --- | --- |
| Client `ProjectPrime.dll` | `39b9f3a0caba4947426e0e99f8fe251b1c6f21d28d11fa68f74bff60e4c7541f` |
| Native library; unchanged from RML98 | `84fdfbe0f7bcd1574aa7f8a0aeb1920ba28a4b4800f10aac35c5b58e381d57c2` |

The full actual Studio build1 journey passed in **21.5 seconds**, including the new B test: Play advances at least 30 frames; backward seek to frame 0, forward seek to frame 60 and restoration to frame 0 retain the same private owner and canonical equality. Receipt: `artifacts/rmlui-replay-parity-rml99-20261019/build1/acceptance/actual-following-b-960x600/execution.json`.

Separate source-linked B lifetime checks passed **408 assertions**. Receipt: `artifacts/rml98-comparison-lifetime/test1/source-and-results.json`. Those CPU/stub checks and the actual-window journey remain distinct evidence.

## Detached export and current qualification

Frozen **build5** client and isolated GL tool compile with zero warnings and errors. Qual7 actual-window Main and full Studio checks passed on the same product inputs; the product is unchanged from build5 through qual6/qual7. Both candidate1 apps passed packaged-window checks, extracted-app smoke tests, strict signed extracted inventories and the no-game-assets guard. The verified archive is delivered.

| Current input | SHA-256 |
| --- | --- |
| Build5 client `ProjectPrime.dll` | `508da3d6390952e59007d25a08cd255a3d1ac48590b0175fbf56ed01aaa72f92` |
| Native library; unchanged | `84fdfbe0f7bcd1574aa7f8a0aeb1920ba28a4b4800f10aac35c5b58e381d57c2` |

Export ticket, queue and worker code, the `ModEntry` CLI and the backend are wired. The implementation uses private copies verified by SHA, camera/vector JSON snapshots, a queue with one worker process, and revision-bound Cancel/Retry/Clear operations. Status and nonce are persisted. It reuses the existing renderer, audio path and encoder. The worker uses an isolated native window rather than Avalonia; queue lifetime is independent of the Studio page.

Original replay and WAV handles are released after private copies are prepared; the configured-files lease persists. `paths.txt` is hash-bound, and retry validates the original mappings and audio hashes. Logical job labels are retained. The scoped output and lifecycle checks below provide current evidence; broader recovery and platform behavior are not implied.

Diagnostic history remains preserved:

- Build3 passed **22 CPU ticket checks**. Its actual child process failed on the first draw at the camera ownership guard. The correction binds `EnsureTrack` before applying presentation state; the later qual7 actual export journey validates the corrected path.
- Build4 failed compilation on the unrelated shared `PlayerEntityRadar` `Headless` identifier. A concurrent thread corrected it to `Mods.Headless`; build5 then compiled cleanly. Build4 is not a passing export receipt.
- The failed build5/qual6 journeys remain diagnostic receipts for fixture ordering and reviewed-read issues. Fixture corrections in qual6/qual7 did not change the frozen build5 product; those failed runs are not relabeled as passes.

Qual7 actual-window evidence:

- Full Studio passed **209 steps** with true exit 0 in **96.9298529624939 seconds**. It produced **577 PNG frames at 1280×720 and 144 FPS**, with **570 distinct PNG hashes**. Closing the parent while the child was Rendering and reopening it preserved the detached job; the new parent's paused canonical frame 30 remained unchanged.
- Clear removed two Pending jobs while preserving the active job. Cancel reaped the exact child; Retry used new private input/output and completed **121 frames**.
- A separate three-frame export used actual FFmpeg to produce MP4 video and nonzero audio: **48,000 Hz stereo, 2,400 PCM frames**.
- Following-B/A playback and seeks, and the original full Studio feature journey, also passed. Receipt scope: `artifacts/rmlui-replay-parity-rml99-20261019/qualification7/acceptance/actual-detached-export-960x600`.
- Actual Main at 960×600 passed **353 steps** in **14.556 seconds**, including real AppKit queued chat, tab navigation and replay reopening. Receipt scope: `artifacts/rmlui-replay-parity-rml99-20261019/qualification7/acceptance/actual-main-960x600`.

The current source-linked export-owner actual-backend suite passed **58 CPU checks** under `artifacts/rml98-export-owner/test1`. The original 26 ticket and five legacy checks remain separate historical evidence. All four Studio layout configurations and the updated **23-check CPU ticket suite passed**.

## Packaged acceptance and delivery

Both candidate1 packaged-window journeys and both extracted-app smoke tests passed, along with strict signed extracted inventories and the no-game-assets guard. Packaged Studio export checks passed for three saved-camera frames and nine ordered Chase/Orbit/Director frames as a PNG sequence with audio. Visual review covered the persistent Studio workspace and the packaged saved-track scene. Earlier failed receipts remain preserved as diagnostic history.

[ProjectPrime-RmlUi-macOS-arm64-rml99-20261019.zip](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml99-20261019.zip) is **185,621,013 bytes**, SHA-256 `24a432ea2e5d8f7b785a54ea593be2df8197f46401887521a17bf9761cff68dc`. Delivery is recorded in `artifacts/ProjectPrime-RmlUi-macOS-arm64-rml99-candidate1-20261019/downloads-delivery.json`. Delivered product inputs retain the build5 client and unchanged native hashes above.

This accepts the tested Mac RmlUi restoration and following-B/detached-export behavior. It does not claim broad hardware, performance, remote-service, Android, Windows, Linux or full-campaign qualification. Map Studio remains intentionally Coming soon. RML98's separate frozen source and delivered archive remain unchanged.
