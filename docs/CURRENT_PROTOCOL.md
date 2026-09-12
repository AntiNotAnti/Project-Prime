# Current Project Prime protocol

Status: authoritative live-wire reference, 2026-09-12. The current
authoritative wire family is `Authoritative`, protocol **16**. Protocol 15 and
older peers are intentionally incompatible with the live build.

## Envelope and admission

`NetHeader` is 24 bytes, little-endian, with magic `0x5046`; the maximum UDP
packet is 1024 bytes. The fixed authoritative simulation step is 1/60 second.
Established UDP datagrams are authenticated with a 32-byte per-connection key,
direction-specific domain separation, and a 16-byte HMAC-SHA256 tag. The tag
covers protocol version, direction, the complete header, and payload. Failed
authentication or malformed framing must have no state effect. Node-issued
admission/rejoin identity and current match/life validation remain separate
from client-supplied hints.

## Version milestones

| Protocol | Current meaning |
| ---: | --- |
| 12 | authoritative input life epoch (`InputEpoch`) |
| 13 | bounded frame/timing telemetry |
| 14 | authenticated established-connection datagrams |
| 15 | explicit optional quantized radial movement axes |
| **16** | authoritative Spire alternate-form attack presentation flag |

This table records wire history; it does not make old live peers compatible
with protocol 16.

## Input command (introduced in protocol 15)

`InputCommand.Size` is 43 bytes. Existing sequence, ticks, buttons, pressed
edges, finite aim, desired weapon, boost intent, and `InputEpoch` fields retain
their prior validation. The final bytes are:

| Offset | Size | Field | Rule |
| ---: | ---: | --- | --- |
| 40 | 1 | `MoveX` | signed quantized axis, -127..127 |
| 41 | 1 | `MoveY` | signed quantized axis, -127..127 |
| 42 | 1 | `AnalogMovementPresent` | 0 or 1 |

The signed value -128 is reserved and rejected. Present axes decode to a
finite radial vector; only bounded encoder-rounding overrun is normalized.
Absent axes mean no analog contribution. Digital movement binds remain
unchanged, including keyboard diagonals, opposing keys, touch, bots, and
alternate forms. Mixed keyboard/controller capture preserves digital intent
and adds/clamps the controller contribution once; it does not reapply
controller-derived digital buttons on the server.

## Snapshot player (protocol 16)

`SnapshotPlayer.Size` remains 96 bytes. Bit 14 of its existing 16-bit flags
field is `SpireAltAttack`; the Worker sets it only while a spawned Spire is in
alternate form and its authored rock attack is active. Remote clients and
modern replay playback use the edge to start or stop the matching animation,
sound, and rock presentation. It grants no client authority and does not add a
new input or gameplay mutation path.

`InputBundle` carries up to eight contiguous commands from one input epoch.
`WithoutEdges` retains held axes; `Neutral` clears axes and analog presence.
Analog magnitude affects movement acceleration/traction only, never speed caps
or gameplay timing. Boost/flick does not acquire invented analog semantics.

## Replay compatibility

Live admission is exact-family/exact-protocol. Stored protocol-14 and
protocol-15 replay timelines remain readable through their replay codecs when they
do not contain live `InputCommand` payloads; this is storage compatibility, not
live wire compatibility. Historical replay format identifiers are retained as
on-disk contracts.

## Evidence boundary

Protocol codec, mutation, quantization, admission, and round-trip tests are
static/focused evidence. They do not establish geographic WAN behavior,
physical controllers, or a deployed Worker/Backend. See
`CURRENT_RELEASE_GATES.md` for the current run ledger.
