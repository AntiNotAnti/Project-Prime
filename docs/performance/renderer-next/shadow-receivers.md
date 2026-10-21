# Shadow receiver boundary and stability work

R20 implementation and focused Windows qualification; R21 partial implementation.
This is a receiver correctness repair to the existing user-selected Shadows mode.
Shadows Off keeps the independent canonical world shader. No stencil bits,
simulation timing, selected cap, scale or default shadow quality are changed.

## Receiver contract

| Surface | Reception |
| --- | --- |
| Standard opaque room and mesh actors/platforms | Enabled in existing opaque pass |
| Alpha-textured/cutout mesh | Only alpha==1 portions in opaque pass; original coverage test preserved |
| Transparent portions, decals | Disabled in their original passes |
| Viewmodel/arm cannon, procedural muzzle/beam/particle/trail effects | Disabled |
| Emissive material map | Added after shadowing base/direct-light color |
| Explicit lit native emissive object | Conservative whole-object receiver exclusion |
| Player/cel outlines, scene overlays, 3D HUD, final HUD | Outside receiver program scope |
| Native pause map | Original orthographic/view uniforms preserved; reception disabled |
| Custom source-GX shader objects | Excluded until their separate shader contract supports receivers |

`ShadowReceiverPolicy` is part of exact retained material identity, so packet
grouping/shared-state proofs cannot silently fuse different effective receivers.
The receiver uniform is refreshed per item even when other material/object setup
is elided. Each six-pass iteration enables reception only for Opaque. The full
native `0xFF` polygon-ID stencil protocol is unchanged.

The World frame-graph node reads ShadowDepth and uses the real interpolated
world `surface_position`/`surface_normal`; it never reconstructs an arm-cannon
fragment through world projection. Shadow multiplication occurs before authored
emissive addition and before late cosmetic/outline/HUD output. The old fullscreen
postmultiply is disabled, including when inline reception fails.

## Program and context ownership

The optional receiver fragment source derives from the full canonical material
source. Desktop GLSL120 and GLES300 share the receiver body, use their respective
sampling function and give GLES shadow-depth sampling explicit high precision.
The translated source participates in the existing shader binary identity.

One optional WorldShaderProgram belongs to each Scene; it is never borrowed by
another scene/context or cached globally. Full, native, plain and unlit prior
programs remain independently owned. Begin saves the current world bundle;
End restores that exact bundle, active texture unit and unit4 binding. Unit4 is
re-established after custom source-GX draws. Optional teardown runs before
canonical scene-program cleanup. The texture-unit capability and complete uniform
bundle are validated before reception is enabled.

Failed compile/link/initialization releases pending objects and restores native
unshadowed rendering. It emits an explicit effective-setting diagnostic. Failure
is bounded per selected shadow quality/scene target size, and Off or a descriptor
transition permits deliberate retry. Shadow-map refusal uses the same explicit
quality/scene-size transition principle; scene/context teardown resets optional
owners. A successful receiver program stays scene-owned across ordinary toggles,
avoiding repeated compile/link work.

## R21 implemented and remaining

Existing texel snapping, light-space room collection and nine-tap PCF remain.
The receiver adds a four-texel border/depth-range fade and a bounded derivative
footprint contribution to the existing normal/slope bias. Alpha-textured dynamic
mesh casters now retain alpha==1 cutout coverage instead of being rejected merely
because their material is classified Translucent; procedural effects are excluded
from that caster replay. These changes do not qualify motion shimmer, acne or
detachment on real maps by themselves.

**Offscreen dynamic collection remains implementation pending.** The source root
cause is specific: `ShadowMap.RenderShadowMap` replays camera-produced dynamic
`_retainedRenderWorld.Opaque` packets, while only rooms have a separate
`CollectLightSpaceShadowCasters` source. `Players/PlayerDraw.cs:230` rejects body
emission using camera portal visibility. `PlatformEntity.GetDrawInfo` rejects
camera-invisible platforms, changes `_animFlags.WasDrawn`, and calls
`EffectEntry.SetDrawEnabled`. `EntityBase.UpdateTransforms` mutates model animation,
material, node and matrix state. Player Draw composes separate biped, alt-form,
gun/smoke and presentation/network/replica poses with owner-specific rollback.
Re-invoking these callbacks under a light camera would duplicate effects or alter
draw-dependent gameplay facts; forcing global visibility would also alter world
submission. Neither shortcut was introduced.

