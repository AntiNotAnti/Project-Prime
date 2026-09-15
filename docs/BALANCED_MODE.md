# Balanced Mode

Status: Balanced V1 is implemented in the shared authoritative/predicted
simulation and protocol foundation. Extracted-content collision and rendered-client
gates remain separate validation work.

Balanced Mode is selected by the single authoritative `MatchRules.BalancedMode`
boolean and defaults to `false` (Classic). Lobby and host-edit APIs carry it as
nullable `BalancedMode`; `null` means “use the mode default,” which currently
resolves to Classic. The Node-owned lobby snapshot, revision, match handoff,
rematch, and next-round projections are the authority. A frozen `MatchSpec`
delivers the rules to the Worker, which owns one immutable balance context per
`MatchRuntime`. There is no player-local setting and no independently mutable
profile identity.

## Authority, late join, and reports

Host edits are normalized once by the Node. Lobby list/snapshot payloads,
revision checks, rematch and next-round continuations, and `MatchSpec` all carry
the same nullable-to-boolean `BalancedMode` value. The Worker constructs its
profile from that boolean and rejects a network/spec rules mismatch. Late joins
receive the authoritative baseline rules in `JoinAcceptedPacket` and observer
transition packets; they do not infer a local setting.

`MatchReportV1.Rules` already is the report's frozen rules contract, so it
persists `BalancedMode` without adding a second profile field. Worker completion
and `MatchReportBinding` retain and compare the complete `MatchRules` value;
Backend validation and rating policy do not silently reinterpret Balanced Mode.
Historical JSON that omits the field deserializes to `false` (Classic).

## Wire contract

Live protocol 24 grows `MatchRulesWire` from 84 to exactly 85 bytes:

| Offset | Field | Values |
| ---: | --- | --- |
| 83 | `PowerupsEnabled` | `0` enabled, `1` disabled |
| 84 | Balanced profile | `0` Classic, `1` Balanced V1 |

The profile byte is appended; byte 83 is never reused. Current decoding rejects
unknown profile values and any truncated or extended rule payload. `NetHeader`
is now 24, while Node control framing advances to codec version 4. A protocol-23
replay uses the frozen 84-byte payload and maps the missing profile to Classic.
Protocol-23 snapshot (112-byte player stride), combat (82-byte event, six-event
batch), match-transition (84-byte rules), and highlight paths use explicit
`NetHeader.BalancedModeVersion` schema cutoffs after the live protocol advances;
they must not fall through to the current 24/85-byte layouts. v21/v22 replay
fixtures remain on their own historical codecs.

## UI contract

Host/Edit Match exposes **Balanced Mode** with the tooltip:

> Applies Project Prime's competitive hunter and weapon balance adjustments.
> Disable for original MPH-style mechanics.

Lobby summaries expose `BALANCED: ON/OFF`. The setting is displayed from the
authoritative lobby snapshot and is not inferred from Enhanced Hunters.

## Balanced V1 gameplay values

Gameplay changes are resolved from the match-owned immutable profile and are
relative to the canonical multiplayer `WeaponInfo`/hunter metadata. Classic is
an identity profile: every value below is zero or one, and the authored values
are used unchanged. The resolver is a fixed switch over compact value types; it
does not mutate shared weapon tables or allocate on a warmed hot path.

| Hunter/form | Balanced delta | Unit/limit |
| --- | ---: | --- |
| Trace Triskelion | horizontal speed cap × 0.95 | multiplier; biped unchanged |
| Sylux Lockjaw | horizontal speed cap × 0.95 | multiplier; biped unchanged |
| Samus Morph Ball | normal horizontal speed cap × 0.90 | Boost Ball impulse/cap is not scaled |
| Samus Boost Ball | damage basis `max(canonical - 12, 0)` | apply before charge scaling |
| Kanden Stinglarva | player damage +5 | canonical damage units |

The existing canonical boost integer division is retained. Samus's boost
adjustment is made before charge interpolation and is clamped at zero.

### Weapon/equip deltas

Damage and splash values are the integer fields from `WeaponInfo`. Ammo values
are internal pool units (the 20-unit missile charge cost is two display
missiles). Speeds and radii use the authored fixed-point representation
(`4096` units per world unit); therefore the Battlehammer radius delta `8192`
is exactly two world units.

