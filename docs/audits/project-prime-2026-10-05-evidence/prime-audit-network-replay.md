# Network, authority, hosted-server and replay audit notes

Audited clean main `a68627d0` at `/Users/jarrett/.codex/worktrees/engineering-audit/Prime Hunters Online`. Current implementation and `ARCHITECTURE-INVARIANTS.md` were treated as authoritative. Product files were not modified. This is a source-level defensive engineering review: no attack client, public-server traffic, or abuse demonstration was created or run. Runtime evidence below is explicitly attributed to the root audit's builds/checks.

Scores are 1–10: **Impact** = user/production benefit of correcting the issue; **Risk** = regression risk of the proposed change; **Effort** = implementation/verification cost; **Confidence** = strength of current evidence. “Confirmed” means the source-level behavior is established, not that an exploit or production performance impact was demonstrated. “Highly likely” means the end-to-end consequence needs the indicated focused regression. “Suspicious/measurable” is a profiling hypothesis, not a measured bottleneck.

## Essential conclusion

The process-isolated authority, lifecycle fences, transport bounds, pooled replay ownership and canonical passive player are substantial and mostly coherent. The largest remaining trust-boundary gap is combat: a claim can become authoritative damage without proving the claimed attack launched, and ordinary inventory/powerup state remains client-authored. Remote hosting also has a concrete idempotency gap outside custom-map preparation. Replay work has useful byte/count limits, but the limits do not cover every retained structure or all synchronous owner-thread work. Exact custom-package discovery and failed initial-world restoration need correctness fixes before broad performance refactors.

## Findings

### N1 — Claimed combat outcomes become authoritative without evidence that the attack launched

**Critical; confirmed source-level defect. Impact 10 / Risk 7 / Effort 7 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/NetHitClaims.cs:1221` (wire validation), `:1233` (receive), `:1282` (judge/park), `:1332` (Judge), `:1403` (sole launch-history check), `:1705` (damage ceiling), `:1815` (ledger pairing), `:1855` (rescue application), `:1941` (ApplyOne), `:2030` (damage), `:2053` (afflictions). Ingress is `src/MphRead/Mods/Network/DedicatedServer.cs:1261–1271`; it validates framing and binds the sender to its admitted slot, then calls this claim path. Claims are enabled by default at `NetHitClaims.cs:76`.

The path is Receive → lifecycle/time/radius/damage/impulse checks → Park → grace-window authority-hit pairing → ApplyOne when no independently resolved hit exists. `ValidateWireClaims` requires a nonzero ShotId for a beam, but does not prove that ID belongs to a successful launch. `Judge` deliberately treats the historical victim position as the only geometry evidence; it does not trace from a validated historical muzzle/direction, check obstruction/trajectory/head band, or require an accepted fire event. Its only successful-launch lookup is the OneInTheChamber Imperialist special case. `TakeLedger` suppresses a duplicate independently resolved hit; failure to find one is the reason the claim is applied, not a rejection. `ApplyOne` rechecks lifecycle, victim liveness, earlier shooter death and bounded ledger capacity, but adds no launch proof.

Damage is the client's already-multiplied amount. The bound selects the maximum of all charge/head/splash values then allows the largest possible combined multipliers (`raw * 5 + 1`), rather than validating the state of this shot. Client headshot/direct and freeze/burn/disruption flags reach the durable damage/semantic pipeline and affliction setters. Thus the authoritative scoreboard can be written through accepted claim evidence even if ordinary authoritative simulation never produced that attack. This is a trust-boundary defect independent of owner-reported movement; the fix does not require input replay or movement rollback.

**Fix:** maintain a bounded authorized-attack ledger keyed by the existing match/epoch/shooter generation/life/ShotId. A claimed rescue must reference an accepted fire/alt-contact event, legal cadence, weapon ownership, charge, powerup and permitted outcome class. Authorization must preserve legitimate accepted locally fired events when physical authority collision misses; requiring an independently resolved authority hit would defeat the documented rescue feature. Check plausible trajectory/LOS against the same acknowledged world (including dynamic geometry), and derive the allowed damage/headshot/afflictions from that attack. Retain existing delayed-projectile, mutual-kill, direct-plus-splash, ricochet and bounded-grace behavior. Temporarily disabling unverified rescues is an explicit optional operator hardening mitigation, with clear gameplay/latency tradeoffs; do not silently change the production default before the latency/loss/jitter matrix measures feature loss.

**Benign regression expectations:** an absent launch produces an explicit InvalidLaunch result without health/score/affliction changes; a genuine missed launch can still rescue a bounded hit; incorrect affliction flags and excessive charge/headshot damage are refused; valid chamber, headshot-difference, direct+splash, old-life traveling projectile and mutual-kill cases remain stable. Existing `NetCombatCheck.cs:167–195` checks radius and impulse boundaries but does not establish launch evidence; the radius fixture itself expects a claim close to the victim to pass without setting up a corresponding shot.

### N2 — Ordinary weapon acquisition, ammo and Double Damage are client-authored combat state

**High; confirmed. Impact 9 / Risk 7 / Effort 7 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/PlayerReplicationBridge.cs:513–517`, `:596–600`; `src/MphRead/Mods/Network/PlayerEntityNetAim.cs:1642–1657`, `:1788–1828`, `:1877–1890`.

