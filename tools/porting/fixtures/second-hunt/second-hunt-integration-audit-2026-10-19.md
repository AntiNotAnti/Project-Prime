# SH-00: frozen Second Hunt integration audit

This is source inspection and an acceptance plan, not completion or release evidence.
No runtime behavior, protocol, replay format, import support or feature default changes
in this audit. The implementation plan is dated 2026-10-19; repository code wins when
older prose disagrees. Current implementation after this baseline belongs in each
later slice's evidence, not retroactively in this snapshot.

## Frozen comparison and evidence ownership

| Item | Inspected identity |
| --- | --- |
| Project target/main baseline | `5bc952147ce45f87d777ff0de4b2927f0c27ef9e` |
| Prior PORT source pin (retained) | `c16a907b3658c2fd4079e394a73711596a85caee` |
| New reference, inclusive | `41452f2d053fcfa679e33c00a73d120a53f59e61` |
| Upstream repository | `BountyHunterKanden/SecondHunt`, reference branch `master` |
| Open PR inventory | Coordinator reported zero open PRs before work at the target SHA; this is a point-in-time observation, refresh before each slice |
| Delta completeness | 11 commits, 641 changed paths; all commit/path metadata in [frozen delta](second-hunt-delta-2026-10-19.json) |
| Manifest semantics | Original `provenance` and content arrays remain the old PORT inventory; `integrationAudit` records this new reference, contract and fixture witnesses separately |

The frozen delta includes names of changed upstream replay binaries, but no replay,
source media, generated mesh/texture/collision, disc, key or protected payload is
copied. MIT licensing of source code is separate from rights to source game data.
No upstream code is adapted in SH-00; retain upstream notices in later adaptations.

`CLAUDE.md` and `ARCHITECTURE-INVARIANTS.md` were reviewed alongside the full plan.
Older CPP-08 and campaign gap documents remain dated historical evidence. In
particular, the later CPP-08 appendices already implement a private source-rig
palette consumer and synthetic Mac GPU closure; do not restart their earlier draft.
The source contract, not the earliest table in that document, governs new work.

## Complete commit ledger

All paths are relative to the frozen upstream checkout. The JSON includes the
changed paths for every commit as well as the complete final comparison, so deleted
documentation, nonshipping tools and unrelated Beta 3 changes remain inspectable.

| Commit | Source change | Planned disposition |
| --- | --- | --- |
| `b6fc4861adba0175ead5ac6a255493896f8e2b70` | Beta 3: arena/turret, Beyond source import, input/settings, First Hunt, effects, HUD/music, online served matches and broad native fixes | Inspect by feature below; not a bulk port or proof of Project behavior |
| `aa92ada16cf4103bea65b9f8156be070e84e6ff0` | Trimmed XCI handling and documentation | SH-09 research only; no Switch/key pipeline in Project |
| `54edafaa1159180172b6eb6575db0a52f87e8035` | One logical Sylux import card | SH-03 grouped transaction design; actual MP4 content remains SH-09 |
| `947861ba9f928e289658cf86d017673d8e2df265` | Touch/controller arbitration, Aim Only | SH-01A |
| `0574582eed4d4f3e68d3d4545a929ba721fe9fbf` | Tappable weapon ring | SH-02 |
| `1578a87f8e6a828ed6eefb1704efb3717eb1da52` | Immediate stylus response | SH-01B |
| `417389355d7feaa1ed75a0862b3f6420402d2d77` | Pirate Blaster in Try Controls, Beta Phazon visor, import freshness indicators | SH-03 status concepts, SH-05 reference; cosmetic suit work separate |
| `501e7ca82d110d15a7b8b629b0451b75f753a932` | README update | Source product claims only |
| `742d64d4bcdeec59832c7721e0c734247bbe9cd1` | README update | Source product claims only |
| `2438dd0d7daf7ead6cb81688f7d2a7103d1ff59c` | README update | Source product claims only |
| `41452f2d053fcfa679e33c00a73d120a53f59e61` | Overcharged Dark Samus, served Find Game, Hyper Ball disabled | SH-05B cosmetic candidate, SH-06 reference; respect disabled Hyper Ball |

## Source exposure and scope ledger

“Public” below describes source gating/offering at the reference, not verified
playability in Project or a promise about upstream release binaries. Source paths
are upstream paths; Project owners are in the phase matrix below.

