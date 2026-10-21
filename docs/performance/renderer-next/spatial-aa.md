# R23 spatial-AA experiment

Prepared 2026-10-21. Implementation and Windows focused validation complete for
this narrow candidate; **NO_ROLLOUT** pending real-scene still/motion, thin-geometry
and frame-tail acceptance. Retired AA settings remain inert and defaults remain
unchanged.

## Contract and ownership

`PROJECT_PRIME_GL_SPATIAL_AA=1` enables a five-tap edge-adaptive spatial filter.
It reads and writes **native-encoded RGBA8**, preserving flat colors and center
alpha. Alpha-discontinuous neighborhoods bypass filtering. It uses texel-center
samples with explicit edge clamping and never changes source texture filters or
samplers. This is a small experimental spatial candidate, not branded FXAA,
temporal AA, linear-light AA or an HDR path.

The pass runs after world/outlines and **before SceneOverlays**. A single RGBA8
scratch target receives the filtered world, then an exact-size color copy
returns it to the established `_screenTexture`. Preview models, helmet/damage
models, native HUD/text and launcher UI render afterward and remain sharp.
The copy preserves existing scene target/readback dimensions, framebuffer,
depth/stencil and encoding descriptors. Its bandwidth cost is included in the
measured candidate, rather than hidden in composition accounting.

The scratch target participates in the aggregate scene byte budget, including
old/new replacement overlap. Texture, framebuffer and optional program publish
only after successful allocation/link and framebuffer validation. Failed
replacement retains the previous valid owner; the same size is not retried each
frame. Changed size permits a new attempt. Oversize/capability/budget failure
falls back to the original world. Scene unload retires all three owners.
Incoming program, framebuffer bindings, viewport, active texture/binding,
write masks, enables and desktop polygon mode are restored.

GLES alpha-test state is emulated. `GlEs.IsEnabled` now reads that emulated state
and returns false for ignored legacy capabilities, preventing a native GLES
invalid-enum query while saving the AA boundary. Android physical tests remain
NOT_RUN under the user's Windows-only constraint.

## Actual Windows checks

Executed with `C:\Users\Jarrett\.dotnet\dotnet.exe` SDK 10.0.401:

```powershell
dotnet build tools/spatial-aa-check/spatial-aa-check.csproj -c Release
dotnet tools/spatial-aa-check/bin/Release/net10.0/spatial-aa-check.dll
dotnet tools/spatial-aa-check/bin/Release/net10.0/spatial-aa-check.dll --enabled
```

Build PASS, with one existing unrelated CS0649 warning. Both native executions
PASS on NVIDIA GeForce RTX 5080, NVIDIA 617.42, OpenGL 4.6.0. The asset-free tool
exercises the production implementation with native GL storage, compilation,
rendering and readback. Checks cover:

- Default off: no AA owners allocated; original RGBA pixels unchanged.
- Flat RGB/alpha byte parity; diagonal smoothing limited to edge neighborhoods;
  opaque alpha retained and input repeat-wrap cannot contaminate borders.
- Alpha-discontinuous neighborhoods unchanged; one-pixel post-AA overlay sharp.
- Dirty incoming texture unit, write mask, wireframe and enable state restored.
- Injected pre-publication target failure retains valid storage; unchanged-size
  retry suppressed; changed-size recovery retires old storage.
- Aggregate budget refusal and complete shader/texture/framebuffer disposal.
- Warmed production AA path records zero managed allocations across 32 calls.

The fixture was subsequently rebuilt with `--no-restore -p:BuildProjectReferences=false` and rerun using a bound production-method delegate, excluding reflection lookup from CPU timing. This follow-up passed all checks and added the warmed allocation check. Its authoritative synthetic 1280x720 diagonal-edge fixture has 30 measured calls:

| Scope | p50 ms | p95 ms |
|---|---:|---:|
| CPU pass + exact-size copy submission | 0.0412 | 0.0632 |
| GPU pass + exact-size copy | 0.015744 | 0.019104 |

Scratch storage: **3,686,400 bytes**. Uploads and explicit test completion waits
are excluded. Production uses no synchronous GPU query wait. This isolated
fixture does not establish gameplay frame-time gains, end-to-end cost, image
parity, temporal stability, 1080p/4K behavior or Android/macOS/Linux acceptance.
The binary MVID for these observations is
`daab2773-ac08-4f85-90ba-1adb462b1e5b`.

Evidence root:
`C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-next-20261021`.
Retained files: `spatial-aa-build.log`, `spatial-aa-default-off.log`,
`spatial-aa-fixture-build.log`, `spatial-aa-direct-enabled.log`,
`spatial-aa-enabled.log`, and `spatial-aa/{cost.json,diagonal-original.png,
diagonal-spatial.png}`. Both PNGs were visually inspected. Native still/motion
comparisons with thin wires, fences, foliage, scaled scenes and real HUD layers
remain the next acceptance gate. The Original option remains available by
leaving the experiment off.
