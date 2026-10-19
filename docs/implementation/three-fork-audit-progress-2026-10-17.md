# Three-fork implementation progress

Date: October 17, 2026; continued October 18.
Source plan: `Project_Prime_Three_Fork_Audit_Implementation_Plan_2026-10-17.md`
provided by the user. This records the user's execution choices separately
from the audit's recommendations.

| Item | Tracking status | Evidence / remaining acceptance |
| --- | --- | --- |
| CPP-01: Graphics memory and Android startup gate | **COMPLETE — SKIPPED by explicit user direction** | No AGX remediation or Android/device certification is claimed. Existing engineering findings and unverified release gates remain applicable. |
| CPP-02: RmlUi integration-blocker closeout | **COMPLETE — RML-97 DESKTOP MIGRATION, MAC SCOPE** | Full native Settings/HUD, versions/updates, startup/setup/recovery and constrained lobby presentation complete. Old Replay Studio authoring enhancements restored, Main Theatre stays a viewer, Map Studio intentionally Coming soon. Final native layouts pass three size/density cases; actual Main/Studio, seven-Hunter stage, license, replay capability and audio checks pass. Exact resource/code inheritance, validation boundaries and signed Mac delivery are recorded in [RML97 completion](rmlui-migration-completion-rml97-2026-10-18.md). Live services, physical input and other operating systems are separate acceptance; they are not certified by this migration. |
| CPP-00: Source lock and comparable benchmark evidence | **IN PROGRESS — PARALLEL** | Current refs and open PRs reconciled; 12 evidence/comparability tests pass. Dirty or mismatched captures cannot pass the comparable baseline gate. RML-72 integrates backend target configuration, Community ticket hardening and historical SQL delimiter repairs; its 83 Edge, 47 routing, 75 closed server-owner and 463 compatibility-schema SQL checks pass. The absent original EF schema is not reconstructed as historical fact. An explicitly new private fresh-install baseline plus all 24 migrations passes 555 SQL checks and independent review; its deployment recipe/configuration and complete acceptance-input freeze remain unfinished. Exact-head comparable benchmarks and deployed-service proof remain open. |
| CPP-03: Display-time pose contracts | **COMPLETE — IMPLEMENTED; MAC VALIDATED** | Lifetime/clock/seek-fenced entity histories, draw consumers and canonical restoration are implemented. Final client/server builds, 77,268 core and 69 scene checks, frozen schema/field-stream compatibility, fresh 28-cell native world matrix and loaded headless/rendered replay checks pass. See [October 19 completion](cpp03-cpp04-presentation-completion-2026-10-19.md) for immutable receipts and remaining release/platform limits. |
| CPP-05: Dedicated match lifecycle | **IN PROGRESS — RML-66 IMPAIRED MAC MATRIX PASSES** | [Actual Mac world rotations](../architecture/cpp05-actual-world-rotation-2026-10-17.md) pass 83 cleanup/startup-failure regressions and 158 full-rotation checks. RML-64's terminal-claim fix passes 252 compiled-owner checks and the real 2-client pilot. RML-66's bounded ACK-retention fix passes 514 compiled-owner and 135 actual UDP checks. Fresh strict 4-client/150ms and 8-client/250/350/750ms native-AI worlds all pass with zero health mismatches, all process exits zero, unchanged assets and every finalized archive fully read. The 750ms arm alone compares 61,168 health states. Original failures are retained; targeted alt-form scenarios are running, while real League acceptance remains open. |
| CPP-07: Vanilla fidelity oracle | **IN PROGRESS — REPEATABLE ORIGINAL BUTTON TRACE** | All 47 stored canonical hashes validate; actual-content regression remains 46/47 with its numerical failure reproduced on clean base. Two fresh original button runs produce byte-identical 8,599-row streams (7,799 boot plus 800 probe), exact native initial state and native room byte 45 throughout the probe. Actual AMHE 1 ARM 9 readers independently bind that byte to UNIT 2_LAND. Native update/input-latch ownership, matching Project traces and all 13 accuracy comparisons remain open. Earlier callback, first-frame GL and storage failures are preserved. |
| CPP-04: Animation/camera/effects coherence | **COMPLETE — IMPLEMENTED; MAC VALIDATED** | Camera/cannon generation fencing, particle and projectile sampling, continuous attachments and specialized alt-form poses are complete. Fresh seven-Hunter 60/120/144/240 matrix: 28/28 cells, 40,320 canonical steps, 94,759 outputs, 364 camera transactions, 56 positive gun/charge controls and 21 exact cross-rate comparisons. Loaded rendered replay restores match all seven picture pairs. Content fidelity remains the known 46/47; historical RML77 timings are preserved separately. See [completion and limits](cpp03-cpp04-presentation-completion-2026-10-19.md). |
| CPP-06: Alt-form transition authority | **IN PROGRESS — BOTH TARGETED 750 GAMEPLAY GAPS COVERED** | Historical RML-71 remains 10 PASS/2 INCOMPLETE. Fresh Weavel 750 on RML-75 passes with 2,183 matching health states and the native owner cycle. The explicit collision-verified Kanden encounter on RML-77 passes:14 placements/14 detonations/9 positive damage observations,2,036 health comparisons with zero mismatch, same-key motion/orientation on authority and all 3 peers, four clean process exits and four fully read archives. Its fixed 2 s neutral preparation and distinct initial geometry are disclosed; original stock zero-damage run stays INCOMPLETE. Rendered effects and League acceptance remain open. |
| CPP-08: Imported character readiness | **IN PROGRESS — RML-77 SOURCE GPU ORIENTATIONS PASS** | [Source-rig work](../porting/cpp08-character-readiness-2026-10-17.md) retains the existing pack/scene owners. Actual RML-77 full character acceptance and 24 source captures pass; all 36 angle pairs differ 99,367–116,964 crop pixels, with distinct GPU palette stacks and unchanged raw skin matrices. Root visually inspected front and side captures. Earlier RML-73/76 angle-inert failures remain intact. Faithful GX materials, source animation graphs, first-person cannon, forms/Trooper mechanics and online/replay qualification remain open; owned media stays private. |
| CPP-09: Echoes arenas and scoped mechanics | **IN PROGRESS** | Actual RML-69 importer/compiler passes all six Echoes blockouts and six visual previews from the verified owned disc. Four cannon playtests and four routed playtests pass, including all twelve exact per-route final compiler attestations. Each uses fresh private roots/catalogs; ordinary graph bytes/map IDs and source/companion hashes remain intact. The 196-check synthetic suite also passes. All six also pass 600 actual native-bot steps each under their original solo capability (3,600 total). Human gameplay, combat/opponents, faithful materials/audio, scoped mechanics and match/replay qualification remain open. See `artifacts/cpp08-cpp09-project-source-rml69-20261018/tier-acceptance.json`. |
| CPP-10: Campaign UI and front-end parity | **IN PROGRESS — REDUCED SCOPE** | Native journal/ending/ship fixtures and the actual Celestial Archives scene/save pilot pass. Original ScanLog data exposed nine off-by-one starter equipment IDs; RML-70 fixes the constructor only. Actual frozen RML-70 passes 41 native three-slot lifecycle checks: new game has zero scans/nine equipment entries, exact load/resave, guarded copy/archive, confirmation cancellation, stale command refusal and abandoned-new-game preservation. Focused visible RmlUi, new-game startup/scene entry, save management, asset-import and scoped cutscene/audio lifecycle checks remain. Full campaign playthrough and ending/credits validation were removed by the user on October 18. Android runtime stays deferred under Mac-only validation. |
| CPP-11: Local mod sandbox | **OPTIONAL — DEFERRED** | Not on the main release path; requires fidelity/content contracts first. |
| CPP-12: Renderer-backend spike | **CONDITIONAL — NOT ENABLED** | Remains outside the release critical path; no default renderer change. |
| CPP-13: Release qualification | **OPEN — RML-97 MAC ARM MIGRATION BUILD DELIVERED** | Signed game/Theatre and Prime Studio apps delivered together in the RML-97 ZIP. Both pass packaged render/resize, no-game-assets, extracted inventory/signature and startup checks. Desktop UI migration is complete within Mac scope; broader integrated release acceptance remains open. Windows/Linux/physical Android runtime checks remain deferred by the user. |

