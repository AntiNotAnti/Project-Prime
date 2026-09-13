# Project Prime maps

Project Prime keeps editable map source, compiled content, distributable packages,
and mounted runtime content separate:

```text
MapProject (map.json) -> MapCompiler -> map-cache/<fingerprint>/ -> .fpmap
                                                        |
                                                        +-> selected-map overlay
```

The checked-in `maps/` tree contains first-party editable recipes and import
sources. It never contains extracted cartridge data or generated room binaries.
Generated `Model.bin`, `Anim.bin`, `Collision.bin`, `Ent.bin`, and `Node.bin`
files are written atomically to the content-addressed map cache, not into AMHE1.
At runtime, only the selected map cache is mounted over immutable base content.

## Map identities

Every map has three distinct identifiers:

- `StableId` is a canonical creator-owned ID such as `community.parallax`.
- `Version` is the declared map version.
- `ContentHash` is the canonical SHA-256 of the logical package content.

An installed `.fpmap` also has an artifact hash: the SHA-256 of the package
bytes. Runtime numeric room IDs are allocated locally and are never used as a
persistent or network identity.

## Authoring and editor

Launch `ProjectPrime.Editor` directly, or choose **Maps -> My Maps -> Edit** in
the desktop launcher. A native project contains authoring geometry, materials,
entities, environment, mode support, and editor-only view settings. Build,
playtest, and export operate on a snapshot; autosaves never overwrite the saved
project. Use **Maps -> Import Q3** for BSP/PK3 sources (kept as read-only imported
geometry), and **Add Texture** on a local project to add creator-owned PNG/JPEG
materials before assigning them to a whole brush or one selected face in the editor.
The face inspector controls tiling, UV scale/offset/rotation, terrain, and
solid-versus-decorative collision behavior.

The checked-in map sources and examples include:

- `dust2/dust2.json` — imported `df_dust2` geometry.
- `obiq1dm3/obiq1dm3.json` — Obi-Wan's **The Abandoned Base III**, a
  Quake DM3 conversion with its original attribution in `obiq1dm3.txt`.
- `nodm13/nodm13.json` — Rich "swelt" Jacques's **The Dark Zone**, an
  Egyptian-styled Quake III arena with its original permissions and attribution
  in `nodm13.txt`.
- `parallax/parallax.json` — the original PARALLAX arena; editable Q3 source and
  validation evidence are under `parallax/source/`.
- `q3dm17.json.example` — an example requiring a separately obtained `pak0.pk3`.
  Project Prime does not download or redistribute id Software's commercial data.

## Command line

Use the dedicated map command surface for new automation:

```bash
ProjectPrimeTools map validate maps/parallax/parallax.json
ProjectPrimeTools map build maps/parallax/parallax.json --content-dir /path/to/AMHE1
ProjectPrimeTools map cook maps/parallax/parallax.json --out PARALLAX.fpmap
ProjectPrimeTools map verify PARALLAX.fpmap
ProjectPrimeTools map stats PARALLAX.fpmap
```

The legacy `-mapgen`, `-mapbundle`, Q3 inspection, thumbnail, and map-test flags
remain as compatibility wrappers while existing map workflows migrate.

### Build-owned cook artifacts

Release and CI builds cook the editable tree exactly once into
`artifacts/maps/current`; the source tree is never used as an output directory.
The fingerprint includes every map input plus the map compiler, schema,
central package graph, and supplied build/package version. A process lock and
atomic manifest update make concurrent callers share that owner safely:

```bash
tools/cook-maps.sh --source maps --output artifacts/maps/current \
  --compiler-version "$VERSION" --schema-version 1 --package-version "$VERSION"
```

Client, editor, Android, and server packaging then copy only those current
`.fpmap` artifacts. Pass `--skip-map-cook --map-artifacts DIRECTORY` to
`tools/package-server.sh` when a CI job has downloaded the cook-once artifact.
Ignored bundles left by an older local build are excluded from the private cook
source snapshot and are never overwritten.

## Packages and installation

`.fpmap` v2 is a data-only ZIP package with canonical `manifest.json` and
manifest-declared paths. The package validator rejects undeclared content,
duplicate or ambiguous paths, traversal, links, executables, unsupported formats,
malformed JSON, oversized entries, excessive expansion, and hash mismatches.
Both compressed artifact size and decompressed content are bounded before a
package is admitted.

The launcher installs packages under the Project Prime data directory using a
temporary file plus atomic rename. It does not unpack packages into AMHE1.
Installed packages, editable projects, generated caches, and preview caches are
owned independently so removing an installed map never deletes creator source or
base-game files.

For online play, the Node advertises the exact stable ID, version, content hash,
artifact hash, and package size. Missing maps are downloaded only from the trusted
Node HTTPS origin, verified while streaming, installed atomically, compiled, and
then checked again as part of the match content identity before admission.
Each configured Node map is validated independently: only `Ready` entries enter
the lobby/map-vote catalog, while an `Invalid` community package remains visible
in Node status without taking the Node offline.

## Distribution rights

Packaging a level does not grant permission to redistribute it. Publish only
source, textures, previews, and imported data you have the right to distribute.
The bundled metadata for `obiq1dm3` and `nodm13` deliberately leaves automatic
online redistribution disabled. `nodm13.txt` also prohibits inclusion in a
commercial product without the author's permission; obtain any additional
permission needed for the intended release.
The repository asset guards continue to reject extracted game data and known
commercial archives.

Low-level importer and binary-format evidence is documented in
[`../.claude/mapgen/MAP-PIPELINE.md`](../.claude/mapgen/MAP-PIPELINE.md).
