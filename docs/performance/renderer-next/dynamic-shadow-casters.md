# Detached dynamic shadow caster qualification

Prepared 2026-10-22 on Windows. Base source is `46299080ba441970a6f32ce3acf74a8a65f1b7a6`; the qualification worktree adds the Parkour compatibility fix and fixture changes described below. The final snapshot manifest, rather than the base commit alone, identifies the tested source.

The candidate is explicitly opt-in with `PROJECT_PRIME_GL_DYNAMIC_SHADOW_CASTERS=1`. Global force-legacy wins. No graphics quality, resolution, frame cap or default is changed. This is functional qualification of a bounded candidate, not a performance or Android-device acceptance result.

## Source contract and limits

The ordinary single `PlayerEntity.Draw` or `PlatformEntity.GetDrawInfo` visit prepares an omitted caster. It does not call either draw callback a second time, advance an animation, process an entity, submit a camera-visible packet, allocate a polygon ID, or drive a weapon/effect callback.

[`DetachedShadowPoseCache`](../../../src/MphRead/Mods/Render/DetachedShadowPoseCache.cs) owns mutable CPU copies keyed by entity owner, source `ModelInstance` and source `Model`. Distinct animation channels sharing one model therefore remain distinct. Every preparation refreshes the current source node/material state, animation cursor and borrowed geometry handles. Immutable asset definitions remain shared. The cache owns no GPU lease. Its named limits are:

| Limit | Value |
|---|---:|
| `MaxRigs` | 128 |
| `MaxNodes` across owned rigs | 8,192 |
| `MaxMeshes` across owned rigs | 4,096 |
| `MaxMaterials` across owned rigs, including unused materials | 4,096 |
| Shader palette per rig | 32 matrices |
| `MaxDetachedShadowPackets` per picture | 2,048 |
| Source instructions inspected for a geometry envelope | 65,536 |

Unused prior-picture rigs may be evicted. Same-picture capacity exhaustion refuses further detached preparation; the ordinary visible caster path remains available. Bounds and collection identity are refreshed each picture. Presentation-generation changes, shadow quality Off, pause collection and scene unload clear detached ownership. LOD/source-model identity changes select a separate pose; borrowed driver handles are copied again rather than cached as owners.

[`DetachedShadowCasters`](../../../src/MphRead/Mods/Render/DetachedShadowCasters.cs) routes shadow-only packets into their own bounded queue and returns rejected pooled packets. [`DetachedShadowBounds`](../../../src/MphRead/Mods/Render/DetachedShadowBounds.cs) decodes native source vertices and conservatively encloses the envelope under all packet palette matrices. Selection uses light-space X/Y, independently of camera/portal visibility. Unknown, over-budget or HD geometry bounds remain included. The current light pass retains its existing orthographic coverage, texel snapping, PCF and range/bias policy; there is no static shadow-map reuse or cascade addition.

Supported preparation paths are player biped, ordinary alternate form, Kanden segments, Spire attack, an already valid private drone owner, frozen body/alternate shells, rigid and weighted HD geometry following the native pose, ordinary platforms, and the Delano invisible-rig/ship/turret dependency chain. The private gameplay getter can retire a stale owner, so detached preparation uses a raw rule read after a read-only validity/thread guard and declines an invalid owner. Native/GX source previews already require shadows Off in their presentation eligibility contract; this candidate does not add source-preview shadow support.

The detached queue excludes viewmodels, alpha below the opaque threshold, decals, source-GX programs, billboards and wireframes. Weapon lasers, trails, particles, death callbacks and cosmetic attachment emitters are not invoked by this preparation. Alpha-textured opaque fragments may cast using the existing alpha-equals-one shadow pass. Generic non-player, non-platform entities still use the ordinary camera-visible caster packets; this implementation does not claim a detached offscreen collector for every entity subclass.

## Current upstream compatibility repair

Current visible biped submission applies `ParkourPoseOverlay` before `HeadHitPoseOverlay`. The interrupted candidate applied only the latter, producing a different offscreen silhouette for mantle/grapple/wall presentation.

The only resumed R21 production changes are clone-profile prewarming in `DetachedShadowPoseCache` and the same ordered Parkour/head-hit scopes in [`PlayerDetachedShadows`](../../../src/MphRead/Entities/Players/PlayerDetachedShadows.cs), using the exact biped world matrix. Both scopes operate on the private clone. Existing picture-only pose sampling remains the owner of the visual cue; source skeletons, animation frames, accepted movement, network state, collision anchors and effect lifetimes are unchanged by the collector.

## Windows Release evidence

The real Windows OpenGL context reported **NVIDIA GeForce RTX 5080**, OpenGL **4.6.0 NVIDIA 617.42**. SDK: `C:\Users\Jarrett\AppData\Local\ProjectPrime\dotnet-10.0.401\dotnet.exe`.

