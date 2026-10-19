# AI visibility occlusion query

Eight-bot movement acceptance exposed a costly AI visibility workload. On Proving Ground, Momentum's changed paths caused 20.67% more directional visibility rays and 56.85% more collision-face visits than Classic. The visibility callers discard contact metadata, but the existing query continues searching for the nearest contact after finding an obstruction.

`CollisionDetection.HasObstruction` shares the existing candidate collection, collision masks, directional plane crossing, entity transforms and polygon tests. It returns after the first accepted intersection. Only the two boolean-only AI visibility/global queries use it. Calls needing the nearest contact retain the original full query and result writes. Visibility cadence, target samples, movement, native collision response and network rules are unchanged.

## Verification

The Windows Release fixture passes 8,820 translated-room/transformed-entity, mask, endpoint, reverse and zero-length cases. Each compares repeated any-hit answers with the original query and checks exact nearest-result bits before and after. Existing 618 snapshot rows retain SHA-256 `68461616B61605635F0AA9532FA7716F2E399CF9A9D0B2190B917A5E0F7D8435`. Nested workspace lifetime, independent/concurrent scenes, cleared references and warmed allocation checks pass, including 50,000 any-hit queries with zero allocated bytes.

A separate diagnostic wrapper compared both queries immediately in the same real world, returned the original answer, and observed **1,931,862 comparisons with zero mismatches**. It covered Classic and Momentum, Proving Ground and Sanctorus, each with 1,200 warm-up and 3,600 measured simulation ticks. Four further optimized-only arms preserved exact original recorded workload hashes: warm bodies/RNG, per-step occupancy, per-slot activity/travel/lifecycle, shots and damage. This is scoped workload parity, not a universal world-state proof. The wrapper and phase instrumentation are not production code.

The diagnostic source was main `f908c7e534bce67e07a3776b21a658790034544d` plus the explicitly recorded PR-09 test overlay and this two-file production patch. The collision and AI source files are byte-identical between that base and this PR's base `361e42cb35aaf8aef5e36c566022d8705c4c7a29`. The asset-free collision fixture comes from the latter base. Host: Windows 11 x64, Ryzen 7 9850X3D, SDK 10.0.401/runtime 10.0.12, Release. The initial fixture archive included an AppleDouble source file; removing that extracted metadata file allowed the tool build to pass. Both attempts are retained.

## Acceptance status

This change removes unnecessary nearest-contact work. No percentile speedup or completed movement acceptance is claimed here. Original integrated performance failures remain recorded. Tiered-JIT settings were unrecorded in those runs; a fresh baseline/candidate experiment will explicitly control them with identical instrumentation and retain all original results. Elapsed wall-time percentiles and aggregate process CPU remain distinct measurements. Hosted CI is pending at publication; merge does not wait for CI or review.
