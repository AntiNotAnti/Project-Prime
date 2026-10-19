# CPP-08 imported character readiness audit

Audit date: 2026-10-17. The source audit below describes the initial checkout.
The approved bounded implementation is recorded separately below. Neither is
gameplay, imported-media, renderer, multiplayer or device acceptance. Wire
protocol, Hunter IDs, baselines and imported assets remain unchanged.

## Existing owner and pins

Continue [`second-hunt-content.md`](second-hunt-content.md) and
[`second-hunt-manifest.json`](second-hunt-manifest.json). Do not restart PORT-00
or the completed PORT-01–26 decoder/authoring work.

- Current Project checkout HEAD: `dc7f097067d755ec76d145fc7cc333068566863d`, with
  shared uncommitted RmlUi/CPP work; this is not an exact-head release claim.
- Existing PORT contract target baseline: `ebbf3c51fa6f205ac3b8c952f4f3ab67d0e7cc80`.
- `git ls-remote https://github.com/BountyHunterKanden/SecondHunt.git HEAD`
  returned `c16a907b3658c2fd4079e394a73711596a85caee` again during this audit.
  That matches the existing PORT manifest pin.
- Current Project multiplayer protocol is **57**, from
  `NetConfig.ProtocolVersion` in `Mods/Network/NetProtocol.cs`. The original
  attached audit's older protocol number is not the current wire contract.

## What is implemented

| Area | Current source-backed state | Remaining boundary |
| --- | --- | --- |
| Stable names | `ExternalCharacterCatalog` defines `darksamus` with Samus semantics and `trooper` with Weavel as initial parent; extras default off. | The catalog has no runtime consumer. Its presence does not enable selection or abilities. |
| Original IDs | `Hunter` retains Samus–Weavel values 0–6, Guardian 7, Random 8. | Never append extras by reusing Guardian/Random or weakening seven-Hunter wire validation. |
| Local source import | Bounded GameCube FST/PAK, ANCS/CINF/CMDL/CSKR, skin weights, and ANIM readers already exist. Source exporter verifies pinned archive editions and stages private output. | No runtime character install/activation transaction; MP3/Wii remains reserved/deferred. |
| Five body variants | Echoes Dark Samus and four Prime Trooper appearances have private OBJ, skeleton, weights, sparse motion, native-rig calibration and Weighted4 candidate tooling. | Actual media/artist calibration/device evidence is still required. Corruption is not one of these five supported exporter variants. |
| Native animation previews | `RetroMotionPreview` samples at most 12 primitive clips at five timestamps; unresolved blends remain explicit. | These are diagnostics, not full clip playback, state mapping, blend graph or accepted root-motion treatment. |
| PORT-24–26 acceptance | Existing private test-pack and complete-five runner use the existing native model validator and honest receipts. | They stage one LOD0 **biped** entry and explicitly deny gameplay, first-person, alternate-form, source-motion, selection and online acceptance. |
| Render ownership | `CharacterModelPack` and `CharacterModelRuntime` resolve by `(Hunter, CharacterModelPart, LOD)` and keep the native model authoritative. | They cannot distinguish native Samus from a Dark Samus appearance on another seat, or different Trooper looks on simultaneous Weavel-parent seats. |
| First person / alt | Generic native kit, ViewModel/AlternateForm/Halfturret model parts and first-person acceptance checks already exist. | No source-backed extra-character cannon/alt candidates, muzzle calibration or full per-character acceptance are integrated. |
| Network / replay | Roster and replay metadata carry original Hunter identity; authority/match/slot/life fences and cosmetics packets already exist. | No negotiated extra-character/skin/ability identity. Current cosmetic IDs are not an external character capability channel. |
| Trooper mechanics | Existing Weavel/Halfturret gameplay remains available to the native parent. | No Trooper wall-cling, Scout Drone, drone laser or Pirate Blaster implementation exists in Project. |

Relevant current owners:

- `src/MphRead/Mods/Import/ExternalCharacterCatalog.cs`
- `src/MphRead/Mods/Import/RetroCharacterPreviewExporter.cs`
- `src/MphRead/Mods/Import/RetroMotionPreview.cs`
- `tools/retro-character-preview/retarget_contract.py`, `build_native_glb.py`,
  `local_visual_pack.py`, `acceptance_matrix.py`
