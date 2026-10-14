# A2 verified timed pickup claims

**Status: measured reconciled A2 correctness PASS; stack merge remains gated by pacing adjudication, A3 acceptance and final-head CI.** Gameplay evidence is bound to clean source `467d748f687c1fc55f854c986f7304d7151c76d0`, client SHA-256 `2e8d22c2c0bf057ec203e2361edd082545a32583294eb4451391885a35b505d0`, loaded client MVID `8c984a50-1996-4fd4-a8c6-3da2dbeb37f5`, and protocol **55**. Follow-up source `c7833972c9bc5e6f15778038e19330dd9ae4953c` changes only `ClaimStressTests.cs`; its corrected harness passes against that same frozen gameplay binary. This documentation update changes no runtime code. The 467 results retain their original source qualification; they are not fresh builds or completed CI for a later head.

Protocol 55 combines current main's native-touch intent with A2 pickup messages. Staged A2 used 53 with a different intent layout, and staged A3 used 54; matching live client/server builds are required. Current private PPRC checkpoints use **version 12, feature flags 3** (NativeTouch plus PickupTables). Historical manifests below remain protocol-53 evidence and cannot accept the reconciled protocol-55 build.

## Hypothesis and scope

Exact authority-observed contact, item incarnation and bounded immutable terminal delivery can verify timed pickup claims without duplicate consumption or renewal. The measured results below support that bounded contract; final merge gates remain pending.

## Parent/base and exact source/binary identity

A2 PR #473 is stacked on reconciled A1 PR #472, code source `a8f309e5761246410d378c88f5a2aa608a436238`, preserving reconciled main `16b1d89bfb6ed7776b801b3e39d513e740ee5e58`. Gameplay source/hash/MVID and the fixture-only c783 qualification are stated above. Integrating an A1 documentation update does not change these gameplay results; the resulting head still requires hosted CI.

## Changed files and component ownership

A2 owns `NetPickupClaims`, pickup codecs/transport/readiness, `PickupItemHistory`, native contact adapter hooks, canonical pickup replay/checkpoint storage and their focused/audit fixtures. It consumes A1's authority ledger rather than replacing its ownership. The independent N4 correction owns the narrow `NetHitClaims.Compose` retry schedule plus `TerminalRetryTests` and Program dispatch; c783 changes only `ClaimStressTests.cs`. This commit changes only this A2 document. Complete changed-file ownership is reviewable in the PR diff.

## Baseline/reproduction (including negative controls)

The 4b impaired campaign FAIL, frozen-old-engine terminal falsifier FAIL and 467 hosted/local stale ClaimStress FAIL remain retained, as detailed below. The 37c protocol-53 acceptance is historical; source-qualified fresh 467 positives do not relabel these negatives.

## Implementation and fallback/rollback

Captured native contact plus exact incarnation/life decides settlement; immutable cached verdicts recover retransmissions. The retry repair reserves two late requests within existing bounds. Rollback deploys matched clients/servers and preserves main's touch, A1 and retired-feature policies; the full rollback section below forbids wire relabeling. Canonical replay-baseline reconstruction remains the explicit fallback for incompatible durable capsules.

## Compatibility impact: wire, replay, saves, gameplay, settings, assets

Live protocol is 55; PPRC v12 flags are 3. Historical replay layouts require detached, complete, unique validation. The pickup contract introduces no save-format change, new settings migration or asset-format requirement. Fresh test profiles copy only original asset paths, with separate exports. Native/offline prediction, damage/payment and main's retired Enhanced behavior remain as described below.

## Tests: local + exact-head hosted CI

467 has 38 focused PASS, 460 Python tests with one explicit skip, both strict UDP campaign audit sets PASS and 12 metadata/replay commands PASS. The current full 39-route harness passes at `ac435eaf4a088f11898e8eca80df5ed10043e5ff` against the unchanged frozen 467 client; its physical harness SHA-256 is `6836ba4784a0511d9c59d382b03dca16d6472325919920aaec9756b8140c7ff7`, MVID `36d1816a-98ff-4038-b475-7cd834974409`. The corrected receipt is `work/net21-reconcile/a2-39-ac435eaf-deps/receipt.json`. The initial loader setup attempt remains separate: `a2-39-ac435eaf/` records one PASS and 38 missing-client `FileNotFound` failures; none are relabeled. All 30 hosted checks pass at ac435eaf. Later documentation/base heads require their own CI; the original stale-fixture hosted run remains FAIL.

