# Project Prime Studio migration and recovery

Studio is a separate desktop process. Current source adds explicit Studio, Map
Editor and Replay Editor entry points while retaining the embedded compatibility
routes until platform and feature acceptance is complete. Game still owns live
rooms, playable map installation, quick Theatre playback, recordings and killcams.

Build both applications with the pinned SDK:

```sh
dotnet build src/ProjectPrime.Studio/ProjectPrime.Studio.csproj -c Release
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet run --project src/ProjectPrime.Studio -- --home
```

Supported document launches are `--map /absolute/project.json`,
`--map /absolute/archive.ppmap`, `--replay /absolute/recording.ppdemo` and
`--clip /absolute/clip.ppclip`. `--maps` and `--replays` open their libraries.
`--recover` restores the saved session; `--safe-mode` opens source documents using
safe preview and avoids GPU viewports. `--version` prints application/IPC versions.
Home opens without cartridge assets. Rendering and cartridge-dependent compilation
use the existing user-owned extraction through a configured paths file.

Do not copy extracted game files into Studio or Radiant distributions. Local test
and build outputs never establish permission to redistribute another author's art.

## Existing native projects

Keep the JSON file as the source master. Open it directly in Studio; map identity
and existing `.ppmap` archives are preserved. Save As creates a different source
location when that is intended. A stale saved revision must be reviewed before
replacing external changes. Undo and autosave/recovery operate on the authoring
project; package construction uses a detached snapshot.

Offline editing, validation and packaging work with Game closed. Installation,
playtest and hosting hand a frozen exact package to Game through its local broker.
Game rejects busy/in-use/mismatched packages before replacing live runtime data.
A missing compatible Game produces an actionable error; it does not make Studio
write into a live Game room directly.

Both apps must come from the same paired desktop version for broker operations
and coordinated Community writes. Updates stage and verify the whole pair. Close
both applications before replacing their binaries; maps, sources, recordings and
sidecars are user files and are never update ownership entries.

On macOS, keep both app bundles and their hidden paired metadata in the extracted
folder and move the complete folder together. Quit both apps before replacing
that folder with a new paired archive. In-place bundle updates remain disabled;
the transaction kernel's process/recovery tests do not enable that UI feature.
When the archive includes `Project Prime Radiant.app`, keep that third sibling
with the pair as well.

## Editing maps in Radiant

Studio's primary authoring action is **Edit Map in Radiant**. **New map in
Radiant** creates and launches a new owned workspace; opening or restoring a
project alone never launches the editor. Use **Native geometry fallback** only for explicit recovery editing.

Studio discovers fixed installed siblings: `Project Prime Radiant.app` beside
the Game/Studio apps on macOS, or a `Project Prime Radiant` directory on
Windows/Linux. On macOS the executables are `Contents/MacOS/radiant` and
`Contents/MacOS/q3map2`, with resources under `Contents/Resources/netradiant`.
Override discovery with `PROJECT_PRIME_NETRADIANT_EXE=/absolute/path/to/radiant`
and optional `PROJECT_PRIME_Q3MAP2_EXE=/absolute/path/to/q3map2`. Overrides must be
regular absolute files, not symlinks; an invalid override blocks discovery of
that tool until corrected or removed. Studio never searches PATH or map content.
The launcher selects the installed original gamepack at
`gamepacks/games/projectprime.game` and its creator engine root.

The [separate fork recipe](../tools/project-prime-radiant/README.md) describes the
native Cocoa Qt5 Mac build, symlink-free app with closed non-system dependencies
and verified ad hoc signatures, and the optional sixth Radiant app argument to
`tools/package-macos.sh`. A bounded native Mac open/edit/save/quit and strict
review/package loop passed on October 15;
Windows/Linux GUI checks, release signing and dependency/license qualification
remain separate from build and packaging checks.

A supported project creates its own source workspace:

```text
netradiant-sources/<MapId without hyphens>/
  world.map
  project-prime.json
  world.map.prime-source.json
```