| Feature | Upstream owner / commits | Source exposure | Project disposition |
| --- | --- | --- | --- |
| Touch modes/controller coexistence | `src/MphRead.Android/MatchTouchOverlay.cs`, `CampaignActivity.cs`, `src/MphRecomp.Core/Config/RecompSettings.cs`; `947861ba`, `b6fc4861` | Public settings/input | SH-01A, reuse Project Android pointer/held-button owner |
| Direct stylus sampling | `src/MphRecomp.Core/Input/StylusAim.cs`, `src/MphRecomp.App/Campaign/CampaignRenderer.cs`; `1578a87f`, `b6fc4861` | Public input | SH-01B, keep Classic path and native morph semantics |
| Touch weapon ring | `src/MphRecomp.App/Campaign/CampaignRenderer.WeaponRingTouch.cs`, `CampaignHost`/`WeaponRingInput`; `0574582e` | Public, including Aim Only | SH-02, Project's actual HUD arc/transform differs |
| Grouped import and freshness | `src/MphRecomp.Core/Frontend/ImportGames.cs`, `RecompMenus.ImportGamePage.cs`, `Import/ImporterVersions.cs`, `ImportService.cs`; `54edafaa`, `41738935`, `b6fc4861` | Public card/status, subject to `Offered` gates | SH-03; derive transaction semantics from existing Project leases/staging |
| Six Echoes arenas / heavy turret | `src/MphRecomp.Core/Arenas/EchoesArena.cs`, `EchoesTurret.cs`, `EchoesBreakables.cs`, `EchoesCrates.cs`, `EchoesMovement.cs`, `src/MphRead/Entities/Players/PlayerEchoesSeat.cs`, `src/MphRead.Tools/EchoesTurretTool.cs`; `b6fc4861` | Public extra arenas, local import required | SH-04; source test count does not close Project physics/render gate |
| Dark Samus / Overcharged | `src/MphRecomp.Core/Multiplayer/Characters.cs`, `Campaign/DarkSamusBody.cs`, `recipes/bodies/MP2DarkSamusFinal.json`, `recipes/hd/MP2DarkSamusFinal{,Ball}.json`; `41452f2d` plus prior pin | Public imported extra; new skin belongs to `darksamus` | SH-05B; no new numeric Hunter; new recipe not installed by metadata audit |
| Trooper / drone / wall cling / Blaster | `src/MphRead/Entities/Players/PlayerWallCling.cs`, `PlayerScoutDrone.cs`, `PlayerDroneLaser.cs`, `PlayerPirateBlaster.cs`, `PlayerSquadMark.cs`, `src/MphRecomp.Core/Multiplayer/Net/SeatAbilities.cs`; prior pin plus `b6fc4861`, `41738935` | Public extra rules; host gating | SH-05C/D; port outcomes to Project server contracts |
| Shield Wall | `Characters.ShieldWallTest`, `src/MphRead.Tools/ShieldTest.cs` at reference | Parked/test-only, default false | Excluded from shipping plan; not a Trooper acceptance requirement |
| Served Find Game | `src/SecondHunt.Server/HubServedFind.cs`, `HubServed.cs`, `HubFind.cs`, `src/SecondHunt.MatchHost/ControlMode.cs`; `41452f2d`, `b6fc4861` | Public server capability, falls back to player host when unavailable | SH-06 behavior study only; reject fallback, keep isolated dedicated allocator |
| Smooth frames / cap | `src/MphRecomp.App/Campaign/CampaignSmoothFrames.cs`, `src/MphRead/SceneHostSmoothFrames.cs`, `src/SecondHunt.Desktop/FrameLimiter.cs`; `b6fc4861`, `41452f2d` | Extrapolation initialized off for public builds; Windows-specific timer | SH-07 A/B only, no direct timer/render copy |
| Ship cockpit / planet selection | `src/MphRecomp.Core/Frontend/ShipMenu.cs`, `ShipPlanetSelect.cs`, `src/MphRecomp.App/Campaign/CampaignShip.cs`; existing reference plus `b6fc4861` | Public campaign presentation | SH-08A, preserve Project travel/save/sound ownership |
| Ending / detailed pause map | `src/MphRecomp.Core/Frontend/GameEnding.cs`, `PauseMap.cs`, `PrimeMap.cs`, `HudMiniMap.cs`; existing reference plus `b6fc4861` | Public presentation, some visual settings conditional | SH-08B/C; no save-format/decoder replacement |
| Prime 4 Sylux body/cannon/Lockjaw | `recipes/bodies/MP4Sylux{,Lockjaw}.json`, `recipes/guns/MP4Sylux.json`, `src/MphRecomp.Core/Import/Beyond/*`, `Import/Switch/*`, `Campaign/BeyondSyluxBody.cs`, `BeyondLockjaw.cs`; `b6fc4861`, `aa92ada1`, `54edafaa` | Offered Sylux import; title containers need local keys upstream | SH-09 research only; no Project source support or production toggle |
| Zero Suit / Paralyzer / Brawl | `Characters.Offered`, `src/MphRecomp.Core/Import/Brawl*`, `Campaign/BrawlZeroSuit*`, `src/MphRead/Entities/Players/PlayerParalyzer.cs`; `b6fc4861` | Zero Suit hidden in public builds, developer work retained | Outside SH-01–08, no implied new character/weapon approval |
| Hyper Ball / effects | `src/MphRead/Entities/Players/PlayerHyperBall.cs`, `src/MphRecomp.Core/Import/Effects/HyperFx.cs`; `b6fc4861`, `41452f2d` | Present code, latest reference disables feature | Excluded; no production enablement inferred from parser/tests |
| First Hunt demo game/menu/movie path | `src/MphRecomp.Core/FirstHunt/*`, `src/MphRead/FirstHunt/*`, `src/MphRecomp.App/FirstHunt/*`; `b6fc4861` | Separate demo source path | Outside SH scope; no second runtime/front end |
| Prime music / imported track catalog | `src/MphRecomp.Core/Music/*`, `recipes/tracks/*`, `Frontend/ImportGames.Offered`; `b6fc4861` | Prime songs hidden in public imports; developer rows remain | Outside scope; retain Project single mixer/device ownership |
| Cosmetic Samus suits, HD guns, visors and flourish | `recipes/hd/MP1BetaPhazonSuit*.json`, `Campaign/BodyFlourish.cs`, `Import/Guns/*`, `Frontend/MenuSamus.cs`; `b6fc4861`, `41738935` | Mixed public imported appearance and developer variants | No blanket new suit support; follow existing CPP/private parts and source receipts |
| Native combat/bot, gyro/zoom, dialog/menu, updater, replay and notices | `src/MphRead.Tools/*`, `PlayerAi.cs`, `Input/GyroAim.cs`, `Input/ZoomAim.cs`, `src/SecondHunt.Desktop/*`, upstream replay files/notices; principally `b6fc4861` | Mixed public correctness and developer tools | Regression references only; no rules/updater/replay/UI replacement. Every remaining path stays in frozen delta for later scoped review |