## A/B: full samples, median and p95/p99/p99.9; GPU/managed/native where available

Matched A/B and native/GPU render performance are UNVERIFIED. Full per-process correctness samples remain in raw campaign JSON. Descriptive simulation median/p95/p99/p99.9 is 0.38/1.31/3.40/11.46 ms LAN and 0.39/1.16/1.89/5.76 ms impaired. Allocation, traffic and queue data below are descriptive; neither observed dropped steps nor stalls qualify as zero or performance-clean.

## Fidelity: world/HUD/depth/stencil, replay hashes, server authority

Fresh replay continuation/seek/checkpoint hashes and native authority contact/reservation/terminal conservation PASS within the declared fixture/campaign scope. Durable capsule fallbacks remain explicit. World/HUD/depth/stencil rendering, rendered projectile agreement and exported `.ppclip` fidelity remain UNVERIFIED; metadata inspection does not count as playback.

## Missing or inconclusive gates (NOT_RUN / UNVERIFIED / FAIL)

The final 39-route inventory and ac435eaf hosted CI pass; final stack/base-head CI and pacing adjudication remain pending. Live native-reservation acknowledgment and Cloak/Death Alt claims are NOT_RUN with fixture coverage only. Native GL/audio, physical-device gameplay, controlled pacing, matched render/GPU/managed/native A/B and exported clips are UNVERIFIED for this A2 binary. Historical 4b campaign and 467 stale-fixture hosted run remain FAIL; no excluded timing overlap or rerun converts them to PASS.

## Remaining risk and next dependency

Client-reported movement remains a trust boundary, capacity/history/deadline limits remain explicit and arbitrary packet loss is not guaranteed recoverable. A3 presentation reconciliation, A4 shot-specific damage authority, dynamic owner identity and R0 remain next dependencies. The remaining merge gates apply to the exact final documented head.

## Preserved invariants

This slice verifies authenticated timed pickup claims, historical native contact, incarnation discovery, immutable terminal delivery and canonical replay facts. A1 remains the authority reservation/source-history layer. New awards require an actual captured pose and available exact incarnation; acknowledging an existing native award cannot create or renew one. Offline/native prediction, movement reporting, ammo and weapon journals, current-main native-touch semantics and bounded unsigned frame rollover remain intact. The retired Enhanced hunter policy remains intact: an `enhanced` headless fixture sets a legacy compatibility flag on a normal ServerSim scene; `EnhancedHunters.Enabled` requires replay scene services, so this route does not restore live Enhanced behavior.

A3 presentation reconciliation, A4 accepted-shot damage authorization, dynamic-drop owner mapping, independently authoritative movement and R0 release readiness remain outside A2 acceptance.

## Current source-qualified validation

The frozen 467 runtime passes **38 focused routes**, including asset-backed native ledger fixtures, replay/checkpoint compatibility and terminal-retry contracts. The Python suite passes **460 tests with one explicit skip**; `git diff --check` passes. Source and runtime hashes remained unchanged through these runs. The original 38-route inventory omitted the existing `--claim-stress` route; the expanded final inventory has **39** routes, and its final-head run remains pending. The old receipt is not relabeled as 39 passes.

Hosted run `38480692397`, Linux job `115582459561`, **FAILS** on 467's stale ClaimStress expectation that all six sends occur before prediction expires at age 60. The new terminal-retry contract passed in that run. The original failing log and local reproduction remain retained. Fixture-only follow-up `c7833972` instead requires four early attempts, two late attempts at ages 184/192 for unknown RTT, exactly six total attempts, unchanged prediction release and receipt expiry. Its corrected frozen-engine harness passes locally. Fresh hosted CI on that follow-up and the later documentation head is a required gate; the original hosted failure is not waived.

