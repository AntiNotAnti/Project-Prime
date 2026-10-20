# CW-10d — copied native pose to primitive rig

`CorpseHumanoidProfile` calibrates a bounded named humanoid tree from copied
native composed matrices. It owns indices, body mapping, scalar dimensions and
local primitive offsets, with no engine/model/actor reference. The engine must
still authenticate a native model/LOD and bind the accepted scene/life/event.
This leaf API does not infer a death or qualify a replacement model.

Calibration and capture require finite affine input. Required body anchors must
have proper, approximately orthogonal uniform-scale frames; reflection, shear,
singularity or a later scale change over2% refuses capture. Trace/Noxus's native
approximately.706 scale is valid. Non-anchor affine data remains intact in the
independent render binding, including scale/shear and hidden zero-scale helpers.
Successful calibration must itself produce a valid neutral capture, rather than
publishing an unusable profile. Invalid input returns no partial rig/binding.

The fifteen boxes follow the actual current hip/knee/ankle and shoulder/elbow
segments. Torso extends from the actual Pelvis (or Spine_1 when absent) to the
shoulder midpoint. Cross sections derive from neutral leg-length calibration;
actual posed segment lengths are never scaled a second time. Matrices already
contain native scale/root composition. Capture subtracts one translation origin
and retains world orientations, so no additional root multiplication is needed.

Native right elbows are leaves; no right wrist bone exists. Five native left
wrists exist and use their actual posed positions/rotations. Missing wrists use
an explicitly authored local+X endpoint transformed by the current elbow matrix,
preserving a bent forearm. The proposed native caller uses.28125 local units for
the five wrist-bearing rigs and.40 for Trace/Noxus. These lengths are primitive
authoring choices, not recovered anatomy or a cannon/muzzle attachment. Hands
are authored short continuations in the current wrist/forearm frame.

Head center and sole offsets are authored in the neutral up/forward frame and
stored through each native node's inverse calibration matrix. Current head/ankle
matrices then carry those offsets through animation and root rotation. Feet are
not constructed from a world-down vector or the knee direction. No authoritative
joint or player position is moved to repair a collider.

Box localY follows the segment; roll uses that current native node's localZ,
with localY as a covariant alternate. A real wrist also supplies hand roll.
Each part now carries an independently owned common joint basis whose localZ is
the twist axis. Both connected bodies derive their local bases from the same
captured frame, starting aligned. Existing reference rigs default to identity.
Limits remain conservative symmetric swing cones/twist ranges, not anatomical
elbow/knee hinges. No gameplay constraint or collision is registered.

The returned capture owns a validated rig, initial fifteen body poses and a
`CorpseAffineBinding` of the full current native matrices. Caller input changes
and later captures cannot overwrite it. Capture allocates only bounded once-per-
corpse data; sampling the affine binding remains allocation-free and never steps
physics. Each LOD requires its own binding; one layout signature or another LOD's
calibration is not a skinning qualification.

This slice remains non-shipping. It does not implement accepted-life scene
ownership, static-source fences, actual body drawing, HD retargeting or physical
replay. The game/server still do not reference the physics assembly, native
profiles remain physically unqualified and CW-08 remains the runtime fallback.
See [evidence](evidence/CW-10d.md) for actual native/synthetic results and limits.
