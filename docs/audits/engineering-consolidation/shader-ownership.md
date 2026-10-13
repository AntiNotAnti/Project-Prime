# Scene shader ownership regression

Baseline source: `91b1c0c6c44e8c90effd250729709f791a84765e` (`origin/main`).
This is a focused partial-initialization correctness fix. It does not attribute
historic process RSS growth to shaders and makes no FPS or transition-time claim.

## Proven failure paths

The original `Scene.InitShaders` kept vertex/fragment shader names in locals and
threw on compilation failures before deleting them. Even the editor factory's
`catch { scene.UnloadGl(); throw; }` could only release program fields, not these
local shader objects. All four canonical programs also lacked desktop link-status
validation, so a rejected link could publish a scene and subsequently generate GL
errors. Optional world variants already had local `finally` cleanup and remain
optional fallbacks.

A real desktop GL negative control, with diagnostic callbacks added to baseline
source before fixing ownership, exercised 14 scenarios:

- Six compiler failures (`main-vertex`, `main-fragment`, `rtt-vertex`,
  `rtt-fragment`, `shift-fragment`, `cel-fragment`) each retained shader objects
  confirmed by `glIsShader` after caller teardown.
- Four canonical linker failures (`main-program`, `rtt-program`, `shift-program`,
  `cel-program`) each returned a scene and left GL errors. The linker fixture uses
  two individually valid compiled shaders with conflicting uniform types.
- Optional variant vertex/fragment/link failure and failure after initializing
  the second variant already released their objects correctly.

The negative control reported `scenarios=14 failures=10` (expected failure).

## Ownership boundary

A shader compiler helper owns each new shader until successful return; a program
linker helper owns each new program until successful return. Both release on
exceptions. The canonical initializer keeps its reused RTT vertex shader in a
`finally` scope. RTT, shift and cel continue sharing that compiled vertex shader.
Only linked programs transfer into scene fields.

The initializer rolls back all earlier canonical programs, successful variants,
and scene color/depth/framebuffer targets if a later step fails. Normal unload
uses that same release boundary. A bound program is unbound before deletion when
this scene owns it; another scene's current program is preserved. Repeated unload
is harmless. Successful shader sources, variant policies and uniform setup remain
unchanged.

The explicitly enabled, thread-local diagnostic callback reports actual created
GL shader/program handles. It is unset during ordinary startup and is always
cleared by the regression's `finally`.

## Reproduction and scope

```bash
dotnet build tools/render-resource-check/render-resource-check.csproj -c Release
PROJECT_PRIME_GL_SHADER_VARIANTS=1 PROJECT_PRIME_GL_PLAIN_SHADER=1 \
  PROJECT_PRIME_GL_UNLIT_SHADER=1 \
  dotnet tools/render-resource-check/bin/Release/net10.0/render-resource-check.dll \
  -shadercycles 120
bash tools/check-opengl-only.sh
```

The resource harness requires a real desktop compatibility GL context and an
awake/attached display. It does not need retail game assets or initialize audio.
The default is 120 repetitions; `-shadercycles 1` gives a pilot and the accepted
range is 1–200. It also runs the existing cel framebuffer and shadow lifecycle
checks.

Each repetition exercises all 14 failure cases. Failed initialization must release
objects and zero scene target/program fields before outer caller teardown.
Optional variant compile/link rejection must still return a working canonical
scene. The final recovery sequence renders a non-vacuous synthetic editor scene,
checks exact color and depth against a reference from the same binary, preserves
an independent scene's current binding, and verifies immediate bound-program
release and idempotent unload.

This fixture covers scene canonical shader creation and optional world variants,
not driver memory caches, the entire game rendering workload, full Studio UI,
Android physical-device context loss, or real-map transition responsiveness.
Readback and repeated shader initialization are lifecycle evidence, not timing
measurements. Cross-build real-map parity and platform coverage remain separate
gates.

## Local evidence

Measured October 13, 2026 UTC, on Darwin 27.0.0, Apple M4 Pro,
`OpenGL 2.1 Metal - 91.7`, SDK 10.0.401. Client MVID:
`035cd321-ec88-4308-bf47-3c16894b1ecd`.

| Check | Result | Evidence |
| --- | --- | --- |
| Baseline negative control | PASS (expected test failure) | 14 scenarios; six shader leaks and four incorrectly accepted failed links |
| Desktop Release compilation | PASS | 30 existing warnings, zero errors; final incremental build zero warnings/errors |
| Candidate pilot | PASS | All 14 failure scenarios, one recovery frame |
| Real GL stress | PASS | 120 repetitions, 1,680 failure scenarios, zero failures |
| Recovery color/depth | PASS | 120 synthetic editor frames exactly match the same-binary reference |
| Independent binding / bound-program release / repeated unload | PASS | 120 owner/other scene pairs |
| Existing cel / Off-Low-High-Ultra shadow resource lifecycle | PASS | Existing three-cycle fixture retained |
| Dedicated server Release compilation | PASS | 22 existing warnings, zero errors |
| Linux Mesa CI shader ownership regression | PENDING | Existing `opengl-renderer-linux` job now runs all 120 repetitions under Xvfb; remote execution pending |
| OpenGL-only source/package guard and diff whitespace | PASS | Existing guard and `git diff --check` |
| Cross-build real-map image/depth parity and transition responsiveness | NOT_RUN | Synthetic recovery does not establish complete game visuals or timing |
| Android physical-device pause/resume and context recreation | NOT_RUN | Desktop GL fixture only |
| Long-run RSS/native memory boundedness | NOT_RUN | Object-release assertions are not process memory attribution |

