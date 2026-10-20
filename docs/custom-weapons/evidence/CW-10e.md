# CW-10e evidence — accepted-life physical runtime

Scope: experimental client integration, independent native LOD palettes, copied
static sources and original-thread cleanup. [Design](../10e-accepted-life-physics.md).
Runtime defaults off; no packet, native weapon table, pickup or replay format
changes. The initial candidate is based on main76d0654b (CW-10d).

Environment: SDK10.0.401/runtime10.0.12, macOS arm64, processor count2,
single MSBuild worker and shared compilation disabled. Owned AMHE1 data and
isolated process user-data/paths supply actual native tests. Raw commands,
environments, source/copy hashes, failures and results remain under
`artifacts/custom-weapons/CW-10e/`. No extracted assets or native screenshots
are committed.

Candidate4 desktop SHA256:
`e0cc3a88445ca41279a6c8e60a1edc95f82d9f0cd6f4b2371b55888d603c0ece`.
Core SHA256:
`b6789b19ddc9a2e6c72cf6e9ae16d82c31c75cda03256bb6931a8000ad6c0491`.

| Check | Actual result |
|---|---|
| Desktop candidate4 Release | PASS93.22s, one existing DiscardPickupAcks CS0649 warning, zero errors |
| Dedicated candidate3 Release | PASS94.98s, same one warning; dedicated output/deps/assembly tokens contain no Core or BEPU dependency |
| Actual accepted-M7 smoke | PASS51 checks: native collision/TakeDamage, queued drawing, next-fixed-step activation,15 bodies, independent packets, exact source restoration and cleanup |
| Candidate4 capture-corrected native matrix | PASS1,061 checks/14 cases/77 PNGs,18.535s; six actual physical Hunters, explicit Spire fallback, actual near/far/near LOD selection and both LOD packet-copy independence |
| Integrated native lifecycle | PASS2,780 checks/8.932s:128 primary death/native-respawn cycles,137 actual deaths/129 respawns including baseline and8 active-world cleanup deaths;8 scenes, zero faults |
| Managed/native lifetime | All136 backend/capture weak-reference pairs collected after cleanup and forced GC outside timing; active pool maximum2,228,224B in first16 and later112 cycles, every respawn/cleanup releases to0B |
| Full custom CPU / head-death replay | PASS35,726/15.653s and3,278/2.149s on exact candidate4 engine |
| Full Android game Debug arm64 | PASS114.09s,26 warnings/zero errors; desktop DLL unchanged |
| Android game package | Signed APK99684605a1bbf6703542b7d01acbdf1b67abe2cc347d2a8aa95888eef0fca172,77,442,909B; BEPU/Core assembly containers and third-party notices included; build/package proof only |
| Standalone Android arm64 Mono AOT | PASS1,272 contracts,45 workloads,128 worlds/1,024 corpses, all warmed step/sample rows0B;24 AOT modules and3 relevant mapped modules; no permissions; not game GPU or physical-device proof |
| Server/desktop replay format | PASS3,050 each |
| Server/desktop cosmetics | PASS16,226 each |
| Server/desktop replay control | PASS both actual CLI runs |
| Fresh nettest on candidate4 | PASS architecture, protocol17, replay932, Quake659, Momentum126, reliability, accepted343 and Imperialist949; references-only build5.05s/zero warnings/errors |
| Native fidelity oracle | FAIL46/47 on both server/desktop: only known weapon.missile.splash tick177 victim.position4096.z expected52814/actual52815; unchanged Imperialist digest6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351 |
| Standalone simcheck | FAIL120 steps/zero snapshots/spawns, NullReference NetMatchSync.Apply:43. Reproduces exactly on retained pre-runtime CW-10a server and current desktop/server; still a failed diagnostic, not reported as PASS |

The matrix uses declared test-only shooter ownership and authored actor placement,
then fires the actual M7 emitter through native collision and damage. Victims own
no custom weapon. Accepted cause, native death/kill and custom magazine payment
must occur exactly once, with native Imperialist kill accounting unchanged.
Drawing must leave health, body position/speed, RNG, frame count, weapon state,
muzzle/aim and every shared node/control/palette unchanged. Earlier copied GPU
packet ranges must survive later LOD and same-Hunter actor submissions.

FeatureOff, qualityOff, HD-enabled and nonzero catalog cases allocate no physical
world. A disabled/gated death cannot restart when the gate is removed. Teleport,
chunk Translation and Active changes retire the existing world and release its
pool, with no resurrection after source restoration. The stock Proving Ground
position rejects extents4/3 and accepts extent2/two triangles under unchanged
polygon tolerances. Each supported Hunter remains active through35 physical
steps with contacts and no fault, followed by actual native respawn and release.

Failures retained: first fixture compile used an ambiguous ErrorCode enum and
was corrected; actual matrix caught an HD-off mid-death restart and production
now consumes current causes at every scene fence. The first nonzero-catalog
fixture targeted a remote offline actor, where debug appearance overrides do
not apply; that negative case now correctly uses the local actor. The initial
matrix saved window images after Present, contrary to the capture API contract;
those images are superseded, not accepted as motion-frame evidence.

