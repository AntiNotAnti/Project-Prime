# Cinematic menu motion and layout pass — 2026-10-08

Base: `75879308` on main, including merged native launch recovery #396, shader
contract fixes #398 and lobby/session/replay polish #399. This pass preserves the
requested removal of the large background halo and overhead square rail, and
keeps Quick Play hidden. Reactor movement is expressed through the floor rings,
structural channels and light assemblies instead.

## Architecture audit and implementation

| Subsystem | Finding | Implemented behavior |
| --- | --- | --- |
| Chamber shader | Float uptime from TickCount loses subframe precision after long uptime; instantaneous activity parameters can jump phase. | A monotonic, accumulated double presentation timeline starts at zero. Activity speed integrates into a separate continuous energy phase. Colors and scalar presets settle exponentially over ~180 ms. |
| Stage FX | Separate clocks and immediate palettes disconnected atmosphere from chamber transitions; motifs were hard to distinguish. | Shader, fog, scanner, service lamps, dust and Hunter motifs share the presentation clock/palette. Motifs crossfade with seven weights; foreground strength stays restrained. |
| Hunter previews | The render loop presented poses advanced at 60 Hz. | Sample isolated preview idle-node scale/translation/shortest-angle rotation between fixed poses at render time. Paused, terminal, reverse and ping-pong clips retain their fixed pose semantics. Gameplay instances default to zero interpolation. |
| Focus and suspension | Wall-clock motion could jump forward after an inactive window or suspension. | Focus loss freezes presentation time; gaps over 250 ms do not fast-forward. Resume continues the last pose. Switching preview clock domains resets that preview's schedule, not gameplay clocks. |
| Environment | Static struts, largely stationary rings and subdued accents. | Slow gantry oscillation, phased conduits, traveling structural illumination, independently counter-rotating segmented floor rings and broad surges. Existing parallax, fog, contact shadows, technical side bays and reflected light remain layered around the Hunter. |
| Hunter identity | Palette changes were abrupt; atmospheric motifs had little visual weight. | Amber stable Samus circulation; green Kanden bioelectric bands; crimson angular Trace scans; blue Sylux traveling conduits; icy Noxus glints; warm Spire thermal motion/embers; asymmetric Weavel industrial scans. Existing model materials and equipped palettes are retained. |
| Lobby state | Platforms and characters did not share a precise projected ground anchor; rear label collided with status. | Model origin is projected onto the shared formation pad. Centered viewports/nameplates, smaller rear perspective, compact Squad Status, density-aware label exclusion. Picker previews explicitly opt out of formation correction. |
| Lobby motion | Platform state snapped to occupied; no one-shot ready energy confirmation. | Presentation-only occupancy ramps, materialization/dematerialization, per-slot palette interpolation and independently phased rings. Ready changes generate one decaying pulse. Departure retains only the previous display model during the fade. Authoritative roster/order and commands remain immediate. |
| Countdown | Environmental activity was disconnected from deployment. | Server Starting/countdown facts drive a smoothed launch-energy target. Correction, cancellation, leaving or returning changes that target; no local match timer or map loading change. |
| RmlUi microinteractions | Existing entry/drawer/nav motion already uses a native double steady clock and retained documents. | Shared button color/border easing, occupied-nameplate arrival and ready confirmation. Existing focus/input ownership and page retirement stay intact. No background document remains interactive. |
| Hunters | Top navigation opened a modal. | Full content page with shared shell navigation and fixed footer actions; live lobby keeps its compact model strip. Selection button styles are scoped to the Hunter panel. |
| Community | First public request was cold; installed badge lookup could initialize/hash packages on the UI thread. | Warm a 30-second public catalog cache and installed index off-thread. Browse waits asynchronously for index readiness. Explicit refresh/mutations invalidate even in-flight cache reuse; private catalogs remain authenticated and uncached. Diagnostic mode suppresses startup warming. |
| Post-match | Scoreboard reserved legacy HUD panel dimensions, while native panel had different dimensions. | Resolve native report bounds before first scoreboard draw, reserve that exact right column and align the table title to its top. Return panel clears top diagnostics; persistent sessions are correctly labeled connected. |

## Rendering and resource policy

The chamber remains one shared GLSL pass; generated WGSL and uniform layouts are
committed alongside it. GL and modern backends use the same mathematics. No new
full-screen postprocess, texture stream, gameplay scene, per-frame shader compile
or simulation clock is introduced. Uniform locations and the eight-slot arrays
are cached. The existing radial texture and Hunter preview instances are reused.

