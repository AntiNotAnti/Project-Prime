# SH-04A bounded source texture decoding

Status: final protocol-66 local qualification passed for [PR817](https://github.com/AntiNotAnti/Project-Prime/pull/817).
This is a source-conversion foundation, not completed map-material rendering.

Frozen final production/test commit: `32dffe17437145ce2f0804370b872a27e2d4f7d2`;
parent baseline `3933d9ea2a69e0295cd564c7de8d26907d36866d`, protocol 66.
This slice adds no wire change. Two exact git archives contain 5,877 tracked
files each, without overlays. Archive SHA256:
`86420387db0f7fe85a6eb6a74ad94ceee391920347a35d4d49cce940689273fd`.

## Implemented and reused owners

`RetroTextureSource` extends the existing Retro import namespace. It is pure
bounded conversion: no IO, source download, cache, GL, file publication,
material selection or alternate renderer. `Inspect` validates the complete
declared tiled mip chain, including lower levels that `DecodeTop` does not
return. Only format 10 CMPR and format 8 RGB5A3 are admitted; other formats
explicitly throw `NotSupportedException`.

The 12-byte big-endian header supplies format, dimensions and mip count. Every
tile/mip extent is checked before pixel allocation. Hard limits are 4096 per
dimension, 13 mips,64 MiB source and64 MiB top RGBA. Mip count cannot exceed the
dimensions' complete chain. A caller can lower the top-output budget; lower
encoded mips still must fit the source. Partial/tiny tiles are consumed in full
while writes are cropped to the actual image rectangle. Returned pixels are a
detached read-only span; returned mip metadata is a detached read-only list.

Sources must end exactly after all declared mips, or at the next 32-byte boundary
with a uniform FF suffix. The witnessed uncompressed PAK resources contain 20
such bytes. Zero, mixed, partial or arbitrary suffixes are refused. A top mip
that fits does not excuse a truncated lower mip.

The named pixel contract is `gx-expanded-cmpr-3-5-rgb5a3-v1`.

Pixels are top-first straight RGBA with replicated endpoint channels, integer
3:5 CMPR interpolation and averaged RGB for the transparent selector. These
choices are supported by the pinned primary decoder and its blend helper:
[Dolphin pixel dispatch](https://github.com/dolphin-emu/dolphin/blob/cf5cb7ef81c3e8e7af5c9d566334219ffda9f68b/Source/Core/VideoCommon/TextureDecoder_Common.cpp),
[integer blend facts](https://github.com/dolphin-emu/dolphin/blob/cf5cb7ef81c3e8e7af5c9d566334219ffda9f68b/Source/Core/VideoCommon/TextureDecoder_Util.h).
They differ from the frozen upstream preview's floor-scaled channels,
one-third interpolation and transparent black. Pixel comparison with that
preview alone would therefore be the wrong acceptance oracle.

The MIT tile-layout reference remains frozen Second Hunt
`41452f2d053fcfa679e33c00a73d120a53f59e61`,
[Retro/Txtr.cs](https://github.com/BountyHunterKanden/SecondHunt/blob/41452f2d053fcfa679e33c00a73d120a53f59e61/src/MphRecomp.Core/Import/Retro/Txtr.cs)
(SHA256 `4f652ef82cc4c5ece34493a1673c2e0b68351f2ac01a5ee6615d04528a367869`).
Its attribution is retained; the existing MIT license applies to code, not
commercial textures. The implementation uses independently expressed format
arithmetic; no GPL decoder implementation is transplanted. This specified
CPU contract is not proof of original physical GPU/game pixel fidelity.

## Verification

Initial commit `bb1f8be26` passed all 13 commands, Desktop/dedicated Release,
Android Release/AOT/signatures, source-free and full owned-source checks on
protocol 65. Main then merged concurrent custom-weapon PR816/protocol 66. The
slice was rebased, current-version prose updated, and final qualification is
repeated successfully on `32dffe174`. The decoder and all three external-import
test/probe files are byte-identical between these candidates; predecessor
proof remains separate from the final baseline.

Desktop Release builds with zero warnings/errors. The full source-free
`external-import-check` suite passes, including existing RVZ/CPP09 contracts
and 191 new TXTR checks. The new fixtures cover literal expected pixels,
all selectors/subblocks, reversed/equal endpoints, channel expansion, row and
tile order, cropped odd/non-square images, complete mip ranges, invalid formats,
source/output budgets, suffix corruption and read-only/detached results.
The warmed truncated 4096x4096 request allocates 856 bytes for metadata/error
handling, below the 1 MiB fixture allowance; it never allocates its64 MiB output.
Historical replay 500/309/84/51 and detached map 91 checks also pass.
All 13 final protocol-66 commands pass. Desktop and dedicated Release builds
have zero warnings/errors. Android Release/AOT compiles all 119 assemblies and
packages a signed APK with ARM64 and x86_64 ProjectPrime AOT libraries. APK v2/v3
signatures verify; Android has 25 existing XML documentation warnings and zero
errors. Both exact archives retain all 5,877 tracked files unchanged after
verification. No new GPU, physical-device or material-rendering acceptance is
claimed.

The explicit read-only command is:

```sh
dotnet run --project tools/external-import-check -c Release -- --probe-textures OWNED_ECHOES_RVZ
```

It verifies USA revision 0/disc 0 and pinned `Metroid6.pak` SHA1
`b8b92af37dc772ecd8234fb1ce664be0493188f6`, then admits every requested layout
before any pixel decode. The probe applies 2 MiB/image and 128 MiB total top-output
budgets. It sequentially decodes all 390 entries and compares every logical
pixel against a separate coordinate-addressing oracle with bit-pattern
expansion tables. The oracle shares no production tile loop, palette or
channel helper. Only JSON counts/layouts/hashes are emitted, never source or
pixel bytes. No source fixture is committed.

| Frozen owned-source witness | Result |
| --- | --- |
| Per-map entries / globally unique TXTR IDs | 390 / 385 |
| Formats | 387 CMPR, 3 RGB5A3 |
| Top RGBA byte total / compared pixels | 104884224 / 26221056 |
| Independent pixel oracle | Every pixel and every texture hash matches |
| Complete encoded mip ends | 373 exact compressed, 17 uncompressed with 20 FF alignment bytes |
| Largest top image | 1024x512, 2097152 bytes |
| Media/image writes | None; the isolated probe context contains zero files |

All six arena material sets pass; this does not inventory every actor, sky,
fluid or campaign image. Source hash before/after, independently checked by
both probe and outer wrapper, remains
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
The decoded arrays are not retained or exported. Ignored receipts under
`artifacts/sh-integration/texture-32dffe174` record exact commands, isolated
user-data/temp environment, binary hashes, timestamps and exit codes.
Final protocol-66 Desktop DLL SHA256:
`af44cacf173dbf6f7cf1dc90681125b8284ce35b6c31bddf0f101c9a44bee9ef`.
The final owned JSON metadata matches the predecessor byte-for-byte (SHA256
`301ef40c5f17c9830191c12bd446ccb963c03f34487d2cd78431e6e43ad364b6`).
Final dedicated DLL SHA256:
`34da3f7dcd32128c4a27122293d31efcde936fe7ad7ad29e3251fb70a2692431`.
Final signed APK SHA256:
`9833c4c1d1fc5fb7331e31afa927cf41ee9a6305b60900caa0a8a159cc1df192`.
Windows authentication remains unavailable; this is the user-authorized Mac
fallback. No CI or external-review success is claimed.

## Remaining engineering and rollback

There is no map/material activation in this slice and no new feature switch.
Source images are not published, packaged, uploaded or transferred. Historical
map identities, collision and private-source/online fences remain unchanged.
Removing the helper/diagnostic restores the earlier source-only capabilities
without changing saves, gameplay, protocol, imports or active scene state.

The source-material renderer still needs receipted bounded texture banks,
full-precision independent UV streams, exact common-material qualification,
a semantic lightmap shader channel in the existing shared renderer, explicit
C1/raster lighting inputs, native fallback policy and platform draw/lifetime
acceptance. Top-only decode does not qualify original mip/sampler behavior,
gamma policy, lighting, animated/translucent materials or GPU fidelity.
See the material gap memo and integration ledger; SH-04 remains incomplete.