The server bridge applies the owner's selected weapon, ammo and shot-state bits. `ModSetWeapon` grants `_availableWeapons` and `_availableCharges` when a selected enabled weapon was previously unavailable. `ModSetAmmo` overwrites normal authority ammo with a client value clamped only to the pool cap; a cap does not prove the supply was earned. `ModSetShotState` holds `_doubleDmgTimer` above zero as long as the owner repeats the bit and accepts charge as a reported value. Therefore authority damage/score alone does not establish authority over the resources governing that combat. This is intentional trust in local pickup simulation in comments, but it is a material public multiplayer integrity limit that should be documented accurately.

**Fix:** authority own pickup grants, inventory, consumption and powerup lifetime; let owner reports be low-latency presentation/state evidence reconciled against those grants, not new grants. Preserve owner-reported position and same-life no-reconciliation constraints. Record the authority resource state for N1's attack checks.

**Regression expectations:** normal pickups give identical legal weapons/ammo/powerup duration under loss/jitter; a report cannot invent ownership, refill a consumed pool or prolong a powerup. Preserve the already sound OneInTheChamber early return, BalancedImperialist ammo cap and Samus legal boost maximum/consume-on-hit rules.

### N3 — Rejected position values remain accepted raw intents and are consumed as shot origins

**High; highly likely harmful consequence, confirmed inconsistent validation. Impact 8 / Risk 3 / Effort 3 / Confidence 9.**

Anchors: `src/MphRead/Mods/Network/DedicatedServer.cs:2425–2452`; `src/MphRead/Mods/Network/NetFireEvents.cs:136–155`; `src/MphRead/Mods/Network/NetPlayerLifecycle.cs:149–154`; `src/MphRead/Mods/Network/NetSession.cs:1307–1334`; `src/MphRead/Mods/Network/PlayerReplicationBridge.cs:421–428`, `:1012–1018`; `src/MphRead/Mods/Network/NetHooks.cs:257–270`; `src/MphRead/Entities/Players/PlayerInput.cs:1307`.

Dedicated ingress validates lifecycle/framing and the optional fire-event pose, but not the enclosing intent's Position. `AcceptSlotIntent` stores it and records it as an accepted replay fact. Later `ApplyReportedPosition` rejects nonfinite/out-of-range positions and leaves the collision body intact. `RemoteShotOrigin` nevertheless adds the raw stored intent Position to the current muzzle/body offset without the same sanity check. Aim rejection is a separate later bridge check. Fire-event pose validation does not repair the enclosing raw position; the authority origin branch is distinct from the replay-authored pose branch. A malformed enclosing position therefore can reach a projectile origin even though placement rejected it. No process crash or world contamination was reproduced, so that consequence remains highly likely rather than asserted as measured.

**Fix:** share one finite/bounds predicate and validate the whole intent before ordering, assignment, relay and accepted-fact recording. If partial field acceptance is intentionally retained, store a sanitized accepted position separately and use it everywhere including `RemoteShotOrigin`; do not leave raw rejected values in the accepted-state slot.

**Regression expectations:** ordinary stale/life/position cases preserve owner-position semantics; invalid vectors do not advance the accepted frame baseline or enter projectile/replay state; no beam origin or direction becomes nonfinite. A valid later intent is still accepted.

### N4 — Duplicate builtin hosting requests allocate multiple children outside spawn budgets

