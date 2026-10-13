# Encoder capability probe discarded-stderr allocation

Baseline `91b1c0c6c44e8c90effd250729709f791a84765e`; branch `codex/consolidation-encoder-error-drain`; Phase 6/9. Evidence baseline #489; conceptual real media lifecycle dependencies #492/#494/#495 and applicable scene seams, no code prerequisite. Independent main-based compile.

## Purpose and source ownership
`ReplayEncoderCapabilities.Probe` starts PATH `ffmpeg -hide_banner -encoders` with both pipes redirected. Stdout determines the finite encoder choices; stderr is awaited but its string is discarded. The original `ReadToEndAsync` nevertheless accumulated all stderr output. A malformed or noisy external process could therefore allocate output-sized strings during this probe. This is an external-process robustness finding, not an arbitrary-archive or network exploit claim.

The stderr task now drains the same StreamReader using one 2,048-character array, without retaining an output-sized string. It still starts alongside stdout before wait, consumes through EOF, preserves StreamReader Unicode decoding and caller/process ownership, and propagates the same I/O failures into the existing probe fallback. No output-length rejection, encoder-list parsing/content change, timeout/cancellation/kill change, progress parser change, or render-thread disposal is introduced.

## Before / after evidence
Standalone source-linked drain tool only, no external encoder/audio/GL/game assets. The baseline helper reproduces the exact discarded `ReadToEndAsync` behavior. A generated 16 MiB no-newline byte stream does not allocate an input-sized test buffer.

| Check | Before | Candidate |
|---|---|---|
| Empty stream, caller retains reader/stream | PASS | PASS |
| Fragmented UTF-8/Unicode and unterminated final text, consume exact EOF | PASS | PASS |
| 16 MiB generated stderr allocation threshold | FAIL: 67,283,248 allocated bytes | PASS: 4,120 allocated bytes |
| Mid-read IOException propagates to existing fallback boundary | PASS | PASS |
| Total | 3 PASS / 1 FAIL | 4 PASS / 0 FAIL |

The 1 MiB assertion is a regression allocation threshold for this fixture, not a product output/content limit. Allocation delta uses GC.GetTotalAllocatedBytes after a small warmup and includes managed allocations during the drain. It is not RSS/process/native memory or a CPU throughput measurement.

Compatibility: stderr was already intentionally discarded; it remains discarded after complete read. Stdout/finite Allowed selection and default libx264 fallback, existing error catch, five-second wait and worker/process ownership remain unchanged. Actual process integration is NOT_RUN because the parent owns the native/external encoder campaign slot. The tool does not evaluate the capability probe's lazy process startup.

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-error-drain-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
git diff --check
```

Desktop Release build: PASS, 30 warnings in unchanged source / 0 errors. Workflow YAML and diff checks PASS. CI runs the device-free source-linked fixture in the renderer lifecycle policy job. Keep independent SFX/Music/queue steps when integrating shared workflow anchors.

## Remaining gates, risk and rollback
Stdout capability ReadToEnd and per-job progress ReadLine remain unbounded accumulation; a future fix needs streaming finite-token/integer recognition with exact known-output parity, malformed/fragmented/long-output fixtures and device-free allocation witnesses. Do not infer this stderr fix establishes all encoder pipe bounds.

Cancellation kill failures and inherited pipe handles can delay completion; a separate coordinated process fixture must prove termination/EOF ownership before altering kill/wait semantics. Actual encoder-list discovery, media render/export identity/audio/video parity and Windows/Linux physical subprocess acceptance: NOT_RUN.

Rollback: revert this commit. No settings, sidecar/replay schema or persisted migration. No FPS/RSS/native leak claim.