- `src/MphRead/Mods/Render/Characters/CharacterModelPack.cs`,
  `CharacterModelRuntime.cs`, `CharacterModelAuthoring.cs`,
  `ViewModelAcceptanceCheck.cs`
- `src/MphRead/Mods/Network/NetProtocol.cs`, `NetCosmetics.cs`,
  `ReplayFormatV3.cs`

RmlUi currently enforces the seven-Hunter contract in its draft, intent and 3D
stage owners. Adding a selectable extra requires a separate validated identity,
not changing `HunterSelect`'s integer range or replacing the real 3D picker.

## Upstream semantics to preserve deliberately

The pinned [Characters owner](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/src/MphRecomp.Core/Multiplayer/Characters.cs)
separates character, native parent and cosmetic skin. Dark Samus keeps Samus's
gameplay; Trooper has four looks and separately gated abilities. Missing imports
use locally available parent presentation. This is a useful identity model,
not permission to adopt its match or networking authority.

The pinned [character/rig decisions](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/docs/CHARACTERS_AND_SKINS.md)
require each Dark Samus skin's own source rig/clips and a source-derived cannon.
Project's native-rig biped candidate is an intermediate authoring option; it is
not proof that the own-rig requirement is complete. A 39-bone Echoes source rig
also cannot simply be passed into Project's current 32-joint Weighted4 skin
contract without deliberate palette partitioning or another validated draw path.

The pinned [wall-cling owner](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/src/MphRead/Entities/Players/PlayerWallCling.cs)
uses host-enabled collision-face checks, three height probes, bounded wall slope,
duration and chain count, and resets on relevant form/life conditions. Its rules
need Project server validation and movement/replay auditing; copying client
movement hooks would not establish authority.

