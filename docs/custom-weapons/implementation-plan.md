# Project Prime: Custom Weapons Platform, Riftline M7 Sniper, Headshot Reactions, and Hybrid Ragdoll Physics

**AI-agent implementation specification**  
**Target repository:** [`AntiNotAnti/Project-Prime`](https://github.com/AntiNotAnti/Project-Prime)  
**Starting point:** latest verified `main` at execution time; inspect open/in-flight PRs before writing code  
**Document prepared:** 2026-10-18  
**Status:** proposed implementation plan, **not yet implemented or acceptance-tested**  
**First deliverable:** an entirely new, first-class custom sniper rifle called **Riftline M7**  
**End-state:** players can safely create, import, publish, install, and use custom weapons through Project Prime Studio and Community Hub, with authoritative multiplayer validation and replay compatibility.

> **Agent instruction:** Execute the PR sequence below, beginning with CW-00. Work against actual repository code, not assumptions in historical docs. Commit each independently reviewable slice, run its acceptance checks, document evidence, and move to the next slice only when the prior slice meets its gate. Resolve implementation details from inspected code and tests. Do not falsely mark unrun tests as passed or invent CI/device evidence. Do not refactor unrelated subsystems or silently replace vanilla mechanics. Do not declare the overall project complete until the final acceptance matrix passes.

---

## 1. Mission and non-negotiable requirements

Build an extensible, **data-driven custom weapon ecosystem** without destabilizing Metroid Prime Hunters gameplay. Validate it first with the Riftline M7 sniper rifle, then deliver headshot-specific animation, optional physical corpses, and the Community/Studio authoring and sharing workflow.

### Scope

1. Add a first-class custom weapon identity and registry distinct from native `BeamType` values.
2. Ship the Riftline M7 with custom firing rules, magazine/reload/bolt cycle, scoped input, damage and headshot behavior, first-/third-person presentation, sound and VFX.
3. Give all seven standard Hunters a convincing **nonlethal headshot flinch** and **lethal headshot collapse** using render-only bone/pose overlays.
4. Prototype and optionally ship a **hybrid animation-to-ragdoll** presentation for confirmed deaths. Physical corpses never become gameplay collision objects.
5. Establish a safe `.ppweapon` format and immutable package identity.
6. Add Community Hub `weapon` content lifecycle and a Project Prime Studio weapon creator. Use existing Community services rather than rebuilding uploader/auth/catalog infrastructure.
7. Enable custom weapons in opt-in custom matches after content and client compatibility checks; preserve ordinary/ranked/vanilla rulesets by default.

### Invariants (must remain true after every PR)

- **Vanilla safety:** Native `BeamType` numeric values, existing `Weapons.WeaponsMP`/`Weapons.Weapons1P`, cartridge assets, native weapon unlocks/slots, original game modes and behavior remain stable. A custom rifle must not masquerade as `Imperialist`, overwrite an unused enum value, or silently change affinity rows.
- **Authoritative combat:** Only the authoritative game simulation can resolve damage, headshots, ammo spending, reload legality, hit impacts, kills, and match rewards. Client visuals never author these facts.
- **Multiplayer correctness:** No host advantage, no double-pay/double-damage, no delayed-shot loopholes, no player-supplied damage numbers, no unbounded network or projectile allocation.
- **Replay safety:** Older demos, bookmarks, legacy checkpoints, and killcam flows remain readable. New recordings pin exact weapon definitions and present the same accepted shot/death facts. A missing custom package causes a truthful fallback or explicit unavailable state, never mislabeled native combat.
- **Compatibility:** Native-only sessions do not require custom weapon packages or new UI. Non-capable clients are refused entry to custom-weapon matches with a clear reason, not allowed to join with mismatched damage tables.
- **Security:** Community packages contain validated declarative data and assets only. No executable C#, scripts, native binaries, plugins, shaders supplied by creators, external URIs, unsafe archive paths or arbitrary file system access.
- **Performance:** Preserve a fixed 60 Hz authoritative simulation. Rendering may interpolate at higher display refresh. Corpse physics are client-local, budgeted, quality-gated, disabled headlessly, and isolated from gameplay transforms and collision.
- **Platforms:** Windows, Linux, Android. Do not regress macOS build compatibility where applicable. Maintain OpenGL/GLES paths; avoid introducing a second general-purpose renderer.
- **Clean architecture:** Prefer additive adapters and reusable services over broad changes to tightly coupled native weapon logic. Avoid duplicated combat simulation, shadow hit resolution or custom damage authority.
- **Rollback:** Every feature is default-off until its slice passes acceptance; each PR must have a reversible toggle/compatibility policy and cleanup guidance.
- **Licensing:** Do not commit cartridge-extracted assets or other unlicensed art/audio. Author original rifle artwork or use temporary clearly identified development primitives. Publishing UI requires creator licensing/ownership attestations and moderation/reporting hooks.

### Explicit non-goals for the first release

- No Unity migration, source-movement rewrite, new renderer, replacement of the main physics/character controller, or replacement of existing bot navigation.
- No scripting, custom code, third-party weapon DLLs, user-defined shaders, or network-controlled arbitrary visual logic.
- No change to native Imperialist balance, vanilla headshot hitbox geometry, native replay semantics, rated matchmaking, or permanent corpses with gameplay collision.
- No promise of bit-identical cross-platform ragdoll simulation without pose recording.
- No Community publication until security and multi-client match-preflight gates pass.

---

## 2. Current architecture: confirmed integration points and audit targets

The following paths were inspected on the repository's default branch while preparing this plan. Recheck them because the active branch can change.

| Area | Observed entry point | Why it matters |
| --- | --- | --- |
| Native weapon mechanics | `src/MphRead/Metadata/Weapons.cs` | `WeaponInfo`, `EquipInfo`, charge, damage, cooldown, speed, splash, effects, ammo and fixed native weapon tables |
| Native beam identity | `src/MphRead/Formats/Enums.cs` | `BeamType : sbyte` includes native IDs; `WeaponUnlockBits : ushort` is native-oriented |
| Native inventory | `src/MphRead/Entities/Players/PlayerEntity.cs` | `AvailableArray` currently holds nine entries; `_weaponSlots` has three native `BeamType` slots; `BeamEffectiveness` is fixed size |
| Scene weapon table | `src/MphRead/Scene.cs` | `WeaponRules` exposes native single/multiplayer arrays |
| Projectile implementation | `src/MphRead/Entities/BeamProjectileEntity.cs` | Projectile physics, hit position, damage/headshot, derived shots and launch/authority identities |
| Network accepted fire | `src/MphRead/Mods/Network/PlayerEntityAcceptedShot.cs` | Explicit native ID range check through Omega Cannon and native `WeaponRules` indexing |
| Shot validation | `src/MphRead/Mods/Network/NetShotProof.cs`, `NetShotSourceJournal.cs` | Evidence, source inventory, ammo/cooldown history, payment, component identity and anti-cheat constraints all currently assume native mechanics in places |
| Network contracts | `src/MphRead/Mods/Network/NetProtocol.cs`, `SessionProtocol.cs`, `src/MphRead.Protocol.Generator/` | Packet formats, protocol versioning, session admission; use current value from source, not historical docs |
| Weapon selection | `src/MphRead/Mods/Input/GamepadActions.cs`, `WeaponSelectionDirection.cs` | Native fixed weapon bindings, six-sector arc and controller selection |
| Gameplay restrictions | `src/MphRead/Mods/Multiplayer/WeaponResourceRules.cs` | Insta-Gib/No-Imp native restrictions, weapon pickups; custom rules need independent legality checks |
| Third-person animation | `src/MphRead/Entities/Players/PlayerEntity.cs`, `PlayerDraw.cs` | DamageFront/Back/Left/Right, split biped lower/upper poses, `Head_1`/`Spine_1` nodes and draw-rate sampling |
| Replay reactions | `src/MphRead/Mods/Replay/PlayerEntityReplica.cs` | Damage history drives directional animations; authority death has a distinct presentation path |
| Canonical combat facts | `src/MphRead/Mods/MatchEvents/SceneMatchEvents.cs` | Headshot/kill events and source/victim life identities |
| Impact presentation | `src/MphRead/Mods/Network/NetConfirmedImpacts.cs`, `ConfirmedImpact.cs` | Optional cosmetic impact lane currently assumes native weapon ID range; does not own gameplay |
| Death cosmetics | `src/MphRead/Mods/Cosmetics/Death/DeathPresentationRuntime.cs`, `src/MphRead/Mods/Cosmetics/PlayerCosmetics.cs` | Existing procedural death poses, cosmetic visibility, replay/respawn lifecycle; not skeletal ragdolls |
| HD model loading | `src/MphRead/Mods/Render/Characters/CharacterModelPack.cs`, `CharacterModelRuntime.cs` | Native bone mapping, RigidNodes/Weighted4 GLB replacements, viewmodel path |
| Community content types | `src/MphRead/Mods/MapGen/Sharing/CommunityContentSchema.cs` | Currently `map`, `texture`, `material`, `model`, `prefab`; weapon kind is not yet defined |
| Content validation | `src/MphRead/Mods/MapGen/Sharing/CommunityAssetValidator.cs` | Existing typed, non-executable asset intake; needs weapon-specific archive/manifest validator |
| Catalog and publishing | `CommunityContentRegistry.cs`, `MapCommunityClient.cs`, `MapCommunityServer.cs` | Immutable revisions, object storage, authentication, visibility, lifecycle and published assets |
| Community architecture | `docs/architecture/community-hub-pr08-typed-content.md`, `community-hub-pr09-publishing.md`, `community-hub-pr14-cutover.md` | Historical v2 map identities, v3 typed publishing, safe cutover and incomplete runtime acceptance |
| Automated headshot testing | `.claude/testing/HITRIG.md`, `src/MphRead/Mods/Network/HitRig.cs`, `tools/hitrig/` | Long-range and moving-target latency checks; original head region is narrow |
| Representative custom firing precedent | `docs/gameplay/balanced-battlehammer-burst.md` | Three-round authoritative multi-component firing and replay compatibility decisions |
| Required project rules | `ARCHITECTURE-INVARIANTS.md`, `CLAUDE.md` | Re-read before implementation; code and tests outrank dated prose |

**Important current observation:** `PlayerEntityAcceptedShot` rejects weapon IDs beyond `BeamType.OmegaCannon`, `NetShotProof` has native-index assumptions, `AvailableArray` holds nine entries, and `WeaponInfo.Name` indexes native weapon names. Increasing an enum value or appending to a native table does **not** make custom weapons safe. This is a distinct feature boundary, not a one-line data-table extension.

**Important protocol caution:** Some repository documentation cites historical protocols 41, 53 or 56, while newer tests describe later protocol behavior. Do not hardcode a version from this document. Inspect `NetConfig.ProtocolVersion` and wire tests on the actual base SHA; version intentionally when modifying packet contracts.

---

## 3. Required target architecture

```text
[Native Metroid Prime Hunters weapons]          [Custom definitions]
 [BeamType, WeaponInfo, original arrays]         [ID + revision hash, data-driven]
                  |                                  |
                  +----- WeaponResolutionService ----+
                                   |
                            WeaponRuntimeRef
                                   |
          +------------------------+-----------------------+
          |                        |                       |
       Loadout / UI         Gameplay command           Presentation
          |                 / shot identity               |
          |                        |                  Model + VFX + SFX
          |                 Authority admission            |
          |                        |                       |
          |                 Damage + hit facts ------------+
          |                        |
          +------------- Replay / killfeed / bots ---------+
                                   |
                    Frozen match weapon manifest
                                   |
                Verified Community package identities
                                   |
           Creator Studio -> local .ppweapon -> Hub publish
```

### 3.1 Core domain types

Implement names adaptively to project conventions, but preserve the following semantics:

- **`WeaponIdentity`**: immutable, persistent identifier. `Native(BeamType)` or `Custom(contentId: Guid, revisionHash: SHA-256)`. Include a developer-bundled ID for the Riftline M7, versioned by immutable definition digest. Do not conflate creator project ID and immutable package revision.
- **`WeaponRuntimeId`**: compact match-local identifier assigned by an authoritative, frozen loadout/manifest map. The ID may be a ushort if tests justify it; it is **never** a stable save/replay/network identity without the corresponding manifest.
- **`WeaponDefinition`**: immutable, validated typed record for custom firing, magazine, damage, headshot classification policy, zoom, recoil, rendering, audio, behavior presets, resource limits and version.
- **`WeaponRegistry`**: scene/session-scoped resolver that exposes native wrappers and custom definitions without mutating `Weapons.WeaponsMP`/`Weapons1P`.
- **`WeaponRuntimeState`**: per-player state: equipped identity, magazine count, reserve, chamber/bolt, reload stage, accepted shot epoch/frame, selected optic, and cancellation conditions. Distinct from vanilla `_ammo` and `_availableWeapons`.
- **`WeaponBehavior`**: allowlisted engine-implemented firing behaviors (semi-auto, burst, automatic, charge, homing, ricochet, projectile, hitscan-style near-immediate simulation, optional direct/splash). Creator data selects and configures these behaviors; it cannot execute user scripts.
- **`ResolvedShotDefinition`**: immutable authoritative per-shot values copied from the frozen manifest. Clients cannot choose damage, multipliers, radius, cooldown, bone positions or projectile physics.
- **`CustomWeaponMatchManifest`**: ordered, immutable roster of each allowed weapon, exact definition/package hashes, dependency hashes, deterministic runtime ID mapping and schema/engine-feature version.

**Implementation strategy:** Keep the legacy `BeamType` lane untouched. Add a separate custom-weapon lane where needed, then factor *shared, proven* post-admission damage/impact/attribution functionality. Do not instantiate fake native beam identifiers or cause two independent resolution systems to pay the same hit.

### 3.2 Native vs. custom transport strategy

Use a clear protocol design decision record (ADR) before mutating wire types.

- Preserve existing native `FireEvent` serialization and any legacy demo wire layouts bit-for-bit for native-only matches.
- Introduce a **versioned custom-weapon capability**, authoritatively advertised by the server/room; reject unsupported joins into custom sessions.
- Define a bounded `CustomWeaponAction` or equivalent typed extension for equip, fire, scope and reload input, using the same match/authority/slot generation/life and shot identity model as native events. Never rely on an unchecked byte cast to `BeamType`.
- Preserve the original source-frame, aim ray, launch, ownership and acceptance ordering guarantees. Bind custom weapon runtime ID to an exact frozen definition digest before accepting a shot.
- Authority must validate cooldown, magazine, reload eligibility, scope legality, shot source pose, ammo resources, occupancy, damage/flags, lag compensation window and duplicate/reordered fire requests.
- Reuse existing payment, authoritative damage and resolved hit facts where the type system supports it. Where original APIs are `BeamType`-only, extend their presentation/analytics adapter with a discriminated identity instead of leaking custom IDs into native tables.
- Add reliable manifest/readiness sequencing and bounded resend/ack policy. No custom shooting or loading state is admitted before both peers agree on the weapon manifest for this match.
- Version packet shapes, replay serializers, packet generator tests and compatibility rules intentionally. Produce byte-fixture tests and document old-client behavior.
- Snapshot the actual custom ammo/reload state, with exact per-life transitions; ensure spectator, late join and reconnect can reconstruct it without replaying every old fire event.

### 3.3 Resource ownership

- Definition/package ownership: Community installer/cache, exact SHA-256, immutable on disk.
- Match registry ownership: session-scoped and frozen before Ready; dispose on match/scene exit.
- Weapon state ownership: authoritative player simulation; predicted UI state is an explicitly labeled client projection reconciled by accepted snapshots.
- Projectile ownership: existing scene/projectile lifecycle with shot/component source IDs and maximum pool capacity.
- Art/audio ownership: scene-owned GPU/audio caches; predictable load/prewarm on match start and deterministic disposal on leave/context loss.
- Corpse physics ownership: render-only presentation subsystem; no server/headless allocation, gameplay query registration or authoritative state mutation.
- Creator/edit-time assets: private Studio workspace until validated and published; never loaded directly into another player's online runtime.

---

## 4. Riftline M7: first built-in custom weapon specification

**Working product ID:** `prime.riftline-m7` (developer-owned logical ID; map it to a distinct immutable definition digest).  
**Category:** Precision magnetic sniper rifle.  
**Role:** High-risk/high-reward long-range pick weapon, visually distinct from the Imperialist.  
**Initial game mode:** Offline/custom-only, with server-authoritative custom matches after CW-04/CW-12; **not** vanilla or rated by default.

### 4.1 Proposed tuning values (initial test values, not final balance)

| Parameter | Prototype |
| --- | --- |
| Fire mode | Semi-auto, single projectile, mechanical/electromagnetic bolt cycle |
| Body damage | 65 HP |
| Headshot damage | 150 HP |
| Magazine capacity | 4 rounds |
| Starting reserve | 16 rounds |
| Bolt/shot cadence | 75 authoritative ticks (1.25 s at 60 Hz) |
| Full reload | 138 authoritative ticks (2.30 s at 60 Hz) |
| Optics | 2.5x and 4x presets, scope held/toggled using existing control settings |
| Hip-fire | Allowed with visibly lower accuracy; exact spread fixed by manifest |
| Scoped fire | Precision, no aim-assist shortcut; align to existing scope/mouse sensitivity options |
| Projectile model | Very fast, finite near-hitscan precision round; reuse proven trace/rewind geometry where possible |
| Splash/affliction | None in MVP |
| Penetration | Off in MVP; later independently validated behavior preset |
| Movement penalty | None in MVP unless an explicit match rule is approved |
| Reload policy | No firing during reload; reserve decremented only by authority; swaps/interruption rules specified below |
| Alternate ability | None in MVP; schema reserves capability-safe extension fields |
| Headshot shape | Existing authority headshot region for v1; do **not** substitute render head bones for hit classification |

**Why this tuning:** The rifle must reward headshots, but a full-health target should survive a typical single body shot. Establish distinct scope/sound/reload identity before adding armor penetration or alternate fire.

### 4.2 Reload and fire-state machine

```text
Uninitialized / NotOwned
        |
      Equip
        v
      Ready --FireAccepted--> BoltCycling --75 ticks elapsed--> Ready
        |                          |
        |                          +--- No magazine ammo ---> Empty
        |
        +-- ReloadRequest --> Reloading --138 ticks elapsed--> Ready / Empty
        |
        +-- Ammo==0 -------> Empty --ReloadRequest and reserve>0--> Reloading
```

Additional mandatory rules:

- A fire must pay exactly one magazine round **once** per accepted shot and create **at most one initial projectile**. Denied shots cannot debit ammo or create replay/kill evidence.
- Client-local button repeats at 120/240+ Hz never increase fire rate. The authority uses simulation frames, not presentation frames.
- Server owns current magazine, reserve, fire-ready tick, reload tick and accepted last-shot identity. Duplicate/replayed events cannot refill/restart/recharge.
- Prefer **preserving reload progress only if existing weapon state policy explicitly supports it**. MVP decision: swapping cancels reload, does not grant rounds, and a new reload takes a full 138 ticks. Document the exact point where transfer of reserve to magazine occurs, ideally **on accepted completion**, not start.
- Reload transfers `min(4 - magazine, reserve)` exactly once on completion, with reserve reduced by that amount. A partial magazine is legal.
- Taking damage does not cancel reload unless specified separately; death, respawn, despawn, form transitions (if prohibited), match reset and server rule changes always invalidate pending reload states.
- Choose one policy for aiming during bolt/reload; no hidden bypass by switching optics mid-cycle.
- Server denies clients specifying unsupported optic, invalid weapon identity or impossible target/owner life.

### 4.3 Aiming, hit detection and recoil

- Derive scoped FOV using lens magnification (`FOV' = 2 atan(tan(FOV/2)/magnification)`) rather than dividing FOV linearly.
- Align near/far reticle, mouse scoped X/Y sensitivity, controller settings and mobile touch sensitivity to the existing aim path. Scope is presentation/aim input, not a source of unvalidated extra damage.
- Use existing shooter-pose launch origin and server lag-compensation evidence; use an accepted impact point for trail/particle alignment.
- Use recoil on the *presentation* camera/viewmodel (bounded angular kick, quick recovery). If accuracy bloom is part of game rules, author it as a separate server-validated value, never infer it from renderer motion.
- Draw bright muzzle flash and fast tracer only after accepted shot/allowed local prediction; reconcile canceled/missed predictions cleanly.
- Keep default aim assist policy explicit (ideally no assist for this custom precision weapon unless a game-wide accessibility rule requires it). Never sneak in new magnetism.

### 4.4 Art and animation assets

Deliver original, license-cleared assets:

- First-person M7 viewmodel (idle, raise/lower, fire, bolt cycle, reload, scoped idle).
- Third-person weapon mount/attachment compatible with all seven Hunters, including Spire/Trace and left/right handed poses as needed.
- LOD0/LOD1 geometry, UVs, materials, emissive rail, scoped glass and mobile fallback.
- Precision reticle and scope vignette, ammo/reload HUD icon, muzzle flash, tracer, hit spark, charge/battery soundscape and mechanical bolt sounds.
- Define attachment sockets in source authoring (muzzle, eject/vent, hand/grip, left-shoulder). When necessary, map to validated native skeleton nodes instead of requiring new gameplay joints.
- If a long, handheld rifle requires two-handed poses that cannot be safely retargeted to all native arm-cannon skeletons, ship the MVP as an **arm-mounted precision rifle module**, with explicit later handheld animation work. Do not accept visibly floating hands or corrupt third-person muzzle transforms.
- Interpolate weapon visuals at display rate, but restore canonical bone/weapon emitter matrices after every draw. Prevent camera/weapon ghosting above 60 Hz.

### 4.5 Balance and playtest gates

- Confirm 65 body / 150 headshot baseline in a clean 100-HP test, accounting for modifiers and spawn protection.
- Compare body-time-to-kill, headshot opportunity, magazine economy, zoom acquisition and missed-shot recovery against the Imperialist, without changing native Imperialist values.
- Test open sight lines, moving targets, vertical jump pads, 8-player FFA and duel modes, melee-range hip shots, wall occlusion and high ping.
- If the rifle dominates, change definition data with a new immutable revision and new test baseline, not an untracked runtime constant.

---

## 5. Dedicated headshot reaction system

### 5.1 Existing boundary

The game records `DamageFlags.Headshot` and has native directional body damage animations. The headshot rig documents a narrow authority hit region near the top of the collision cylinder (`MaxPickupHeight - 0.3`), not an articulated physical helmet hitbox. **Do not change the competitive hit test while introducing a visual reaction.** A headshot event identifies the accepted gameplay classification; it does not prove the exact geometry of the rendered visor.

### 5.2 New presentation contracts

Add a `HitReactionFact`/`DeathImpactFact` presentation model derived exclusively from authoritative damage facts or replay records:

- victim slot/generation/life, shooter/source identity when available;
- accepted impact class (`Body`, `Head`, `LethalHead`, `LethalOther`);
- accepted impact direction and authoritative point if valid;
- server tick / damage event sequence / match / authority epoch;
- stable animation variant seed derived from facts, **not** client gameplay RNG;
- weapon identity/manifest reference if custom;
- per-victim deduplication and expiration bounds.

Prefer extending an existing accepted damage event in a versioned, compact and tested manner if it already supplies all information. Do not add a second conflicting death author or trust client claims to choose `LethalHead`.

### 5.3 Nonlethal headshot reaction

- Implement render-only additive offset to `Head_1`, `Spine_1`/upper torso and shoulders when present.
- Blend on top of ongoing walk/run/jump/strafe/aim poses; preserve native lower-body locomotion.
- Apply directionally signed head pitch/yaw, helmet recoil, shoulder response and recovery using clamped curves.
- Prototype timing: anticipation ~1-2 rendered frames, peak ~90 ms, settle within ~240 ms; cap head rotation (for example 10-15 degrees) and upper-body deviation so there is no rubber-necking.
- Ignore or coalesce rapid repeated hits within bounded windows; respawn/morph/teleport cancels stale overlays.
- Preserve weapon muzzle/aim/gameplay nodes: save presentation transforms, apply overlay only while submitting visual geometry, then restore canonical transforms even on exceptions.
- Provide optional reduced-motion setting and respect current cosmetic/performance quality without eliminating necessary damage feedback.

### 5.4 Lethal headshot reaction

- On an **accepted lethal headshot**, trigger a distinct short reaction: helmet snap, torso recoil, legs buckle, directional collapse.
- Prototype animation duration before physics blend: 100-200 ms. Deterministic procedural variant from accepted seed.
- Priority: death always overrides nonlethal flinch; killcam/replay must not play both in the wrong order or double trigger on reconciliation.
- Link to the existing death-cosmetic system via an explicit priority policy. Suggested defaults: special user-selected vanish/dissolve death effects remain authoritative for visibility; headshot overlay may run briefly before the cosmetic takes over. Ragdoll suppressed for effects that intentionally remove the body.
- Preserve classic death particles, killfeed, confirmed headshot audio and HUD notifications without duplicates.
- Additional headshot impact VFX are strictly cosmetic, depth-tested and distance/quality-limited.

### 5.5 Acceptance

- Every standard Hunter, skins and LOD levels, moving/jumping/morphing/frozen states, spectator, first-person, replay and killcam.
- A nonlethal headshot changes no HP beyond the accepted damage, hitbox dimensions, velocity, look direction or weapon firing origin.
- Headshots denied by the authority produce no confirmed head reaction (predicted temporary presentation is allowed only if fully reversible and never mislabeled canonical).
- Delayed/duplicate/out-of-order damage facts do not trigger a reaction on a new life or different slot occupant.
- Replay seek and repeated playback do not accumulate bone offsets or duplicate audio.
- Visual smoothness at 60/120/144/240 Hz; fallback on missing bone mappings.

---

## 6. Hybrid ragdoll physics design

### 6.1 Recommendation and feasibility decision

Use a **hybrid** approach: accepted lethal event -> short authored/procedural death pose -> constrained skeletal corpse physics -> fade/despawn. The corpse is a *client-local visual replica*; authoritative player collision continues using existing original rules, and the dead player's gameplay entity does not become a simulated rigid body.

Candidate library: [bepuphysics v2](https://github.com/bepu/bepuphysics2), a C# rigid-body engine with constraints, triangle meshes and a ragdoll example. Its current upstream README reports .NET 8 targets and Apache-2.0 licensing. Treat Android AOT and target-platform packaging/perf as **unverified until CW-09**. Compare a minimal built-in kinematic/procedural fallback and choose based on measured results, not library branding.

### 6.2 Implementation contract

Create an optional `IRagdollPresentationBackend` and an explicit `ProceduralDeathFallback`. The backend must accept an immutable `CorpseSpawnDescriptor`, including:

- Hunter/model identity, death pose, world transform, optional alt form, accepted impact vector, optional point, frozen match/life context and deterministic presentation seed;
- render mesh and validated native bone mapping; corpse collision shape profile by Hunter/body part;
- lifetime, quality preset, spawn tick and optional selected death cosmetic.

Physics must not own or modify `PlayerEntity.Position`, velocity, health, `PlayerVolumes`, projectile queries, pickup queries, teleport triggers, gameplay `ModelInstance` matrices, or server snapshot state. The render path can sample a **separate corpse pose palette** only.

### 6.3 Rig and collision constraints

- Initial target: **12-17 rigid segments** (pelvis, torso, head, upper/lower arms, thighs/shins, optionally feet), using capsules/boxes with a stable mass ratio and conservative joint angles.
- Derive starting positions from the *rendered* death pose; avoid snapping at animation-to-ragdoll handoff by matching root/limb transforms and initial velocities.
- Define per-Hunter shape maps with fallback for unusually proportioned Hunters. Joint limits, damping, gravity, restitution and terminal velocities must be finite and bounded.
- Use collision against a validated read-only world mesh/static geometry subset. No collision against live players, active weapon projectiles or gameplay-owned dynamic platforms initially. Add moving-platform visual contact only if the attachment is provably safe and budgeted.
- Enable sleeping after settling. Avoid tunneling/NaNs, over-constrained jitter, explosive impulses and infinite falls. Limit number of collision contacts and solver iterations.
- Alternate forms get distinct procedural/fx fallbacks by default; do not force humanoid ragdolls onto Morph Ball, Spire alt form, Weavel detached turret, or others without authored rigs.

### 6.4 Presentation and lifetime policies

- High: up to 8 visible corpses in a scene; 12-17 segments, highest quality collision.
- Medium: up to 4, simpler shapes/reduced solver work.
- Low/mobile: up to 2 or a short baked collapse; optionally physics disabled if device fails perf/thermal acceptance.
- Off: existing original procedural death/effect only.
- Initial target lifetime: 2-4 seconds, shorter if the existing death lifecycle or match camera requires it.
- Retire by oldest/farthest, with pooling and explicit event counters. Stop/cleanup on scene unload, match reset, respawn, replay seek, graphics-context loss and shutdown.
- Fixed physics step independent of display-rate sampling; render interpolation only. No frame-rate-dependent joint impulses.
- A spectator/replay may use the same accepted seed for approximate behavior, but **cross-platform bit-exact limb paths are not promised**. If exact cinema replay is required, later add a bounded pose-keyframe track with explicit versioning and size budgets.
- Use a `show_ragdolls` toggle; debug overlay may show bodies, constraints, collision proxies and lifecycle statistics in developer builds.

### 6.5 Benchmark gate before production

Prototype on real target hardware and compare with feature off:

- 1, 2, 4 and 8 simultaneous ragdolls; 8-player crowded FFA; static world collision complexity; repeated respawns and map rotation.
- Render/physics CPU cost, GPU frame pacing, allocation rate, retained buffers, asset residency, scene-exit memory, Android AOT/thermal and crash behavior.
- Suggested provisional gate: p95 frame-time delta <= 1 ms on target desktop and <= 2 ms on supported mobile **or** an evidence-backed budget approved from baseline, no meaningful sustained allocation growth and no gameplay simulation delta. Do not use these provisional thresholds as proof before collecting baselines.
- If the library cannot meet Android or compatibility gates, ship desktop opt-in only with a procedural fallback, and keep the backend experimental; do not block the custom weapon feature on it.

---

## 7. Custom weapon package and Community integration

### 7.1 Format and identity

**Proposed authoring artifact:** `.ppweapon`, a bounded immutable ZIP package with `manifest.json`, compiled/validated declarative weapon definition and permitted visual/audio files or exact pinned Content v3 dependencies. The extension and format version are new; do not confuse them with the existing `.ppmap` format.

A published package is identified by the **SHA-256 of exact archive bytes**. A distinct normalized **gameplay definition digest** is computed from canonical mechanics fields and engine behavior schema, excluding mutable listing metadata and irrelevant ZIP timestamps. Store both. Never let a display name become the security identity.

Example **illustrative** manifest (the agent must formalize, validate and test the production schema before using it):

```json
{
  "format": 1,
  "kind": "weapon",
  "contentId": "00000000-0000-0000-0000-000000000000",
  "name": "Riftline M7",
  "minEngineFeature": "custom-weapons-v1",
  "mechanics": {
    "behavior": "semi-auto-precision",
    "damage": { "body": 65, "head": 150 },
    "magazineCapacity": 4,
    "initialReserve": 16,
    "shotCooldownTicks": 75,
    "reloadTicks": 138,
    "opticMagnifications": [2.5, 4.0],
    "splash": false,
    "piercing": false
  },
  "assets": {
    "viewModel": "models/riftline_view.glb",
    "worldModel": "models/riftline_world.glb",
    "icon": "ui/riftline.png",
    "shotSound": "audio/riftline_fire.ogg"
  },
  "dependencies": []
}
```

The zero UUID is **only a documentation placeholder** and must never be accepted for publishing. Production content IDs come from the existing creator-owned catalog. The example deliberately omits archive digest fields, since these are assigned by the builder and must not recursively hash themselves.

### 7.2 Package validator: fail closed

Use existing Community typed validators where possible; create a **dedicated weapon gameplay schema validator** that never trusts a file extension or uploader-provided MIME. Required controls:

- Exact magic/version, strict JSON schema and unknown-field policy, canonical serialization/normalization, finite numeric values only, integer tick bounds, valid enums and safe cross-field combinations.
- Archive entry count, compressed/uncompressed size, expansion ratio, path length, file count, metadata depth, string length, texture dimensions, triangle count, clip duration and audio decoding budgets. Suggested first engineering caps to benchmark: package archive <= 64 MiB, expanded assets <= 128 MiB, <= 64 entries, <= 2 MiB manifest; reuse stricter existing caps when applicable. These are provisional, not an invitation to increase global Community budgets.
- Reject absolute/traversal paths, duplicate normalized entries, case-colliding names, symlinks, nested archives, unsupported compression methods, executable extensions, external URIs, remote model buffers, dangerous glTF extensions and shaders.
- Validate all referenced files exist, are unique and match declared content hashes. Reject dependency cycles, unresolved exact versions and duplicate runtime IDs.
- Original models/animations: glTF 2.0/GLB subset, bounded mesh/skin/animation support, no runtime extension execution. A lightweight authoring-side conversion step may produce engine-owned, validated runtime-ready assets.
- Sound: decode into bounded engine-supported PCM/streamed assets using allowlisted codecs; protect against decompression bombs/absurd channel/sample-rate/duration values.
- Server treats all mechanics from package as **untrusted until validated and frozen**. It never loads full graphics/audio assets to decide combat.
- Asset-free headless package admission must work; graphical asset decode failures show fallbacks and never change gameplay stats.
- Quarantine malformed/unsupported packages with a precise error; never partially promote or install active gameplay content.
- Report package identities and capability errors without leaking local file paths, creator auth tokens or private upload URLs.

### 7.3 Mechanics budget

Choose initial safe limits in a versioned policy and test boundaries:

- Damage, projectile velocity, hit radius and fire cadence min/max; reject ultra-high fire rate that floods authoritative events.
- Max initial/derived projectiles per shot, max active projectiles per player, maximum lifetime/distance/splash targets and homing/ricochet counts.
- Bounded ammo/reserve and refill rules; no negative costs, negative reload time or infinite recursion via effect chains.
- Only enumerated effect/affliction/behavior presets. No creator-definable damage callbacks, movement impulses beyond server caps or authoritative teleport hacks.
- Custom damage and weapons default off in ranked/vanilla; server can disable specific presets and enforce match-specific budgets.
- Definition hash includes relevant behavior-implementation version so rule semantics cannot silently change across a build.

### 7.4 Studio authoring workflow

Build into **Project Prime Studio**, not the game runtime's main settings page:

1. **New Weapon:** choose template (precision, automatic, shotgun, beam, charged), name and create project ID.
2. **Model:** import original GLB, set first-person/third-person transforms, muzzle socket, LOD, materials and scale. Preview with all Hunters where possible.
3. **Mechanics:** declarative editor for damage, cadence, ammo/reload, spread, velocity, splash, zoom, allowed preset behaviors, mode availability.
4. **Animations and sound:** import supported clips/sounds, map to `Idle`, `Fire`, `Bolt`, `Reload`, `Equip`, optional `Scoped` and impact cues.
5. **Preview/Test:** local test arena sandbox, dummy targets and bots, recoil/hit markers, headshot/body damage logs, zoom test, projectile/impulse visualization, simulated latency in dev mode.
6. **Validate:** present schema errors, budget failures, dependency and missing-asset diagnostics, no silent corrections that change damage.
7. **Build `.ppweapon`:** canonical package/archive hash and definition hash, local install and smoke test.
8. **Publish:** creator authentication, thumbnail, description, tags, license/ownership attestations, public/unlisted/draft visibility, version history and moderation/report link.
9. **Update:** create immutable new revision, never replace historical bytes; pinned sessions/replays keep the old version.

MVP Studio should start as a **developer-focused JSON editor/preview** if graphical tooling would delay the rifle. The later user-facing creation workflow must be approachable without hand-editing JSON.

### 7.5 Community Hub integration

The v3 typed content catalog currently knows `map`, `texture`, `material`, `model`, `prefab`. Extend it with a distinct **`weapon` kind** using additive migrations and capability advertisement. Do not create a second independent user/catalog/auth subsystem.

- Reuse creator identity, upload lane, immutable `contentId` + revision hash, visibility, search/filtering, owner permissions, archive/revision history, moderation, downloads and retry infrastructure.
- Weapon archive upload is a **new validated type**, not an unconditional use of generic model/descriptor validation. Use content-type-specific archive and gameplay validation before publication.
- Reuse exact immutable dependency lockfiles for shared models, textures, materials and audio when the existing dependency graph supports them; otherwise embed only validated assets in v1 and extend dependencies separately.
- Downloads/install run before match Ready. The hosted child/dedicated authority must have the exact validated **mechanics definition** before accepting a custom match. Clients must have corresponding presentation assets or an explicitly supported fallback.
- Never let a joining client provide arbitrary code, local paths or a remote URL that the server fetches directly. Downloads use the trusted operator-configured Community service and verified content hashes.
- Preserve all historical `.ppmap` hashes and map catalog v2 projections; new content kind must not remap existing kinds, map Ready rules or asset ownership.
- Verify that uninstall refuses removal of active/pinned content or defers safely. Quota and corruption recovery must be explicit.
- Search/discover/install UX should integrate with the current active Community UI; the RmlUi migration may still be in progress, so do not assume a specific UI implementation is complete.

### 7.6 Match manifest and replay bootstrap

A custom match must pin **the exact full resolved dependency graph**:

```text
MatchId / AuthorityEpoch
  -> CustomWeaponMatchManifest(version, feature set, ordering)
      -> [RuntimeId 1 -> contentId + packageHash + mechanicsHash]
      -> [RuntimeId 2 -> ...]
      -> [model/material/audio dependency hashes]
```

- Host/server validates content, broadcasts digest, and waits for Ready from each required participant. An unready or mismatched peer cannot enter the live custom match.
- A late join resolves the same manifest, not an arbitrarily newer Community revision.
- A creator updating a weapon cannot change a currently running match or previously recorded demo.
- Match snapshots, accepted shot facts, killfeed and historical replays retain enough data to distinguish custom weapon identities without rewriting native enum history.
- Old replays remain parseable by the previous decoder. New replay readers should handle absent packages with exact metadata and a placeholder visual if necessary; if gameplay reconstruction needs the missing definition, state that playback is limited and do not silently simulate with a different definition.
- Preserve replay seeking and spectator ownership/life boundaries. Eliminate duplicated audio/effects across live, replay, seek and resimulation paths.

---

## 8. PR implementation sequence

**Review rule:** One scoped branch/PR per slice. Commit the resulting changes before proceeding. The dependency tree may be stacked, but the target base, merge order, CI status and rollback must remain clear. Do not merge unverified/failed PRs or force-push unrelated concurrent work.

### CW-00: Read-only audit, baseline and architecture decision records

**Goal:** Freeze source and behavior baseline; establish feasibility and exact contracts before touching gameplay.

Tasks:

- [ ] Inspect `main` SHA, relevant open PRs, CI failures, installed tools and repository rules; record baseline in `docs/custom-weapons/00-baseline.md`.
- [ ] Build desktop Debug and Release; run native weapon tests, multiplayer acceptance, replay-format tests and Community tests. Mark each `PASS`, `FAIL`, `BLOCKED`, or `NOT_RUN` with exact commands/log paths.
- [ ] Locate **all** native weapon-ID assumptions: `BeamType`, 9-entry arrays, `WeaponInfo.Name`, shot packets, damage/claims, replay, HUD, affinity, bots, pickups, gun game, death messages, achievements/analytics and save serialization. Generate a referenced inventory, not a guess.
- [ ] Trace complete accepted Imperialist shot through input, authority, projectile, damage/headshot, killfeed, replay, and HUD. Trace native corpse/death state, skin replacement and the current Community publish/install lifecycle.
- [ ] Capture baseline fixed-60 Hz frame timing, memory, original headshot rig and 8-player bot scene, plus baseline Android behavior where runnable.
- [ ] Write ADRs for (A) identity/native isolation, (B) wire/custom input and versioning, (C) headshot presentation source, (D) physical corpse backend, (E) package canonical hash vs mechanics hash.

**Exit:** Verified base SHA, feature risks and invariant ledger, passing baseline or known failures distinctly documented, real edit locations and ABI/serializer decision approved by test evidence. No gameplay changes.

### CW-01: Immutable weapon identity and registry foundation

**Goal:** Registry and typed identity without altering existing weapon behavior.

Tasks:

- [ ] Add `WeaponIdentity`, `WeaponRuntimeRef/Id`, `WeaponDefinition` and `WeaponRegistry` with strict native/custom cases.
- [ ] Implement stable equality/hash/serialization, catalog lookup and frozen match registry; integrate a read-only native wrapper.
- [ ] Add custom state storage outside legacy nine-element arrays and 3-slot native selections. Avoid default save/replay writes until versioned serializers are designed.
- [ ] Add test-only `prime.riftline-m7` definition (headless, no renderer). Provide no gameplay access until opted in.
- [ ] Assert original native enum/array lengths/identities and native serialized fixtures unchanged.

**Tests / exit:** Registry uniqueness, version hashes, corrupt/missing resolution, concurrent sessions, scene teardown and all native weapon snapshots. Zero native wire/behavior change.

### CW-02: Custom mechanics schema, validation and simulation state

**Goal:** Separate, deterministic, bounded data-driven weapon rules.

Tasks:

- [ ] Define versioned mechanics DTO with defaults, units and per-property constraints. Canonical definition serializer and hash.
- [ ] Implement allowlisted behavior catalog and rule validation; freeze immutable data at match entry.
- [ ] Implement per-player `WeaponRuntimeState` with magazine/bolt/reload/optic state and phase transitions at authoritative 60 Hz.
- [ ] Add reset/invalidation for death, respawn, morph, swap, spectator, owner generation and match changes.
- [ ] Add synthetic state-machine tests, fuzzed invalid JSON/numbers/limits, simulation determinism at equal accepted inputs, and no allocation in routine ticks.

**Tests / exit:** One-fire/one-spend behavior, 75-tick bolt, 138-tick reload, reserve transfers, cancellation, duplicate requests and invalid state rejection. Legacy state untouched.

### CW-03: Offline Riftline M7 functional prototype

**Goal:** Fire a first-class custom sniper in a local controlled match, with correct damage and headshots.

Tasks:

- [ ] Bind `prime.riftline-m7` to 65/150 damage, near-hitscan ballistic settings, hip/scoped profiles, 4+16 ammunition and finite projectile effects.
- [ ] Add custom fire pathway through the scene's approved projectile/hit/damage hooks. Share native damage and attribution only where proven safe.
- [ ] Use current authoritative head region from the native system, unchanged; distinguish scoped/hip rules without casting to `BeamType.Imperialist`.
- [ ] Add test-room pickup/loadout/dev command (off/custom only), headless bot/dummy fixtures and source ownership.
- [ ] Test collision with floors/walls, zoomed headshots, shot range, missed shot, ammo empty, pool exhaustion, friendly fire, protection and death transition.

**Tests / exit:** Shooter can equip/fire/reload; accepted body and head damage are correct; denied/pool-exhausted shots never debit or damage; vanilla behavior is byte/metric equivalent.

### CW-04: Authoritative custom firing, anti-cheat and network state

**Goal:** Real networked custom-weapon combat with no host advantage or protocol confusion.

Tasks:

- [ ] Implement the ADR-approved custom-weapon packet/capability, ingress parsing and server `CustomWeaponAction` acceptance.
- [ ] Version the protocol if wire shape changes; generate packet byte fixtures, malformed/flood tests, native-only compatibility tests.
- [ ] Implement authoritative loadout/weapon-owned resource journal, shot proof, ammo/cooldown/reload/pose validation, dedupe and indexed shot component evidence.
- [ ] Track slot/match/generation/life, source frame, key sequence and frozen manifest/definition hash in all custom action validation.
- [ ] Reuse canonical accepted impact/damage/kill and kill-trade ordering. Never double damage on client correction or stale late fire.
- [ ] Extend snapshots/reconnect/late join/spectator with custom weapon state and definitive shot events.
- [ ] Wire custom shotgun/derived projectile capacity guards generically only where the foundation needs them; initial M7 is one shot/one component.
- [ ] Add latency/loss/reorder simulations and both host/remote/dedicated-server modes.

**Tests / exit:** Accepted/denied headshots, ammo, reload, stale shots, source-aim disagreement, duplicate packet and disconnect/rejoin are correct at low/high latency. Old native-only protocol fixtures remain unchanged; mismatched clients refused with reason.

### CW-05: Rifle inventory, input, HUD, scope and prediction

**Goal:** Complete player control and truthful feedback.

Tasks:

- [ ] Add opt-in custom loadout slot and accessible weapon selection UI without changing the native wheel's semantics in ordinary matches.
- [ ] Cover keyboard/mouse, controller and touch: equip, fire, reload, scope toggle/hold, 2.5x/4x optic selection, ammo display and empty dry-fire cue.
- [ ] Implement scope magnification math and per-scope sensitivity, recoil/camera presentation, local fire/animation prediction with correction.
- [ ] Present authoritative magazine/reserve/reload in HUD; make client predictions visually reversible and never authoritative.
- [ ] Add fallbacks for incomplete RmlUi migration, first-person/third-person player view, spectator, replay and reduced motion.

**Tests / exit:** No overlap/conflict with six-sector weapon wheel or native bindings; 60/120/240 Hz identical fire cadence and scope logic; touch/controller smoke tests; reconnect HUD state correct.

### CW-06: Original rifle assets, sound and animation

**Goal:** Ship original-looking first- and third-person rifle presentation.

Tasks:

- [ ] Author/obtain properly licensed M7 model, textures, LODs, icon, reticle, muzzle/tracer/impact VFX and audio. Track asset licenses and origin.
- [ ] Add validated viewmodel/world model asset loading, attachments, skin binding, animation clips and fallback geometry/audio.
- [ ] Attach weapon to seven Hunter rigs; check aim/muzzle offsets, arm-cannon interactions, left/right orientation and third-person grip. Choose arm-mounted mount variant if a safe handheld two-handed rig cannot be achieved in this slice.
- [ ] Prewarm shaders/resources/audio, cache by exact digest and release on scene unload/Android context loss.
- [ ] Add preview/test harness screenshots in first-/third-person and quality settings, including headless zero-asset path.

**Tests / exit:** Visual/muzzle alignment and no crashes or resource leaks across scene rotation; no delayed camera/gun ghosting at high refresh; valid Android fallback; no copyrighted assets committed.

### CW-07: Nonlethal headshot-specific procedural reactions

**Goal:** All standard Hunters visibly react to accepted headshots while surviving.

Tasks:

- [ ] Consume authoritative headshot facts from existing damage/semantic/replay paths; add a compact presentation fact only for data not already present.
- [ ] Add bounded `HeadHitPoseOverlay` for native `Head_1`, spine and shoulders, weighted4/native mapping and LOD fallback.
- [ ] Directional recoil and smooth fade, with coalescing and reentrancy protection; restore all canonical node matrices after rendering.
- [ ] Add effect/sound policy, reduced-motion/quality gates and replay seek cancellation.

**Tests / exit:** All seven Hunters, moving/jumping/scoped/frozen/morph transitions, death/respawn, replay and high-refresh; no changes to hit registration, player speed, emitter position or authoritative timeline.

### CW-08: Lethal headshot collapse and cosmetic death integration

**Goal:** A distinct, believable lethal headshot transition, still animation-only.

Tasks:

- [ ] Classify accepted death fact as lethal headshot vs other lethal cause.
- [ ] Animate helmet/torso recoil and knees/hips collapse with direction-based variant.
- [ ] Ensure existing eight custom death presentation types and killcam/replay systems remain compatible; determine how dissolve/vanish effects suppress physical bodies.
- [ ] Add authoritative life/slot dedupe, replay checkpoint restore and optional photo-mode style visual tests.

**Tests / exit:** Headshot collapse plays once, correct source/life and direction, no duplicate death audio; revive/respawn cancels; older demos still play via original fallback.

### CW-09: Physics backend technical pilot (opt-in, non-shipping)

**Goal:** Decide if real ragdolls are safe and performant, without putting game physics at risk.

Tasks:

- [ ] Prototype a standalone render-only corpse scene with 12-17 constrained parts and one reference Hunter, using BEPUphysics v2 candidate.
- [ ] Build clean .NET compatibility on all configured targets, especially Android AOT/arm64; inspect license, dependency size, SIMD/platform and memory behavior.
- [ ] Validate corpse against collision-only static meshes, joint limits, spawn pose, impulses and interpolation.
- [ ] Benchmark the same scene with no physics, procedural fall and physics (1/2/4/8 corpses); test 60/120/240 Hz and scene dispose.
- [ ] Keep fallback and backend interfaces; do not wire it into authoritative collision, bots, pathfinding or movement.
- [ ] Record accepted/failed platform benchmarks and ADR decision.

**Tests / exit:** Demonstrated correct disposal and bounded physics at target budgets, or explicitly document why ragdoll stays experimental. A rejected pilot must not block CW-10 fallback or rifle work.

### CW-10: Hybrid ragdoll presentation rollout

**Goal:** Make corpses physically react to lethal shots, opt-in with safe degradation.

Tasks:

- [ ] Map all seven Hunter reference bone/collision profiles with safe fallbacks, LOD and independent corpse render palettes.
- [ ] Transition animation to physics without pose snapping. Apply accepted direction/impulse and optional impact point; never trust a client force scalar for gameplay.
- [ ] Implement quality limits, age/far-distance retire, pooling, sleep, static world collision, cleanup and disable in headless mode.
- [ ] Integrate death cosmetics, killcam, replay and spectator with event seeds and explicitly documented approximate physics recreation.
- [ ] Add coverage for out-of-bounds, teleport, moving platforms (fallback if unsupported), repeated deaths and map/context switching.

**Tests / exit:** No server/gameplay query changes or damaging corpse collision; measured costs meet target budgets or option defaults off; Android device constraints honored; no memory growth across 100+ respawns/scene transitions.

### CW-11: Local `.ppweapon` building, validation and install

**Goal:** A safe, offline package format that carries the M7 and future creator weapons.

Tasks:

- [ ] Implement strict package schema, semantic/canonical definition digest, archive identity, bounds and dependency lockfile.
- [ ] Implement CLI/dev export, validate, test and local install/uninstall workflow with atomic promotion and immutable cache.
- [ ] Include original rifle as a **bundled reference definition** usable through the registry, not a second gameplay implementation.
- [ ] Prevent path traversal, symlink tricks, zip bombs, malformed glTF/audio, unsafe numbers, missing dependencies, duplicate identifiers and unsupported schema capabilities.
- [ ] Validate on server without graphics/audio; missing asset fallbacks are explicit.

**Tests / exit:** Byte-for-byte deterministic builds on supported platforms when inputs are the same (or precisely documented platform-invariant canonical manifest digest and ZIP differences); extensive negative/fuzz/boundary suites; historical packages remain intact.

### CW-12: Custom-match package handshake and replay manifest

**Goal:** Custom weapons work only when all participants share exact definitions.

Tasks:

- [ ] Add custom-match rule/capability gate and immutable weapon manifest (runtime ID mapping, definition, package and dependency hashes).
- [ ] Integrate installer/prewarm/Ready with host allocator, dedicated server and late join/reconnect. No gameplay start when hash mismatch.
- [ ] Add authoritative inventory/grants/pickups and any custom loadout server restrictions. No custom modes injected into ranked/vanilla.
- [ ] Add replay bootstrap and safe old-demo reader versioning; restore weapon definitions during seek or show truthful limitation.
- [ ] Update spectator, killfeed, match events, analytics and bot selection to distinguish custom identity without corrupting native rows.
- [ ] Test old-client refusal and native-only no-op compatibility.

**Tests / exit:** Two-client, dedicated-server, 8-bot/custom, late join and replay seek tests all preserve exact hash identity, shot proof and historical vanilla behavior.

### CW-13: Community Hub `weapon` content-kind publishing

**Goal:** Secure distribution using existing Community infrastructure.

Tasks:

- [ ] Extend `CommunityContentKinds`, v3 capabilities and kind-specific server validation to add `weapon` while preserving map v2/v3 semantics.
- [ ] Reuse authenticated creator ownership, revisions, private/public/unlisted, archive/restore, checksum, storage quotas, audit and moderation.
- [ ] Implement typed browse/detail/revision/download/upload and dependency verification. Manage `weapon` as gameplay content, not merely generic model bytes.
- [ ] Ensure uploads are validated and published atomically; malformed or unsupported packages cannot be advertised/installable.
- [ ] Add structured diagnostics, rate limits and abuse-report hooks.

**Tests / exit:** Authenticated publish and public read, private draft isolation, hash integrity after restart, malicious archive rejection, exact revision retrieval, mixed map+weapon downloads without queue starvation, no map identity regressions.

### CW-14: Project Prime Studio Weapon Creator MVP

**Goal:** Creator can author/export/test a weapon without editing engine code.

Tasks:

- [ ] Add Studio workspace with Create/Open/Import, model/attachment preview, mechanics editor, firing/zoom/reload controls, valid ranges and error messages.
- [ ] Add optional asset converters for supported GLB/textures/audio and preview against a reference Hunter and test target.
- [ ] Provide local sandbox test, validation report, deterministic package build and one-click local install.
- [ ] Keep editor app process isolated from game runtime; reuse only safe library/IPC interfaces and manage ownership across concurrent Studio/game processes.
- [ ] Add signed-off/recorded asset license metadata and unsupported-behavior warnings.

**Tests / exit:** Fresh user project -> playable local custom weapon package; malformed assets and invalid stats are blocked with actionable errors; Studio crash cannot poison game cache; controller/mobile runtime not coupled to Studio UI dependencies.

### CW-15: Community UI install, upload and creator lifecycle

**Goal:** Complete player-facing Browse -> Download -> Equip/Test -> Publish flow.

Tasks:

- [ ] Add weapons tab/category to active Community UI and RmlUi counterpart when that architecture is ready. Avoid duplicating services during migration.
- [ ] Add cards, revisions, permissions, install status, queued/failed downloads, collection labels and creator details.
- [ ] Show required custom packages before joining custom matches; accept/decline download; no silent background install of unknown gameplay content.
- [ ] Add creator upload, revision notes, visibility, dependency warnings and report/withdraw UI.
- [ ] Test network interruptions, expired authentication, retry idempotence, missing packages, hot unload, reinstall and content corruption.

**Tests / exit:** User can reliably discover/import/test/publish/install and enter a compatible custom match; original Community maps, textures/models and legacy clients continue working.

### CW-16: Full security, performance, CI, deployment and release hardening

**Goal:** Production-ready optional custom-weapon support, with feature gates and evidence.

Tasks:

- [ ] Run cross-platform builds, authoritative nettest, replay, history, Community security, UI, package fuzz, device and graphics acceptance matrix below.
- [ ] Benchmark rifle and ragdoll disabled/enabled against CW-00 baseline, publish p50/p95/p99 frame times, allocations, networking bytes, install latency and failure rates.
- [ ] Threat-model spoofed weapon hashes, unauthorized publish, malicious archives, custom source-frame manipulation, impossible fire cadence, ammo/refill, invalid joints/audio/textures and replay deserialization.
- [ ] Validate all rollback gates, versioned room features, error UX, privacy, moderation, asset licensing, server deployment/updater compatibility and documentation.
- [ ] Perform staged opt-in release: internal builds -> private custom lobby -> public custom-only Community category. Keep vanilla defaults unchanged.
- [ ] Close only genuinely completed acceptance checks; record remaining issues and blocked platform validation explicitly.

**Tests / exit:** All required acceptance rows pass or are listed as explicit release blockers; no known severity-high package/runtime vulnerability; rollback rehearsed; final PR summary reports evidence and limitations.

---

## 9. Verification and automated acceptance plan

### 9.1 Required execution protocol

For **every PR**, save a structured evidence file under `docs/custom-weapons/evidence/CW-NN.md` with:

```text
Base SHA:
Head SHA:
Scope / changed paths:
Command and environment:
Result: PASS | FAIL | BLOCKED | NOT_RUN
Baseline comparison:
CI job URL / status:
Artifacts and logs:
Regressions found:
Known limitations:
Rollback method:
Next dependency gate:
```

Do **not** translate "CI queued" into "passed". Do not report a headless compile as physical Android acceptance or a synthetic hit proof test as a two-client real network pass. If a required test cannot run locally, keep the PR gated until CI/device evidence is available, or label it a documented release blocker.

### 9.2 Initial discovery/command candidates

Discover actual project files, runtime executable and CLI parsing on the current branch. The following commands represent known repository workflows, but the agent must verify option semantics and adjust to active source:

```bash
# Baseline builds
dotnet restore src/MphRead/MphRead.csproj
dotnet build src/MphRead/MphRead.csproj -c Debug
dotnet build src/MphRead/MphRead.csproj -c Release

# Network / architecture / lobbies
dotnet run --project tools/nettest -c Release -- --architecture
dotnet run --project tools/nettest -c Release -- --lobby

# Community and protocol tools
dotnet run --project tools/community-v3-check -c Release
dotnet run --project tools/community-dependency-check -c Release
dotnet run --project tools/protocol-generator-check -c Release

# Combat/event identity
dotnet run --project tools/match-events-check -c Release

# When game binary/runtime has been built, inspect actual executable name/output path
# and run the corresponding -replayformatcheck / -cosmeticscheck / -maptest
# and other built-in diagnostics. Never assume these exit successfully.

# Specialized headshot/latency harness (review scripts and setup first)
# tools/hitrig/bench.sh ...
# tools/hitrig/run-local.sh ...
```

Tests additionally required by module once implemented:

- `custom-weapons-check`: registry, schema, immutable hash, rules, life transitions and native parity.
- `custom-weapons-authority-check`: shot proof, ammo, reload, frame-order and malformed packet cases.
- `custom-weapons-package-check`: archive and asset validation, safe extraction, fuzz and dependency graph.
- `custom-weapons-replay-check`: frozen definitions, seek, checkpoint and old-demo compatibility.
- `head-reaction-check`: directions, life/cancel, bone restore, 7 Hunters, high-FPS presentation, replay.
- `ragdoll-check`: corpse-only state, pose transfer, joint constraint stability, pooling and dispose.
- `weapon-creator-check`: Studio import, edit, save, test, build, publish and cancellation.

Create these as named repository tools or integrate into established suites per the project's test style. Avoid adding a large test runner when a focused existing suite suffices.

### 9.3 Canonical gameplay scenarios

| Test ID | Scenario | Required result |
| --- | --- | --- |
| WPN-001 | Native-only 1v1 and vanilla bot match | Exact existing weapon identity/behavior; no manifest change |
| WPN-002 | Equip M7 in offline custom match | Correct independent identity and UI; no native slot overwrite |
| WPN-003 | Scoped full-health body hit | 65 base damage, server-authoritative, correct hit marker |
| WPN-004 | Scoped full-health head hit | 150 base damage, canonical headshot event, expected lethal result |
| WPN-005 | Missed/wall-blocked shot | Ammo spent only for accepted actual fire; zero victim damage |
| WPN-006 | Four rapid presses | No more than cooldown allows; finite magazine; no duplicate fire |
| WPN-007 | Reload 1/4 and reserve 16 | Completes with 4/4 and correct remaining reserve; exactly once |
| WPN-008 | Reload, swap, immediately return | Deterministic cancel policy, no free rounds or cooldown bypass |
| WPN-009 | Die/respawn during reload | Old life reload cannot complete for new life |
| WPN-010 | Malformed/unknown custom ID | Reject; no native fallback or damage |
| WPN-011 | Old client joins custom session | Informative capability reject; cannot observe invalid rifle |
| WPN-012 | Custom client joins vanilla lobby | Works without custom packages, native rules untouched |
| WPN-013 | Wrong package hash at Ready | Cannot enter gameplay until corrected |
| WPN-014 | Burst/reordered/duplicate network fire | Exactly-once accepted shot and payment |
| WPN-015 | 150-400 ms RTT with jitter/loss | Headshot decisions remain bounded to authority; no false body/head upgrade |
| WPN-016 | Two snipers kill each other near same source frame | Trade arbitration follows canonical server rules |
| WPN-017 | Jump-pad headshot test | Authority/seen mismatch measured; no new visual-only correction passed as accuracy fix |
| WPN-018 | 8 player custom FFA with bots | No projectile pool leak, OOM, net flood or stat corruption |
| WPN-019 | Switch to native gun after M7 | No stale M7 mechanics, reload, affinity or ammo grants |
| WPN-020 | Headshot survivor running/aiming | Smooth upper-body overlay; original velocity/aim/hitbox unchanged |
| WPN-021 | Lethal headshot during morph/teleport | One canonical death and safe pose/fallback |
| WPN-022 | Replay seek before/after headshot | Correct event timing and no repeated sound/offset |
| WPN-023 | Ragdoll fall down stairs or onto wall | Stable joints, read-only collision, no infinite impulse |
| WPN-024 | Corpse overlaps living player | No blocking, damage, aiming or pickup effect |
| WPN-025 | 100 repeated deaths/reloads/scene exits | No retained render/physics/audio memory growth |
| WPN-026 | Community attacker uploads zip-slip/zip bomb | Rejected without persistent promotion |
| WPN-027 | Private or archived revision requested anonymously | Authorization/privacy restrictions upheld |
| WPN-028 | Published M7 v1 changes to v2 | Running match and historical replay remain pinned to v1 |
| WPN-029 | Multiplayer runtime missing asset | Clear install/Ready error or permitted cosmetic fallback only |
| WPN-030 | Stale/duplicated headshot event after respawn | No new-life visual reaction; no double audio |
| WPN-031 | Ragdoll disabled | Exact existing cosmetic/native death fallback |
| WPN-032 | Android cold start, pause/resume, match join/leave | No crash, GPU leak, resource ownership error or performance cliff |
| WPN-033 | Community map+weapon dependency download | Neither worker starves the other; hashes and Ready correct |
| WPN-034 | Bot switches, reloads, scopes and fires M7 | Uses same admission and cost rules as human players |
| WPN-035 | Gun Game, Insta-Gib, No Imp, campaign, ranked | Native rules unaffected; custom rifle unavailable by default |
| WPN-036 | Killfeed/Hunter License/leaderboard | Original weapon stats remain valid; custom records are typed and scoped to custom matches |

### 9.4 Network and compatibility matrix

Run scenario tests in **offline local**, **hosted client+server**, **dedicated server**, **late-join**, **reconnect**, **spectator**, **replay playback/seek**, and **8-player or 8-bot** configurations where relevant.

| Mode | Native-only client | Custom-capable client, no package | Custom-capable client with pinned package |
| --- | --- | --- | --- |
| Vanilla server | Works exactly as before | Works, no forced download | Works, package ignored |
| Opt-in custom server | Refused with clear capability reason | Remains unready, prompted to get exact content | Allowed when hashes and server rules pass |
| Ranked/native competitive | Original weapons only | Original weapons only | Original weapons only |
| Old recorded demo | Old decoder behavior preserved | Native playback preserved | Native playback preserved |
| New custom demo | Explicit incompatibility/limited view | Playback requires pinned mechanics or states clearly limited | Full supported authoritative replay + presentation |

**High-latency plan:** Use `tools/hitrig` for long-range and jump-pad comparisons, simulated 150/270/320/400 ms RTT scenarios with jitter and 0/2% loss, plus packet duplication and reordering. Archive `headshot accepted`, `headshot denied`, `body accepted`, rewind clamped, shot reconciliation and trade outcome counts. No cosmetic animation is accepted as a fix to underlying headshot disagreement.

### 9.5 Presentation, graphics and device matrix

- 7 standard Hunters, native and available HD character-model replacement; first-person, third-person, spectator and replay.
- FOV presets and 60/120/144/240 Hz; scoped cannon and camera position must share the same presentation timestamp.
- Forward OpenGL and Android GLES graphics validation; exercise graphics context loss and reload, LOD swap, scene exit.
- Quality Off/Low/Medium/High, bright-skin and death cosmetics, flash/frozen/cloaked states.
- M7 animation idle, shot, bolt, reload, scope transition, morph, respawn and model fallback.
- Ragdoll collision stability and silhouette/floor clipping on varied terrain; missing bone/profile fallback must not crash or decompose.
- Memory/frametime headroom before enabling quality default. Record actual target devices and driver/build hashes.

### 9.6 Performance budget and reporting

Baseline (CW-00) versus each milestone, with identical scenes/hardware:

| Metric | Measurement | Initial policy |
| --- | --- | --- |
| Simulation frame time | p50/p95/p99 at 60 Hz | No measurable regression in native-only matches beyond test noise |
| Render frame time | p50/p95/p99 at 60/120/240 Hz | Add M7 and effects only where visible; match baseline when disabled |
| Physics cost | CPU duration per corpse, peak scene budget | Opt-in until measured; preliminary p95 extra <=1 ms desktop, <=2 ms mobile |
| Allocations/GC | Bytes/s, Gen0/1/2 collections | No sustained per-tick allocations in hot authority path |
| Retained memory | After 100 deaths, multiple map loads, context reset | Returns within documented cache bounds; no monotonic growth |
| Network | Bytes/s at 1/4/8 players, reliable backlog, packet drop | Stable bounded packet output, no impact on gameplay-lane reliability |
| Asset prewarm | Peak RAM, load/Ready duration, shader reuse | Does not block frame loop or start gameplay before hash readiness |
| Package validation | Peak memory/CPU, malformed failure time | Within configured bounds; never extracts untrusted archive outside cache |
| Mobile | AOT/startup, thermal, battery, pause/resume | No startup crash; quality gracefully downgraded when necessary |

Do not label a statistical comparison as a regression without same-scene multi-run baselines. If budget is exceeded, revise quality/lifetime/caps with new evidence, not by deleting assertion coverage.

---

## 10. Security and correctness threat model

| Threat | Countermeasure | Where to test |
| --- | --- | --- |
| Client sends custom ID unknown to server | Frozen registry lookup and explicit fail-closed admission | CW-04, WPN-010 |
| Client alters damage/head multiplier | Server-authoritative immutable definition hash | CW-04, WPN-003/004 |
| Client shoots during reload, after death, or above cap | Per-life authoritative cooldown/ammo/reload journal | CW-02/04, WPN-006/008/009 |
| Spoofed aim, source frame, impossible muzzle | Existing pose-history/rewind policies extended to custom lane | CW-04, WPN-015/017 |
| Replayed/double-billed fire | Full `ShotKey`/component identity, dedupe/payment invariants | CW-04, WPN-014 |
| Bad package hash/altered dependency | SHA-256 verify, immutable staging/commit and Ready match digest | CW-11/12, WPN-013/028 |
| Zip-slip, bombs, duplicate filenames | Strict traversal/ratio/size/count checks with atomic sandboxed staging | CW-11, WPN-026 |
| Malicious GLB/texture/audio decoder input | Strict validated allowlists, bounded decoding and fuzzing | CW-06/11/16 |
| Arbitrary code execution | No creator code/scripts/shaders/binaries/external URI | CW-11/13 |
| Creator privilege escalation | Existing authenticated creator ownership and access checks | CW-13/15, WPN-027 |
| Download/Ready starvation | Separate bounded metadata, upload and download worker lanes | CW-13, WPN-033 |
| Ragdoll alters collision or aim | Distinct visual scene and world read-only collision queries | CW-09/10, WPN-024 |
| Stale headshot after respawn | Generation/life/epoch matching and event dedupe | CW-07/08, WPN-030 |
| Replay silently uses changed mechanics | Exact pinned mechanics digest and versioned bootstrap | CW-12, WPN-028/029 |
| Asset copyright infringement | Original/rights-owned assets, licensing attestations, moderation/report workflow | CW-06/13/15 |

No creator package is trusted merely because it was installed on the game server, listed by the website, or previously cached. Revalidate the exact digest, required schema feature and session ownership boundary.

---

## 11. Feature flags, migration and rollback

Do not invent production flag names if repository naming conventions suggest better choices. Document and test equivalents of:

```text
PROJECT_PRIME_CUSTOM_WEAPONS=0              # default until release
PROJECT_PRIME_CUSTOM_WEAPON_MATCHES=0        # server-side custom match admission
PROJECT_PRIME_HEADSHOT_REACTIONS=0           # new reaction overlay
PROJECT_PRIME_RAGDOLL_PHYSICS=0              # opt-in client presentation
PROJECT_PRIME_WEAPON_COMMUNITY_PUBLISH=0     # creator publishing capability
PROJECT_PRIME_WEAPON_CREATOR=0               # Studio creator interface
```

Rules:

1. Disable custom weapons in native-only modes by design, not only via a client preference.
2. A disabled custom match feature cannot accept custom weapon events, package installs or a stale manifest.
3. Disabling headshot overlay immediately reverts to established directional/native damage animations, without affecting accepted damage.
4. Disabling ragdoll returns to existing death cosmetics/particles with proper corpse-pool disposal.
5. Disabling Community publishing leaves immutable published objects and historical links intact; stop new publish, do not delete current revisions.
6. Version the replay and wire compatibility contracts explicitly when extending them; do **not** roll back by pretending a changed packet is the old protocol.
7. Updater/packaging must distribute server/client versions compatible with custom matches. Rolling back binaries may require temporarily disabling custom lobbies while leaving vanilla play available.
8. All flags must be exposed in debug diagnostics and acceptance logs; define precedence for command-line, configuration and server-advertised policy.

**Rollback rehearsal:** Disable flag on each completed phase, launch native match, play historical replay, leave/rejoin Community, close Studio, validate no stale resource ownership. Record any destructive migration as a release blocker; prefer additive schemas and forward-compatible reads.

---

## 12. Implementation details to decide during CW-00 ADRs

Do **not** let open questions drift into contradictory implementations. For each, record choice, tradeoff, test and rollback:

1. **Custom type bridge:** New discriminated `WeaponIdentity` wrapper versus wider unified IDs. Default recommendation: separate native/custom identities and a bounded match-local ID map.
2. **Accepted custom fire transport:** Explicit new packet or generator-supported extension; preserve legacy byte fixtures. New version required if existing wire shapes change.
3. **Shot engine reuse:** Reuse established collision/damage facts without creating fake native `BeamType` values. Prove no double processing under authority and predictions.
4. **Ammo persistence:** Per-life magazine/reserve, reload on swap cancellation, UI prediction and snapshot reconciliation.
5. **Near-hitscan implementation:** Prefer finite ballistic projectile using existing accepted rewind semantics. Document point of impact and projectile/tracer life so a client visual cannot override a server hit.
6. **Bot intent and pickup rules:** Bots use same input/authority path; custom pickups and grants allowed only under custom rules.
7. **Character viewmodel mount:** Arm-mounted MVP versus bespoke two-handed rig. Choose from actual seven-Hunter visual capture and attachment behavior.
8. **Headshot fact path:** Extend existing damage/semantic stream if sufficient; avoid redundant packet where the accepted flags/directivity already exist.
9. **Corpse source pose:** Sample full final rendered biped palette independently from canonical player simulation and zero out inherited invalid velocities.
10. **Physics engine:** BEPU v2 vs a small procedural fallback, based on Android/desktop benchmarks; reference [BEPU documentation](https://github.com/bepu/bepuphysics2/blob/master/Documentation/GettingStarted.md) and test license/package/runtime behavior.
11. **Community packaging:** Self-contained `.ppweapon` v1 vs dependency-locked references to typed model/texture/material/audio blobs. Prefer a simple safe v1 and expand only after dependencies are demonstrably correct.
12. **Server package needs:** Server only requires gameplay definition+immutable hash; clients also require presentation. Define safe cosmetic fallback vs mandatory exact package download.
13. **Replay behavior:** Exact custom mechanic identity in bootstrap; what happens if package is missing or physics differs across devices.
14. **Analytics/rating:** Custom matches segregated from ranked verified career ratings; add typed weapon aggregates without reinterpreting native `BeamType` statistics.
15. **RmlUi and Studio:** Bind to currently active UI navigation/service providers. Do not let incomplete migration hold the gameplay foundation hostage.

---

## 13. Agent working contract: how to execute

### Required execution loop

For each `CW-NN` slice:

1. **Inspect:** Pull latest intended base branch and the PR dependency stack. Re-read `ARCHITECTURE-INVARIANTS.md`, actual code, test fixtures and relevant current PRs. List non-negotiable invariants for this slice.
2. **Design:** Briefly record exact affected methods/classes, proposed schema or wire changes, tests, expected baseline impact, feature flag and rollback.
3. **Implement:** Make a narrow PR-sized change with straightforward code, minimal new abstractions and no parallel custom gameplay authority.
4. **Test:** Run targeted unit/scene/integration suites, compile required targets, then perform appropriately scoped comparative tests. Save logs and results.
5. **Review:** Inspect diff for native enum shifts, altered packet layouts, stale lifetime references, allocations, replay incompatibility, malicious package handling and accidental generated binaries/artwork.
6. **Commit:** Commit the slice on a scoped branch, reference CW identifier, push and open an independently reviewable PR with implementation summary, acceptance evidence, risks and rollback.
7. **Integrate:** Merge only after required checks/review gates pass and merge permissions authorize it. If stacked, update child PR bases carefully and re-run affected tests after merge.
8. **Continue:** Move to the next slice, resolving collisions with in-flight PRs before editing their files. Do not leave a partially merged contract for later branches to assume.

### Required PR template

```markdown
## CW-NN: <short feature title>

### Base / changes
- Base SHA: ...
- Head SHA: ...
- Files and APIs changed: ...

### Invariants
- Vanilla rules/IDs preserved: ...
- Server-authoritative combat preserved: ...
- Legacy replay/protocol preserved or versioned: ...

### Tests and measured evidence
| Command / environment | Result | Log |
| --- | --- | --- |
| ... | PASS/FAIL/BLOCKED/NOT_RUN | ... |

### Performance
- Baseline: ...
- New: ...
- Measurement quality: ...

### Known limitations / explicit blockers
- ...

### Feature flag / rollback
- ...

### Next PR dependency
- ...
```

### Restrictions for automation

- If unrelated main-branch failures or merge conflicts are present, report and isolate them. Do not mask them as passing tests or rewrite unrelated history.
- Do not commit binary game dumps, extracted copyrighted material, test secrets, private creator keys, credentials, huge profiling captures or shader caches.
- Do not silently widen protocol or legacy enum fields and hope old readers survive.
- Do not bypass failing tests by disabling canonical gameplay correctness checks.
- Don't claim the SDK supports Android merely because a managed desktop project compiles.
- Prefer existing CI workflows and scripts. Add a new job only when new coverage cannot be accurately expressed in current jobs.
- Use feature flags for unfinished runtime paths; keep optional content non-default until complete.
- Keep existing OpenGL rendering and netcode direction intact.
- **Avoid user intervention where an engineering choice can be made safely from this plan.** If a major irreversible decision or missing user-owned art/license blocks acceptance, document the blocker while completing independent slices that remain safe.

---

## 14. Staged delivery milestones and sign-off

### M1: First playable custom sniper (CW-00 through CW-06)

- [ ] Distinct persistent custom weapon identity, not a native enum alias.
- [ ] Playable rifle and genuine independent magazine/reload/zoom rules.
- [ ] Authoritative low/high latency fire, correct body/head damage, safe shots and stale event rejection.
- [ ] Original rifle asset path, scoped HUD, audio, viewmodel and basic LOD/fallback.
- [ ] Vanilla weapons, affinity, AI and replays remain unaffected.

### M2: Satisfying headshot presentation (CW-07 through CW-08)

- [ ] Seven-Hunter render-only head/spine reaction with proven canonical transform restoration.
- [ ] Different lethal and nonlethal reactions.
- [ ] Killcam/replay/spectator/cosmetic compatibility; no invented headshots.

### M3: Optional physical corpses (CW-09 through CW-10)

- [ ] Physics pilot performance evidence and clear choice of backend/fallback.
- [ ] Hybrid ragdoll, body collision against level, no living gameplay collision.
- [ ] Feature quality/disable toggles, pooling, device verification and scene cleanup.
- [ ] If real-device acceptance fails, keep this milestone opt-in and explicitly nonblocking for M4/M5.

### M4: Content-authoring foundation (CW-11 through CW-12)

- [ ] Safe immutable `.ppweapon` schema, builder, validator and local installer.
- [ ] Exact custom-match manifest, Ready gate and replay metadata.
- [ ] Server can reject invalid/missing weapon definitions without graphic assets.

### M5: Community creation and distribution (CW-13 through CW-15)

- [ ] V3 Community `weapon` content kind, authenticated upload/download and immutable revisions.
- [ ] Studio editor and local sandbox test.
- [ ] Published custom weapon can be downloaded and used in compatible custom lobby.

### M6: Production hardening and staged release (CW-16)

- [ ] No release-blocking security/correctness failures.
- [ ] Native parity, compatibility, authoritative multiplayer and old-replay checks pass.
- [ ] Measured performance and memory results with feature on/off.
- [ ] Desktop and Android acceptance, or explicit platform restrictions and release blockers.
- [ ] Release/rollback documentation and clear package moderation controls.

### Final definition of done

This project is finished **only** when a player can create a valid custom rifle in Project Prime Studio, package it, publish it through Community Hub, another player can securely download its exact revision, both can join an opt-in custom match with authoritative consistent shots and headshots, view the correct optional reactions/corpses, record/replay the match, and still play a vanilla match with unchanged original weapons and behavior.

The **first standalone release** may legitimately be M1 + M2, with ragdolls experimental and creator distribution deferred, as long as release scope and flags clearly say so. Never call M1 complete fulfillment of the full Community weapon ecosystem.

---

## 15. References to inspect before coding

### Project code and architecture

- [Project Prime repository](https://github.com/AntiNotAnti/Project-Prime)
- [Native weapons and `WeaponInfo`](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Metadata/Weapons.cs)
- [Native `BeamType`](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Formats/Enums.cs)
- [Native player inventory and animations](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Entities/Players/PlayerEntity.cs)
- [Authority accepted firing](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/PlayerEntityAcceptedShot.cs)
- [Shot proof and authoritative ammo/weapon evidence](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Network/NetShotProof.cs)
- [Player drawing and split biped poses](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Entities/Players/PlayerDraw.cs)
- [Player replay damage/death presentation](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Replay/PlayerEntityReplica.cs)
- [Cosmetic death runtime](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Cosmetics/Death/DeathPresentationRuntime.cs)
- [Character model authoring and rig mapping](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/Render/Characters/CharacterModelPack.cs)
- [Community content kinds](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/MapGen/Sharing/CommunityContentSchema.cs)
- [Community typed content validation](https://github.com/AntiNotAnti/Project-Prime/blob/main/src/MphRead/Mods/MapGen/Sharing/CommunityAssetValidator.cs)
- [Community Hub typed content design](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/architecture/community-hub-pr08-typed-content.md)
- [Community publishing hardening](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/architecture/community-hub-pr09-publishing.md)
- [Burst firing example](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/gameplay/balanced-battlehammer-burst.md)
- [Headshot testing protocol](https://github.com/AntiNotAnti/Project-Prime/blob/main/.claude/testing/HITRIG.md)
- [Modern Hunter rig authoring notes](https://github.com/AntiNotAnti/Project-Prime/blob/main/docs/rendering/samus-hd-kit/README.md)

### External engineering references

- [BEPUphysics v2](https://github.com/bepu/bepuphysics2) and [ragdoll/constraint getting-started guidance](https://github.com/bepu/bepuphysics2/blob/master/Documentation/GettingStarted.md). Review current target framework, Apache-2.0 license, dependencies and Android/AOT support **at implementation time**.
- [Khronos glTF 2.0 specification](https://github.com/KhronosGroup/glTF/blob/main/specification/2.0/Specification.adoc). Use validated subsets and explicit engine-defined animation/skin constraints; specification compliance alone does not guarantee safe runtime import.

---

## 16. Direct AI-agent kickoff instruction

**Start now with `CW-00` followed by `CW-01` and `CW-02`.** First audit the latest `AntiNotAnti/Project-Prime` main, active PRs and CI; read `ARCHITECTURE-INVARIANTS.md`; identify exact native weapon-ID, packet, resource and replay coupling; establish reproducible native/headshot/Community baselines. Design the additive weapon registry with immutable custom definitions and a clean native/custom split. Implement CW-01 and CW-02 in independently reviewable branches/PRs with unit tests and proof that vanilla weapons and serialization are unchanged. Commit and test each slice, report all actual validation results, and proceed to the offline Riftline M7 in CW-03 only when the identity/rules layers meet their gates. Follow the remaining CW-04 through CW-16 sequence without abandoning compatibility, security, replay correctness or feature-flagged rollback. Do not claim work completed until code, tests and CI actually demonstrate it.
