# RML-25: native Offline Battle setup, verified launch and arena picker

The historical `pages/offline/setup.rml` and `pages/offline/arena.rml` screens now use the **existing** `OfflineLaunch` / `StartScreen` gameplay handoff. The native UI owns only a presentation-neutral, in-memory `RmlUiOfflineDraft` and the already-shared RmlUi page manager. Full Settings remains the final migration slice.

## Implemented

- Game types/matchups are derived from `MatchTypeCatalog.GameTypes`, preserving the existing game-mode IDs and Free For All / Teams semantics. Player Hunter, suit, bots (0–7), bot difficulty (0–3) and built-in arena are bounded local choices. These edits do not write preferences or mutate a match.
- The authored native arena modal displays 16 installed built-in maps per page with case-insensitive, length-limited search. Page state, close/Back, focus and typed event delivery belong to the existing RmlUi document manager. No unverified remote Community name is accepted here.
- Expensive `MapModeCapabilities.Supports` validation runs outside the native OpenGL owner thread. Each completion is fenced by cancellation epoch and the *exact submitted* offline draft. Leaving the page invalidates the pending completion.
- The existing `StartScreen` launch gate refuses an active network session, active replay, missing game data, invalid local request and invalid match modifiers. The new pure `OfflineLaunch.Preview` validates a candidate without writing any settings; only `OfflineLaunch.Create` commits the accepted local launch through the established `Finish` route.
- An accepted launch is single-flight. A second click cannot launch a duplicate scene.
- The authored advanced offline rules, Aim Lab and advanced training still use the existing fully functional route until independently reimplemented. The native page does not pretend to expose those controls.

## Acceptance

`tools/rmlui-live-menu-check` verifies pagination, search, bounds, typed projections, nonpersistent drafts, one-shot launch, cancelled late validation, return to Home and the pure launch preview. `tools/rmlui-runtime-check` verifies the actual scene-free native Offline document, foreground arena modal, stale background event rejection and field ownership.

The full desktop native software-OpenGL, no-assets, Windows/macOS/Linux and Android package gates remain mandatory before merge. This is **not** a default-UI cutover or a claim of end-to-end acceptance.

## Next

Migrate Adventure/save management, Aim Lab and remaining non-Settings workflows; restore richer Hunter preview and Community/replay flows; finish Android input/lifecycle acceptance. Only then migrate full Settings and perform default-UI cutover.
