# Encoder cancellation after parent exit

Phase 6/9. Parent dependency #516 (`0c55a955cc10b4b534e869b1e6f35e18cbce1d7e`); branch `codex/consolidation-encoder-pipe-cancel` is stacked on its bounded progress helper. Evidence #489 and conceptual runtime lifecycle dependencies #492/#494/#495/#501/#503 remain. This slice changes reader retirement, not process supervision or audio capture/mux.

## Confirmed ownership failure and changed contract
An actual synthetic encoder exits zero after starting a child that inherits stdout/stderr. The parent has exited, but both readers await that child's eventual EOF. Previously `Cancel()` could not interrupt the subsequent untokened `Task.WhenAll`; the job stayed active until the independent child closed the pipes. The fixture observes the parent gone and child alive before cancellation; an explicit watchdog witnesses the failure without waiting indefinitely.

The worker now gives both readers a private cancellation token. Process cancellation retains the original kill/wait path. **Only after the owned process exits**, cancellation interrupts the remaining reads, awaits both reader tasks' retirement, closes the owned readers on the worker, writes the log and returns the same available exit/result/progress/tail information. Normal completion still drains through EOF. Cancellation after exit may stop reading bytes a descendant writes later; it retires this job's pipe ownership rather than requiring that separate process to close its inherited handles.

`Cancel()` still only signals; no blocking close/wait moves to rendering, polling or callbacks. The progress helper remains fixed-buffer/scalar and does not own/dispose its caller's reader. Read I/O faults still reach the existing job failure boundary. Closure is in a `finally` after the combined read task retires, so a concurrent read IOException still closes both readers before reaching the existing outer failed-result boundary. The tail remains16KiB; command/log/PNG/user output files remain preserved.

## Actual before / after evidence (initial19-check and reviewed21-check fixtures)
SDK10.0.401, macOS arm64. Source-linked tool launches its own C# executable, so success/failure/active cancellation/inherited-pipe cases need no shell quoting dependency, FFmpeg, game data, GL or native audio. Its long-line child generates16MiB from an8KiB buffer. The fixture kills/waits only its explicitly recorded synthetic children; executable-path checks avoid treating unrelated process IDs as owned.

| Check | Baseline #516 | Candidate |
|---|---|---|
| Success/failure/16MiB long line: result, command/log and progress/tail | 9 PASS | 9 PASS |
| Missing encoder explicit failure | PASS | PASS |
| Active cancel: actual running child, kill/wait and available log | 3 PASS | 3 PASS |
| Exited parent/live inherited-pipe child observed | PASS | PASS |
| Cancel completion before the1s fixture watchdog | FAIL | PASS |
| Orphan child remains independently alive until explicit fixture cleanup | PASS | PASS |
| Cancelled=true/exit0/error=null/frame9 and available tail/log preserved | 2 PASS | 2 PASS |
| Explicit child cleanup | PASS | PASS |
| Total | 18 PASS /1 FAIL | 19 PASS /0 FAIL |

Root review added two read-fault ownership checks. Actual pending StreamReader reads inject IOException on one cancellation and cancel the other; the original70747bd6 post-exit pattern propagates the fault but skips closure (source-equivalent20PASS/1FAIL including the19 child checks). The revised retirement helper propagates IOException and closes both streams only after both reads complete: final21PASS/0FAIL. This is an I/O ownership test, not a mirrored expected-value parser test.

The watchdog is a test assertion, not a new product process deadline. Baseline uses the exact #516 job source with the final fixture via the tool's optional `EncoderJobSource` build property. Both versions preserve cleanup even when the assertion fails. Initial failed shell witness on frozen integration and initial/final C# trials remain in machine artifacts.

Additional candidate gates: source-linked bounded progress33PASS, including10,000 differential lines and generated valid/unknown long-line allocation checks; Desktop Release PASS30 unchanged warnings/0 errors; workflow YAML/diff PASS. An independent actual installed FFmpeg test returned multiple encoder choices beyond fallback, encoded synthetic128×96 video30frames plus48kHz AAC, verified streams/count/rate with ffprobe, observed frame30 and cancelled an active software encoder through process/pipe completion:8PASS. That synthetic A/V fixture tests encoding only, not gameplay audio or renderer media parity. Minimal manifest stub is an argument seam, not the production serializer.

```sh
# Characterize the exact parent job; intentional18PASS/1FAIL.
git show 0c55a955cc10b4b534e869b1e6f35e18cbce1d7e:src/MphRead/Mods/Replay/ReplayEncoderJob.cs > /tmp/ReplayEncoderJob.baseline.cs
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-pipe-check -c Release -p:EncoderJobSource=/tmp/ReplayEncoderJob.baseline.cs
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-pipe-check -c Release
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-pipe-check -c Release -- --baseline-reader-fault
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-progress-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
```

CI runs the real synthetic-child fixture in the existing Linux/Windows/macOS tool matrix. Remote results remain pending publication. [Machine evidence hash index](encoder-pipe-evidence.json); raw logs/harness/output remain in managed `consolidation-encoder-pipe-cancel`, `artifacts/final-completion-pass-20261013/encoder-pipes`.

## Limitations and rollback
This does **not** terminate a child whose parent has already exited, establish process-tree closure, or add a general process supervisor. The fixture intentionally proves that the orphan remains alive until test cleanup. If OS termination fails while the owned process remains alive, the original wait-for-exit can still remain pending; this slice deliberately retains that ownership/safety policy. Capability discovery has its own stdout and inherited-handle lifetime gates. Windows/Linux actual child and pipe retirement require the new remote matrix; local evidence is macOS only. No whole encoder hardening, native-memory/FPS bound, Android physical coverage or end-to-end audio export claim.

Rollback: revert this child commit while retaining #516's progress helper. No protocol/replay/settings/content cap or schema changes.
