# Independent Scout Drone source bundles

Merged in [PR966](https://github.com/AntiNotAnti/Project-Prime/pull/966) at
`5927946086ea4dd0a97b0a935608c487bfc5cda7`; the complete merge tree equals
qualified head `855264b1791bb06d763d2ee953ad994420bdccf2`.

Alpha and Gamma Scout Drone authoring now use independent private v1 bundles:
`trooper-drone-alpha` and `trooper-drone-gamma`. Each captures original whole
geometry, all33 source joints, raw materials/textures and a strict animation
bank through the existing transactional importer. Existing four Trooper v6
recipes and all52 component files remain identical. This slice does not load
the new manifests into a renderer or activate imported drones in practice.
Native Psycho Bit practice presentation remains the fallback.

## Source and components

Pinned MIT donor `41452f2d053fcfa679e33c00a73d120a53f59e61`, recipes
`bodies/MP1AlphaDrone.json` and `MP1GammaDrone.json`, supplies source identity
and intended future presentation. Existing LICENSE/attribution is retained.
The owned GM8E01 disc0 revision2 uses Metroid3 SHA1
`30953a16e8820f7d1a5425084e04541a0439617a` and AudioGrp
`157e0841155cc7b699176dd7afc746c7048dbc28`. The donor's original Metroid3
hash differs; original-edition drone requests refuse.

The new bundles hash only their own two archive requirements. Global Prime
requirements and all old bundle fingerprints stay unchanged. Group verification
uses the union, while each source's resource delegate reads only its own
verified subset. Duplicate resource IDs in unrelated PAKs cannot change an
existing body or substitute a drone source.

Shared ANCS is `5e058399`; CINF `c3af0235`. AlphaDrone uses CMDL `ef920604` /
CSKR `84bda9d2`; Gamma_Drone uses CMDL `0b6586cb` / CSKR `604a291d`.
Each retains1,663 positions and1,390 complete triangles in970/420-triangle
material groups. Bones3–35 retain root3/external-parent2 and Skeleton_Root4;
no joint is synthesized, truncated or relabeled as the Trooper35 profile.
All4,170 corners match independently decoded positions, normals and UVs.

Each bundle has12 required files: eleven `id.drone` components (OBJ, skeleton,
weights, sparse motion inventory, raw CINF, animation bank, source GLB,
provenance, graph descriptor, raw ANCS and material archive), plus its own
`characters.json`. The manifest has exactly one Weavel AlternateForm entry,
`trooper` / `MP1AlphaDrone` or `MP1GammaDrone`, with its own geometry/provenance/
bank hashes and empty native bone map. It has no body/cannon aliases.
Raw material profiles9/10 append to unchanged profiles1–8 and archive format1.
Both original programs are opaque and compile in the CPU GX compiler; this
does not qualify GPU rendering or lighting fidelity.

The banks preserve100 original graph roots,88 strict roots,85 declared roots
and87 unique resources. All12 donor-required clips decode, including380
required key/off-grid samples per appearance. Raw animation/root motion remains
original: donor root pinning and a fixed Ready pivot are still required before
runtime flight. Provenance explicitly records source authoring and unqualified
runtime animation/gameplay; a successful import cannot self-grant an actor.

## Edition-aware group ownership

The original public five-parameter `ExportFromOwnedGame` signature stays intact.
A separate `ExportSourceGroupFromOwnedGame` enters the same private transaction
code with explicit group intent. The pure edition selection policy returns four
Trooper bodies for original Prime and six sources for Rev2. An explicitly
requested unsupported drone refuses; group mode requires the complete same-game
ID set. Echoes group behavior remains two appearances.

Both Settings paths capture group intent before asynchronous source selection.
The native controller detaches IDs/flag into its worker and clears pending state
on return/cancellation. The existing conditional Settings caller forwards its
group flag through `SourceCharacterImportWork`; no provider/device work or
Android qualification is claimed. Help text now describes supported appearances
on the selected disc. Import/check/update/remove remain per-bundle operations;
failed/cancelled preparation preserves working generation pointers and bytes.

## Windows qualification

Authoring production freeze `7d578eb1ca25b574d260497e307228e306265902`;
final source `b705dff34` additionally corrects conditional group help text.
SDK10.0.401, Windows x64. Final binaries (SHA256):

| Binary | SHA256 |
|---|---|
| Desktop core | `83fe6cfe446e4d0978f737fb7425b89dd7a0cb563e080760d70f8b16d3ed4b95` |
| Dedicated core | `c37ab6255f64e84d3f74f7168c436ef9413aec8de4e8c07fa8fabdc36b98564b` |
| External source tool | `e1ff8884745ab1d828d4ff654388b0bb5fd5b8cd041e3253f0255337d8842898` |
| Nettest | `0a7ea9e90cfad5dc6e076272b1080814a0e6142c36db5522bf0027d8332bb841` |

Tests first refuse missing drone IDs on frozen `e0a47b84…b4b2f` before asset
I/O. A separate114-check baseline freezes all52 old Trooper hashes and four
v6 recipe/fingerprint witnesses, then deletes exports. The final owned probe
passes45,898 checks and40 refusals. It independently verifies raw geometry,
full hierarchy/weights/inverse binds, original material/TXTR/decoded pixels,
strict banks, exact manifests/provenance, pair/single/mixed-six equality,
corruption, wrong source identities, generation locks and cancellation.
Captured sampling/texture decoding continues with all12 files exclusively
locked; no `Pack.Load` or GPU path is invoked.

Final Prime Rev2 recipe checks pass199; desktop/dedicated source-body checks
pass2,708/2,690; replay-format3,107, historical replay694, offline policy971
and architecture checks pass. Desktop/dedicated Release builds retain the
existing unused readiness-fixture field warning. Original-edition selection is
tested through the pure policy only; no original-edition disc was opened.

An initial test compile attempted to write a readonly decoded pixel view; it
now mutates a detached copy. Another initial test deferred component hashing
until after temporary cleanup; final metadata is captured eagerly. Retained
failed logs are superseded by complete final runs. Earlier authoring core
`53e618c5…cf36f` also passes; all24 generated drone files match the final core.
No stale or failed invocation is counted as a final pass.

Final metadata receipt SHA256:
`0298396dc797f143747392aa800933e07175ca7a94a2c01c46e763c9be97141c`.
Raw owned log `374505d9fd6cc72ca67833e9826d054647c3f8a6567b539eb6df429740e30bc8`;
complete24-component metadata
`455428b1c9400affb2ea855e2824996ebca9c0d4c2171c53d73def9861408e00`.
All receipts remain ignored under `artifacts/sh-integration/source-drone-*`.
The owned RVZ remains
`3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844`;
all temporary derived imports are deleted and native RNG remains unchanged.
No proprietary payload, screenshot or source transfer is included.

Commands use the named SDK and reference-disabled tool build:

```text
dotnet tools/external-import-check/bin/Release/net10.0/external-import-check.dll --probe-character-drone-source <owned-Prime-USA-Rev2-RVZ>
dotnet tools/external-import-check/bin/Release/net10.0/external-import-check.dll --prime-rev2-check
```

## Next runtime work

Closed generated profile admission, a separate drone selection/owner bound to
the initialized practice drone, full33-joint root-pinned Ready flight and actual
Windows Scene/GX/native gameplay parity are still required. Body/cannon/ball
and practice ability admission fences remain intact. Burst/laser/hit source
animation, Gamma runtime, mixed seats, online/replay and full imported character
support remain engineering. The user requests Windows and defers Android tests;
no other platform or physical-control acceptance is claimed.