Android pilot APK779c26712e0abd89c8f952075dd41a97cb94c7c86b249873b5e804680a656b9b
actually contains coreb095372e, not current desktopb6789b19. Read-only PE comparison
finds all275 IL bodies/286 methods and36-type API identical. Changes are limited
to Git informational/product versions, MVID and derived deterministic debug
identities. Explicitly normalizing only those fields makes the entire PE equal
(SHA9b517695c1cd9725cb80f1eaa459c6f83d76b7660c1c9edc3c34cdaa3ab5b564).
Current leaf sources equal committed CW-10d sources. This establishes executable
equivalence; it does not relabel the APK's actual binary or qualify native game
rendering on Android.

The lifecycle fixture retains real fire deadlines and advances ordinary native
simulation until each75-tick cooldown matures. Declared magazine-only fixture
refill leaves owner, source sequence, optic and deadlines intact. Every cause
comes from the actual projectile/collision/TakeDamage path, every respawn calls
native Player.Spawn, and every final scene cleanup begins with a live backend.
Whole-engine timings include native simulation, rendering and Present; they are
not isolated physics cost or a controlled on/off delta. Initial-run values:

| Whole operation | Samples | p50/p95/p99 ms | Managed bytes p50/p99 |
|---|---:|---|---|
| Feature-off simulation |24|.103208/.159667/.271500|2,048/2,128|
| Active simulation |1,536|.070416/.319042/.449834|1,016/2,160|
| Feature-off draw/render/present |24|4.201334/8.646833/8.747000|15,184/16,416|
| Active draw/render/present |384|2.124417/5.565042/7.901167|15,400/15,432|

Different sample cohorts and warmup explain why these cannot establish a speedup
or device budget. Raw samples and the separate final-retention run are retained;
no whole-game zero-allocation or process-RSS plateau claim is made. The corrected
window captures execute before Present, contain distinct0/12/24/35-step images
for all six physical bodies, and preserve the Classic particles/fade. Parent
inspection confirms the sampled Samus/Trace floor collapse and Spire fallback;
continuous motion and general art approval remain outside this fixture.

Costs of copying/calibrating/capturing/creating worlds are bounded setup costs,
not claimed allocation-free or target-device approved. The new aggregate pool
and contact bounds are retirement tripwires rather than hard peak caps. Native
primitive/body movement screenshots require visual inspection and do not prove
continuous animation, absence of every mesh intersection, achieved display Hz,
WAN rendering, HD retargeting or physical Android acceptance. Physical replay
remains disabled; existing approximate collapse/catalog clocks remain intact.

The existing CW-06b Android touch-reload and preview/context-crash limitations,
Trace recoil fit issue and physical audio/device qualification remain CW-16
release blockers. CI/review is not awaited or claimed under the user's merge
instruction.

## Final rebased source qualification

Slice1dcf7b6f001dcbd13364ef1f7acf095f6063d9e8 rebases cleanly onto latest fetched
maincc8babc0d (PR920), preserving the unrelated drone/reticle/launcher work.
Final production/test sources remain unchanged through qualification; the final
documentation amendment adds these receipts and the user's Android-test deferral.

Final desktop DLL:
`ff77bbc579847b8bc2ffc765812b7b39e0e13e93dbcb273de773d8cb32280b7e`.
Final leaf DLL:
`35cfd545cefca4220c51d5fa57d877dce55d6b0ad77735784f967dbabfec307a`.
Dedicated DLL:
`4f5e9a103690510abdd3e9b5c9bf183f0433c9c8bbdfde5a32f89213cad4573f`.
Diagnostic DLL:
`98a74f912899ebca9138e91677438c6daf7f6ca0d69767f4307e99d12d6da811`.

Rebased receipts live under `CW-10e/rebased/`. Desktop Release passes91.748s
with one existing CS0649 warning; dedicated Release passes56.32s with the same
warning and no physics dependency. References-only diagnostic/nettest builds
pass3.082s/4.72s, zero warnings/errors. Exact copied game/leaf references and18
production/test source hashes remain stable before/after every tool phase.

The corrected native matrix passes1,061 checks/14 cases/77 images in17.881s.
Integrated lifecycle passes2,780 checks in8.238s,128 primary native respawns,
eight active-world cleanups and all136 backend/capture pairs collectible.
Full custom CPU35,726/5.589s and head-death replay3,278/.853s pass again.
Dedicated/desktop replay3,050, cosmetics16,226 and replay-control pass; fresh
architecture/protocol17/replay932/Quake659/Momentum126/reliability/accepted343/
Imperialist949/registry76 pass. Fidelity and simcheck retain exactly the failed
baseline outcomes described above, with no unexpected failure.

Initial rebased desktop compilation and Android packaging both failed with
ENOSPC on the shared host. Those failed commands/logs are retained. Only owned
completed generated runtime copies and a stopped private emulator overlay were
reclaimed with hash receipts. No mixed old-game/new-leaf pair was tested. Both
retries pass: Android packaging56.171s/zero warnings, signed APK
`2626c0955df082963b85abfe60adbeb819736d28798da5992ab2920fff58150c`.
All four managed assembly payloads and notices are verified in that APK;
it remains compile/package proof. Read-only PE comparison also establishes
b095-to35cfd executable/API equivalence after the same explicit build-identity
normalization, without relabeling the earlier actual AOT APK.

The user subsequently requested skipping Android tests for now. No Android
process remained running, and no further Android validation is planned under
that instruction. Physical-device/native-game Android acceptance stays pending;
desktop/server implementation proceeds through CW-16. The opt-in remains off.
