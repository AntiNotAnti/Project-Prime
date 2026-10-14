# Community Hub PR-12 | External NetRadiant Gamepack and q3map2 Bridge

Selected upstream reference for separate installation: `Garux/netradiant-custom`, with active 2026 maintenance and external q3map2 tools. This is an **optional separately obtained desktop editor**. Project Prime does not bundle NetRadiant binaries or copy upstream GPL source, proprietary game assets, commercial Quake textures, or compiled maps into the game.

The upstream has GPL, LGPL and BSD components. Before any redistribution of a compiled fork, a full license/third-party notices and source-offer review, code-signing, Windows/Linux/macOS builds, and origin/license checks are required. The gamepack and entity descriptions in this PR are newly authored Project Prime files, not fork redistributions.

## Working bridge

- `NetRadiantBridge.LaunchEditor`: desktop-only external process, configured absolute executable path, no shell interpolation, safe working directory, editable `.map` source is not overwritten.
- `CompileImportAsync`: copies a saved `.map` source into a temporary isolated directory, runs explicitly configured `q3map2 -game quake3 -meta -fs_basepath ...` with a five-minute bounded deadline and log size, verifies the source did not change, then invokes the existing transactional `Q3ImportService.Import` to create a Project Prime JSON map. The existing normal validator/packager produces the self-contained `.ppmap` later.
- Map Studio's File menu now includes *Open in NetRadiant* and *Compile NetRadiant Source*. Existing Project Prime map editor, import Q3 and source model menus remain available. Android never executes a desktop editor.
- Environment variables: `PROJECT_PRIME_NETRADIANT_EXE=/absolute/path/to/radiant`, `PROJECT_PRIME_Q3MAP2_EXE=/absolute/path/to/q3map2`. Paths must point to actual separately installed regular binaries. Studio shows a descriptive error when absent.
- New original `tools/netradiant-gamepack` XML/entity/sidecar scaffold for Quake brush authoring and gameplay helper names. It includes **no commercial game files or textures**.

## Validation and explicit limits

The cross-platform CI tool validates gamepack XML, entity descriptions, external process path restrictions, missing-binary failure, and absence of proprietary assets. A **real NetRadiant binary launch, .map open/save, q3map2 BSP compile and import are NOT RUN** unless a test runner explicitly installs the optional editor/compiler. The gamepack is a first integration scaffold, not a verified production Qt editor fork or a claim of successful real gameplay compilation.

Bridged Quake conversion currently imports a compiled BSP through the existing Q3 service and may create read-only imported geometry. PR-13 adds a native-editable supported subset with loud diagnostics for unsupported brush/patch/UV constructs. The existing JSON editor remains the fallback until full feature parity and acceptance evidence.

**Rollback:** remove launcher/menu/gamepack and retain all JSON sources and old `.ppmap` archives unchanged; no protocol or server change. Final CI is evaluated at PR-14 only, per request.
