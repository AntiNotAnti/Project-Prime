# RML-31: restore live engine 3D preview in native Hunter selection

## Root cause

The native Home Hunter picker restored the selectable Hunter/suit buttons and
local-first cosmetic persistence but left the original preview pane as an empty
RML rectangle. The previously restored RML-29 deployment stage only owned
Home/Lobby pages, so switching to the standalone Hunter picker suspended its
preview. The fallback text incorrectly claimed 3D previews were only available
inside the old Hunter License UI.

## Implementation

- The existing native picker presenter exposes a read-only current preview
  selection (Hunter, suit, pending cosmetic loadout) plus the exact live
  RmlUiDocumentToken for its authored `native_hunter_preview` rectangle.
- The native host's existing document element-bounds API yields framebuffer
  coordinates. RmlUiPreviewViewport converts them into one normalized,
  bounded, clipped rectangle before calling the existing engine 3D preview.
- This renderer draws *after* the native RmlUi chrome into the scoped viewport,
  because opaque/translucent picker card surfaces would otherwise hide meshes.
  It uses the game's own Hunter model/suit/material/cosmetic renderer, not a
  second model loader or a billboard/screenshot of a model.
- The in-progress cosmetic draft is used for the preview, without persisting it
  or requesting Hunter License account changes. CosmeticPreview state is
  restored in a finally block, alongside preview flags and GL program/texture
  state, so no cross-page material or camera leak can reach gameplay.
- The page remains fully interactive if game files are unavailable or
  preview rendering fails; the failure cannot break the native shell.
- Normal legacy preview behavior and authoritative Lobby Hunter selection
  remain untouched. This remains behind the opt-in RML GL runtime.

## Automated checks

`tools/rmlui-hunter-check` covers positive and partially off-screen
framebuffer-to-viewport conversion plus invalid, nonfinite, undersized and
missing framebuffers. Normal local Hunter selection, cosmetic draft and
authenticated equip/unsynced tests stay active.

Required: native software GL state check, desktop builds, no-game-assets
tests, Android and server exclusions. Before enabling the default RmlUi
runtime, run visual QA with legally owned game data across all seven Hunters,
four suits, cosmetic layers, window resize, hidden/reopened picker, macOS/Linux
high-DPI, and render backend failure.

## Still pending

Native in-Lobby Hunter identity modal live preview, advanced rotation/death
preview camera controls, richer Hunter License customization, social, Community
creator/install and other full-page parity. Full Settings is deliberately last.
