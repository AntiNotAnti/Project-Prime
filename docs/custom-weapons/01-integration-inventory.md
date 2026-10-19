# CW-00 integration inventory

## Evidence boundary

This is a source audit of `AntiNotAnti/Project-Prime` at commit
`d726c766a10affaa0d8f95bc25bce9d178528215` (`origin/main` when inspected).
References below are one-based lines in that commit, inspected with `git show`
and `git grep`, rather than the unrelated dirty primary checkout. Paths beginning
with `Entities/`, `Mods/`, `Metadata/`, or `Formats/` are relative to
`src/MphRead/`. This document introduces no runtime behavior.

No build, gameplay, renderer, server, replay, Community service, mobile, or
performance acceptance was run for this inventory. A listed test/check is an
existing verification seam, not evidence that it passed during CW-00. The risk
ledger describes future integration obligations and does not claim them solved.

## Current protocol and stale documentation

The actual live protocol is **58**: `Mods/Network/NetProtocol.cs:2543`.
The preceding comment at line 2540 identifies the authoritative third-person
presentation rule as the reason for protocol 58. `ARCHITECTURE-INVARIANTS.md:9`,
`:58`, and `:214` still name 57. Its line 5 explicitly says current code and
tests outrank prose. Incompatible simulation or wire changes still require a
version bump and exact live-peer matching (`ARCHITECTURE-INVARIANTS.md:10`).

There is a second material disagreement: `ARCHITECTURE-INVARIANTS.md:74` says
the optional unscoped penalty does not halve Imperialist headshot damage.
`Entities/BeamProjectileEntity.cs:1920` actually divides **both** ordinary damage
and headshot damage by two when `Features.HalfDamageUnscoped` is enabled and
the shot was unscoped. Lines 1922–1924 explicitly describe this as vanilla
behavior. This audit records the implementation; changing that tuning is not a
documentation repair and must not happen accidentally during extraction.

Community schema/registry comments also lag implementation:
`Mods/MapGen/Sharing/CommunityContentSchema.cs:106` and
`CommunityContentRegistry.cs:81` describe non-map upload as future PR-09 work,
while `MapCommunityServer.cs:596` already implements the authenticated v3
revision upload route. Likewise schema line 48 calls dependency resolution future
work while `CommunityOptionalAssets.cs:49` implements a bounded resolver. These
comments cannot be used to infer that generic Community publishing is absent.

## Native weapon coupling

| Boundary | Source witness | Current contract / consequence |
| --- | --- | --- |
| Native identity | `Formats/Enums.cs:124` | `BeamType : sbyte`, player weapons 0–8, Imperialist 4, platform 9, enemy 10. These values already address native resources and are not an extensible custom-content namespace. |
| Native unlocks | `Formats/Enums.cs:141` | `WeaponUnlockBits : ushort` uses beam-indexed bits. |
| Mechanics selection | `Scene.cs:22` | Scene chooses global native multiplayer or campaign weapon rows. |
| Immutable mechanics / mutable equipment | `Metadata/Weapons.cs:133`, `:7` | `WeaponInfo` holds mechanics and native presentation IDs; `EquipInfo` holds charge, zoom, ammo delegates, projectile pool, and damage overrides. Equipment must not become shared mutable custom-definition state. |
| Inventory | `Entities/Players/PlayerEntity.cs:29`, `:251`, `:262` | `AvailableArray` has nine entries, weapon slots have three entries, effectiveness has nine entries. |
| Equip / affinity | `Entities/Players/PlayerEntity.cs:1490`, `:1549` | Equip applies native match gates and selects native affinity row using `+9`. |
| Gun animations | `Entities/Players/PlayerEntity.cs:1666` | Gun animation lookup indexes hunter, animation and current native weapon. |
| Source admission | `Mods/Network/NetShotProof.cs:146` | Source evaluator clamps weapon to 8 and indexes native/affinity mechanics; ammo/cooldown/input/modifier evidence follows at lines 163–204. |
| Fire transport | `Mods/Network/NetFireEvents.cs:12`, `:555` | Authored shot has a byte native weapon, and live validation rejects values above OmegaCannon. |
| Accepted emission | `Mods/Network/PlayerEntityAcceptedShot.cs:67` | Native weapon plus affinity offset selects mechanics for remote accepted playback. |
| Projectile presentation | `Entities/BeamProjectileEntity.cs:1299`, `:1539`, `:2843` | Draw-function dispatch, beam effect lookup and terrain-splat table remain native/indexed. |
| Muzzle presentation | `Entities/Players/PlayerInput.cs:1476` | Muzzle effects index current native beam. |
| Outcome transport | `Mods/Network/ReplayShotFact.cs:42`, `:123` | Result contains native byte weapon and validates against OmegaCannon. |
| Semantic events | `Mods/MatchEvents/MatchSemanticPackets.cs:11` | Weapon field is bounded -1..255, but consumers still cast it to native `BeamType`. |
| HUD / killfeed | `Mods/Render/PlayerEntityKillFeed.cs:166` | Icon lookup uses native beam index and explicitly caps at OmegaCannon. |

