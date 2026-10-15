# Project Prime creator gamepack

This original MIT scaffold targets the pinned NetRadiant-custom fork in
`tools/project-prime-radiant/upstream.lock.json`. The fork remains a separate
GPL/LGPL/BSD product; this gamepack does not change its licenses.

Install `projectprime.game` at `gamepacks/games/projectprime.game`, and `entities.ent`
at `gamepacks/projectprime.game/baseq3/entities.ent`. The fork bundle helper
creates this layout and an original gray checkerboard/closed arena. Select a
creator source root containing `baseq3` as the engine path. Do not point it at
extracted cartridge files. On macOS these paths are relative to
`Project Prime Radiant.app/Contents/Resources/netradiant`; elsewhere they are
relative to the editor directory. The Studio launcher selects this gamepack and
its engine path only when the trusted install paths exist. The historical
`games/projectprime.game` location remains a compatibility fallback.

Neutral spawns and canonical pickups have editable import equivalents. Team
spawns, native gameplay policies and other specialized fields remain in the
Prime JSON project during geometry-only edits. Trigger descriptors are marked
**BSP reference import only**: they are not a promise of direct editable import.
Unsupported geometry stays in native Studio. Review, validate, build and playtest
through Studio; the gamepack does not run the Game or hold Community credentials.

Studio's primary geometry action is **Edit Geometry in Radiant**, followed by
**Review / Apply**; **Native geometry fallback** is explicit. Version 2 source
workspaces store each brush/patch in a marked `func_group` with a persistent
`prime_id` and patch collision/tessellation keys. The patched editor's Save
normalizes new worldspawn primitives into those containers without rebuilding
their surfaces. Keep one primitive per group; reconcile unsupported group
properties in Studio. These keys survive normal editor saves, unlike comments.

The native macOS editor uses Cocoa Qt5. Its real GUI acceptance is **in progress**;
GUI open/save, DPI/focus, UV/patch editing, Windows/Linux GUI acceptance and
release dependency/license/signing qualification remain separate pending checks.