**User execution policy, October 17:** RmlUi is not a hard blocker for other
implementation tasks. Work may proceed in parallel with separate ownership.
Any new or changed product UI must use RmlUi. This policy is a direct user
instruction; the attached audit remains source material for task selection.

**User scope revision, October 18:** full campaign playthrough,
new-game-to-ending acceptance and ending/credits validation are removed from
the current plan and release gates. Existing implementation and evidence are
preserved. Focused campaign UI, startup, save, import and audio lifecycle checks
remain in CPP-10; the removed validation is not recorded as passing.

**Current priority, October 18:** work exclusively on RmlUi migration (CPP-02,
including focused front-end CPP-10 flows), as subsequently directed by the user.
Networking, presentation, character imports/mechanics, Echoes arenas and DS
accuracy work are paused with their evidence preserved. Shared application-owner
changes required by native UI flows remain in scope. RmlUi must recover the original
migration's features and visual/interaction polish; structural page presence
and compilation do not establish parity. Full Settings remains last.
The verified original migration references include
`codex/rmlui-game-client-migration` at `31c3b20d4d8cc56ea3e4c5587798ce27e72b80fc`
and `origin/backup/main-2026-10-09` at `d08107a8a7a7ca64f87e96780aff8c6646cc2443`,
alongside retained original GUI owners. The broader progress estimate below
is not a completion percentage for this newly prioritized subset.

**Current RmlUi/network continuation:** [checkpoints 79–80](rmlui-parity-restoration-rml79-rml80-2026-10-18.md) restore weapon preview, dirty-draft navigation feedback, readable Home rail labels, filtered Theatre selection and two Smart Views, leased game invitations/friend admission and verified Home invitation/joinability projection. Actual Hunter GL passes9,132 checks on79;80 Social GL passes639 checks with synthetic services, plus66 invitation/56 connection-Home/22 SmartView assertions in the full tools. Desktop80 builds with zero warnings/errors; live account/backend qualification and full parity remain open. The exact checkpoint binding optimization passes complete state/byte/restore proof over6,093 native replay steps and reduces measured constructor allocations about91%; strict League zero-drop startup remains open. Later81 polish/metadata work is separate from this freeze. The delivered Mac ZIP is still78 pending a separately checked replacement.

**Progress estimate, October 18:** approximately 60% of the current Mac-only
main implementation and validation scope (engineering estimate, range 55–65%).
This is not a counted acceptance percentage. It excludes explicitly skipped
CPP-01, optional CPP-11, conditional CPP-12, full campaign/ending validation
and deferred other-device validation.
Original-game comparisons, complete imported-character/arena gameplay, live
services, final RmlUi/campaign UI parity and integrated release qualification still
carry substantial work; green builds and bounded fixtures do not close them.

**Current validation scope, October 17:** the user directed validation on macOS
only for now. Continue independent implementation and compilation work; Windows,
Linux and physical Android runtime qualification are deferred, not passing.
Original-game accuracy still requires matching original traces independently of
the machine running the comparison.

**Latest continuation, October 18:** RML-71's actual separately compiled server
and client pass the corrected Weavel250 case and all eight targeted 250/350 ms
arms. Weavel750 remains strictly INCOMPLETE: accepted-source bridge diagnostics
miss the owner's brief native Morphing phase, so the required same-life ordered
cycle is not established. A separate actual owner-frame observation correction
is private and review ready; historical evidence is not backfilled. Samus750
and Lockjaw750 pass. Kanden750 remains INCOMPLETE with fourteen observed
placement/detonation pairs but no positive larva damage. The final targeted
matrix is ten PASS and two INCOMPLETE, with unchanged strict gates.

RML-72 integrates shared backend project/public-key configuration, bounded
Community ticket input and pinned dependency metadata, plus four local historical
SQL delimiter repairs. Full client/server/GL builds have zero warnings/errors;
83 exact-handler Edge tests, 47 routing checks, 75 closed server-owner checks and
the 463-assertion Social SQL suite pass. That SQL suite uses a compatibility
prerequisite, not the absent original EF schema. The full repaired chronological
chain reaches 15/24 then correctly refuses its missing prerequisite. The user
does not think the old backend/schema is available. An explicitly new private
fresh-install baseline from current contracts now applies all 24 migrations
and passes 555 SQL checks. Independent review also passes sixteen catalog,
role and existing-schema-refusal checks. Remaining installation work includes
the baseline-before-migrations procedure, direct database role requirements,
career-report gateway configuration and freezing three additional executed SQL
acceptance inputs. No live database, migration history or service deployment
has changed; live Auth, Edge, Realtime and concurrent PostgreSQL are unqualified.

RML-73 integrates the private real-source material attachment/caller-time clip
adapter and source-correct RESULTS music for the existing ending-results page.
Its initial full server build exposes 28 missing namespace-import errors hidden
by implicit imports in the private compose project. That failure is preserved;
adding four explicit imports makes client/server/GL builds pass with zero
warnings/errors. The actual frozen client passes 29 campaign audio, 12 ending
handoff and 22 ship-flow checks. The actual real-source GPU run subsequently
passes its 24 packet/capture checks and native baselines; distinct preview yaw
orientations remain unverified at that checkpoint. Original credits, ending
counters and a genuine new-game-to-ending campaign were not validated there;
full campaign/ending validation was subsequently removed from the current
acceptance scope by the user on October 18.

RML-74 integrates the reviewed nineteen-path native Game Files v5 and the
three-path native camera-key fix. Client, server and GL tool compile with zero
warnings/errors. The camera fix retains exact native pose bits for frame-only
edits after the actual RML-70 diagnostic exposed a one-ULP degree round trip:
the sidecar was saved, but live adoption was correctly refused. Fresh actual
RML-74 replay and native setup/picker/extraction checks are still NOT_RUN.
Full Settings remains private and last; it must be rebased onto this integration.

The matching Project fidelity producer has not yet produced a usable landing
trace. Its first invocation stopped before frame one because the wrapper
precreated the forbidden output directory. The corrected invocation reached
the ordinary Scene 0-to-1 StoryIntro frame, then failed the unchanged combined
renderer-invariant/GL gate. That combined check cannot identify which term
failed. Both failures remain recorded, with no DS accuracy or cleanup-counter
claim. The thirteen original-game comparisons remain open.