**High; confirmed. Impact 9 / Risk 3 / Effort 3 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/HostRequestGuard.cs:89–101`, `:115–118`, `:161`; `src/MphRead/Mods/Network/NetMaster.cs:526–540`; `src/MphRead/Mods/Network/DedicatedServer.cs:2094–2106`; `src/MphRead/Mods/Network/HostPool.cs:89–138`.

Once an endpoint+nonce+request fingerprint has passed a challenge and a token debit, HostRequestGuard accepts exact retransmissions for 180 seconds without taking another token. Custom-map requests are deduplicated by HostedMapRequests. Builtin requests instead call HostPool.Start on every accepted duplicate, which picks a new free port and creates another process; the pool has no request key or cached reply. One admitted request can consequently consume multiple port/process reservations, defeating the intended spawn admission bound. This applies to both a master allocator and a dedicated server configured as allocator. The finite port range and abandonment reaper bound the ultimate count/lifetime but do not make allocation idempotent.

The normal builtin launcher sends once (`NetMaster.cs:1196`); its current automatic timeout retransmission at `:1205–1206` is custom-only. Do not claim normal builtin timeout retries occur today. Duplicate delivery or repeated use of an already authorized request still demonstrates the source-level defect.

**Fix:** cache the pending/result allocation by endpoint+nonce+full request identity for the same acceptance window, including refusal/owner token as appropriate. Return the same existing allocation for duplicates; a fresh nonce legitimately starts another lobby. Preserve distinct people behind one NAT and do not restore the old replace-by-public-IP behavior.

**Regression expectations:** repeated identical accepted requests invoke a fake allocator exactly once and return one port/owner token; separate nonces and separate endpoints behind one public address remain independent; custom-map preparation and builtin allocation share the same idempotency contract; expired or altered request proofs remain refused. No live child process or external traffic is needed for this test.

### N5 — Directory admission is unverified and unbounded; insertion order can hide legitimate listings

**High; confirmed. Impact 8 / Risk 5 / Effort 5 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/NetMaster.cs:254`, `:572–611`, `:614–622`, `:634–673`, `:475–488`; `src/MphRead/Mods/Network/NetTransport.cs:743–753`.

Any accepted unsequenced heartbeat creates or updates `(source IP, advertised port)` without proving that game port is reachable, authenticated or controlled by the reporter. `_entries` is an uncapped List; each insertion searches linearly and Expire scans it every allocator pass. TTL bounds age but not meaningful population. The discovery global token bucket (300/s, burst128) is helpful but not a per-address identity/capacity bound; it still admits far more entries per TTL than the published list's 255-entry limit. `SendList` returns the first255 insertion-order entries, so later genuine servers can disappear behind unverifiable entries. Query replies can be many datagrams without return-path proof, creating avoidable UDP amplification. Farewell similarly removes by source IP and advertised port without endpoint ownership proof.

**Fix:** bounded dictionary with per-address/per-host quota and admission budget, a return-path/reachability challenge for the advertised game endpoint, bounded query replies/pagination and explicit overflow behavior. Authenticate operator-hosted registrations when available. Keep multiple legitimate servers behind one NAT supported.

**Regression/benchmark expectations:** synthetic normal heartbeat churn preserves established reachable servers, never exceeds configured count/bytes, and has stable lookup/expiry cost; valid multiport NAT hosting remains visible; challenge failures cannot install/remove a listing; query response bytes are bounded. Do not describe the entire transport inbox as unbounded—it is bounded and pooled.

### N6 — Dedicated startup exceptions bypass the shutdown/finally region

**Medium; confirmed. Impact 6 / Risk 2 / Effort 2 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/DedicatedServer.cs:419–448`, `:479`, `:688–690`.

Run binds transport, enters headless mode, may prewarm, configures recording and starts the outbox before map staging/rule validation. The finally calling Shutdown begins only around the later loop. Missing game files, staging failures or invalid definitions can throw before that finally, leaving in-process callers with a bound socket/receive worker and initialization resources. A separate process exiting allows OS socket cleanup; the concrete leak is important for tests, embedded callers, retries and failed setup paths rather than a claim that a dead process retains a socket. The listening log also precedes prerequisites, while the later ready log correctly follows them.

**Fix:** validate prerequisites before binding when possible and put all acquired initialization resources inside one encompassing try/finally with idempotent partial-initialization shutdown. Avoid a second parallel cleanup lifecycle.

**Regression expectations:** inject benign game-file/staging/definition failures; the port can be rebound, background jobs are stopped, staging leases are released, and a repeated Run attempt does not inherit partial state. Normal idle-lobby deferred simulation remains cheap.

### N7 — The master allocator has no finally for child/worker/socket shutdown

**Medium; confirmed. Impact 6 / Risk 2 / Effort 2 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/NetMaster.cs:354–356`, `:385–429`, `:431–435`.

Unlike the dedicated server's established loop cleanup, MasterServer.Run places HostedMapRequests.Dispose, HostPool.StopAll and transport.Dispose after the loop without a finally. An exception in packet dispatch, request pumping, reaping or maintenance skips all cleanup, potentially stranding allocated child processes and staging/transport resources. Some handlers catch expected errors; that does not provide an encompassing lifecycle guarantee.

**Fix:** one encompassing try/finally plus best-effort independent cleanup so a failure stopping one child does not prevent socket and staging disposal.

**Regression expectations:** fake a packet/maintenance exception after registering one fake child/job; all cleanup hooks execute and no port/staging owner survives. Cancellation and safe-update exits must produce the same outcome.

### N8 — Directory DNS resolution runs synchronously on the authority loop

**Medium; confirmed blocking call, performance impact requires measurement. Impact 6 / Risk 3 / Effort 3 / Confidence 9.**

Anchors: `src/MphRead/Mods/Network/DedicatedServer.cs:589`; `src/MphRead/Mods/Network/NetMaster.cs:111–137`, `:189–205`.

