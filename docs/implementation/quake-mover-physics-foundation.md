# Q3 moving brush physics foundation

This PR adds real 60 Hz rigid mover reference transforms, contact of biped/alt-form feet, passenger carry and swept-AABB moving-body broadphase. All functions are deterministic, side-effect-free and fail closed on invalid geometry and unsupported rotating collision narrow-phase. The existing `nettest --q3-mover-source` exercises contact, non-contact, tunneling protection, rotation, bad values and late-join/replay frame reconstruction.

It is **not yet an in-game moving platform**: imported Quake BSP submodel mesh and per-brush collision are currently dropped by Q3Import, and no authoritative entity exists to own movement and serialized activation. Applying carry to players without a corresponding solid collider would cause airborne or desynchronized players; this kernel is deliberately not wired to PlayerEntity or combat rewind yet.

Required next implementation: compile BSP submodel brush meshes separately, bind native EntityCollision per brush, register collision geometry with NetDynamicGeometryHistory, author deterministic activation events with authority owner, apply carrier deltas only after real native swept contact, and include mover lifecycle in world/replay snapshots. Do not enable incomplete movers or change wire format silently.

Performance evidence: NOT_RUN on physical hardware. No licensed game/Quake assets included.