## Current Project contract and fixture witnesses

The manifest pins SHA-256 of 21 target-baseline source/fixture files. These are
source witnesses, not evidence those tests ran in SH-00. Verify them from Git with
`python3 tools/porting/verify_integration_audit.py --upstream /path/to/SecondHunt`.
This checks the frozen comparison without reading any game data or making network
requests. Ordinary manifest tests require only Python and this repository.

| Contract | Source-confirmed baseline |
| --- | --- |
| Live protocol | `NetProtocol.cs`: `NetConfig.ProtocolVersion = 61`; exact Hello match required |
| Packet registry | `CustomWeaponManifest = 83`, `CustomWeaponReady = 84`, `CustomWeaponReadyConfirmed = 85`; capability only, no gameplay admission |
| Modifiers | `MatchDefinition.cs`: `MatchModifierFlags : uint`, 4 bytes; Momentum `32768` (bit 15), Quake III `65536` (bit 16); `LobbyRuleFlags : ushort` remains separate |
| Replay reads | `DemoFile.cs` admits formats 1–6; historical protocol conversion remains in `ReplayIdentityCompatibility` |
| Replay writes | `ReplayCapture` ordinary capture v3, opt-in timed-shot certificates v6; checkpoint clips ordinarily v4; v5 indexed-fire continuation preserved; `ReplayMetadata` default stays 3 |
| Authority | Dedicated process owns combat/world/score; owner supplies movement position; no player-authority fallback, no movement reconciliation |
| Import/map | Local private sources and immutable exact map/package identity; existing Echoes generated editions remain offline-only |
| Character | Seven original playable Hunter IDs; private identity-keyed source-rig biped consumer exists, no public extra identity/abilities acceptance |
| UI/render | Existing RmlUi controller/state owner and renderer; Hunters customization and read-only Hunter License; 60 Hz simulation independent of drawing |

