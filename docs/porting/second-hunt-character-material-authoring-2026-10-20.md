# Echoes body material and texture authoring

Implementation slice following PR878. This is private import work, not source
shader activation or playable-character completion. Normal and Overcharged
body imports now retain their complete original material-set records and all
TXTR resources referenced by any local material slot in one required
`.materials.bin` component. Related ball and Trooper materials remain future
work. Source TEV rendering, Overcharged runtime admission, first-person firing,
Trooper abilities and selection/network/replay remain unfinished.

## Ownership and behavior

`RetroCharacterMaterialArchive` is a pure, bounded CPU codec. The existing
`RetroCharacterPreviewExporter.WritePart` owns extraction under the verified
Echoes source requirements. The existing `LocalImportBundle` owns staging,
integrity receipts, cancellation, immutable generations and publication.
Normal advances to bundle6/body5, Overcharged to bundle5/body4; ball stays3.
Updating a prior bundle is deliberate. Its old generation is retained, and a
failed or cancelled update cannot publish the new receipt.

The archive stores the complete first source material-set section, including
opaque stage/binding bytes and record/set tails. Parsing a material object and
serializing it again would lose those bytes. Every referenced material-local
texture slot is covered, even when no TEV stage executes that slot. Shared
TXTRs are stored once in sorted resource order. Local slot, material-set table
index, TXTR ID, TEV texture slot and texgen index remain separate namespaces.
Unknown shader operations remain raw authoring data; they are not silently
translated, truncated, masked into a supported shader or executed.

The header binds appearance, pinned source fingerprint, source CMDL/CSKR/CINF/
ANCS IDs, full CMDL and CSKR hashes, generated diagnostic GLB hash, raw material
set hash and the existing pixel contract defined by
`RetroTextureSource.PixelContract` (`gx-expanded-cmpr-3-5-rgb5a3-v1`).
Original tiled mip chains and padding are validated and retained in the TXTR
bytes. The decoded pixel hash covers top-first straight RGBA8 with source
alpha preserved. No original-mip sampling or hardware-pixel fidelity is claimed.

The format has a256-byte header and112-byte sorted texture directory entries.
All payload extents are contiguous, bounded and individually hashed. Reserved
fields must be zero; missing/duplicate textures, mismatched source references,
invalid mip extents, integrity errors and trailing bytes refuse the complete
component. Maximums are32MiB archive/CMDL,2MiB material set,32 materials,
64 textures,4MiB per TXTR/CSKR/decoded image and16MiB aggregate decoded pixels.
All texture layouts and aggregate budgets pass before pixel decoding begins.
CMDL extraction bounds its entire section directory before accessing the
first material set and requires the observed body envelope version4. This
does not expose the legacy complete geometry parser as an untrusted reader.

The reader validates a detached copy. Its lists and raw byte spans are read
only; `ReadProgram` returns a fresh mutable parser object whose mutation cannot
change the snapshot. Texture decoding returns independent immutable pixels.
The codec owns no files, GL objects, model caches, source lease, Scene or clock.
Successful structural decoding is not trusted source/UV evidence or a runtime
capability. A later rendering owner must bind the same verified generation,
source/geometry identities and independently qualified semantic profile.

Diagnostic GLB generation, provenance, normal fixed-cannon inputs/recipe,
related ball components, default native gameplay, Hunter identities, protocol
and replay layouts are unchanged. No new public selection or package route
is registered. Rollback is to the preceding importer recipe; retained old
source generations can be reused by that code without deleting user saves or
original source discs.

## Qualification

Pending frozen-source checks. No rendered-material or platform acceptance is
inferred from the implementation above.
