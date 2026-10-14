# GL-03 procedural consolidation disposition

**Proposed strict adjacent merge: NOT_ELIGIBLE.** Current native eligibility
and source barriers do not justify a new queue or draw-merging rewrite. This is
a measured negative target-selection slice, not a rendering optimization or a
claim that no other workload could benefit. Existing defaults are preserved.

## Inspectable evidence

The sanitized [receipt](gl03-procedural-disposition.json) binds the full corrected
GL-01 source log and validated review by SHA-256, plus the actual loaded client
MVID/hash and native fixture. It omits user paths, machine name, PID and assets.
All raw windows remain in the external native evidence bundle.

Selection uses `draw_attribution.measured_stdout`: exclude windows preceding
the simulation measurement-start marker, then discard **four** completed
post-marker windows. The selected 34 complete 240-frame windows represent 8,160
presented frames. Counter totals recover each window's integer count as
`round(average * 240)` and sum those counts; median window averages are separate.
The corrected log SHA-256 is
`cbfa659b964f7608b291aab05deed5d9320159fb78e4897325883740ce0ecaf2`.

| Observed work | Count across selected frames | Median window average/frame |
| --- | ---: | ---: |
| Procedural item submissions | 2,548,029 | 306.55 |
| Already reused material preparation | 276,014 | 29.30625 |
| Already reused exact object preparation | 5,510 | 0 |

Only **0.216%** of procedural item submissions satisfy the existing exact
adjacent shared/object-state reuse proof, even before restricting stencil,
primitive topology, pass and inherited attributes. Some windows have positive
reuse (maximum window mean 6.379/frame); the median is zero. This is an upper
bound on candidate *item pairs* under the proposed unchanged-state merge, not
a bound on CPU/GPU time or driver draw-call savings. Pair-specific cost and a
complete per-pass/type/stencil eligibility witness have not been captured.

Particle geometry median window CPU wall p95 is 0.4902015 ms. Those inclusive
scopes can contain driver/backpressure; high draw frequency and that wall time
do not establish GPU execution cost or an eligible merge. GPU timing remains
UNVERIFIED. Existing preparation reuse is already saved work, not a new gain.

## Source barriers

- `ProceduralStateReuse.CanReuseObjectState` requires adjacent exact shared
  material/raster state plus zero palettes, unweighted geometry, no billboard
  or viewmodel, and identical transform, diffuse and both lights. These existing
  proof conditions produce the small numerator above.
- `RetainedRenderGraph.DrawRenderGraphPackets` sets each item's stencil function
  and polygon reference in MarkTranslucent, TranslucentBehind and TranslucentFront.
  Apparent material equality does not permit merging different references or
  crossing those pass barriers. Opaque and RebuildDepth also execute procedural
  alpha fragments; they are separate passes rather than a single free merge run.
- `GraphicsApi.TryDrawProceduralImmediate` preserves native Quads and QuadStrip
  topology. Joining independent QuadStrips into one strip can add connecting
  triangles. Native UV/vertex order and final inherited attributes must survive.
- The default-enabled `PROCEDURAL_BATCH` already reuses exact adjacent preparation,
  bypasses per-vertex list-capture hooks on the direct immediate path, and emits
  each TrailStack's ordered independent quads within one Begin/End. Reimplementing
  those reductions would repeat completed work.
- `ProceduralFrameGeometry` already packs bounded immutable points once per frame
  for the opt-in desktop stream. `DesktopProceduralStream` already has three
  orphaned stores, validated VAOs, finite capacity and original fallback. A
  second queue or duplicate packing layer has no supported measured target here.

The existing unchanged stream pilot is already unfavorable: about 422→407 FPS
unprofiled and world CPU p99 1.465→1.548 ms profiled, recorded in
`docs/performance/opengl-procedural-stream-pilot.json` and the modernization
results document. That historical result is not generalized to every GPU, but
it supplies no reason to repeat the unchanged experiment or enable it now.
The current default stream-off capture has zero buffer uploads/orphans/bytes;
those zeros are not a new streaming measurement.

## Disposition and future gate

No production flag, queue, vertex format, order, batching rule or default changed.
`PROCEDURAL_BATCH` stays enabled; `PROCEDURAL_STREAM` stays opt-in. There is no
new model display-list/VBO promotion experiment.

A future proposal needs a positive bounded same-pass eligible-run denominator,
explicit topology/stencil/state rejection reasons and immutable order witnesses,
then exact native color/depth/stencil parity and matched end-to-end performance
at the affected source. Unsupported GPU/device results remain UNVERIFIED.
The current receipt provides target-selection evidence only; it does not assert
that the full GL-03 workload or device matrix is accepted.

The existing 22-case modernization analyzer suite verifies count conservation
and receipt handling; no redundant tests of arithmetic in this document or
unchanged renderer were added. GL-04 policy selection must use validated actual
loaded-binary native results, excluding captures that resolved the wrong target.
