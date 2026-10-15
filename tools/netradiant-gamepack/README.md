# Project Prime creator gamepack

This original MIT scaffold targets the pinned NetRadiant-custom fork in
`tools/project-prime-radiant/upstream.lock.json`. The fork remains a separate
GPL/LGPL/BSD product; this gamepack does not change its licenses.

Install `projectprime.game` at `games/projectprime.game`, and `entities.ent`
at `gamepacks/projectprime.game/baseq3/entities.ent`. The fork bundle helper
creates this layout and an original gray checkerboard/closed arena. Select a
creator source root containing `baseq3` as the engine path. Do not point it at
extracted cartridge files. The Studio launcher selects this gamepack only when
these trusted sibling install paths exist.

Neutral spawns and canonical pickups have editable import equivalents. Team
spawns, native gameplay policies and other specialized fields remain in the
Prime JSON project during geometry-only edits. Trigger descriptors are marked
**BSP reference import only**: they are not a promise of direct editable import.
Unsupported geometry stays in native Studio. Review, validate, build and playtest
through Studio; the gamepack does not run the Game or hold Community credentials.

Real GUI open/save, DPI/focus, UV/patch editing and platform qualification remain
required before this candidate becomes the supported primary geometry tool.
