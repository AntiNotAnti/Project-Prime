# Headless server content

The game data directory is the extracted cartridge filesystem, containing
`models`, `levels`, `_archives`, and `_bin`. An `.nds` file is the cartridge
image, not this directory. Development servers can load the extracted directory
directly. The server-content command creates a separate package for headless
simulation without copying the whole cartridge or changing `paths.txt`.

```sh
dotnet FruityPrime.dll -servercontent /srv/fruity/content-amhe1 \
  -data /path/to/extracted/AMHE1 -dataversion AMHE1 \
  -room 'MP1 SANCTORUS' -room 'MP3 PROVING GROUND' -room 'MP4 HIGHGROUND'

dotnet FruityPrime.dll -headlesscheck 'MP1 SANCTORUS' \
  -data /srv/fruity/content-amhe1 -dataversion AMHE1 \
  -mode Battle -players 8 -frames 3600
```

The output must be a new directory outside the input tree. Baking currently
supports the USA revision 1 release, identified by the SHA-256 of its extracted
`arm9.bin`; an operator-supplied version label alone does not establish identity.
No ROM extraction, downloads, persistent configuration writes, or repository
asset copies occur during baking. A failed bake removes only its own staging
directory. Successful output is published by renaming that directory.

Use `-allrooms` instead of repeated `-room` arguments to select the shipped
retail multiplayer table. Selection excludes campaign rooms, First Hunt,
unused/unreferenced rooms and appended custom maps.

## Package contract

`server-content.json` uses format 1 and profile `headless-cpu-v1`. It records the
source revision fingerprint, sorted relative paths, file lengths and SHA-256
hashes, requested rooms, entity-layer player count, probe duration, and
supported room/mode combinations.
No timestamps or absolute source paths enter the manifest. The compiled server
continues to supply gameplay tables and room metadata; the package format must
be revised if its CPU data contract changes incompatibly.

Every package is verified before `ServerContent.Open` selects it. Validation
rejects unknown formats/profiles, another game revision, missing/modified files,
unlisted files, duplicate or unsafe paths, symbolic links and excessive manifest
or payload sizes. File hashes establish integrity relative to the manifest;
they do not authenticate an untrusted publisher. Operators provide content from
their own trusted extraction.

The baker runs each requested room in all twelve multiplayer modes with eight
hunters, using the same fixed two-player entity layout as the authoritative
server and clients. The layout count selects map entity layers; it does not
limit the number of connected players. Both RNG streams reset before each
scenario, so source and projected probes use the same seeds. It observes the files actually consumed during 600 simulation frames,
including reads through shared model, collision, entity, animation, navigation
and AI parsers. A room/mode is advertised only when all eight hunters spawn with
finite positions and required objective entities exist. Capture requires flags
and scoring bases for both teams; Bounty requires an Octolith and scoring base;
Nodes and Defender require a defense node. Unsupported combinations are reported and excluded. Baking
then repeats all advertised probes using only the staged package, and publishes
it only after those probes pass. `ServerContentPack.Verify` supports longer
bounded replay checks for automation.

## Preserved simulation data

CPU model projection preserves node hierarchy and transforms, node weights and
position tables, material and mesh metadata, and display-list bounds used by
entity setup. It omits texture/palette payloads and display-list render commands.
The shared parser uses the same gameplay and animation structures in headless
mode while supplying empty presentation containers. Original animation files
are retained because their frame counts and transforms affect gun state,
attachments and movement. Some animation channels describe appearance; the
package does not claim that every retained byte is physics data.

Collision, entity placement, navigation and AI files retain their established
binary formats. Entities provide spawn points, doors, moving platforms,
teleporters, jump pads, pickups and objectives. Headless initialization skips
fonts, HUD strings, visual effects, audio data and presentation-only models.
Switching content directories clears model, collision, AI and string caches so
the previous extraction cannot mask a missing packaged dependency.

## Validation and limits

The focused tests cover model projection, truncated/out-of-bounds input,
manifest integrity, traversal, duplicate paths, malformed metadata, version
mismatch and symlinks. Independent native headless probes compared projected
model simulation with the full extracted data; the 3,600-tick eight-hunter
Sanctorus Battle final states were identical. Longer 36,000-tick probes also ran
on three maps in Battle, Nodes, Capture and Bounty.

`MP4 HIGHGROUND` lacks usable Capture team spawns in the full extracted data;
that combination is excluded. It is not repaired by content projection.
Sanctorus and Head Shot CTF navigation filenames were corrected from the
invalid `.bi)` suffix to the existing `.bin` files so their navigation data is
loaded and packaged.

The observed-dependency approach proves the declared probe workload. Rare
gameplay paths and additional/custom rooms require their own exercised coverage;
the manifest does not claim universal asset coverage from one map or mode.
Custom/First Hunt/hybrid rooms and other cartridge revisions are not accepted by
this baker. Packaged CPU models are for headless servers and must not be used as
a client's rendering asset directory.

All measured packages and supplied game assets remain outside the repository.

The full retail bake covered 26 rooms and 312 candidate room/mode combinations.
245 combinations passed spawn and objective requirements and then replayed from
the staged package, for 334,200 total simulation ticks. The output contains 230
files, including 85 CPU models: 6,649,127 payload bytes plus a 60,811-byte manifest.
That totals 6,709,938 bytes versus 97,394,584 bytes for the extracted source tree,
a 93.11% reduction. A second independent full bake produced a byte-identical
manifest and payload hashes. No HUD, font, string-table, audio, movie, shader, texture or
palette files occur in that package; projected models contain no texture/palette
or display-list command payloads.
