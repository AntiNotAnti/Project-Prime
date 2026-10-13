# Encoder capability stdout and probe lifetime

Phase 6/9; main baseline `b0d44a1e9578d350714221be9a73e17059501ec2`, branch `codex/consolidation-encoder-capability-probe`. Ordinary integration base: current main `9bd938a54c73a8871da7eca387ae883415468eb3`; the earlier b0d44a1e measurements remain baseline evidence only. Evidence dependency #489. Uses merged #511 discarded-stderr helper; #516 is related progress-parser evidence, not a code dependency. Merged #518 encoder-job cancellation is unchanged by this slice. Conceptual runtime dependencies #492/#494/#495/#501/#503/#515 do not make this an audiovisual acceptance gate.

## Proven boundary and implementation

The actual source-linked private `Probe()` launches a PATH executable with `-hide_banner -encoders`. It previously accumulated stdout with `ReadToEndAsync`, split every line/token and retained all second-token names in a HashSet before filtering the ten Allowed encoders. A synthetic owned subprocess producing a 16 MiB unterminated unknown line plus late valid lines allocated 101,586,264 managed bytes in the initial baseline. When its parent exited while its explicitly owned fixture child retained both pipes, Probe remained pending past the existing five-second budget. Initial baseline: 2 PASS / 2 FAIL; child cleanup released the old pending probe.

The scalar reader keeps a 2,048-character buffer, bounded column/position/match state and ten supported flags. It recognizes exactly the old `Split('\n')` then `Split(' ', RemoveEmptyEntries)` second token: literal space and LF only, ordinal case, CR/tab/NUL/Unicode preserved as token characters, exact normal EOF, duplicates ignored and Allowed order retained. First/second/third tokens may be arbitrarily long; unknown names are consumed without retention. No output/content limit or new encoder selection is added. `libx264` remains the fallback, and Apply is unchanged.

The original no-argument Probe and `Lazy<Task<...>>` / `Task.Run(Probe)` entry remain. The existing five-second CTS is armed before reads begin; a private reader CTS links to it. Both loops check cancellation even when reads repeatedly complete synchronously. The same timeout covers owned-parent waiting and the remaining stdout/stderr drain, not a fresh five seconds after exit. Existing active `Kill(true)` / untokened wait is preserved. Finally, both reads retire before both owned readers close; nested finally closes stderr even if stdout disposal faults. Reader IOException still reaches the existing fallback boundary. The one-argument stderr Drain/method-group contract remains through an overload.

Deadline behavior changes intentionally: late output beyond that budget may be excluded, and an unterminated second token is not published when cancellation cuts off its delimiting space/LF/EOF. Completed earlier tokens survive. Normal EOF grammar is unchanged. This is a lifetime budget, not a size cap. No close/wait work moves to a renderer or callback.

## Before / after and compatibility evidence

SDK 10.0.401, macOS arm64. Tests compile the production files directly; a minimal manifest argument record enables the unchanged Apply method. The actual probe uses an owned copy of the tool apphost named ffmpeg in its private build directory; PATH is scoped to the tool process and restored. No installed FFmpeg, GL or native audio device starts. The holder child inherits both descriptors, is confirmed alive after parent exit and after candidate completion, and is explicitly killed/waited by the fixture with executable identity verification. This does not claim orphan process-tree ownership by Probe.