The multiplayer Imperialist baseline is `Metadata/Weapons.cs:2212`: body damage
72 and head damage 200 (lines 2238–2243), native cooldown 60 (2224), ammo cost
20 (2235), no spread/gravity (2263, 2277), one component (2286), fixed-point
speed and maximum distance 819200 (2257, 2274), draw function 8 (2216), and
collision/muzzle effect IDs 31/66 (2227–2228). These are source values, not newly
measured balance or timing results. Shot admission converts native cooldown to
60 Hz frames in `NetShotProof.cs:183`.

A custom definition needs identity independent of this native backend mapping.
Merely extending `BeamType`, replacing table rows, or borrowing Imperialist's
numeric ID would alias inventory, source proof, damage, attribution and replay.

## Accepted Imperialist shot: complete ownership chain

1. **Input and selection.** `Entities/Players/PlayerInput.cs:115` defines the
   nine-entry native cycle. Direct binds are handled from line 214, Imperialist
   at 242, affinity slot at 279 and scroll selection at 285. Stylus selection
   maps native controls at 155. Native zoom treatment includes the Imperialist
   FOV branch at 1147. These bindings select a weapon; they do not establish
   authoritative damage.
2. **Native emission attempt.** `PlayerInput.cs:1404` begins the fire event,
   starts `NetUnlagged`, creates timed proof, resolves modifiers and calls
   `BeamProjectileEntity.SpawnWithEmission`. Successful emission commits at
   1419; failed/exceptional emissions abort proof. Semantic `WeaponFired` and
   replay presentation follow at 1438. HUD firing follows at 1455, muzzle at
   1476, native shot audio and Imperialist reload at 1495–1498.
3. **Authored shot identity and evidence.** `Mods/Network/NetFireEvents.cs:12`
   carries ShotId, source frame, ACK frame/subframe, native weapon, charge,
   continuous phase, muzzle, projectile direction, aim, camera view, reticle,
   source life and fenced target. Current canonical size is 74 bytes (line 20).
   Noncontinuous scope/Prime/Double/affinity state is packed into phase bits
   (30–41); scope is explicitly Imperialist-only. Authoring at 696–738 captures
   that state, preserving original scope when later carriers recover a shot.
4. **Intake and deduplication.** `NetFireEvents.cs:346` accepts incoming facts
   into the accepted ledger. `AcceptedShotLedger.cs:45` distinguishes duplicate,
   conflicting, invalid, expired and overflowing admissions. `NetFireEvents.cs:415`
   drains accepted work; line 456 checks authority/match/generation fences and
   line 461 requires authored pose. Repeated carrier arrival is not a new shot.
