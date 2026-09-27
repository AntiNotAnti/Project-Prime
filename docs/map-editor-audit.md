# Map editor audit — September 2026

This pass audits the current Map Studio implementation, including the existing uncommitted editor and sharing work. It preserves that work. It does not establish that every possible editor bug is eliminated.

## Fixes

- **Save shortcuts:** viewport camera controls consumed Ctrl+S. Modified camera keys now bubble to the editor; Command shortcuts work on macOS, and Ctrl/Command+Shift+Z performs redo.
- **Drag lifecycle:** Escape cancels the transform preview without committing a command. Capture loss and viewport detachment clear interaction state. Extra mouse buttons cannot start a second interaction or prematurely commit the first. Keyboard editing cannot change the command or selection during a drag.
- **Planar editing:** unrestricted movement in Front and Side views now follows their visible planes instead of discarding the vertical component. Invalid scale snap values cannot divide by zero. Camera-aligned gizmo handles no longer intercept center clicks when their projected axis collapses.
- **Framing:** focus selection fits the selected geometry, and frame all includes entity positions in otherwise empty projects.
- **History and selection:** active selection is repaired after deletion. Mixed geometry/entity transforms invalidate both domains. Non-finite transform results are rejected before changing the document or history.
- **Clipboard and bulk edits:** source collection order is independent of selection order. Capture traverses each collection once; comparison uses ID dictionaries; deletion uses collection compaction rather than repeated searches and removals. Transform selection and application avoid a separate whole-map search for each selected object.
- **Recovery:** manual save waits for the old autosave writer before deleting recovery. Final recovery also waits for that writer so stale snapshots cannot overwrite newer ones. Failed autosaves retry after ten seconds.
- **UI space:** the empty Problems panel is collapsed; diagnostics reveal it automatically unless manually overridden. Loading another document clears stale rows. Cancel Job appears only during work.
- **Rendering allocations:** native-only maps reuse the cached visible mesh list rather than allocating another list every frame.

## TrenchBroom reference

Reviewed the [TrenchBroom repository](https://github.com/TrenchBroom/TrenchBroom) and its [reference manual](https://trenchbroom.github.io/manual/latest/), particularly selection, camera navigation, editing interaction, undo/redo, and the issue browser. This pass uses these as interaction design references; it does not copy TrenchBroom source or assets. Its C++/Qt implementation is not a drop-in component for this C#/Avalonia editor.

## Verification

Results: desktop build passed with 18 existing warnings and no errors; **202 document checks** and **39 native GPU/UI checks** passed. Full editor and four-view captures were visually inspected. `git diff --check` passed.

The check projects exercise history, snapshots, transforms, cache invalidation, persistence, geometry, compilation, and packaging. The native GPU check exercises actual pointer input, keyboard routing, renderer state, scaled framebuffer picking, compositing, and transitions between single and four views. Regression cases added here cover shortcut routing, redo, planar drags, drag cancellation, clipboard order, mixed transform invalidation, overflow rejection, active selection repair, and cached mesh-list reuse.

Build with a .NET 10 SDK. An isolated artifacts directory avoids conflicts with simultaneous desktop/server restores:

```sh
dotnet build tools/map-editor-check --artifacts-path /tmp/prime-editor-audit-artifacts -m:1 -nr:false
dotnet /tmp/prime-editor-audit-artifacts/bin/map-editor-check/debug/map-editor-check.dll
dotnet /tmp/prime-editor-audit-artifacts/bin/MphRead/debug/ProjectPrime.dll -mapviewportcheck /tmp/prime-editor-audit-visuals
```

The GPU command requires a desktop OpenGL context. Captures include the full editor, four views, preview output, and overlay ordering. This is not a full manual playtest, exhaustive import-format audit, community-service audit, or proof of performance on every map size. Existing missing-source warnings for the local `mk_blockfort.json` fixture are separate from the synthetic test maps.

## Follow-up: dense maps and layout switching

- Entity labels now reserve screen space and omit overlapping labels. Selected entities have first priority, followed by entities with warnings; entity markers remain visible for picking. Text layout caching remains in place.
- The entity-helper toggle hides unselected spawn labels and jump-pad trajectories along with the existing item and trigger helpers. Selected spawn labels remain available for orientation.
- Four-view switching reuses one named focus handler instead of accumulating anonymous handlers on the retained viewport. Placement mode now follows the active editing tool when changing panes.
- Native GPU/UI verification passes 46 checks with `maps/dust2.ppmap`, including three new label checks. The imported-map capture was visually inspected. This is still an editor preview, not a gameplay playtest or a guarantee that every source-map mechanic is supported.
