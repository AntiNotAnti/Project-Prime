# RmlUi Settings layout requirements — 2026-10-18

Full Settings is the final migration slice, after the other UI surfaces. The
user's reference is the current working `Gui/SettingsView`, because the previous
RmlUi Settings layout was clunky and hard to use. This document records source
evidence and an implementation proposal; it does not qualify the native page as
working or complete.

## Reference and navigation

`src/MphRead/Mods/Launcher/Gui/SettingsView.cs` is the layout and behavior
reference. Its `BuildPages` order is:

1. Display
2. Graphics
3. Audio
4. Controls
5. Replays
6. Profile
7. System
8. Maintenance
9. Credits

The shell variant has a Search Settings toolbar, Basic/Advanced switch, category
tabs, one generous scrolling form, and a right-hand calibration/diagnostics and
command rail. System and Maintenance are hidden in Basic mode, but search can
reveal them. The non-shell variant uses a 190-point category sidebar and form;
below 760 points wide or 500 points high its categories become a horizontal
scrolling strip. The shell variant currently bypasses that responsive routine.

Both variants use short section headings and actual `ChoiceRow`, `SliderRow`,
`ToggleRow`, `FieldRow`, `KeyRow` and `PadRow` controls. Search matches labels,
category names and descriptions, then opens/focuses the matching setting. Focus
updates the help line. Category selection, focus and scroll are retained while
the draft is applied or discarded.

## Form grouping to preserve

| Category | Existing grouping and important behavior |
| --- | --- |
| Display | Window (desktop mode only); View (FOV with live preview); Frame pacing (FPS stops/counter); HUD (Edit HUD, Pro mode, native smoothing, feed, crosshair, weapon, radar); Accessibility (highlight, outlines/colors, reduced motion). Show dependent rows only when relevant. |
| Graphics | Quality preset; Rendering; Textures; Native lighting and shadows; Atmosphere; Cosmetics; Cel shading. Preserve preset-to-draft updates and conditional controls. |
| Audio | General / Notifications subpages. General has master/channel/music volume then language. Notifications groups visual/volume settings, match awards, hit confirmation, rapid multi-kills and life streaks, with individual cue choices. |
| Controls | Keyboard / Gamepad / Touch (Android only) / Stylus subpages. Keyboard groups mouse/scope tuning, alt-form gestures, collapsed advanced options and bindings. Gamepad shows common setup first, advanced tuning on demand, then gameplay button bindings. Touch keeps its layout editor and per-control enable/scale controls. Stylus keeps common setup visible and advanced tuning collapsed. |
| Replays | Instant clips; Kill cams; Replay library/storage policy; Spectating; Replay keyboard; Replay controller. Gameplay and replay bindings remain distinct, with a separate Reset Replay Controls action. |
| Profile | Identity; Servers; Game files/local arenas; Hunter/suit; Account; Debugging. Keep specialized file/account operations as explicit actions. Hunter/suit changes in a match queue for respawn. |
| System | Settings backup: archive path where applicable, export/import/reset/open-folder. Import/reset are launcher-only and require a clean draft; their saved stores activate after restart. |
| Maintenance | Installation/storage status and verify/cache/thumbnail actions; performance draft reset/diagnostic; effective settings. Respect platform and in-match availability. |
| Credits | Project Prime and upstream/contributor attribution. |

`GamepadSettingsPanel.cs` is the reference for Controller selection (including
Automatic/last used), control-layout presets, horizontal/vertical sensitivity,
common deadzone, vertical inversion and vibration. Its Advanced section adds
the live monitor, glyph family, scoped multipliers, weapon wheel/modifier and
slots, individual inner/outer deadzones, aim curve, horizontal inversion,
southpaw, trigger/activity thresholds, vibration strength, device setup and
profiles. Preserve hotplug/profile refresh without losing focus or an active
capture.

## Draft, persistence and recovery

`Gui/Workspaces/SettingsDraft.cs` snapshots values rather than rebuilding controls.
It also snapshots static input settings/bindings and each encountered controller
runtime. `Accept` advances the baseline after Apply; `Discard` restores it;
category reset restores the captured baseline for that category. This is a
revert operation, distinct from Reset to Defaults and the destructive saved-store
reset. Use an unambiguous **Revert Category** label in the native UI.

