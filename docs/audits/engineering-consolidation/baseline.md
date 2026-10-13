# Engineering consolidation execution baseline

Baseline source: `91b1c0c6c44e8c90effd250729709f791a84765e`, fetched from `origin/main` on October 12, 2026 (America/Chicago). The primary checkout was clean and remains unchanged. [Architecture inventory](architecture-inventory.md) maps existing implementations, owners, feature reachability, ranked modules, warnings and every phase's prerequisites. [Evidence manifest](baseline-evidence.json) preserves exact commands, binary identity, settings, measurements and raw-log hashes; large local logs and captures stay under `artifacts/engineering-consolidation/`.

## Current-source reload result

The current binary completed 60 cycles / 120 scene arms, rotating multiplayer Cryochasm and Proving Ground in one actual macOS OpenGL context. Each arm advanced exactly 120 simulation steps. All five allocation-failure stages fired, RGBA/depth fallback comparisons were exact, repeated map hashes remained stable, retained generations invalidated, and every tracked cleanup counter returned to zero. Sixty Studio renderer sessions each rebuilt geometry/material caches three times and released their tracked objects. This tests renderer/cache ownership, not full Studio UI interaction.

| Sample | RSS MiB | GC committed MiB | Live managed estimate MiB |
| --- | ---: | ---: | ---: |
| First 10 arms | 704.87 | 675.72 | 580.58 |
| Last 10 arms | 529.55 | 289.19 | 217.56 |
| Before final drain | 525.03 | 291.17 | See manifest |
| After audio wait | 526.77 | 291.17 | See manifest |
| After diagnostic GC/finalizers/GL completion | 400.47 | 132.36 | See manifest |

All 120 distinct audio shutdown tasks completed, with zero faults/cancellations/backlog. The old report's steadily rising RSS is **not reproduced in this run**. This is not a measured improvement over that report: source/runtime and measurement conditions differ. Mac private memory is unavailable. RSS alone cannot distinguish driver caches, allocator retention, managed residency or true native leaks; no sustained quiescent plateau or VM allocation category was measured yet. The P0 boundedness question remains open. Concurrent compilation and one brief separate native shader characterization are disclosed in the manifest. Readbacks, reloads and forced drain are excluded from FPS evidence.

## Validation

| Check | Status | Evidence / limitation |
| --- | --- | --- |
| Desktop solution Release | PASS | SDK 10.0.401/runtime 10.0.12; 30 warnings, zero errors |
| Dedicated server Release | PASS | `-p:MphReadServer=true`; 22 warnings, zero errors |
| OpenGL-only guard | PASS | `bash tools/check-opengl-only.sh` |
| Existing real GL resource regression | PASS | `tools/render-resource-check`, failures=0 |
| Replay format regression | PASS | 3,034 checks; historic reader/bounds/CRC/storage cases |
| Network Python evidence contracts | PASS | 414 tests, one existing explicit skip |
| Baseline build/release CI | PASS | [Build](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38343443470), [release](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38343469903), both at the baseline SHA |
| Current binary 60-cycle lifecycle | PASS | Exact fallback/cleanup checks; native boundedness remains UNVERIFIED |
| Full pacing, 2/4/8-peer impairment, export/reconnect and physical Android/Windows/Linux GPU matrix | NOT_RUN | Separate follow-up gates; existing CI is not physical-device evidence |
| Native VM/allocation attribution and long quiescence | NOT_RUN | Controlled diagnostic arms are the next slice |

Warnings are 11 CS0162, 4 CS8602, 8 CS0618, 1 CS8600, 2 IL3000 and 4 CA2014 in the desktop build. Do not interpret tool incremental-build zero-warning output as elimination of these diagnostics.

## Review queue and gates

The independent [protocol/replay documentation correction #488](https://github.com/AntiNotAnti/Project-Prime/pull/488) changes no runtime bytes. The next Phase 1 PR adds controlled same-build map/GL, Studio, audio-only and combined isolation plus quiescent samples. Proven shader compile/link cleanup and SFX resource ownership defects are separate fixes, with failure-injection characterization. Neither is asserted to cause the historical RSS trend. Scene-transition GC policy stays unchanged until matched transition/memory measurements support a change.

Independent eligible families may proceed from this baseline: Enhanced Hunters live retirement with archive compatibility; scoped collision workspace; profile-supported AI scratch; failure/cancellation cleanup at ingress. UI migration depends on complete RmlUi route/platform acceptance and is not supplied by draft #424. Rendering optimizations need CPU attribution and exact visuals. Network ownership and replay/editor restructuring depend on the collision/scene ownership gate and existing open authority PRs #472–#474. Full integration/acceptance depends on reviewed eligible PRs and final platform evidence; this baseline does not claim those phases complete.

No repository settings were changed. A fresh GitHub branch query reports `main` unprotected; required essential checks and current-head approval are recommended in the inventory. Revert this documentation commit to roll back; runtime/data formats are unchanged.
