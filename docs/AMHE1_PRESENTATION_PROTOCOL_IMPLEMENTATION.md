# AMHE1 presentation and input implementation boundary

This document records the implementation boundary for the fixed-step
presentation/input pass. It is an engineering note, not live client or WAN
acceptance evidence.

## Implemented

- Simulation, biped, model, remote snapshot, and first-person gun node histories
  remain owned by the existing presentation path. Gun history captures copied
  node/stack poses and uses a discontinuity key covering weapon, animation,
  model/form/life, presentation epoch, timing, correction, and scene barriers.
  The source model, muzzle, aim, and projectile origins are never rewritten.
- Camera position and FOV are captured as presentation state. FOV uses a typed
  scalar history and resets at camera discontinuities. The existing advanced
  NetworkHealth HUD is the single development diagnostics overlay; its existing
  `AdvancedNetwork` setting controls the additional frame, GC, look-device, and
  processed-stick readouts. It reports measured render/simulation Hz, current
  steps, bounded p50/p95/p99/max CPU samples, GC allocation rate, and generation
  collection counts. The runtime does not expose a portable last-GC timestamp,
  so no unsafe or platform-specific approximation is displayed. F3 remains
  available to spectator controls.
- Shock Coil enters its authored Shot animation once while held and alive with
  ammunition, advances through the normal 30 Hz model update, and exits cleanly
  on release, weapon change, ammunition exhaustion, death, or morph. Texture,
  material, and texcoord animation are left on the existing model update path.
- Movement keeps the existing digital binds and hysteresis. A controller's
  post-deadzone radial vector/magnitude is optional state, quantized to -127..127
  on protocol 15, and rejects -128 or invalid radial values. Local capture
  carries pre-controller digital movement/roll provenance, so the server does
  not apply controller-derived digital direction a second time. WithoutEdges
  retains held axes; Neutral clears them. Protocol-14 replay timelines remain
  readable where their stored timeline format does not contain input-command
  payloads.
- Existing weapon selection, pickup, damage, hit feedback, and prediction paths
  were kept intact. No balance changes or renderer-backend changes are part of
  this pass. Collision allocation optimization remains unapplied pending a
  repeatable benchmark showing meaningful recurring allocations.

## Intentional live/follow-up boundary

The final camera orientation is intentionally the newest simulation orientation.
Consumed gameplay aim/current orientation cannot be interpolated without either
delaying input or decomposing CameraInfo into renderer-owned base orientation,
view-bob/landing/shake, and input orientation. The current source exposes one
composed view matrix, so the implementation interpolates only the captured
camera position (which carries existing bob/landing translation) and FOV, then
applies pending render look once to a camera copy. A future live sequence should
introduce a typed base/effect decomposition before orientation smoothing is
enabled; no approximation is promoted here.

`SmoothCamSeqHandoff` remains default-off. No live camera-sequence handoff
evidence was available to authorize changing that default.

## Evidence boundary

Focused unit tests cover vector finite/fallback behavior, camera obstruction
and collision edge classification, gun/model discontinuities, FOV history,
Shock Coil entry/exit, analog quantization/round-trip/axis resolution, bounded
diagnostic percentiles, and the AdvancedNetwork toggle. Release Game, Renderer,
and Client builds are the relevant static checks. High-refresh controller,
GPU/material animation, Android-device, geographic WAN, and human visual review
remain external acceptance gates.