5. **Accepted playback and source authorization.** `NetFireEvents.cs:481`
   activates the exact ShotKey, admits source proof, checks emission admission,
   resolves timed modifiers and calls `ModSpawnAcceptedShot`. Successful spawn
   marks proof fired and completes the timed certificate (505–511); refusal or
   exception aborts it (513–524). `NetShotProof.cs:143` owns native source
   mechanics, pose/muzzle, ammo, charge, cooldown and input checks; certificates
   retain their admission answer rather than resampling later pickups (220).
6. **Remote emission.** `PlayerEntityAcceptedShot.cs:45` intentionally bypasses
   rerunning input, inventory, cooldown, zoom, charge-release and respawn
   transitions. It chooses mechanics at 67, creates flight-owned `EquipInfo` at
   71, rewinds at 84 and spawns at 87. Successful emission creates semantic /
   replay presentation at 92 and shot audio at 109. Continuous/turret lanes have
   separate lifecycle checks and must not inherit a discrete hitscan shortcut.
7. **Collision and damage channels.** `Entities/BeamProjectileEntity.cs:725`
   recognizes an upright head by native height threshold. Imperialist has no
   short-range head restriction; other eligible beams require travel <=15 units
   (729–737). Channel interpolation uses head values at 740 and marks Headshot
   only when body/head values differ (751). Equipment channels are selected at
   1894, Double/Prime applied at 1899, original authored scope recovered at 1911,
   and optional unscoped halving applied at 1920. Projectile presentation calls
   `SpawnSniperBeam` at 995; Insta-Gib's Ion Fracture presentation is bounded and
   separate at 2928–2953.
8. **Authoritative payment and health.** `Entities/Players/PlayerEntity.cs:2108`
   supplies damage identity to networking; `Mods/Network/NetDamage.cs:596`
   allocates authoritative damage-event sequence, line 627 records the
   lifecycle/ShotId/component payment and line 632 publishes native beam/flags.
   `PlayerEntity.cs:2114` emits Headshot, 2203 emits Damage, and 2250 emits Death.
   Local prediction is not a durable score or death authority.
9. **Head/body reconciliation and rescue.** `Mods/Network/NetHitClaims.cs:1824`
   is explicitly Imperialist-specific. It adds only missing headshot damage,
   requires a live valid victim, applies timed bounds at 1848, and uses
   `ClaimScope(Imperialist)` plus `TakeDamage` at 1872. `NetTimedShotModifiers.cs:47`
   requires retained certificate, matching weapon, component and victim profile;
   exact Imperialist impact/head validation is at 68–76. Missing head evidence
   falls back to body classification, not speculative headshot credit.
10. **Confirmed semantic facts and HUD.** `Mods/MatchEvents/SceneMatchEvents.cs:113`
    emits authoritative Headshot. `MatchSemanticEvent.cs:17` fences event,
    match/epoch/tick and actor/target generation/life. `MatchSemanticPresentation.cs:46`
    turns canonical death into killfeed and confirmed headshot into audio.
    `NetDamage.cs:961` handles predicted nonlethal confirmation and only then
    supplies headshot HUD feedback at 969. `Mods/Combat/KillFeed.cs:131` consumes
    canonical events and casts weapon to `BeamType` at 149.
11. **Resolved replay evidence.** `PlayerEntity.cs:3113` records accepted
    `ReplayShotFact`; its schema at `Mods/Network/ReplayShotFact.cs:21` explicitly
    separates authored trigger/pose from the accepted damage result. It joins on
    shooter identity/ShotId and DamageEventId. Payload is v1, 61 bytes (57–58).
    `DedicatedServerReplayShots.cs:17` validates and distributes these facts.
    `ReplayPoseStream.cs:450` derives confirmed hit-marker flags, 519 merges
    sibling headshot outcomes, and 809 restores the authored native weapon.
    `MatchSemanticPresentation.cs:75` handles replay forward presentation and
    suppresses historical notifications on seek.

