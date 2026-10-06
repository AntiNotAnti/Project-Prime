# Project Prime audit evidence

Main snapshot: `a68627d0f7299c873cce9ad8046afbc6af0c46d5`, October 5, 2026 America/Chicago. The audit used the clean managed checkout at `/Users/jarrett/.codex/worktrees/engineering-audit/Prime Hunters Online`. Concurrent user/agent product changes in the original workspace were not modified. Remote main still matched the snapshot on the final check; ongoing work may resolve these findings later.

The report is [Project Prime engineering audit](../project-prime-engineering-audit-2026-10-05.md). The report is the consolidated interpretation; module notes retain detailed source reasoning and validation limits.

## Build distinction

- `prime-audit-build.txt` and `prime-audit-build-confirm.txt`: ordinary desktop main fails six compiler errors. The latter forced rebuild avoids incremental reuse of supplemental outputs.
- `prime-audit-server-build.txt`: unmodified server conditional build passes with 37 warnings, no errors. Acceptance source is excluded in this branch.
- `prime-audit-supplemental-build.txt`: desktop engine checks made executable only through **external compile-only substitutes for two acceptance-check files**. `prime-audit-validation/` archives those copies and the MSBuild override. The actual engine implementation and all tracked product source were unchanged. These are investigation tools, not a shipping patch and not evidence that ordinary main builds.
- Independent updater/pacing/timeline/semantic/platform/Python checks do not require compiling the ordinary desktop product.

SDK used: `/Users/jarrett/.dotnet/dotnet` (10.0.401). The shell-default `/usr/local/share/dotnet/dotnet` has no SDK. Python image tests used the bundled interpreter with Pillow; system Python's missing Pillow was an environment issue, not a product defect.

## Logs

Console logs are archived with a `.txt` suffix so repository build-log ignore rules do not hide the evidence.

| Evidence | Files | Qualification |
|---|---|---|
| Updater, process wait and local path containment | `prime-audit-updatecheck.txt`, `prime-audit-update-wait-repro.txt`, `prime-audit-manifest-repro.txt` | Benign reflection against unmodified server assembly; disposable temporary files and a sleeping local process. |
| Collision growth | `prime-audit-collision-pool-repro.txt` | Ten calls against unmodified server assembly; count 0→20,480. Scene-byte retention was not measured. |
| Real server refusal and replay formats | `prime-audit-real-server-smoke.txt`, `prime-audit-replay-format.txt` | Unmodified server assembly; refusal status 1 and empty directory, 2,888 format assertions. |
| Pacing/semantic/timeline/platform checks | `prime-audit-desktop-pacing.txt`, `prime-audit-android-pacing.txt`, `prime-audit-match-events.txt`, `prime-audit-replay-timeline.txt`, `prime-audit-platform.txt` | Independent source-linked tools. |
| Network/control/render/input contracts | `prime-audit-net-*.txt`, `prime-audit-rendergraph.txt`, `prime-audit-renderbackend.txt`, `prime-audit-frametiming.txt`, `prime-audit-pointer.txt`, `prime-audit-character-model.txt` | Supplemental acceptance-only compile substitution; engine unchanged. No asset-backed full match or native performance conclusion. |
| Remaining check failures | `prime-audit-gamepad.txt`, `prime-audit-replay-control.txt`, `prime-audit-replay-control-confirm.txt`, `prime-audit-macos-tools.txt` | Stale fixture expectations/packaging positive documented in report. Later assertions in stopped suites remain unverified. |
| Render parity comparator | `prime-audit-render-parity-tests.txt` | Eight Python tests. GPU arithmetic models in `prime-render-repro.py` are not native timing/capture. |

Focused community/editor/model check outcomes and four separate reproductions are recorded in `prime-audit-community-ui.md`; no standalone full console log was retained for those child-agent runs. Reproduction source is archived in `prime-audit-repros/`. Results use the same supplemental engine qualification.

The synthetic smoke-descendant result was observed in the tool session rather than a retained standalone log: the script failed and a deliberately long-running local descendant remained alive. It was terminated after observation. The full source-based ownership chain is in the report. This result does not imply the real prerequisite refusal test failed.

## Source notes and reproduction source

- `prime-audit-network-replay.md`: combat, hosting, directory, world/reader/retention paths and narrow cleanup candidates. Defensive source review only; no attack client or abuse demonstration.
- `prime-audit-gameplay-coverage.md`: bots, spectator, JIP, waitlist/rematch and server call-path coverage, sound safeguards, remaining asset/native evidence limits.
- `prime-audit-render.md`: platform capability matrix, scene/surface/atlas/Hi-Z/mip findings and input trace.
- `prime-audit-community-ui.md`: map publication, catalog/upload/auth/UI findings and source protections.
- `prime-audit-root-notes.md`: intermediate root evidence notes, qualified by the final report and cross-check corrections below.
- `source-anchors.tsv`: 163 unique main-snapshot file/line anchors. All were checked to exist and be within file bounds before the report links were generated.
- `prime-audit-repro/`: pool, updater wait and neighboring local-manifest file deletion in private fixtures, with references to the unmodified server output.
- `prime-audit-repros/`: valid flipbook export, near-budget catalog reload, held-preparation publication, stale resumable sessions on loopback.
- `prime-render-repro.py`: exact-source arithmetic and assumed-size allocator models; no real GPU memory/driver measurements.

Archived projects refer to the original audit build paths and must be deliberately adapted to a fresh clean checkout if rerun. They do not include game assets or build binaries. Do not install the supplemental override into normal builds.

## Cross-check corrections

Collision queue capacity grows permanently; stale entity references can retain a scene **until that candidate is reused**. Updater modification after timeout is established; Linux may refuse executable replacement while earlier library/data changes already occurred. The renderer harness measures CPU-side scene draw durations that can include implicit driver waits; Present/modern submission is outside that timer. macOS's positive wrapper specifically requires **wgpu and KTX** beyond OpenAL; MoltenVK is optional in this gate.

Native DX12/Vulkan/Metal performance, physical Android lifecycle/thermal behavior, deployed Supabase configuration and multi-hour gameplay/replay soaks were not run. No performance percentage or production exploit outcome is claimed.

## Open PR finalization

PR #347 opened during finalization; reviewed head `7f1716391b1ec87dff4f08fe5264abd54992ecc9`, based on audited main. `prime-audit-pr347.diff` freezes that diff. The report distinguishes its three additional source-level findings from main. Full build/native proof was not run.
