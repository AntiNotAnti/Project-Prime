# GL-01 palette preparation and uniform work attribution

This diagnostic slice closes collection and upload attribution gaps before
choosing another renderer optimization. It changes no shader, vertex format,
matrix value, palette snapshot, ordering, damage, wire or replay contract. It
uses the existing opt-in `-glprofile` / `PROJECT_PRIME_GL_PROFILE=1` frame window.
No new optimization or default-enabled policy is included.

## Collected evidence

`[glpasses-json]` retains schema 1 and adds optional fields/counters:

- `cpu.PaletteCollection` measures the existing source revision/read and
  scaled palette copy in mesh collection, plus the existing retained-room
  revision/copy path. It excludes transform scaling and scale-layout setup
  before that scope. It is inclusive CPU wall time, not isolated GPU execution.
- `PaletteCollectionRequests`, `PaletteCollectionVersionedRequests`,
  `PaletteCollectionCopiedFloats`, and `PaletteCollectionReuses` distinguish
  known revisions, original copies, and already-implemented retained-room reuse.
  Zero-length palettes do not create a positive denominator.
- `PaletteRepeatedPreparations` records consecutive same source object,
  positive revision, palette length and exact scale within one frame.
  `PaletteRepeatedCopiedFloats` records repeated floats actually copied by the
  original path, excluding retained-room reuse. It is a target eligibility
  denominator, not proof of an optimization or source immutability beyond the
  existing revision contract. Unknown/unversioned sources never count as repeats.
  The profiler stores only the last source temporarily and clears it at frame
  begin, completion, cancellation and context reset; it does not keep it in a
  receipt or across presentations.
- `LegacyScalarUniformRequests`, `LegacyScalarUniformSubmissions`, and
  `LegacyScalarUniformSkips` wire the existing desktop backend sample to each
  completed frame. Scalars here include vectors and single matrices. Uncached
  array overloads are excluded so the scalar count does not double-count arrays.
- `uniformAttribution.matrixArraysByProgram` separately partitions matrix4
  array requests, executed facade uploads, `count * 64` payload bytes and palette
  skips. At most 32 program integers are retained per window; `UNKNOWN` and
  `CAPACITY_OVERFLOW` rows preserve work outside that capacity.
- `uniformAttribution.matrixArraysBySource` independently conserves the same
  matrix-array totals by originating CPU pass, item owner, and `Setup`,
  `Geometry` or `Unscoped` origin. Setup scopes now establish their actual owner
  and preserve the parent pass before entering `ItemSetup`; nested scopes
  restore prior attribution. Direct unowned/HUD submissions remain explicit.

Each partition should agree with `UniformArrayUploads`, `UniformArrayBytes`,
and `UniformArraySkips`. Byte values estimate upload payload, not GPU memory
residency. Direct native GL bypasses and implicit GLES emulation uniforms are
outside the facade counts. Negative/invalid matrix counts report zero payload
in the new attribution, while the original GL call/error behavior is preserved.
Such invalid calls cannot form valid performance evidence.

GLES/dedicated-server scalar backend coverage is `UNVERIFIED_PLATFORM`, not a
measured zero. Existing asynchronous GPU timers continue to report unsupported
or missing data as UNVERIFIED. These diagnostics do not query or synchronize GL.

Fixed numeric storage is allocated only after an enabled profiling frame starts.
With profiling disabled, collection/upload hooks return without allocating
diagnostic state, querying GL, taking timestamps, changing a cache, or reading
palette values. The device-free disabled-call microprobe is a warmed synthetic
diagnostic overhead observation; it cannot establish whole-renderer overhead or
an FPS improvement.

## Inspection and verification

`tools/opengl-modernization/draw_attribution.py` accepts historical captures
without the new optional field. When present, it rejects partial windows,
duplicate program/source rows, unknown owners/origins, lost operation/byte
counts, capacity violations, and scalar count conservation failures. Its report
preserves per-window program/source records and median window work counters.

Device-free `-rendergraphcheck` exercises nested setup/geometry restoration,
program/source conservation, unknown/overflow work, cancellation/context reset,
numeric-only upload storage, versioned/repeated palette denominators, source
release, and disabled-path allocation/overhead. `-renderbackendcheck` tests
scalar/uncached-array accounting against the actual existing uniform cache.
The Python analyzer checks malformed and valid receipts.

The full GL-01 plan remains open: quiet-host matched profile captures, five
interleaved/reverse unprofiled runs per arm, exact native world/HUD/depth/stencil
parity, the specified player/shadow/presentation/workload matrix, and Windows,
Linux and physical Android coverage are not established by device-free checks.
Native GPU execution and platform outcomes are UNVERIFIED until captured.
There is no one-frame per-location normalized uniform/value witness in this
slice; aggregate repeat counts must not be presented as that stronger evidence.

The next candidate may eliminate repeated native scaled palette preparation
only if actual positive repeat/copied-float denominators and collection CPU
cost justify it. Native snapshots and fallback remain authoritative, and a
candidate stays default-off until matched fidelity/performance gates support it.
The reverted retained-model VBO experiment is not repeated.