Custom identity must survive all eleven stages, including refusal, head/body
correction, claimed hits and replay seeking. A custom name on a native fire event
does not preserve accepted result attribution.

## Save data, achievements, bots, pickups and modes

| Surface | Source witness | Integration obligation |
| --- | --- | --- |
| Story saves | `GameState.cs:2050`, `:2065` | Two native ammo pools, three integer native weapon slots, ushort unlock mask; default native unlocks at 2096. Keep campaign/native save compatibility explicit rather than adding custom IDs to the mask. |
| Campaign pickups | `Entities/Players/PlayerProcess.cs:1700` | Pickup writes native unlock bit, name and HUD-message array indexes. |
| Weapon pickup grant | `PlayerProcess.cs:1661`, `:1682`, `:1714`, `:1752` | Insta-Gib/Gun Game/Chamber reject weapon additions; ItemType maps to BeamType, mechanics refill inventory, authoritative source journal records actual grant. |
| Spawn / pickup lifecycle | `Entities/ItemSpawnEntity.cs:99`, `:135`; `Mods/Network/NetPickupClaims.cs` | Timed authoritative pickup projection manages spawn identity/availability. Any custom pickup requires equivalent exact identity and grants, rather than a local inventory-only addition. The claims file is a subsystem entry point, not a line-specific claim about a new contract. |
| Resource replacement | `Mods/Multiplayer/WeaponResourceRules.cs:19`, `:25`; `ARCHITECTURE-INVARIANTS.md:223` | Native allowed-beam gates and deterministic room/spawn replacement. Preserve fixed hashing and simulation-RNG independence. |
| Gun Game | `Mods/Multiplayer/GunGameRules.cs:8`; `Entities/Players/PlayerModeLoadouts.cs:13`; `PlayerEntity.cs:2764` | Seven native-beam ladder, one equipped stage, cleared native inventory, infinite ammo, Imperialist precision final-stage condition. Custom ladder policy needs explicit definition, rather than incidental native mapping. |
| Other loadouts | `PlayerModeLoadouts.cs:28`, `:67` | Fiesta uses deterministic native selections; Chamber binds ammo cost, reward and fallback specifically to Imperialist/Power Beam. |
| Bot selection | `Entities/Players/PlayerAi.cs:2393`, `:2594`, `:2605`, `:7264` | Bot mechanics and selection use native rows/index conversion; several Imperialist-specific range/aim branches remain, including 7367. A custom weapon needs supported bot behavior or an explicit unsupported policy. |
| Bot pickup strategy | `Entities/Players/PlayerAiMapControl.cs:170` | Pickup ownership checks explicitly map native ItemTypes to native available beams. |
| Bot wire presentation | `Mods/Network/NetBotInput.cs:60` | Shock Coil target forwarding is native-specific. Bots remain dedicated-server-owned (`ARCHITECTURE-INVARIANTS.md:205`). |
| Match/career totals | `Mods/Network/MatchReportStats.cs:73` | Accepted semantic facts count shots, kills and headshot kills; replica/prediction/non-authority excluded. Custom mechanics must retain canonical accounting. |
| Achievements | `Mods/Launcher/Gui/Workspaces/LicenseWorkspace.cs:979` | Career milestones derive from accepted totals; Headhunter uses career headshots >=50 at 991. `HunterLicenseClient.cs:1054` stores aggregate headshots. No custom-weapon achievement registry was established by this audit. |
| Bot career eligibility | `ARCHITECTURE-INVARIANTS.md:208`; `Mods/Network/DedicatedServer.cs:2470` | Bot-assisted round flag is sticky and carried in roster; existing career suppression must survive custom matches. |

Existing checks worth extending later include
`Mods/Network/GameModeSceneCheck.cs:148` for every Gun Game stage,
`AdvancedMatchRulesCheck.cs:57` for native restricted loadouts, native fidelity
fixtures under `tools/fidelity-oracle/baselines/weapon.imperialist*`, and existing
shot/payment/pickup/replay suites. None were run for this report.