MasterReporter.Beat executes inside the game loop and calls `Dns.GetHostAddresses` initially and hourly. A slow/failing configured hostname resolver can block all authority packet processing and fixed stepping. Failures do not refresh `_lastResolve`, so it retries on heartbeat cadence. The default is presently a literal IP, reducing the common path's DNS exposure; this is specifically a configurable-hostname failure/stall issue. Initial launcher-only DNS is a different lifecycle and is not claimed to stall a running match.

**Fix:** background bounded resolution/heartbeat work with an immutable cached endpoint and retry backoff. Preserve the Scene/NetSession owner boundary; the worker receives detached listing values only.

**Benchmark expectations:** inject resolver delays/failures while a local authority timing fixture runs; compare step deadline misses, packet age and P95/P99 frame times. DNS changes should update the listing without blocking simulation. No networking protocol or movement model change is required.

### N9 — Windows authority pacing has a large repeated-yield region worth profiling

**Medium; suspicious/measurable bottleneck. Impact 5 / Risk 5 / Effort 4 / Confidence 7.**

Anchor: `src/MphRead/Mods/Network/DedicatedServer.cs:700–742`.

On Windows, once the next-step gap falls below12ms but remains above0.25ms, PaceLoop only yields and returns to the full server loop. Thus most of a nominal16.7ms interval can repeatedly perform packet/maintenance passes and scheduler yields, depending on contention and Sleep granularity. Unix uses a2ms threshold. This is a plausible single-core CPU/time-granularity cost and a scaling limit across isolated children, not an established latency regression. Idle non-simulating lobbies correctly wait on transport activity and should not be included in that complaint.

**Fix only after measurement:** platform-appropriate deadline wait (including a Windows high-resolution waitable timer) or a measured adaptive wait/activity strategy; preserve absolute60Hz deadlines and responsive bounded packet pumping.

**Benchmark:** active2/8-player authority across Windows/Linux, CPU/core utilization, loop passes per step, allocation rate, deadline misses/P99 step jitter and burst packet age. Include 1/8/32 isolated children and idle lobbies. Report frame-time distribution, not just aggregate throughput.

### R1 — New v4 metadata loses the exact custom-map identity used during replay preflight

**High; confirmed. Impact 8 / Risk 3 / Effort 3 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/ReplayTimelineArchive.cs:16–29`, `:139–154`; `src/MphRead/Mods/Replay/ReplayWorldCheckpoint.cs:54–69`; `src/MphRead/Mods/Network/ReplayMetadata.cs:58`, `:64`, `:80–105`, `:134–149`; `src/MphRead/Mods/Network/ReplayPlaybackSession.cs:185`, `:200–224`.

Both new full-recording metadata and frozen-clip metadata contain the world capsule, room key and map hash, but leave Bootstrap empty. `CustomMapIdentity` and `PrepareExactPackage` read only SessionState packets from Bootstrap, not the world capsule's construction decoder. Preflight invokes PrepareExactPackage and validates the local room before decoding that capsule. Therefore the authoritative custom identity/historical download source embedded in construction state is unavailable to the exact-package retrieval path. A missing historical map fails MapMissing; an installed newer same-name room fails MapHashMismatch instead of retrieving/installing the exact archive. With an older protocol it can also be mistaken for the builtin best-effort case because CustomMapIdentity is null.

**Fix:** persist the bounded custom SessionState identity/download source in header bootstrap for both metadata writers, or derive only that detached construction configuration from a supported capsule during preflight. Do not build a Scene merely to discover the package or relax exact map hash checking. Confirm nested/materialized/frozen clips retain original identity/source.

**Regression expectations:** full recording, instant clip and nested clip retrieve an absent historical exact package and handle a newer same-name installation; mismatched/tampered identity stays refused; builtin and v2/v3 conversion behavior stays explicit. Preserve the replay-local Community-address guard.

### R2 — Initial world restore failure continues the same partially mutated replica

**High; confirmed exceptional-path correctness defect. Impact 8 / Risk 3 / Effort 3 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/PassiveReplayScene.cs:24–43`; `src/MphRead/Mods/Replay/ReplayWorldCheckpoint.cs:293–320`, `:333–341`, `:353–410`; correct optional-checkpoint comparison: `src/MphRead/Mods/Network/PassiveReplayPlayer.cs:145–155`.

Restore is intentionally a transaction only through ownership of a new disposable replica; its comment says the caller disposes that replica on validation failure. The initial file constructor catches InvalidDataException and instead keeps it, adds ReplayPoses and continues. Restore can change hunter resources before all nodes validate, and later sets anchor fields/collections incrementally before a bad link, trailing field, asset appendix or cosmetics appendix throws. A compatible outer header/CRC is insufficient to make these later failures harmless. The file constructor can consequently publish a mixture of defaults and partially restored state. Optional durable/memory checkpoint rebuild correctly disposes the failed replacement and opens a fresh one, demonstrating the intended boundary.

