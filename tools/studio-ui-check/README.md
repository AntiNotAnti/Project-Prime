# Studio shell UI check

```sh
dotnet run --project tools/studio-ui-check -c Release -- <capture-directory>
```

The check renders the production Avalonia desktop shell with Skia on Avalonia's headless platform. It captures Home, explicit source-only empty Map/Replay workspaces and the actual canonical Map editor at 1280×800, 1920×1080 and 1280×800 with actual 2× render scaling. It rejects blank images, clipped measured controls and undersized viewports. `manifest.json` records logical and physical dimensions.

It checks command routing, multiple tabs, custom shortcut normalization and persistence, global search activation, actual docks, queued open validation, missing sources, session recovery and safe mode. About and performance HUD checks require readable version information, positive measured process memory, unavailable GPU timing without a graphics device, stable viewport bounds and timer cleanup.

The Map gate drives the canonical document and real production panels: default/four views, materials, UV, gameplay analysis, structural diff, prefabs, community and the independent Asset Browser. It verifies Save As identity, history relative to the saved state, private build/package output, autosave/recovery including a second immediate crash, and actual dirty-document Save/Cancel. The Asset Browser allocation fixture contains only the template's materials; it does not prove texture/model/audio import, replacement or drag/drop workflows.

Add `--assets` to run a generated PNG/WAV/OBJ/MTL/prefab fixture through the actual import, model reimport, replacement and typed viewport-drop facades. Every operation must produce one canonical history edit with exact Undo/Redo, and external source bytes remain immutable except the fixture's deliberate model-source revision. Loaded Asset Browser captures cover completed texture, audio waveform and model/prefab geometry thumbnails, tag search and used/unused filtering at three sizes. These facade checks use the same handlers as toolbar/drop actions; they do not simulate an operating-system drag gesture.

The loaded workflow also checks raw, stale, cross-owner and Save As drag rejection, changed prefab sources, escaped linked destinations and mid-import root substitution without outside-owner writes. Missing declared texture previews must fault explicit readiness, show an actionable error and still permit discard/cleanup. Animated prefab copies require independent material identities and unique native animation names. The exact model-source query puts its geometry preview in view rather than relying on a matching material row above it.

A held-permit fixture blocks the two real thumbnail workers. Dirty preflight Cancel must leave those requests running; caller cancellation after preview pause must preserve the document and renew visible previews. A later Discard must cancel and drain held requests and release the owned browser without waiting for the permits to be released.

The optional Replay workspace gate uses an explicit recording:

```sh
dotnet run --project tools/studio-ui-check -c Release -- <capture-directory> --replay /absolute/recording.ppdemo
```

It drives actual Replay camera/timeline/combat/export controls, marker/key search and clip Save As. An owned source-worker barrier checks cancellation of initial preparation before scene adoption while preserving the previous actual editor tab. Shift-drag pointer fixtures distinguish low/high camera keys over the same frame span and require two-dimensional graph/list selection synchronization without editing camera bytes. Speed drags, Shift-boxes and a channel change during capture must preserve canonical and durable camera state. Actual analysis completion/cancellation must appear in central Jobs while preserving the private world and prior analysis; an owned Jobs window fixture checks visible progress and its real pointer Cancel action. A queued continuation case completes the actual Save As worker, closes/disposes the original document before UI adoption, and requires rejection without document resurrection or hidden source pins. Its headless viewport is intentionally CPU/passive and supplies no native GPU pixel proof. Use the separate lifecycle harness's `--native-replay` gate for native captures and worker ownership.

Use `-p:StudioHostSnapshotPath=/absolute/studio-output` with `dotnet build` to compile this harness against a complete coherent Studio output snapshot without rebuilding production project references. Native libraries and runtime files are copied with that snapshot.

Screenshot review remains necessary for visual defects beyond the measured bounds/content checks. Passing headless captures alone does not certify native graphics, device loss, exports, playtesting or full migrated editor parity.
