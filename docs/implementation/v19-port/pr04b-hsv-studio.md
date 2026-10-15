# V19 PR-04B: HUD Studio navigation and HSV/RGB color editing

## Implemented

- Three-column HUD Studio on wide screens: left **Core / Match / Combat / System** category navigation, center draggable canvas, right inspector. Narrow screens reorganize navigation, canvas and inspector vertically, without destroying persistent selection controls.
- Existing element dropdown remains available for gamepad next/previous selection. Both navigation surfaces synchronize with the same selected HUD element; there is no parallel editor model.
- `HudColorEditor` gains Hue/Saturation/Value sliders, exact integer RGB fields, validated #RGB/#RRGGBB input, traditional 30 swatches, session-bounded recent colors and favorites, and optional alpha numeric input.
- Alpha uses the selected HUD element's `Opacity` directly, avoiding an uncoordinated second opacity row. The theme inspector retains its dedicated theme alpha controls. Part and hitmarker alpha are edited with their colors.
- Every color callback is construction-scoped. Per-element callbacks use a stable ID and per-weapon/zoom crosshair callbacks use the captured target. A delayed focus/clipboard edit cannot silently land on a newly selected HUD element.
- No new profile format, asset bundle, RmlUi runtime, render backend, or network packet. Existing schema-2 JSON persists the same colors and values.

## Tests added

`tools/hud-ui-check` covers HSV primary axes and multiple RGB/HSV roundtrips, malformed alpha and color rejection, old-element color/opacity callbacks after selection changes, and category navigation. Existing undo/redo, pointer drag, controller focus, import/export and responsive layout snapshots remain as they were.

## Known limitations

- Recent/favorite swatches are deliberately **session-only**, not another data file. Persistence can be added later under the existing settings owner.
- There is no 8-digit hex format. Alpha is stored separately with the target element/part to preserve compatibility with old six-digit profiles.
- Advanced keyboard/gamepad focus and real hardware are not validated until CI/platform runs. Color picker static structure must be visually accepted on 420px-wide layouts.
- Optional low-health loop audio remains blocked on asset licensing rather than silently copying a third-party recording.

## Acceptance

- Verify `dotnet run --project tools/hud-ui-check -c Release` and native Avalonia layout captures.
- Verify exact target-scoped edits across Core/Match/Combat/System, zoom and weapon overrides.
- Confirm no event-recursion or stale focus after preset switches, palette favorites or undo/redo.
- Confirm hover/tab/gamepad access to HSV, RGB, alpha, preview and color swatches on all supported platforms.