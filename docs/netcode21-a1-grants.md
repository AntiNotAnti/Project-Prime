# A1 authority-owned timed power-up grants

**Status: bounded reconciled A1 correctness PASS.** Runtime evidence is bound to clean code source `a8f309e5761246410d378c88f5a2aa608a436238`, client SHA-256 `ea36e81251e992995c0eef39e8fa93b62b1d67469bed563b37543a2d0dc8ab24`, loaded client MVID `7c75a8cf-3af2-4517-865e-6154602cef44`, and protocol **53** inherited from current main. This document update changes no runtime code; new documentation-head CI must pass before merge. A1 establishes bounded native reservation/source-history evidence. It does not establish A2 claim acceptance, A3 reconciliation acceptance, A4 shot-time authorization or release readiness.

## Current reconciled acceptance

The final frozen runtime passes all **31 focused routes**, including all four asset-backed native ledger profiles (16,510 assertions each), the touch-capable canonical replay fixture and current-main compatibility. The `enhanced` route name is a retained legacy compatibility tag, not restoration of the retired hunter feature. The network Python suite passes **427 tests with one explicit skip**, repeated on clean `a8f309e5` for this review. Protocol 19 passes three fresh default-runtime repetitions with **zero bytes allocated** in the existing blocked-writer Emit window; the bounded fixture warmup exercises accepted and full/drop paths before measurement. Its zero-allocation requirement remains intact.

Hosted CI on `a8f309e5` reports **31 COMPLETED/SUCCESS entries, 30 distinct workflow/name checks** (one repeated semantic-event check). This includes Windows/Linux/macOS builds/tests, Android packaging and server checks. It is hosted build/contract evidence, not physical-device gameplay. The task evidence bundle `work/net21-reconcile/a1/` retains commands, UTC, actual loaded modules, source/build/log hashes, default shot-proof Shadow, physical candidate entry files and unchanged asset/binary identities. Current receipts are `31-final-a8f309e5/receipt.json`, `protocol19-stability-a8f309e5/receipt.json`, `replays-a8f309e5/receipt.json` and `a1-doc-review-hosted-ci.json`; the committed protocol 52 manifest below is historical, not a protocol 53 acceptance manifest.

Two fresh Fuel Sluice campaigns use ordinary native pickups, AI level 2 / seed 1729, server `--powerups`, fixed fault seed8128, recordings and 35-second bounded drain. LAN uses two clients plus one authority bot for 60 seconds; impaired uses eight clients for 180 seconds, 250 ms RTT, 40 ms added jitter, 5% loss/reordering and 2% duplication. Both campaign and AI audits PASS without failures/incomplete findings; native grant audits PASS with zero overflow/unknown source boundaries and no open grants. Pickup totals cover all native pickup types and are separate from timed-grant counts.

| Historical campaign | Timed Double grants | Closures | Native owner pickups | Declared / received / applied hit claims | Component payments |
| --- | ---: | --- | ---: | --- | ---: |
| `udp-2-fuel-a8f309e5` | 2 | 1 Expired, 1 Retired | 12 | 9 / 11 / 1 | 29 |
| `udp-8-fuel-a8f309e5` | 4 | 4 Death | 74 | 633 / 864 / 28 | 554 |

Receipts include retries; they are not unique claim counts. Four independent recordings (LAN peers 0/1, impaired peers 6/7) pass **12 commands**: four metadata inspections, four full determinism checks and four replica/checkpoint/seek/frozen-memory-clip checks. Metadata inspection is not a playback check. Durable checkpoint fallback counts/reasons stay in the raw logs; passing reconstruction does not certify direct restore of an incompatible capsule or exported `.ppclip` fidelity.

Available server-step telemetry is descriptive, with one observed segment per campaign:

| Campaign | Step p95 / p99 / p99.9 (ms) | Step failures / dropped steps / stalls |
| --- | --- | --- |
| LAN | 1.49 / 2.53 / 8.89 | 0 / 2 / 1 |
| Impaired | 1.36 / 2.63 / 14.23 | 0 / 22 / 1 |

These are server simulation-step histogram tails, not native frame/GPU time or matched performance improvement. Native frame p95/p99/p99.9 and GPU time are **UNVERIFIED**. The dropped steps/stalls preclude a performance-clean claim.

Current live coverage is Double Damage only: six genuine grants, one natural expiry, one retirement and four deaths. Cloak/Death Alt live grants, same-life renewal, room-reset transitions, spectator/map rotation, exported clips, sustained 20-minute runs and physical-device gameplay remain NOT_RUN; all three timed types retain focused native fixture coverage. Remote contact still relies on reported movement. No A3 acceptance or source-specific damage authorization is implied.