**Fix:** dispose every failed candidate; rebuild a fresh candidate only when complete validated fallback facts exist. Never continue a mutation-bearing restore failure on the same replica. Keep foreground scene and live static facades isolated.

**Regression expectations:** benign corrupt/incompatible late-node/link/appendix fixtures fail without replacing the presented world, leak no leases/GL resources, and either reconstruct from a fresh valid baseline or report StateMismatch. Repeat open/seek failure cycles. Root's separate collision-pool leak should also be fixed before these repeated construction tests are interpreted as stable memory.

### R3 — An initial v4 world is necessary state, but compatibility fallback treats it as an optional accelerator

**High; highly likely replay loss/corruption, confirmed unsupported fallback design. Impact 8 / Risk 5 / Effort 6 / Confidence 9.**

Anchors: `src/MphRead/Mods/Network/ReplayPlaybackSession.cs:215–240`; `src/MphRead/Mods/Network/ReplayTimelineArchive.cs:16–36`; `src/MphRead/Mods/Replay/ReplayWorldCheckpoint.cs:86–108`; `src/MphRead/Mods/Replay/ReplayWorldSchemas.cs:6`, `:110`, `:247`, `:423`; `src/MphRead/Mods/Replay/ReplayReviewCheck.cs:14–22`.

Initial checkpoint rejection resets decoder/transport to a bootstrap/linear fallback under the comment “checkpoints are accelerators.” New v4 initial worlds capture the actual origin including preexisting projectiles/effects/random/timers; the archive writes only subsequent records and provides no independent complete bootstrap. No linear replay of facts after origin can recreate those pre-origin objects. Some files will simply lack a usable match/configuration and refuse to open; a file with a later configuration can open a different incomplete world. Optional durable checkpoints really are accelerators, but the sole initial world of a mid-match/frozen v4 clip is a different dependency.

Unknown schema/anchor rejection itself is sound and must not be weakened. SupportsContract reconstructs the current field schema with optional assembly-version adjustment; it is not an old-layout migration. History confirms real changes since v0.1.34: chamber/token/hardpoint/objective fields, then Battlehammer cluster child (`20307805`), Balanced Imperialist ammo (`132b7673`) and immunity/life-drain state (`baebac7d`). The root's supplemental `-replaycontrolcheck` fails the hardcoded “unchanged v0.1.34 layout” assertion. That premise is stale; this is not proof that the current assembly-version normalization alone is broken.

**Fix:** distinguish required initial world from optional seek worlds. If its layout cannot be migrated with an explicit archived schema, fail with StateMismatch and clear producer-version guidance; only fall back when the file actually carries complete independent reconstruction history. If historical v4 clips are a supported product requirement, implement versioned field adapters/defaults and genuine archived fixtures rather than accepting a hash for the wrong layout. Correct the stale test message/premise without suppressing the failed compatibility requirement.

**Regression expectations:** an old supported initial capsule with projectiles at visible frame0 restores exactly, or explicitly refuses with the correct reason; no best-effort half-world is advertised as exact. Optional corrupt durable entries still fall back to a valid initial world. Include real archived producer builds and compare gameplay plus effect/animation hashes.

### R4 — Presentation deduplication grows for the whole replay despite bounded lookahead

**Medium; confirmed retained-state growth. Impact 6 / Risk 3 / Effort 3 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/ReplayPoseStream.cs:43–64`, `:81–89`, `:1101–1105`, `:1171–1176`, `:1207–1213`, `:1247–1264`.

Pose/intent/clock samples and resolved impact schedules have explicit bounds/pruning, but `_seenShotFacts` keeps one identity for every distinct shot fact encountered across continuous playback. It is only cleared on presentation reset/rewind. `_fireCapable` likewise retains every observed slot generation/life. The960-frame fact lookahead and140-frame history therefore do not bound all retained presentation state. A multi-hour high-hit-rate replay grows memory continuously. The fact identity also excludes ResolveTick and ShotId; eventual ushort DamageEventId wrap for the same shooter/victim lives may suppress later distinct facts (rare; needs a boundary fixture).

**Fix:** bounded time/sequence deduplication tied to the reliable lookahead+presentation history; expire keys once their duplicates cannot affect the current cursor. Fence current/relevant fire-capable lives, and include sufficient tick/ShotId identity for wrap safety. Do not advance simulation/RNG to prune presentation state.

**Benchmark/regression:** ordinary repeated reliable facts still produce one effect; multi-hour synthetic normal shot facts and many respawns reach a memory plateau; rollover facts are distinct; backward seek resets and deterministically reproduces effects. Track retained key counts separately from frame-cache bytes.

### R5 — Passive checkpoint cache accounts logical length instead of pooled retained capacity

**Medium; confirmed memory-budget defect. Impact 7 / Risk 2 / Effort 2 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/PassiveReplayPlayer.cs:21–22`, `:154`, `:167–176`; `src/MphRead/Mods/Replay/ReplayCheckpointWriter.cs:14–19`, `:29–32`; `src/MphRead/Mods/Replay/ReplayWorldCheckpoint.cs:26–28`, `:185–212`; `src/MphRead/Mods/Network/ReplayTimeline/ReplayPayload.cs:16–24`; correctly accounted timeline: `src/MphRead/Mods/Network/ReplayTimeline/ReplayTimeline.cs:45`.

