# CW-12f13 — private replay draw-state isolation

Base: `9e8e514e3` (integrates the unrelated macOS packaging PR987; compiled core unchanged from `af0bb1d4f982057543e06a8bed7a0ca48dbcb25b`).

`PlayerEntity.Draw` preserves the existing ordinary/live path. Private replicas
call the same draw body inside a `try/finally` that restores exactly the native
`Lod1` / `DrawnThirdPerson` bits, both original biped model LOD references, and the 19-entry biped ice transform array.
Consumers inside the draw still observe its selected LOD and skeleton. These
values are serialized and consumed by native thaw presentation, so leaving them
changed makes checkpoint state depend on camera and render cadence.

Charge/muzzle placement and visibility use per-picture effect overrides; element flags and simulation transforms stay canonical. Override registration is shared with the existing transform reset list and clears on the next picture or explicit cleanup. The renderer consumes visibility overrides only during submission. These new properties are presentation state outside the explicit checkpoint field contract.

Particle submission also saves the existing value-only draw cache before its camera-dependent canonical billboard calculation and restores it after render items have copied their geometry. Ordinary live particle cache behavior stays unchanged. This uses a local value snapshot rather than a second persistent cache on every particle.

The scope uses bounded stack storage; it does not clone the Scene or invoke
simulation. No checkpoint fields, comparison exclusions, wire identities or
world versions change. Accepted gameplay facts and exact package ownership
remain unchanged. Ordinary gameplay draw semantics remain unchanged.

Validation must exercise repeated drawing and then genuine accepted suffixes,
strict whole-world comparison, camera/LOD variations and separately identified
adversarial frozen/hidden/spectating presentation fixtures. Synthetic fixtures
must never be described as recorded authority freeze or combat acceptance.
No other replay admission gate opens as a side effect of this change.

Local results are recorded in [CW-12f13 evidence](evidence/CW-12f13.md) after
observed completion. Android remains explicitly deferred.