The existing shell commands are Apply Changes, Discard, and Reset Category, with
visible saved/unsaved/error status. Discard also cancels outline previews,
restores FOV and reloads controller controls. HUD and outline editors contribute
drafts to their parent; leaving a nested editor must not silently save them.
The current snapshot implementation associates static input state with Controls,
including replay key properties, so exact category ownership needs deliberate
handling in the native model rather than copying this implementation blindly.

`SettingsView.Commit` is the persistence/runtime inventory: MenuSettings through
`GameState.CommitSettings`, `InputSettings.Save`, `LauncherPrefs.Save`, HUD
profile save, runtime `GameSettings.Apply`, scoped player input refresh, respawn
choice, feedback reload and replay storage policy application. Keep existing
stores and paths. Do not treat that multi-store procedure as evidence of atomic
rollback: native failure handling must prove that failed saves retain a useful
draft and do not falsely report success or accept the baseline.

`SettingsView.Archive.cs` and `Settings/SettingsPersistence.cs` define import/reset
restart behavior. Pending replacement stores block ordinary settings writes so
old runtime values cannot overwrite the imported/reset files. Preserve status,
retry/recovery, Restart Now and Restart Later, plus Android document-picker
behavior. Account credentials, saves, game files and replays are outside the
settings archive.

`Portable/QuickLauncherSettingsController.cs` is the current native transactional
reference: private draft, owner-thread checks, stale-store refusal, acceptance
only after successful save and rehydration on discard. Its three preferences do
not establish full video/audio/input parity.

## Binding and video requirements

`Gui/KeyRow.cs` accepts Enter/Space to start capture, Escape to cancel, and
Backspace/Delete to clear. Capture consumes navigation/window shortcuts so
keys such as F11 remain bindable; mouse buttons and wheel are supported.
`Gui/PadRow.cs` has two binding slots, modifiers, conflict resolution and a
picker so capture command buttons can themselves be bound. It cancels safely
on focus loss, disconnection or controller revision change. Preserve Cancel,
Swap, Replace and Keep Both choices, explain the affected action(s), and restore
focus to the originating slot. Input capture must not trigger normal menu
navigation or gameplay.

The current Avalonia Commit requests the selected window mode, but contains no
15-second video confirmation transaction. `RmlUi/Pages/settings/video.rml`
declares Keep/Revert and a 15-second automatic return after a usable frame;
markup alone is not rollback implementation. The final native slice must prove
previous-video-state capture, usable-frame acknowledgement before Keep, timeout
and Back/dialog-exit rollback, focus/device failure handling, and failure/retry
without persisting an unusable mode. Keep and Revert must remain reachable by
keyboard, controller and pointer throughout the transaction.

## Native layout proposal and pitfalls

Use the nine familiar categories in the existing order. Put Controller and
Touch/Stylus under Controls, Edit HUD within Display, and online/account work
within Profile rather than expanding the top category list. Keep specialist
editors as nested pages with a clear return and parent draft ownership.

Use one scrolling, sectioned form with directly operable semantic controls:
toggles for booleans, sliders with readable units/limits for tuning, choices
with discoverable options, text fields for actual text, and explicit binding
capture buttons. A change should enter the draft immediately; a separate Stage
button beside every value adds unnecessary steps. Keep search, saved/unsaved
state and Apply/Discard/Revert Category visible independently of form scroll.
On wide screens retain the useful preview/command rail; at narrow sizes move
preview below or behind a deliberate control and preserve form width and touch
targets. Use a compact category strip or selector rather than two narrow rails.

The currently checked-in `RmlUi/Pages/settings/settings.rml` differs materially:
13 category buttons, 192 predeclared generic group buttons, 12 recycled text
input/Stage rows, and Previous/Next paging. `settings.rcss` contains successive
overrides of widths, scrolling and short-window layout. Avoid carrying that
interaction pattern forward: ordinary categories should use natural scrolling,
not arbitrary pages or a second dense group navigation wall; group headings
should explain the form without becoming mandatory extra navigation.

Search should reveal the owning subpage and conditional/advanced context, then
focus the actual control. Category/subpage changes must preserve pending field
edits, draft values, and a predictable focus/scroll position. Verify layout and
input at desktop and Android sizes when the final Settings slice is implemented,
including short windows, long labels, tab/controller traversal, visible errors,
dirty exit, save failure, capture cancellation and video rollback.

No old private `379` prototype was located in a bounded top-level search of
`/tmp` and `/private/tmp` during this source-only review. The live reference above
is authoritative; no build, UI, network or runtime validation was performed for
this documentation change.
