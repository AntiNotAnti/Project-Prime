# FPS-03: remove a proven diagnostic I/O hitch

All five Windows raw captures contain a 42–45 ms interval at simulation tick 242,
with about 40 ms unattributed gap, zero GC observations and ~0.025 ms swap wall
time. The harness's measurement boundary hashes its loaded DLLs after the raw
profiler begins collecting. Its callback timing baseline excluded that work,
but the profiler's actual Present-to-Present stream correctly observed the stall.
This is a diagnostic-induced hitch, not an ordinary gameplay renderer bottleneck.

Loaded client/harness SHA-256 and PE/loaded-MVID binding checks now run before
warmup and before the harness wall clock starts. Actual measurement-start/end
simulation ticks, RNG and roster observations stay at their original boundaries.
Failure reporting and source identity checks remain intact. The harness-only
`-late-provenance-control` switch reproduces the original phase for a same-binary
control, records `binaryIdentityPhase`, and cannot qualify normal timing. It is
rejected for screenshot, pose, resource and transition modes. No player/client
feature switch, gameplay/render change or settings migration is added.

Focused production-receipt tests verify phase, actual tick/RNG boundary readings,
SHA/MVID binding and control ineligibility. Full native-harness Release build
passes with zero warnings/errors. The Windows same-binary comparison and corrected
sustained receipts are recorded with their frozen identities in the final decision.
Do not count artifact removal as an unprofiled game FPS or photon-latency gain.

Rollback: revert this PR; `-late-provenance-control` retains the original timing
only as a diagnostic reproduction. Earlier raw runs and the stopped pre-soak
protocol attempt remain preserved with explicit exclusions.

## Windows same-binary result

All ten arms completed with one loaded client/harness MVID pair, zero telemetry
loss, zero dropped simulation steps and no focus transitions. Late controls have
40.3277 ms median maximum boundary gap; early identity has 1.148 ms. This verifies
removal of the known diagnostic artifact. It is not an unprofiled game-FPS gain.
Progress queries summarized completed captures in two windows, so these arms
must not qualify general renderer performance. The independent sustained run uses
only lightweight progress/process observations.

The [receipt index](evidence/provenance-controls/index.json) includes every arm,
source/binary identities, raw SHA-256 and compressed receipts. Each gzip restores
the original bytes; PowerShell stdout logs retain UTF-16. No game images/assets
are included. The corrected 30-minute campaign uses the same frozen source and
records its own functional/performance limits separately.