The64MiB/128-entry cache counts Bytes.Length+128. Capture starts from a pooled array of at least1MiB and Detach transfers the whole array to the checkpoint; it does not compact to logical length. Thus128 capsules smaller than roughly512KiB can remain below the reported64MiB while retaining at least128MiB of buffers. Larger capsules similarly undercount pool bucket slack. The count cap prevents truly unbounded cache growth, but the promised byte budget and telemetry do not describe actual retained payload memory. This is distinct from R4's unbounded key sets.

**Fix:** use checkpoint.Payload.Capacity+overhead consistently for insertion, rejection, eviction and diagnostic bytes, as the shared timeline already does. Consider lowering initial capacity only after profiling capture copy/allocation tradeoffs; capacity accounting alone is a small safe fix.

**Regression/benchmark:** fixtures just below/above pool boundaries plus small capsules cannot retain more than64MiB accounted capacity; all add/remove/reject paths return the same sum; repeated seeks/eviction return leases. Report both logical serializer bytes and retained capacity so compression/capture telemetry remains meaningful.

### R6 — File playback opens three independent readers and re-decompresses the same chunks

**Medium; confirmed redundant work, performance impact requires profiling. Impact 6 / Risk 6 / Effort 6 / Confidence 9.**

Anchors: `src/MphRead/Mods/Network/ReplayPlaybackSession.cs:167`; `src/MphRead/Mods/Network/ReplayPoseStream.cs:942–967`, `:1142–1179`; `src/MphRead/Mods/Network/ReplayFormatV3.cs:622`, `:676–698`.

One reader feeds the canonical decoder, a second reader scans fire/pose lookahead, and a third reader scans960 frames for resolved-shot facts. Each reader separately opens/validates the archive and inflates/validates chunks; the shot-only reader parses every record in that horizon to select a rare packet kind. This is bounded, purposeful presentation evidence—not a correctness duplicate recorder—but costs repeated I/O, decompression, record allocations and file/index parsing during cold start/seek and continuous playback. No speedup percentage was measured.

**Fix after measurement:** share bounded immutable decoded chunks or persist a compatible shot-fact/pose index, keeping independent cursors and never feeding lookahead into simulation. Detached file read/decompression may use workers; Scene/resource construction remains on its owner. Preserve v2/v3 adapters and exact seek semantics.

**Benchmark:** cold/warm opens and random seeks for8-player30/120-minute archives; decoded chunk count, decompressed bytes, read system calls, allocated bytes and P95/P99 update/seek latency. Compare server recordings/client delivery delays and frozen clips. Any cache needs a capacity+lease budget, not another unbounded whole-file memory copy.

### R7 — The seek/preparation time budget does not cover synchronous reconstruction or lookahead

**Medium; confirmed budget boundary, user-visible stall severity requires profiling. Impact 6 / Risk 6 / Effort 6 / Confidence 9.**

Anchors: `src/MphRead/Mods/Network/PassiveReplayPlayer.cs:71–114`, `:130–165`; `src/MphRead/Mods/Network/PassiveReplayScene.cs:90–108`; `src/MphRead/Mods/Network/ReplayPoseStream.cs:942–980`, `:1142–1179`; `src/MphRead/Mods/Network/ReplayMetadata.cs:89–105`.

The24-step/about1ms optional preparation policy bounds stepping, not the work in Rebuild before the budget loop. Rebuild synchronously loads/decompresses a world, opens the file/preflights the exact package, constructs a room/scene and restores the graph. A single Step can also initialize/scans lookahead. Its at-least-one-step policy permits that whole synchronous step even after the wall-time budget has elapsed. Initial construction/restore must remain on the owner per invariants; the issue is no stage/instrumentation budget for expensive owner work or slow detached I/O/network preparation. Large/custom maps and resource-heavy checkpoints are the relevant risk. A wall budget cannot preempt one monolithic OnLoad/restore.

**Fix:** instrument and separate read/verify/decompress/package preparation from owner binding, use a staged reconstruction state machine and bounded owner work where practical, or surface progress before unavoidable construction. Share rather than duplicate file lookahead (R6). Retain unpublished-replica transactional replacement and120fixed-step cap. Do not move mutable Scene/GL state to workers.