Two fresh Fuel Sluice campaigns use ordinary native power-ups, Shadow shot proof, AI level 2 / seed 1729, fault seed 8128, recordings and a 35-second drain budget. LAN has two clients plus an authority bot for 60 seconds, port 28102. Impaired has eight clients for 180 seconds, port 28095, 250 ms RTT, 40 ms jitter, 5% loss/reordering and 2% duplication. All three/nine processes exit zero without timeout; campaign, AI, strict pickup and native-grant audits all PASS. Native owner pickup totals include other pickup types and are a separate denominator from timed grants.

| Current 467 campaign | Pickup claims / authority terminals / owner receipts | Granted claim terminals | Native Double Damage grants / closures | Owner native pickups | Declared / received / applied hit claims | Component payments / paid AI source keys |
| --- | --- | ---: | --- | ---: | --- | --- |
| Two-client LAN plus authority bot | 2 / 2 / 2 | 2 | 2 / 1 Death, 1 Retired | 5 | 15 / 20 / 1 | 51 / 17 |
| Eight-client impaired | 4 / 4 / 4 | 3 | 4 / 4 Death | 61 | 677 / 884 / 21 | 605 / 596 |

Received hit-claim counts include retries. The LAN authority bot produces 188 shot keys. Canonical deaths/spawns/respawns are 5/9/5 in LAN and 92/106/92 in impaired. All six raw timed grants close, with zero open grants and zero unknown source boundaries. The fourth impaired claim receives TooOld, rather than a grant, and its owner receives that terminal. All five granted proofs use captured source F through F+2; native-reservation acknowledgment remains live NOT_RUN with fixture coverage. Live timed pickup claim/grant coverage here is Double Damage; Cloak and Death Alt remain fixture coverage. Exact proof rows remain in the raw ledger and strict audit.

Four independent original recordings (LAN peers 0/1 and impaired peers 6/7) each pass `-demoinfo`, `-replaydeterminism` and `-replayreplicacheck`: **12 commands PASS**, including eight replay commands. Metadata inspection is separate from playback. Canonical checkpoint/seek and frozen-memory clip checks do not establish exported `.ppclip` fidelity. Durable firing-contract mismatches use recorded-baseline reconstruction: each LAN recording logs 90 determinism and 12 replica fallbacks; each impaired recording logs 262 and 37. These successes do not establish direct restoration of incompatible durable capsules.

## Retained failure and terminal retry repair

The original impaired campaign at `4b6c9a9462249528d867e4d9105cd51ba7469dd9` remains **FAIL**, despite all nine processes exiting zero and completing bounded drain. Peer 6's exact shot `639275270550480360/2/6/1/2/5` exhausted six fast requests while turret arbitration was pending; its terminal receipt timed out. This is an inherited N4 delivery gap exposed during A2 acceptance, not a new pickup or touch admission rule. Original request/authority/terminal/timeout logs remain retained; the corrected-source campaign is separate positive evidence.

The wire-neutral repair reserves the final two of six hit-claim attempts until age **184**, comprising the maximum arbitration window `2 * 72` plus the existing maximum RTT resend margin 40. The first four keep their existing measured RTT cadence; the last two retain that spacing and fit before the unchanged **424-frame** delivery deadline. Prediction still expires after age **60**. Full ShotKey, payment/cache/life authority, damage rules and packet layout remain unchanged. This schedule is separate from A2 pickup delivery's six-send/500-ms wall-time policy.

The actual Compose/pending-authority/cache/ApplyVerdicts fixture passes **2,756 assertions**: terminal production after 59/144 frames, loss of the sole initial answer, late retransmission of the original request, cached terminal recovery, receipt closure without reviving prediction or damage/payment, zero/known RTT, frame rollover, exact six-send cap and death/new-life fencing. The same harness bound to the original 4b engine fails its late-request recovery assertion; that falsifier and the old live FAIL remain retained. Fixture coverage does not promise delivery under arbitrary packet loss.

## Metrics and evidence locations

Server simulation telemetry below is descriptive, from headless correctness campaigns with real assets and null audio. It is not a controlled performance comparison or native render-frame measurement. The observed dropped steps/stalls prevent a performance-clean claim.

