# Netcode 2.0 N0 validation — 2026-10-10

**N0 is blocked; this is an unmerged baseline/harness slice.** The capture and
adjudicator expose failures instead of turning a successful process exit into a
netcode pass. No dependent slice or gameplay fix is included. Read the
[runbook](netcode2-n0.md) for commands, flags, source mapping and rollback.

## Identity and scope

The control is restored main `468bbbf027148ce94901dfa8bf138d16a4498c03`, descended
from v0.1.47 `0351ef6f707ab0369c95d8d4185869f6e02622f8`. Main was checked again
before submission. No historical #410–#423 code was imported. Protocol **41**,
wire/replay layouts and gameplay authority are unchanged. Diagnostics are off
unless `PRIME_NET2_EVIDENCE` is set; sustained scripted inputs also require
`PRIME_NET2_SOAK=1`.

Only the owner's Mac and Windows computer were used. Mac: Apple M4 Pro,
macOS 27 arm64, SDK 10.0.401/runtime 10.0.12, Python 3.9. Windows: build
10.0.26200 x64, SDK 10.0.400/runtime 10.0.12, Python 3.14.2, GTX 1660.
All 3,803 extracted AMHE1 asset file identities and contents match across hosts;
manifest byte hashes differ because of ordering/formatting. Assets and raw game
recordings stay on the owner's computers.

The final C# build is `2c62508161a9328eae756e130a13a9d4ce068685`. The earlier
`aeff2306` campaigns predate the rig readiness/observer correction; Windows's
asset-free and scene suite run uses `2a1bba72`. Those differences are retained
per attempt. Subsequent changes affect Python adjudication/isolation and docs.
The final adjudicator was tested on both hosts and rerun against preserved raw
campaign evidence without replacing the original summaries.

| Loaded engine | SHA-256 |
| --- | --- |
| Mac control | `288de6d894b207fa220fd6ee21a20201286845a603baa44bf7d658f7e00db602` |
| Mac final candidate | `2a43c936057cc9aac380e1d0fc47b9c6a766af4a84d5eaee742d20dc1f6c15c9` |
| Windows control | `e3e66bfb8899b1b43857552e09f11403eb0eb6ac4366a9ac59cf5197b2084c63` |
| Windows final candidate | `f92c3c9e54a238a1b421be6d944a04cb72772eb824c75f79b40162e456cd495e` |

Windows builds came from verified Git source archives, so their informational
version is `local`; the archive hash, explicit source SHA and loaded DLL hash
are the identity. Frozen control's old benchmark reads the current checkout's
SHA, which is misleading when running an older DLL. Its frozen build manifest
is authoritative. The new benchmark records loaded assembly identity.

## Build and suite results

| Check | Mac | Windows |
| --- | --- | --- |
| Release nettest/engine build | PASS | PASS; initial MSBuild worker failure preserved, single-worker retry passed |
| Final false-green harness regressions | 30 PASS | 30 PASS |
| Existing asset-free suites | 27/27 PASS for control and candidate | 10/10 PASS for control and candidate |
| Combat scene | Baseline and candidate FAIL | Baseline and candidate FAIL |
| Continuous scene | Baseline and candidate FAIL | Baseline and candidate FAIL |
| Claim-load, geometry, bootstrap scenes | Baseline and candidate PASS | Baseline and candidate PASS |
| Bots online scene | Candidate PASS | Candidate PASS |
| Extended virtual impairment benchmark | Control and candidate 2,016/2,016 PASS | Control TIMEOUT at 360 s; no completed extended comparison |
| Default virtual impairment benchmark | Covered by extended matrix | Control and candidate 16/16 PASS |
| Content-free semantic event checks | 47 PASS | NOT_RUN |
| Three-view replay format validation | 3/3 PASS | 3/3 PASS |
| CI / Linux / Android | NOT_RUN | NOT_RUN |

CI was intentionally skipped to honor the Mac/Windows-only test restriction.
No release/package validation or native replay playback pass is claimed.
Windows rendered processes require the active desktop: the SSH service session
has no primary display. The successful recordings used a temporary limited-user
interactive scheduled task, which was removed after completion.

## Independent real-UDP evidence