**User-selected continuation, October 18:** focus proceeds on presentation,
networking, original accuracy, characters and Echoes gameplay. RML-75 compiles
the native owner observation correction and passes its fresh Weavel750 case;
the earlier RML-71 reports remain immutable. RML-76 then compiles exactly three
additional changed production inputs: the source preview receives the existing
owner's world transform, its acceptance checks require submitted orientations,
and movie alpha selects the float uniform overload (`1f`). Actual frozen RML-71
IL proves the old movie call selected the integer overload for a float shader
uniform. A separate actual diagnostic establishes renderer invariants all pass
while the original GL error is `InvalidOperation`; its post-disposal receipt
confirms cleanup returned. The actual RML-76 landing attempt still reports
first-frame GL InvalidOperation with all renderer invariants passing. A second
movie upload also selects an integer overload for a float shader uniform; its
reviewed float correction is included in RML-77. The actual RML-76 character run
passes packet metadata checks but fails independent pixel orientation: angles 0
and 90 remain identical because Weighted4 consumes its palette rather than the
submitted transform. Both failures remain unchanged. Central checkpoints build
client/server/GL tools with zero warnings/errors.

The fresh diagnostic presentation matrix now passes all seven hunters at all
four requested rates (28/28 cells) on the actual frozen RML-77 binary, retaining
the input/canonical/camera/native pixel gates, 364 camera transactions, 56 positive
gun/charge controls and 21 within-hunter cross-rate comparisons.
This suite covers its scripted offline workload, not all replay, network,
weapon-effect or physical scanout scenarios. Separate private performance
tooling retains raw completed-frame intervals, callback wall times, aggregate
process CPU and boundary GC/allocation/RSS observations. Twenty-one closed
analyzer/environment/provenance checks pass; actual results below retain their
focus and timing admission gates.

The first current-build timing campaign accepts eight actual cells (Samus and
Kanden at all four rates). Four Trace 60 Hz attempts are rejected for actual OS
focus loss/refusal, including the bundled apphost; the remaining nineteen cells
have not run. A timer-analyzer
bug is corrected using the runtime's exact 100 ns integer grid and directional
truncation identity; the original rejection is preserved. A normal macOS apphost
bundle now passes environment/cwd admission and kernel-observed exit-status
controls without opening a window. Its first window pilot also receives no OS
focus. Source inspection identifies the hidden startup window and a missing
public show/focus request after loading; a private five-line startup successor
passes its build and no-window preflight, with timing gates unchanged. Its timing
continuation remains pending. Desktop inspection subsequently reports the Mac
locked and unable to unlock automatically. These four refusals remain immutable.

The user subsequently unlocked the Mac. The exact v16 normal apphost continuation
passes all twenty remaining timing cells with actual stable OS focus/context,
zero measured dropped/stalled simulation steps, kernel-observed zero exits and
closed PIDs. Combined with eight separately identified historical cells, the
performance matrix is now 28/28: 75,225 completed application-frame intervals and
33,600 measured canonical steps. Full source/app/runtime/asset postguards pass.
Observed completed-frame ranges at requested 60/120/144/240 caps are
55.23–58.08 / 110.87–117.43 / 134.56–141.16 / 228.06–236.44 Hz. Their p99
frame-interval ranges are 22.90–24.36 / 13.19–14.62 / 11.15–12.16 / 8.04–9.10 ms.
Two explicitly identified harness groups are retained; no GPU duration, input
latency, panel scanout, cross-fork win or canonical CPP-00 benchmark is claimed.
See the durable `artifacts/cpp04-presentation-performance-rml77-20261018` packet.

RML-78 promotes exactly 31 reviewed source paths (1,811 total inputs), combining
the source-cannon and raw graph bindings, material4 diagnostic owner, explicit
local Trooper wall-cling, RmlUi Echoes catalog and shared offline admission.
Central client/server/GL-tool builds exit zero. The corresponding full private
integration passes 904 model, 583 producer, 834 typed graph/cannon, 31 material,
39 arena-policy and 6,512 wall-cling checks. Native/default remains byte-identical
over 1,200 frames. This compilation does not enable private character gameplay
or establish faithful full-character materials, graph semantics or PlayerDraw.
New drone, clock-refresh and seven-program TEV work remains separate. See
`artifacts/rmlui-checkpoint-rml78-candidate-20261018/build-composition.json`.

The material4 functional GPU successor passes seven bounded cases on its exact
private production owner: genuine 438-triangle geometry matches an independent
reference exactly (23,845 covered pixels), source/additive/alpha controls pass,
and shader-failure, stale-lease, state restoration and following actual world/HUD
checks pass. Two harness failures remain preserved. This establishes the one
unlit additive material and its diagnostic sampling, not all seven GX programs
or original sampler/lighting fidelity. The private Trooper drone successor also
passes 3,120 actual-owner CPU checks, with 94 HP burst and 50 HP laser payments,
natural separated hover, lifecycle retirement and unchanged native/default trace.
Its missing visual, blaster, network and replay gates remain open.

The explicit private Trilogy MP1 import successor now passes its full client
build, 997 unchanged GC and 1,388 Wii reader checks, existing preview compatibility
checks and 17 exporter contracts. Actual owned R3ME01 revision0/disc0 edition
archives match their dedicated pins. The production exporter privately emits all
four Troopers, and 22 accessed resource digests are independently verified. Each
body has 1,936 source vertices, 1,970 triangles and 35 bones. The real source has
167 named clips: 12 sparse samples, 142 indexed but unsampled, and 13 composites;
older donor counts are not substituted. Raw graphs retain 167 roots, 241 nodes
and 57 transitions without accepting named semantics. A separate paired actual
Echoes export through frozen78 and the refactor has identical six-file outputs.
No selector, runtime Trooper animation, full materials or gameplay is qualified.
See [the metadata-only packet](../../artifacts/cpp08-trilogy-mp1-source-private78-20261018/README.md).

All six existing Echoes arenas now pass a separate 600-step native bot world:
natural spawn/life, AI controls/navigation, movement and real floor/grounding
observations, 3,600 accepted steps total. These use one bot and zero humans under
the original MaxPlayers1 capability; no actor, input, health, pose or graph repair
is applied. Cannon route arrival, opponents/combat, human rendering, scoped
Echoes movement, water, faithful materials/audio and network/replay remain open.
The five-case summary is
`/private/tmp/prime-presentation-performance-review/echoes-five-bot-cpu77/five-arena-summary.json`;
the separate Spires receipt is `/private/tmp/prime-echoes-solo-bot77-fb51krie/world/result.json`.

The private Echoes static-source continuation identifies seven Spires pillar
CMDLs whose collision exists but whose world visuals were omitted. Its revised
source-bound importer prototype preserves collision and adds 602 visual faces,
retains primary per-corner UVs and passes 37 CPU checks, including mixed float/
short-UV selection, parser backward compatibility and complete parent mutation
refusal. This reviewed four-path importer is compiled into RML-77; actual
production import passes with unchanged collision/map identity. Gameplay remains
pending. Two real-window pilots stop before any simulation: the first exposes a
fixture native-library basename check, and the corrected path-based successor
passes that check but receives no OS keyboard focus within five seconds. Both
failures, cleanup and postguards are preserved. Multi-UV/TEV/source-normal
shading is not established. Raw ANCS time/blend and full/default/half-transition
records pass 781 private checks and are retained by the compiled RML-77 parser;
graph evaluation remains disabled until source proof and transition semantics
are bound through the existing lease.

RML-77 builds server/client/GL tool with zero warnings/errors and freezes 1,800
unchanged source inputs. Exactly 12 reviewed source paths differ from RML-76:
the second typed movie upload, three source-palette/world-pose paths, four static
Spires import paths, the ANCS parser, the headless music owner, and two opt-in
Kanden fixture paths. Source presentation now folds the existing world transform
into a separate bounded GPU palette, preserving raw skin matrices and 32-joint
ABI. The Kanden fixture passes 223 private actual-asset CPU checks and 54 closed
runner checks; its fixed 2 s neutral preparation is explicitly not an authority
ACK. The fresh 750 ms world passes with fourteen placements/detonations, nine
positive authoritative damage observations, 2,036 health comparisons without a
mismatch, four clean process exits and four fully read archives. Rendered bomb
pixels remain unmeasured. The headless music guard avoids
device construction through Music.Init and direct MusicPlayer initialization
while preserving normal startup. These are compiled changes, not runtime claims.