Existing cosmetic-effects quality controls scale ambient dust to Off=0, Low=8,
Medium=14, High=22; no additional settings panel is introduced. Reduce Motion
freezes continuous environment/preview movement, settles palette/state feedback
and suppresses RmlUi transitions. UI hit targets remain stationary during color
transitions; arrival transforms are small and brief.

## Validation and evidence

- Desktop build: pass, 0 errors (122 existing warnings).
- Separate Studio build: pass, 0 errors (135 warnings).
- Android managed Compile: pass, 0 errors (129 warnings); this is not an APK/device run.
- Native C++ bridge: pass.
- Native RmlUi UX: 802 layout/input assertions at 1280×720, 1920×1080,
  2560×1440 at 2× density and 960×600. Includes Hunters page/chrome, rear label
  clearance, centered labels clear of the roster, all roster rows, Enter chat, map categories, compact Hunter picker,
  countdown and post-match panel geometry.
- Shared Shell native routing: 685 assertions, including full-page Hunters navigation/Back/Escape, actual document/intents,
  dirty Settings decisions, retirement churn and deferred Studio IPC cancellation.
- Community: 134 controller checks, including warm-cache reuse, explicit refresh
  invalidation and invalidation after a successful mutation.
- In-game/results contracts: 46 assertions.
- Menu motion: 118 checks. Synthetic 60/120/144/165/240/360 Hz schedules, long uptime,
  variable elapsed time/suspension, focus loss, Reduce Motion, ready-once pulse,
  departure and authoritative countdown cancellation.
- Native Metal window check: pass, including compositing, surface/pacing policy,
  resource retirement, loss/reconstruction and failed replacement cleanup.
- GLSL export / Naga WGSL generation and generated-output consistency: verified.
- Actual Metal + RmlUi captures: seven Hunters, six visible activity presets,
  one/four/eight-player formations at three times, ready changes, arrival/departure,
  countdown/cancellation and Reduce Motion. Stock engine models render into the
  real final composite; fixture state is not a recorded live match.

The software RmlUi screenshots also exercise active friends/invitations/party
layouts. The live Metal gallery uses a solo social state. Post-match panel layout
was verified through native RmlUi and controller tests; a completed network match
with the live scoreboard still needs the user's gameplay pass. Cross-platform
physical display/high-refresh acceptance remains with that live testing pass.

Text-only performance captures remain in `rmlui-evidence/cinematic-motion-2026-10-08/`.
Rendered screenshots containing game-derived Hunter models are local validation artifacts,
not distributable source assets. The repository's asset guard intentionally excludes
those images from git and release packages. Regenerate captures locally with the
reproducible harness documented in `tools/menu-motion-check/README.md`.

## Performance

Apple M4 Pro, Metal Immediate, 2560×1440 Retina framebuffer, default Medium effects.
Clean base built from a git archive of `75879308`; same harness and native libraries.
20 warm-up + 120 samples; isolated chamber + atmosphere, excluding models/UI/readback.

| Serialized measurement | Base | Final capture |
| --- | ---: | ---: |
| CPU + GPU completion median | 9.675 ms | 9.694 ms |
| CPU + GPU completion p95 | 13.283 ms | 12.985 ms |
| Present median | 0.101 ms | 0.103 ms |
| Diagnostic allocations/frame | 8,489 B | 7,664 B |

Completion includes `Finish` and possible surface-acquire pacing; it is not pure
GPU shader cost or an FPS prediction. Median was within ~0.2% in this short run,
which supports no obvious regression in this workload, not a broad speedup claim.
The advertised Metal GPU timestamp feature returned zero-duration samples, so
those readings are unusable. Diagnostic allocation includes synchronous completion
and timestamp machinery; the earlier uninstrumented submission loop allocated
0 B/frame for the enhanced chamber/FX. Full eight-player live frame cost still
needs gameplay profiling on the target hardware.

## Remaining acceptance limits

No Windows DX12/Vulkan, Linux Vulkan, Android device, MoltenVK or physical 240 Hz
session was run here. Shared shader generation and Android managed compilation
check integration, not those hardware paths. The short synthetic motion sequences
validate rendering/state response, not network disconnect/reconnect behavior.
No changes to networking authority, replay data, match rules or map-loading
semantics are part of this pass. Live post-match return and motion feel should be
reviewed in the game before treating the cinematic plan as fully visually accepted.