The needed API is a single-owner, once-per-picture detached dynamic body/platform
pose snapshot prepared before camera rejection. It must expose bounded shadow
mesh/material/palette packets without invoking UI, effect, collision or animation
advancement callbacks, with explicit generation invalidation and a separate
light-frustum/AABB test. Actor skin/form transitions, child platforms and material
alpha must retain their existing presentation ownership. Reuse is permitted only
after full pose/geometry/light generation identity is established.

Required native completion fixture: place a moving actor and a translating/rotating
platform outside the camera/portal volume but inside the directional light volume,
casting onto a visible wall/floor; cross the camera and light boundaries while
turning at 120/144/240 Hz, then change actor form, replay seek and scene generation.
Compare caster silhouettes/receiver pixels and simulation hashes, effect birth
counts, platform WasDrawn/state and polygon ordering against ordinary collection;
measure light-collection CPU/GPU cost and bounded packet memory. A camera-visible
caster fixture cannot satisfy this gate. Static caster reuse remains NO_ROLLOUT.

## Windows evidence

Actual commands (native Windows PowerShell, .NET SDK 10.0.401):

```powershell
dotnet run --project tools/android-pacing-check -c Release
dotnet build tools/renderer-preparation-check -c Release
dotnet tools/renderer-preparation-check/bin/Release/net10.0/renderer-preparation-check.dll --native
```

The linked Android policy/source harness executes on Windows; it is not an Android
device run. `shadow-gles-source-final.txt` records PASS for canonical/translated
source identity, optional receiver translation and shadow-before-emissive order.
The first source-sync run correctly rejected the changed R22 canonical hash; its
local `shadow-gles-source-first.log` is retained, then the verified desktop/GLES
pair received its updated hash.

`shadow-receivers-native-first.txt` records PASS, 14,817 combined preparation and
receiver checks on NVIDIA RTX 5080/OpenGL 4.6 NVIDIA 617.42, application MVID
`daab2773-ac08-4f85-90ba-1adb462b1e5b`. It compares actual canonical/receiver
fragment pixels for opaque, bright authored emissive, viewmodel, transparent,
decal, procedural effect, outline and cutout cases; exercises the actual Scene
optional program boundary, per-item uniforms, program/texture-state restoration,
injected compile failure and explicit Off->Low recovery. Subsequent final-run
assertions add range fade, preserved stencil state, owner teardown and durable
synthetic PNGs; the final integrated receipt identifies that build separately.

Independent review identified a native pause-map projection regression during the
new program switch. The added uniform-query fixture reproduces it against the
previous built DLL in `shadow-receivers-native-prepause-fix.txt`. The source repair
reapplies SetPauseMenuUniforms after switching programs, including its actual
material-camera update, and excludes pause UI from reception. The final integrated
run closes that negative control; the failed receipt remains available.

Final integrated result: `build-shadow-receivers-final.txt` **PASS**, zero errors
and one existing CS0649 warning; `shadow-receivers-native-final.txt` **PASS**, 14,822
checks, application MVID `220155ea-3f7c-4165-8228-82715dbdf68e`. The new range-fade,
full-stencil-mask/reference, optional program deletion and pause view/projection
assertions all pass. The nine synthetic PNGs and their receipt were moved after
verifying both absolute paths to the local evidence directory
[`shadow-receiver-images`](C:/Users/Jarrett/AppData/Local/ProjectPrime/renderer-next-20261021/shadow-receiver-images).
The repository retains `shadow-receiver-image-receipt.json` and
`shadow-receiver-image-hashes.json`, including the SHA-256, byte length and local
destination of every image/receipt, bound to that MVID. The fixture initially
writes under `docs/performance/renderer-next/shadow-receiver-images`; relocate
that generated corpus before committing future evidence. Current combined CPU allocation
negative control/candidate remain 38,400,000/zero bytes; native geometry remains
five/four compiled objects and 960/768 VBO/IBO bytes in the controlled fixture.

All other-platform native tests, fast real-map motion, offscreen dynamic caster,
FOV/multiview/Studio transitions and comparative frame/pass cost are NOT_RUN or
UNVERIFIED. This evidence establishes focused receiver behavior, not universal
image parity, improved shimmering or a production FPS gain.
