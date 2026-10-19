# CPP-08/09 local content inventory and bounded qualification recipe

Initial inspection date: 2026-10-17. The initial inventory records metadata, file existence and measured
package digests. It is not a game-media import, package validation, compiler,
rendering, collision, multiplayer, device or performance acceptance. No source or
derived game asset was copied, extracted, packaged, uploaded or committed. No
application, build, emulator or window was run for that initial inventory. The
2026-10-18 follow-up below records separate, actual private source conversion.

## 2026-10-18 follow-up: supplied media and actual private bodies

The initial October 17 observations below predate the user's explicit source paths.
The source-availability boundary has changed: two real owned-input paths were
supplied and verified read-only under `/Users/jarrett/Documents/MPH/Roms`.
No missing-media claim should be carried forward from the older bounded search.
The default Project catalog has not been activated or replaced.

| Supplied container | ID / physical format | Measured bytes | Full SHA256 before/after |
| --- | --- | ---: | --- |
| `Metroid Prime Trilogy (USA).rvz` | `R3ME01`, Wii RVZ | 7142274304 | `9e12e697a1b6613b1307d4e7801d005d60d91def6997a0e622f119907b997c77` |
| `Metroid Prime 2 - Echoes (USA).rvz` | `G2ME01` rev0, GameCube RVZ | 1287435724 | `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529` |

Actual source-linked donor DiscImage probes verified the Trilogy edition PAKs
and original Echoes `Metroid1/5/6.pak` against the exact expected hashes. The
original Echoes input now supplies matching source archives for native
MP2DarkSamus and the six canonical arena recipes. Their full resource conversion
and Project integration are separate from an archive hash match. Echoes
AudioGrp.pak was measured at SHA1 `8d0282eec11ea74288772bd4b7ee795ca913bab1`;
no reviewed expected Echoes AudioGrp pin exists, so it remains measured/unqualified.
It must never inherit the unrelated Prime1 AudioGrp expected hash.

The supplied donor source is `/Users/jarrett/Documents/MPH/Repos/SecondHunt`, a
snapshot without `.git`; exact inspected source/recipe/license hashes identify it.
Its real RVZ/Wii edition owner admits four Trooper bodies and native Corruption
Dark Samus from Trilogy. The Trilogy MP2 edition admits the HD Dark Samus look,
not native MP2DarkSamus or the six arenas. Native MP2DarkSamus was imported from
the newly supplied original Echoes input, with no allowlist or expected-hash bypass.

All six private one-body imports passed, with complete source discs unchanged.
Trooper4 each retain 35 source bones, palettes 6/5/17, 154 primitive clips and 7
source-pixel-matching body textures. MP2DarkSamus retains 39 bones, 7 material
palettes (at most 32 each), 73 unique exported clips and 10 textures; two composite
entries and the duplicate `A_BoostBall_dark_samus` name are explicit.
MP3DarkSamus retains 44 bones, 5 material palettes (at most 32), 110 exported clips,
six composite/unresolved entries, two explicit skipped dash clips and 7 textures.
Actual binary parent/name/bind, palette, existing GX loader and source TXTR/PNG
pixel consistency checks passed. These are not full composite/state/pose/draw
acceptance or Project model-format compatibility.

Durable metadata-only proof is in
`artifacts/cpp08-local-body-import-20261018/README.md`, `acceptance.json`,
per-body owner source-state/output-hash inventories and dependency/license
witnesses. The 96 generated files total 31,104,843 bytes. Asset payload remains private
under `/tmp`; only owner state and hash/receipt metadata are duplicated here.
No proprietary payload was copied
to Git, product profiles, Community or release output.

The actual diagnostic composition prunes only four unused ImportService
constructor registrations to avoid unrelated app/arena/HD compile ownership;
BodyImportKind, BodyImporter, GunWriter, PngOut, decoders and all identity/hash/
edition/state/transaction code retain their original bytes. The sole external
dependency is privately pinned ImageSharp3.1.12 from official NuGet, with exact
signed archive/content/DLL hashes and its actual split-license file retained.
This is no product dependency or shipping-license decision. Original launcher
failure and its validation-preserving path-only correction are retained.