The pinned [Scout Drone](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/src/MphRead/Entities/Players/PlayerScoutDrone.cs)
and [drone laser](https://github.com/BountyHunterKanden/SecondHunt/blob/c16a907b3658c2fd4079e394a73711596a85caee/src/MphRead/Entities/Players/PlayerDroneLaser.cs)
introduce flight, split-body health, attacks and damage state. Project must own
those transitions and outcomes on the dedicated server, including lifecycle
fences and recorded authoritative facts. Source gameplay is not a cosmetic import.

## Recommended bounded next slice

**Identity-keyed private appearance resolution in the existing model-pack owner.**
This addresses the present global-by-Hunter alias before exposing extra
characters or claiming first-person/animation completion.

1. Add optional catalog character/skin keys to existing local model resolution,
   preserving all existing native-Hunter format-1 behavior. Use only the current
   catalog's names; do not invent numeric character IDs or new Hunter values.
2. Validate character, skin source, native parent, model part, LOD and local
   provenance together. Unknown, mismatched, unavailable or unsupported inputs
   must resolve to an explicit unavailable/native result, never another seat's
   appearance. Keep Corruption's reserved identity separate from actual support.
3. Exercise per-seat appearance inputs in the existing private acceptance path:
   two Samus-parent actors must resolve native/Dark Samus independently, and
   Weavel-parent actors must distinguish the four Trooper appearance keys.
   The initial consumer should remain the private preview/acceptance path.
4. Reuse `CharacterModelRuntime` resources and fallback; do not add another
   importer, global replacement setting, renderer, controller or public picker.
   Keep the current biped-only receipt and all unperformed acceptance flags false.
5. Refuse publication through live network or replay identity channels. No new
   abilities, weapons, protocol fields or competitive/default enablement in
   this slice. A later match/replay owner change must explicitly carry identity.

The slice's acceptance should include unchanged old pack resolution, independent
same-parent seat choices, canonical catalog/skin rejection, missing/tampered
source rejection, native fallback, draw-only state invariance, absent-import
behavior, and unchanged original Hunter/wire/replay contracts. Synthetic assets
prove those contracts only. Actual imported media and device validation remain
separate. No new product UI is needed for this precursor; any later selector or
availability UI belongs in the existing RmlUi owners.

This is preferable to claiming an entire first-person/animation port in one
change. The subsequent presentation slice should extend the existing private
part/kit/acceptance pipeline for a separately authored cannon, alternate form,
full state-to-clip mapping, source-native rig playback and muzzle/VFX calibration.
Do not reinterpret a sparse source-motion preview as runtime animation evidence.

## Bounded implementation

The approved precursor now extends `CharacterModelPack` and its existing runtime
and PORT acceptance owners. Optional `characterId`, `skinSource`, `provenance`
and `provenanceSha256` fields form a complete private binding. Canonical catalog
identity, supported source variant, native parent, one Weighted4 LOD0 biped,
contained receipt, actual GLB hash and source witness hashes must agree. Partial
or unsupported bindings fail closed. Legacy Hunter-only entries retain their
existing lookup and rendering behavior.

Explicit per-seat private inputs resolve independently from native entries.
Runtime GPU/failure caches include the appearance key, so a same-path asset
cannot alias native and extra identities or distinct skins. The runtime requires
an explicit private pack and preview capability and refuses network, replica
and replay use. No actor selection state, public selector, gameplay owner or
wire/replay identity was added. The validator is the initial private consumer;
there is no newly activated product scene flow.

The existing `-charactermodelcheck` now generates triangles for a legacy native
Samus entry and all five private variants. Its assertions cover independent
same-parent choices, explicit gating, cache identity, absent/unknown/reserved
imports, parent/skin mismatches, contained provenance, altered receipts and
unperformed acceptance claims. The PORT Python suite exercises newly staged
bindings and separately labels legacy packs. These are synthetic protocol and
geometry checks; none represent actual game-source acceptance.

The source-only Python suite passed **32 tests**, and the scoped diff passed
whitespace checks. The coordinating task then reported a central server build
with zero warnings/errors, the restored opt-in client build with zero errors and
four existing warnings, and `-charactermodelcheck` exit **0 / PASS**. Its log is
`/tmp/prime-cpp08-character-model-check.log`. The check generated its synthetic
assets in a temporary root; ordinary console diagnostics also wrote to the
canonical user-data log. No assets, saves, settings or account state changed.
No main build was run by this implementation agent.

All generated receipt and matrix readiness flags remain false. Local source
hashes witness file consistency, not licensing, ownership, native source-rig
correctness or full animation coverage. No proprietary files were added.

## Gates still open after that slice

- Distinct character selection and per-seat, host-frozen identity through
  private match setup, admitted lobby/roster, reconnect, death/respawn and replay.
- Own source rig/clips, first-person cannon, morph/drone presentation, LOD,
  source textures/material provenance and missing-import appearance acceptance.
- Server-authored Trooper wall cling, Scout Drone/body split and return,
  Pirate Blaster collision/fire/damage; movement shadow and bot AI coverage.
- Negotiated optional capabilities and historical replay decoding; no fake
  character or attack facts when old recordings do not contain them.
- Windows/Android actual import/absent-import tests, first-person muzzle and
  hit-volume parity, 1v1/4v4/eight-player/bots, seek/reconnect and performance.
- No copyrighted source or derived game assets in Git, releases, Community
  packages, server transfers or network traffic.

No source media, generated body/cannon files or proprietary artifacts were
copied for this audit. No main build was run.

## Own-source-rig palette preflight continuation

The next bounded tooling slice is implemented in
`tools/retro-character-preview/source_palette.py`, reached through the existing
`build_glb.py --validate --palette-plan PRIVATE_PATH` command. The destination
must be a new file under the caller's private source root. This produces a
private diagnostic JSON plan, never a model pack or an accepted runtime GLB;
ordinary validation and Blender export behavior remain unchanged.

The planner retains the entire CINF hierarchy and source motion inventory,
including unweighted ancestor/helper bones and unresolved clip entries. It
partitions triangles in stable source order into draw batches using at most 32
positive-weight source bones each. Every triangle appears exactly once. A batch
remaps its vertex joint indices into a sorted local palette, preserving all
positive source weights exactly, triangle winding and UV corner seams. It never
retargets a bone, merges duplicate influences, normalizes weights or truncates
influences to make a batch fit. Zero-weight entries do not consume shader slots
and their count is explicit. A triangle that cannot fit fails closed.

The plan requires full source-hierarchy evaluation before each batch gathers
skin matrices by source bone ID; dropping an ancestor from a draw palette must
not drop it from animation evaluation. Limits are 200,000 source vertices,
200,000 triangles, 2,048 batches, 600,000 emitted draw vertices and 64 MiB of plan
JSON. Existing source file, hierarchy and four-positive-influence validation
also applies. Before/after SHA-256 stamps reject source mutation during planning;
they witness file consistency, not ownership or licensing.

The complete source-only Python suite passes **40 tests** (32 prior and eight
new). The new synthetic 39-bone fixture produces two draw batches and preserves
all 39 source bones. Across six deterministic hierarchical poses with known
synthetic inverse binds, an independent CPU oracle produces the same skinned
positions using global source indices or gathered batch-local indices/matrices.
Tests also cover all five supported variants, unused parents, UV seams, winding,
deterministic output, malformed indices/weights/cycles, partition/vertex/output
budgets, source stamps, source mutation, path containment and no overwrite.
Log: `/tmp/prime-cpp08-source-palette-tests.log`. No Blender, renderer, native,
main application, shared acceptance tool or GL build was run. All runtime,
source-rig playback, full-clip, first-person, alternate-form, gameplay, network
and device-visual acceptance fields remain false. Tests use only synthetic
geometry in temporary directories; no proprietary files were added.

## Private draft: minimum runtime consumption seam

This is the next implementation boundary, not completed renderer support.
Current `CharacterWeightedModelLoader` permits one shared skin with at most 32
joints, retargets every joint through the native `BoneMap`, and packs those global
joint indices before drawing. `CharacterModelRuntime` then builds one native
palette for the whole replacement. The existing player draw loop submits that
same palette for every segment. A 39-joint own-source-rig GLB therefore remains
explicitly unsupported after the tooling slice.

The smallest compatible extension should keep one complete source skin and the
existing 32-matrix shader draw contract:

1. Add an explicit private source-rig preview mode to the existing pack/asset
   owner. Require the existing canonical private appearance, source/GLB hashes,
   contained provenance and preview gate. Preserve legacy/native Weighted4
   behavior and all network/replica/replay exclusions. Do not infer this mode
   from an oversized skin or silently bypass native rig validation.
2. In `CharacterWeightedModelLoader`, separate bounded full source-rig inventory
   from the 32-entry draw palette. Read global joint indices and inverse binds
   before packing. Preserve all ancestor/helper nodes needed for the source pose;
   validate source-ID-to-GLB-node bindings explicitly. Partition each existing
   material primitive with the tested algorithm, then pack batch-local indices
   through the existing `PackJoints`. Store each primitive's source-joint gather
   map; material, normal, UV, winding and embedded-image validation stays with
   the current loader. Multiple GLB skins and larger shader uniforms are not
   required for this design.
3. Extend the existing `CharacterWeightedModelData`/render segment records with
   per-batch gather maps and bounded matrix buffers. The full source pose and
   inverse binds must be evaluated consistently before gathering; native-mode
   assets continue using native animation nodes. Initially accept an explicit
   immutable diagnostic source pose from the existing private preview consumer,
   without a new simulation clock or gameplay animation owner. Full source clip
   decoding, state mapping and blends remain separate gates.
4. In the existing runtime/draw submission seam, gather and submit each segment's
   own matrix count and buffer instead of `weighted.Joints.Count` and the single
   model-wide `MatrixPalette`. Keep scene/context resource ownership and existing
   appearance cache keys, and ensure per-batch palette buffers cannot alias a
   different actor or pose. The shader and 20-bit packed four-joint vertex field
   can remain unchanged because each actual draw still has at most 32 matrices.
5. Prove the loader/partition/gather path with source-linked synthetic GLB and
   CPU-pose tests first, then the existing private render acceptance consumer.
   Actual imported bind/axis conventions, material provenance, complete source
   clips, cannon/alternate-form parts and device evidence remain unaccepted.

This draft identifies the minimum existing-owner changes needed for actual
source-rig integration. The preflight is a verified preparation step and does
not substitute for that runtime work.


## CPP08 runtime and synthetic GPU closure: immutable RML65

The minimum runtime seam described above is now implemented in the existing
pack, Weighted4 loader, runtime, preview and acceptance owners. Source mode is
explicit private LOD0 biped preview on macOS arm64; the canonical/default
native32 path and shader ABI remain intact. The complete source hierarchy and
inverse binds are evaluated before each segment gathers at most 32 matrices.
Bounded source proofs pin the resolved physical root/ancestry, exact bytes and
lease generation. Resolve/compile/prepublication/explicit refresh perform strong
proofs; cached immutable draw resources do not reread or hash source media per
frame. Private appearance, network/replay exclusions and owner/context retirement
remain mandatory. No public character selection or gameplay state was added.

The actual immutable RML65 `-charactermodelcheck` completed with exit 0, including
904 source assertions and the preexisting model suite. On 2026-10-18 the private
v3 process entry then invoked the same complete compiled
`CharacterAcceptanceCheck.Run`, using its optional private source arguments and
the existing window/renderer owner. The full ordinary native suite completed
through launcher-after with exit **0 / PASS** and zero missing frames reported.
The separate synthetic source receipt records **24** fixed diagnostic captures:
39 joints, 41 hierarchy nodes, two batches with palettes of 31 and 11 matrices,
and actual 1600×1200 PNGs. SaveWindow had to return true; every accepted file was
checked for bounded PNG structure/dimensions and SHA-256. All 24 recorded lengths
and hashes were reverified after exit. This qualifies only the synthetic private
rig/palette consumer on this Mac, not real source characters or animation clips.

The first startup-hook attempt failed main-thread eligibility before GL; the
second private entry reached GL but failed native PNG encoding. Both true
nonzero exits and original pre/post manifests are preserved. The v3 correction
removed the private generic unmanaged resolver, which had prematurely attempted
to load a Windows STB payload during ReFuel probing, and used the client's exact
ReFuel.StbImage 2.1.1 runtime dependency graph. Its no-window sanity test called
the actual compiled PNG encoder and receipt owner, verified exact native RGB
encode/decode/flip, and rejected false writers and malformed PNGs. No screenshot
fallback, GLFW safety override or production codec change was introduced.

Exact successful-run identities:

- Client `ProjectPrime.dll`: `91c74fec1ed146d14a87d7bb05e40d3b59b2eb65a04aa4e5ae258f34e7123d2e`.
- RmlUi native: `830b5ffdbdfbe7559b6ab97197403059d34d34b8ec7817fd77b688fa7f580be5`.
- Private v3 entry: `6e10b0a9278091b9226449f2581de99cc3a3982813ef331a559ebe6831bcc965`.
- Managed ReFuel codec: `191b521ce25d03c73ae10b716b3e55b1a1fcca988201b50cbac9603fcee10ea3`.
- Actual loaded arm64 STB: `4846bfc05ee2d3af9581c29ab2e0c8f4fe756b8de94452f43b03de83cdbf2d6d`.
- Runtime: .NET 10.0.12, macOS arm64, OpenGL 2.1 Metal 91.7.
- Source24 receipt: `f6062ca339581bbb4d246e44d5a6e052c553939fb228d35eb2d4b8f7182d250d`.

The durable metadata-only receipt, logs, prior failure evidence, input manifests
and all 24 capture hashes are under
`artifacts/rmlui-checkpoint-rml65-candidate-20261017/character-source-rig-gpu-run/`.
No source assets or PNG payloads were copied into that evidence directory.
Before/after verification covered all 317 original client companions, 322 private
entry/output files, original source/native pack witnesses and prior failures;
all were unchanged. The window closed and its exclusive lease was released.

Real imported source media, complete clips/state mapping, first-person cannon,
alternate form, gameplay, network/replay identity and device performance remain
**not verified**. Synthetic GPU and ordinary native-suite success does not close
those gates. Copyrighted source and derived assets remain private/unbundled.
