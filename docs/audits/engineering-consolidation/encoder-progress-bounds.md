# Streaming encoder frame progress

Phase 6/9; baseline `91b1c0c6c44e8c90effd250729709f791a84765e`, branch `codex/consolidation-encoder-progress`. Evidence dependency #489; conceptual native lifecycle/export dependencies #492/#494/#495, scene ownership #501 and bounded export queue #503. This main-based branch compiles independently. Capability stderr #511 is a separate concern.

## Purpose and ownership
`ReplayEncoderJob` starts an external PATH encoder off the presentation thread, redirects both pipes, and retains ownership through process exit and pipe completion. Its stdout consumer previously used `ReadLineAsync`, even for discarded lines. A generated 16 MiB unterminated line allocated approximately 100 MiB before it could be ignored. This is an external-output allocation finding, not an arbitrary archive/network exploit or a product content-limit proposal.

`ReplayEncoderProgress.Read` uses one 2,048-character buffer and scalar line state. It recognizes only the exact `frame=` prefix and the same invariant signed integer values. It preserves ASCII whitespace/signs, arbitrarily many leading zeros, signed limits/overflow rejection, terminal NUL handling, CR/LF/CRLF and unterminated EOF. The scalar parser's choices match [.NET 10 integer parsing](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Number.Parsing.cs), with differential fixtures against the original `int.TryParse` call. No line/content length rejection is added.

The existing worker publishes each accepted value with `Volatile.Write`; it owns the reader/process. The helper neither closes the reader nor catches read failures. Cancellation signaling, process-tree kill/wait, stderr tail, queue admission, timing, replay schema and frame generation remain unchanged. No blocking disposal moves to rendering or callbacks.

## Before / after evidence
Host macOS arm64, SDK 10.0.401. Source-linked fixtures; no subprocess, GL or native audio startup. Generated streams create bytes lazily, so the measurement excludes any input-sized fixture buffer. After a small warmup, allocation delta uses `GC.GetTotalAllocatedBytes` during the read. The 1 MiB assertion is a regression threshold, not a valid-output cap or RSS measurement.

| Check | Baseline | Candidate |
|---|---|---|
| Fragmented integer/prefix/overflow/Unicode/NUL/newline/EOF equivalence and caller ownership | 24 PASS | 24 PASS |
| 10,000 deterministic differential lines, every publication in order | PASS | PASS |
| Mid-read IOException and cancellation: prior frame retained, fault propagated, reader still caller-owned | 4 PASS | 4 PASS |
| Generated 16 MiB unknown final line: complete drain | PASS | PASS |
| Unknown line working allocation | FAIL: 100,663,448 bytes | PASS: 4,184 bytes |
| Generated 16 MiB valid whitespace/leading-zero final frame=12 | PASS | PASS |
| Valid long progress working allocation | FAIL: 100,665,176 bytes | PASS: 4,224 bytes |
| Total | 31 PASS / 2 FAIL | 33 PASS / 0 FAIL |

Desktop Release: PASS, 30 warnings in unchanged source / 0 errors. Replay-format check: PASS 3,034, including historical versions, malformed records, CRC/recovery/extraction/backpressure. Diff and workflow YAML checks: PASS. CI runs the source-linked tool in the existing renderer policy job; its remote result is pending publication.

```sh
# Baseline intentionally exits 1 on the two allocation regressions.
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-progress-check -c Release -- --baseline
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-progress-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
PROJECT_PRIME_USER_DATA="$PWD/artifacts/progress-check-user-data" /Users/jarrett/.dotnet/dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -replayformatcheck
```

Machine log hash index: [encoder-progress-evidence.json](encoder-progress-evidence.json). Raw logs remain under `artifacts/final-completion-pass-20261013/encoder-progress` in the managed worktree; retail assets are neither used nor committed.

## Risks, remaining gates and rollback
The scalar parser is deliberately limited to this existing frame integer protocol; it is not a general number parser. Differential corpus/fragmentation tests cover compatibility independently of the implementation. Any future number-style/culture change must update these tests.

Actual FFmpeg progress/cancellation/media output, Windows/Linux subprocesses and rendered replay export are NOT_RUN in this slice pending the coordinated native/encoder slot. Stream fault cancellation tests establish propagation/reader ownership only, not process termination. Capability stdout still uses `ReadToEndAsync`; inherited pipe handles and unsuccessful kill/wait remain independent lifetime gates. This change does not claim full encoder hardening, replay audio export, physical Android validation, native boundedness or FPS/RSS improvement.

Rollback: revert this commit; no settings, schema or migration changes. Preserve the independently reviewed queue/probe stderr ownership fixes during integration.