Project's GameCube reader initially rejected RVZ. A separate reviewed adapter
lends a bounded logical GameCube stream to the existing FST/recipe owner; it must
retain original ISO/GCM behavior and reject Wii/Trilogy/MP3 at that boundary.
The frozen central RML69 client
`54f2ec60caca3573e876b9183942e3689d1ed89fe34c7ff160c97b1325047e40`
now passed the existing Project-owned actual character exporter and all six
original-MREA blockout imports, each true exit 0. Character output has 39 bones,
4245 vertices and 45 weight groups; its motion inventory is still bounded to 12
sparse sampled clips, 62 indexed-only entries and two composites. All six map
recipes were parsed, validated and CPU-built before private publication, with
unchanged solo/local-only Battle scope and MPH fallback material. Complete source
RVZ before/after hashes and all three current archive pins passed; all 317 central
companions recheck unchanged. Metadata-only receipts are in
`artifacts/cpp08-cpp09-project-source-rml69-20261018/README.md`. Actual OBJ/weights/
poses/map geometry remain private, with no product activation or packaging.
Existing source-only maps remain local blockouts; this does not finish faithful
textures, temporal/state playback, Echoes mechanics or multiplayer/device acceptance.
The separate existing Project visual (6), cannon (4) and routed-cannon (4)
private CPU source tiers also passed on the same client in fresh contexts.
All retain independent original collision/source-world render meshes and fallback
materials. Cannon/routed variants have 12 authored pads over four arenas;
routed output has all 12 exact final compiler attestations (ordinal/LinkId,
distinct final nodes, encoded one-way edges, no refusal). No nearest-node guess
or count-only substitute was used. These CPU source checks do not prove actual
bot traversal, cannon landings, faithful textures, mechanics, rendering, online
or device behavior. Exact separate provenance is in that artifact's
`tier-acceptance.json`; all generated map geometry stays private.

## Initial scope and availability, before supplied media paths

Only the documented Project Prime application-data root, its configured source
roots, default model catalog, installed map catalog, repository source/docs and
standard emulator executable/app locations were inspected. No account tokens,
personal preferences, saves, recovery documents or unrelated personal folders
were read. Archive inspection read the central directory and bounded manifest /
project JSON; full package SHA256 was streamed separately without extraction.

The canonical root is `/Users/jarrett/Library/Application Support/Project Prime`.
Its `paths.txt` registers only `AMHE1`, at `files/AMHE1` beneath that root; the
extracted native MPH source exists. This is neither a GameCube ISO nor an original
NDS ROM. No concrete Prime/Echoes ISO/GCM path is registered or documented in the
inspected owner inputs. This does not establish that no such media exists
elsewhere; no broad media search was performed.

- `source-character-previews`, the default Retro exporter output, is absent.
- `character-models/default/characters.json` declares 29 entries, and all 29
  referenced GLBs exist: native seven-Hunter biped/viewmodel/alternate-form
  presentation plus Weavel halfturret. No entry has an external `characterId`,
  `skinSource` or provenance binding. Existing Samus/Weavel viewmodels therefore
  do not establish a Dark Samus/Trooper source-derived cannon.
- No source body/skeleton/weights/motion candidate or source cannon for the five
  supported extra appearances was located in those registered/default owners.
  Arbitrary private preview output locations were not searched.
- `user-maps/local-echoes`, the existing six-arena import destination, is absent.
  Installed catalog metadata nevertheless contains the two conversions below.
  No Spires, Crossfire Chaos, Pipeline or Spider Complex entry was found among
  the 27 installed top-level packages and inspected catalog recipes. The two
  conversions are not six verified Echoes source imports.

## Two installed local conversions

Paths below are relative to the canonical root's `user-maps/` directory. These
packages were already installed; this task did not import or publish them.

