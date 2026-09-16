# Current Project Prime protocol

Status: authoritative live-wire reference, 2026-09-16. The current
authoritative wire family is `Authoritative`, protocol **26**. Protocol 26 and
older peers are intentionally incompatible with the live build. Protocol 21,
22, 23, 24, and 25 replay records remain readable through frozen
version-specific decoders.

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
| 16 | authoritative Spire alternate-form attack presentation flag |
| 17 | authoritative remote weapon charge presentation state |
| 18 | authoritative two-to-four team count |
| 19 | presentation-only cosmetic IDs in reliable roster state |
| 20 | stable rolling-form control heading in input `Aim` |
| 21 | authoritative power-up timers and active cloak flag; frozen replay layout |
| **22** | Guardian/Psycho Bit official Project Prime extension and generic alt-attack semantics |
| **23** | Enhanced Hunters rule, authoritative player suffix, and bounded combat effects |
| **24** | Balanced Mode profile metadata appended to `MatchRulesWire` |
| **25** | Authoritative alternate-form action state in `SnapshotPlayer` and resource-radar policy in `MatchRulesWire` |
| **26** | Authoritative projectile-impact presentation facts in the existing `CombatEvent` wire record |

This table records wire history; it does not make old live peers compatible
with protocol 26.

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

For a biped or a strafe-capable alternate form, `Aim` remains the normalized
weapon aim ray. While a rolling form is active or morphing, `Aim` instead
carries the normalized retained horizontal control heading. The Worker uses
that heading to resolve the same camera-relative WASD axes as local prediction;
ball velocity and collision-adjusted camera motion cannot redefine those axes.
This conditional semantic change introduced protocol 20 without changing the
43-byte command layout.

## Snapshot player (protocol 25)

`SnapshotPlayer.Size` is 115 bytes. Bit 14 of its existing 16-bit flags field
remains the compatibility/active indicator `AltAttack`; protocol 25's phase
field is authoritative. The Worker appends `AltActionPhase` at byte 112 and
elapsed phase ticks (`ushort`, little-endian) at bytes 113-114. `None` requires
zero ticks and a clear compatibility bit. A non-empty phase requires a living
supported alt-form player and the compatibility bit set; malformed phase,
form, life, hunter, and bit/phase combinations are rejected.

Trace, Weavel, Spire, and Guardian currently capture `Active`. Noxus captures
`Charging` while `_altAttackTime` is below its authored startup and `Active`
once startup is reached. `Recovery` is reserved on the wire; no Noxus recovery
timing is synthesized. The state is presentation-only on replicas: applying a
snapshot never activates damage, projectiles, cooldowns, or other gameplay
helpers.

The final two bytes at offset 96 are `ChargeLevel`, an unsigned count of 60 Hz
simulation ticks. The Worker copies its authoritative equipped-weapon charge;
remote clients use it only to select the authored charging gun animation and
muzzle effect. Local prediction is not overwritten during ordinary snapshot
application. Charge is clamped to the equipped weapon's full-charge threshold
before presentation.

`InputBundle` carries up to eight contiguous commands from one input epoch.
`WithoutEdges` retains held axes; `Neutral` clears axes and analog presence.
Analog magnitude affects movement acceleration/traction only, never speed caps
or gameplay timing. Boost/flick does not acquire invented analog semantics.

## Match rules (protocol 25)

`MatchRulesWire` is exactly 86 bytes. The existing `PowerupsEnabled` field is
at byte 83; Balanced Mode is the profile byte at byte 84; and protocol 25
appends `ResourceRadarPolicy` at byte 85. Resource radar values are `0`
Disabled, `1` SpawnLocations, `2` AvailableResources, and `3`
AvailableWithRespawn. Unknown values and truncated or extended payloads are
rejected. Protocol 24 retains the frozen 85-byte rule payload and defaults
resource radar to Disabled. Protocol 23 retains the frozen 84-byte payload and
also defaults Balanced Mode to Classic. Existing bytes are never repurposed.

The public rule identity is the single `MatchRules.BalancedMode` boolean. A
missing value in historical JSON or an omitted nullable lobby option means
`false`; the runtime may derive an internal profile from that value but does
not persist an independently mutable second identity.

## Node control envelope

