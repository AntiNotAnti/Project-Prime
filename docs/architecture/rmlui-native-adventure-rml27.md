# RML-27: native Adventure saves, resume and overwrite confirmation

## Ported functionality

The October 9 RmlUi page assets pages/adventure/saves.rml and
pages/adventure/confirm.rml are now projected by the current owner-thread
native page manager, rather than passing straight to the legacy launcher.

- Three-slot Adventure save browse, read-only area, Octolith, energy and Hunter
  information comes from the existing AdventureSave.Read, not another
  serializer or copied story-state model.
- Native Hunter choice is a local draft until a requested game launch.
- Resume requires a readable occupied slot; a readable empty slot can start
  a new expedition without a spurious overwrite confirmation.
- Every occupied slot needs explicit confirmation before New Run.
  Unreadable slots cannot be resumed or overwritten by this page. They retain
  their original bytes and direct the user to the existing archive/recovery UI.
- AdventureSave.ContentFingerprint computes a read-only SHA-256 of the
  exact stored save file. A confirmed destructive action and StartScreen
  handoff compare the slot identity, full captured progress and saved bytes.
  Another screen changing the same slot invalidates the confirmation.
- Actual launch remains AdventureLaunch.Create, preserving game/room
  progression, checkpoint loading, Menu.SaveSlot and current StorySave
  serialization. RmlUi never writes campaign JSON or creates a second scene.
- The authored confirmation modal owns its own document lifetime and typed
  intent sequence. Back cancels it first. Closing Adventure returns to Offline.

## Acceptance

tools/rmlui-live-menu-check verifies unreadable slots, presentation, explicit
confirmation/cancel, changed-byte refusal, refresh/re-confirm, resume and
empty-slot launch. tools/rmlui-runtime-check validates actual page/modal
composition, foreground event gating and Back/Cancel order.

Build, no-assets, native software OpenGL and platform CI must pass before merge.

## Remaining non-Settings parity

Campaign gunship/star map and save file manager remain in the existing
campaign workspace pending separate native route integration. Full Replay,
Community creator workflows, social/party, 3D Hunter preview and Android
launcher/input/lifecycle remain outstanding.

Full Settings migration remains the FINAL UI slice after all other parity
and platform gates complete.
