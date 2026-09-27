# Reliable Quake 3 maps in Prime

The target is playable Prime maps built from Quake 3 architecture. It does not require matching Quake's lighting, shader effects, weapons, movement, game modes, or mod scripts.

An imported map should retain its visible architecture and structural collision, offer safe player starts, preserve supported jump-pad routes, and identify required traversal or hazards that need a Prime replacement. Successful import means that the editable project was saved; it is not a gameplay certification. Validate and playtest before hosting.

## Hardening implemented

- Real player starts are shared between conversion and legacy runtime imports. Script targets and intermission cameras are never promoted to spawns to reach an arbitrary count.
- Converted starts are editable in the project, with source yaw preserved. CTF player/respawn locations are supported as neutral Prime starts; coincident starts are deduplicated. Team assignment is an authoring choice.
- Invalid source start coordinates are skipped and reported. Invalid/non-finite import scales and out-of-range scaled architecture are rejected during preflight.
- Maps with no effective player spawns cannot pass runtime validation, including older recipes with `keepSpawns` enabled.
- Jump-pad target coordinates and inline model references are checked. Invalid pads are reported for repair rather than silently disappearing.
- Incomplete older texture packs retain affected surfaces and patch collision using an existing texture as a temporary placeholder. A warning names the missing shader and asks for a texture rebake. This preserves geometry; it does not promise correct transparency or shader appearance.
- An import with no available materials reports an error instead of trying to load unrelated game assets unnecessarily.
- Exact positive fixed-point boundaries select the next scale factor, avoiding an otherwise valid import failing packing at its outermost vertex.
- Preflight and build diagnostics identify unsupported teleporters, brush entities (such as doors/platforms), and scripted actions/hazards. Preflight warnings are persisted in the local import manifest and shown in the editor. No teleporter, mover, or Quake scripting runtime was added in this pass.
- The compiler version was advanced so previously cached output is rebuilt with these fixes.

## Checks

Validation: desktop build passed with 18 existing warnings and no errors; 227 editor/import checks passed; 43 native GPU/UI checks passed, including rendering the shipped Dust2 import. The imported viewport capture was inspected. `git diff --check` passed.

`tools/map-editor-check/Q3ImportChecks.cs` writes synthetic IBSP 46 maps and exercises analysis, transactional import, spawn conversion, CTF starts, jump pads, repeat compilation, missing textures/materials, invalid scales, and warning persistence. No Quake assets are needed for these fixtures.

The shipped `maps/dust2.ppmap` is also checked with `-mapvalidate`. It passes runtime validation, but still reports map-specific review items: a placeholder fence texture, unsupported scripted features, navigation regions, and short-walk collision sweep failures. Those warnings are not proof of a specific in-game defect, and passing the compiler is not proof that every route plays correctly. No shipped map data was modified.

A practical acceptance pass on each map should include spawning repeatedly, walking its intended routes, checking stairs/ramps and fences, exercising every jump pad, and verifying that omitted Quake teleporters or movers do not leave essential areas unreachable. Cosmetic differences can be accepted; missing essential routes require a Prime replacement.

Entity class names were checked against the [Quake III entity registry](https://github.com/id-Software/Quake-III-Arena/blob/master/code/game/g_spawn.c); no engine source was copied.
