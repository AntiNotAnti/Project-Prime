# World animation blending: PR-05 incremental rollout

## Implemented

The only production draw path modified is `EntityBase.GetDrawInfo`, and only when the instance `Type == EntityType.Object`. The dedicated `PROJECT_PRIME_WORLD_ANIMATION_BLEND=1` diagnostic flag is disabled by default.

We reuse the canonical animation snapshots from PR-03 and the node curve sampler from PR-04, sample `FrameTiming.TotalSteps + PresentationAlpha`, build the render-item palette, then restore original matrices in `finally`. `Scene.AddRenderItem` snapshots the palette before the restore. The canonical animation frames/flags and server simulation are untouched.

## Deliberately not enabled or implemented yet

* `PlayerDraw` uses a two-part biped pose (spine split), model-specific cannon muzzle and effects, distinct alt-form branches (including Kanden segments and Spire spike). Naively blending that path would change draw-time muzzle/effect positions and may corrupt authoritative shot/visual parity. Keep excluded.
* Many enemy and boss draw overrides compute simulation data from their nodes (notably Alimbic Turret and Gorea) and are excluded. Platforms/doors and their collision attachments are excluded.
* No blanket `UpdateTransforms` or `GetDrawItems` hook is added because doing so would blend non-visual state and affect every derived entity.
* No new frame limiter, protocol, replay format or GL backend.
* No claims of native game acceptance or performance improvement.

## Acceptance needed before expanding

1. Asset-free animation math parity: existing `nettest --animation-sampling`.
2. Inspect Object entities with model-driven collision or scene side effects and exclude them individually if found.
3. Native 60/120/144/240 Hz side-by-side object animation captures, with opt-in OFF/ON.
4. Simulation/replay pose-hash and muzzle-position parity with 2–8 players, bots, and solo.
5. All seven Hunters, alt-form edge cases and bosses must be separately adapted with draw-only data, not this generic pilot.
6. Collect four 240-frame A/B windows and check p95/p99/allocations on real OpenGL/GLES hardware.

This PR is an intentionally **partial PR-05** that should remain draft until native pilot verification and separate player/boss integrations are complete. A conservative off-by-default path is preferred to globally enabling unsafe interpolation.
