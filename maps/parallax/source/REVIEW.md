# Graybox v0.1 design review — 2026-09-06

**Disposition:** playable graybox for review. Stop here before v0.2. The source,
thumbnail and distributable bundle exist; automated technical checks pass.
Competitive acceptance and the 28 human matchup categories remain open.

## Scope and evidence

Only `maps/parallax/` and the map index were authored for this task. No engine
changes, commercial Quake assets, cartridge textures, final artwork or extra
weapons were introduced. Extensive concurrent engine/network changes were already
present and continued during this work; none are claimed as part of PARALLAX.

Rendering and map tests used .NET 9 on Linux ARM64 in a disposable container,
Mesa software OpenGL and a copied AMHE1 asset directory. The CPU movement check
also passed on macOS ARM64 using the same managed game assembly. These are local
runtime observations, not Windows, Android, remote-network or human-play evidence.

Tested game assembly SHA-256:
`c3aa7ec1e5c4918838ca6fc2560526ba97760a2998d2a9ba65409e2f605451f6`.
The map does not depend on the optional movement checker or its headless API to play.

## Technical results

| Check | Observed result |
|---|---|
| P0 sealed room | q3map2 → PK3 → texture bake → room generation passed; 2/2 players spawned; both spawn cameras rendered |
| Full BSP | 140 world brushes; 329 imported surfaces; 1,076 rendered triangles |
| Collision | 538 exposed collision faces; 274 buried sides discarded; no grid overflow |
| Extents | 58×17.5×58 including shell; 56-unit playable width; scaleFactor 4 |
| Runtime content | 4 spawns, 2 JSON jump pads, 9 item spawns; 15 entities total |
| Textures | All 10 original temporary materials baked successfully |
| Bot navigation | 194 generated waypoints and 336 links; this alone does not prove good bot tactics |
| Generated sizes | Model 100,090 B; collision 53,420 B; entities 1,540 B; nodes 61,592 B; animation 24 B |
| Final 2-player test | `-players 2 -seconds 60`: exit 0; 2/2 spawned; 2/2 pads activated; no MAPFAIL; 99.5% minimum rendered coverage across 68 samples |
| Spawn render sweep | Four starts and five seconds walking from each: 0 empty views; 99.7–99.9% minimum coverage |
| CPU movement | 42/42 ramp/landing scenarios passed: seven Hunters × two forms × upper ramp/lower ramp/pad |
| Spire wall | Reached the six-unit climb's top height; dismount not certified |
| Thumbnail | `-thumbnail PARALLAX` rendered at 1280×720 and visually inspected |
| Launcher | Existing launcher UI capture ran successfully after providing missing Linux GUI libraries; interactive Parallax selection/normal match launch still needs manual verification |
| Reproducibility | Two q3map2 rebuilds produced identical PK3 SHA-256 |
| Bundle | 26,217 B; carries only recipe, trimmed BSP and texture pack |
| Fresh bundle test | Only the bundle in its map directory, with all PARALLAX binaries removed from the disposable asset copy; generated all five files identically to the development inputs; separate two-player runtime smoke test passed |
| Repository checks | `validate.py`, `tools/check-maps-shipped.sh` and `git diff --check` passed |

The ordinary map test deliberately holds one player as a probe target. Its
`moved 1/2` is not evidence that the other Hunter cannot move; the separate CPU
check exercised every Hunter. Likewise, the render-sweep mode does not populate
the normal tour's movement counters. Do not use those counters as duel results.

## Dedicated design audit

| Area | Confirmed / bounded evidence | Remaining review |
|---|---|---|
| Spawn safety | Exact rotational pairs; blocked spawn-to-spawn rays at three heights; no rays from sampled standing deck/apron positions to spawn eyes; two swept exits per alcove | Live respawn selection, moving/airborne attackers, pursuit and spawn trapping |
| Route timing | Both forms of every Hunter traversed the tested standard ramps; rotational BSP symmetry holds | Spawn→center and spawn→health travel times; pursuit routes; uninterrupted full rotations |
| Upper-deck power | Ramp, pad, main-level climb approach and drop exits; no health; rear screen and flank exposure | Actual central coverage percentage and how often upper control can be recovered |
| Center control | Four cardinal bridge connections, circular ring, accessible center AW, split reactor; 29.7-unit diagonal remains clear | Freeze pressure, splash around the reactor and whether winning AW control snowballs |
| Lower usefulness | Paired health, two ramp exits, two pads, staggered walls, columns and bridge underpasses | Whether the lower loop becomes a recovery chore or permits trap stalemates |
| Hunter interactions | All seven can reach the upper tier through standard geometry in both forms | The 28 matchup categories, turret blind-side attacks, trap bypasses under pressure |
| Item economy | Exactly 1 AW, 2 medium health, 4 small UA, 2 small missiles; all supported, clear and rotationally paired | Resource denial loops and recovery after losing center |
| Alt-form traversal | Tested upper/lower ramps and north pad landings for every form; south geometry is an exact rotation | Boost runs, opposite-direction approaches, edge entries, trick jumps and Spire dismount |
| Collision quality | Clean source brushes; sampled full perimeter supported and clear; no render holes in inspected views | Exhaustive wall/ledge sweeps, projectile behavior at every join, high-speed snag testing |
| Sightlines | Spawn occlusion, explicit axial blockers, clear diagonal and blocked extended diagonal tested against BSP | Exhaustive elevated/oblique long lines; 50–65% upper coverage target |