## Corpse, character, gun and skin presentation

`Mods/Cosmetics/PlayerCosmetics.cs:11` gives each player a
`DeathPresentationState`; line 29 observes confirmed health/life/generation.
`Mods/Cosmetics/Death/DeathPresentationRuntime.cs:17` activates on alive-to-dead
within the same life/generation, freezing position/facing/alt state (24–27).
It serializes state at 31, validates reads at 39, and gates visibility at 55.
This is presentation state, not a second damage or death resolver.

`Entities/Players/PlayerDraw.cs:147` uses frozen corpse position/facing/form and
allows a visible custom death through native HideModel. `PlayerCosmetics.cs:57`
draws selected death presentation with native biped fallback, a scoped material
submission and bounded particles; appearance is resolved through the current
cosmetic runtime (28, 59). A future persistent corpse appearance contract must
specify whether the death captures asset identity or resolves current appearance.

Authored skin/material paths exist for weighted, rigid and native draw submission:
`PlayerDraw.cs:688`, `:749`, `:818`. Context distinguishes viewmodel, biped and
alt form (698–700, 757–759, 823). Bright skins, cloak, frozen/damage and spawn
status precedence is enforced there and in `PlayerCosmetics.cs:44`.
`Mods/Cosmetics/CosmeticCatalog.cs:49` constructs built-in skin keys and ushort
wire IDs; line 77 resolves wire loadout values. These are not Community weapon
skin identities. Death state/checkpoints, gun models, character replacement
resource lifetime and native fallback all require separate presentation review.

## Community lifecycle

| Layer | Source witness | Current behavior |
| --- | --- | --- |
| Content kinds | `Mods/MapGen/Sharing/CommunityContentSchema.cs:9` | Supported kinds: map, texture, material, model, prefab. No weapon kind. |
| Immutable identity | `CommunityContentSchema.cs:52`, `:63`, `:80` | Dependencies pin SHA-256; project separates current/latest hash, owner and archive state. Maps project the existing v2 catalog at 125. |
| Upload validation | `CommunityAssetValidator.cs:17`, `:29` | Texture/model/descriptor byte budgets and type-specific validation; unsupported gameplay assets rejected at 50. No custom mechanics validator. |
| Revision registry | `CommunityContentRegistry.cs:87` | Verified bytes, immutable owner (111), expected parent conflict (120), published-dependency visibility (153), cycle rejection (171), object-before-pointer persistence (175). |
| Lifecycle methods | `CommunityContentRegistry.cs:268`, `:283` | Owner archive/restore and published-revision promotion exist in registry. Registry method presence does not establish a complete weapon UI/API. |
| Authenticated publication | `MapCommunityServer.cs:596` | v3 revision upload validates creator/id/mime, bounded body, SHA, type validation, then registration (650–654). Private staging is removed on completion/failure. |
| Archive / restore | `MapCommunityServer.cs:663` | Authenticated generic archive/restore routes. |
| Exact retrieval | `MapCommunityServer.cs:690` | Authorized immutable raw object route, SHA-based ETag and appropriate private/public caching. |
| Client APIs | `MapCommunityClient.cs:165`, `:215`, `:290`, `:337`, `:363` | Generic publication, archive/restore, typed catalog/project/revisions exist. |
| Dependency policy | `CommunityOptionalAssets.cs:12`, `:49`, `:91` | Only editor-only / optional-visual roles; bounded resolver rejects gameplay-required dependencies and gameplay-map redirection. |
| Asset cache | `CommunityOptionalAssets.cs:152`, `:189` | Separate immutable visual cache verifies exact SHA and does not install gameplay maps. |
| Actual native Discover | `Mods/Launcher/RmlUi/Pages/community/NativeCommunityLibraryReader.cs:43` | Reads map-project catalog, maps current/latest package into native Community entry. Generic backend kinds do not automatically appear here. |
| Actual Installed view | `NativeCommunityLibraryReader.cs:64` | Enumerates installed custom map definitions and exact installed-map registry, not weapons. |