| Metadata | Sidehopper Station | Shooting Gallery |
| --- | --- | --- |
| Package | `20be17fd1b954637bca4a7706f0c4963.ppmap` | `c0d3242818825dca90a0fc49d765eabe.ppmap` |
| Catalog name | `SIDEHOPPER STATION ORIGINAL TEXTURES V7` | `SHOOTING GALLERY ORIGINAL V2` |
| MapId | `20be17fd-1b95-4637-bca4-a7706f0c4963` | `c0d32428-1882-5dca-90a0-fc49d765eabe` |
| Measured bytes | 115877229 | 92972921 |
| Measured package SHA256 | `7241443f22381f5ed8b974aaa1db3aa48b0873e61f1ae0869cb90e139c68bb66` | `097f95df99043be002b634d9170ee91c0f8875326532804f3c17eeb601903b04` |
| Declared content hash | `6eab4758bf868bed4bb0cb1acda4de70bf6988342f26b37bf1e8d63abe3a1965` | `b671c4fe7c8a1e8fd394f12dd176dbfd2a508e9e756d0b384f3ad8aedc91a9cd` |
| Measured manifest JSON SHA256 | `8de7b4c8bf72e25dc9de7b77a56077f8ba46fa9d7caa6f2cdbfe8c95598e41ce` | `9a303bc7a7120ee23be5fefdfd9f06a14b166c69e13a66a3d5ad5b28a56bb227` |
| Measured project JSON SHA256 | `aa6aac3ec4b3c29ac272b0861c4995aee2e5cd53ce3339f4dfbe4d3904284d03` | `0a39651ade645591b4e2b876fef90f06ae080c9083ff35881ec08dc5fbe4becf` |
| Assets / materials | 178 / 90 | 94 / 45 |
| Spawns / jump pads / items | 24 / 2 / 36 | 24 / 3 / 46 |
| Explicit navigation links | 0 | 0 |

Both recipes reference packaged `import/level.bsp`, `textures/map.tex` and
`collision/mesh.obj`, and all declared asset entry names are present. They are
Q3-BSP/OBJ conversions, not the `Metroid6.pak` MREA importer output. Their JSON
contains no original asset `SourcePath` or `modelSources` provenance. Both declare
Battle/BattleTeams/Survival/SurvivalTeams, 1..8 players, with `LocalSourceOnly`
absent. Those declarations are not proof of accepted online behavior, ownership,
license, correct collision, bot traversal or actual texture fidelity. No metadata
was changed and no sharing permission was inferred from those declarations.
The package-content hash is the manifest's claim; this inventory has not run the
actual package parser's independent content-hash/CRC/dependency validation.

## Existing source and recipe owners

Character owners are `Mods/Import/RetroCharacterPreviewExporter.cs`,
`RetroCharacterSource.cs`, `RetroAnimationIndex.cs`, `RetroMotionPreview.cs`,
`Import/Retro/{Ancs,Anim,Cmdl,Cskr}.cs` and the existing
`tools/retro-character-preview` authoring/acceptance pipeline.

- Echoes Dark Samus: ANCS `39900a11`, native name `Dark_Samus`; body, skin and
  skeleton references are obtained from the actual ANCS.
- Prime Troopers: ANCS `8169653a`, names `power_trooper`, `ice_trooper`,
  `wave_trooper`, `plasma_trooper`; these are four appearances of one catalog kit.
- `ExternalSourceRequirements` in `GameCubeDiscReader.cs` pins the archive SHA1
  recipes listed below. Supported source is uncompressed GameCube ISO/GCM;
  reserved `MP3DarkSamus` is not an implemented Wii/Corruption source.
- Existing output names are `<variant>.obj`, `.skeleton.json`, `.weights.json`
  and `.motions.json` under `source-character-previews`. They are biped authoring
  outputs, not source-derived first-person or alternate-form candidates.

The pinned archive SHA1 recipes in `ExternalSourceRequirements` are:

| Game | Archive | Expected SHA1 |
| --- | --- | --- |
| Prime | `Metroid1.pak` | `1ae13e2325ed8b57462f7a39d9d7fac035745598` |
| Prime | `AudioGrp.pak` | `157e0841155cc7b699176dd7afc746c7048dbc28` |
| Echoes | `Metroid1.pak` | `6b9a47578f41b2c64a12f1a0bf4e5cd783692124` |
| Echoes | `Metroid5.pak` | `b3375e83c5290d35d6c83f35b04b7f60f6fb8ea7` |
| Echoes | `Metroid6.pak` | `b8b92af37dc772ecd8234fb1ce664be0493188f6` |