All listed cases use separate server/client processes, loopback UDP and the
lossless `lan` profile. Impaired virtual coverage does not prove impaired real-UDP
combat. Health comparisons join complete player identity at the same authority
frame. A missing view commit is not automatically a visible missing projectile.

| Candidate campaign | Offered / authority accepted / authority committed | Matching health samples | Result |
| --- | --- | --- | --- |
| Mac 2 peers, missiles, 60 s | 66 / 66 / 66 | 2,393 | FAIL: two shutdown timeout logs |
| Windows 2 peers, missiles, 60 s (`aeff2306`) | 75 / 75 / 75 | 2,084 | FAIL: two shutdown timeout logs |
| Mac 4 peers, feature tour, 60 s | 588 / 588 / 587 | 9,306 | FAIL: one uncommitted offered shot; one authority shot absent from all four view records; shutdown timeouts |
| Windows 4 peers, feature tour, 60 s (`aeff2306`) | 577 / 577 / 485 | 7,792 | FAIL: 92 uncommitted offered shots; 21 missing view commits in aggregate; timeout |
| Mac shooter/victim/spectator, 60 s | 79 / 79 / 79 | 5,388 | FAIL: three shutdown timeout logs |
| Windows shooter/victim/spectator, 60 s | 74 / 74 / 74 | 4,921 | FAIL: one shot absent from victim and spectator commit records |
| Windows 8 peers, Shock Coil, 60 s (`aeff2306`) | 27,448 / 27,448 / 138 | 602 | FAIL: uncommitted offers, queue drops, shutdown timeouts; server advanced only 291 steps |
| Mac 8-slot soak, 1,206.7 s elapsed (`aeff2306`) | 6,234 / 5,148 / 5,100 | 17,988 | FAIL: early reliable disconnects/re-announcements, queue drops, shot losses and 3,005 missing semantic facts |

There were **zero health mismatches** among these matched samples; sampling
cannot establish every intermediate HP change or complete damage/kill parity.
The final rendered runs matched all expected semantic facts inside their
recorded comparison intervals. Mac produced 112 screenshots and three demos;
Windows produced 107 screenshots and three demos. Frames were sampled for basic
rendering sanity; they were not paired to establish same-projectile visibility.

The missing Windows rendered shot is
`(epoch=639272560644630077, match=2, slot=0, generation=1, life=1, shot=14)`.
It reached and committed on the authority but is absent in both other views.
This is an unresolved candidate observation; no equivalent structured control
capture exists to attribute its cause to N0 or claim a production fix.

The soak launched seven initial peers, a late join, and a fresh reconnect, and
observed map rotation. Most initial peers stopped useful simulation after early
connection failures. Bot occupancy and spectator coverage were not observed;
sustained eight-slot gameplay, precise Shock Coil drain/kill parity and
Imperialist spawn-trap coverage therefore remain incomplete or NOT_RUN. A
20-minute wall clock does not satisfy these gates. Evidence overflow was zero.

## Defect ledger

| ID | Classification and evidence | Disposition |
| --- | --- | --- |
| N0-B01 | Control and candidate `combat-scene`: `InvalidHitClaimIsRefused/non-affinity Battlehammer cannot borrow affinity impulse` | Baseline test failure; root cause not changed |
| N0-B02 | Control and candidate `continuous-scene`: `spectator rejected` | Baseline test failure; root cause not changed |
| N0-B03 | Unchanged Mac control, 8-peer Shock Coil: eight `reliable control failed` native log entries, followed by re-announcements | Reproduced without N0 diagnostics; reliable delivery failure blocks stable control |
| N0-O01 | Instrumented 4/8-peer and rendered Windows runs lose offered/observer shot commits | Unresolved; complete identities and intervals preserved for diagnosis |
| N0-O02 | Timeout entries during graceful peer shutdown on both platforms | Strict failure retained; distinguish teardown from in-match loss before considering any exemption |
| N0-O03 | Soak has 3,005 missing authority semantic facts, reliable failures and lost connections | Related delivery failure observed; exact old #423 semantic-history-overrun path NOT_REPRODUCED in this attempt (`SemanticDisconnects=0`, presentation-overrun false); neither a global refutation nor grounds to import the old patch |
| N0-H01 | Initial rendered rig ran unrelated feature-tour assertions | Harness corrected to explicit local evidence readiness plus mandatory cross-process adjudication; original failed recordings retained |
| N0-H02 | Initial Windows SSH rendering had no active primary display | Retried in the active desktop; original failure retained |
| N0-H03 | Frozen control predates evidence reports and graceful stop marker | Missing reports/forced cleanup are known harness capability gaps, not proof of a baseline gameplay defect |
| N0-H04 | Initial PowerShell wrapper stopped on expected diagnostic stderr; Windows parallel build worker failed; extended control benchmark exceeded 360 s | Original attempts retained; Python wrapper, single-worker build and bounded 16-scenario benchmark retries recorded separately |

