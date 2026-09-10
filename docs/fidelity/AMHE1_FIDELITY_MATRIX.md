# Project Prime AMHE1 fidelity matrix

This matrix is closed administratively under the user's 2026-09-09 instruction to
disregard the program blockers. The waiver makes `BlockedByEvidence` an approved
terminal result; it does not turn missing AMHE1 runtime observations into proof.

Evidence labels in this table mean:

- `E2` is raw AMHE1 content inspected by a deterministic, read-only tool.
- `Project` is Project Prime source, test, or controlled-runtime evidence only.
- `E0/E1 missing` means no exact AMHE1 code/data locator or repeatable AMHE1 trial
  was available to establish parity.

## Foundation, combat, shared gameplay, and presentation

| Case | Domain | Subject | Priority | Evidence | Reference | Project Prime | Classification | Status | Tests | Owner/Pass |
|---|---|---|---|---|---|---|---|---|---|---|
| FID-SIM-001 | Simulation | Units, rounding, timers | P1 | Project; E0/E1 missing | Static locator plus trial required | Fixed 60 Hz authority | Reference uncertainty | BlockedByEvidence | Fidelity deterministic smoke | F1/F3 |
| FID-MOVE-001 | Movement | Ground/air, caps, jump | P1 | Project; E0/E1 missing | Code/data/trial required | Real authoritative player update | Reference uncertainty | BlockedByEvidence | Fixed-input fidelity proof | F3 |
| FID-COLLISION-001 | Collision | Shapes, contacts, slopes, hazards | P1 | E2 room entities; E0/E1 missing | Data plus trial required | Scene collision tests | Reference uncertainty | BlockedByEvidence | Existing collision suites | F3 |
| FID-PROJECTILE-001 | Projectile | Spawn, travel, impact, lifetime | P1 | Project; E0/E1 missing | Tables plus trial required | Authoritative entity simulation | Reference uncertainty | BlockedByEvidence | F1 projectile lifecycle | F4 |
| FID-DAMAGE-001 | Damage | Direct, splash, self, ordering | P1 | Project; E0/E1 missing | Code plus trial required | Server combat pipeline | Reference uncertainty | BlockedByEvidence | F1 direct-damage proof; combat suites | F4 |
| FID-AFFLICTION-001 | Affliction | Freeze, disrupt, burn | P1 | Project; E0/E1 missing | Code plus trial required | Authoritative status state | Reference uncertainty | BlockedByEvidence | Existing combat/status suites | F4 |
| FID-PICKUP-001 | Pickup | Eligibility, consume, respawn | P1 | E2 entities; E0/E1 missing | Entity data plus trial required | Authoritative item entities | Reference uncertainty | BlockedByEvidence | F1 pickup lifecycle | F6 |
| FID-SPAWN-001 | Spawn | Selection, protection, death/respawn | P1 | E2 entities; E0/E1 missing | Code/data/trial required | Spawn director | Reference uncertainty | BlockedByEvidence | F1 spawn/respawn; G1 spawning suites | F3/F6 |
| FID-OBJECTIVE-001 | Objective | Ownership, transitions, scoring | P1 | Project content-backed; E0/E1 missing | AMHE1 trial required | Match objective state | Reference uncertainty | BlockedByEvidence | F1 node transition; baseline/overtime tools | F6 |
| FID-CAMERA-001 | Camera | Player, alt-form, observer | P2 | Project; E1 missing | AMHE1 capture required | Presentation projection | Reference uncertainty | BlockedByEvidence | Existing camera/observer tests | F7 |
| FID-ANIMATION-001 | Animation | Gameplay-event timing | P2 | Project; E1 missing | AMHE1 capture required | Presentation consumes facts | Reference uncertainty | BlockedByEvidence | Existing animation/presentation tests | F7 |
| FID-AUDIO-001 | Audio | Trigger, priority, loop, stop | P2 | E2 sequence inventory; E1 missing | AMHE1 capture required | Feedback/audio routing | Reference uncertainty | BlockedByEvidence | Existing audio/feedback tests | F7 |
| FID-UI-001 | UIFeedback | Hit, kill, objective, HUD | P2 | Project; E1 missing | AMHE1 capture required | Client feedback projection | Reference uncertainty | BlockedByEvidence | Existing feedback/radar tests | F7 |

## Weapons

The content-backed weapon policy check exercised 61 Project Prime cases across 18
variants through the real spawn path. That verifies current behavior, not AMHE1
parity, because field-level AMHE1 provenance and dynamic trials are still absent.

