# Studio shell UI check

```sh
dotnet run --project tools/studio-ui-check -c Release -- <capture-directory>
```

The check renders the production Avalonia desktop shell with Skia on Avalonia's headless platform. It captures Home, explicit source-only empty Map/Replay workspaces and the actual canonical Map editor at 1280×800, 1920×1080 and 1280×800 with actual 2× render scaling. It rejects blank images, clipped measured controls and undersized viewports. `manifest.json` records logical and physical dimensions.

It checks command routing, multiple tabs, custom shortcut normalization and persistence, global search activation, actual docks, queued open validation, missing sources, session recovery and safe mode. About and performance HUD checks require readable version information, positive measured process memory, unavailable GPU timing without a graphics device, stable viewport bounds and timer cleanup.

The Map gate drives the canonical document and real production panels: default/four views, materials, UV, gameplay analysis, structural diff, prefabs, community and the independent Asset Browser. It verifies Save As identity, history relative to the saved state, private build/package output, autosave/recovery including a second immediate crash, and actual dirty-document Save/Cancel. The Asset Browser allocation fixture contains only the template's materials; it does not prove texture/model/audio import, replacement or drag/drop workflows.

Add `--assets` to run a generated PNG/WAV/OBJ/MTL/prefab fixture through the actual import, model reimport, replacement and typed viewport-drop facades. Every operation must produce one canonical history edit with exact Undo/Redo, and external source bytes remain immutable except the fixture's deliberate model-source revision. Loaded Asset Browser captures cover completed texture, audio waveform and model/prefab geometry thumbnails, tag search and used/unused filtering at three sizes. These facade checks use the same handlers as toolbar/drop actions; they do not simulate an operating-system drag gesture.

The optional Replay workspace gate uses an explicit recording:

```sh
dotnet run --project tools/studio-ui-check -c Release -- <capture-directory> --replay /absolute/recording.ppdemo
```

It drives actual Replay camera/timeline/combat/export controls, marker/key search and clip Save As. Its headless viewport is intentionally CPU/passive and supplies no native GPU pixel proof. Use the separate lifecycle harness's `--native-replay` gate for native captures and worker ownership.

Use `-p:StudioHostSnapshotPath=/absolute/studio-output` with `dotnet build` to compile this harness against a complete coherent Studio output snapshot without rebuilding production project references. Native libraries and runtime files are copied with that snapshot.

Screenshot review remains necessary for visual defects beyond the measured bounds/content checks. Passing headless captures alone does not certify native graphics, device loss, exports, playtesting or full migrated editor parity.