The single actual RML-77 landing successor passes its unchanged renderer/GL
checks and exits zero after 30.25 seconds. Ordinary StoryIntro/CA Landing reaches
ready at source frame 1,284, followed by all 240 probe frames. The capture retains
1,524 source observations and read-only canonical checks, plus 610 live ship
attachment changes. Cleanup returns with writer/window Scene cleared and visual
release complete; the global GameState still retains its old Scene reference and
is recorded separately. This is a local lifecycle/capture witness, not an aligned
original-game accuracy pass. The old RML-71/76 failures remain unchanged.

The current career-report handler rejects malformed match ticks and participant
ticks exceeding the match before SQL. A fresh closed run of both actual handlers,
all 24 current migrations, real WebCrypto HMAC and PGlite passes 215 checks across 14
cases with no external requests. This uses synthetic Auth and a proposed new
fresh-only business baseline; live League, original schema recovery and actual
world-to-report/replay acceptance remain open. The first actual private RML-77
world bridge completes five rounds/six lifetimes and returns five accepted,
eligible, rated reports, but its aggregate remains FAIL: the fixture feeds replay
facts without the normal Scene consumer and fills the pending bomb queue. An
independent raw-reader pass finds all five archives healthy. The original SQL
snapshot was not persisted before that assertion, so it cannot be reconstructed
as evidence from the original run. A normal-owner offline replay supplement and
a corrected fixture are separate work; neither upgrades that failed run.

Two later private world successors preserve that original failure. The first
refreshes the caller clock before scheduler admission and uses exact report-boundary
replay observations plus complete SQL/wire persistence; it fails with five startup
dropped steps. The next adds audited metadata/JIT preparation before the Run clock
(3,625 methods, 50 constructors, no synthetic Scene or simulation step). It still
fails with two dropped steps in its first round (177 processed frames); later four
rounds have 180 frames and no drops. All five reports are accepted/eligible/rated,
normal five-file replay consumption passes, and all process/asset/postguards close.
The first native step still costs 118.44 ms and allocates 28,125,856 bytes.
Strict zero-drop League acceptance stays open.
See `artifacts/cpp05-league-prewarm77-candidate/world-attempt1-FAIL`.

A separate readonly startup diagnostic records 95 bounded stage observations
across the five rounds, with no overflow or owner errors. The first native step
costs 111.77 ms; canonical bootstrap, bindings and first replica processing carry
the cold cost. Actor activation itself is small. The first replica step includes
session advancement before simulation callbacks, so its residual is not all
attributed to later entity processing. That run also fails with two first-round
drops. Its original evidence is preserved under
`artifacts/cpp05-league-startup77-candidate/world-attempt1-FAIL`.

The next actual prewarm-v2 experiment adds five fixed compile-only callpath types
and audited asset-property metadata initialization, with no startup diagnostic
hooks or hidden Scene/step. It prepares 3,832 methods and 57 constructors before
the Run clock, but still fails the unchanged strict gate: first round 177 frames
and two drops, later four rounds 180 frames and zero drops. First native step is
109.6658 ms with 28,000,952 allocated bytes. All five reports return 201 and are
eligible/rated with ten starters; full normal replay consumption and 147 saved
artifact checks pass. True process exit is 1, all processes/UDP close, and source,
assets and frozen inputs remain unchanged. This candidate is private and is not
in the delivered RML-78 build. Metadata preparation has not resolved League
startup acceptance. See
`artifacts/cpp05-league-prewarmv2-77-candidate/world-attempt1-FAIL`.

Native AMHE1 ARM9 analysis now identifies the Game loop counter, its inhibit
predicates, player input-copy owner and native pressed/held/released formulas.
The read-only v5 strafe attempt verifies all thirteen resident source spans,
room/Game/player owners, button formulas and the exact Samus Values pointer and
increment after the unchanged 7,799-frame bootstrap. It stops before probing:
the assumed 1/1 phase bytes are actually 0/0. That assumption is not relaxed to
fit the result. A source/serialized-register audit locates this initial sample
inside native Game Draw after actor processing; a separate raw phase diagnostic
is being prepared. The existing original and Project traces are not yet admitted
as matching endpoints, and all thirteen numerical comparisons remain open.

The V6 raw CPU diagnostic preserves its unsupported-stack failure. V7 publishes
validated CPU words before that exact typed refusal and skips unsupported stack
reads without relaxing format, TCM, input, source or A/B-state mutation checks.
Its single actual software run exits zero with 7,799 boot and 240 probe frames:
120 counter deltas zero and 120 one, with 119 unsupported-stack samples retained
as diagnostics. These facts do not establish historical actor execution or admit
an accuracy comparison. All thirteen cases remain NOT_VERIFIED; a separate
source-pinned execution-witness design is under review.

Private character work prepares a source-derived cannon cut and a bounded unlit
additive material4 path. The material path compiles with the full actual source
and passes 31 owner checks; its GPU checks and the combined production build are
pending. The default-disabled local Trooper wall-cling/rule candidate now compiles
with full production source and passes 6,512 actual-owner/asset checks, including
an exact 1,200-frame Native/off trace comparison. It remains private and has no
network, replay or public selection admission. The RmlUi offline local-arena
catalog and shared launch-cap preflight also compile privately and pass 39 policy
checks, preserving existing author-map admission and source-map network refusal.
These preparations do not qualify faithful materials, animation graphs, forms,
public gameplay, networking or replay.

A separate actual full78 plus dry Echoes movement candidate builds and passes
942 checks, including context-off pending jump-credit retirement. Its one owned
source CLI invocation imports and compiles all six explicit movement tiers with
new recipe/MapId/feature identities. Their native collision fingerprints match
the retained parents; collision hash alone does not identify new mechanics.
A paired whole-Scene default-off test on the unchanged static Spires recipe also
passes: 600 normal bot steps per actual78/candidate, identical semantic traces
and canonical bytes, all four processes exit zero and all guards pass. No
movement-enabled human/world, water, combat, network or replay acceptance follows.
The all-seven GX source-program candidate separately builds against full78 and
passes 240 actual-engine CPU checks, including stage16, operation vectors and
source-lease retirement. Whole-character GPU submission remains explicitly
refused until original sampling, UV/time, reflection and lighting owners exist.

Original buttons capture's earlier disk-full attempt retains 7,175 bootstrap
rows, zero probe rows, parent exit 1 and unknown child exit. Two successful fresh
runs do not relabel it. Storage recovery preserves every checkpoint path/byte
using 318 independently writable APFS clones of identical debug symbols, freeing
about 15 GiB without deleting source, media or evidence.

Parallel source implementation and local evidence are recorded in the focused
[CPP-00](cpp00-source-benchmark-2026-10-17.md),
[CPP-03](../architecture/cpp03-display-pose-contracts-2026-10-17.md),
[CPP-04](../engineering-next/cpp04-responsive-scope-fov.md),
[CPP-05](cpp05-match-lifecycle-2026-10-17.md),
[CPP-06](cpp06-alt-form-authority-2026-10-17.md), and
[CPP-07](../../tools/fidelity-oracle/DS-TRACES.md) records.
The next UI continuation is recorded in
[RML-47–51](../architecture/rmlui-continuation-rml47-rml51.md).
Implemented source and passing local fixtures do not close the distinct
original-data, clean-build, device, full-world or live-service acceptance.