| Case | Domain | Subject | Priority | Evidence | Reference | Project Prime | Classification | Status | Tests | Owner/Pass |
|---|---|---|---|---|---|---|---|---|---|---|
| FID-WEAPON-PB | Weapon | Power Beam, charge, affinity | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-VD | Weapon | Volt Driver, charge, disrupt | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-MS | Weapon | Missile, charge, affinity | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-BH | Weapon | Battlehammer, splash, affinity | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-IM | Weapon | Imperialist, zoom, headshot | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-JD | Weapon | Judicator, charge, freeze | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-MG | Weapon | Magmaul, charge, burn | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-SC | Weapon | Shock Coil, trace, affinity | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |
| FID-WEAPON-OC | Weapon | Omega Cannon, world interaction | P1 | Project content-backed; E0/E1 missing | AMHE1 table/trial required | Weapon policy/spawn path | Reference uncertainty | BlockedByEvidence | `--weapon-policy` | F4 |

## Hunters and alt forms

Each retail Hunter is a separate vertical audit row. No Hunter constants were
changed because no exact AMHE1 discrepancy passed Gate C.

| Case | Domain | Subject | Priority | Evidence | Reference | Project Prime | Classification | Status | Tests | Owner/Pass |
|---|---|---|---|---|---|---|---|---|---|---|
| FID-HUNTER-SA | Hunter | Samus and Morph Ball | P1 | Project; E0/E1 missing | AMHE1 code/data/trial required | Player/alt state machine | Reference uncertainty | BlockedByEvidence | Existing Hunter/alt tests | F5 |
| FID-HUNTER-KA | Hunter | Kanden and Stinglarva | P1 | Project; E0/E1 missing | AMHE1 code/data/trial required | Player/alt state machine | Reference uncertainty | BlockedByEvidence | Existing Hunter/alt tests | F5 |
| FID-HUNTER-TR | Hunter | Trace and Triskelion | P1 | Project; E0/E1 missing | AMHE1 code/data/trial required | Player/alt state machine | Reference uncertainty | BlockedByEvidence | Existing Hunter/alt tests | F5 |
| FID-HUNTER-SY | Hunter | Sylux and Lockjaw | P1 | Project; E0/E1 missing | AMHE1 code/data/trial required | Player/alt state machine | Reference uncertainty | BlockedByEvidence | Existing Hunter/alt tests | F5 |
| FID-HUNTER-NO | Hunter | Noxus and Vhoscythe | P1 | Project; E0/E1 missing | AMHE1 code/data/trial required | Player/alt state machine | Reference uncertainty | BlockedByEvidence | Existing Hunter/alt tests | F5 |
| FID-HUNTER-SP | Hunter | Spire and Dialanche | P1 | Project; E0/E1 missing | AMHE1 code/data/trial required | Player/alt state machine | Reference uncertainty | BlockedByEvidence | Existing Hunter/alt tests | F5 |
| FID-HUNTER-WE | Hunter | Weavel and Halfturret | P1 | Project; E0/E1 missing | AMHE1 code/data/trial required | Player/alt state machine | Reference uncertainty | BlockedByEvidence | Existing Hunter/alt tests | F5 |
| FID-HUNTER-GU | Hunter | Guardian compatibility/bot path | P2 | Project compatibility path only | Not an in-scope retail player Hunter | Compatibility extension | Scope decision | Rejected | Existing bot/Hunter tests | F5 |

## Retail multiplayer room entity inventories

The read-only multiplayer audit parsed every scoped room's format-v2 entity file
with 16 layers and no errors. Each row remains blocked for dynamic AMHE1 behavior;
an entity inventory is not proof of trigger, mover, door, hazard, or objective timing.

