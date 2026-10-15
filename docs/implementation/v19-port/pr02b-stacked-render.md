# V19 PR-02B: stacked dual-LCD GL/GLES presentation

Dependency: PR-02A #570, branch feature/v19-pr02a-stacked-geometry-20261015.

## Behavior

* Desktop Stylus Mode and explicit stacked toggle prepare one world scene at the upper LCD aspect, with two separate OpenGL output viewports.
* Upper LCD uses actual HP, selected weapon and ammo, reticle and authoritative hit marker; chat, killfeed, voting, results and scoreboard remain visible.
* Lower LCD uses a navy-decorated HUD, existing MPH weapon sprites, touch hitboxes, radar and weapon wheel. The cursor maps onto the real lower LCD.
* The pre-RmlUi Avalonia settings expose screen aspect (4:3 or 5:3), separate radar size/X/Y, and stacked toggle.
* The existing controls.txt owner persists these values with validation and reset; default remains the old single-screen display.

## Safety constraints

Scene.OnDrawFrame and Scene.OnRenderFrame wrap original core methods, temporarily adapting Size for drawing only, and restoring physical Size on all exits. The frame render graph composites to the upper GL viewport; a second HUD-only pass draws the lower one. No additional simulation step, packet, damage, replay format, renderer backend or unverified asset import.

DsLayout.GlBounds provides a pure top-left UI to bottom-left OpenGL conversion. Asset-free PointerCheck asserts 4:3/5:3 viewports and controls.txt roundtrips. Replay/movie/export paths explicitly skip stacked mode.

## Required acceptance (not run yet)

- Windows/Linux/macOS and Android Release build, independent native GL output snapshots
- 4:3 / 5:3 upper FOV, weapon aiming and bottom controls at normal/HiDPI/ultrawide resolutions
- 99 HP readout and confirmed hit marker, real multiplayer killfeed, keyboard/gamepad/pen and touch focus
- Solo vs bot outlines/occlusion, native HUD and shadow depth, 1v1/8-player performance
- Replay/video export unaffected, pause/resume and settings switching, memory/GPU resource lifetime
- Matched p50/p95 frame and rendering cost against the previous one-screen build

This PR is a draft until those render/platform checks pass. No intermediate CI results are awaited per request.