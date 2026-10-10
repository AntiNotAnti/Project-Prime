# N0 completion — 2026-10-10

**N0 exit gate: complete. Combat/release acceptance: not complete.** PR #450
remains unmerged; this completes the baseline slice, not N3/N5/N6 acceptance.

This follow-up uses the implementation plan's N0 exit contract: a stable,
reproducible control, a harness that does not turn missing evidence into success,
and a ledger of classified baseline defects. Earlier reports treated later combat
and release gates as N0 blockers. Those failures remain real and remain failed;
they are assigned to their intended implementation slices below.

The owner requested focused Mac validation, waived another twenty-minute soak,
and limited Windows reruns to platform-specific concerns. The previous Mac-eight
and Windows-two endurance evidence is retained. This follow-up changes shared C#
loading and test fixtures, with no Windows-specific implementation change.

## Loading repair

The loading barrier used to stop `AfterInput`, the only client call site for
`NetRoomChange.Sync`. The new room therefore never loaded and never acknowledged
readiness. The common loading pump now synchronizes the foreground live client's
room. At a frozen boundary the existing synchronous room rebuild runs directly,
canceling any old fade and acknowledging the loaded scene. It does not advance
combat, frame counters, or release the authority's world/bootstrap barrier.

An asset-backed regression uses both headless `OnSimulationFrame` and graphical
`HoldLoadingFrame`: a different-map rotation and a same-map rematch both rebuild
and acknowledge while gameplay remains frozen. Replica scenes and authorities
are excluded from this client loading path. Protocol 41 and replay encodings are
unchanged.

The first rendered rotation exposed a second failure after the loader resumed:
the spectator's retained Roam camera caused `PlayerEntity.Initialize` to skip
HUD construction for the rebuilt local player, while `Scene` still updated that
HUD. The rebuild now initializes the local HUD once when graphics are active.
The headless path remains HUD-free. The crashed attempt is retained separately
from the subsequent rendered verification.

## Test corrections

The combat fixture incorrectly rejected an affinity-sized Battlehammer impulse
solely because the hunter was Samus. Prime Hunter uses the affinity weapon row
for every hunter. The fixture now proves that production row's 0.5 impulse is
legal, retains the existing 0.526 ceiling, and rejects 0.527, excessive vector
magnitude, NaN and infinity for both Samus and Weavel. No damage or admission
policy changed.

The continuous-target fixture expected `Eligibility` from a resolver whose
current-frame pose lookup already rejects a bodyless spectator with
`UnavailableHistory`. It now verifies both the production eligibility predicate
and the resolver's rejection with a cleared target. No targeting policy changed.

The custom-map readiness fixture started its four-second admission timer while
the server was still indexing installed map archives. The failure reproduced
both in the full lifecycle suite and its focused entry point. The fixture now
prepares and verifies its exact immutable map identity before starting the
server; the admission deadline and readiness assertions remain unchanged.

The offline checker previously treated two room-name strings as rotation proof.
It now requires distinct authority epoch/match identities, server and client
simulation progress, and full-identity health comparisons spanning at least
60 authority frames in each required match. Full-duration peers must follow the
rotation; planned late joins/departures need only cover matches they observed.
Missing evidence is `INCOMPLETE`, even if the processes exit successfully.
Shot-phase skips and death proximity still never waive a shot failure.

## Focused Mac validation

The final C# source is `99072453425b37c9d343e8e101ea19a5a6a906e4`;
its engine SHA-256 is
`57d2860d74fb9e374e36acc485cb82d55fd0a0db1611ee6ce07c48edfae0aa63`.
The Release build passes with 41 existing warnings and zero errors.
Combat passes 167 assertions, continuous targeting 63, and the full loading/
lifecycle suite passes all 7,738 lobby assertions. Focused custom-map readiness,
the frozen rotation/bootstrap regression and all 44 Python evidence regressions
also pass. Only the owner's Mac was used for this follow-up; no Windows rerun,
CI run, additional long soak or performance optimization claim is made.

The 85-second eight-client Shock Coil rotation used source `755d1053` (before the
graphical-only HUD repair). All nine processes exited normally. Every client
advanced through both maps, with 52,799 matching health samples and 166,278
expected semantic facts, zero differences, zero queue drops and zero simulation
step failures. Rotation coverage passes; the overall combat verdict remains
**FAIL**: 20,921 offered/accepted shots, 20,708 authority commits and 3,419 missing
view commits. Explicit phase skips explain 204 of 213 authority gaps and 3,393
view gaps. Eleven terminal claim timeouts also remain failed.

The first rendered three-view rotation retained **FAIL** for the spectator HUD
crash and **INCOMPLETE** rotation coverage for that peer. Its 68 shots matched
on the authority and all eligible views, illustrating why shot parity and
process exits alone cannot establish lifecycle correctness. Each rendered run
records a shooter, victim and spectator demo plus screenshots. Those files do
not automatically establish exact same-projectile visibility or replay seek
compatibility.

