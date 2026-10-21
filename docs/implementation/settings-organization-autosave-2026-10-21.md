# Native settings organization and automatic saving

Subsequent HUD reachability, inline editing, section filtering and live preview
corrections are recorded in `v20-non-map-video-reachability-audit-2026-10-21.md`.
The results below describe the initial organization pass.

The follow-up to the v20 feature restoration reorganizes the existing native
settings catalog rather than replacing its engine features or preference owners.

## Settings and navigation

- Settings opens an overview, category choices, then a focused section.
- Controls has Aim, Movement, Weapons, Actions, Interface, Controller, Touch and
  Stylus sections. Keyboard and controller movement/weapon bindings share their
  respective task section. Canonical binding groups remain unchanged for capture
  and conflict detection across sections.
- Other categories separate view/accessibility, graphics quality/textures/lighting/
  cosmetics, mixer/notifications, replay recording/playback/bindings, and profile
  identity/servers/local assets. Controller profile file paths are editable beside
  their import/export controls.
- Duplicate raw palette and placement rows route to the existing outline, touch
  and stylus visual editors; HUD settings stay in the HUD editor. Search reaches
  those editors and the full canonical catalog.
- Escape and Back cancel the innermost capture, conflict, calibration, display
  trial or editor before returning section -> category -> overview -> prior screen.
  Unfinished invalid text is discarded on Back; valid accepted values are flushed.

## Applying changes

Normal Settings automatically commits valid settled edits after 400 ms of quiet
input. Navigation flushes the final valid value. HUD and visual layout gestures
remain detached while held and save after release. HUD undo/redo stays available.
Imports, resets, named profile operations and destructive confirmations still
require their explicit actions. Display mode changes retain the timed Keep/Revert
trial and usable-frame acknowledgement.

The existing transaction owns runtime publication and all preference files.
Failures restore previous files/runtime, keep editable values, expose the error
and suppress per-frame retries. Back retries saving. A failure-only **Restore
unsaved changes** action recovers from persistent errors or stale controller
drafts. A controller replacement does not block unrelated mouse settings.
Restoring unsaved values rebases against the current controller.

## Popup layout

Capture, binding conflict, destructive confirmation and display prompts use a
real native modal document with a dimmed backdrop, bounded scrolling card and
wrapping action row. Pointer and Tab focus remain in the modal. Clicking Cancel
cannot become a Mouse Left binding. Shared semantic headings/paragraphs explicitly
use block flow because RmlUi has no browser user-agent stylesheet; Quit and Update
use the same corrected text flow.

## Validation

The focused CPU suite uses a fresh temporary user-data process and tests complete
catalog coverage, binding domains, debounce, persistence, rollback, retry fences,
display trials, controller replacement and navigation flushes. The native OpenGL
fixture exercises actual RmlUi geometry, pointer/Tab fences, button activation,
Escape/Back hierarchy and nested HUD/outline/touch/stylus automatic saving.

Commands (repository-pinned .NET 10 SDK):

```powershell
dotnet build src/MphRead/MphRead.csproj --no-restore
dotnet build tools/rmlui-live-menu-check --no-restore
# Run --native-settings-acceptance-isolated only with a fresh temporary
# PROJECT_PRIME_USER_DATA / PROJECT_PRIME_NATIVE_SETTINGS_TEST_ROOT.
dotnet build tools/rmlui-opengl-check
# Run <native-bridge> --native-settings-popups with private user data;
# Append WIDTHxHEIGHT for the framebuffer;
# PROJECT_PRIME_RMLUI_CHECK_DENSITY selects density.
```

The full OpenGL test project also enables its existing replay failure-injection
fixture build property; this is a test configuration, not a new product option.
Screenshots and semantic/geometry readbacks are written under the private test
root's `native-settings-popups` folder. No game assets or live network session are
needed for these focused checks.

Final results:

| Check | Result |
| --- | --- |
| Desktop build, including full GL fixture configuration | 0 errors; 2 existing warnings |
| Focused CPU settings acceptance | 228 checks passed |
| Native OpenGL at 1280x800, density 1 | 185 checks passed |
| Native OpenGL at 640x360, density 1 | 185 checks passed |
| Native OpenGL at 1440x900, density 1.5 | 185 checks passed |
| Focused diff whitespace checks | Passed |

Each native run includes 109 popup/render checks, 32 organization/navigation
checks, 26 HUD automatic-save checks, 16 visual automatic-save checks and two host
initialization checks. Summary files record containment of both relative game
settings and launcher preferences within that run's private directory.
Screenshots were inspected for toolbar separation, modal wrapping, background
screen bleed-through and compact/high-density layouts. Test output is under
`tmp/settings-overhaul-ui/run-SIZE-DENSITY/native-settings-popups/`.

During fixture development, one successful run created a new working-directory
`Savedata/settings.json` before its working directory was isolated. The file was
confirmed absent beforehand and moved to
`tmp/settings-overhaul-fixture-diagnostics/generated-working-directory-settings.json`.
The fixture now asserts private working-directory containment before creating any
settings owner; the final three runs leave the project path absent.