Negative controls remain retained: the main replay fixture formerly wrote `FullSize` without NativeTouch while expecting contact; the fix uses the touch-capable canonical carrier, without changing production packet bytes. Earlier allocation runs include a zero result followed by a 12,336-byte failure with startup instrumentation disabled as well as the instrumented failure; they were not waived or relabeled as passing. The final separate bounded warmup and three actual zero-allocation repetitions replace that failed gate on `a8f309e5`. An earlier interrupted lobby attempt has no completed route result and remains incomplete; the fresh final 31-route campaign supplies the positive receipt. Protocol52 zero-grant historical campaigns remain NOT_RUN for live grants.

## Ownership and native contact

`NetTimedGrants` holds the pure reservation/history model. `NetPowerUpLedger` adapts it to active multiplayer authority scenes, excluding replica scenes. Native item initialization registers the three timed types; destruction removes their live spawn entries. Registration covers authored items and dynamic drops. `PlayerProcess.PickUpItems` invokes the adapter inside each timed type's existing native pickup branch, before setting the timer or consuming the item. The existing native contact geometry and current item position select the contact; A1 adds no replacement radius or historical contact reconstruction.

The adapter requires an alive, in-play player, enabled power-ups, a live registered item with a nonzero despawn timer, and the same scene. The pure ledger reserves the incarnation before the caller can run native callbacks. A second contender or repeated callback receives Taken and cannot renew that same item. A fresh same-type item can renew the same life's grant; different timed types coexist. The adapter operates independently of N4 shot proof Off, Shadow or Enforce.

| Identity or record | Fields and purpose |
|---|---|
| Spawn key | Authority epoch, match, room sequence, authored spawn ID or dynamic fallback `-1`, and monotonically increasing incarnation generation |
| Player life | Existing ShotKey epoch, match, slot, slot generation and life ID; ShotId is zero for the life key |
| Grant record | Monotonic grant sequence, spawn key, player life, timed type, authority contact frame, optional input source contact frame, authority expiry and recorded end reason/frame |
| Input observation | Full player life plus source frame; its first authority observation records the three independent grant answers |

Native `_spawnCount` is a quota which can decrease on deactivation. It is not the incarnation identity. Every registered item incarnation receives a new monotonic generation, even if dynamic drops share fallback spawn ID or an authored spawner reuses its ID. Removing an item does not rewind the sequence. Room transitions create a fresh room scope; epoch/match changes create a fresh ledger. Session reset closes active grants and clears the adapter while preserving the process room counter. Empty-server suspension can resume the same epoch/match; the next room load advances that counter so new incarnations cannot reuse the suspended room identity. Spawn and grant sequence exhaustion refuse further allocation rather than wrap into reused identities.

## Native duration and two clocks

The existing three native callbacks retain their 1,800-tick timers. Input processing precedes contact, so contact at authority frame C cannot retroactively authorize input from C. A grant becomes effective at C+1 and expires exclusively at C+1800: subsequent observed input from C+1 through C+1799 can carry its ownership. Same-type renewal closes the previous grant at the new contact+1 boundary and records a new sequence. Death or lifecycle retirement closes active grants at the retirement frame. Development scene fixtures now check the actual native timer countdown and these phase boundaries with explicit fixture clock inputs; this does not establish live input timing under UDP.

Authority and source clocks remain independent. Remote source contact frames come only from a recent matching intent (age at most 30 frames, matching epoch/match/generation/life). Local authority players and native bots use the authority NetFrame as their source frame. Missing matching remote intent leaves the contact source boundary unknown; native authority contact can still reserve the item, but subsequent source queries cannot manufacture that missing boundary.

`ObserveInput` runs on authority input application and immediately before local input processing. The contact adapter also preserves the input observation when reached outside the usual loop. The first observation for a life/source-frame pair is immutable: replaying it after a pickup, renewal, expiry or death cannot rewrite the answer. A first per-life clock anchor bounds later source elapsed time to authority elapsed time plus 32 frames, with at most 1,024 frames of lag. Source acceleration or excessive stall records Unavailable. Expiry is determined by the independent authority clock; a slow reported source clock cannot extend it. Serial comparisons support unsigned frame rollover within the bounded comparison horizon.

## Bounded history and query meaning

The model stores at most 128 live spawn entries, 512 retained grant records and 16,384 input observations, across eight player slots and three timed types. Capacity refusals increment `CapacityRefused`; grant-capacity refusal leaves the item unreserved so it can be retried after space becomes available. Adapter spawn-capacity exhaustion returns false. Native `SpawnItem` destroys the unregistered candidate and returns null before consuming the spawner quota; a direct dynamic item reaching `Initialize` instead receives `DespawnTimer = 0` and cannot be picked up. Unrecorded observations cannot authorize a source query.

