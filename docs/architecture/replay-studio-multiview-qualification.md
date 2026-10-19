# Replay Studio multi-view fault isolation and acceptance (RPS-03)

Parent: RPS-01/02 auxiliary render scope PR. The production renderer is shared by Mac/Windows/Linux OpenGL desktop playback; native Studio acceptance runs with the existing Shell and one real replay clock.

## Default native acceptance

`PROJECT_PRIME_RMLUI_TEST_STUDIO_ROLE=1 rmlui-opengl-check --replay-window OWNED_OR_SYNTHETIC.ppdemo SETUP_paths.txt NEW_OUTPUT_DIRECTORY 960x600`

The native RML97 journey now checks a real four-tile render, disables it, injects a managed exception in tile **index 2** on the next attempt, and expects Studio to disable four views automatically. The real replay owner, transport, source recording, paused frame and canonical state must remain unchanged. Retiring the synthetic error and enabling the mode again must successfully render another four-view picture. Failure hooks are cleared by the fixture's Detach even on a failing run.

## Extended stress (explicit opt-in)

Set `PROJECT_PRIME_REPLAY_FOUR_VIEW_STRESS=1` in the same actual-window command. The harness then performs **50 real enable/disable pairs (100 toggles)**, validating owner/frame/canonical state across each presented frame. It also tests a seek to frame 60 with four views enabled, equality after returning to one view, and a seek back to the baseline frame. Source bytes are still checked after teardown.

Native stress is intentionally opt-in due to GPU and CI time budgets. The default injected-failure case is always tested for the actual Studio role.

## Remaining physical-device gates

- Full-length replay with all eight Hunters, death and respawn during four-view playback.
- Window resize and DPI changes while auxiliary passes are active; 640x360, 1080p, 4K where available.
- Repeated close/reopen and exported PNG/MP4 identity, including cancellation.
- Windows, Linux, and macOS OpenGL compatibility contexts and supported iGPU/dGPU/llvmpipe.
- Check managed and native crash logs, GL errors, retained GPU resources, p95/p99 frametime, pixel composition, and exact gameplay hash at each tested playback cadence.

These tests do not modify replay serialization, user archives, or camera authoring. Do not claim runtime qualification until its corresponding actual-window receipt has run on the branch.

## RPS-04 primary-camera reuse

The normal replay render already contains the exact primary camera/HUD picture when Studio enters Four Views. RPS-04 blits that **existing** output to the top-left quadrant and renders only the three other camera passes. This removes one world geometry/HUD render per displayed four-view frame; a final camera-independent draw-list preparation is still needed to restore retained submission. No extra render targets, GPU texture ownership or second replay clock were introduced. If the normal picture target is invalid the optional feature declines to render instead of copying stale pixels.

Acceptance must compare actual source-frame primary-tile pixels to the normal one-view picture across HUD on/off, authored/free/POV modes and DPI/resize, including the first frame after seeking. Benchmarks must report render pass counts and CPU/GPU p95/p99 rather than assuming a particular FPS gain. Per-tile subresolution rendering and shared static geometry collection are still future optimization work, not claimed here.