`DemoFile.FormatVersion = 2` names the legacy writer constant; it is not the newest
readable or capability-bearing replay format. Likewise class name `ReplayFormatV3`
does not mean v6 is unsupported. Architecture prose was corrected without altering
these code owners. Protocol capability messages do not authorize a content port.

## Per-slice owner, gap, risk and verification matrix

Project paths below are repository-relative. A test name identifies an existing
fixture/owner to extend, not a claim that its future behavior is implemented.
Dependencies follow the user plan; preserve ongoing PORT/CPP/RML/FPS work.

| Slice | Existing Project owner / retained behavior | Gap and primary risk | Verification / activation gate |
| --- | --- | --- | --- |
| SH-00 | Root architecture, `docs/porting`, `tools/porting`, nettest | Older protocol/replay and preview/playability claims | Metadata contracts, complete frozen delta, source witnesses; zero runtime changes |
| SH-01A | `src/MphRead.Android/TouchControls.cs`, `AndroidInput`, `Mods/Input/InputSourceTracker.cs`, `TouchSettings.cs` | Explicit Auto/Touch/AimOnly/Off and pointer capture/handoff; stuck/duplicate button or aim | Extend native input fixtures, Android lifecycle/pointer event bridge; 2/3 fingers, cancel, controller noise/disconnect while shooting, UI modal, rotate, pause/resume, death/morph |
| SH-01B | `Mods/Input/NativeTouchState.cs`, `StylusZone.cs`, `Entities/Players/PlayerEntityNativeTouch.cs` | Project already consumes raw touch aim at the next simulation step and has no demonstrated ten-frame aim hold; explicit Direct/Classic behavior must preserve that path and native roll/boost. Risks: lost short flicks or double-applied delta | `NativeTouchChecks`, `NativeTouchCheckpointTests`, pointer fixtures; exact deltas at 30/60/120/144 Hz, DPI/inversion/zoom and physical swipe latency against baseline |
| SH-02 | `Mods/Input/WeaponWheel.cs`, `Entities/Players/PlayerHud.cs`, `PlayerInput.cs` | Existing Android/stylus absolute arc already exists; add deliberate slot hit/gesture ownership using actual drawn transform | `PointerCheck` and wheel availability fixtures; six slots, unavailable/empty, drag/release outside, aim second pointer, controller handoff, HUD scaling/aspect/DPI; actual Android highlight/equip |
| SH-03A | `Mods/Import/ExternalCharacterCatalog.cs`, `RetroCharacterPreviewExporter.cs`, `CharacterSourcePreviewBinding.cs`, `EchoesPrivateMapImport.cs` | Current receipts and source proofs exist; no unified grouped body/rig/material/gun/ball status transaction | `tools/external-import-check`, `tools/retro-character-preview` source suites; altered hash/version/edition/part, contained paths, parser budgets; Ready never inferred from one file |
| SH-03B | Same import owners; existing private staging, source leases/transfer-index conventions in Map Studio | Cancellation/rollback and game/Studio coalescing, progress through current UI; replacing good assets halfway | Crash/cancel each publication stage, duplicate/concurrent jobs, source mutation, storage URI expiry, old-good preservation; source fixture and private media gate |
| SH-04A | `EchoesPrivateMapImport`, `EchoesVisualLayer`, `EchoesOfflinePlaytestCatalog`, `MapBuilder`/`MapMesh` | Six local previews/collision and routes exist; textures/material/world correspondence, objective/navigation and actual media playability unfinished | Existing Echoes MREA/world/visual/floor/topology fixtures; all six maps with bots, culling, alpha/depth, pickups/fall recovery, GPU/CPU/memory, real imports desktop/Android |
| SH-04B | `EchoesTunedCannons`, native `JumpPadEntity`, simulation/combat/entity owners | Heavy turret authority/life/state not supplied by map preview; 12 kinetic points need actual landing acceptance | Extend `EchoesCannonFixtureTests`/route fixtures, native turret hit/outcome replay tests; simultaneous claims, biped/alt landing, bot paths, seek/reset |
| SH-04C | Existing native collision/entity and `Mods/Movement/MovementModel.cs` owners | Source-specific boosts/double jump/destructibles/water must not mutate native/Momentum/Quake III | Per-mechanic deterministic 60 Hz opt-in fixtures, lifecycle resets, replay facts and all movement baseline controls; unproven mechanics off |
| SH-04D | `DedicatedServer`, existing custom map readiness/hash gates | Lawful dedicated provisioning and exact collision/map identity unresolved | Explicit feasibility conclusion; absent/wrong hash reject, no proxy geometry or protected upload; 1/2/4/8, reconnect and authority loss only after gate. Offline-only until qualified |
| SH-05A | `Render/Characters/CharacterModelPack`, `CharacterModelRuntime`, `CharacterWeightedModelLoader`, `CharacterSourcePreviewBinding` | Private per-seat identity and source palette runtime already exist; remaining product scene, rig/clips/parts evidence | `CharacterModelAssetCheck`, private acceptance/PORT Python suites; native+Dark Samus simultaneous, distinct Troopers, tamper/missing source fallback, palette/hierarchy, GPU/resource lifetimes |
| SH-05B | Same CPP owners plus `ExternalCharacterCatalog` | Overcharged is a proposed skin of `darksamus`, not installed catalog support; full body/cannon/ball/state alignment incomplete | Actual source body/HD/ball/cannon, source animation and muzzle/reticle/hurtbox/death/respawn captures. Samus mechanics only until separately authored changes; no playable claim from synthetic rig |
| SH-05C | `Mods/Multiplayer/CharacterGameplayRules.cs`, native Weavel/Halfturret, server/life/combat owners | `TrooperV1` policy seam exists; no complete Trooper wall cling/drone/Blaster slice | Server-valid bounded contact, duration, drone/standing-body health, recall/life rollback, mark visibility/occlusion and bots; 1v1/8-player only under explicit opt-in |
| SH-05D | `NetProtocol`, `MatchDefinition`, roster, replay metadata, current RmlUi Hunters | Extra identity/capability path and replay facts not accepted; existing cosmetics are not capability negotiation | Historical v1–v6 fixtures, protocol mismatch/admission, exact source IDs, same-parent seats, reconnect/seek/life, vanilla-room rejection, missing cosmetic native render fallback. Deliberate versioning before wire change |
| SH-06A | `RmlUiMenuController.QuickMatch`, `Render/RmlUiLiveMenuServices`, `LobbyQueueClient`, `NetSessionPartyReservations`, authenticated party adapters | Current QuickMatch discovers compatible non-full servers and chooses a candidate; party leases/accept/cancel and menu cancellation fencing already exist. Missing one directory-owned grouped search/mode/region generation | Extend `tools/rmlui-party-check`, `tools/server-browser-party-source-check`, queue fixtures; solo/2/4 group, duplicate/cancel/stale lease, disconnect, ticket binding, no private token logs |
| SH-06B | Existing `MapVote`, `MapPick`, master allocator, isolated dedicated process, persistent lobby | Search ballot to exact content/admission handoff; capacity/timeout/race | 2/4/8 queue, ties/timer, full/draining/restarted server, wrong map/protocol, old tickets, same scoreboards and lobby cycle; synthetic load plus physical dedicated deployment. No player fallback |
| SH-07A | `Mods/Render/FrameTiming.cs`, `DesktopFramePacing.cs`, current interpolation/late latch, profiler | No same-hardware 60/120/144/240 comparison captured in SH-00 | `FrameTimingCheck`, existing replay draw/cadence checks plus controlled baseline recipe below; simulation facts identical |
| SH-07B | Same presentation owner, entity/life draw history | Bounded forward extrapolation is unproven; phase error, ghosting, stacked pacing and resource lifetimes | Same binary A/B, 1/8 players, Shock Coil/Imperialist, morph/rapid reversal/teleport, replay 4-camera, pause/minimize/teardown, Android thermal/120 Hz and 30-minute soak; off unless repeatably better |
| SH-08A | `RmlUiCampaignShipRouting`, `CampaignShipTravel`, `StorySave`, existing native Scene | Optional source cockpit, correct Samus/Delano context | Campaign ship-flow/dialog/Delano7 fixtures; first scan Enter/E/Q, save/new/resume/copy, all planet landing/takeoff, source media and sound lifetime on devices |
| SH-08B | Existing `CampaignMovieDecodeSession`, ending/save owners | Original two endings/credits fidelity and records | `CampaignMovieLifetimeTests`, movie/endings/journey fixtures; both Gorea paths, 15 Hz image cadence, audio skip/stop/close, no-device/re-entry and actual device source parity |
| SH-08C | Existing native pause map/projection and RmlUi campaign route | Detailed map/connector overlay and touch/controller navigation | Campaign presentation tests plus explored-room privacy, panning, 16:9/narrow and GL ownership captures; original progression still works with presentation off |
| SH-09 | Existing bounded local import and CPP/private model owners only | New container/model/skin/codec stack and parts/materials unsupported; no source or rights conclusion | [Research memo](second-hunt-sh09-feasibility-2026-10-19.md); no production toggle, parser port or real-source acceptance. MP1/2/3 first |
| SH-10 | Existing CI/build/update/package, nettest, import/RML/CPP/replay acceptance | Cross-platform actual media/input/GPU/deployment qualification | Windows/Linux/Mac, Android arm64 Release/AOT+ES/controller/touch/thermal/resume, Linux x64/ARM64 authority, native seven Hunters+movement, 1/2/4/8, package/update integrity, historical replay and all shipping source gates |

