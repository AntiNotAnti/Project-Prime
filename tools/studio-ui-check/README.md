# Studio shell UI check

```sh
dotnet run --project tools/studio-ui-check -c Release -- <capture-directory>
```

The check renders the production Avalonia desktop shell with the native Skia renderer on Avalonia's headless platform. It captures Home, empty Map and empty Replay at 1280×800, 1920×1080 and 1280×800 with actual 2× render scaling. It rejects blank images and out-of-window headings, status text and visible buttons. `manifest.json` records logical and physical dimensions.

It also checks command routing, multiple tabs, dock hide/show/detach/default restoration, queued open validation, read-only inspection, missing sources, session recovery and safe-mode preservation of existing saved state.

This is a Phase 1–2 foundation check. It does not certify authoring viewports, four-view mode, UV/material panels, replay playback/timelines, export, playtesting or full migrated editor parity. Screenshot review remains necessary for visual defects beyond the measured bounds/content checks.