| Current 467 campaign | Steps | p95 / p99 / p99.9 step ms | Worst step ms | Failures / dropped / stalls | Step allocation bytes | Server receive / send bytes | Queue high-water / drops |
| --- | ---: | --- | ---: | --- | ---: | --- | --- |
| LAN | 3,841 | 1.31 / 3.40 / 11.46 | 113.0497 | 0 / 1 / 1 | 91,574,584 | 1,347,051 / 9,376,941 | 5 / 0 |
| Impaired | 11,308 | 1.16 / 1.89 / 5.76 | 118.2588 | 0 / 2 / 1 | 370,950,360 | 17,637,741 / 265,620,301 | 9 / 0 |

Both server transport captures have zero invalid packets, oversized packets and socket errors, with queue current zero. Per-process allocation/heap/working-set totals, transport windows, queue/admission counters, contact proofs and raw identities remain in the campaign JSON; they are not isolated A2 allocation costs. Native OpenGL/audio gameplay, CPU/GPU render-frame p95/p99/p99.9, matched rendered projectile agreement, physical device acceptance and a final matched performance baseline remain **UNVERIFIED**. The `enhanced` asset route is a legacy-flag correctness fixture, not live retired-feature or GPU acceptance.

The task bundle `work/net21-reconcile/` retains exact commands, UTC, exits, source status, loaded module identities, asset/runtime/log hashes and unchanged binaries. Current entry receipts are:

- `a2-acceptance-467d748f/acceptance-summary.json`, `python-diff-receipt.json`, `lan-receipt.json` and `audit-receipt.json`;
- `a2-38-467d748f/receipt.json`, `a2-replays-467d748f/receipt.json`, and both `a2-udp-2-bot-fuel-467d748f/` / `a2-udp-8-fuel-467d748f/` campaign ledgers;
- `terminal-retries/proof-receipt.json` for the old-engine falsifier and corrected deterministic proof;
- `a2-ci-failure-467d748f/ci-failure-receipt.json`, `job-115582459561.log` and `fixture-proof-receipt.json` for the retained CI failure and frozen-engine fixture correction.

The 467 harness SHA-256 is `f23214dedbb59f8ebcac14b0b7eb503deadf141557758d1ccd6cfa025fbe0c97`; the fixture-only corrected harness is `c20862bf3909e206ef776d24e6e1733714efad6a7590a3fc86268fc17bed73a9`. Both bind the 467 client hash above. Isolated campaigns/replays copy only asset paths into fresh profiles, with separate exports and no copied credentials/settings. The older committed manifest below retains its historical source qualification.

## Protocol and identities

Protocol 55 retains PickupClaim (73), PickupVerdict (74), PickupItem (75) and PickupReady (76). Claim keys contain epoch, match, authenticated slot, slot generation, life and a nonzero process-monotonic sequence. Item keys retain A1's epoch, match, room, spawn ID and monotonic incarnation generation. Neither native spawn quota nor client-reported position invents an incarnation. Hosted and dedicated dispatch derive ownership from the authenticated endpoint, validate match/readiness and direction, and do not trust an arbitrary payload slot.

| Packet | Exact payload | Contract |
|---|---:|---|
| PickupClaim | 48 bytes | Full claim/item identity, timed type, source frame and finite player position |
| PickupVerdict | 73 bytes | Original exact claim, terminal result, authority decision/proof frames and admitted grant sequence/contact/expiry when granted |
| PickupItem | 40 bytes | Authority incarnation/type, frame, availability and finite current position |
| PickupReady | 16 bytes | Post-load epoch/match/admission generation and monotonic scene sequence |

Codecs reject truncation, trailing bytes, malformed identities/types/statuses and non-finite or out-of-bound positions. Positions must have absolute components below 100,000. Non-grant verdicts carry zero grant sequence/contact/expiry; a grant requires a nonzero sequence and native 1,800-frame expiry. Fixed discovery/verdict facts are batched within 1,200 bytes including the 24-byte network header and four-byte reliable envelope. Reliability and realtime inbox registration are explicit rather than fallback behavior.

