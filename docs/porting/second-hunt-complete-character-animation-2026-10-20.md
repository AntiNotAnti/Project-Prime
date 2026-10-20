# Complete source animation handoff for character imports

The two Echoes Dark Samus imports now retain complete original ANIM resources,
including every encoded temporal key and scale channel, in each part's immutable
bundle generation. The existing private source-rig clip owner can consume the
normal Dark Samus bank directly. This closes the previous gap between the
Settings importer's sparse12-clip/five-pose diagnostics and the runtime's separate
manually authored full-key JSON inputs.

This is an animation data and private playback increment. It does not complete
playable Dark Samus, Overcharged, Trooper abilities, public selection, multiplayer,
replay identity, full source materials, first-person cannon or morph transitions.
The existing seven Hunter values, protocol70 and historical replay formats are
unchanged. Extra gameplay remains disabled. Work on original ship presentation
is preserved separately and is outside this change.

## Import and compatibility

Update selected or Update all through the existing character authoring cards.
Echoes recipes advance from2 to3; body and ball advance from part1 to2. Each
part keeps its four existing geometry/rig/weights/sparse-motion components and
adds raw `.cinf.bin` and `.animations.bin` components, both version1. The shared
parts index remains schema1 and explicitly says `RuntimeInstall=false`.
Thirteen required components and four graph files form one17-file generation.
Old sparse-only receipts show Outdated; missing either new component prevents
Ready. Normal and Overcharged retain distinct namespaces and bank identities.
The four Prime Trooper recipe1 hashes and required inventories remain unchanged.

Publication still belongs to `LocalImportBundle`: pinned archive verification,
worker scheduling, cross-process writer lease, cancellation, atomic receipt
replacement, retained old generations, repair/reuse, sequential partial-batch
outcomes and Remove. A failed animation decode never replaces the installed
working generation. Source files and generated media stay local.

`PPANIM1` banks use one bounded component per part, preserving the existing
64-component limit. They bind the exact ANCS descriptor/source bytes, canonical
appearance/part/native-character identity, raw CINF hash, original root ordinals
and sorted unique resource IDs. Every raw resource retains its exact length and
SHA256. Metadata exposes character-declared roots separately from all shared-set
roots. Blend/sequence/random roots remain graphs even when all leaves share one
resource; nonzero/unknown play-time semantics remain unsupported.

## Decoder and playback ownership

`Anim.LoadBounded` preflights a private source snapshot before the existing
Retro decoder allocates or loops:16MiB/resource,20,000keys,256channels,
4,000,000decoded scalars, encoded flag/delta/table extents and signed bit widths.
Finite timing, channel values, complete key counts and source quaternion
quantization are checked. Archive admission additionally bounds the aggregate
at64MiB,32million scalars and64million decode-work units; empty channels cannot
escape the work bound. Sampling clamps the floating frame position before its
integer conversion, including tiny positive ticks.

Captured clips and poses are detached and read-only. Full scale is retained and
sampled; the existing rigid source-rig adapter refuses nonidentity scale. No
source IO, rehash, clock or simulation mutation occurs during pose sampling.
Root motion is retained as source pose data; it is not applied to player motion.
No animation graph, gameplay state mapping or alternate renderer is introduced.

An existing private normal Dark Samus `SourceRigPreview` Biped entry may set:

```json
"sourceAnimationArchive": "darksamus-echoes.animations.bin",
"sourceAnimationArchiveSha256": "42a042f58ef2ff6395607fce60c458e54c7b10ad90c13142fbc0d143019471f1"
```

The same contained private pack must include this bank and its imported
`darksamus-echoes.ancs-graph.json`, `.ancs.bin` and `.cinf.bin` siblings. The
normal source-rig GLB and existing visual-only provenance remain mandatory.
Archive mode and the legacy graph-JSON mode are exclusive. Existing legacy
full-key and fixed cannon paths retain their original qualification.

The new owner pins both the verified whole bank and raw CINF
`3a8d0a84695906ac06fee59478fac134623a4a885cb3b1a1e6e144c4670b3de4`;
caller-rehashed edited animation or skeleton data cannot impersonate the
original source. Geometry remains under the existing private GLB/provenance
contract. Every one of the39 source bones, names, parents and bind transforms
must match the loaded40-node hierarchy, including the identity mesh. There is
no39-to32joint truncation: the existing weighted loader partitions draw palettes.
Only character-declared strict zero-time primitive roots enter this adapter.

