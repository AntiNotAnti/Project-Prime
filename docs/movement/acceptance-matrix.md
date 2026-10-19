# Momentum acceptance matrix

This audit maps implementation-plan sections 7 and 9 and PR-09 to retained
results. **Release acceptance is incomplete.** PASS below means the named fixture
and source lease passed, not every platform, mode or input combination. Earlier
results keep their original revision; current-main builds do not refresh their
runtime evidence. The principal fresh build lease is `91f3a96d3` plus the
PR-09 overlay; the replay repair has its separately recorded source lease.
Source review and the additional Windows team fixture reach publication main
`46c101cdb`, including render-only reticle/customization and replay follow-ups.
The focused build/Native guards and team results retain that explicit source
lease; they do not relabel earlier runtime results. See [PR-09 results](pr09-results.md).

The plan requires coverage across these areas; it does not require a full
Hunter × map × mode × device × cadence × fault-profile Cartesian product.
Independent rule/lifecycle tests, real scene traces and actual UDP tests prove
different boundaries and are identified separately here.

| Required area | Existing evidence | Status and remaining boundary |
| --- | --- | --- |
| Pure movement mathematics | [PR-01](pr01-results.md): 104 checks, signed/rotated bases, reversal, zero/invalid input; [PR-03](pr03-results.md): 20,046 profile checks including long air-turn stress. | **PASS, scoped.** Pure/profile determinism and warmed zero-allocation checks do not establish whole-engine cost or cross-CPU bit identity. |
| Fixed simulation / render clocks | [PR-09 cadence evidence](evidence/pr09-cadence-draft.json): seven Hunters × two models × virtual30/60/120/144/240 schedules, 70 actual GL arms,119,166 checks; [PR-06](pr06-results.md) playback cadence. | **PASS synthetic scheduling. UNVERIFIED measured display/device refresh.** Simulation remains60Hz; synthetic draw schedules are not achieved physical Hz. |
| Ordinary movement / Native preservation | [PR-00](pr00-results.md): unchanged56 golden trajectories/10,080 ticks at1e-5; [PR-03](pr03-results.md):187,405 jump/landing/Space Jump checks; [balance observations](balance-validation.md): seven Hunters/two maps,112 macOS trials and preserved84 Windows trials. | **PASS named controls. OPEN broader competitive/envelope calibration.** Straight manual-hop distributions and traversal observations are measured; they are not exhaustive optimal-strafe/cap stress or approved competitive balance. |
| Collision | [PR-04](pr04-results.md):364 fresh traces/378,667 assertions, authored walls, slope/edge/corner/step/ceiling/thin/high-speed cases; [collision evidence](evidence/pr04-collision-scenes.json). | **PASS fixture scope**, not all maps. Live collision/recovery remains native; no replacement sweep is claimed. Advisory UDP displacement cases below retain their limits. |
| Forms, abilities and impulses | [PR-04](pr04-results.md):98,556 special-event assertions,14,442 ability checks including real Spire climb/ledge,1,051 native carrier checks; [PR-03](pr03-results.md) native jump/Space Jump timing. | **PASS named actual scene activations.** Paired form trajectories, pad/teleport/bomb/damage, death/respawn/Prime and carrier bypass/resumption are distinct from every possible online timing combination. |
| Slippery / terrain modifiers | [PR-04](pr04-results.md), [collision trace evidence](evidence/pr04-collision-scenes.json), [PR-07 navigation](evidence/pr07-navigation.json). | **PASS sampled native surfaces/contact paths.** No global map-surface equivalence claim. |
| Keyboard, analog, touch and mixed input | [PR-07](pr07-results.md):7,520 scene checks,36 packet/unit checks, partial analog/diagonals/reversal, six paired desktop synthetic-pointer GL scenarios/11,117 assertions, delayed linked touch-model UDP. | **PASS controlled input boundaries. UNVERIFIED full physical-controller/focus recovery matrix.** Synthetic keyboard/pointer and linked Android touch model are explicitly not physical devices. |
| Bots / recovery | [PR-07](pr07-results.md):56 offline cases/298,674 checks;28 authority scenes/496,890 checks;28 navigation cases/19,154 checks. PR-09 actual4human+4bot UDP cohorts. | **PASS actual native AI/accepted-intent/navigation fixtures.** Authority bot motion and human puppets are different owners; no teleport/solver authority is fabricated. |
| 1v1 / FFA / teams and population | [PR-00](pr00-results.md): existing game-mode lifecycle2,198 checks, advanced rules130 and lobby7,750 assertions; [PR-05](pr05-results.md):146 movement-rule/adoption checks. [PR-09 network](evidence/pr09-network-draft.json): real2/4human and8-slot Battle cohorts; [owner combat](evidence/pr09-owner-combat-draft.json):2owner and8human/all-Hunter fault combat. | **PASS scoped mode/lobby, Battle populations and representative actual team gameplay.** [PR-09d](pr09d-results.md) adds current46 Native/Momentum DefenderTeams, two actual human owners plus two server bots, exact objective adoption and positive replicated scoring; all six processes exit 0. The separate Native Nodes control contacts objectives but earns no ten-second capture; Nodes capture remains unverified. No full mode/population/fault/device Cartesian claim. |
| Net admission / frozen rules | [PR-05](pr05-results.md): authorization/stale revision/invalid bits/order freeze, offline29 checks, real protocol58-to59 refusal; [PR-08](pr08-results.md) host-only editor and hidden preservation. | **PASS named contracts**, including current59 interpretation, pre59 reserved-bit conversion and future refusal. No new movement packet type. |
| Latency / owner authority / lag compensation | [PR-09 owner combat](evidence/pr09-owner-combat-draft.json): real2owner200:20 arms;8human/all-Hunter60s arms with1%loss/5%reorder/1%duplication,18 processes exit0, Native51/Momentum59 complete joins and positive authority rewind. | **PASS scoped3de cohort and focused91f refresh.** The fresh91f Native/Momentum two-owner fault arms pass22 proof checks each with all six processes exiting0; the preserved eight-owner arm remains a separate source lease. Real claims/payment identities/health changes are proven. Counts are not equal-input hit-registration balance or universal absence of rewind bias. Geometry-only tests are not owner payment evidence. |
| Spectators / JIP / death / slot reuse / transitions | [PR-09 network](evidence/pr09-network-draft.json): genuine4human+4bot roster, live disconnect/rejoin/generation change, natural respawn, spectator cohorts, same-map rematches and distinct `MP3 PROVING GROUND`→`MP1 SANCTORUS` rotation. | **PASS named model pairs.** Spectator cohort has8slots but7playingentities. Roster/lifecycle acceptance is separate from combat-lifecycle combinations; exact simultaneous death/form/shot/rewind timing is not exhaustively covered. |
| Replay old/current mode / seek / export / killcam | [PR-06](pr06-results.md): actual Native/Momentum Clip/FullMatch, memory/durable seeks, private worlds, rewind geometry, GL theatre/export/killcam; [PR-08](pr08-results.md): cached/uncached labels. Qualified protocol57 recording:1,980 frames/14seeks/five rates; v1/protocol4 timing and reviewed56/57/58/59 archival fixtures. | **PASS these historical fixtures and repaired exact packet comparison, not arbitrary old archives.** The91f index regression and failed repair attempts remain preserved; [PR-752](https://github.com/AntiNotAnti/Project-Prime/pull/752) passes all six exact comparison targets and unchanged scene digests. Saved-world indexes fall back to complete packet history; packet-only acceleration remains deferred. See [replay validation](replay-validation.md). No blanket old-replay compatibility claim. |
| UI / defaults / rollback | [PR-08](pr08-results.md): native GL lobby248 checks and offline126 at each640×360/1280×720/1920×1080; legacy19, metadata54, offline109; authority checks. | **PASS stated UI paths.** Classic default, explicit host/offline choice, stale/nonowner refusal and recorded-mode labels. Android emulator uses its actual Avalonia/GLES backend; optional native Android UI is not inferred. |
| CI / solver allocation / integrated performance | [PR-01](pr01-results.md), [PR-03](pr03-results.md): warmed solver/profile zero allocation; [PR-09 performance](evidence/pr09-performance-draft.json) and [results](pr09-results.md): matched retained Native/Momentum workloads, timing tails and diagnostic cycles. | **FAIL integrated5%p95 gate on Proving Ground.** Later default shot-proof environment pin does not relabel old undisclosed settings. p99/worst and workload differences remain visible. Relevant hosted CI was queued at documented inspection; **OPEN final green CI and retained-source refresh**. No new timing run or budget waiver is authorized by this audit. |

## Platform and input coverage

| Platform | Proven scope | Genuine remaining gap |
| --- | --- | --- |
| macOS arm64 | Actual Release engine/GL, fixed-input scenes, real localhost UDP, replay hashes and synthetic cadence. Latest91f Core/tool build passes. | OS-event fixture is **BLOCKED**: actual TCC preflight denies event-post/accessibility; zero events posted. Physical keyboard/controller, achieved refresh and sustained budgets remain unverified. |
| Windows x64 | Fresh source build; Native gold/rewind; actual owner UDP combat; balance trials and controlled performance investigation. Limited interactive OS-input evidence. | Full physical-input/focus/controller/touch matrix and display-cadence claims remain unverified. Focused91f combat refresh passes; current46 production build, Native gold/rewind/replay-rule/frame-timing guards and additional team pair pass under their separate lease. |
| Linux desktop | Earlier asset-free three-OS math/contract CI exists in prior PR reports. | Current actual asset-backed engine/input/UDP/GL/device runtime and final relevant hosted CI are **UNVERIFIED** in the retained PR-09 evidence. Earlier pure CI is not gameplay acceptance. |
| Android | Earlierb1780d624+overlay arm64 APK build; Android36 emulator actual kernel touch→MotionEvent→virtual-stick/world in both modes; rule dialog exercised. | **UNVERIFIED physical device**, precise movement/jump, networked touch, solver counters, current-source runtime and sustained performance. SwiftShader artifacts/death-state limits remain recorded. |

## Completion decisions still required

1. The indexed replay regression is repaired with exact decoder/player checks
   intact. Keep the saved-world versus packet-only distinction; no indexed
   acceleration acceptance follows from the canonical linear fallback.
2. Resolve or explicitly decide the documented failed integrated performance
   budget. Small absolute frame cost and a passing second room do not satisfy
   the agreed relative gate. Do not run timing on a busy user's host.
3. Representative actual team/objective gameplay now passes in DefenderTeams.
   Keep the separate unsuccessful Nodes capture control and stated physical/
   platform gaps explicitly unverified. The plan permits unavailable hardware
   to be reported as not run; it does not permit claiming it passed.
4. Current46 focused compatibility and team gameplay now pass. Record final
   relevant hosted CI outcomes. Preserved3de eight-owner combat retains its
   source lease; it is not a claim that every later merge ran that matrix.
5. Preserve unresolved diagnostics honestly: impaired05 seven advisory rows lack
   per-step native velocity/contact witnesses; impaired06 reproduced five rows
   with complete zero-overflow traces and proves large position correction is
   distinct from native Speed. Exact correction subtype remains unwitnessed.
   HardRejections remains0; advisory WouldRefuse is not zero. The earlier UDP
   final-Y outlier also remains unclassified, although actual accepted-jump
   seam checks pass without widening native bounds.

The plan's regression examples include exact landing/shot/rewind coincidence,
low-jump release, high-speed reversals and mixed input. Existing jump/input/
special/replay suites cover many named boundaries; no report establishes every
possible simultaneous online event. That is a scoped evidence limitation, not
an invented requirement to enumerate all combinations. PR-10 surf/slide/training
features are explicitly optional future work and are not v1 release gates.
