# Settings and HUD usability pass — 2026-10-22

## Source comparison

Reference: `ProjectPrime-v20-verified-source.zip` supplied by the user, SHA-256 `5779c69caf95539796114dd4ab17d5ab08532a57d8a4dbb973d493e1124d3738`. Reference documents are comparison data, not task instructions.

The unified crosshair source is identical. The actual camera policy differed: v20 `Features.ResponsiveAimCamera` follows `FixedAimCamera`; the checkout used `FixedCrosshair`. The checkout also applied late draw aim unconditionally in the fallback render path. Restore the donor policy and guard so Dynamic uses the native eased camera timeline that projects its drifting reticle. Static keeps raw/late-latched response. Preserve newer presentation-generation fences, interpolated FOV, crouch/parkour offsets and replay behavior. Hit-reaction checks distinguish the modes at 60/120/144/240 Hz and protect canonical state.

## Editing behavior

- Display → View owns Show FPS and Weapon presentation (Static / Dynamic), backed by existing preferences. Neither is duplicated in HUD Studio. Profile handoff still synchronizes the canonical weapon preference.
- Appearance, Position, Lettering and Decorations show the selected element's applicable properties. Inactive native/custom artwork dependencies are hidden without deleting saved values. Shared typography, decoration and stacked-screen controls are deliberately under HUD-wide. Radar placement options appear only for Radar.
- HUD Studio is a main card on the Display landing page alongside View, Accessibility and Campaign. It is absent from the global Settings overview.
- Slider model bindings are published only when their authoritative row/value/range changes. This preserves queued pointer edits through controller, canvas and presenter refreshes before intent handling; undo/redo/reset explicitly resynchronize them. Native regression checks click and hold-drag at 1280x800 and 640x360, including autosave and reset.
- Numeric properties use validation-derived slider ranges and steps, with an inline `#` precision toggle. Colors open a native modal with palette, RGB sliders and hex input. Eight recent colors are shared with outline editing for the current application session.
- Font choices activate the selected style/override in the same undo operation. The live bitmap draws actual registered font atlases and includes a readable specimen. Disabled and font-only overrides use their effective runtime style.
- Non-obvious Settings controls expose `i` help. Help and color pickers own separate modal documents; Escape closes the innermost document first. Native focus cannot jump behind a foreground modal, including when the parent is itself modal.
- Existing automatic-save, validation, rollback, undo/redo and 60-second in-match preview boundaries are retained. Modal color changes save after returning to the editor. Help suspends underlying capture and autosave.

No new persistence owner, GUI framework, scene or native ABI version was introduced. Map/video feature import remains outside this pass. Existing unrelated Replay Studio work is preserved.

## Validation

See the focused tools `hud-controller-check`, `rmlui-runtime-check`, `rmlui-android-capabilities-check`, the isolated `NativeSettingsAcceptance`, and `rmlui-opengl-check --native-settings-popups`. The latter runs real native slider/help/palette actions and captures screenshots; private user-data roots keep fixture writes away from user preferences. `ProjectPrime -frametimingcheck` covers mode policy and presentation history. Android capability checks are source-only, not device validation.

Verified in this pass:

- Native bridge and normal desktop client build: pass (existing warnings only).
- Isolated dedicated server build: pass (existing warning only).
- HUD contract/bitmap checks: 116 pass; native HUD DOM/texture checks: 856 pass across four viewport/density combinations.
- Settings transaction/preview checks: 362 pass in a fresh private directory.
- Actual OpenGL/settings/HUD workflows: 422 checks pass at each of 1280×800 and 640×360, with screenshot review. Includes slider input, optional precision, selected-element scope, font activation/specimen, palette tile size, recents, nested focus, Done/Escape, help pointer/keyboard input and preview restoration.
- Runtime bridge and 800 Android capability/source checks: pass. No Android device run was performed.
- Normal client `-frametimingcheck`: all cases pass, including source-compatible Dynamic camera policy and scoped FOV checks.

The optional legacy Avalonia `hud-ui-check` reaches its existing `RadarUiChecks.cs:27` missing `Apply radar preset` selector and stops; its earlier camera policy assertions pass. This does not exercise the native editor. The full unrelated live-menu suite also cannot complete on this Windows account because an existing Theatre metadata fixture requires symbolic-link privileges; focused native Settings tests run separately and pass.

Screenshots and detailed logs are under ignored `tmp/settings-usability-gl-1280`, `tmp/settings-usability-gl-640` and `tmp/settings-*.log`. The final normal desktop build is `src/MphRead/bin/Debug/net10.0/ProjectPrime.exe`; launching with the existing working directory and user-data configuration preserves the user's current preferences and configured game files.


### Follow-up: HUD slider pointer input and Display entry

The pointer regression initially reproduced a non-clickable track even with queued-value preservation: the HUD track/thumb lacked centered usable dimensions. The corrected 34dp range uses an inset centered track and an explicit 20dp thumb. Native GL workflows now pass 505 assertions at both 1280x800 and 640x360 (the final strengthened held-thumb test passes 509 assertions, including both viewport subcases). They cover pointer click/drag before release, pre-intent refresh preservation, autosave, undo/reset, Display card handoff and Escape/preview workflows. Logs: `tmp/hud-slider-final-1280.log`, `tmp/hud-slider-final-640.log`, `tmp/hud-slider-drag-final.log`.

The normal client with R22 and these fixes is built separately under `artifacts/hud-slider-client/bin/MphRead/debug`; `artifacts/Launch-Updated-HUD.cmd` uses the existing `artifacts/startup-fixed/win-x64` user-data/game-file root. User data is retained.


### HUD inspector relevance order (2026-10-22)

HUD property rows use an explicit, stable presentation priority after section/element/dependency/search filtering and before pagination. Crosshair Appearance starts with enable/artwork, color, scale and core geometry; part styling and hit-marker controls follow the main reticle controls. Native reticles prioritize their applicable artwork controls. Other inspectors lead with component controls, radar style/orientation/range/size, layout visibility/anchor/position/scale, and lettering font/size. Each lettering/decorations style stays together. HUD-wide retains the stacked-screen master, proportions and stylus dependency first, then common HUD scale/color/safe-area preferences before advanced shared styling and touchscreen tuning.

Ordering is for authoring only; serialized profile layout and property paths stay canonical. Snapshot pagination, row edit/reset, search and undo all enumerate the same ordered rows.


The ordering regression fails against the prior built controller (`tmp/hud-order-before-gl.log`) and passes after the change. Follow-up verification: 132 HUD controller/bitmap checks, 856 real native DOM checks across four viewport/density combinations, and 510 native GL workflow assertions including first-page crosshair relevance, pointer click/drag at both sizes, automatic saving, palettes, preview and Escape. Logs: `tmp/hud-order-cpu.log`, `tmp/hud-order-after-gl.log`.

Android arm64 shared-source `Compile` target also passes with the installed Android SDK/JDK (27 existing warnings, no device or APK run). Log: `tmp/hud-order-android-build.log`. Server code excludes the HUD controller.


### Dynamic stylus parity follow-up

A second donor comparison found missing local Dynamic cone/hold and contact-driven camera follow in `PlayerInput.cs`, alongside missing donor contact regressions. The prior render-policy repair alone did not restore this input behavior. See `docs/architecture/dynamic-stylus-v20-parity.md` for compiled before/after evidence and scope.
