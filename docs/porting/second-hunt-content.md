# External content port: pinned source and compatibility contract

Status: historical inventory plus explicitly scoped integration slices below. This document and
`second-hunt-manifest.json` are source-code review artifacts, not extracted Nintendo data.

## Historical PORT source pin

- Source: [Second Hunt](https://github.com/BountyHunterKanden/SecondHunt/tree/c16a907b3658c2fd4079e394a73711596a85caee)
  at `c16a907b3658c2fd4079e394a73711596a85caee` (do **not** assume later upstream commits are compatible).
- Target baseline: [Project Prime](https://github.com/AntiNotAnti/Project-Prime/tree/ebbf3c51fa6f205ac3b8c952f4f3ab67d0e7cc80)
  at `ebbf3c51fa6f205ac3b8c952f4f3ab67d0e7cc80`.
- Upstream code is MIT-licensed with notices; the MIT code license does not license
  Nintendo/Retro Studios meshes, music, disc images, textures or cutscenes.
- Review the files and notices when adapting code. Do not vendor ROM, PAK, MREA,
  generated body/model/collision archives, or disc keys. Generated game material
  must stay on the importing user's machine.

The separately frozen October 19 integration review compares that historical pin
through `41452f2d053fcfa679e33c00a73d120a53f59e61` against Project Prime
`5bc952147ce45f87d777ff0de4b2927f0c27ef9e`. See the
[SH-00 source/owner/gate ledger](second-hunt-integration-audit-2026-10-19.md).
The manifest preserves original PORT provenance and records this newer audit in
`integrationAudit`; it does not promote newer source recipes into supported imports.

## Exactly what the historical source offers

Playable identities: `darksamus` (Samus-equivalent simulation and separate
Echoes/Corruption native visual rigs) and `trooper` (Weavel-derived but NEW
wall-cling, Scout Drone, Pirate Blaster gameplay). Four Trooper looks are skins
of a single identity, not four character kits. Dark Samus is not a Samus skin.

Six Echoes multiplayer arenas: Sidehopper Station, Spires, Crossfire Chaos,
Pipeline, Spider Complex, Shooting Gallery. Each has a recipe to locate the
user-provided Echoes assets. Arena conversion builds new collision, entity
spawns, pickups, navigation, and HD presentation. Arena-specific movement and
interactions MUST remain independently testable.

Useful upstream references at the pinned SHA:
- `docs/CHARACTERS_AND_SKINS.md`, `src/MphRecomp.Core/Multiplayer/Characters.cs`
- `src/MphRead/Entities/Players/PlayerWallCling.cs`
- `src/MphRead/Entities/Players/PlayerScoutDrone.cs`,
  `src/MphRead/Entities/Players/PlayerDroneLaser.cs`
- `src/MphRecomp.Core/Arenas/EchoesArena.cs`,
  `src/MphRecomp.Core/Arenas/EchoesMovement.cs`
- `src/MphRecomp.Core/Import/Disc/GcDisc.cs`,
  `src/MphRecomp.Core/Import/Disc/DiscFileSystem.cs`
- `recipes/arenas/*.json`, `recipes/bodies/*.json`

## Project Prime integration invariants

1. Existing `Hunter` numeric IDs, original hunter balance, and vanilla
   character behavior are unchanged. Introduce a separate stable character ID
   in match setup and a locally resolved cosmetic ID; no enum renumbering.
2. Movement remains owner-reported through `IntentPacket.Position`; the dedicated
   server validates lifecycle/order and owns combat, health, score and world state.
   New drone/projectile, destructible and pickup outcomes require server validation.
   Never adopt the
   upstream peer/relay networking as Project Prime authoritative networking.
3. Imported skins are strictly draw-only and cannot change hurtboxes or physics.
   Clients missing a skin render a locally generated native MPH look-alike;
   no proprietary model/texture is transferred over the network.
4. Imported map collisions MUST match exactly across participating clients and
   the dedicated server. Missing Echoes assets are a join precondition failure,
   not an instruction to silently use different collision. Avoid publishing
   generated commercial game assets into Community map packages.
5. Stable map identity, namespace and content fingerprints must use Project
   Prime's existing map registration/package paths. Never replace historical
   room IDs, saved maps or replay asset references.
6. All new gameplay remains disabled unless the host explicitly enables it.
   Preserve legacy replay decoding, 60 Hz sim, ordinary hunter movement,
   vanilla maps and backward-compatible default rules.
7. Source import validates disc header, FST boundaries, recipe version and
   hashes before reading; it reads from a user-supplied file or extracted
   local tree, not from a network service. Failed imports leave active state
   unchanged. No import work on the render frame thread.
8. The menu must distinguish available/local, not imported, incomplete,
   incompatible source and unsupported compressed/edition formats.
9. Test Windows, Linux, Android, headless server, 1v1, four- and eight-player
   matches, offline bots, wrong-source rejections, interrupted import, replay
   playback/seek, deterministic damage and no local asset leakage.

## Reviewable PR sequence

- 00 inventory and asset-free acceptance gate (this change).
- 01 source import foundation: bounded, read-only GameCube FST and validation;
  expanded RVZ/WIA/Wii support only with actual regression fixtures.
- 02 Dark Samus vertical slice: identity, imported native visual rigs, fallback.
- 03 one Echoes arena to native Project Prime map runtime: Spires.
- 04 other five arenas, mechanics and map mode capabilities.
- 05 Echoes-only movement and authoritative dynamic map state.
- 06 Trooper body, selector and authoritative wall cling.
- 07 Scout Drone and Pirate Blaster authority, replication and replay support.
- 08 cross-platform device/performance and backward-compatibility signoff.

**Validation truth:** inventory/asset-free tests are not evidence that any new
character/arena is playable. Each later PR must report actual imports, builds,
tests and evidence instead of marking acceptance complete on metadata alone.


The October integration's separate Overcharged Echoes source candidate is documented in [SH-05B authoring evidence](second-hunt-overcharged-source-evidence-2026-10-19.md). It reserves MP2DarkSamusFinal under darksamus and exports the distinct Battle_3 source body through managed private receipts. That authoring evidence does not establish runtime acceptance. The later [offline body runtime](second-hunt-offline-body-runtime-2026-10-20.md) adds opt-in normal/Overcharged source bodies in local Samus matches through Hunters, with native gameplay and explicit native presentation fallbacks. Its current hardware acceptance is Windows x64; the normal [first-person Power Beam cannon](second-hunt-normal-cannon-runtime-2026-10-20.md) and [four Trooper offline body cosmetics](second-hunt-trooper-offline-body-runtime-2026-10-21.md) are subsequent runtime slices. Troopers use native Weavel gameplay; selecting a body does not enable practice abilities. The subsequent [Trooper fixed Power Beam cannons](second-hunt-trooper-cannon-runtime-2026-10-21.md) use each appearance's own source cut and original material programs. Animated guns, other weapons, Overcharged cannon, source balls, drone/ability presentation and multiplayer identities remain unfinished. The manifest's additive runtime sections record these scopes without rewriting historical provenance. No protected payload is shipped.
