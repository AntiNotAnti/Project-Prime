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

## Frozen verification

Production/test commit `dd191b6eb1bb49d0bb485c40300e0cabb4b283e5`, based on current main `8c6ec015d`, passes **42/42 serial commands** from two fresh Git archives. All6110 tracked files in both archives remain unchanged after testing. The documentation follow-up does not change production/test bytes. Protocol71 comes from the merged movement baseline; this slice adds no wire IDs, packet changes, Hunter identities, native intent ABI or canvas ABI changes.

* Desktop and dedicated Release builds: zero warnings/errors.
* Android Release:25 existing warnings, zero errors;119 AOT assemblies for each of arm64/x64; signed APK verifies with v2/v3.
* All three native libraries rebuilt from the exact pinned dependencies/current native closure and independently verified: macOS arm64 GL2 and Android arm64/x64 draw-list.
*150 markup/style files resolve; native Home178 checks and canvas CPU1085 checks pass.
* Full importer, animation, character, Settings, game-files, archive, ship and credits suites pass;48 Python authoring tests and910 source-rig asset checks pass. The new writer contributes421 checks; bounded animation/archive fixtures retain977/177 checks.
* Owned bundle acceptance accounts for every original corner/material/weight/joint, deterministic repeated output and same-generation repair/reuse/cancel/remove for all four Echoes parts. All16 historical authoring component hashes remain identical to the independent earlier receipt.
* Actual runtime playback retains55 declared strict rigid clips,2979 key samples and165 off-grid samples with no sampling-time proof/file reads. Generated profile acceptance passes 33 checks and 32 refusal cases, a fully rehashed format1 downgrade, forged geometry, false gameplay claims, and retained selection/lease use after Remove with reopen refusal.
* Actual GPU acceptance passes228 checks over18 frames and two recreated contexts: post-twelve raw clip13 visibly animates; four visible angles differ; resources and A/B/A palettes stay stable; immediate native restoration needs no60Hz step; foreign owners cannot close the source binding.
* The actual native source page passes96 checks and six translated clicks at each physical960×600 and640×480, with global character replacement still disabled. Native focus scrolls the control pane; actual pointer Back closes the view. Draft/stale-document/late-worker fixtures pass separately.
* Historical protocol58, Quake3, pickup and grant replay checks and campaign map/timing checks pass. No extra-character multiplayer/replay playability is claimed.

The initial frozen7dcd8f871 run caught two Android references to the desktop preview owner; the final source adds explicit platform guards. Exploratory tests also exposed the source-root framing and small-window footer overlap; the final GPU/native pointer tests verify their corrections. Superseded failures remain in the private receipt history.

Windows SSH authentication was rejected (exit255), so the requested Mac fallback was used. These tests do not qualify a Windows/Linux source-root runtime, physical Android device, GX material fidelity, game actor, cannon, morph transition, extra selection or performance parity. All owned source hashes remain unchanged; all temporary derived media and test contexts were deleted. Receipts retain metadata, counts, IDs and hashes rather than source pictures, geometry or pose arrays.

Private receipts: `artifacts/sh-integration/character-generated-final-dd191b6eb/`, with the earlier native builds/exploration under `character-generated-preview-exploratory/`.

| Frozen output | SHA-256 |
| --- | --- |
| Git archive | `cfa3ed4e30bde15ecaad99334cb338e9af070d97aac4c07706b120d5a74245a8` |
| Desktop DLL | `79ddbd98e5890ba1bc33ba5e646616bb2b7e9eb95f4011b49fea35e60fe26f1c` |
| Import test tool | `fb6bb6e569dcd47dbed875eb23f3b897fc9b6f2224936bf4359a6f9f10d9fc3b` |
| Dedicated DLL | `8c3e715a81a0b1e868d19086bb5a23f615c9d11970d7f23a3be07633fe2acdc5` |
| Signed Android APK | `064e5556ac4a8e2cbb98e75f5013e8aad589fba9b2ba8f3e97b60e06be02bef3` |
