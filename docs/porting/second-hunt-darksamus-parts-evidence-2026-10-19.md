# Dark Samus complete body and ball authoring bundles

Status: local qualification passed for [PR815](https://github.com/AntiNotAnti/Project-Prime/pull/815).
Frozen production/test commit: `a0c24687a5313c7636ad198b262f20d019681f4c`.
Parent: `38dc5b104dd8048c7662597c8b7ced9ee01a70ac`; current protocol65,
unchanged by this slice. Historical replay readers and seven native Hunter
identities are unchanged. Reference remains
`41452f2d053fcfa679e33c00a73d120a53f59e61`.

## Implemented

The existing private exporter and appearance cards own the entire update.
Normal Dark Samus and Overcharged now use recipe v2 with body and ball parts,
each at part version1 and geometry/rig/weights/motion-samples version1.
The nine required files are four legacy body sidecars, four `.ball.*`
sidecars and `<appearance>.parts.json`. Each part independently captures its
optional graph descriptor/raw ANCS pair. The witnessed Echoes source yields
thirteen files in a single immutable generation per appearance.

The parts index binds canonical parent ID, source game, part/native resource
references and component versions. `RuntimeInstall` is false. Source edition
fingerprints, receipt schema1, permanent writer leases, before-commit
cancellation, sequential batch outcomes, atomic current-pointer replacement,
retained old generations and Remove ownership remain with `LocalImportBundle`.
Identical updates reuse an already verified repaired generation even when its
original deterministic directory remains damaged. Staging, re-verification
and all cancellation boundaries still run before publication.
No body-only v2 generation can become Ready. The four Prime Trooper recipes
retain their v1 hashes and required inventory.

Inspection returns a detached read-only component status list. Missing files
are Incomplete; invalid bytes are Failed, which takes precedence over missing
files regardless of receipt order. Whole-bundle `Files` remains empty unless
every component verifies. Existing Check reports each logical part's version
and readiness, with the version/state of unavailable components. Independently
verified body files remain visible as body Ready when ball geometry is missing;
the aggregate still reports Incomplete. Missing shared index or bad optional
proof also prevents aggregate readiness. Old v1 receipts report Outdated.

Body artifact filenames and serialized graph shape remain compatible. Ball
proofs retain the same canonical appearance ID with explicit `sourcePart:ball`
and a `.ball.ancs.bin` snapshot reference. Verification regenerates the exact
descriptor from bound source bytes; unsupported parts and filename/identity
tampering fail closed.

Python `build_glb.py` and `source_palette.py` accept `--part body|ball`, with
body as the default. Only the two Echoes appearances allow ball; no missing
ball input falls back to a body sibling. Skeleton parent ID and explicit ball
tag must agree. Ball palette plans use the separate authoring-only
`private-source-part-rig-palette-preflight` kind; existing runtime biped palette
and retarget identities are not extended. Overcharged is accepted by authoring
tools while existing runtime `MP2DarkSamusFinal` rejection remains intact.
The witnessed Echoes CINF root sentinel0x61 is admitted only for root bone0
named `root`, canonical appearance/game/native-part identity and the exact
body/ball CINF resource. Serialized parent IDs are preserved. Other missing
parents remain errors; no generic rig repair or retargeting is performed.

## Local authoring use

Use existing Settings / Game Files / Private character authoring previews /
Update selected or Update all from source. Check verifies the complete receipt;
Remove withdraws its pointer while retaining immutable files for open consumers.
Point the tool at the generation folder returned by the exporter, for example:

```sh
python3 tools/retro-character-preview/build_glb.py --root LOCAL_GENERATION --character darksamus-echoes --part ball --validate
python3 tools/retro-character-preview/build_glb.py --root LOCAL_GENERATION --character darksamus-overcharged-echoes --part ball --palette-plan LOCAL_PRIVATE_PLAN.json
```

Both commands are structural authoring checks. They do not install gameplay
content. All inputs and outputs stay local; never package or upload source
models, snapshots, textures or derived previews.

## Verification

Initial `f8351a88d` passed source-free import/graph/native settings/replay checks
but failed the owned-source repeat-after-repair assertion: the publisher kept
creating fresh repair generations after finding the retained damaged original.
Its Python owned-source handoff also rejected the actual root sentinel.
These failures are retained in `artifacts/sh-integration/parts-f8351a88d`.
The earlier desktop/dedicated builds passed, but its Android run was canceled
after these issues were found and is not qualification. All twelve legacy
body-file hashes matched the earlier exporter even in this failed run.

The fixes and regression fixtures are in `a0c24687a`. Two fresh exact git
archives contain5865 tracked files each; no mutable worktree overlays are used.
Archive SHA256: `191a9172025f0614c7f524bb991bb40cdcd4d66f281b2c7f9ca7b33964bed8f8`.
Desktop and dedicated Release builds pass with zero warnings/errors. Android
Release/AOT passes with25 existing XML documentation warnings and zero errors;
all119 assemblies are AOT compiled, and the signed APK verifies v2/v3 signatures
and contains ARM64 and x86_64 ProjectPrime AOT libraries. All24 serial commands
and the separate Python suite pass. Both archives'5865 tracked files remain
unchanged after qualification. Command lines, isolated environments, exit codes
and timestamps are retained in the ignored results receipt; no CI success or
external review is claimed.

| Frozen successor check | Result |
| --- | --- |
| Python unittest discovery | 48 passed, including qualified sentinel and negative mutations |
| `external-import-check` | Full source-free import/receipt/component/repair/coalescing/cancel suite passed, including RVZ and197 CPP09 checks |
| `retro-character-preview-check` | Canonical part/recipe/version/inspection contracts passed |
| `retro-animation-check` | Existing motion contracts, independent legacy descriptor bytes and part-proof tamper cases passed |
| `rmlui-live-menu-check` | Existing native owner suite and body/ball Check status/missing-file/repair fixture passed |
| `rmlui-game-files-check`, `settings-archive-check` | 18 and26 checks passed |
| `-charactermodelcheck` | 904 source-rig checks passed; no new runtime appearance admitted |
| Historical replay checks | Protocol archive457, Quake III304, pickup84 and grant51 checks passed |
| `--campaign-map-detail` | 91 detached source/load lifecycle checks passed |
| `--probe-bundle OWNED_ECHOES_RVZ` | Both complete appearances, missing-ball repair, reuse after repair, BeforeCommit and partial-batch cancellation, Remove/repeated Remove passed |

The actual source probe verifies13 files and9 required recipe components per
appearance. Normal body retains4245 positions/6340 triangles/39 bones;
Overcharged retains4849/12734/39. Each ball has1553 positions/2220 triangles/2
bones and two sparse sampled clips. Exact independent graph/raw bindings and
source references verify. Repaired generations retain all surviving old files;
repeat imports reuse the repaired paths and preserve the byte inventory.

An independent frozen-assembly comparison against the earlier `3e181df59`
exporter (DLL `c98919449c1a3dac61aad432da88f6bbd26cf81910e9112f7a66b67bdff8da28`)
confirms all twelve legacy body files are byte-identical: OBJ, skeleton, weights,
motions, graph and raw ANCS for both appearances. Frozen Python consumes all
four actual parts and builds plans: normal body1 palette batch, Overcharged2,
each ball1; every acceptance flag remains false and source parent97 is retained.
This is structural authoring acceptance, not a Blender/GLB bake or rendered rig
qualification.

Both owned probes reread the full RVZ SHA256 before/after and obtained
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
All derived media, temporary user data and export directories were deleted in
`finally`; only metadata and logs remain in ignored
`artifacts/sh-integration/parts-a0c24687a`. Desktop DLL SHA256 is
`2b99467837e8885adb749d6f2b69885f468e52422c9181e39d19c8aab4597f67`.
Dedicated DLL SHA256 is
`2852ddc4d0a1a6ba6991a8b65806e4f298cb58631feca24b33e5336e42e35377`;
signed APK SHA256 is
`8d91a7c9548c3403a857c81e8d870ce5686355104d6b2ec3e59ac914d4fff0e1`.

## Unproven, disabled and rollback

This slice does not qualify HD/core/vein appearance, source materials/textures,
first-person cannon, full-rate motion/state-graph evaluation, morph transitions,
abilities, network/replay use or playable extra Hunters. Source metadata and
sparse poses do not establish those behaviors. No Blender export or new GPU
rendering acceptance is claimed. Windows authentication is unavailable;
platform checks use the user-authorized Mac fallback. No physical Android
device is available, so Android compile/AOT is not device acceptance.

The feature is a user-invoked private authoring update. Remove retires its
active pointer without deleting retained generations or source media. Reverting
this slice restores the earlier body-only recipes; it does not mutate saves,
wire formats, map authority or the external source files. The full SH-03/SH-05
phases remain incomplete; see the integration status ledger and the initial
source evidence for the remaining source and runtime gates.