## Contact proof and timing

`NetPowerUpLedger.ObserveContact` captures the player's value-only native contact pose and current registered items before native consumption. `NetPickupClaims` retains the immutable first contact observation for each full life/source-frame pair; repeated input does not move or rewrite it. A new award can use actual captured source observations F through F+2 to account for post-step contact versus the next pre-step intent, while requiring the same geometry and bounded timing proof. No synthetic or overwritten pose is introduced. Samples contain authority frame, player position, alt sphere position, native radius/height limits, form, alive/pickup eligibility, and exact item identity/type/position/availability. There are at most 128 retained contact observations per slot, each containing at most 128 live item facts. Source-clock plausibility allows at most 32 frames of advance or 128 frames of lag relative to the life anchor.

The claim position must lie within 0.75 game units of the captured authority player position. Both that captured position and the claimed position must satisfy the actual native geometry: alt-form sphere distance strictly below radius plus 0.45; biped lateral distance strictly below the same radius, with inclusive native minimum/maximum height. Moving-item proof uses the captured ItemInstanceEntity position, not the authored spawner origin. This preserves native contact geometry; it is not an independent map-collision or movement proof.

The authority derives a bounded proof age window from measured RTT and jitter: full RTT plus two jitter margins and six simulation steps, clamped to 12 through 120 frames. Client timestamps cannot widen it. Owner contact is post-step while reported authority poses are pre-step inputs normally sent every two ticks. Proof may use an actual captured source sample at the contact frame or either of the next two source frames, with both captured and claimed positions still satisfying the same native geometry, availability, life and age checks. Missing poses are never interpolated. Existing movement/shot transmission cadence is unchanged. Source input and claims use separate lanes; an incoming claim may wait up to 24 simulation frames for its actual following contact observation when the first pose cannot prove contact before receiving a terminal HistoryUnavailable result. The delivery deadline remains separately bounded in wall time. Missing history, wrong life, unavailable incarnation, excessive age or failed geometry cannot become an implicit grant.

A separate immutable native-reservation proof table holds at most 512 observations captured at actual native contact. Every proof joins the exact retained grant sequence, source frame, life, spawn, type and authority contact frame, and must pass the independently anchored source clock, native geometry, 0.75-position tolerance and RTT-derived window. A native proof may span source delta 0 through that window, but only acknowledges the exact existing award. It can never call the commit path to create or renew a grant, and it never replaces the first-source observations.

Current life/alive state is required for new settlement. An already successful native pickup for the same life/type/incarnation can be acknowledged with its original grant without applying or renewing it. Another player's prior reservation wins; historical proximity cannot steal a consumed or replacement incarnation. A fresh accepted claim reserves the exact still-available item at the current authority decision frame, applies the existing native timed effect and consumes it once through `ApplyNativeTimedPickup`. It does not backdate activation or overwrite A1's immutable source observations. Native timers, journals, form changes, SFX and spawner callbacks share the same apply/consume path.

## Finite admission and delivery

The authority admits at most 256 distinct terminal claims per slot per room. It reserves terminal storage before any native callback and retains exact verdicts without eviction. Exhaustion permanently closes that slot's claim admission until the room changes; it never makes an old ID grantable again. A deterministic uncached Capacity response carries no invented decision/proof clock. Native authority contacts remain available independently of claim admission.

Discovery uses shared `PickupItemHistory` for live items and recent unavailable tombstones, with at most 2,048 retained entries. Unavailable facts older than 1,024 authority frames relative to the retained watermark are pruned; incoming facts older than that horizon are ignored, so expired tombstones cannot resurrect an incarnation. Active items are never pruned. Late-join baselines refresh current item authority frames. This is a retained-history bound, not a cumulative room birth quota: native spawners can continue producing new incarnations throughout long rooms without an A2 lifetime birth cap. A1's separate bound of 128 live registered timed items remains unchanged.

