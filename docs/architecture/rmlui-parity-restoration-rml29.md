# RML-29: restore original 3D Hunter stage on Home and Lobby

## Root cause

The October 9 native RmlUi build had a complete four-pass composition:

1. LauncherBackdropComposer drew the authored animated deployment chamber.
2. LauncherStageFx drew floor contact, halo and ambient lighting.
3. LauncherHunter.Draw drew the actual game models through Scene.ModDrawPreviewAlone with a transparent preview buffer.
4. RmlUi drew the menus and player nameplates **last**, over those 3D meshes.

The live Lobby had eight distinct cached HunterPreviewEntity instances and a shared back-to-front, local-first chevron formation. The current migration restored much of the original RML/RCSS markup, but the scene composer, formation table, animated Hunter instances, transparent preview setup and the hook into the opt-in native draw loop were absent. The existing single, opaque Avalonia preview is not an adequate substitute.

## Changes

- Restore renderer-neutral formation/lighting/activity tables plus the native GL2 deployment chamber shader and stage atmosphere.
- Restore individual per-slot engine Hunter previews, transparent preview background, cinematic framing, 3D foot anchoring and 60Hz pose-clock support. Retain main's shader-variant selection and standard opaque Avalonia preview behavior.
- Use one existing RenderWindow/GL context and the current game Hunter models. No new simulation, fake artwork, session controller or model loader.
- The local player is always at platform zero. Other players are sorted by their actual network slot. Copy verified occupant generation, Hunter/suit and ready state; unverified entries produce empty pads.
- Native nameplate anchors and GL model positions derive from one LauncherLobbyFormation table.
- Compose 3D before the RmlUi overlay; suppress the duplicate opaque preview in the native route.
- Respect Reduce Motion, cache per-slot models, draw rear-to-front, retire stage state on exit and keep RmlUi navigation usable if the visual stage itself fails.

## Acceptance

The no-assets live-menu test verifies local-front roster order, generation identity, duplicate/invalid occupant rejection, missing-game-data safety and the authored formation geometry. CI must build the opt-in GL2 path, the regular desktop paths, Android and dedicated server. Manual pixel verification with legitimate game files is still required for all seven Hunters, suits, 1-8 occupied pads, focus/resize/re-entry, animations, and backend fallback. A green compiler test alone does not prove live meshes render correctly.

## Outstanding parity queue

| Priority | Original route/feature | Migrated state |
| --- | --- | --- |
| P0 | Main Home 3D Hunter, Lobby 3D formation, chamber | This slice restores code; hardware/pixel acceptance outstanding |
| P0 | Home social and news rails, Lobby chat/map preview, roster state, time/score | Authored RML exists but current live model lacks multiple bindings and actions |
| P0 | Navigation, focus, back handling and overlay layering | Page manager present; real page-by-page visual acceptance remains |
| P1 | Social, friends, party, invites | RML assets exist but current native route lacks complete interactive implementation |
| P1 | Community install, downloads, favorites, creator workflow | Native Discover is read-only; deeper functions use old workspace |
| P1 | Hunter cosmetics full 3D turntable/death previews | Choice/equip exists; full viewport and material preview are missing |
| P1 | Theatre, Replay Studio and Map Studio full parity | Basic native entry/archive exists; richer functionality uses legacy tools |
| P1 | Campaign gunship/star map/save files; offline match rules | Basic pages migrated; deeper screens still use legacy |
| P1 | Android lifecycle, launcher surface, IME and touch | ABI/GLES resources exist; full default integration not done |
| P2 | Motion, accessibility, localization, GL/Vulkan performance | Partial authored styles; needs manual/system tests |
| FINAL | **Full Settings** | Intentionally leave for the final phase |

Further parity slices should reuse existing authoritative services and add page-specific automated/runtime checks without creating parallel network or gameplay logic.
