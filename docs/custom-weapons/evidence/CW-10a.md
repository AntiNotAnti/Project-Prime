# CW-10a evidence — native profiles and bounded fallback

Base: latest main `09822e2f8fe8ae412f1c464075d79c87726729a1`
(CW-09 PR904 plus evidence correction PR905).
Head: completed-slice commit associated with this evidence/PR; no CI claim/wait.
Scope: native profile/budget classes, existing body-only fallback/scene adapter
and focused CPU/actual-rig tests. No wire, mechanics, assets, replay format or
shipping project/dependency change. [Design](../10a-corpse-profiles-fallback.md).

SDK10.0.401/macOS arm64; DOTNET_PROCESSOR_COUNT=2, single MSBuild worker,
UseSharedCompilation=false. Desktop/server Release, Android default embedded
arm64 Debug. Server intermediates/output are separately owned; game sources
were frozen before final three-target builds. Asset-backed processes have
separate PROJECT_PRIME_USER_DATA directories and readonly paths fixtures.
Raw commands/logs/receipts are under `artifacts/custom-weapons/CW-10/`.

Final desktop DLL SHA256:
`0f03fa9fc780355d29797425294ff129d3a22aed4f2203d1f6df44765f8ac0c2`.
Final fixture SHA256:
`2080c9f34d6806c84c14de77ad7491227e9b8c206f5d3fabba1138ad95b07a9a`.

| Check | Actual result |
|---|---|
| Desktop Release | PASS89.133s,1 existing warning,0 errors |
| Server Release | PASS60.348s,1 existing warning,0 errors; separate output |
| Android default arm64 embedded | PASS131.303s,26 existing warnings,0 errors; compile/package only in this slice |
| Fixture Release | PASS3.68s,0 warnings/errors |
| Full CPU suite | PASS35,726 checks,12.559s |
| Actual lethal-head native GL | PASS12,295 checks/56 genuine M7 deaths/322 captures/420 LOD seams,85.984s |
| Actual native replay/replica/cold reset/seek | PASS3,278 checks,2.323s |
| Native protocol registry | PASS78 historical names unchanged; protocol76 |
| Native tuning tables | PASS all original stored MP/1P fields |
| Native fidelity | Known46/47 baseline,3.799s,raw exit1; missile splash tick177 victim.position4096.z expected52814/actual52815 unchanged |

The new68 CPU assertions cover pure quality/distance boundaries, adjacent
representable squared distances, unknown quality, nonfinite/negative inputs,
100k nonvacuous warmed budget queries at0B and unchanged gameplay RNG. Actual
Scene CPU dispatch also tests inclusive24-unit admission, irreversible far/Off
retirement,8 accepted injected death causes with deterministic2-slot Low capacity,
unchanged canonical HP, quality upgrade refusing to restart retired causes and
headless dispatch creating no tracks. These explicitly inject accepted facts/HP
and are not network or genuine damage claims.

The GL fixture retains its declared shooter-only developer ownership, victim100HP,
genuine floor/clear ray/free camera and native-only rendering. It invokes the
actual M7 emitter/collision/TakeDamage/normal death path for all7 Hunters and8
catalog choices. It checks original classic/catalog lifetimes, exact ankle
positions/leg lengths, all19 composed nodes and full palette restoration, source
resources/HP/score/RNG/position/aim/muzzle/clock invariance and respawn cleanup.
Source hiding is only for packet-identification/capture disambiguation, as in
CW-08; no new multi-actor physical palette qualification is implied.

New actual-rig checks resolve all14 strict native model/LOD profiles, palette
counts17/16/17/17/14/13/17, named leg ancestry and Spire's unweighted head. Cloned
unknown-name/duplicate/out-of-range palette layouts refuse prewarm and make the
scope an exact no-op without changing their original models. Replacing either
LOD with an independent unsupported clone disables whole-pair Classic readiness
and visibility; restoring the pair restores readiness and exact original matrices.
All profiles truthfully retain PhysicalQualified=false in this slice.

An initial compile failed on private CosmeticDeathDefinition access; the scene
now uses a narrow internal read accessor to the existing selected definition.
The corrected/final builds pass. No failed command is represented as PASS.

CW-10 is ongoing: no physical handoff/skin bind or game static-world export is
qualified here. CW-09 physical/emulator results remain separate pilot evidence.
Modern/HD multi-actor palette use, moving surfaces, physical replay,100+ actual
integrated respawn/scene cycles and target frame budgets remain subsequent gates.
No new network code changed; live transport suite was not repeated in10a.
Existing device preview/re-entry and reload uncertainties remain CW-16 blockers.

The first graphics pass's condition `Head>=.99` skipped malformed/partial-LOD
probes: actual sampled peak Head was0.875 because death/cause clocks differ by
a step. The condition now selects the nonzero Classic kick, and a separate
receipt plus mandatory assertion requires exactly14 malformed-layout cohorts and
14 incomplete-pair probes. Final actual result is14/14 for both categories,
code0 (`native-rig-negative-final/profile-negative-results.json`). The previous
positive graphics run is retained; it does not count as negative coverage.
Final CPU/replay runs use the same final fixture hash above. Production DLL
remained unchanged throughout the corrected test run.
