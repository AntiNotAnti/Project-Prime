# CW-10e — experimental accepted-life physical bodies

The desktop and Android heads now reference the managed corpse backend. The
dedicated-server build excludes both the dependency and its implementation.
`PROJECT_PRIME_RAGDOLL_PHYSICS=1` is an experimental startup opt-in; its default
is off. Cosmetic quality remains the existing live budget/disable control.
Weapon acquisition remains world pickup. No equipment or physics menu is added.

The existing accepted head-death track authenticates actor reference, scene,
match, authority epoch, slot generation, life, timeline and event. Only an active
Classic body on a native, unfrozen biped can attempt capture. Native replacements,
Spire's unweighted head, nonzero death choices, replay/killcam/private replicas,
side/editor/thumbnail scenes and unsupported poses retain CW-08 presentation.
Each cause has one attempt; a failed or disabled presentation never starts later
when settings change. The original track owns age, fade and retirement.

Drawing copies both actual native LODs and the selected composed pose inside the
existing collapse scope. The other LOD is composed once with identical split,
crouch and anchored-root inputs, with all model controls and palettes restored.
Independent CPU instances supply neutral calibration. No gameplay attachment,
muzzle, RNG, health or collision state supplies a cosmetic correction.

Capture queues data only. The next consecutive completed 60 Hz presentation step
creates a private fifteen-part world. Later fixed steps advance it; repeated
draws only sample. The accepted direction supplies the bounded impulse and seed.
Optional impact and inherited velocity remain absent rather than inferred from
mutable gameplay. Each LOD has a separate full-affine binding and reusable palette
lease. Ordinary render submission copies its matrices before exact restoration.

Static geometry is copied from native stock-room runtime collision tables into
the existing bounded polygon builder. It applies chunk translation once, retains
player-blocking/beam-transparent surfaces, and rejects the whole candidate on
invalid nearby geometry or limits. Capture tries extents4,3,2 in that order;
rotated body bounding spheres must remain entirely within the accepted region.
Any active nonplayer entity mesh in the room refuses physical capture. Nearby
closed doors and active force fields also refuse it. This conservative policy
avoids pretending moving barriers have qualified swept collision history.

Every step and draw checks room/chunk identity, activity, connector, bit-exact
translation and immutable table identity/counts. Editor selections, transformed
rooms, transitions and changed manual door/field transforms invalidate the
snapshot. These visual worlds never enter gameplay collision or hit queries.
Native runtime tables are immutable after loading; arbitrary mutable custom-map
tables are excluded rather than assumed to have a revision counter.

Scene-owned slots enforce the existing0/2/4/8 quality caps,120 aggregate bodies,
512 additional contact constraints and32 MiB reserved-pool tripwire. These are
post-operation retirement checks, not hard peak-memory guarantees. Each corpse
owns a private backend; this first integration does not pool worlds across deaths.
Capture, calibration and world construction allocate bounded setup data. Sampling
does not imply the whole renderer is allocation-free.

Life changes, teleports, source changes, quality/feature fences and failures
retire the world. Normal `Scene.DoCleanup` releases CPU pools before other
cleanup. Android's original render-thread `finally` also releases them when EGL
cleanup cannot proceed. Ownership stays on that original thread; another thread
cannot dispose or sample the world. No background worker or GL context owns a
physics allocation.

All native profiles remain physically unqualified for general release. Current
experiments cover six native bodies and a deliberate Spire fallback. Primitive
dimensions/joints are approximations, motion is local cosmetic physics, and HD,
physical Android devices, achieved refresh rates, WAN rendering and physical
replay remain release gates. Replay and catalog effects continue to use their
existing presentation clocks. Leaving the opt-in unset is the rollback; it
changes no save, inventory, packet or historical replay format.

See [measured evidence](evidence/CW-10e.md). CI/review is neither awaited nor
reported as a local pass under the user's merge instruction.