Incarnation facts and tombstones use bounded retained delivery with bootstrap/cursors. Ordinary hosted sessions wait for an authenticated post-load PickupReady message before creating a baseline; discovery cannot be lost by sending it before scene initialization clears client state. Duplicate readiness is idempotent; replacement admission and scene resets request a fresh baseline. Dedicated sessions retain their existing match/world admission gate. Readiness uses ordinary reliable delivery and a 15-second local queue-admission deadline. An incarnation never becomes available again; same-frame consumption wins over its available fact. New room scope clears prior discovery and owner requests. Slot-generation changes clear incoming/answer delivery state so replacements cannot receive another occupant's verdicts. Owner claims from a prior life retain delivery until an exact terminal or the finite deadline; death alone does not discard that obligation. A3 must separately fence any eventual presentation application by life. Owner claim sequences remain monotonic across reconnect/reset and refuse sequence exhaustion.

Delivery budgets are 16 incoming requests per slot, 32 queued answers per slot and 16 owner requests. Owner application retry admits at most six sends separated by at least 500 milliseconds. Last-send time and count advance only when the transport admits the send; refused queue admission does not spend the retry budget. Reliable UDP separately retransmits admitted bytes. Requests, answer queues, discovery bootstrap/backlog and owner terminal waits have finite 15-second deadlines; capacity, overrun or delivery failure is explicit rather than a silently successful settlement. Drain includes pending application/discovery work as well as transport state.

## Prediction and discovery limitations

Owner claim enqueue runs after native in-range detection and before predicted consumption. It requires local client identity, world readiness, matching authority discovery and an authored spawner identity. Unknown discovery cannot fabricate a claim. Dynamic drops currently have no shared owner entity mapping: owner claims for them are not implemented, while their native authority contacts still grant normally. Position-only matching is deliberately insufficient to invent their identities.

Offline and ordinary client native pickup prediction remain on their existing path. Receiving a verdict terminates the matching delivery record; A3 timer/item/presentation reconciliation is not implemented. A2 provides no grant delta/bootstrap reconciliation lane or owner correction UX. Existing ammo/weapon journal paths remain independent. No accepted-shot binding or damage enforcement is inferred from a pickup result; A4 must join accepted shots to grant-at-source proof.

Remote native contact still relies on client-reported movement. Captured authority pose and native proximity validate the engine's observed contact, not an independently authoritative trajectory, anti-teleport rule or anti-cheat guarantee. Native geometry matching also does not establish independent map collision validity.

## Replay and evidence

Item and admitted verdict facts are recorded with their source/proof/authority clocks separate from carrier arrival/application time. Each live verdict embeds its exact original claim. The raw claim codec/replay adapter remains supported, but unadmitted pending requests are not published as canonical live facts; A3 must separately restore bounded prediction state. Private replica state retains identity/terminal facts without invoking the foreground authority ledger or applying new visual/gameplay grants. Claim and verdict tables allow at most 2,048 retained entries per room; item history shares the live-item/recent-tombstone retention policy above. Conflicting immutable facts fail rather than overwrite. Checkpoint decoding validates retained active item facts independently of table iteration chronology, so an older active item remains loadable beside newer facts. Private checkpoint version 12 carries an explicit feature byte for native touch and pickup tables. The reader validates detached legacy v10/protocol-53 candidates for main's touch layout and staged A2's pickup layout, requires exactly one complete valid decode, and commits only after validation. Older checkpoints retain absent-field semantics; no historical recording acquires missing ownership proof.

Uncached permanent Capacity responses have zero authority clocks and cannot affect gameplay. They remain in campaign/transport diagnostics but are excluded from the admitted canonical replay ledger, avoiding unbounded replay growth through fresh refused IDs/reconnects. This exclusion does not hide their delivery/admission diagnostics.

Authority proof evidence independently captures the contact geometry, source/authority clocks and item facts used for each judgment. Native raw grant records are joined separately to admitted pickup results; packet self-consistency alone does not establish contact or reservation conservation. These evidence paths retain the residual reported-movement trust described above.

## Historical protocol 53 validation and bounded live results

Historical bounded A2 acceptance passed on immutable source `37c5f6bc2f246c5074c10c229907292e60607ae3`, engine SHA-256 `4abd570ff71c33cbe0f36c9bb6bfad35f5856d13840355eec45bba15b59f44ca`, protocol 53. The [committed acceptance manifest](../tools/nettest/baselines/net21-a2-20261012.json) and full raw ledger `artifacts/net21-a2/acceptance-37c5f6bc.json` retain that historical scope. These results do not accept the reconciled source above.

