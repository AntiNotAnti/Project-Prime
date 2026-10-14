# Controlled ownership/save soak (LIFE-04 partial campaign)

This slice adds a reproducible device-free regression campaign for the eligible
LIFE-02/LIFE-03 contracts. It changes tests and CI only. It is not acceptance of
rendered lifecycle flows, a gameplay benchmark or a claim that all scene roots
have been removed. Production GC, shader/GL/audio ownership and network-session
architecture remain unchanged.

## Exercised contracts

`dotnet run --project tools/nettest -c Release -- --lifecycle-soak` runs independent
groups of 10, 100 and 500 sequential content-free Scene constructions. Every
world has seven actual PlayerEntity bot owners, one for each supported hunter
value, retained by the production AI destination table through save and cleanup.
Hunter values are assigned without loading models, rooms or movement code; this
is ownership coverage, not seven-hunter gameplay coverage.

Each transition uses the real save normalization/serializer and filesystem
writer. The expected bytes come from the independent frozen baseline writer in
PostMatchSaveTests, including changing Health, Stats and numeric byte arrays.
Slots rotate through 1/2/3. Every seventeenth transition obstructs Savedata with a
real file, verifies failure retains its immutable payload and foreground owner,
mutates the old save, then retries after removing the obstruction. The retry
must preserve original bytes and leave a newer same-value request intact.
Every successful context is retained by the fixture after release; repeated
Commit/Release cannot write again. Final facade release occurs only after real
cleanup, visual release and save completion, with configuration and RNG parity.
An old context is tested against the next live generation before every handoff.

Every tenth iteration also constructs and tears down a private replica without
changing any foreground facade. Each group additionally holds two live scenes
and one replica simultaneously. A real RoomEntity owned publication updates only
the old scene while its global AI adapter reset is rejected; after cleanup its
terminal publication and release are rejected. Three actual collision query
leases are nested across those worlds and disposed properly. This empty-room
case tests lease scope; nonempty collision candidates are covered by the reused
collision-query-check harness, not by a copied test implementation.

There is no diagnostic collection within a transition loop. After its final
eligible owner release, the fixture performs a diagnostic-only collection and
checks weak targets while all released contexts and value snapshots remain
alive. The observation buffer stays capped at 32 records and reports overflow.
No production reset/abandon-save or bypass-release hook is exposed for testing.

## Local measured results

Release nettest build passed; the only warning is the existing duplicate
System.Text import in PlayerNameChatTests. The final controlled run passed:

| Sequential owners | Real byte comparisons | Failed/retried saves | Private replicas | Live old worlds / replicas / simultaneous worlds | Kept / dropped snapshots |
| --- | --- | --- | --- | --- | --- |
| 10 | 10 | 1 | 1 | 0 / 0 / 0 | 32 / 22 |
| 100 | 100 | 6 | 10 | 0 / 0 / 0 | 32 / 508 |
| 500 | 500 | 30 | 50 | 0 / 0 / 0 | 32 / 2668 |

Across the groups this is 610 exact real-write byte comparisons, 37 failure/retry
cycles and 61 private replicas, plus nine simultaneous-world fixtures. All
released bot table entries and foreground Owner references were absent. No
surviving weak scene required a retained-root investigation in this controlled
scope; this is not an actual GC-root dump of a running native application.

The raw run records normal transition GC counts, total allocation, elapsed time
and managed bytes after the diagnostic collection. These are descriptive test
measurements, not performance acceptance. Retained fixture lists deliberately
scale from 10 to 500 value contexts, strings and weak references; aggregate
managed bytes must not be interpreted as old-world growth. The final rolling
save digests are:

- 10: `f405372fff5f0e1532fa3f71e964859fc79f98bf9a61244d5ded10c231b14e10`
- 100: `81fcc92f0d1495a93aef7903b382c26fcca68ee2ba8c95da67b83ee27048d151`
- 500: `1a4ada5c643c264c1257f4e02834d1a24353b06d2ae65927323bb458cd96d669`

The existing collision-query-check passed its nonempty nested candidate and
deduplication checks, 2,000 independent-scene checks, 32,000 concurrent queries,
scene-reference clearing, 24 nested leases with a pool capped at eight, injected
query failure cleanup and oversized-query cleanup. Its 618-row collision
snapshot SHA-256 was
`68461616b61605635f0aa9532fa7716f2e399cf9a9d0b2190b917a5e0f7d8435`.
Warmed convenience and borrowed query allocation checks remained zero. This
synthetic collision result is not movement/replay or rendered GL parity.

The 24 foreground-binding, 26 post-match-save and 16 lifetime checks and existing
scene-setup ownership checks also passed on the final test tree. Raw logs and
collision snapshot are in the external `life-evidence/life04-*` files. The
dedicated scene-ownership workflow runs all these checks under the pinned SDK
on PRs and main pushes; exact-head hosted results remain required.

## Native and broader failure gates

| Plan case | Status and next evidence needed |
| --- | --- |
| Save writer IOException, retry, duplicate success, replacement request | PASS in real device-free writer and byte oracle; power interruption remains NOT_RUN |
| Two live owners plus replica; nested collision leases | PASS in controlled source fixtures, including nonempty candidate harness; full room/gameplay flow NOT_RUN |
| 10/100/500 varying loaded maps; pause/respawn/menu/lobby/reconnect | NOT_RUN: fixture has no room assets, simulation loop, launcher interaction or socket |
| Seven hunters in native movement/combat/health | NOT_RUN: bot PlayerEntity/hunter ownership values are covered, model initialization and behavior are not |
| Rendered Studio seek/clip, editor open/close, replay-lab rollback | NOT_RUN: explicit activation/rollback and native workflow need independent eligibility design |
| Player/room initialization, shader compile, network spawn, audio-close failure | NOT_RUN: injected fake failures would not establish production rollback; only real save and query failure were injected here |
| Export/capture/custom texture/model/audio callbacks | NOT_RUN: known decoder/pending decode owners conservatively block release; untracked callback ownership remains a design gate |
| Active Adventure worker teardown and setup mutation | NOT_ACCEPTED: LIFE-02 preserves original AfterMatch timing and LIFE-03 prohibits release; SetUpRoom/personality/setup cancellation is not fully fenced |
| GL color/depth/native handles; end-to-end save/settings/results/replay parity | NOT_RUN by this device-free tool; requires coordinated native campaigns with exact binary/asset identity |

Native rendering campaigns remain coordinated by the parent task; this slice
launches no GPU/window/audio process. No broad LIFE-04 acceptance can be inferred
from these controlled results. Roll back the test/CI slice independently; roll
back LIFE-03 eligibility changes separately if native supported-flow parity
fails. There is no GC policy switch or blanket global clear in either slice.
