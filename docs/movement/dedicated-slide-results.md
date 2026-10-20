# Dedicated slide presentation

Date: 2026-10-20. Classic remains the default; live protocol 76 and movement
profiles are unchanged from the ramp fix.

The previous glide reused a stable crouch skeleton. Traversal now has a dedicated
procedural slide pose: a six-tick entry, stable asymmetric extended/tucked legs,
a small rearward pelvis shift and twelve-degree torso lean, and a six-tick
recovery aligned with crouch recovery. The cannon branch counter-rotates the
cosmetic torso lean to retain its accepted aim orientation. Travel heading is
captured and smoothed in world space so aiming sideways does not turn the slide
pose away from actual momentum.

Only completed fixed-tick travel advances presentation. Extra pictures sample
readonly history; missing/backward frames, teleports, life/form changes and
presentation resets discard stale transitions. Replica/puppet glide inference
is cosmetic and requires native body eligibility and real Standing contact.
Frozen, Prime, locked, attached, carrier and other excluded bodies cannot acquire
an inferred slide pose. No input entitlement, slide timer, collision body,
velocity, native animation cursor, camera, muzzle fact, packet or replay checkpoint
is changed by the pose. Canonical biped composition bypasses cosmetic gait and
retains the original crouch arithmetic. Existing draw isolation restores native
nodes and matrix stacks after each picture.

The pose samples each native Hunter's leg lengths and neutral stance. Two-bone
solving preserves lengths; the visual ankle target is bounded by native reach
without raising the foot plane. Unknown replacement skeletons retain the existing
height-compression fallback. No new model, animation, sound asset or dependency
is distributed.

Verification is performed on Windows, including actual W/Ctrl slide entry,
raw mouse aim turns, release recovery, all seven Hunters at both LODs, pose and
cannon comparisons, repeated-picture purity and exact canonical/source-state
restoration. Native OpenGL captures use body-hidden negative controls and
identical repeated RGB. Original screenshots/assets remain local; only selected
measurements and hashes are published.

All eight focused Windows gates passed: 3,879 pure stance/timeline assertions,
89,569 outer stance assertions plus 1,453 dedicated-slide checks per Hunter,
335 ground rules, 413,037 ground-scene assertions, 56 native movement goldens,
metadata, compatibility and protocol-58 archive contracts. Maximum measured leg
segment error is below 0.000002 world units (tolerance 0.0005). The unchanged
actual protocol-76 Traversal recording passed 115,830 replay checks across 2,399
frames, including 7,018 stance comparisons.

The normal Windows Release production build had zero errors and one unchanged
CS0649 warning. Candidate 02 rebuilt only nettest after the fixture corrections
below; exact candidate-01 production and reference outputs were retained and
hash-fenced. Final runtime/test source bytes match the frozen candidate.

Native NVIDIA OpenGL passed all 55 body/hidden/repeat groups (165 PNGs), including
held-W slide and forward/backward/side crouch controls. Repeated RGB is exact.
Selected positive images were inspected locally. The first SSH render attempt
could not see a primary display and failed before window construction; the same
binaries passed through the existing active Windows desktop's interactive task.
No display guard was bypassed. Owned processes and scheduled tasks returned to
zero.

See [sanitized evidence](evidence/dedicated-slide-selection.json) for exact
source/binary/recording and local PNG archive hashes. Hosted CI includes known
unchanged launcher header and Android NDK download failures recorded on the ramp
PR; those do not establish a movement failure.

The first asset fixture exposed two invalid assumptions. Ordinary biped keyboard
AimRight did not generate a yaw delta, so the turning test now supplies actual
raw mouse positions and verifies the gathered delta and body turn. Kanden's
walking animation changes joint offsets; comparing its canonical Walk frame's
leg length with the visual Idle0 skeleton was the wrong baseline. The corrected
check independently samples the actual unposed Idle0 skeleton and retains the
same strict segment-length tolerance. Production bytes are unchanged by these
test corrections, and original failed logs are preserved locally.

This slice makes no whole-frame performance or physical input-feel claim.
Improved planted crouch footwork/pelvis shifts and optional HUD/audio feedback
remain for subsequent PRs. Hosted CI status is recorded on the PR; merge proceeds
without waiting for CI or review as requested.