The first rerun on `99072453` has no process, shot, health or semantic failures,
but remains **INCOMPLETE**: the rendered netcheck's duration is frame-counted,
so the spectator ended before slower rendered clients entered a third match.
The checker conservatively requires full-duration participants to cover every
authority match. A shorter follow-up targets one common rotation. No third-match
evidence was invented and the incomplete attempt was not overwritten.
The spectator uses a free camera; its screenshots can retain a viewpoint outside
the new map. These runs establish simulation/capture and crash-free rotation,
not a measured same-projectile visibility rate.

The final three-view control requests 60 rendered seconds and completes in
82.10 wall seconds. It **PASSES**: all three clients observe both authority
matches, the observer remains a spectator on both maps, all 60 offered shots
are accepted and committed with zero missing eligible view commits, 4,981
authority health samples match, and all 183 expected semantic facts match.
There are zero transport queue drops/socket errors, crashes or simulation step
failures. All four processes exit normally. Three demos and 115 screenshots
are retained. The engine hash is unchanged at campaign completion.

| Follow-up campaign | Source | Strict verdict | Rotation coverage |
| --- | --- | --- | --- |
| Mac 8, Shock Coil, 85 s requested | `755d1053` | FAIL: classified shot/claim defects | PASS |
| Mac 3 rendered, 85 s requested | `755d1053` | FAIL: spectator HUD crash | INCOMPLETE |
| Mac 3 rendered, 85 s requested | `99072453` | INCOMPLETE: unshared third-match tail | INCOMPLETE |
| Mac 3 rendered, 60 s requested | `99072453` | PASS | PASS |

The [completion ledger](../tools/nettest/baselines/net2-n0-completion-20261010.json)
contains full source and build identities, commands, per-view denominators,
failure classifications, timings and the SHA-256-indexed raw evidence bundle.
Earlier broad asset-free, asset-backed, synthetic impairment and Mac/Windows
endurance runs remain in the original ledgers; unchanged areas were not rerun.

Local bundle: `artifacts/net2-n0-completion-evidence.tar.gz`, 184,712,259 bytes,
SHA-256 `d7dc1b61cf61b930b3a507bd8ce9517543a93a5aaf58254bc4d62a53b8892f0a`.
The index covers 645 files / 475,181,715 raw bytes, excluding staged dependency
directories whose executable hashes are retained in the manifests.

## Classified baseline defects and later gates

| Item | Evidence / disposition | Owning slice |
| --- | --- | --- |
| Reliable dedup-span exhaustion / missing close ACK | Reproduced and repaired; prior Mac/Windows endurance retained | N0 repair, N1 instrumentation |
| Client rotation blocked by readiness freeze | Repaired and covered by host-entry regression and UDP rotation | N0 repair |
| Retained Shock Coil phases consumed without a commit | Explicit `phase-skipped` records; campaign stays FAIL | N3 event lifecycle; N6 continuous weapons |
| Delivered missile/Imperialist events lack some view commits near death | Full shot identities and arrival evidence retained; sampled death proximity is association, not exact ordering proof | N3 event lifecycle |
| Terminal hit-claim verdict not consumed | Short eight-client run has 11 Shock Coil verdict timeouts across five peers; every full shot key has an earlier authority verdict. Queue admission, wire delivery and client rejection are not distinguished by current evidence. These are combat claim failures, not reliable-channel disconnects. | N4/N5/N6 claim completion |
| Exact Shock Coil drain/kill and Imperialist spawn-trap parity | Not established by generic firing counts | N5/N6 specialized combat acceptance |
| Exact rendered same-projectile agreement | Rendered captures are evidence, not an automatically passing visibility metric | N3 presentation / N14 acceptance |
| Representative performance acceptance and broad impairment matrix | Earlier control measurements retained; no optimization claim from these targeted runs | N1 measurements / N13 / N14 |

The source remains based on main `468bbbf027148ce94901dfa8bf138d16a4498c03`,
which still descends from restored v0.1.47. No historical #410–#423 patch was
imported. All failed attempts, original ledgers and raw evidence bundles remain
unchanged. The completion ledger records the follow-up source/build identities,
commands, verdicts and raw-file hashes.

## Rollback

Revert `99072453` for the spectator HUD repair, `a41566a6` for custom-map fixture
preparation, and `755d1053` for the loading repair, stricter rotation adjudication
and two scene-fixture updates. Revert `4e38efe2` for the earlier production
delivery repair. Leaving `PRIME_NET2_EVIDENCE` and `PRIME_NET2_SOAK` unset disables
capture and campaign drivers. There is no packet, replay or profile migration.