The CPP-01 tracking decision allows migration work to continue. It does not
change AGX-04's recorded engineering acceptance or convert untested devices
into passing release evidence. Avalonia stays the default; native desktop
and Android remain opt-in, and full Settings migration stays last.

Current-state reconciliation used the local merge history, `git ls-remote
origin HEAD refs/heads/main`, and the open-PR list. The only open PR at this
check was #561 (native Radiant Map Studio); this slice does not change editor
ownership. Presentation fixes adapt behavior within the existing C# owners;
the imported-character continuation remains private and does not ship assets.

## October 18 — exclusive RmlUi completion

The user now prioritizes completing RmlUi exclusively. Separate networking, presentation-performance, character, arena and accuracy work is paused. Actual replay-world runs remain in scope only to exercise native UI ownership, input, editing and return flows. Full Settings remains the final UI slice and must follow the current working SettingsView layout; see [the layout requirements](../architecture/rmlui-settings-layout-requirements-2026-10-18.md) for the requested usability changes.

RML81 completes all 14 original Theatre smart views and duration sorting (80 index assertions and 29 real sidecar/projection assertions), and the contextual Social commands pass 293 native checks at each of 640×360, 960×600 and 1280×720 with synthetic authorities. One actual production Shell/DemoPlayback run passes all 134 steps at 960×600, including held camera resize/reentry, native timeline editing, seek/rebuild, source-sidecar ABA rejection and normal teardown. The 1,979-frame durable source replay, configured setup and all 3,858 game files remain unchanged. This is bounded Mac UI acceptance, not physical-input, networking or general performance qualification. Evidence: `artifacts/rmlui-parity-presentation-rml81-20261018/`. RML82 Community transfers and Hunter comparison/default staging are implemented but undergoing corrected compilation and runtime acceptance.


### RML-83 lobby trial, October 18

The latest user-requested lobby fixes are complete in the [RML-83 Mac ARM trial](rmlui-lobby-restoration-rml83-2026-10-18.md): matched side rails, actual selected-room map previews (including the RGB decode repair), authoritative empty-row Add Bot controls, and original owned MPH Samus art with the game tint cycle/warm controls. Three actual Mac lobby sizes pass with real models/art/map and synthetic command authority; the extracted signed application renders/resizes and captures the real AD1 room. Community transfer detail accessibility is corrected and all three targeted GL sizes pass. Full migration remains IN PROGRESS; the broader replay run's unexpected close during the paused timeline hold is recorded as INCOMPLETE, not passing. Full Settings and remaining parity/polish are still open. Work remains exclusive to RmlUi under the user's latest priority; other plan areas stay paused.


### RML-84 original chamber and four-seat Home, October 18

The user reversed the Samus/menu-color experiment and requested the reference platform layout. [RML-84](rmlui-platform-layout-rml84-2026-10-18.md) restores the original chamber, blue/cyan navigation and OPEN SLOT / + ADD BOT treatment, retaining real map previews and authoritative bot controls. Home now adds three party platforms and four Current Session seats, backed by the shared current-account Party owner for member names/presence. Native UI passes 699 checks at three actual Mac window sizes; five CPU suites and the signed/extracted Mac ARM package checks pass. Remote appearance data is not supplied by the Party service and is not guessed. Full migration remains IN PROGRESS; Settings stays last and all other workstreams remain paused.


### RML-85/86 Community library and reporting, October 18

[RML-85](rmlui-community-library-rml85-2026-10-18.md) restores native Installed/Favorites, exact links, metadata and previews. [RML-86](rmlui-community-report-rml86-2026-10-18.md) adds account-bound reporting and accessible detail labels. Five CPU suites, headless RGB preview and 788 native checks pass, with a signed/extracted Mac ARM build delivered. Account mutations remain qualified with fake authorities; no external report was submitted. Local package import is the next native continuation. Full migration remains in progress; Settings stays last.


### RML-87 native local package import, October 18

[RML-87](rmlui-community-import-rml87-2026-10-18.md) replaces the legacy Community import handoff with native file/path entry, private preparation, exact-hash review and explicit owner confirmation through the existing installer. Five CPU suites pass, including 142 import checks; actual installation passes 24 checks against an independent owned-file clone. All original files and the source package remain unchanged. Retina validation found and fixed native input/action and library row/paging overlaps; seven final density-2 cases pass 1,239 checks with 48 reviewed PNG/semantic captures. Local map registration and five runtime outputs are verified. Live backend/physical input, install-and-host, remaining Hunter/rich-screen parity and final Settings remain open.


## October 18 — RML-88 map picker and lobby usability

[Checkpoint 88](rmlui-map-lobby-rml88-2026-10-18.md) opens MPH maps while installed archive verification runs in the background, removes catalog-lock hashing, adds MPH/Echoes/Halo/Quake/Community tabs and six asynchronous preview bindings, separates the three Home party pads, publishes authoritative Start enablement and bounds chat controls. Five CPU suites, eight native cases (1,103 checks), two headless cases (167 checks), signed/extracted startup, packaged Home/resize and real AD1 capture pass. Physical Mac typing remains incomplete because desktop inspection repeatedly timed out; native scoped focus, typing and Enter/Send pass. The delivered Mac ARM ZIP is RML88. The migration remains in progress with Install & Host, remaining Hunter/rich-screen parity and Settings last.


[Checkpoint 89](rmlui-license-card-rml89-2026-10-18.md) adds the original MPH-style Hunter License card with locally decoded owned portraits, identity/rank/points and five compact record tabs. The top-right Profile opens a modal over Home or Lobby, including while connected; closing preserves the parent, focus and unsent chat draft. Retained account/privacy controls remain accessible. Final client/server and seven tools build with zero warnings/errors; six fresh CPU suites, three density-2 native sizes (1,485 checks), retained account/privacy acceptance, signed/extracted startup and packaged launcher/resize pass. Portrait decoding covers all seven original sources with unchanged-source reuse proof. The verified Mac ARM ZIP is RML89, SHA-256 `25dcd37b298d42398081e17727cb6c14d401dc3d648dd66eef51f983eb842cbd`. Native fixture values are synthetic; physical Cocoa keyboard/IME and the global profile dispatcher are not newly runtime-qualified by this slice. Wider migration remains in progress, with Settings last.

[Checkpoint 90](rmlui-license-stars-rml90-2026-10-18.md) adds all five original MPH star ranks, titles and next-rank thresholds/countdowns to the license using verified server points and numeric tier. Null tiers remain Unranked; inconsistent or unverified facts cannot create stars. Owned star textures decode asynchronously with exact alpha and a readable text fallback; the compact card fits at 640×360. The full client/server and seven tools build passes with zero warnings/errors; six CPU suites include 180 rank checks. Existing SQL tier/pair helpers pass 85 isolated checks. All five original star crops and seven portrait regressions pass; final native qualification passes 1,887 checks at three density-2 sizes, plus 579 fallback checks and retained account/privacy acceptance. The final asset-only RCSS repair preserves compiled binaries. Signed/extracted startup and packaged Home/resize pass; the delivered RML90 Mac ARM ZIP is 85,205,175 bytes, SHA-256 `fc3a461c58352d3af94d1b0391fddd5e8c7aec4cff80ba2bc998aa8f8eaa73a5`. Existing backend eligibility, normalization and persistence remain unchanged; live ingestion, physical Cocoa input and the global profile dispatcher are not newly qualified. Wider migration remains in progress, with Settings last.