The static checks use compiled convex BSP planes and conservative swept boxes.
They do not emulate the full capsule/alt-form collision response or opponents.
The runtime checker uses actual game physics and controls, but isolates Hunters
and steers their rolling-camera basis; it does not substitute for human control.

## Measured route segments

Times start after settling/morphing and stop at the specified destination. They
are simulation seconds at 60 Hz, not stopwatch times through a live duel.

| Hunter | Upper ramp biped / alt | Lower exit biped / alt | Pad approach + landing biped / alt |
|---|---|---|---|
| Samus | 2.60 / 2.10 s | 1.85 / 1.40 s | 1.92 / 1.37 s |
| Kanden | 2.60 / 2.07 s | 1.85 / 1.40 s | 1.92 / 1.42 s |
| Trace | 2.60 / 1.85 s | 1.85 / 1.30 s | 1.92 / 1.32 s |
| Sylux | 2.60 / 1.45 s | 1.85 / 1.02 s | 1.92 / 2.20 s |
| Noxus | 2.60 / 2.32 s | 1.85 / 1.40 s | 1.92 / 1.57 s |
| Spire | 2.60 / 1.83 s | 1.85 / 1.32 s | 1.92 / 1.63 s |
| Weavel | 2.60 / 1.90 s | 1.85 / 1.33 s | 1.92 / 1.30 s |

P1 review observations: the direct upper segment is faster than the brief's
3–5-second target; the lower pad shortcut is faster than its 4–6-second route
target. The scripted Spire climb reached top height in 6.18 seconds, so a *speed*
advantage for that shortcut has not been demonstrated. Verify real inputs and
full approach paths before resizing geometry. The Boost advantage is unmeasured.

## Narrow repairs made during the graybox pass

- Added roofed dogleg spawn cover after a sampled deck edge saw a spawn interior.
- Moved a lower column that protruded into one newly widened spawn exit.
- Added axial cover after finding unintended straight views along both main axes.
- Added a main-level approach to the climb wall, making its intended climb six
  units rather than requiring a ten-unit ascent from the basement.
- Set pad control lock to zero after observing ceiling impacts, and added a
  landing apron after observing shorter alt-form arcs falling in front of the
  original deck. Both static clearance and actual landing checks now pass.

## Next experiment / stop condition

Run human 1v1s beginning with Samus mirror, Trace–Spire and Sylux–Weavel. Record
spawn selection/first damage, route times, center item ownership, recoveries from
lower health and deck takeovers. Test Spire dismounts and Boost arcs in both
directions. A repeatable spawn trap is P0 and blocks competitive promotion.

Do not begin art or v0.2 until this review is resolved. Keep the current bundle
labeled Graybox v0.1; automated load success is not competitive certification.

## Artifact identity

- PK3 SHA-256: `18e8eb7808f479a9e433b7b58654fdae78f4ace02d9a1bc7a4c560cc139101be`
- JSON SHA-256: `b1dec8186836f2c49e9d14bef090d43e84b5488ba59288ef639d83cf4a29c925`
- Bundle SHA-256: `e8e8c639c0bce01ef9b9b496331cd285d9a2e4fe13efa559755854433482d045`

Raw local logs and snapshots from this run are under `/tmp/codex-re/parallax/`:
`final-mapgen.log`, `final-static.log`, `final-maptest.log`, `final-spawns.log`,
`final-movement-wrapper.log`, `final-thumbnail.log`, `bundle.log`,
`fresh-mapgen.log`, `fresh-maptest.log`, and `launcher.log`. These are local
evidence, not map inputs or source assets. Use the commands in README to reproduce.