| Result | Assembly identity |
|---|---|
| Initial resumed Release, expanded pre-Parkour fixture: **PASS 195** | MVID `9ad6b7d3-e440-41d7-b320-e1e8504181f7`; SHA-256 `93f83a6b0167374898965bf0fc9b728220dc70060c5953e313f3fc9b6bf471b6` |
| Corrected independent Parkour regression against that initial DLL: **expected FAIL** | The visible overlay witness passes; `detached_matches_visible=False`; actual visible/detached packet parity fails |
| Fixed Release, complete synthetic fixture: **PASS 215** | MVID `5d14b5fe-80be-41a1-838a-b3c59a02d46f`; SHA-256 `f22d19de70681de8e7a1dfa48ec514e96969904a83a3e2cf4c99ba8c19d12e74` |

The negative run uses the same corrected fixture executable with the initial copied DLL in an isolated `tools/renderer-preparation-check/bin/Release/parkour-before` staging directory. Its positive witness checks the actual visible shoulder packet with the overlay enabled versus suppressed before comparing the detached packet. This distinguishes a missing detached overlay from an insensitive synthetic rig. The fixed run reports both the visible witness and detached parity passing.

The fixture invokes the actual offscreen player/platform hooks, native/rigid/weighted HD emitters and production shadow pass. It proves source node/material/cursor preservation, distinct-instance identity, native camera flags and polygon IDs, private drone shot/laser counts and stale-owner refusal, existing player/platform effect visibility and anchor state, Delano dependency order, frozen body/alternate parity, LOD changes in both directions, generation reset, bounded rig/material/packet storage and borrowed GPU lifetime. The fractional-curve case uses real `AnimationSamples` history and `FrameTiming` presentation phase, compares the visible/detached packets and proves that a canonical-only control differs. No scene simulation step is paid by those draws.

The native offscreen body produces **28 covered depth pixels in a 1,024-square shadow target**, and its complete depth image equals the ordinary camera-visible replay. This is a small synthetic equality check, not a full-game visual or frame-time result. The Parkour cue is supplied through the existing replicated visual seam; it is not a test of network admission or real mantle/grapple input acceptance.

Evidence files in this directory:

- [Final application/tool Release build](build-dynamic-shadow-release.log): zero errors; two pre-existing upstream warnings, `CS0618` in `HudStudioView` and `CS0649` in `NetLobbyTest`.
- [Final fixture-only build](build-dynamic-fixtures-final.log): zero errors/warnings.
- [Initial expanded Release result](dynamic-shadow-initial.log).
- [Corrected independent negative result](dynamic-shadow-parkour-negative.log).
- [Final Release result](dynamic-shadow-release.log) and [machine-readable receipt](dynamic-shadow-release-receipt.json).

Earlier `dynamic-shadow-parkour-before.log` is an inconclusive witness-construction attempt, not the defect reproduction. `dynamic-shadow-release-fixture-failure.log` records a fixture LOD-holder alias that was corrected to match the separate production LOD instances. Neither is a product failure or timing sample.

Commands, run from the isolated worktree on Windows:

```powershell
$env:DOTNET_ROOT = 'C:\Users\Jarrett\AppData\Local\ProjectPrime\dotnet-10.0.401'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
& "$env:DOTNET_ROOT\dotnet.exe" build tools/renderer-preparation-check/renderer-preparation-check.csproj -c Release --no-restore -p:NuGetAudit=false -m:1 -v:minimal
$env:PROJECT_PRIME_DYNAMIC_SHADOW_RECEIPT = Join-Path (Get-Location) 'docs/performance/renderer-next/dynamic-shadow-release-receipt.json'
$env:PROJECT_PRIME_QUALIFICATION_COMMIT = '46299080ba441970a6f32ce3acf74a8a65f1b7a6+R21-local-patch'
& "$env:DOTNET_ROOT\dotnet.exe" tools/renderer-preparation-check/bin/Release/net10.0/renderer-preparation-check.dll --dynamic-shadow-native
```

Subsequent fixture-only rebuilds used `-p:BuildProjectReferences=false` against the copied Release DLL. The final snapshot qualification may rebuild unchanged source and should record its own manifest and assembly identity.

## Remaining qualification

Real maps with moving camera/light-space boundaries, thin geometry, offscreen moving actors/platforms, and sustained CPU/GPU/memory cost have not been qualified by this synthetic fixture. Android runtime, device visuals and cost are untested here; a Windows-hosted Android build or source check does not establish them. Private source-GX shadow support and detached preparation for other entity subclasses are outside the implemented source scope. Keep the candidate opt-in and retain these distinctions in the R21 status.

## Final integrated rerun

The final v4/v5 client (MVID `53589a2c-2a2a-423c-94cc-24d50b7f6355`, SHA256 `26906006a69c2275eb385e58ad1c1007167c137ec43b3eb541c74c2b73cbe729`) reruns all215 checks successfully, including the visible Parkour witness and detached parity. See [integrated log](windows-dynamic-shadow-v4.log) and [receipt](dynamic-shadow-v4-receipt.json). The complete v5 frozen bundle reproduces v4 byte for byte; broader motion/device/cost limits above still apply.