`NodeControlCodec.Version` is **4**. `LobbyConfigure.Rules.BalancedMode` is an
additive nullable option in this envelope. The Node remains authoritative:
clients may edit and display the option, but snapshots, revisions, rematches,
and next-round projections carry the value returned by the Node.

## Replay compatibility

Live admission is exact-family/exact-protocol. Stored protocol-14 through
protocol-19 replay timelines remain readable through their replay codecs when they
do not contain live `InputCommand` payloads; this is storage compatibility, not
live wire compatibility. The frozen protocol-8–16 snapshot adapter supplies a
zero charge level because those 96-byte records never encoded one. Historical
replay format identifiers are retained as on-disk contracts. Protocol 17–20
replays retain the frozen 98-byte codec. Protocol 21 and 22 replays retain a
separate frozen 104-byte codec; they must never be routed through the 98-byte
adapter. Protocol-21 Guardian records remain biped-only, while protocol 22
retains its recorded Guardian alternate-form semantics.
Protocol 21 and 22 replay player records remain frozen at 104 bytes and are
expanded with canonical no-Enhanced state (target slot 255 and zero timers).
Protocol 23 appends the 8-byte Enhanced Hunters suffix: target slot at 104,
Overcharge at 105, target ticks at 106, Chill ticks at 108, and cloak-fade
ticks at 110. Protocol 24 replay snapshots remain frozen at 112 bytes; their
legacy `AltAttack` bit maps to `Active` at elapsed tick 0 for the Hunters whose
active pose can be reconstructed. Protocol 25 snapshots are 115 bytes and an
eight-player snapshot is 946 bytes, within the authenticated transport payload
budget.

Protocol 23 also appends the canonical `EnhancedEffect` world record for an
active Spire Lingering Heat patch. It carries the scene-owned effect ID,
position, full owner combat identity, and remaining lifetime. Clients use the
record only to restore bounded presentation for late join and replay seek;
damage and lifetime remain Worker-owned. Protocols before 23 reject this
record kind.

Protocol 23 match records retain their 84-byte `MatchRulesWire` payload and
are decoded by a frozen match-transition adapter. Protocol 23 snapshot records
retain the 112-byte player stride, and protocol 23 combat records retain the
82-byte event / six-event batch layout. Highlight timeline inspection uses the
same explicit protocol-23 snapshot stride. None of these records fall through
to the protocol-24 live decoder.

Protocol 24 replay records retain the 112-byte snapshot, 82-byte combat, and
Enhanced Hunters world layouts already introduced by protocol 23. They are
decoded by explicit frozen adapters and never fall through to the protocol-25
live snapshot decoder. Protocol 25 records add the alternate-action snapshot
suffix and the resource-radar match-rule byte described above; combat and world
layouts are otherwise unchanged.

Protocol 26 retains the fixed 82-byte `CombatEvent` record and six-event batch
capacity. `CombatEventKind.Impact` records carry the authoritative collision
effect ID in `Amount`, the impact position in `Position`, and the normalized
surface normal in `Direction`; `NoSplat`, `Charged`, and `Affinity` are the
only permitted impact flags. Impact records are presentation-only: replicas
and replay readers may spawn the bounded impact effect, while gameplay state
continues to come from snapshots and Worker-owned combat mutation. Protocols
before 26 reject impact kinds and the `NoSplat` flag.

## Guardian/Psycho Bit extension

Guardian is an official Project Prime playable Hunter at enum value 7;
`Hunter.Random` remains selector sentinel 8. This is a Project Prime extension,
not a claim of retail AMHE1 player fidelity. Guardian uses Power Beam affinity,
the existing authoritative projectile/lag-compensation path, and the generic
active `AltAttack` snapshot bit. Psycho Bit uses the supplied AMHE1 model and
effects with a conservative grounded/hover-styled alt adaptation bounded by
Guardian's authored collision and `PlayerValues` envelope. Exact retail enemy
semantics and frame-count meanings are unavailable; see
`docs/fidelity/GUARDIAN_V1.md`.

## Evidence boundary

Protocol codec, mutation, quantization, admission, and round-trip tests are
static/focused evidence. They do not establish geographic WAN behavior,
physical controllers, or a deployed Worker/Backend. See
`CURRENT_RELEASE_GATES.md` for the current run ledger.