### RML-91 replay library return, October 18

Closing a replay previously left the retained Theatre index marked Launched, disabling selection and Watch/Edit indefinitely. Shell now notifies the native menu after scene teardown and `DemoPlayback.Stop()`, releasing that flag while preserving selection, search and filter. The notification rejects active playback, another window, busy validation and disposed/unlaunched menu owners. Fullscreen and failed-launch returns use the same cleanup path.

Client/server and three tools build with zero warnings/errors. Both frozen CPU suites pass, including 39 library launch/return checks and 154 playback transport/viewport checks. Actual production Shell qualification at 960×600 (density 2) passes 28 steps: native Studio → Library → Watch → Library → another recording → Watch → Library → same-path Editor → Library, with three distinct sequential player owners, the same retained menu/index, real world pictures and unchanged archive bytes. This focused regression uses native keyboard action injection and suppresses unrelated thumbnail jobs; it does not close out the earlier broader timeline qualification or assert physical input. Evidence is `artifacts/rmlui-replay-reopen-rml91-20261018/build-attempt1/acceptance/actual-library-reopen-960x600/actual-window/acceptance.json`.

The signed/extracted Mac ARM package passes startup and Home/resize checks. All 1,936 frozen source/test inputs remain unchanged through delivery. The [RML91 download](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml91-20261018.zip) is 85,205,061 bytes, SHA-256 `9946718c1656c67b5be738288ab0ece1aa31d8f8e2e543826a0debf61e90545c`; the main binary is `330adae5dd82ba92644eaed3711284ee06eb0cd261268a13a07ba13db4b2e91c`, and the pinned RML89 native library is unchanged. One exact owned thumbnail worker from packaged resize qualification was stopped after the main process exited; cleanup verifies no owned processes remain. Wider migration remains in progress, with Settings last.


### RML-92 license, matchmaking and Studio polish, October 18

The Hunter License now shows the original red starting star for a verified, coherent zero-RP profile whose accepted server tier is absent. It explicitly remains Unranked until an eligible ranked result exists; this supersedes RML90's hidden-row presentation for that starting case without changing server points, tier or eligibility. Inconsistent or unverified facts still cannot produce a rank. Owned artwork and an accessible colored text fallback both pass. The overlay is capped to a compact height, adds verified career metrics, and removes the Customization tab while retaining account/privacy controls and the five card record tabs.

Home now reads MODE / ONLINE / MATCHMAKING. RANDOM MATCH continuously discovers compatible, nonfull public sessions, retries refused admission, shows elapsed search time and supports cancellation back to the browser. Exact attempt/session ownership rejects stale completions; competing actions and native header navigation stay locked during admission. This is public-session search through the existing directory and admission owners. The separate original SecondHunt hub's player grouping, arena voting and host allocation are not implemented by this slice.

Hunters now has catalog descriptions, clear equipped/draft/comparison state, guarded cosmetic switching and independently scrolling selection controls beside the stable real-model preview. All preview and footer controls fit the compact Retina layout. Community restores the forge backdrop and responsive panels. Studio opens a two-destination Map Studio / Replay Studio chooser, following the old migration's separation while preserving project drafts, recovery, publishing and replay owners. Corrected native media-rule syntax and compact decorative-art suppression prevent card artwork covering buttons.

Final replay qualification exposed a configured-path race: background library scans and launch preflight could observe Paths while a launcher status read republished it. Both workers now retain the existing configured-files capability for their read lifetime. The new deterministic regression fails against the pre-fix product and passes after the fix, proving status refresh cannot republish paths while either worker waits for replay ownership and that the capability is released afterward. The original first-frame failures and diagnostic runs remain in the artifact history. Three fresh final production Shell runs, with extra file diagnostics disabled, each pass all 28 library/Watch/close/second-recording/Editor/return steps, preserving the same controller/index and unchanged archive bytes.

Final client/server and nine tools build with zero warnings/errors; eight fresh CPU suites and the replay file/virtual-library/configured-reader suite pass. Native layouts pass at 640×360, 960×600 and 1280×720, including corrected Studio entry/editor cases, actual owned Hunter meshes, starting-star artwork, fallback and retained account/privacy controls. The final lease-only production change leaves all 136 UI resources byte-identical to the reviewed UI freeze; reuse and fresh acceptance are reconciled in `artifacts/rmlui-menu-polish-rml92-20261018/build-attempt13/qualification-reconciliation.json`. Discovery/account fixtures use synthetic authorities; physical Cocoa input and live original-hub/backend acceptance are not asserted.

The signed/extracted application passes startup and native Home/resize checks; all 1,981 frozen source/test inputs remain unchanged through delivery and no owned processes remain. The [RML92 Mac ARM download](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml92-20261018.zip) is 85,216,653 bytes, SHA-256 `efc8c4131481925315aba8269c7be5bb2e37168f99ca3679ec3dd4d697a5f570`. Its main binary is `a626ace94fda3334ed4811e2b2fa024d41f0033524f3670e26857ee84581dd69`; the pinned RML89 native library is unchanged. Package evidence is `artifacts/ProjectPrime-RmlUi-macOS-arm64-rml92-20261018-release13/`.

Migration remains IN PROGRESS. The next concrete gaps are Community Install & Host with an exact installed-package handoff, advanced Map Studio workflows still using the retained editor, an authoritative Recent Players source, and integrated startup/live-service/physical-input acceptance. Full Settings and HUD integration remain last. Earlier notes that list Installed/Favorites/import, Hunter compare/default controls, Theatre smart views or Home Party summaries as missing are superseded by their later completed checkpoints.


### RML-93 Community Install & Host, October 18

Community now offers Install & Host, and Installed offers Host Installed Map.
Both verify the selected package on a worker and register through the existing
owner before emitting a one-shot native lobby-form handoff. The handoff retains
configured root, map ID, runtime name, content hash and package hash. Selection
changes, cancel, root retirement and page disposal retire pending handoffs; the
durable download queue remains the existing owner. Offline-only local sources
cannot become online host selections. Installed hosting performs no HTTP fetch.

The native create form now includes arena and game-type controls in addition to
name and host location, reports the selected map's capacity, and scrolls in small
windows. Changing to an MPH arena clears the custom selection. Submission passes
the captured identity through capability validation and both existing local and
remote authority paths. Local preparation copies exact bytes into a private
library and rechecks identity; it does not stage loose editor sources or create
hardlinks to the installed archive. Remote host-request first-map identity and
rotation hash come directly from the captured selection. Existing committed
allocation and child-process ownership rules are preserved.

Client/server and nine tools compile with zero warnings/errors. Eight CPU suites
pass, including the new handoff, menu and host-request wire checks. Actual private
package qualification passes import, installed fast-path verification, idempotent
registration, capability/stale/offline-only rejection, cancellation, independent
server bytes, tamper refusal and cleanup. Four native pointer/scroll journeys pass 320 assertions at logical 1280×720,
960×600 and 640×360, including the equivalent Retina layout. Installed/Favorites
and random-search native regressions also pass. The signed and extracted app
passes startup and actual Mac window/resize checks, and all 1,985 frozen
source/test inputs remain unchanged through delivery. The window-check thumbnail
child required explicit cleanup of the recorded owned PID; automatic thumbnail
worker teardown is not certified. No RML93-owned process remains. This checkpoint
does not certify live remote allocation, another client's download, or physical
Cocoa input.

