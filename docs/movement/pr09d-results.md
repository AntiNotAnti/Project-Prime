# PR-09d — actual team/objective compatibility

**Scoped PASS; release acceptance remains incomplete.** This slice adds a genuine
team/objective UDP fixture on main
`46c101cdb7a4a25e2e8fb651181610d733b1fe11`. No production gameplay, movement,
protocol or replay source changes. Classic remains the default.

## What changed

The optional team worker uses the existing human-owner combat worker and real
persistent lobby commands. It adds two actual server bots, checks the accepted
four-slot roster, balanced teams, bot difficulty, frozen mode/model and native
owner team, then observes received authority worlds after ordinary simulation.
Every observed node must exactly match its native client entity; life-aware
capturer resolution follows the production projection. No positions, velocities,
health, captures, teams or readiness are written by the fixture.

Nodes and Defender use distinct native contracts. Nodes still requires completed
capture counters plus authority/client points. Defender requires an uncontested
authority owning team, positive authority per-player objective seconds, and
independently replicated client team time. Team time is carried by snapshot
time sync, not the objective-world record; the fixture makes no same-frame
equality claim across these score streams. Existing nonteam worker/server
defaults and bot difficulty remain unchanged. Required team CLI duration/seed
arguments reject malformed values.

## Actual results

The fixed additional DefenderTeams case uses MP3 PROVING GROUND, two independent
AI-driven human owners on one team against two level-three server bots, 45 seconds
per model, configured 200 ms RTT / 20 ms jitter and no added loss/reorder/duplication.
Both client pairs observe 451 distinct accepted worlds each and share 128
identical bounded authority node facts, including a real owning-team witness.
All six processes exit 0.

| Model | Positive owning-node samples per owner | Authority objective-seconds worlds per owner | Client score worlds per owner | Owner solver steps |
| --- | --- | --- | --- | --- |
| Native | 88 / 88 | 346 / 346 | 426 / 426 | 0 / 0 |
| Momentum | 98 / 98 | 402 / 402 | 411 / 411 | 2,238 / 2,168 |

All authority/client traces report zero overflow, no connection loss, no semantic
overrun and no semantic disconnects. Counts are observations, not equal-input
balance comparisons. Representative sample truncation is reported separately
from transport/evidence loss. Actual build/source/binary hashes and witnesses
are retained in [numeric evidence](evidence/pr09-team-objective-draft.json).

The current Windows production build passes with zero warnings/errors and
preserves its Core hash through every tool rebuild and run. Current-source
checks pass: 56 Native gold trajectories, 18,489 rewind assertions, 82 replay-rule
assertions, and frame timing including the merged reticle changes. The final
focused macOS tool build passes with zero warnings/errors; four malformed CLI
controls exit 2 without producing a success report. Source review found no
blocking issues; accepted bot difficulty and argument checks close its two
minor evidence concerns.

## Latest-main integration

PR-754 merged during these runs. The slice rebases cleanly onto
`33cb131a3f616bd61cb341b3688c7c2a825c0257`. Review finds no movement, protocol,
replay schema or default authority change. NCX scripted placement/input,
presentation pacing and pixel-readback probes are explicit opt-ins and remain
disabled in ordinary Momentum acceptance. The new Python timeline/visibility
consumer checks pass 42 tests. The team measurements above keep their original
current46 source/binary lease.

The separate fresh current33 Windows Core/tool build passes with zero warnings
and errors. All 21 focused checks pass: Native gold, rewind, replay rules,
evidence retention, the touched impact/prediction/payment contracts, actual
continuous-combat scene, protocol58 archive, seven NCX guard invocations and
both recorded replay scenes with real durable-file seeks. The separate
production durable packet checker also passes both archives, all six exact
comparison targets, nested clips and corrupt-checkpoint recovery. Full scene
digests remain identical to preserved results: Native 85,673 checks/2,982 frames
and Momentum 85,586/2,979. This is evidence for these recordings, not universal
cross-platform bit identity.

Both fresh two-owner Battle fault arms also pass: 60 seconds at 200:20 with 1%
loss, 5% reorder and 1% duplication, all six processes exit 0 and both strict
authority proofs pass 22 checks. Native has 15 joined payments and 48 rewinds
(722 frames); Momentum has 10 joins and 55 rewinds (827 frames). All six traces
have no disconnects or overflows, and Core/tool hashes remain unchanged.
Rewind totals are authority aggregates, not depth proof for every paid shot;
counts are not a balance comparison. These are a focused refresh, not a rerun
of the preserved eight-owner or team matrix. See
[current33 source/build hashes and numeric results](evidence/pr09-current33-compatibility.json).
Whitespace and the focused asset guard pass, including all source files added
by the concurrent main merge. Hosted CI remains pending; no green claim follows.

## Retained failures and limits

The separate Native Nodes control remains failed, not converted to Defender.
Earlier AD1 attempt01 had a fixture score-contract error; corrected attempt02
had no objective contact. Predeclared Proving Ground/level-three attempt03 has
119 occupied-node samples per owner but no completed capture or points. Its
maximum observed progress is 1.8166656 seconds versus the native ten-second
capture requirement. First occupancy belongs to server bot slot 1. There was no
state-adoption mismatch, Momentum Nodes run or random seed reroll. Nodes capture
acceptance remains unverified; the additional Defender match proves its own
representative team/scoring boundary only.

These are localhost process cohorts, not physical LAN, controller or human
playtesting. The failed performance proxy and platform/input limitations in
[PR-09 results](pr09-results.md) remain open. No further Mac timing is used while
the operator's host is busy. Relevant hosted build/movement/replay/campaign CI
was queued at publication inspection; its eventual result is not inferred.

## Reproduction

Use separate user-data directories with the operator's own `paths.txt` and
distinct `PRIME_NET2_EVIDENCE` paths. Probe the declared map with
`--momentum-team-objective-map <assets> <json> DefenderTeams`. Start authority:

```text
--momentum-test-server <assets> <port> <Native|Momentum> 180 4 lobby 120 - DefenderTeams "MP3 PROVING GROUND"
```

Start two independent workers (first `lobby-owner`, second `member`) with fixed
seeds 1729/1866 and fault seeds 9000/9001:

```text
--momentum-team-objective-online <assets> <port> <model> <json> 45 <seed> 200:20 0 0 0 <fault-seed> Samus 2 <role> DefenderTeams
```

Require both actual worker exit codes 0, then create the server evidence path
plus `.stop` and allow 60 seconds for orderly flush. Preserve the completed
authority report and binary/source leases. Omitting the final mode selects
NodesTeams and retains its stricter capture/point requirements.