**Benchmark:** first killcam/replay preparation, random long seeks and repeated backward seeks on large maps with high projectile/effect counts; show P95/P99 owner update blocking, stage times and maximum single Step/rebuild. Test cancellation and failed stage cleanup. Current step caps should remain, but do not advertise1ms as a total construction guarantee.

### R8 — Recoverable partial recording files are outside retention/storage accounting

**Medium; confirmed. Impact 6 / Risk 3 / Effort 3 / Confidence 10.**

Anchors: `src/MphRead/Mods/Network/ReplayFormatV3.cs:255–268`, `:352–357`; `src/MphRead/Mods/Network/ServerReplayRetention.cs:12–16`, `:49–55`; `src/MphRead/Mods/Replay/ReplayStudio.cs:558–574`.

Writers correctly retain `.ppdemo.part` after abort/crash and refuse to truncate an orphan. Both server and personal retention enumerate only finalized `*.ppdemo` files, so those recoverable chunks contribute neither bytes nor age to the advertised storage policy. Repeated crash/overflow/storage failures accumulate disk usage indefinitely outside a25GiB/14day server policy. This is not a request to silently destroy recovery data; recovery and bounded retention must be designed together. Finalized-sidecar byte accounting also understates storage; primary risk is potentially large unbounded orphan chunks.

**Fix:** a separate orphan recovery/quarantine policy with age/count/byte bounds, explicit user/operator visibility, and protection for active writer paths. Retain a useful newest recovery window and report when protected/recoverable data exceeds policy. Include artifact bytes in storage diagnostics and clean old abandoned temporary sidecars safely.

**Regression expectations:** benign aborted/crashed writer fixtures count toward recoverable storage, active writers are preserved, selected recovery succeeds before expiry, and old eligible orphan cleanup stays within policy. Existing finalized KeepLast/favorite protections must remain; exceeding the budget due to deliberate protections should be reported, not silently treated as success.

## Coverage and sound safeguards to preserve

- **Authority/process model:** DedicatedServer + ServerSim own match outcomes; normal clients cannot publish authoritative snapshots/results. Hosted lobbies run separate processes because NetSession has static session state. Idle authoritative lobbies/nonoccupied continuous servers defer60Hz simulation and wait on transport activity. Do not combine many games into one process without a separate explicit instance-isolation project.
- **Movement scope:** owner-reported position and same-life no-reconciliation are deliberate. N2/N3 harden validation/resources within that model; they do not propose rollback, movement command streams, observer deltas or historical client authority.
- **Transport/lifetime:** NetTransport's live inbox is capped2048 with reserved control capacity; connection table cap320; synchronous sends consume spans; delayed fault-injection queues own copies; rented receive buffers are returned on drop/drain. Reliable queue ordinary32/total40 with history256 and critical reserve avoids a whole unbounded retry structure (`NetReliableChannel.cs:15`, `:90–102`). Admission binds endpoint/connection IDs; queue peers cannot author gameplay. Discovery has a useful global rate limit; N5 is the missing registration identity/population bound.
- **Hosting:** HostRequestGuard's endpoint+nonce HMAC return-path proof, cookie expiry, per-IP/global token budgets and altered-request rejection are real protections. HostedMapRequests custom preparation is bounded and deduplicated. The fix for N4 should apply that idempotency equally to builtin requests without conflating users by NAT address.
- **Lifecycle/projectile/lag compensation:** match/epoch, generation/life and fire IDs are checked before ordering; late packets cannot reset a new occupant's baseline. ShotId pairing is independent of ACK/source frame. Traveling projectiles preserve their launch fence through shooter death/respawn while old occupant/epoch invalidation is guarded. Ricochet children inherit the original fire identity. NetUnlagged and dynamic geometry/contact histories preserve bounded60Hz history; historical target interpolation does not rewind foreground movement. Keep finite impulse, victim history and detached-halfturret checks.
- **Claims:** claim count/framing, life/time/radius/impulse bounds, grace deadline, capacity refusal, no duplicate damage, spawn/team damage protections and explicit verdicts are present. Authority body-hit/headshot difference applies only missing damage, and speculative lethal damage is deferred at1HP. Those protections should survive N1; they do not prove launch/LOS themselves.
- **Quickscope/continuous/alt integration:** FireEvent's scoped-at-fire bit and TryScopedAtFire preserve the source shot's Imperialist scope; continuous Shock Coil source tick is retained; Samus reported boost is constrained to stock legal max and one-hit consume state. OneInTheChamber has successful-shot/ammo-specific checks. Do not blame all quickscope/alt lag behavior on networking without packet/asset-backed timing tests. High-speed/contact/dynamic platform verification remains an important test matrix.
- **Replay pipeline:** accepted canonical recorder feeds full client/server recordings and instant clips; there is no need to invent another recorder. RollingReplayTimeline has64MiB pooled-capacity accounting, whole-segment eviction and invalidates a continuation containing a dropped fact (`ReplayTimeline/RollingReplayTimeline.cs:58–74`). Frozen clips retain independent leases and survive timeline reset.
- **Write ownership:** ReplayWritePump has4096commands/32MiB and4active-writer admission, exclusive worker file I/O/compression, producer nonblocking overflow abort, and release in completion/error draining (`ReplayWritePump.cs:47–54`, `:101–112`, `:123–131`). The existence of `.part` on abort is valuable recovery behavior; R8 is the missing separate disk policy.
- **Private scenes:** passive hosts/replicas own decoder/lifecycle/random/clock/players/resources and no live gameplay socket; replica construction/cleanup does not rebind foreground static facades. Durable optional checkpoint failures dispose the replacement and rebuild fresh. Cache count cap128 and fixed-step cap120 are real; R5 corrects byte accounting, and R7 identifies the owner-work boundary.
- **Format/compatibility:** size/count/decompression/CRC/index validation and protocol adapters are present. Unknown world contracts/anchors must fail closed. Root reports unmodified server `-replayformatcheck` passed2888 assertions, while the supplemental replay control suite stopped at the stale historical layout expectation described in R3. These checks do not cover asset/GL world restore, exact custom-package preflight or malformed late world nodes. Parent owns clean client build failures and supplemental build qualification.