| Case | Domain | Subject | Priority | Evidence | Reference | Project Prime | Classification | Status | Tests | Owner/Pass |
|---|---|---|---|---|---|---|---|---|---|---|
| FID-MAP-093 | MapEntity | MP1 SANCTORUS (58 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-094 | MapEntity | MP2 HARVESTER (55 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-095 | MapEntity | MP3 PROVING GROUND (27 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-096 | MapEntity | MP4 HIGHGROUND - EXPANDED (70 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-097 | MapEntity | MP4 HIGHGROUND (44 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-098 | MapEntity | MP5 FUEL SLUICE (30 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-099 | MapEntity | MP6 HEADSHOT (60 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-100 | MapEntity | MP7 PROCESSOR CORE (28 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-101 | MapEntity | MP8 FIRE CONTROL (60 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-102 | MapEntity | MP9 CRYOCHASM (45 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-103 | MapEntity | MP10 OVERLOAD (31 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-104 | MapEntity | MP11 BREAKTHROUGH (25 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-105 | MapEntity | MP12 SIC TRANSIT (74 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-106 | MapEntity | MP13 ACCELERATOR (56 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-107 | MapEntity | MP14 OUTER REACH (47 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-108 | MapEntity | CTF1 FAULT LINE - EXPANDED (82 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-109 | MapEntity | CTF1_FAULT LINE (52 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-110 | MapEntity | AD1 TRANSFER LOCK BT (53 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-111 | MapEntity | AD1 TRANSFER LOCK DM (40 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-112 | MapEntity | AD2 MAGMA VENTS (63 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-113 | MapEntity | AD2 ALINOS PERCH (45 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-114 | MapEntity | UNIT1 ALINOS LANDFALL (46 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-115 | MapEntity | UNIT2 LANDING BAY (44 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-116 | MapEntity | UNIT 3 VESPER STARPORT (40 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-117 | MapEntity | UNIT 4 ARCTERRA BASE (40 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |
| FID-MAP-118 | MapEntity | Gorea Prison (23 entities) | P1 | E2 raw entity digest/inventory | Dynamic trial missing | Content loads/audits | Reference uncertainty | BlockedByEvidence | `--audit-multiplayer` | F6 |

## Multiplayer modes

The real-content Project Prime baseline and overtime tools passed all 12 supported
modes. These rows remain blocked only on the absent AMHE1 runtime comparison.

| Case | Domain | Subject | Priority | Evidence | Reference | Project Prime | Classification | Status | Tests | Owner/Pass |
|---|---|---|---|---|---|---|---|---|---|---|
| FID-MODE-B | GameMode | Battle | P1 | Project content-backed; E1 missing | AMHE1 trial required | Score/win path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-BT | GameMode | Battle Teams | P1 | Project content-backed; E1 missing | AMHE1 trial required | Score/win path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-S | GameMode | Survival | P1 | Project content-backed; E1 missing | AMHE1 trial required | Lives/win path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-ST | GameMode | Survival Teams | P1 | Project content-backed; E1 missing | AMHE1 trial required | Lives/win path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-C | GameMode | Capture | P1 | Project content-backed; E1 missing | AMHE1 trial required | Flag path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-BO | GameMode | Bounty | P1 | Project content-backed; E1 missing | AMHE1 trial required | Objective path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-BOT | GameMode | Bounty Teams | P1 | Project content-backed; E1 missing | AMHE1 trial required | Objective path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-N | GameMode | Nodes | P1 | Project content-backed; E1 missing | AMHE1 trial required | Node path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-NT | GameMode | Nodes Teams | P1 | Project content-backed; E1 missing | AMHE1 trial required | Node path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-D | GameMode | Defender | P1 | Project content-backed; E1 missing | AMHE1 trial required | Defender path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-DT | GameMode | Defender Teams | P1 | Project content-backed; E1 missing | AMHE1 trial required | Defender path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |
| FID-MODE-PH | GameMode | Prime Hunter | P1 | Project content-backed; E1 missing | AMHE1 trial required | Prime path passes | Reference uncertainty | BlockedByEvidence | baseline/overtime | F6 |

## Modern projection and extensions

| Case | Domain | Subject | Priority | Evidence | Reference | Project Prime | Classification | Status | Tests | Owner/Pass |
|---|---|---|---|---|---|---|---|---|---|---|
| FID-NET-001 | Networking | Server authority, transport, ordering | P0 | Product invariant | AMHE1 outcomes only | Worker UDP plus Node control | Intentional deviation | IntentionalDeviation | Existing integration/impairment suites | F8 |
| FID-REPLAY-001 | Replay | Record, playback, seek, observers | P1 | Product invariant | AMHE1 outcomes only | Versioned authoritative facts | Intentional deviation | IntentionalDeviation | Existing replay/observer suites | F8 |
| FID-BOT-001 | Bot | Server bots use shared corrected rules | P1 | Product invariant | AMHE1 outcomes only | Inputs enter same authority | Intentional deviation | IntentionalDeviation | Existing bot/integration suites | F8 |
| FID-RULESET-001 | Ruleset | Competitive remains separate | P1 | Product invariant | Classic is comparison target | Explicit Competitive overrides | Intentional deviation | IntentionalDeviation | Fixture ruleset validation | F4/F9 |
| FID-CONTENT-001 | Content | Custom maps/player-count extensions | P2 | Product invariant | Outside AMHE1 scope | Shared-rule extensions | Intentional deviation | IntentionalDeviation | Package/capacity/isolation suites | F6/F9 |

## Closure summary

- Total cases: 73.
- `BlockedByEvidence`: 67.
- `IntentionalDeviation`: 5.
- `Rejected`: 1.
- Nonterminal cases: 0.
- Gameplay corrections made by this audit: 0; no discrepancy had sufficient
  AMHE1 evidence to authorize a Classic behavior change.

Terminal states are `Fixed`, `IntentionalDeviation`, `UnableToReproduce`,
`BlockedByEvidence`, and `Rejected`. Any later evidence-driven correction must open
a narrow case, preserve its raw locator/trial, add a deterministic regression, and
update this matrix rather than treating the administrative waiver as parity proof.
