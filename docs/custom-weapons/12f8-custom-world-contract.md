# CW-12f8 — detached custom world capsule contract

This describes the source contract in `ReplayWorldCheckpoint.cs`,
`ReplayWorldCheckpointLayout.cs`, `ReplayWorldCheckpoint.CustomWeapons.cs`
and the format7 metadata/checkpoint paths. Local detached-codec qualification is recorded in
[CW-12f8 evidence](evidence/CW-12f8.md). It extends the
[detached archive contract](12f7-detached-custom-archive.md); actual custom Scene capture/restore qualification remains open.

## Explicit world12 decoding

`FromCustomBytes(bytes, producerBuild)` accepts only explicit world version12.
Native `FromBytes` accepts historical native capsules through version11 and
refuses version12; the custom entry point likewise refuses native versions.
Both preserve the overall world byte budget and saved-layout contract check.
Decode retains its payload only after complete body validation; failures release
the temporary payload.

World12 declares appendix flags 15: NativeTouch (1), Pickups (2), Grapple (4)
and CustomWeapons (8). Both initial construction and current decoder must be
explicit replica15 values with complete healthy custom state and byte-identical
CWRD2 descriptors. The current outer recording frame equals the world header
frame. Initial frame/sequence/world cannot lie ahead of current state; both
Match values must agree with the world room and mode. Replica admission still
enforces the exact custom match/epoch and descriptor scope. When initial and
current custom sequences are equal, their canonical embedded CWRS bytes must
match exactly, even if the native outer recording frame has advanced. Equal
sequence cannot disguise a different accepted custom frontier.

The existing saved graph and native appendices remain structurally validated.
A final length-delimited 72-byte custom appendix contains:

| Offset | Value |
| --- | --- |
| 0 | `CWW1` magic, four bytes |
| 4 | Little-endian ushort version 1 |
| 6 | Two zero reserved bytes |
| 8 | SHA-256 of exact current descriptor bytes, 32 bytes |
| 40 | SHA-256 of canonical current CWRS bytes, 32 bytes |

Decode recomputes the whole appendix from the validated current decoder and
requires exact bytes. These hashes bind consistency; they do not authenticate
an authority or prove historical package/resource availability. Native world11
emission and default refusal boundaries remain unchanged.

## Current decoder and archive relationship

`DecoderState()` returns the capsule's actual current replica checkpoint;
`ConstructionState()` preserves its initial construction separately. For a
custom capsule, `ClipMetadata` emits format7 with CWRD2 and a bootstrap made
from **current** decoder bytes at the world origin, rather than relabelling
the initial construction as current. This is pure metadata/codec work without
Scene or resource access.

An optional initial world12 in format7 metadata must match the descriptor,
room, mode, map hash, timed-shot capability and origin frame. Its current
replica bytes must equal the archive bootstrap exactly. Optional indexed world
checkpoints use the explicit custom parser and preserve the same archive scope;
their header frame must equal origin plus indexed relative frame. Custom
sequence/world frontiers cannot regress behind the archive construction. When
an optional world and archive bootstrap have equal custom sequence, their
canonical embedded CWRS bytes must also match exactly; native outer frames
may advance independently.
The writer constructor encodes and decodes the validated header, retaining its
own decoded metadata for later optional-checkpoint checks. Caller reuse or
zeroing of bootstrap/world buffers after construction cannot change the
contract already committed to the file.
Reader and writer retain bounded checkpoint lengths/storage and explicit
format7 capability checks; native containers do not silently adopt world12.

## Ownership and proof limits

The capsule can validate a detached graph/decoder contract and current-state
metadata without proving that a live custom Scene produced it. Scene capture
still throws when custom state is present; Scene restore still throws for a
custom capsule, requiring a retained historical resource and presentation
contract. There is no package lease owner, model/audio restoration, simulation
resumption, grant, damage payment or playback gate opening in this slice.

Integrated GL and cold archive regressions may exercise accepted custom facts
and detached replica/archive reconstruction; they do not exercise world12 Scene
capture/restore. Actual Scene fidelity, genuine custom archive playback/seeks,
two-client accepted contacts and bots remain subsequent work. Android, CI and
release/performance qualification are not claimed by this source document.