Authority advancement expires active grants at their exact expiry frames. Ended grant records survive through 1,024 frames after their recorded end, then retire. Input observations survive through 1,024 authority frames after their first observation. Death, disconnect, replacement and life changes retire current ownership through the existing lifecycle hooks while retaining bounded prior-life observations for delayed, independently admitted shots. A new life or slot generation does not inherit the old grant. Room/match/epoch reset discards the previous scope.

`GrantedAt` returns three distinct knowledge states. Granted identifies the grant sequence attached to an actually observed input frame. NotGranted means a retained, plausible observation established absence at that input, including contact-frame input or an input observed after authority expiry. Unavailable means the observation or required boundary is absent, evicted, refused by capacity, implausible, or outside the ledger scope. Unavailable must not be relabeled as known absence or authorization. A retained pre-expiry observation may still answer Granted after expiry or death for later independent adjudication; it does not revive the grant for newly observed input.

No grant is inferred from a client-reported Double bit or timer. The ledger records authority native contact and authority input observations. Existing owner-reported fields and native timers remain on their current gameplay paths; A1 does not yet replace them as downstream damage authorization. `GrantedAt` normalizes a supplied ShotKey to its life key and queries the source frame: that answer alone does not prove an arbitrary shot ID was actually authored. A4 must join the separately accepted shot and its exact source input before using ownership to authorize damage or other shot effects.

## Process evidence and strict transition audit

Opt-in `NetCampaignEvidence` appends process-lifetime `TimedGrants` creation and closure records. The callback copies each record at the transition, so later mutation of the retained grant cannot rewrite its earlier Active observation. `CloseAll` closes active grants with Room or Session reasons before room/session teardown; these copied evidence facts survive the ledger reset and empty-server suspension. The evidence shares the campaign's bounded storage and overflow signaling. Pure callback tests check the exact creation and closure transitions.

`tools/nettest/net_timed_grants.py` audits native authority reservation and transition conservation. It validates full spawn/life identities, timed type, contact+1 and 1,800-frame expiry, optional source boundaries, one creation per incarnation, unique matching closure, immutable identity/timing, legal end reason and bounded closure time, and reported evidence overflow. Missing instrumentation or measured zero grants is NOT_RUN; malformed or inconsistent evidence is FAIL; valid positive grant evidence is PASS for this audit's declared scope. It reports grant/type/closure/end-reason counts, open grants at capture and unknown source boundaries. This audit supplies no pickup-claim verification, client reconciliation, accepted-shot admission or damage proof.

## Preserved behavior and residual trust

Offline play and ordinary client prediction follow their existing pickup callbacks. A1 adds no pickup claim/verdict wire message, protocol bump, client reconciliation, replica checkpoint state, claim payout change or new damage multiplier enforcement. Existing native Double journal calls remain, but they are not the authority pickup admission. Ammo expenditure and weapon/ammo grant journal paths remain independent and unchanged.

Remote native contact still uses movement positions supplied through existing client reports. Authority reservation prevents duplicate consumption of a known incarnation; it does not establish independently authoritative movement or provide anti-teleport/anti-cheat guarantees. A2 must define historical item availability and pickup claim verification, A3 must define reconciliation, and A4 must connect accepted shots to grant evidence. These remain pending.

## Dedicated server opt-in

Dedicated rotation matches previously hardcoded `DisablePowerups = true`. The explicit server flag `-powerups` now enables native timed pickups in the initial match definition; omission preserves the disabled default. The campaign runner forwards `--powerups` only to the server and records the option in its manifest. This uses ordinary match configuration and native spawned items, with no injected pickup, forced position or fabricated match-state packet. Existing lobby rule changes remain available.

## Historical protocol 52 validation and acceptance

Historical bounded A1 native grant acceptance passed. Immutable source `3ba799abfcac35c40a009f6c5a923e800100269a`, engine SHA-256 `1b716abcc450d6f2396e5acfa703dce559ab497493d6e536c378faeee9ce4de9`, protocol 52, has passing live Double Damage reservation evidence, both required campaign/AI audits, 31 focused contracts and the Python suite. All four historical recordings passed both replay modes, all 27 CI checks completed successfully on that exact source, and the strict evidence ledger is PASS with no failures or missing inputs. The committed [A1 acceptance manifest](../tools/nettest/baselines/net21-a1-20261012.json) references the full raw ledger and source/build/artifact hashes. This slice establishes bounded authority ownership evidence for Double Damage, Cloak and Death Alt; A2 verified pickup claims, A3 client reconciliation, A4 shot authorization, later gameplay slices and R0 remain pending. G1's bounded harness acceptance is separate.

`TimedGrantTests` (`--timed-grants`) passed 1,422 pure assertions in `artifacts/net21-a1/pure-dev-3.log`. They cover distinct clocks and phase boundaries, renewal, contention, all three types, lifecycle retirement, slot reuse, stream/room fences, rollover, retention, eight-slot isolation, capacity recovery and exact evidence callbacks.