Arena owners are `EchoesArenaSources` in `EchoesMreaSections.cs`,
`EchoesPrivateMapImport.cs`, `EchoesCollisionSource.cs`, `EchoesScriptSource.cs`,
`EchoesWorldGeometrySource.cs`, `EchoesLocalMapDraft.cs`, `EchoesVisualLayer.cs`,
`EchoesFloorPlacement.cs` and `EchoesTunedCannons.cs`.

| Arena | MREA resource in pinned `Metroid6.pak` |
| --- | --- |
| Sidehopper Station | `02690de1` |
| Spires | `09749b2f` |
| Crossfire Chaos | `44d34a2e` |
| Pipeline | `845dcf93` |
| Spider Complex | `e7095052` |
| Shooting Gallery | `d97a600b` |

The required `Metroid6.pak` SHA1 is
`b8b92af37dc772ecd8234fb1ce664be0493188f6`. The importer preserves source
collision separately from render geometry, deterministic tier-specific MapIds,
source-only floor tests and solo Battle limits. Its source-derived JSON goes to
`user-maps/local-echoes/`; the two installed packages above are a separate lineage.
Existing cannon/routed tiers cover twelve authored pads across four arenas,
using normal MapJumpPad simulation and one-way source-grounded routes. They do
not implement water, Gravity Boost, double jump, breakables or moving geometry.
`LocalSourceOnly` rejects imported source maps in network callers by default,
and MapPackageBuilder refuses to package that source-derived tier.

## Remaining concrete implementation boundaries

Current source is ahead of the older CPP-08 document's final private-draft
paragraph: `CharacterSourceRig`, `CharacterSourcePreviewBinding`, source-root/file
leases and per-segment palette gathering already exist. Do not implement a second
palette path. The binding accepts explicit immutable local poses for a private
biped preview, using the same scene/context resource owner.

The next bounded character code slice is a source-ID-bound temporal clip sampler
feeding that existing pose input at a caller-supplied time. Reuse the ANIM decoder,
retain full hierarchy evaluation and provenance, reject unresolved composite
blends/root-motion policies rather than fabricate them, and test exact global /
batch-gather skin positions. `RetroMotionPreview` currently samples at most 12
primitive clips at five times; it is not a full actor-state-to-clip/blend system.
Actual source clips, rig-axis calibration, first-person cannon/muzzle and
alternate-form authoring remain unavailable in the inspected default catalog.
Trooper wall cling, Scout Drone/body split, drone attacks and Pirate Blaster
server authority are separate gameplay work; no matching implementation was
found. Host-negotiated extra identity/skin/rules and historical replay behavior
also remain outside the private appearance preview.

For arenas, begin by qualifying the actual two installed conversions with the
existing owners below. Source-to-native material/texture conversion and the
four absent installed arenas need their actual source inputs; do not substitute
these packages for the six canonical MREA recipes. A future scoped mechanics
slice must explicitly bind arena/rules identity and preserve canonical MPH rules;
existing cannon pads do not establish Gravity Boost/water/double-jump parity.
Reuse `MapBuildFingerprint`/`MapBuildManifest` output hashes and
`MapContentIdentity`; do not invent a second compiler or fingerprint owner.

## Prepared qualification recipe; not executed

This is source-read-only qualification with private compiler outputs. Before any
run, the central owner must pin the actual client/tool/source recipe and approve
the diagnostic build. Use a fresh process with absolute `PROJECT_PRIME_USER_DATA`
set before game statics, a fresh working directory, private empty map catalog and
library, and a private cache/output root. If native MPH fallback inputs are needed,
use the existing `tools/native-render-check/campaign_identity.py`
`isolate_user_data` helper to write only normalized
`paths.txt` referencing the existing AMHE1 root; copy no preferences, credentials,
saves or package payloads. Keep all receipts and generated binaries private.

The exact parser/compiler/preflight owners are:

- `src/MphRead/Mods/MapGen/Packaging/MapPackageReader.cs`
- `src/MphRead/Mods/MapGen/Project/MapContentIdentity.cs`
- `src/MphRead/Mods/MapGen/Packaging/MapPackageInstaller.cs`
- `src/MphRead/Mods/MapGen/Build/{MapBuildSnapshot,MapBuildScheduler,MapBuildFingerprint}.cs`
  (`MapBuildManifest` is declared in `MapBuildFingerprint.cs`).
