# RML-07: Authored RmlUi pages and font resources

Restores 17 page families' authored **RML/RCSS** from the October 9 backup,
not the historical C# presenters/controllers. Current services must remain
the source of truth for settings, lobby, Community, Hunter License and Studio.

Opt-in desktop builds copy the same page hierarchy under `rmlui/pages`,
shared templates under `rmlui/components`, and theme files under
`rmlui/themes`. Noto Sans JP is restored from the licensed historical
source alongside its OFL attribution, so native JP punctuation/glyphs can
be rendered without a fallback-to-tofu when font selection is completed.

`tools/rmlui/check-pages.py` validates expected page resources and local
href references in no-game-asset CI. It does not claim navigation, input,
backend behavior, physical-GPU rendering, or complete localization.

Current rollout remains opt-in. Later slices will connect native pages to
fresh data snapshots and authenticated service calls and then validate
Windows/Linux/macOS/Android package parity.