| Weapon/owner/form | Balanced V1 delta | Scope |
| --- | --- | --- |
| Stinglarva | player damage +5 | Kanden's player-owned alt attack |
| Volt Driver/Kanden affinity | uncharged body +2, uncharged headshot +2; min/full charged body +8 | charged headshots unchanged |
| Volt Driver/Kanden affinity | charged initial speed and final speed = affinity Volt uncharged values | applies to charged projectile stats |
| Missile/Samus affinity | uncharged body +2 | headshots unchanged |
| Missile/Samus affinity | min/full charged cost = 20 | internal ammo-pool units; insufficient ammo cannot charge/spawn |
| Trace Universal Ammo | cap delta -300 | `15` Imperialist display shots × canonical `AmmoCost 20`; clamp current ammo at init/profile change |
| Magmaul (all forms) | uncharged body +2, uncharged splash +10 | shared delta |
| Magmaul (non-affinity) | min/full charged body +10, splash +2 | mutually exclusive with Spire affinity branch |
| Magmaul/Spire affinity | min/full charged body +12, splash +12 | mutually exclusive with non-affinity branch |
| Judicator/Noxus affinity | uncharged body +2, uncharged headshot +2 | charged fields and child weapons unchanged |
| Battlehammer (all forms) | uncharged body +4, uncharged splash +6 | shared delta |
| Battlehammer (non-affinity) | uncharged splash radius +8192 | fixed world units; Weavel affinity radius unchanged |
| Shock Coil | unchanged | Classic and Balanced |

Player weapon equip and Halfturret initialization/reconciliation select the
existing affinity metadata first, then apply the central resolver. Ammo and
charge checks, bot selection, projectile spawn, and turret aim read the
effective `EquipInfo` values. Root projectiles freeze their resolved values;
Judicator children, ricochet attribution, and explicit enemy/alternate overrides
keep their existing metadata unless one of the named deltas applies. A
same-profile rules update does not reset ammo/equipment; a profile transition
reconciles initialized players and the turret once.

### Spire Dialanche ledge transition (P3b)

Balanced Spire alternate form may transition over a ledge only from a current
static lateral wall face. The shared authoritative/predicted collision path
requires a finite, non-damaging wall whose top rises above the sphere by no
more than one alternate-form diameter, a static forward/above face beyond the
lip, a clear stationary Dialanche sphere, and a downward static support hit
with `normal.Y > 0.5`. Entity or moving faces, edges/corners, ceilings,
unsupported or steep surfaces, damaging/Lava/Acid support, obstructions, and
active rectangular ForceFields reject the transition. Probe radius and forward
distance are derived from Spire's canonical alternate collision radius and
sweep padding; the landing preserves horizontal velocity and clamps downward
vertical velocity to zero. Classic never enables this policy.

### Splash policy

The current collision source applies ordinary body splash to Trace and Noxus
alternate forms in Classic. That source evidence diverges from the older plan
note that proposed Classic immunity, so Classic remains a no-op and the same
body vulnerability is preserved in Balanced. Balanced additionally considers
the active Weavel Halfturret head as a splash point. For each owner, the
nearest visible eligible body or historical/current turret point is selected;
there is at most one application. A direct body or turret hit suppresses the
owner's later splash application, and a head selection carries the existing
`DamageFlags.Halfturret` bookkeeping. Team, self, authority, occlusion,
lag-compensation, and near-splash behavior remain in the existing collision
path.

## Enhanced Hunters independence

`BalancedMode` and `EnhancedHunters` are separate rule bits. All four
combinations are valid and keep Guardian's canonical identity; enabling one
does not derive, enable, or mutate the other. Enhanced Hunter behavior and
Guardian parity remain owned by their existing subsystem.

## Validation and external gates

The focused balance, protocol, lobby, replay, Worker, and Enhanced Hunter tests
cover Classic no-op behavior, every numeric distinction, profile transitions,
late-join rule ownership, projectile stat freezing, four-way Balanced ×
Enhanced isolation, and warmed resolver allocation behavior. `git diff --check`
is part of the local gate. The Release solution build and the focused balance,
protocol, lobby, replay, report, and UI suites have been rerun successfully.

The content-backed `tools/nettest` combat, duel, weapon, homing, catch-up, and
mixed-soak diagnostics accept an explicit `--balanced` flag (or the documented
trailing `balanced` token on legacy positional commands). Omitting the selector
retains the existing Classic behavior. Local AMHE1 runs covered both profiles
for direct combat, a real two-client UDP duel, weapon policy, homing,
projectile catch-up/collision, and an eight-client mixed-combat soak.

The paired 10-second mixed-combat samples each completed 601 authoritative
ticks. Classic measured 0.468 ms p50 / 1.115 ms p95 and 165,350 B/tick for the
whole diagnostic process; Balanced measured 0.458 ms p50 / 1.490 ms p95 and
165,130 B/tick. These short process-wide numbers are noisy operational samples,
not a benchmark guarantee. The focused warmed resolver and blast-policy tests
are the allocation gate for the balance layer itself and measure zero bytes per
operation.

Windows client/rendered gameplay, real WAN loss/jitter, Android hardware, and
AMHE1 fidelity gates remain external evidence requirements and are not claimed
by this source or local headless validation.