## Practical validation order and metrics

1. Establish benign negative authority/resource/finite-intent cases for N1–N3 using production policies and fake side-effect sinks; no public servers or external attack traffic. Preserve the existing ghost-shot, chamber, headshot-gap and projectile-lifecycle matrix.
2. Verify fake allocator idempotency and fail-path disposal N4/N6/N7; then normal local UDP lobby→starting→match→results→lobby, rejoin, spectator, waitlist and custom package barrier cycles.
3. Fix replay exact identity and initial-world transaction N/R1–R3 and verify archived supported producer fixtures. Compare full gameplay hash plus animation/effect projections at visible start, linear run, forward/back seek and exported frames.
4. Correct capacity accounting and key pruning before a memory soak; measure real retained pooled bytes, keys, allocation/GC rates, file handles, native/GL owners and process RSS over many open/seek/cancel/killcam cycles.
5. Profile before optimizing DNS/pacing/readers/construction: step deadline misses, intent age and P95/P99 authority times; cold/warm seek and maximum owner update stalls; decode counts/bytes; crash-orphan and finalized disk usage. Keep the synchronous storage producer path nonblocking and preserve60Hz simulation under presentation/export rates30/60/120/144/240.

## Verified cleanup candidates and historical/documentation issues

- `src/MphRead/Mods/Network/NetSessionLanes.cs:18–25` private SendHostLanes has no callers anywhere in tracked source. Its `_hostLanes` instance at`:7` is referenced only inside that unused method. Remove the method and owned field together; preserve `_laneReceiver`/`_laneCanonical` and archive lane decoding. This is a narrow cleanup, not a request to strip legacy reader compatibility or change protocol layouts.
- `src/MphRead/Mods/Replay/ReplayStudio.cs:657–660` private ReplayStorageManager.TryDelete has no calls in its class or elsewhere. Delete that unused helper while touching retention; actual artifact ownership deletion remains in the live methods.
- `src/MphRead/Mods/Network/NetUnlagged.cs:19–22` still describes first-client authority, and`:46–51` says no movable room geometry is rewound. Current dedicated-authority and dynamic-geometry paths supersede those comments. Update them in a documentation-only change; dated lag measurement evidence should stay labeled historical.
- `ReplayReviewCheck.cs:14–16` historical hash assertion claims “unchanged” layout while schema histories prove additions. Keep the compatibility requirement visible and replace it with real archived fixtures/migration expectations. Do not delete the assertion merely to make CI green or unconditionally whitelist an old hash.
- Do not delete v2/v3 readers, bootstrap/lane adapters, archived conversion or diagnostics merely because normal recording writes v4. They are deliberately permanent compatibility surfaces. Likewise do not remove traveling-shot launch history or critical reliable capacity as apparent redundant state without life/packet-loss tests.

## Review limits

This module audit traced gameplay intent→server→bridge→shot origin/fire/claims/damage, hosting challenge→allocation→process lifecycle, directory heartbeat/query, replay accepted facts→timeline/write→metadata→reader→private world/checkpoint→pose/seek and exceptional disposal paths. History was consulted for current replay schema and historical compatibility expectations. No source-level trust-boundary issue was described as an executed exploit. DNS/pacing/readers/construction costs are candidates requiring benchmarks. Asset-backed multiplayer gameplay, actual platform GL restore, Android AOT, multi-hour playback and live regional latency matrices were not run by this child agent; the root audit is collecting the available builds and existing benign checks.