The accepted source passed all 37 focused contracts, 460 Python tests with one explicit skip, all 27 CI checks, and four independent recordings in both replay modes (eight successful checks). Focused results include 349 codec assertions, 3,087 pure pickup claim assertions, 6,219 network/history/fault assertions, 130 native adapter assertions, 19 hosted readiness checks and 24 replay/checkpoint fixture checks. Prior native apply/consume regression also passed 16,510 assertions in each of prime, balanced, enhanced and prime-hunter, covering all three timed types across shot-proof Off/Shadow/Enforce. Exact identities, configurations, raw hashes and results are retained in the acceptance manifest and raw ledger above.

Recorded seek checks used canonical baseline reconstruction when a durable capsule’s firing contract differed from the consumer. Those fallback counts and reasons are retained in the manifest; passing seek hashes do not establish direct restoration of those incompatible capsules.

Both live Fuel Sluice campaigns used ordinary native power-ups, AI clients and recorded gameplay. The LAN campaign had two clients and an authority bot. The eight-client campaign requested 180 seconds at 250 ms RTT, 40 ms added jitter, 5% loss, 5% reordering and 2% duplication. Both passed campaign parity/drain, AI activity and strict pickup proof/terminal/native-grant joins.

| Historical accepted campaign scope | Owner claims | Grant terminals | Matching owner receipts | Raw native Double Damage grants |
|---|---:|---:|---:|---:|
| Two-client LAN plus authority bot | 2 | 2 | 2 | 2 |
| Eight-client impaired, 180 seconds | 4 | 4 | 4 | 5 |

All six live claim proofs used the normal first-source F through F+2 path. The separate native-reservation acknowledgment branch is live NOT_RUN, with passing fixture coverage only. Both LAN native grants closed; the impaired native grants all closed with four Death and one Retired transitions. The extra impaired native grant is a separate authority contact, not an invented owner claim. Live Cloak and Death Alt claim coverage remain NOT_RUN; all three types have passing native fixtures.

Earlier coverage failures remain retained. The `4074acc7` campaign delivered two TooFar terminals but no claim grants and remained INCOMPLETE for accepted-contact coverage. The `11b61` LAN candidate passed with one Grant/receipt, but its impaired campaign delivered four TooFar terminals with zero claim grants despite five native Double Damage grants; that campaign remains INCOMPLETE for A2 coverage. Their fixture/replay successes do not rewrite those outcomes. The accepted `37c5f6bc` campaigns provide fresh positive claim denominators.

Acceptance is bounded to the measured A2 contract. It establishes neither A3 prediction reconciliation nor A4 source-shot damage authorization, dynamic owner claims, independently authoritative movement/anti-cheat, broad release readiness, sustained 20-minute stress or rendered projectile agreement. Explicit clip export was not exercised by these recordings; replay continuation/seek/checkpoint checks establish their own declared scope. Server timing, allocations, queue and traffic remain descriptive; no matched performance improvement is claimed. The native-reservation acknowledgment branch and the unobserved timed types remain explicitly separated from live exercised claim coverage.

## Rollback

Deploy the chosen rollback source to both clients and servers as a matched build. Reverting A2 means reverting claim/verdict/discovery producers and consumers, transport registrations, private pickup replay/checkpoint additions and contact/delivery hooks together, while preserving the reconciled A1 ledger, current-main native touch and retired Enhanced policy. The pre-A2 reconciled contract is protocol 53 with main's touch layout; the historical staged A2 protocol 53 is a different layout and cannot be substituted. Never make a protocol-55 binary advertise 53 or remove checkpoint flags to disguise a mixed deployment. Existing PPRC readers preserve their declared historical decoding semantics; a rollback binary must not claim it can read newer retained state without a validated reader. Preserve ordinary offline/native prediction. Reverting the independent hit-claim scheduling repair would restore its demonstrated lost-terminal gap and requires separate revalidation.
