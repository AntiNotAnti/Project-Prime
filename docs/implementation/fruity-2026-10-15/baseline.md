# Fruity Prime October 2026 backport: verified baseline and acceptance ledger

**Project Prime starting point:** `dfa987b1c55f57ea86253cd5010b5ab0879828d9` (main after #580)
**Fruity Prime comparison point:** `86019836fd5feceba03188caee5db7858c5c6dab` (master, merged #156)
**Wire baseline:** `NetConfig.ProtocolVersion = 56`.
**Last verified main CI:** build #38843710433, campaign acceptance #38843710423, scene ownership #38843710416, semantic event foundation #38843710452, all successful.
**In flight:** Project Prime #561 (draft native Radiant). Do not alter or assume merged.

This is a baseline ledger, **not** a claim of post-patch, hardware, or performance acceptance. Actual benchmark samples must be collected on supported real devices. Do not invent numbers from unit tests.

## Candidate ledger

| Upstream | Prime file/contract | Finding | Decision | Evidence gate |
|---|---|---|---|---|
| Fruity #129 server discovery | `Launcher/Portable/ServerBrowserService.cs`, `Launcher/Gui/PlayScreen.cs` | Confirmed endpoint persisted before join; stale status callback can outlive refresh | PR-01 | failed join preserves last good endpoint; overlapping results fenced |
| Fruity #137/#138 map vote and readiness | `Network/DedicatedServer.cs`, `MapGen/CustomRooms.cs`, `MapBuildManifest` | Vote proposal accepts resolved map without complete build preflight; start has late validation | PR-02 | no bad room committed; original match stays responsive |
| Fruity #155/#156 high Hz animation | `Formats/Model.cs`, `Entities/EntityBase.cs`, `Mods/Render/FrameTiming.cs`, `HunterPreview.cs` | Fixed animation tick and draw data share mutable state; high-Hz channel blending absent | PR-03–06 | 30/60-Hz canonical parity, read-only intermediate draw |
| Fruity #145/#151 homing shots | `Network/NetFireEvents.cs`, `NetShotInputProof.cs`, `NetHitClaims.cs`, `NetUnlagged.cs` | Existing authority and historical validation; new malformed-origin/cancel cases require audit | PR-07 | adversarial historical origin and lifecycle matrix |
| Fruity #144 demo intent | `ReplayTimeline/ReplayRecorder.cs`, `ReplayTimedShotFacts.cs` | Different authority-fact recorder; no direct payload slicing match | PR-08 test-first | own/mirrored missiles, bombs, shot rays survive record/seek |
| Fruity #125 Quake movers | `MapGen/Q3Gameplay.cs`, `Entities/Players/PlayerQ3Traversal.cs` | Typed push/teleport present; func_* movers explicitly not simulated | PR-09 | multiplayer, collisions, replay and 7-hunter traversal |
| Fruity #125 Quake shading | `MapGen/Q3Import.cs`, `MapGen/MapTextureBake.cs`, OpenGL renderer | Source shader stages/fog not at reference fidelity | PR-10 | paired GL captures, alpha/depth parity, no licensed packs |
| Fruity #143 Android updater | `Mods/Update/UpdateCheck.cs`, `SyncHttp.cs` | Managed HttpClient, not upstream libcurl; no evidence same bug | PR-11 verify | Android release sees newer release, TLS errors surfaced |
| Fruity #127 transparent HUD | `Renderer.cs` `DrawHudLayer` | Already returns for BindingId=-1 or Alpha<=0 | **Skip** | existing GL tests |
| Fruity #141 vertex weld | `MapGen/MapCollisionPacker.cs` | Existing wc02 32-bit extended output, no lossy 16-bit weld required | **Skip** | map-editor-check, collision-query-check |
| Fruity #140 instagib rail | `Mods/Render/InstagibIonFracture.cs` (#563) | Unique retained effect; no reason to replace with helix | **Skip** | existing Ion Fracture tests |
| Fruity #150 movie scene lifetime | Project Prime #577 | Separate owned VX decoder and cancellation already landed | **Audit only** | campaign acceptance |

## Animation ownership inventory (PR-03 review contract)

- `EntityBase.UpdateAnimFrames` calls `ModelInstance.UpdateAnimFrames` every second 60-Hz scene step; inspect exceptions (pause, bosses and draw-owned animation).
- `ModelInstance.UpdateAnimFrames` owns two slots, reverse/ping-pong/no-loop/paused/ended flags. Only actual ticks may change it. Sampling must never advance canonical ticks.
- `EntityBase.UpdateTransforms` calls `AnimateMaterials`, `AnimateTextures`, `ComputeNodeMatrices`, `AnimateNodes` and `UpdateMatrixStack`. These currently mutate shared model/material values. Do not use these for unguarded draw-only lookahead.
- `FrameTiming.PresentationAlpha` already owns fractional render progress; no second frame limiter or simulation cadence.
- `HunterPreview.Step` currently calls `UpdateAnimFrames` once per invocation. Measure effective cadence before changing clock ownership; cosmetic time advances by 1/60.
- Animation sampling must reset on scene unload, model pool reuse, animation/group change, HUD/context loss, new Hunter and replay seek.
- Turrets, collision platforms, hit and muzzle origins, bosses with material-gated gameplay, replay state and projectiles require canonical state even while blending is enabled.

## Test and benchmark matrix

| Scenario | Commands / evidence | Pass criterion |
|---|---|---|
| Portable build | `dotnet build src/MphRead.sln -c Release` | no new compile errors |
| Network authority/replay | `dotnet run --project tools/nettest -c Release -- --health-shot`; replay format check and existing focused nettest runners | no regressions; protocol remains 56 |
| Map/editor | `dotnet run --project tools/map-editor-check -c Release`; `dotnet run --project tools/map-multiplayer-check -c Release -- <temporary-directory>` | no map-format/cache/identity regression |
| Collision | `dotnet run --project tools/collision-query-check -c Release -- --compare docs/audits/engineering-consolidation/collision-query-baseline.txt` | same hit facts |
| Frame-pacing and native GL | existing `-glprofile -glgpu`, `tools/opengl-phase4/analyze.py`, paired `-glvisualcheck` | measured parity, no GL errors |
| Animation | new focused asset-free flag in PR-03, 24/30/60/75/120/144/165/240/360/540 Hz | equal canonical frame counts/flags and simulation hashes |
| Android | release emulator startup + real device where available | no UI/input or missing-map regressions |

Collect paired **same build, same camera, same map, same quality/cap, same machine** samples with no background load:
render p50/p95/p99, GPU timing when supported, GL submissions, allocations per presented frame, CPU render p95/p99, dropped sim steps, max lifecycle memory and visual captures. Use four adjacent 240-frame windows per comparison. Mark any unsupported graphics measurements `NOT_RUN`, never `PASS`.

## Compatibility, publication and source provenance

- No changes to network protocol 56, replays, package identities or `.ppmap` format without separately reviewed proof.
- Retain post-rollback approved #563/#566/#567; do not resurrect #562, #564–565, #568–570, #573–576.
- Use existing Avalonia launcher and OpenGL/GLES. Do not require RmlUi, Vulkan, DX12 or Qt.
- Keep map input external. Nintendo/Quake textures, fonts, commercial map packs and ROM-derived assets are not accepted as new repository contents.
- Avoid directly copying native C++ implementations. Adapt tested behavior to maintained C# systems; verify third-party license before literal port.
- PR-01, PR-03, PR-07, PR-11 can be independently branched. PR-02 precedes PR-09. PR-03..06 are sequential.
- #561 is not a foundation for runtime PRs unless explicitly rebased/merged. No code from this series should edit #561 implicitly.

## Gate ledger

- **Verified before changes:** pinned main SHA, protocol, upstream reference, open PR and successful baseline CI.
- **NOT RUN in this review:** local `dotnet` builds (GitHub connector does not expose a runtime/checkout), real hardware frame pacing, Android device, multiplayer match playback.
- **PR-00 completion:** this source-baseline inventory and acceptance contract delivered. Hardware baseline capture remains a prerequisite for enabling animation globally; it is not inferred from CI.