Pack/asset/part/generation and archive hash participate in ownership/cache
identity. Strong proof at load/decode/publication revalidates all four inputs;
explicit refresh retires stale generations. Cached sampling uses only the cheap
current-generation fence. Removal or a missing file cannot silently redirect an
already captured clip; explicit strong revalidation retires it. Final, ball,
cannon, replica, network and replay capabilities are not broadened.

## Source evidence and limits

The verified owned G2ME01 RVZ hash is
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
Exploratory source checks found66resources/3,918keys in each body bank, with76
shared roots (68strict and8unsupported); the normal character declares55roots
and Battle_3 declares66. Each ball has2resources/27keys. Normal bank474,077bytes;
Overcharged474,076bytes; the two ball banks1,110 and1,122bytes. The bank differences
retain appearance identity despite shared source resources.

Independent historical-decoder comparisons checked every key-time and three
additional samples per resource, including scale:626,528 scalar comparisons per
body and462 per ball. Actual source update/repair/reuse/cancellation/removal
passed and the full RVZ hash stayed unchanged; temporary derived media was
deleted. Only IDs, counts and hashes were retained.

Frozen production/test commit `2c79267927a3c4d094a6586186f4ba56b2c09284`
passed all37 serial qualification commands. Two independent git archives retained
all6,068 tracked files unchanged; archive SHA256
`152b9ba9ebc0ee0d88159f8aa0cef3dd91208a4e49de5733b596c50f8c4c768c`.
Desktop and dedicated Release builds report zero warnings/errors. Android Release
has25 existing documentation warnings and zero errors; both ARM64 and x86_64
contain119 AOT assemblies, and the signed APK verifies v2/v3 signatures.

The strict animation suite passes977 bounded-decoder checks and177 archive checks;
48 Python authoring tests pass. Existing import, native Settings, game-files,
settings-archive, ship, credits and historical replay suites pass. The existing
source-rig synthetic loader/palette suite passes910 checks. Cached native source
closure and all three native runtime manifests verify unchanged.

The owned runtime test passes281 checks through the actual pack, lease, weighted
loader and `CharacterSourceClip`: all55 character-declared strict rigid roots,
2,979 key poses and165 additional poses evaluate the complete39-joint palette.
It uses our own single triangle with the original full source hierarchy. Sampling
performs no clip file reads; captured samples remain usable without source files
until explicit strong revalidation retires the lease. Fresh generations reproduce
poses and cannot revive old clips. Caller-rehashed edited animation/CINF, altered
bind hierarchy, undeclared/composite roots and Final identity are refused. Global
source caches, binaries and the owned RVZ remain unchanged; derived data is deleted.
All16 legacy geometry/skeleton/weights/sparse-motion hashes match the independent
`a0c24687a` source evidence.

Frozen desktop DLL SHA256:
`3ea6e5acbbaa691b10b48df75a23c45458c6bcc07d9412f95145b6ace915486d`.
Dedicated DLL:
`8a22a8be10b615a4a4f38ba32b32e6aac4d7eba72e481fe1953fafe7edc5203e`.
Signed Android APK:
`552262cb41ec1aa1eb435b9930c6035e40f7fc2b1bee0fc4e46410bc726ce2a0`.
Local receipts/logs are under ignored
`artifacts/sh-integration/character-animation-final-2c7926792`.
An initial test fixture needed an unsigned integer literal; another omitted the
expected missing-source IOException. Their failed exploratory logs are retained;
the final frozen suites pass. No CI or external review success is claimed.
Windows SSH authentication currently fails; user-authorized Mac fallback is used.
Android compilation does not establish storage-URI, device, crash or runtime
character acceptance. A CPU source-rig test with our own triangle does not prove
complete character rendering or playability.

The implementation reuses Project's existing ported Retro decoder, bundle,
source-rig, lease and palette owners. Second Hunt reference
`41452f2d053fcfa679e33c00a73d120a53f59e61`, particularly
`Import/Bodies/BodyImporter.cs`, `Campaign/NativeBody.cs`, `DarkSamusBody.cs` and
`BodyArmCannon.cs`, supplies the source behavior reference. Its renderer and
network authority are not transplanted.

PR850 merged at `a9f4bbcd404c232f62a46bdc154afe3fdc12769a`. Concurrent
CW-05k/l/m changes are outside the frozen2c7926792 whole-binary proof; the
animation/importer/character ownership closure is unchanged at merge.

Focused postmerge checks at a9f4bbcd4 also passed: desktop Release, both
animation/import tools and defaults, and the owned281-check runtime probe.
The subsequent [generated source preview](second-hunt-generated-character-preview-2026-10-20.md)
adds importer geometry and an actual diagnostic viewer without claiming
complete character gameplay.