Migration remains IN PROGRESS. The next concrete native editor slice is Validate
Map with Problems and Budgets over the retained Map Studio owner. Other advanced
Studio operations still use the retained editor. Authoritative Recent Players,
integrated startup/live-service/physical-input acceptance and final Settings/HUD
integration remain open. Settings stays last and should follow the existing
working settings layout.


RML93 delivery: [Mac ARM ZIP](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml93-20261018.zip),
85,224,380 bytes, SHA-256 `1fdab3b3c3b0822e59aaa1e72b9fcc6a7b9fd06e5fd1c79a4f4eb30638520c84`.
Main binary: `657fe75051ab779914f31dbfaad0368032e9e8fca2603ab5daf406284355c19e`.
Pinned RML89 native library remains unchanged. Final evidence is
`artifacts/rmlui-community-host-rml93-20261018/build-attempt4/`, CPU evidence is
`artifacts/rmlui-community-host-rml93-20261018/acceptance/cpu-attempt2/`, and package
provenance is `artifacts/ProjectPrime-RmlUi-macOS-arm64-rml93-20261018-release4/`.
The earlier failed actual-package test mutated a definition retired by
idempotent registration; the fixture now reacquires the published definition.
The failure and corrected run are preserved in attempts 2–3. A final CSS height cap removes excess
blank space on large windows without changing the main binary.


## RML-94 — separate Prime Studio and Main Theatre, October 18

Direct user scope revision: disable Map Studio and show Coming soon; restore a
separate Prime Studio app containing that future placeholder and the current
replay editor; add Theatre to the game for basic viewing. This supersedes the
RML-93 proposal to continue advanced in-game Map Studio. Dormant map-owner/build
source remains future code and is not a remaining migration requirement.

The separate app uses the historical `codex/rmlui-game-client-migration`
`31c3b20d4d8cc56ea3e4c5587798ce27e72b80fc` process/IPC reference with native RmlUi
presentation. It is `Project Prime Studio.app`, executable `ProjectPrimeStudio`,
bundle identity `com.projectprime.studio`. Game and Studio have independent native
windows, replay owners and instance leases. Authenticated, bounded local IPC
forwards absolute logical replay/clip paths and queues requests without replacing
an active editor or dropping a dirty Settings draft. Map launch/import/project/
publish/edit handoffs are refused by the native destination hub.

Main Theatre retains transport, seek, speed, player/camera viewing and fullscreen.
Its library and playback open editing in the separate app. Main playback creates
none of the bookmark/range/camera/export/editor capabilities; typed commands and
editing shortcuts cannot acquire them. Replay error/recovery paths now preserve
that boundary instead of reopening the old editor. Fatal native-host failure
closes the native application with a logged error instead of reviving legacy UI.

Validation: client, server and nine tools compile with zero warnings/errors.
Eight CPU suites and 18 authenticated instance checks pass. Native hub/header
checks pass at client 640×360 and 960×600 with density 1, plus 640×360 density 2;
actual UI controls remain separate and all map intents are refused without
acquiring a map owner. Viewer/editor modal capability checks pass. Production
Shell replay runs pass 33 Main steps at each of 960×600 and 640×360 and 27 Studio
steps at 960×600, including repeated recording opens, returned-library reuse,
resize, blocked Main editing shortcuts and Studio inline editor reopen. These use
owned synthetic durable recordings and actual world presentation, not physical
keyboard/controller input or a deployed-service acceptance claim.

Both ARM app bundles are independently copied and signed, pass packaged window/
resize checks, and retain identical verified inventories after ZIP extraction.
Their bundled runtimes start successfully after extraction. No game data is
included. Source and packaged assembly hashes are checked, and no owned
qualification/package processes remain. Earlier test failures are preserved:
header animation/focus translation and a non-semantic brand DIV were corrected
in qualification assertions; production header CSS did not require a repair.

RML94 delivery: [Mac ARM ZIP](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml94-20261018.zip),
170,528,407 bytes, SHA-256 `2f854bdb1634821b50bc810adbb66047db88f8c7f8489d95122058f3878e585e`.
Keep both apps in the same folder. Main assembly:
`04e250d631b7a5167c1242f254b2c93e0cd7b555c489b6fd02890944de6ec9d0`;
native library: `0876e31b45408141118ec3951c64563726dda44efe801e361a20ef8e07e32bb0`.
Build/CPU evidence: `artifacts/rmlui-prime-studio-rml94-20261018/build-attempt3/`.
Final GL/replay evidence: `artifacts/rmlui-prime-studio-rml94-20261018/qualification-attempt5/`.
Package/ZIP/signature/process receipts:
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml94-20261018/`.

Migration remains IN PROGRESS. Full Settings and HUD are the final substantial
normal product UI routes to migrate, following the existing working SettingsView
layout. Setup/pause/results/campaign recovery, live backend availability and
physical-input acceptance remain separately tracked. RML-94 does not claim total
migration completion.


## RML-95 — in-game Studio chooser, Home rails, Hunters chamber and startup, October 18

The user's latest correction supersedes RML-94's header launch: PRIME STUDIO now
opens the two-card chooser inside the game. OPEN MAP STUDIO launches the separate
app's Home/Coming soon placeholder; OPEN REPLAY STUDIO launches its native replay
library. The separate app still disables map authoring. Main Theatre's logical
recording handoff and viewer/editor capability boundary remain intact. The bounded
local protocol adds a path-free ReplayLibrary request without renumbering existing
kinds; authenticated forwarding and CLI rejection cases pass 33 CPU checks.

Home restores the original Friends section below all four Current Session seats.
Verified friends, requests, invitations and shared presence use their existing
projections; unavailable data stays explicitly unavailable. Both utility columns
scroll at compact sizes, preserving news, Mode and Social action reachability.
Two additional headlines come from the existing bundled news provider. Native
pointer activation reaches the real Social page through the existing launcher
controller; those account fixtures are synthetic and make no deployed-service claim.

Hunters explicitly repaints the original procedural deployment chamber before its
translucent native UI, then draws the isolated real Hunter mesh after UI in the
native preview rectangle. The compositor restores framebuffer/viewport/scissor
state; a retained magenta photo sentinel leaves zero pixels after the chamber pass.
No Samus menu background or new native ABI is introduced.

Migration continues with the startup splash: native RmlUi owns Press Start and the
unchanged supplied Project Prime artwork. Pointer, Enter, Space and the existing
controller Accept route complete the foreground gate once. The native build no
longer allocates PrimeStartupScreen. Update prompts wait while the gate is pending;
updates/version dialogs themselves remain a later migration. The authored RGB PNG
is losslessly converted to the renderer's supported uncompressed TGA by
`tools/rmlui/convert-startup-art.py`; this authored asset alone is explicitly allowed
by the asset guard. It contains no cartridge data.

Qualification: client/server and nine tools build with zero warnings/errors. Eight
CPU suites pass. Three native cases at client 640×360 and 960×600 density 1, and
640×360 density 2, pass 1,070 Studio/header, 258 Home rail and 99 startup checks.
Actual production Shell runs pass 43 Main steps at both sizes and 27 Studio steps,
including native startup, internal header navigation, explicit workspace launch
requests/refusal/retry, repeated recording opens, resize and the editor boundary.
The process-launch boundary in these GL runs is injected; actual authenticated
workspace forwarding is covered separately by the CPU protocol harness. Real
cartridge-model stage acceptance passes 3,174 checks, including the new chamber
sentinel and unchanged canonical scene/RNG. No physical event delivery, live backend
availability, device performance or full migration/release completion is claimed.

Both independent ARM app bundles pass signatures, packaged render/resize checks,
extracted inventory/signature checks and bundled-runtime startup. Keep both apps in
the same folder. [RML95 Mac ARM ZIP](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml95-20261018.zip):
176,819,943 bytes; SHA-256 `c71b13616aba4b64c754fc8ce411794e7422846b6fc517f42702cfb0d716acd0`.
Main assembly `76b65439f04d66b8b00081515ec6aa979795518d769512d2634be68dd1056919`;
unchanged native bridge `0876e31b45408141118ec3951c64563726dda44efe801e361a20ef8e07e32bb0`.
No game data is included and original player data is untouched.

Evidence under `artifacts/rmlui-home-studio-rml95-20261018/`:
`build-attempt4/` (core/CPU), `qualification-attempt5/` (native composition and actual
replay runs), `hunters-qualification-attempt6/` (real models), and
`startup-art-conversion.log` / `studio-instance-cpu.log`. Package receipts are in
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml95-candidate2-20261018/`.
Earlier failures are preserved: compact selector width/focus overflow, plain DIVs
absent from semantic readback, unsupported direct PNG loading, test bootstrap opt-in
sampled too early, and a stale disabled Map workspace test binding. The last two
were test-only corrections; production hashes did not change afterward. An initial
private package stage lacked its README and was preserved before fresh staging.

