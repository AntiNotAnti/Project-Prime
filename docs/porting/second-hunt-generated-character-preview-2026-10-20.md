# Generated Echoes character geometry and animated diagnostic preview

This slice removes the manual Blender/GLB dependency for a normal Echoes Dark Samus source preview. It does **not** complete a playable extra character. Source GX materials, first-person cannon, alternate-form runtime, Overcharged playback, Trooper abilities, selection, multiplayer and replay integration remain unfinished.

## Import and preview

The existing Echoes character importer now publishes recipe4, body/ball part3. Every part includes its original authoring files, full CINF/ANIM archive, generated `.source.glb`, and generated `.source.json` provenance. Normal Dark Samus also includes a private `characters.json` in that same immutable generation:18 required components/22 files; Overcharged has17/21. The four Prime Trooper recipes remain version1 with their historical fingerprints and required files.

In desktop Settings, choose normal Echoes Dark Samus, Update Selected from the owned Echoes source if needed, then **View Animated Preview**. The view supports all admitted strict rigid clips, previous/next, pause, restart, rotate, zoom and reset. Back restores the Settings draft. It never equips a character or changes global character replacement preferences. The current physical source-root runtime qualification is macOS arm64; other devices/platforms remain unqualified.

The view captures one Ready import generation on a cancelable CPU worker. Opening refuses missing, outdated, incomplete, corrupt or unsupported bundles. A running view retains its captured generation until close; reopening rechecks discovery. Update/Remove never mixes files from generations. Closing, covering or invalidating the document prevents stale worker publication and releases the GPU attachment. There is no new Scene, renderer, graphics context or player identity: the existing launcher Hunter stage/side Scene owns drawing.

## Source geometry contract

`RetroCharacterSourceGlbWriter` is a bounded managed writer. It captures caller arrays, material flags, positive influence tuples and the complete source skeleton before validation/encoding. It emits GLB2 with one embedded buffer and no external URIs or baked animation tracks. The existing full ANIM archive remains the temporal owner.

The writer applies `(-x,z,y)` once to positions, normals and skeleton translations. It retains corner normals/UV seams, winding, all original material groups, and every positive skin influence without normalization or pruning. Unsupported >4 positive influences, non-unit sums, invalid attributes, foreign/cyclic parents and oversized inputs refuse. Root97 is an explicit single external-root contract. All39 body joints and both ball joints remain present. Inverse binds come from complete global source bind translations; mesh ancestry stays identity.

Original material/triangle ordinals are retained in each primitive. The existing loader partitions whole triangles into palettes of at most32 joints after complete hierarchy evaluation; the original primitive/material/triangle identities survive each partition. Capture work is bounded at4M scanned influences,500k corners/positions,1023 bones and256 materials; output retains the64MiB component,4MiB GLB JSON and existing256MiB bundle limits.

Materials use **authored procedural diagnostic colors**, one embedded opaque pixel per original material group, with a fixed managed PNG/zlib encoding. They do not claim source TXTR/TEV accuracy, lighting fidelity or source transparency. No original cartridge texture is used as a source-material substitute. The native model supplies only the existing draw anchor and stage lighting. Source pose/root motion stays intact; a separate preview-only fit transform frames the model and follows source-root displacement without changing the raw skin palettes.

## Admission and lifetime

Generated provenance format2 is separate from legacy manual format1. It cannot invent native-kit/mapping inputs. The first admitted generated profile is exactly normal G2ME01 Dark Samus, CMDL `d2c3805b`, CSKR `b9ec2f8d`, CINF `4abc8a50`, ANCS `39900a11`, with4245 positions/6340 triangles/7 materials/39 joints. Source/geometry/animation hashes, writer version, basis, material policy and false gameplay/part/device flags are checked. The generated GLB pin is reproducible through the product importer rather than a manually authored file. Legacy private provenance and Final refusal remain unchanged.

| Generated part | GLB SHA-256 |
| --- | --- |
| Normal body | `38b03806faf22851efd3caeb4fcd22b900556b5fc4b21a2051b075b63f6fb8ca` |
| Overcharged body | `ff1866fd7c92c7aad0326381e059119febd0993d63aacc85b45400b019af5164` |
| Shared ball geometry | `eca17407d4933e3a1d829bc931de02561321c185d9bbaa3a574538d57d8851c9` |

The existing exact pack/asset/lease generation and source-animation profile own playback. Cached drawing samples captured clips without source file IO. The explicit diagnostic stage permission is scoped to its exact launcher side Scene, foreground request and owner; it does not enable global character replacement. Request attachment/clearing runs every draw, including frames without a60Hz native preview step. Closing the page retires its own binding; a foreign owner cannot close it. No source pose is written to a gameplay model.

## Verification status

Exploratory desktop builds pass with zero warnings/errors. Synthetic writer checks cover deterministic bytes, seams, short/float/absent UVs, exact source weights, forward hierarchy references, full39-joint loader partitioning, cancellation, detached capture and malformed/budget refusals. Settings fixtures cover dirty draft preservation, captured selection, state gates, stale documents and late worker retirement. The owned bundle probe compares every generated original source corner and joint, repeated writer bytes, source/provenance hashes, same-generation publication and repair/reuse/cancel/remove for all four Echoes parts; the source RVZ remains unchanged and derived test payloads are deleted.

Actual GPU, native layout and final frozen build qualification are pending in this work branch. No current evidence here claims character gameplay or untested platform parity.
