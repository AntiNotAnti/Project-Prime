# Private source texture banks

This is the next bounded SH-04 infrastructure slice after PR817. It does
not declare any of the six Echoes source-world material editions complete.
The old private map recipes and runtime appearance qualification stay intact.

## Implemented behavior

`EchoesSourceTextureBank` is a pure stream/byte owner. Version1 uses a
160-byte little-endian header, sorted112-byte entries and contiguous raw
top-level straight RGBA pixels. It binds the exact `G2ME01` revision0/disc0
Metroid6 archive identity, known arena MREA ID and supplied source digest,
and the decoder's `gx-expanded-cmpr-3-5-rgb5a3-v1` pixel contract.
The supplied MREA digest and known ID do not independently qualify retail
content; the importer/probe still owns source archive verification.

Limits are512 images,2MiB per decoded image and64MiB per component. The
writer admits the complete output before allocation; the reader validates
the directory, dimensions, original mip counts, exact ranges, ordering,
reserved fields and complete finite stream hash. Selected reads recheck
the captured header/directory and the selected pixel digest, without
rehashing unrelated pixels for each material. The caller must compare the
inspection's whole-bank hash with its expected generation hash.

Typed `MapMaterial.AlbedoBank` references carry bank and pixel SHA256,
resource ID and dimensions. A model metadata snapshot copies these values
and the private project root before worker decoding. It shares one lazy
bank inspection per path/hash within that snapshot, retains no stream or
pixel cache, and survives scene copies. Current global definitions cannot
retarget an older model or queued request to a newer bank generation.
Every worker open uses the existing private import ancestor/symlink guard.

Raw RGBA preparation feeds the existing material decode queue, quality
policy, Scene upload, residency, sampler and teardown owners. No encoded
image parser receives raw bank bytes. Explicit8x8 white native fallback
materials share one generated texture/palette pair while retaining distinct
authored material slots. Procedural fallback maps need no borrowed ROM
room, including Q3 texture-size calculation. Ordinary public procedural
white maps can still be packaged.

The initial source-bank material binding accepts opaque pixels only.
Bank storage remains lossless; nonopaque pixels fail the current binding
and retain the native fallback. Animation and packaged companion channels
are refused for this binding. Local overrides remain available, including
companion-only overrides that preserve the captured bank albedo. Promoted
raw albedo updates its flat/cel color average. It does not add a new Echoes
TEV, lightmap, fog, blend or alpha policy.

Bank references or declared bank assets force private-source handling even
with old optional provenance flags removed. Community package readers and
builders refuse them. Local project export rewrites typed bank paths;
reusable prefab export/insertion refuses bank materials before mutation.
The editor shows bank albedo read-only with a raw thumbnail and preserves
local override controls. New DTO defaults serialize away for old maps.

## Verification and boundaries

Final frozen-source results are recorded below. The
source-free suites cover format/allocation/stream bounds, corruption,
generation changes, detached references, worker preparation, alpha refusal,
private packaging and a real native white fallback model/package with an
intentionally nonexistent borrowed room. The existing material GPU harness
uses its one context and Scene to check independent bindings, exact pixels,
queued decoding, stale work retirement, quality changes and teardown.

`external-import-check --probe-texture-banks OWNED_RVZ` extends the complete
390-entry independent oracle with six in-memory bank encode/inspect/read
round trips. It writes only JSON counts/identities/hashes, creates no bank
or image files, and hashes the read-only source before and after.

This infrastructure is not yet connected to a new complete-arena import
recipe. Full-precision paired UV streams, base/lightmap shader semantics,
unsupported material fallbacks, source world mechanics, playtests, physical
Android/Windows execution and controlled performance acceptance remain.
No online content admission or protocol change is introduced. Roll back
the authored bank material to its white fallback by disabling existing
texture replacements; old import recipes and ordinary map materials keep
their established paths.

## Frozen acceptance result

Frozen source and tests: `e1bbd3c2043b28170880c1595882d0f87182e0d3`; tree `c16d4ddeb889d9dd4945138d144c169323b715bb`. The final evidence-only documentation commit changes no production/test bytes. All17 serial commands passed from two exact clean archives, with5884 tracked files unchanged in each. Each command used isolated user data and temporary storage; all contexts were removed. Windows SSH authentication still failed, so the user-authorized Mac fallback was used.

- Desktop and dedicated Release:0 warnings,0 errors. Android Release:119 AOT assemblies,25 existing XML documentation warnings,0 errors; signed APK verifies v2/v3 and contains arm64-v8a/x86_64 ProjectPrime AOT libraries. No physical device acceptance.
- Source-free tests:137 bank format,128 material boundaries,191 TXTR and486 map editor checks. Material tool:58 assertions; companion allocation failure coverage retires38/38 handles. Replay checks:500 protocol58,309 Quake compatibility,84 pickup,51 grant; campaign detail91. Bank aggregate overflow refuses before output allocation with10,968 measured managed bytes, below the1MiB allowance.
- Actual Mac OpenGL checks use the existing context/Scene. Two independent bank bindings have exact expected2×1 RGBA bytes; queued decode, stale revision retirement, immutable DTO/root capture, quality refresh, replacement toggles, source flat-color means and complete texture teardown pass. Stable handles are deliberately retained after disabling replacements, each containing its native white fallback. The Apple driver emitted an unloadable-sampler warning during an existing editor draw; GL error checks, bank readbacks and final resource checks passed. This is synthetic upload/lifetime evidence, not actual Echoes material rendering acceptance.
- All390 owned texture entries/385 unique resources match the independent26,221,056-pixel oracle. Six in-memory banks round-trip all pixels (104928864 total bytes including headers/directories); the largest is28,930,016 bytes. The original RVZ SHA256 remains `ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`. No bank/image/source payload files or probe temporary files were written.

Binary SHA256 values:

| Binary | SHA256 |
| --- | --- |
| desktop | `96d576518b8752a05bb1c2c9abf1d83beea88c4ac6b6246492c6d238b861224b` |
| dedicated | `6371a98aa4890988ff8c7c22370f2cfdf67504294e631edf40db4f55b5bbf488` |
| android_signed_apk | `01f9428b0338a61a31c0647f474870a1f6158d261294d21671daaa28da79da01` |

Ignored receipts: `artifacts/sh-integration/banks-e1bbd3c20/summary.json`, exact commands/logs/source manifests, bank metadata and source hashes. Exploratory predecessor failures remain recorded: loaded-image reference equality, an omitted optional reflection argument, and the incorrect toggle-test expectation that stable authored handles should merge. Those fixtures are corrected in the frozen source; there is no green-CI or external-review claim.