`PowerUpLedgerSceneTests` (`--powerup-ledger-scene <paths-directory> [profile] [room]`) passed 16,510 asset-backed native contact/ledger assertions in each of four profiles: prime (stock), balanced, enhanced and prime-hunter. Results are retained in `artifacts/net21-a1/scene-prime-dev-4.log`, `scene-balanced-dev-4.log`, `scene-enhanced-dev-4.log` and `scene-prime-hunter-dev-4.log`. The checks use real item entities and pickup callbacks for all three timed types across shot proof Off/Shadow/Enforce. They exercise actual timer countdown, incarnation renewal, native spawner delay/quota and capacity refusal, disabled-power-up flag behavior, reported Double and inherited Cloak failing to authorize a grant, offline/client prediction, and a real private replica scene whose item initialization/destruction does not modify foreground authority ownership.

The historical protocol 52 immutable source passed all 31 focused contract routes in `artifacts/net21-a1/contracts-3ba799ab/results.json`, including pure timed grants, all four native profiles and the server power-up opt-in/default checks. The Python suite ran 427 tests with one explicit skip and reported OK in `artifacts/net21-a1/python-3ba799ab.log`. These immutable checks supplement the development fixtures above.

Both fresh recorded Fuel Sluice campaigns enabled ordinary native power-ups with `--powerups`, AI level 2, AI seed base 1729 and a 35-second drain. The two-client LAN run added a native authority bot and requested 60 seconds. The eight-client run requested 180 seconds at 250 ms RTT, 40 ms added jitter, 5% loss, 2% duplication and 5% reordering. Native owner pickup consumptions are a separate denominator from authority timed grants.

| Historical campaign | Campaign / AI | Double Damage grants / Death closures | Declared claims | Receipts including retries | Applied claims | Component payments | Paid AI source keys | Owner pickups | Canonical deaths / spawns / respawns |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| `udp-2-bot-powerups-3ba799ab` | PASS / PASS | 2 / 2 | 14 | 14 | 0 | 52 | 23 | 11 | 10 / 14 / 10 |
| `udp-8-impaired-powerups-3ba799ab` | PASS / PASS | 4 / 4 | 593 | 799 | 26 | 594 | 563 | 128 | 111 / 126 / 111 |

All six live grants have known source contact boundaries, and every recorded grant closes on Death. The LAN authority bot observed 306 shot keys. The native grant transition audit passes for both reports; the strict artifact assembler `artifacts/net21-a1/build-ledger-powerups.py` also joins authority native consumption evidence to grant creations as an exact multiset, so native consumptions and ledger grant counts cannot diverge silently. This proves the declared reservation/transition/consumption conservation scope, not reconciliation, independently authoritative movement or source-shot damage authorization.

The two LAN recordings and two eight-client recordings each passed determinism and replica/seek/clip checks: eight passing checks across four independent demos. All 27 historical-source CI checks finished COMPLETED/SUCCESS. The final raw ledger, `artifacts/net21-a1/evidence-3ba799ab-final.json`, is PASS with no failures or missing inputs. Recorded checks establish their declared replay scope and do not add a grant bootstrap or client reconciliation lane.

In those protocol 52 campaigns, live Cloak, Death Alt, timed expiry, same-life renewal and room-reset grant transitions remained NOT_RUN; they have fixture coverage only. The live Double Damage deaths do not establish every lifecycle edge or accepted-shot proof. The four native profiles each cover all three types and actual native timer countdown, but fixture timer poses and spawner delay/quota are explicit inputs. The fixtures do not exhaust all no-power-up modes or establish actual native input/UDP ownership reconciliation. Remote contact still depends on client-reported movement. Server timing, allocation, queue and traffic measurements remain descriptive; no matched performance improvement is claimed.

The first immutable candidate, `d788bbcbc9131c0148f39fe499d61f3d7b66161f`, retains its passing 30 focused contracts, 426 Python tests with one explicit skip, all 27 CI checks and four campaign/AI results: two-client/server-bot LAN, eight-client impaired Sanctorus, eight-client Fuel Sluice and eight-client Sic Transit. Six independent recordings from LAN, Sanctorus and Fuel each passed both replay modes. These historical campaigns observed zero timed grants because dedicated rotation defaults disabled power-ups; Sanctorus also has no authored timed power-ups. They remain regression evidence with live grants explicitly NOT_RUN and cannot satisfy the new grant acceptance gates.

## Rollback

Revert the A1 ledger, adapter, native contact/input/item/lifecycle/session hooks and associated tests together. A1 introduces no protocol change or new wire producer. Reverting this reconciled slice retains current main protocol 53; do not downgrade it to the historical protocol 52 deployment. Rollback requires no A1-specific wire/save/checkpoint migration. Existing native offline/client prediction and the independent ammo/weapon journal remain the baseline behavior.