| Check | Baseline | Candidate |
|---|---|---|
| Initial actual process witness | 2 PASS / 2 FAIL | 5 PASS |
| Expanded actual process cases: ordinary grammar/order/fallback, nonzero exit, large stderr, active kill/wait, immediate/delayed parent exit, late-before-budget names | Final 10 PASS / 3 FAIL; three failures are allocation plus immediate/delayed parent-exit reader waits | PASS |
| Warm actual 16 MiB stdout allocation | 101,340,400 bytes in expanded baseline | 411,912 bytes in final linked candidate |
| 10,000 differential lines, 1/2/7/2,048 byte fragments, LF/CR/CRLF, space/tab/NUL/Unicode/case/prefix/EOF | Original Split expression oracle | PASS |
| Generated 16 MiB first/second token, late valid/normal EOF names, complete drain | No size rejection allowed | 4,600 / 4,624 allocated bytes |
| Parser EOF/fault/cancellation leaves caller ownership | Contract | PASS |
| Synchronously ready stdout and stderr whose reader deliberately ignores tokens | Linked deadline must still be observed | PASS |
| Both reader IO-fault directions, other read held pending during cancellation | No early close; fault must propagate after retirement | PASS |
| Apply software/hardware/rejection and original Drain method group | Existing behavior | PASS |
| Final source-linked tool | Baseline process-only intentionally fails measured boundary regressions | 62 PASS / 0 FAIL |
| Existing error-drain regression | Compatibility | 4 PASS |

The process allocation threshold is 8 MiB to allow runtime/process/chunk overhead across hosts; generated-reader threshold is 1 MiB. Neither is a product cap or RSS/retained-heap measurement. Chunk/task allocation can scale with read count even though parser storage has a fixed bound. Initial and expanded logs remain separate. A strengthened fixture initially failed with a managed NullReferenceException from a public-only reflection lookup of internal Drain; the corrected lookup passed. That failure log is retained and is not classified as a product/native crash.

Fresh Desktop Release: 0 warnings / 0 errors. Source asset/OpenGL/map guards, YAML and diff results are recorded in [encoder-capability-evidence.json](encoder-capability-evidence.json). CI runs the actual subprocess and grammar tool in the existing Linux/Windows/macOS map-editor matrix. Fresh remote results are pending publication.

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-capability-check -c Release
/Users/jarrett/.dotnet/dotnet run --project tools/encoder-error-drain-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```

For baseline reproduction, extract the two baseline production files with `git show b0d44a1e:<path>` to a private directory, then set MSBuild `EncoderCapabilitiesSource` and `EncoderErrorDrainSource` to those absolute files and pass `--process-only`. The source/byte hash index preserves the exact copied originals.

## Remaining gates and rollback

Local actual child/process tests are macOS; Windows/Linux retirement requires the new actual CI matrix. Reader cancellation is supported on tested/runtime paths, not an unconditional bound on all possible OS calls. If the owned live process cannot be killed or waited, the original wait may still remain pending; an uncancellable OS pipe read can also delay retirement. Probe does not supervise or kill a descendant after its parent is gone. Direct helper fault injection proves retirement/closure; the Probe outer exception-to-fallback mapping is source-reviewed, not an injected real OS pipe-failure witness.

Existing exact rendered export FAIL remains separate: unchanged main/frozen/final controls reproduce the same one-pixel difference at source352. No renderer tolerance, JIT policy, audio mux, schema, user file or gameplay changes are made here. Actual replay audiovisual export, physical Android/display, FPS/RSS, and whole ingress safety are not claimed.

Rollback: revert this focused commit while preserving earlier stderr/progress and independent job cancellation changes. No settings or migration change.

## Windows fixture cleanup follow-up
Initial exact-head CI at `ad82281a`, run38430515244/job115411384326, passed all17 actual process assertions, then failed with UnauthorizedAccessException deleting its copied ffmpeg.exe before grammar checks. Linux/macOS capability steps passed. The negative raw/clean logs and hash receipt are preserved. [File.Delete documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.delete?view=net-10.0) lists executable-in-use and read-only files as possible causes; the log alone does not distinguish them.

The fixture verifies its last parent has exited, retains explicit holder-child identity/kill/wait cleanup, normalizes only the copied host's read-only attribute and retries deletion on Windows for at most a two-second retry budget. Persistent cleanup failures still throw. Runtime sources and ownership assertions are unchanged. Corrected local tool:62 PASS; fresh actual Windows CI is required, not waived. No unchanged client/gameplay reruns are implied by this fixture-only change.
