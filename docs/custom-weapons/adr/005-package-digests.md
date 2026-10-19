# CW-00 ADR E: canonical mechanics and immutable package bytes

Status: accepted foundation; package publication disabled until CW-11–13 gates.

Use distinct SHA-256 identities: exact archive bytes (Community immutable revision) and canonical
validated mechanics plus behavior/schema version (combat definition). Display text, listing metadata,
ZIP timestamps and compression do not change mechanics. Never place an archive hash inside the
archive it hashes. UUID identifies content lineage; neither UUID nor display name identifies revision.

CW-02 defines strict UTF-8 canonical mechanics property order, invariant numeric encoding and bounds.
Unknown fields/versions/presets and nonfinite/ambiguous values fail closed; published inputs never
silently clamp. v1 starts with an engine-owned precision preset; unsupported behaviors are rejected.

CW-11 starts self-contained with no arbitrary filesystem/URI resolution. Reject traversal, symlinks,
case collisions, executables, scripts/shaders, nested archives, decompression bombs and unsupported
assets under stricter existing Community limits. Validate in private staging; atomic immutable
promotion follows full validation. Servers can validate mechanics without graphics/audio allocation.
Dependencies, if enabled later, require exact hashes, acyclic bounded graph and trusted service only.

CW-13 reuses creator ownership/catalog/revisions/privacy/moderation, adding a typed weapon validator;
generic model validation is insufficient. No historical ppmap hashes or v2 projections change.
Rollback stops new publication/admission; never delete pinned historical revisions.