## Baseline capture procedure and tolerance decision

No physical Android swipe baseline or same-hardware 60/120/144/240 frame-time
baseline was collected in SH-00. Earlier dated Mac synthetic GPU or native FPS
artifacts are useful references, not substitutes for this experiment. No tolerance
band or improvement percentage is invented from compile/synthetic success.

1. Freeze target SHA, binary/dependency hashes, OS/driver, CPU/GPU, monitor and
   measured refresh, display/input scaling, touch sampling rate, power/thermal state,
   map and exact source/package hashes, native movement/rules, camera route and
   settings. Record absent monitors/devices as skipped, never simulated hardware.
2. Use one instrumented binary and route, fixed scene/seed and frame cap; collect
   baseline and candidate arms alternating order at 60/120/144/240 Hz where the
   device can actually present them. Warm up and repeat at least three runs per arm;
   preserve per-frame timestamps and warmup policy, not only average FPS.
3. Report p50/p95/p99/p99.9 frame time, 1% lows (state formula), CPU submit/GPU timing,
   input-to-photon proxy methodology, camera/gun phase, dropped simulation steps,
   allocation/GC/memory and crashes, with duration/sample counts and raw data hashes.
   Include 1/8 players and 30-minute memory/thermal arms before activation.
4. Derive baseline run-to-run bands on that hardware before judging candidate gain;
   require repeatable improvement beyond baseline variance and no meaningful tail,
   simulation/replay-fact, resource or visual regression. A machine without the
   required monitor cannot sign the corresponding visual/pacing gate.