Weapon Community work therefore requires kind/schema, bounded package validation,
immutable local install registry, exact acquisition, authoring/preview/publish,
discovery, update/revision/archive semantics, and runtime activation. It cannot
be obtained by adding a label to the existing map-only Discover view. Existing
optional-visual dependency policy must not silently authorize gameplay content.

## Risk ledger and unresolved integration decisions

These are source-backed risks, not observed runtime failures.

| ID | Risk / required decision | Source basis | Required later evidence |
| --- | --- | --- | --- |
| CW-R01 | Custom/native identity aliasing through indexed tables and native `+9` affinity rows. Keep custom definition identity independent of native adapter IDs. | Native coupling table above. | Native fidelity unchanged; custom identity survives equip, emission and attribution. |
| CW-R02 | Client-authored definition/pose/modifier becoming damage authority. Resolve immutable definition and capabilities on authority; keep original lifecycle and payment proof. | `NetShotProof.cs:143`; `NetTimedShotModifiers.cs:47`; invariants 31–75. | Rejected/missing/mismatched definitions cannot resolve damage; duplicate/reordered shots pay once. |
| CW-R03 | Hitscan custom behavior bypasses exact Imperialist head/body proof or native first-collision semantics. | `BeamProjectileEntity.cs:725`; `NetHitClaims.cs:1824`; timed modifier lines 68–76. | Head/body reconciliation, occlusion, range, scope, alt/turret target and missing-history cases. |
| CW-R04 | New wire/simulation semantics advertised as protocol 58 without migration. | `NetProtocol.cs:2543`; invariants 10. | Explicit protocol bump/layout checks and exact live-peer refusal; old replay adapters preserved. |
| CW-R05 | Replay resolves installed-current definition rather than recorded exact content. | `ReplayShotFact.cs:29`; invariants 111, 138–175. | Exact bootstrap/assets/checkpoints, silent seek, historical fallback/refusal, deterministic private replica. |
| CW-R06 | Fire carries custom identity while result, claim, semantic event, HUD or statistics lose it. | Native byte result schema, semantic casts and killfeed cap. | Authority-result identity and source identity remain joined across correction/rescue/replay. |
| CW-R07 | Optional visual Community role accidentally becomes gameplay dependency or bypasses exact match readiness. | `CommunityOptionalAssets.cs:91`; invariants 93–99. | Separate required gameplay identity and authority/client readiness; missing package blocks safely. |
| CW-R08 | Local install/publish cancellation exposes partial or mutable active definitions. | Community object-before-pointer persistence; invariants 97–99. | Staging, SHA, parent conflicts, leases, cancellation, uninstall/in-use and activation ownership. |
| CW-R09 | Native save/unlocks/mode ladders mutate through a custom backend ID. | Save/pickup/Gun Game table above. | Native saves and modes remain compatible; custom participation policy explicit. |
| CW-R10 | Bot selection/range/charge assumes a native weapon and becomes invalid or cheats. | `PlayerAi.cs:2393`, 2594, 7367. | Supported custom bot metadata/adapter policy; server-only AI and career suppression retained. |
| CW-R11 | Renderer/corpse/viewmodel assets outlive scene or alter collision, aim, simulation RNG or local profile authority. | Cosmetic draw contracts; invariants 103–107, 147, 185–199. | Scene-owned load/unload and replica separation; fallback presentation; unchanged hit geometry and 60 Hz simulation. |
| CW-R12 | Current prose silently changes existing gameplay during cleanup. | Protocol and unscoped-headshot disagreements above. | Correct documentation to source; native behavior changes require independent design/versioning/verification. |

CW-00 establishes the integration map. It does not certify complete custom
weapon support, cross-platform compatibility, service deployment, package safety,
runtime fidelity, performance budgets, or any CW-01–CW-16 implementation.