- `src/MphRead/Mods/MapGen/{CustomRooms,MapCommands}.cs`

Use their existing sequence:

1. Hash the original package and compare its exact SHA256 and MapId above.
   Instantiate `MapPackageReader(path)` and require its manifest, verified content
   identity, all source/texture/collision entries, and valid declared dependencies.
   Capture `MapContentIdentity.FromPackage(path)`; record compiler/version flags.
2. Set `CustomRooms.MapDirectory` and `UserMapDirectory` to private empty folders
   before querying those catalogs. Use `MapDefinition.Load(path)`,
   `MapBuildSnapshot.Capture`, `MapBuildScheduler.AnalyzeAsync` with navigation,
   then `BuildAsync` under a scheduler explicitly rooted in the private output
   directory. Record every diagnostic/budget and the actual five output hashes
   through the existing `MapBuildManifest` owner. Repeat into an independent
   private cache, not a cache-hit assertion, and compare exact output bytes.
3. For exact installer preflight without an archive copy or publication, a
   dedicated read-only diagnostic may invoke the existing private
   `MapPackageInstaller.PrepareSourceAsync(path, identity, ownsSource:false,
   preparationCopiedBytes:0, token, onValidated:null)` through reflection. This is
   not a new product API or a claim that a ready CLI exists. That owner performs
   identity/conflict/build validation and returns `PreparedMapInstallation`;
   inspect its public `Identity` and `ArchiveIo` and dispose it. Its build result
   is private; collect build diagnostics and output hashes through step 2, rather
   than claiming a public prepared-build inspection API exists. **Never call Commit,
   Install, InstallLeased, PackageAsync or any Community/HTTP owner.** The public
   mutable-source `PrepareAsync` copies a package to a private snapshot, so it is
   deliberately not the no-copy route described here. Pin and rehash the borrowed
   original before/after; concurrent-source changes must fail the receipt.
4. Do not run `-mapbuild` without an explicit private `-out`: its default install
   writes native runtime outputs. Existing CLI `-mapvalidate` and `-mapinspect`
   provide parser/analyzer routes, but they do not by themselves prove the full
   installation preflight or collision equivalence. A diagnostic avoids launcher
   startup/network/window side effects and reuses the actual owners directly.
5. Preserve source/package SHA before and after, build manifests and complete
   warnings/errors. Stop on failure. No bot/window/server/network test follows
   automatically. Later separate actual 0/2/4/8-player, bot navigation, collision,
   spawn/pad landing, replay/seek and Windows/Android byte-equivalence runs remain
   necessary; metadata capabilities alone cannot pass them.

No execution, build or window was performed for this recipe.

## Genuine DS trace capture on this Mac

`tools/fidelity-oracle/compare_ds.py`, `README.md` and `DS-TRACES.md` already
implement a strict consumer for original numeric DS traces: real ROM/core/tool,
route/setup/input hashes, measured 30 Hz logic parity, and matching 60 Hz Project
captures. They distinguish the 47 stored Project regression references from real
DS evidence. The Power Beam cadence producer supplies an actual Project-side
counter/input capture; it does not produce a DS trace.

The local repository provides no DS emulator runner or vendored post-frame Lua
capture driver. Its docs link the pinned Second Hunt BizHawk workflow/driver;
that link alone is not a configured Mac capture route. This check found no
`melonDS`, `melonds`, `desmume`, `retroarch`, `RetroArch`, `EmuHawk`, `BizHawk` or
`lua` on PATH, nor their named executables in `/opt/homebrew/bin` or
`/usr/local/bin`. No melonDS/DeSmuME/RetroArch/BizHawk/EmuHawk app was present in
`/Applications` or `~/Applications`. No concrete ROM/savestate/route/trace path is
documented in the inspected Project owner inputs; example `/local/private/...`
paths are placeholders. The AMHE1 extracted root is not a bootable NDS ROM.

Thus the comparison contract is usable on Mac, while a genuine original capture
is not currently ready from these documented local inputs. It still requires an
explicit owned ROM, compatible emulator/core and route-driving capture API,
measured update parity and matching setup, or an independently captured original
numeric trace. No ROM search, emulator install/download, capture or baseline
regeneration was performed. Original DS accuracy remains **NOT_VERIFIED**.