Migration remains IN PROGRESS. Remaining normal routes include full Settings and
HUD (last, following the existing working SettingsView layout), updates/version
management and specific setup/pause/results/campaign/create-lobby recovery routes.
Disabled Map Studio remains intentional future scope.

### RML-96: centered Home and reliable tab transitions

The latest requested composition now uses actual native left/right rail bounds.
The Hunter viewport and four chamber platforms share one uniformly scaled layout;
the rear platform is wholly above the model viewport and the other pads fit the
central gap. Real seven-Hunter/four-suit runs pass 12,144 checks across 640×360,
960×600 and the user's 1512×818 client size. Root visually inspected Trace's full
Home composition at 960×600 and 1512×818.

Native page routing now consumes repeated active-tab commands and explicitly
navigates Home after native pages, fixing retained Theatre destinations resurfacing
through PLAY. Header active styling reflects the actual foreground, keyboard
adjacency includes Theatre, and dirty Quick Settings explains Apply/Discard.
Actual production windows pass all 25 directed pairs at both 640×360 and 960×600,
including pointer mapping and native keyboard activation, same-page owner retention
and unchanged discarded settings. Main and Studio replay close/reopen tests pass
557 steps. The discovery fixture is read-only/empty and the process-launch boundary
is injected; no physical-input, child-process or live-backend acceptance is inferred.

Migration continues with the automatic update offer: metadata waits on the existing
StartScreen until native Home owns presentation. The native modal makes one guarded
decision through the retained updater, defaults to Later, and blocks covered-page
commands. Mac confirmation retains the existing release-page behavior. Three final
native size/density cases pass, including contained long version tags; actual Shell
runs queue a synthetic offer in Theatre, show it at Home, then choose Later without
acquiring installation, browser or playback effects. Explicit release browsing,
version-switch confirmations and update progress still need migration.

Evidence: `artifacts/rmlui-centered-tabs-rml96-20261018/build-attempt2/` and
`qualification-attempt7/`. The 12,144 real-model checks are in `qualification-attempt3/`.
Eight CPU suites pass on the final `qualification-attempt7/` freeze. The separate
playback-capability GL run passes in `qualification-attempt2/`; its final main/native
binaries and used resources are identical. Only unrelated update-modal RCSS
subsequently changed, and its native/actual Shell cases were rerun. The exact
source/runtime delta is frozen in
`qualification-attempt7/evidence-inheritance.json`. Earlier failed test assumptions,
focus interruption, warning and clipped-version-row runs remain preserved. The
first private signed candidate was not delivered; candidate2 contains the corrected
update-card CSS.

Delivered: `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml96-20261018.zip`
(176,843,162 bytes), SHA-256
`d7e8e019235d1a49834d8aa6664b74b21defd5cc7aee2e2c5a4032156968407c`.
Main DLL SHA-256 `9bef44d3496fb3f9d4ee6f2a662c618f49b185d1fb5c8ff25b536ad99ec11848`;
native bridge remains ABI224, SHA-256
`0876e31b45408141118ec3951c64563726dda44efe801e361a20ef8e07e32bb0`.
Package evidence: `artifacts/ProjectPrime-RmlUi-macOS-arm64-rml96-candidate2-20261018/`.
Both independent signed apps pass packaged window/resize, no-game-assets, ZIP
extraction, exact inventory/signature and extracted startup checks. Keep both apps
together; PRIME STUDIO still opens the internal chooser before explicit launch.

RmlUi migration remains open. Full Settings/HUD stay last; explicit version
management, setup/native failure recovery, constrained Create Lobby and integrated
acceptance remain. Map Studio is intentionally a future placeholder.

### RML-97: desktop RmlUi migration completed and Mac copy delivered

Full Settings/HUD, explicit version management, native setup/recovery, quit and
constrained lobby presentation complete the remaining desktop UI ports. The Mac
native profile defaults to RmlUi; normal native owners no longer fall back to the
old UI. Settings follows the working category/scrolling layout, with draft-safe
HUD edits and a drawn-frame-gated display preview/rollback. The old branch's actual
Replay Studio camera graphs/Bezier tools, four views, spatial tools, private-world
comparison, portable imports/exports and custom cue mixing are restored. Theatre
remains a viewer; Map Studio remains intentionally Coming soon.

The chamber/platform composition, transparent license stars, explicit Social/News
backgrounds, reduced-motion-aware feedback and supplied menu music/original MPH
menu cues are qualified. Client/server and all eleven tools compile with zero
warnings/errors. Ten CPU suites, three final Settings size/density cases, actual
Main at two sizes, actual Studio, three real seven-Hunter stage sizes, two license
sizes and viewer-capability checks pass. Earlier failures and exact final
source/resource inheritance remain preserved. See
[RML97 completion and validation boundaries](rmlui-migration-completion-rml97-2026-10-18.md).

Delivered: `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml97-20261018.zip`
(185,278,311 bytes), SHA-256
`d1479169acc1ecad61c151213c16a42387c786bd61e1f395c9faa3abc71d458c`.
Main DLL SHA-256 `619a61233634fe6786107b18d107ed8e2cb45351ecee163049858d9771c4c25b`;
native SHA-256 `7a12d86f3c38454b66d8f2fd11ce9642cfa386118b9b1fa47ded22bdc726a3a8`.
Package evidence: `artifacts/ProjectPrime-RmlUi-macOS-arm64-rml97-candidate3-20261018/`.
Both independently signed apps pass packaged render/resize, no-game-assets,
extracted signature/inventory and startup checks. Keep both apps together.

This closes the desktop UI migration under Mac-only validation. Live account and
matchmaking services, physical input, other platforms, refresh-rate performance,
content readiness and original-game accuracy retain their separate plan status.
Full campaign/ending validation remains excluded by the user's earlier direction.

## October 19 — CPP-03/04 presentation resumed and completed

The user explicitly resumed the Presentation — CPP-03/04 goal after the exclusive RmlUi continuation. Its implementation and requested macOS validation are complete; other work retains its recorded status. The final fresh source/runtime, native matrix, CPU fixtures, deterministic content regression and headless/rendered replay evidence are consolidated in [the completion record](cpp03-cpp04-presentation-completion-2026-10-19.md). Concurrent Studio UI edits remain intact and outside this isolated presentation validation. Physical-device, other-platform, live-network impairment and broader release/performance qualification remain distinct.
