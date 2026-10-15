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

Custom `prime_spawn`, `prime_pickup`, `prime_jumppad`,
`prime_navigation_link` and `prime_trigger` classes expose the complete authored
Prime fields in the native Entity Inspector. Standard Quake descriptors remain
for ordinary source/BSP import. Prime source identities survive normal Save,
Duplicate and Save As. Keep one brush, patch or editable polygon mesh per marked
geometry container.

Studio's primary action is **Edit Map in Radiant**, followed by **Review / Apply**.
Version 4 workspaces preserve native polygon topology and explicit UVs in
`primeMeshDef`, and reconcile all authored gameplay lists in one undoable apply.
Protected native/BSP reference meshes retain their dependency ownership. Rich
assets, environment, validation, `.ppmap` builds and playtest remain in Studio;
the gamepack runs no shell commands and holds no Community credentials.

The native macOS editor uses Cocoa Qt5. Automated native parser, component-edit,
undo, source-writer and process-boundary checks are distinct from interactive GUI
acceptance. Wider DPI/focus, Windows/Linux GUI and release dependency/license/
signing qualification remain separate pending checks.