The first candidate pilot was rejected before exercising shader failures because
a 32x32 recovery image did not satisfy the non-vacuous geometry/depth witness.
The fixture now uses the existing lifecycle harness's 320x180 layout/camera and
spatial-variation witness; the corrected pilot and full run passed. This was a
test fixture repair, not a renderer change.

Raw logs remain machine-local, without committing bulky output:

- `/tmp/prime-shader-characterization.log`: SHA-256
  `21f5dfb58b73a14da856d781ab6c8d01a9d1cff5205f134120e078ba1c054396`.
- Rejected pilot `/tmp/prime-shader-candidate-pilot.log`: SHA-256
  `3e4885e250b6a6db20ede47c603c4fc6b8aa940193035a5ec02e335d70673393`.
- Corrected pilot `/tmp/prime-shader-candidate-pilot2.log`: SHA-256
  `17ff5b527ad281ac06787468db28fbb741ebb8e8dc1f0977f06880dcd9d2df81`.
- `/tmp/prime-shader-candidate-120.log`: SHA-256
  `8195a8e1e237005f604f9c350e93615e512b79cc081a338a2665b34a720ea297`.
- Tested harness SHA-256:
  `edc81919a39e72a913c96300dbdaa8652d364d37ab184d5956bd69028e8912a7`.
- Tested client SHA-256:
  `f8b7527aeb975a012fbe747f0d92d47ff36c08b36e09bfbb89c5e76ed137c1c8`.
- Recovery RGB SHA-256:
  `085C37F72243AED3A5DF919D8FBC382D3F826D9F40C1019D06C3B3CAB27FC16F`.

The final run set `PROJECT_PRIME_USER_DATA=/tmp/prime-shader-test-user-data` and
explicitly enabled the three documented shader policies. Other repository builds
ran concurrently; neither compile durations nor emitted per-variant stopwatch
lines are performance evidence.

Rollback is the single shader-ownership commit. No protocol, replay schema,
settings, simulation cadence, GC policy, or graphics optimization defaults change.

The Linux CI step explicitly enables the three shader policies, isolates user data
under the runner temporary directory, and executes this same resource harness in
a 640x480 Xvfb display after installing software OpenGL. Remote CI results have
not yet been observed.

## Bound auxiliary program follow-up (October 13, 2026)

The scene-wide release guard recognizes canonical and variant programs, but
`UnloadGl` disposes graphics-compositor and player-outline programs first. An
explicitly current auxiliary program was deleted and its field zeroed before the
guard could recognize that name. A real GL negative control against production
source `0273aa86c44772e728e069ebcd95e04f9c0b2590` reproduced both cases: the owner
field was zero, `glIsProgram` remained true, the program was still current, and
`GL_DELETE_STATUS` was 1. GL errors were absent. Unloading another owner preserved
the current auxiliary binding, as required. These were two additional expected
negative-control failures; the original 14 failure scenarios still passed.

The existing `DeleteProgram(ref)` helper now unbinds only when the actual current
program equals the owned name being deleted. This closes both auxiliary release
paths before the field is zeroed and also preserves an independent owner's
binding. Repeated teardown remains harmless. Shader creation, successful drawing,
uniform state, settings and defaults are unchanged.

The corrected actual desktop GL run passed 120 repetitions: 1,680 injected
compile/link/initialization failure scenarios, 120 canonical owner pairs, and
240 auxiliary owner pairs using the real lazy allocators. Every auxiliary name
was recognized before teardown and immediately unrecognized afterward, with
current program 0, delete-pending 0, and no GL errors. Other-owner bindings and
idempotent unload passed. Existing cel and Off/Low/High/Ultra shadow lifecycle
checks also passed.

The same immutable final harness was run with the original and corrected client
DLLs. The non-vacuous synthetic reference color and depth hashes matched across
builds; all 120 candidate recovery frames exactly matched that reference:

- RGB: `085C37F72243AED3A5DF919D8FBC382D3F826D9F40C1019D06C3B3CAB27FC16F`.
- Depth: `6A87A00CE12CE45AB9EF0C5DDC66F51D1AE76EB7E42201E5E07869109E889E46`.

Desktop and dedicated-server Release builds passed (30 and 22 existing warnings
respectively, zero errors; final desktop incremental build zero warnings/errors).
The OpenGL/GLES source/package guard and whitespace check passed. One command
used a nonexistent separate server project; its MSB1009 log is preserved, and the
correct `MphRead.csproj -p:MphReadServer=true` build passed without source repair.
Exact commands, source-file, binary and log hashes, all negative controls, and
scope limits are in [auxiliary-shader-ownership-receipt.json](auxiliary-shader-ownership-receipt.json).

This is a proven owner-lifetime correction, not an attribution of the independent
AGX compiler-string population. Frozen `edcf0f1b` normal-workload controls retired
all observed owned program names, yet direct AGX malloc-origin groups persisted
unchanged through context disposal, GLFW event polling and final termination.
Those stacks establish allocation origin and lifecycle persistence, not retaining
roots, cache classification or long-session boundedness. No driver cache or GC
policy change is justified by this follow-up. New full-game visual acceptance,
physical Android lifecycle and driver boundedness remain separate gates.
