# V19 PR-04A: Halo-inspired and Approved 4:3 HUD themes

## Implementation

- Adds editable **Halo** and **Approved 4:3** presets without removing Classic, Project Prime, Competitive, Minimal, Duel, Accessibility, or Broadcast.
- Uses a new validated schema-2 `HudThemeProfile` for stacked background, panel, accents, muted separators, text, button pressed, health warning, and health track colors/alpha.
- Compiles hex colors once into immutable GL-ready `HudThemeRuntime` when `HudProfiles.Publish` builds its render snapshot. No per-frame parse, new shader, network state, or gameplay entity.
- Routes the existing two-screen upper and lower HUD decoration through this palette. Default colors intentionally reproduce the previous blue/navy styling when an old profile is upgraded.
- Keeps health and ammunition values tied to MPH resources and the critical HP <=99 threshold; warning colors have their own field so global theme changes cannot recolor the semantic alert.
- Keeps match timer color independent in its existing `match.timer` element; preset colors change only its authored default.
- Adds a persistent collapsible **Preset themes / stacked colors** section in Avalonia HUD Studio, with independent color and opacity controls. Changes stay in the existing undoable draft until Settings Apply.
- 'Approved 4:3' is a preset display label; named-library export uses the Windows-safe filename 'Approved 4x3'.

## Compatibility

Schema remains v2. Existing v1/v2 profiles load with the old look; future v19 `decorations` extension payloads are still retained without being silently interpreted as a different schema. This PR does not switch render backend, import third-party HUD artwork, change 60Hz simulation, wire packets, or replay codecs.

## Tests

`tools/hud-check` now asserts preset continuity, unique Halo vs 4:3 identity/colors, serialized profile roundtrip, compiled immutable colors, default inheritance, semantic timer color, and clamping of malformed inputs.

**NOT RUN in this authoring session:** local dotnet not installed; CI and live GL/desktop/Android appearance validation are necessary. Do not equate new tests with proof they have passed.

## Explicitly deferred

- Full category-list usability and HSV/RGB picker are PR-04B.
- Optional ≤99 HP warning audio must be sourced from a verified redistributable/original asset. The v19 archive only credits a third-party clip; copying it would not prove license.
- Stacked WPN selector plus five-second killfeed is tracked by existing PR #574; keep its dependency independent.

## Acceptance gates

- Desktop HUD studio keyboard/controller focus, theme undo/redo, reset, import/export and save.
- Original vs Halo vs Approved screenshots at 4:3, 5:3, 16:9 and low-health (99/100 HP).
- Existing menu/radar/weapon/score colors remain stable outside selected theme.
- Zero allocations on the draw path caused by theme/color parsing, and successful Windows/Linux/macOS/Android builds.