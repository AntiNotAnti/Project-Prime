# CW-10d evidence — native primitive fitting

Base: latest main `3b035836150070bbb40740ae699a0602c9f64a90` (PR914), including
the unrelated low-travel movement and launcher/asset-guard fixes. No unrelated
changes are reverted. Scope: new leaf profile/capture API, copied joint bases and
standalone/native CPU diagnostics. [Design](../10d-native-collider-fitting.md).
No shipping game/server source or project reference changed; CW-10 remains ongoing.

SDK10.0.401/runtime10.0.12/macOS arm64, processor count2, single MSBuild worker,
shared compilation off. Raw receipts: `artifacts/custom-weapons/CW-10d/`.
Final core SHA256:
`b095372efe34def719a5b31fcb37d80a22aacd14611b752ceefeba966981f3c5`.
Final native diagnostic SHA256:
`898d243dee4f3da5ce7570e696b830d91e1a13509a06990f6775371c59c66885`.

| Check | Actual result |
|---|---|
| Leaf Release build | PASS, zero warnings/errors |
| New isolated synthetic fitting contracts | PASS330,0.201s, zero build warnings/errors; immutable capture/calibration, bounds, malformed neutral refusal, proper uniform frames, authored virtual wrists, wrist roll, foot/head frames, common rigid/joint covariance and full-affine helper preservation |
| Synthetic fitted solver | PASS15 bodies/42 joints/120 ticks,13 additional contact constraints, finite/no fallthrough/zero faults |
| Full standalone pilot | PASS2,624 contracts,45 workloads and128 worlds/1,024 corpses |
| Actual native CPU fit | PASS27,168 checks,14 independent model/LOD calibrations and neutral captures;1,080 supported poses and12 explicit refusals across1,092 sampled cases;0.991s |
| Actual fitted-neutral solver probes | PASS all14 ×120 ticks,9–13 additional contact constraints, zero faults; worst box-corner penetration.04284114 within unchanged.15-unit floor tolerance |
| Existing full custom CPU regression | PASS35,726 checks,5.565s, isolated user data |
| Final native tool build | PASS3.141s, zero warnings/errors; BuildProjectReferences=false |
| New shipping game/server/Android build | NOT_RUN; no shipping source/dependency changed in this leaf slice. Existing CW-10c Android coverage does not qualify this new fitting API; later integration must test it on Android. |

The native fixture reads owned AMHE1 assets through isolated user-data/paths. It
uses the unchanged CW-10a engine DLL
`0f03fa9fc780355d29797425294ff129d3a22aed4f2203d1f6df44765f8ac0c2`.
It does not claim a current main game build or acceptance of later unrelated
movement/UI behavior. All referenced/copy DLL hashes remain unchanged per run.
No extracted animation/model or derived native screenshot is committed.

Calibration uses each actual LOD's own composed bank0/frame0 matrices with
explicit up/forward. Current cases sample all26 banks at first/mid/last frames
under a common yaw/pitch/world translation. The12 refusals are Trace/Noxus,
both LODs, bank17 frames0/5/9. Source enum names that bank Unused17; its unit
bases differ from these profiles' approximately.706 calibrated bases. The strict
scale/segment policy is retained; no assertion or bound was weakened to include
unsupported poses. Runtime morph/frozen/unknown-profile policies remain separate.

Maximum full native affine rest reconstruction error is9.536743e-7; maximum
hand/foot transform covariance error1.0728836e-6. Actual missing-wrist cases
exercise bent native forearms; no muzzle or upper-arm extrapolation supplies a
hand. Nodes, transforms, palette/skin indices, control flags and gameplay RNG stay
exact. Each returned rig/binding owns its data. The synthetic test separately
rotates a real wrist around its longitudinal axis and verifies hand roll follows.

The fourteen physical probes use explicitly synthetic floor/spawn descriptors,
not accepted game deaths or native room collision. Maximum measured BallSocket
drift is.07481005 units; that number is not art approval or a new stability
threshold. Boxes/cross sections and missing wrist/hand/sole offsets are authored
primitive approximations, not recovered anatomical meshes or collision volumes.
This fixture does not execute PlayerDraw's split/crouch blends, GPU rendering,
HD replacements, scene/life ownership or physical replay.

Read-only audit corrected three issues before qualification: neutral creation
must fit its own pose, real hands must use their wrist's roll, and roll fallback
must use a second current native axis rather than a global direction. The original
candidate was superseded before an isolated test copy, and no unrun production
RED is claimed. A hash-guard stop and corrected test-fixture Spine_1/Pelvis mistake
are retained as diagnostic history, separate from the final330-check pass.

All45 final desktop warmed step/sample rows report0B. Lifecycle arena plateaus
at1,073,152B and releases to0B. Eight-corpse/240-scheduled-Hz step p50/p95/p99
=.192791/.205875/.260917ms, maximum.319625ms. These standalone CPU workloads are
not achieved refresh, a controlled game-frame delta or physical-device acceptance.
Capture allocation is bounded once per corpse; it is not claimed allocation-free.

Rollback removes the unused profile API/diagnostics and restores the prior common
joint basis policy. Default identity retains the reference pilot behavior. No
shipping option or data migration is enabled. CI/review is not claimed or awaited
under the user's immediate-merge instruction. Accepted-life render integration,
source fences, GPU ownership and integrated100+ death/scene cycles remain next.