5. Touch baseline/candidate use equal physical swipe distances and recorded pointer,
   UI sampling, simulation consumption and displayed reticle timestamps. Include
   taps and short flicks before ten frames, all lifecycle/cancel/hybrid cases,
   Classic control and render rates 30/60/120/144. A high-speed-camera proxy must
   disclose uncertainty and actual display rate; synthetic sample accounting does
   not prove perceptual/device latency.

## Go/no-go and evidence truth

- P0 SH-00 permits independent implementation with the listed owners/tests. SH-01/02
  may proceed, but release/default qualification requires actual Android device proof.
- P1 SH-03 can proceed locally; SH-04/05 depend on grouped receipt semantics and
  existing PORT/CPP work. Echoes stays offline; extra gameplay stays off. Source
  media, rig/clips/cannon/alt-form, physical graphics and multiplayer identity remain
  separate gates even when parser, palette and synthetic GPU fixtures pass.
- P2 SH-06/08 may progress as scoped opt-in slices. Directory deployment and complete
  campaign progression/media/hardware gates remain open. SH-07 remains an experiment
  until measured signoff; SH-09 is research only and blocks no other phase.
- SH-10 release approval is **not granted**. The audit ran metadata/source-witness
  checks only. No new Android, Windows, Linux, GPU, gameplay, source import, latency,
  performance, server deployment or replay scene execution is claimed here.

Every later slice must record exact target/upstream SHA, owners reused, defaults,
commands/results, added/passed/failed/skipped fixtures, protected media/device use,
performance where relevant, protocol/replay/map effects, missing verification,
rollback and readiness. Refresh PR/main state before implementation and after merge.

SH-00 verification: `python3 -m unittest discover -s tools/porting -p
'test_*.py' -v` passed 7 tests; `python3
tools/porting/verify_integration_audit.py --upstream /tmp/prime-secondhunt-reference`
verified all 21 target witnesses, 11 commit inventories and 641 final-delta paths.
`git diff --check` passed. These checks inspect metadata and Git source bytes only.
