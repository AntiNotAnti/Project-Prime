# PR-14: Ion Fracture (Project Prime exclusive Imperialist FX)

## Intent and artistic direction

Implement a **new**, recognizable Project Prime Insta-Gib signature instead of copying Fruity Prime's Quake-style rail/double helix.

**Four beats:** (1) razor-thin cold-white core inside a saturated slot-colored shaft, (2) an **X-shaped traveling phase knot** that surges down the beam in 0.19s, (3) three angular **shear fins** expanding from the shot axis, (4) three brief prismatic impact flashes on the shooter's side of the actual collision point. No spiral. The silhouette remains readable in duels without covering the first-person reticle.

This is an original rendering implementation inspired by the broad idea of a persistent rail. The specific geometry, colors, timings and trails are Project Prime's own.

## Integration

- `BeamProjectileEntity.SpawnSniperBeam(collisionPosition)` routes **only** multiplayer Insta-Gib player-owned Imperialist impacts through the new presentation path. Other modes keep the original beam entity.
- The _scene_ lazily owns `InstagibIonFracture` only after its first eligible client shot. It keeps at most **24 rails** and ages out each after **0.82s**; old scenes cannot retain global effect state. Headless dedicated servers never construct the pool.
- A non-zero `ModShotId` + launch frame + shooter slot prevents duplicate rails from repeated accepted/replica presentation. Legacy/unkeyed fallback deduplicates same-frame identical traces; collisions do not change.
- Existing `Renderer.GetDrawItems` submits the world-space geometry as untextured pooled `TrailMulti` ribbons, only if the pool already exists. There is no shader backend, new packet, shared model mutation, gameplay RNG, or effect material change.
- Slot identity uses eight bright but distinct colors. Shared inner white core keeps the beam readable.
- Existing `HudProfiles.Runtime.ReduceMotion` disables fin/impact motion but leaves the readable rail intact. `ReduceTransparency` removes broad atmospheric glow.
- The effect ends at the collision result supplied by the original projectile logic. Color geometry is submitted into the existing transparent world pass; it does **not** opt into through-wall rendering.
- Scene-time rewind clears the pool to prevent remnants after replay seek. No gameplay or replay serialization changes.

## Run asset-free checks

```bash
dotnet run --project tools/nettest/nettest.csproj -c Release -- --instagib-ion-fracture
dotnet build src/MphRead.sln -c Release
tools/check-opengl-only.sh
```

These commands are **NOT RUN** by the current tool session. A native OpenGL/GLES runtime acceptance is **required**, especially for world occlusion, 1v1/8p spam and scene/replay lifetime.

## Before marking ready

- [ ] Windows and Linux OpenGL visual screenshots: slot colors, tapered core, angular fins, impact flash (no Fruity helix)
- [ ] Empty solo world and live bot world: no through-wall effect or solo lighting regression
- [ ] Android GLES shader/translucency and thermal/performance evidence
- [ ] Local, remote high-latency and replay-authoritative shot: exactly one rail per accepted shot
- [ ] Collision angle, long/short trace, player hits, wall hits and narrow corridors
- [ ] Native, Balanced and Adventure Imperialist look unchanged
- [ ] P95 frametime and heap/native allocations compared with baseline
- [ ] Optional further cosmetic enhancement: scoped gun/muzzle accent through per-instance uniforms rather than shared model materials (not implemented in this slice)
- [ ] Full CI evidence, server startup and historical replay remains green

## Explicit non-goals

No damage, loadout, ammo, weapon pickup, server, protocol, replay format or C++ runtime backports. `instagib-ion-fracture` CLI runs value checks only, not a GPU screenshot or native match.