The native adjunct preserves project metadata and the complete reviewed authoring baseline.
Source, identity and initial adjunct use one recoverable commit. Keep any
`.prime-transaction` recovery file if there is a conflict; it contains the bytes
needed for reconciliation. Never delete it to force an unreviewed overwrite.

Save the map in Radiant, return to Studio, choose **Review / Apply** (Tools:
**Review / apply Radiant map changes**), inspect the object diff, and apply
one undoable edit. Source changes
made during review require a new preview. Studio geometry, brush, gameplay or material changes since
export require explicit conflict reconciliation. Reopening never exports over an
external author's saved `.map`.

Version 4 workspaces own authored geometry, legacy brushes, spawns, pickups,
jump pads, navigation links and traversal volumes. Native meshes retain shared
vertices, original polygon rings, per-face materials, corner UVs, transforms and
collision flags in `primeMeshDef` primitives. They are editable polygon objects,
with object, face, edge and vertex selection; rendering triangulation does not
replace the authored polygons. The Prime Mesh menu provides modeling, UV,
material and collision tools. Modeling runs through the fixed sibling Studio
worker and commits as one native undo transaction.

Each primitive has one marked `func_group` and durable `prime_id`; custom Prime
point entities also have durable IDs. Save normalizes new primitives and points;
Duplicate receives fresh identities, while Save As retains existing identities.
Unchanged parameterized primitives and legacy brushes retain their original
native kind. A topology edit can convert them to an explicit mesh, which the
review reports before applying. Native Radiant layers round-trip to authored
layer paths; hierarchy and colors remain in the external source.

Imported and native-room architecture is shown as protected reference meshes.
Its source dependencies, reference identities and payloads are pinned; modified,
missing, duplicated or unlocked references refuse review. Reference materials
never enter the authored material table. Source preparation generates private
PNG previews beside the workspace and never copies cartridge assets into an app
bundle. Missing borrowed textures use labeled checker previews; publishing still
requires normal asset validation.

Projects, rich materials and asset management, environment, audio, validation,
package builds and playtest remain Studio responsibilities. After reviewing the
Radiant source, build `.ppmap` through Studio's canonical compiler. **Compile
source to BSP reference** remains a separate path for ordinary Quake sources;
Q3Map2 does not compile the custom `primeMeshDef` authoring format.

Older workspaces upgrade only when their source, identity and owned native
baseline still match the original export. Changed sources remain untouched for
explicit reconciliation. Unknown source fields are rejected, and source changes
during review require a fresh preview. Keep source, sidecar, native adjunct and
any recovery journal together.

New exports use standard Quake plane winding. Earlier Prime exports with the exact
legacy header can still be read explicitly; reexport them before a real compiler
build. Arbitrary malformed plane winding is not silently corrected.

## Replay source safety

Replay editing uses private readers/snapshots and non-destructive sidecars. Original
recording bytes remain immutable. Video output and clip output have distinct file
types and must never target the recording, logical clip, backing recording or
sidecar through a case/path/symlink alias. Cancelled/failed outputs remain private;
a finished export publishes atomically after revalidation.

Each replay tab has independent timeline/camera/audio ownership. Export workers
own their snapshots, fixed sample clock and bounded lifetime. Closing a tab must
not delete an export's source snapshot. Missing historical exact map packages are
errors with recovery guidance; Studio never substitutes the latest same-named map.

## Rollback and qualification

`PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW=0` restores the established creator
dashboard, `PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED=0` disables mirroring and
`PROJECT_PRIME_NETRADIANT_DEFAULT=0` disables the configured external-editor
preference. Leaving the creator/mirror flags unset enables current defaults.
Native JSON editing and Game Community remain available.

Source integration, automated tests, real external-editor/GPU/gameplay/device
acceptance and public release are separate statuses. See the
[implementation and acceptance ledger](architecture/studio-radiant-implementation.md).
No unexecuted platform, real-editor or physical-device check is certified by this guide.
