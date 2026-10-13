# GL-02 opt-in palette preparation prototype

`PROJECT_PRIME_GL_PALETTE_PREP=1` enables a narrow collection-time experiment.
It defaults **OFF** and forced legacy disables it. There is no frame-time gain
or rollout claim; measured collection headroom is about 0.018 ms at p95 in the
current eight-player High-shadow fixture.

## Native eligibility evidence

GL-01's corrected 25-second native eight-player/High/1920×1080 capture loaded
the intended diagnostic DLL, SHA-256
`290e6a6f2719a39017e5e7443c0a00a16b15061bc50ff0effb6abb01fc1417b2`.
`draw_attribution.py` excludes all windows before the simulation
`measurement-start` marker, then explicitly discards **four** completed
post-marker windows. This convention was selected before candidate A/B runs.
There are 34 retained windows. Their median window observations are:

| Observation | Per frame / median window |
| --- | --- |
| Palette collection requests | 49.048 |
| Copied palette floats | 9,280.4 |
| Consecutive known repeat requests | 37.9 |
| Repeated copied floats | 7,453.8 |
| Collection CPU wall p95 | 0.018333 ms |

About 80.3% of copied floats repeat a known source/revision/count/scale. These
are real operations, but their tiny absolute CPU budget does not establish a
worthwhile end-to-end frame improvement. Inclusive geometry/setup wall scopes
can include driver backpressure; they do not isolate GPU execution. GPU timing
in this capture is UNVERIFIED.

The first attempted capture resolved a symlinked harness to the baseline DLL
(MVID prefix `3bfa`), so it was excluded from target evidence. Its raw log/JSON
remain retained. The first provisional 41-window analysis used only profiler
warmup flags and also remained ineligible; the corrected 34-window report uses
the marker plus four-window convention above. No raw windows were deleted.
The receipt names are `gl01-eight-high-corrected.log`,
`gl01-four-window-attribution.json`, and the excluded original
`gl01-eight-high.log` in the parent `work/gl-evidence` bundle.

## Implementation and ownership

The scene owns one lazily created cache with exactly one 512-float scratch.
Only a positive existing native revision, identical source object, matrix
count, and **bitwise** scale identity permit reuse. Distinct sources, updated
revisions/counts, near-equal scales, and positive/negative zero do not alias.
Unknown/unversioned or partial/unsupported inputs keep the original indexed
scaled copy. A miss computes the exact existing factors; a hit avoids source
reads/multiplications. `Array.Copy` writes every render item's separate existing
snapshot. No item receives or exposes the scratch array.

The cache is active only while the scene collects this picture's draw items.
Collection begin and the `finally` end clear its source identity. New collection
therefore cannot borrow pre-seek/replay state. World restore clears it explicitly;
scene unload clears it and releases the scratch owner before the headless early
return. There are no static source owners, cross-scene caches, GL resources,
vertex-format changes, different calculation clocks, reordered draws, shared
item arrays, or changes to program-specific palette upload semantics. The new
private preparation fields are outside the explicit replay checkpoint schema;
its versions and contract fields remain unchanged.

The revision contract matters: an arbitrary mutation of a *known* source while
lying about its revision is not independently detected. Native model writes and
replay restore already use the existing versioned write boundary. This prototype
does not hash mutable arrays or claim to detect an unversioned write into a
falsely versioned source. Unversioned inputs always fall back and see current
values.

`PaletteSourceFloatsScaled` measures actual dynamic source multiplications in
both arms. `PalettePreparationHits`, `PalettePreparationMisses`,
`PalettePreparationFallbacks`, and `PalettePreparationSnapshotCopyFloats` expose
the opt-in path. Existing copied-float counters remain truthful: every item's
snapshot is still copied. Shader matrix-array upload counts/bytes should remain
unchanged for identical native frames. `palettePrep` is an explicit context
flag for same-binary A/B analysis.

## Verification and remaining gates

The existing device-free `-rendergraphcheck` hook runs 16 focused cases covering
exact floats, equal/near-equal identity, signed zero, independent snapshots,
unknown mutation fallback, bounded/partial inputs, collection reset, and source
release. Existing CI invokes this hook in the Linux OpenGL job; no new command
flag is required. `-renderbackendcheck` continues to run the GL-01 scalar checks
in existing build/release jobs. Default, opt-in and forced-legacy graph checks,
backend checks and the 22-test analyzer suite pass on the local .NET runtime.

An optional device-free microbenchmark runs with
`PROJECT_PRIME_GL_PALETTE_PREP_BENCH=1 -rendergraphcheck`. It executes the real
original scaling loop and candidate arithmetic/copy with 256 floats and eight
items per collection, five alternating warmed runs per arm. Initial median
observations were 347.2 ns/item original and 61.2 ns/item candidate, with zero
allocations and equal checksums. All individual observations are retained.
This synthetic grouping is neither native asset evidence nor rendering/rollout
evidence; it cannot turn the 0.018-ms headroom into a frame-rate claim.

Native matched off/on color/depth/stencil captures, source operation counts,
five interleaved/reverse unprofiled runs per arm, lifecycle/replay/editor coverage,
and Windows/Linux/physical Android gates remain open until recorded by the
native campaign. Missing GPU/device results remain UNVERIFIED. The setting
stays off unless the plan's fidelity and measured end-to-end gates support it.
