# CW-04 network contracts and integration gates

CW-04 is split into reviewable changes. CW-04a supplies **dormant payload codecs**
and exact mechanics-manifest reconstruction. It does not complete CW-04 or enable
custom multiplayer. Authority admission, source-time history, indexed payment,
rewind/catch-up, typed results, trade ordering, snapshots, lifecycle integration,
real transport, latency tests and the live protocol bump remain required before
advancing to CW-05.

The inspected CW-04 base is `2f169d9d1e9eb0c1d2cb6edeb04837820a760a01`, with live
protocol **59**. The original ADR's protocol 58 describes CW-00's historical base.
No `PacketType`, Hello, native packet, replay reader, sender or receiver changes in
CW-04a. Payload version 1 is a separate local codec version, not live protocol 1.
Allocate checked live packet IDs and the next live protocol only at integration;
IDs through 82 are already assigned at this base.

## Canonical mechanics manifest

A manifest has 1–64 distinct content GUIDs, with one exact revision per content ID.
Entries sort by canonical persistent identity. Match-local runtime IDs start at 10,
after the nine unchanged native entries. The hash input is exactly:

| Offset | Bytes | Meaning |
| --- | --- | --- |
| 0 | 4 | ASCII `CWM1` |
| 4 | 2 | Mechanics schema, little endian, 1 |
| 6 | 2 | Engine behavior version, little endian, 1 |
| 8 | 2 | Custom row count, little endian |
| 10 onward | 50 each | Runtime ID u16 LE, 16-byte GUID in RFC/network byte order, raw 32-byte mechanics SHA-256 |

SHA-256 covers the complete canonical input. Names, locale, enumeration order and
mutable native tables do not affect this digest. It binds validated mechanics,
including the behavior version. CW-12 must separately version package/assets and
dependencies; this mechanics manifest makes no asset-possession claim.

An invalid digest is all zero. In-memory digests use four integers; action and
readiness readers allocate no strings or arrays. The bounded manifest parser
rejects malformed counts, lengths, native IDs, duplicate/unsorted GUIDs and missing
hashes before creating the detached immutable page.

## Payloads

Every payload has a 65-byte prefix: version byte 1, the existing complete 32-byte
`WorldBootstrapIdentity`, and 32 raw manifest-digest bytes. The bootstrap retains
match, full 64-bit epoch, start generation, baseline revision, recipient occupant
generation, authority frame and slow/world revisions. All identity fields required
by the bootstrap are nonzero. The exact bootstrap is an admission fence, not an
approximation based on the newest movement packet.

Manifest pages add total rows, page index and row count (three bytes), followed by
up to 16 rows. At most four pages, each at most 868 bytes, reconstruct 64 entries.
Exact expected page lengths and contiguous IDs are mandatory. Pages may arrive in
any order. Identical repeats are idempotent; conflicting repeats cannot overwrite
retained rows. Incomplete data never resolves. Cross-bootstrap/digest/count pages
are refused. Incorrect full digest or cross-page ordering permanently invalidates
that assembler; a fresh admission requires a fresh instance. The final manifest
must resolve every exact runtime ID and definition against the frozen scene registry
before any readiness acknowledgement can be considered.

Readiness is exactly 72 bytes: prefix, claimed slot byte, mechanics schema u16,
behavior version u16 and required capability bits u16, all LE. Version 1 requires
schema 1, behavior 1 and precision capability 1, with no unknown bits.

Actions are exactly 109 bytes and carry no damage, ammunition, target or kill claim:

| Offset | Bytes | Meaning |
| --- | --- | --- |
| 0 | 65 | Common prefix |
| 65 | 1 | Claimed slot (ingress must replace/check against authenticated sender) |
| 66 | 2 | Owner life |
| 68 | 2 | Match-local custom runtime ID 10–73 |
| 70 | 4 | Nonzero per-life custom action sequence |
| 74 | 4 | Original owner source frame |
| 78 | 4 | Opponent-world ACK frame |
| 82 | 1 | ACK subframe |
| 83 | 1 | Fire 1, reload 2, optic 3, equip 4, unequip 5 |
| 84 | 1 | Source/requested optic: hip 0, precision 1 or 2 |
| 85 | 12 | Three finite float32 LE muzzle coordinates |
| 97 | 12 | Three float32 LE aim-direction components |

Fire rays must have finite approximately unit directions (squared norm within .02)
and finite coordinates within 1,000,000 units. This is only structural rejection;
source history must enforce actual muzzle distance, aim agreement and occlusion.
Non-fire actions require zero ray/ACK fields; only an optic action may carry a
nonzero optic. No caller-supplied mechanic is authoritative. Readers reject short,
trailing, unknown-version, malformed or unsupported payloads without throwing.

`CustomActionIdentity` is a distinct type with full owner life, manifest digest,
runtime ID and action sequence. It has no conversion to native `ShotKey` or
`BeamType`. Source/ACK frames are timing evidence, not pairing keys. Future proof
and payment integration must preserve the custom domain through every shared join;
using a second bare native shot counter is prohibited. A custom per-life request
high-water must burn denied IDs across action kinds/weapons; changing payload,
weapon or source frame cannot revive a denied request.

## Mandatory runtime gate, still pending

Codecs do not establish authorization. Integration must derive sender slot from the
peer endpoint, require custom rules and every participant's exact capability/ready
acknowledgement, compare the current complete world bootstrap, and verify current
match/epoch/generation/life. Bound ingress and pending work before expensive
history checks. The source clock/pose/resource journal must validate immutable
accepted evidence, reserve exact emitter capacity, and pay only a genuine emission.

Custom native collision metadata must not enter native-only proof/claim joins or
be reconstructed as `NoBeam`/alternate-form feedback. Add typed accepted outcomes
joined to canonical damage-event identity and full target life, using existing
health/death and equal-world kill-trade ordering exactly once. Native and custom
same-number request IDs must never alias. Pass explicit accepted timing and the
private pool through rewind/catch-up; never substitute native equipment temporarily.

Late join, spectator, reconnect, rematch, occupant/life reuse and result/snapshot
reordering require complete state and bounded replay/delivery fences. Until CW-12
provides exact custom replay bootstrap, refuse custom recording/killcam paths
explicitly. Validate real dedicated/host/remote transport under loss, duplicate,
reorder and low/high RTT before enabling custom multiplayer or advancing CW-05.
Historical native packet/replay fixtures remain unchanged. No CI status substitutes
for these runtime gates.
