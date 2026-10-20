# Custom weapons implementation ledger

Current gameplay correction: the user clarified on October 20, 2026 that the M7 must be a world pickup, not a menu/loadout grant. This overrides the attached CW-05 selection proposal; see [pickup contract](05k-pickup-correction.md).

Source specification: `PROJECT_PRIME_CUSTOM_WEAPONS_SNIPER_HEADSHOT_RAGDOLL_IMPLEMENTATION_PLAN.md`
supplied by the user on 2026-10-19 and preserved as [implementation-plan.md](implementation-plan.md). The user's request authorizes sequential CW-00 through CW-16,
one independently reviewable PR and completed-slice commit at a time, immediate merge after
local acceptance, and **no wait for review or CI**. CI queued/running is never reported as PASS.

Initial source: `d726c766a10affaa0d8f95bc25bce9d178528215` (latest fetched main at kickoff).
Implementation uses an isolated managed worktree; unrelated working-copy edits are excluded.

| Slice | Deliverable | Status |
| --- | --- | --- |
| CW-00 | Source/behavior baseline and five ADRs | Complete; documented baseline failure/platform gaps |
| CW-01 | Immutable identity and registry | Complete; 69 checks and native parity passed |
| CW-02 | Mechanics schema and deterministic per-life state | Complete; 1,179 checks, zero-allocation routine ticks, native parity |
| CW-03 | Offline M7 through canonical combat hooks | Complete; 1,181 pure and 45 real-scene checks; native parity |
| CW-04 | Custom action authority/network state | Authority implementation complete through04q: live private results, public recipient replicas, exact source/resource/contact admission, impaired ordinary-client owner symmetry, spectator reconnect/old ACK refusal and native client HP/death projection validated. Developer flags remain off; public controls/device cadence are CW-05 and custom bots/packages/replay are CW-12. Physical/rendered/WAN acceptance remains a release gate. |
| CW-05 | Pickup acquisition, input, HUD and scope | Pickup/input implementation complete through05t:05a–05i delivered ordered input, source selection history, native isolation, device bindings, optics/HUD, frozen match availability and settings persistence.05k retires05j menu selection after the user's world-pickup correction.05l adds source-fenced engine funding;05m adds immutable layouts and world arbitration. 05n adds actual offline world acquisition on all27 stock maps, empty public lives, next-tick funding, respawn/refill and the Android offline activation path. 05o adds owned-weapon Next/Previous cycling with tagged targets and native eligibility. 05p adds the acquired-only gameplay wheel for mouse, controller and absolute stylus targets while keeping unowned holds native. 05q adds private pickup knowledge, immutable accepted-source evidence and reconnect fences, still without live wire activation. 05r removes the online spawn grant and adds explicit private empty-life state under protocol72. 05s carries creation-time private knowledge through owner envelopes, genuine accepted input, retained journal/outbox terminals and protocol73 without putting nonces in public/native payloads. 05t activates authoritative online contact/funding, private knowledge admission and application-acknowledged public item state under protocol75. Public combat fixtures now collect actual authored items and wait genuine1800-tick respawns; ordinary reconnect generations start empty. Device/display-Hz release qualification and custom replay remain open. |
| CW-06 | Original assets and presentation | Implementation complete through06a/06b: original CC0 two-LOD resource, digest/bounds validation, shared pickup geometry, seven arm-mounted native rigs, fixed-step bolt/reload/raise, original PCM with native fallback, muzzle/trail/spark and HUD. Desktop/server/Android builds and genuine Android touch pickup/fire/scope/empty are validated. Physical audio/context/high-refresh qualification, a minor Trace recoil fit issue and the existing Debug automatic-preview crash remain CW-16 gates; see06b evidence. |
| CW-07 | Accepted nonlethal head reaction | Implementation complete: full-life accepted native/custom/local facts, bounded future queue and freshness/dedupe, fixed-step head/spine/shoulder scope with exact matrix restoration, seven native rigs and both LODs, actual moving/jumping/scoped/form controls, genuine M7 collision/HP/protection/death/respawn and native replay linear/seek CPU evidence. Physical/HD/continuous high-refresh/WAN acceptance remains CW-16; custom replay remains CW-12. See CW-07 evidence for precise fixture limits. |
| CW-08 | Lethal head collapse/cosmetics | Implementation complete: accepted full-life native/custom/local cause latch, delayed snapshot/timeout fences, bounded helmet/torso/analytic knee collapse, exact body palette restoration, all seven native Hunters/two LODs/eight death choices and held nonzero disappearance policy. Genuine M7 kills, actual native replay/seek/cold visual rebuild and platform builds and all24 network regression commands validated. Physical/HD/continuous animation and inherited device gates remain CW-16; custom replay remains CW-12. See CW-08 evidence. |
| CW-09 | Experimental physics pilot | Standalone fifteen-part backend, static collision, pure interpolation and procedural baseline implemented; desktop CPU/GL/lifecycle and actual Android Debug/arm64 Mono AOT CPU/lifecycle evidence recorded. Physical/game-renderer/target-budget gates keep physics outside the shipping game. See CW-09 evidence. |
| CW-10 | Hybrid corpse rollout/fallback | In progress:10a validates all14 native model/LOD layouts, whole-pair fallback and bounded fixed-step quality/distance retirement;10b supplies independent affine pose capture/sample and named helper mapping;10c adds bounded copied static polygon validation and relative-height retirement, with stock-map and standalone solver evidence. Physical handoff/independent pose ownership and full lifecycle acceptance remain subsequent CW-10 work; no shipping physics option enabled. |
| CW-11 | Safe local packages and installation | Pending CW-10 |
| CW-12 | Match readiness and replay manifest | Pending CW-11 |
| CW-13 | Typed Community weapon publication | Pending CW-12 |
| CW-14 | Studio creator | Pending CW-13 |
| CW-15 | Community creator/install UI | Pending CW-14 |
| CW-16 | Full release/security/platform acceptance | Pending CW-15 |

CW-05j evidence remains historical. Its menu design and the unmerged responsive
menu work are superseded by the user's world-pickup correction. No pickup
acquisition or mobile usability completion is implied by those earlier captures.

No pending row denotes delivered runtime functionality. Device/visual/network gaps remain explicit
release blockers. Foundation code alone cannot satisfy the final create/publish/download/play/replay flow.

Evidence lives under [evidence](evidence/); local complete logs under
`artifacts/custom-weapons/CW-NN/`. Production game files and private credentials never enter Git.
