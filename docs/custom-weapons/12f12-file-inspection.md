# CW-12f12 — explicit historical file Scene inspection

This connects [world12 Scene restore](12f11-world-scene.md) to a retained local
format7 reader. Local validation is recorded in [the evidence](evidence/CW-12f12.md).
Ordinary custom file Join, recording, killcam and export admission remain closed.

`PreparedCustomReplayArchive.Prepare` runs before Scene construction, on a worker.
It requires exact protocol83/format7 metadata, a world12 baseline and zero lead-in.
It validates the header/current decoder relationship, target range and nearest
indexed world at or before the requested absolute recording frame. Only the
selected optional checkpoint is decoded. A reconstruction gap exceeding36,000
frames refuses with a request for a nearer checkpoint; this bounds synchronous
inspection reconstruction rather than claiming unrestricted theatre seeking.
Copying the validated world preserves the producer build ID needed by accepted
version-dependent historical layout contracts.

The baseline is a complete recording frame. Preparation checks every initial
relative-frame0 record with cancellation and a count bound. Packet suffixes at
frame0 refuse; only a one-byte end marker in a zero-duration archive is allowed.
This deliberately supports a narrower Scene subset than detached format7 analysis,
which still retains its existing frame0 semantics. Missing exact historical bytes
refuse; there is no download, substitution, installation, grant or equipment change.

Preparation owns the reader's read-only file handle, selected immutable world
and prewarmed historical resources. Input transfers exactly once to a session;
disposing preparation then releases only its own world/resource owners. The
Scene retains independent package leases, and the session owns its reader until
Stop. Failed construction disposes unpublished ownership.

`OpenCustomArchiveInspection` creates a fresh resource-owned Scene from the chosen
world, attaches the retained reader after that complete frame, and reconstructs
through the requested target. Explicit file stepping applies accepted packet bytes
on their absolute recorded frames. Visible file time remains relative to the
original archive origin. Each frame allows at most4,096 records/1MiB, rejects
construction replacement and undeclared timed facts, and requires end markers
to close the declared final frame. Corruption retires the private session.

Generic native PumpFrame/Reposition cannot enter this path: native rewind would
destroy the custom owner and use a different clock convention. Seek instead
prepares a fresh reader/world/Scene with independent resources. EOF is inert.
No live input, readiness, socket, damage authority or native gameplay grant is
created. The silent passive presentation adapter remains in use.

File Scene reconstruction does not by itself establish native indexed-fire
presentation, first-person optics/audio, accepted two-client contacts/head-deaths,
bot behavior, ordinary recording or production theatre/killcam admission. Those
remain separate CW-12 acceptance boundaries.
Canonical file reconstruction is qualified on undrawn owners, with separate
picture owners for rendered captures. Native PlayerDraw changes tracked LOD/
third-person flags; draw-then-suffix invariance is an explicit unresolved gate.
The comparisons retain those flags rather than masking the observed difference.
