# CW-12f4 — durable detached custom replay state

This is the source contract for `ReplayCustomWeaponDescriptor` and the byte
checkpoint methods on `ReplayCustomWeaponState`. Local qualification and cold
detached-state reopen evidence are recorded in [CW-12f4](evidence/CW-12f4.md). These file-only formats do not enable custom
Demo recording, native checkpoint blocks, passive Scene presentation or archive
reopening/seeks. The [typed passive-state contract](12f3-passive-replay-state.md)
continues to define acceptance and simulation boundaries.

## Historical descriptor: CWRD1

`CopyBytes()` returns independent bytes; `TryRead` accepts at most 20 KiB and
requires exact length and canonical re-encoding. Integers are little-endian;
the map GUID uses big-endian GUID bytes.

| Field | Bytes / constraint |
| --- | --- |
| Magic, version, reserved flags | `CWRD`, ushort 1, ushort 0 |
| Embedded bootstrap length, room-name byte length | Two ushorts; total header 12 bytes |
| Exact historical bootstrap | Length-delimited CWRB bytes, validated independently |
| Map identity | Custom flag 1 byte, GUID 16, content hash 32, archive hash 32 |
| Room key | Strict UTF-8, 1–320 bytes; at most 80 characters, no controls or whitespace-only value |
| Placement count | Ushort, 1–64 |
| Placement rows | 28 bytes each: spawn ushort, runtime ushort, X/Y/Z int32, initial delay uint32, respawn uint32, reserve refill int32 |

Rows require increasing nonzero spawn IDs, runtime 10, coordinates within
±8192 units at 4096 quantization, bounded delays, positive respawn and
nonnegative refill. Native map identities require zero GUID/hashes; custom
identities require nonzero GUID/hashes. Reserved fields and trailing bytes fail.

`Bind(exactDefinition)` reconstructs the recorded authored pickup layout and
requires exact bootstrap mechanics/layout context digests. It does not look up
today's stock layout. The caller resolves the embedded package manifest through
`PrepareHistorical`: exact retained archive bytes, all pinned identities,
independent prepared CPU resources and owned store leases. An absent installed
marker is permitted; missing or mismatched historical bytes fail closed. There
is no install, download, fallback definition, weapon grant or equip operation.
The binding must remain alive for its resources; the descriptor owns no lease.

## Passive checkpoint: CWRS1

`CaptureCheckpointBytes()` requires complete healthy state. The maximum is
768 KiB, including a 32-byte SHA-256 trailer over all preceding bytes. Binary
integers are little-endian. The ordered body contains:

- `CWRS`, ushort version 1, ushort zero flags, exact context digest (32 bytes).
- World, recording frame and state-table world (three uint32), sequence uint64.
- Eight frozen 41-byte slot rows; byte pickup count equal to the authored count.
- Each pickup's world uint32, incarnation uint64, ready tick uint32,
  availability byte and revision uint64, in authored placement order.
- Ushort flight count, at most 256; increasing ushort storage index, anchor
  world uint32 and frozen 219-byte flight row for each active anchor.
- Ushort action count, at most 1,024; increasing ushort storage index, presence
  flags, touched world and launch/contact recording frames. Optional baseline
  launch uses the frozen 171-byte row; retained launch/contact facts use ushort
  lengths followed by their exact semantic fact bytes.
- Ushort retry count equal to min(sequence, 1,024), then contiguous sequence
  uint64/digest32 pairs in sequence order. Modulo positions are reconstructed.

Decode validates clocks, complete pickups, slot ownership, unexpired original
flight anchors and matching retained action sources. Terminal contacts cannot
coexist with active flights. Retained facts have sequence at least 3, at most
the checkpoint sequence, and cannot reuse a sequence across retained facts.
Facts still inside the retry window must hash to that window's digest; a
retained bootstrap digest must match the exact context bootstrap. Sequence must
cover bootstrap, baseline, every initial pickup and all retained action facts:
at least `2 + layout.Count + retainedFactCount`.

`RestoreCheckpointBytes(bytes, context)` decodes into a fresh temporary owner,
publishing it only after all semantic checks, exact consumption and canonical
byte re-encoding pass. Failure exposes no partial owner and mutates no existing
state. SHA detects corruption; it is not authentication or proof that a trusted
authority produced the facts. These checks cannot reconstruct discarded history
or prove every absent event occurred. Callers must provide the exact context
and retain the original source/log/assets; this format authorizes no cleanup.

Replaying an accepted suffix into a restored owner remains passive: no input,
collision, damage payment, inventory funding, RNG, sound or graphics simulation.
Detached byte parity and a diagnostic file reopen are narrower than a genuine
Demo archive record-close-reopen/seek contract. Native replay versions and
custom recording/playback refusal gates remain unchanged in this slice.