## Performance and bandwidth

The virtual codec/impairment comparison produced identical packet, byte, drop,
coalescing and scheduler counts in all **2,016 Mac scenarios** and **16 Windows
scenarios**. Total virtual bytes sent were **11,876,094,720** in each Mac arm and
**99,803,320** in each Windows arm; maximum packet size was 1,199 bytes. This
measures the existing synthetic codec/queue workload, not live combat bandwidth.
Individual scenario identity and compared fields are recorded in the ledger.

Server reports retain p50/p95/p99/p99.9, worst step, allocation totals, per-map
segments and transport counters. For context, the failed final Mac rendered run
had step p50/p95/p99/p99.9 **0.13/0.29/0.53/7.77 ms**, and the failed Windows run
**1.23/2.54/3.47/18.97 ms**. These are different hosts/workloads, not an A/B
performance comparison. The failed Windows eight-peer run had p99 82.54 ms,
p99.9 515.40 ms, 74 dropped steps and 134 stalls. Most of the Mac soak's later
fast steps occurred after participants lost useful gameplay; those values must
not be reported as an optimization win.

The Mac's existing synthetic server benchmark also ran control/candidate pairs
with N0 diagnostics disabled: 300 warmup steps plus 1,800 measured steps, no UDP,
and legacy synthetic intents rather than the canonical fire-event workload.
Single-run values are descriptive, not statistically established improvements.

| Peers | Control / candidate mean (ms) | Control / candidate p99 (ms) | Allocated bytes, each arm |
| --- | --- | --- | --- |
| 2 | 0.0728 / 0.0718 | 0.1552 / 0.1523 | 2,659,264 |
| 4 | 0.1143 / 0.1131 | 0.2993 / 0.2852 | 2,996,352 |
| 8 | 0.2442 / 0.2428 | 0.7819 / 0.7604 | 3,677,728 |

These six synthetic runs had no step failures, dropped steps or stalls. Their
allocation totals match exactly; they do not measure the enabled evidence cost.

Representative canonical combat performance and the suggested <5% regression
gate remain **NOT_RUN**. This slice makes no bandwidth, latency or frame-time
improvement claim. The unchanged wire is the baseline for later work.

## Acceptance and retained evidence

| Gate | Status |
| --- | --- |
| Frozen source/runtime/assets/assemblies; protocol 41 | PASS |
| Bounded opt-in evidence and 30 false-green regression checks on both hosts | PASS |
| Stable lossless control | FAIL (N0-B01/B02/B03) |
| 2/4/8 real-UDP and shooter/victim/observer exact parity | FAIL |
| Full 20-minute eight-slot scenario coverage | INCOMPLETE |
| Semantic-history historical failure investigation | Related failures observed; exact old overrun path NOT_REPRODUCED in this attempt |
| Rendered same-projectile agreement / precise drain-kill / spawn trap | NOT_RUN |
| Representative combat performance acceptance | NOT_RUN |
| N1 and other dependent slices | BLOCKED |

The committed [compact evidence ledger](../tools/nettest/baselines/net2-n0-20261010.json)
contains per-attempt source/assembly hashes, counts, parity, step/transport
metrics, coverage and retained report hashes. Raw reports, original summaries,
commands/environment/runtime manifests, native logs, screenshots and recordings
remain under `artifacts/net2-n0/` in the Mac worktree; original Windows files
remain in the isolated `ProjectPrime-net2-n0-*` directories on that computer.
`evidence-index.json` and `net2-n0-evidence.tar.gz` provide a local integrity index
and compressed evidence copy, excluding binaries and extracted game assets.
They are not uploaded with the PR. Preserve these originals when fixing the
classified failures and rerun into new attempt directories.
