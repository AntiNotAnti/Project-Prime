# Prime Rev 2 Trooper material authoring

Updating a verified Prime USA Rev 2 Trooper import now retains its complete
original material set and every referenced TXTR alongside its existing rig,
animation bank and geometry. All four appearances use bundle v4/body v3 with
eleven required components. The previous ten generated files remain byte-for-byte
identical. Old v2/v3 receipts inspect as Outdated until an explicit update;
inspection never rewrites them. Original Prime recipe v1 and Echoes recipe v6
remain unchanged.

This completes Trooper raw material/texture authoring transport. It does not
enable Trooper source rendering, a playable imported actor, first-person gun,
Scout Drone geometry, multiplayer or replay support. Runtime preview selection
still refuses these four profiles before changing native resource caches.
The existing provenance's unqualified source-material field continues to
describe rendering qualification, rather than the presence of raw transport.

## Existing owners and bounds

The existing PPCM v1 codec gains four closed Prime profiles (IDs3–6), each
bound to its canonical appearance, exact CMDL/CSKR/CINF/ANCS tuple and pinned
Rev 2 source fingerprint `c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0`.
The profile selects GM8E01/CMDL2/Prime material syntax; Echoes retains
G2ME01/CMDL4/Echoes syntax. Header, source-game, resource or dialect mismatches
fail closed. Original material bytes, opaque tails, local slots, raw TXTR mip
bytes and decoded top-image hashes are retained without rewriting selectors.

The codec's existing byte, count, source/mip extent and aggregate decoded-image
bounds remain in force. Input capture, detached parser copies and cancellation
are preserved. The archive is verified before joining the existing importer
transaction; it adds no second importer, renderer, simulation clock or wire
field. Protocol80 is inherited from the baseline main.

Each owned appearance has three material programs and seven referenced TXTRs;
there are23 unique TXTRs across the four. All use the existing CMPR decoder,
at most128×128 and five mips. Every model retains1970 source triangles,
5910 corners,1936 positions,35 source joints and142 ANIM leaves.

| Appearance | Archive bytes | Archive SHA256 |
| --- | ---: | --- |
| Power | 59172 | `50f5acc05dc966847c96f05e9bafa045777510e77d6e3a3f224227020a9c553a` |
| Ice | 42788 | `16f6f537954145940030874afb6d8dae3187de08e4f5997568085c3ad96701f0` |
| Wave | 42788 | `d84ae5a8ecfe71703020dfc0348fcac23713726e5af03c9fc26942654c1d2c6b` |
| Plasma | 42788 | `0eb01263a7dad10519af2c70e3158fd623b3a56bf18251c625e7761e651a5933` |

## Frozen Windows verification

Production freeze `76f9e05de37893216a3d17bd943ec5f58ad503e1` is based on merged
cannon main `67fc370548c7fec4b59327e37208bedd1f07fcac`. Test freeze is
`f273fca7f085081bb44a84e57aa108d813e509eb`. Windows desktop and x64 dedicated
Release pass with one existing CS0649 warning and zero errors each.

- Desktop core SHA256: `dd30361bbbd4a57159086c69cbf8ba4d4f1601a03dbf6b54b662e95875b53388`.
- Dedicated core SHA256: `825a42c3e19781a698457e3ddcc7a6982832a7c392a7c6c3d1f1a6bab614dd82`.
- External-import tool SHA256: `dfc7264060214583e6c3ec37452f2d7593bb830846c1d2370a5c794d6c1e9f80`.
- Character-preview tool SHA256: `d9f88271d393aabd16382bd8791c5e6db6ae179eeccb0f1ebbe8d214ce93dd61`.

The actual owned Prime Rev 2 probe passes182,729 assertions. It runs the frozen
pre-material exporter in an isolated assembly context to create actual old v3
generations, cancels an update at BeforeCommit, verifies rollback, then upgrades
all four using the current default importer. All40 prior component files compare
byte-for-byte and by hash. Every raw material record/set tail and complete TXTR
matches the independently pinned source inventory. Repeating reuses the same
generation; pre-cancel preserves receipts; removal withdraws discovery and
retains immutable generations until the probe cleans its disposable root.
The source RVZ SHA256 remains
`3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844`.
All temporary derived payloads are deleted, with no runtime or GPU activation.

The full external-import synthetic suite passes, including151 recipe/migration,
997 RVZ,191 TXTR,137 texture-bank,122 material-snapshot,952 material-boundary
and2126 geometry-transport checks. The character-preview suite passes501 raw
archive,421 source GLB,966 Trooper GLB,124 GX and50 generated-cannon checks,
alongside existing bundle/OBJ contracts. Missing/corrupt materials fail current
readiness while verified sibling roles retain their individual state.
The invented Echoes archive matches the independently retained old binary's
1035-byte SHA256 `3fb3aae28302ad02d03cc84f050cc742c51bc4866877347d47fa7a94c2e7f937`.
Windows lacks file-symlink privilege1314 for one negative fixture; actual
directory-junction ancestry checks run. This is not file-symlink coverage.

The actual Echoes import regression also passes update/reuse/cancel/remove,
exact original archive hashes (normal `6a5666bf…be86d7`, Final
`69d27a8e…04df8d`), full material/TXTR pixel-oracle checks, temporary cleanup
and unchanged source hash. Existing body941, physical-root24 and source-rig910
CPU checks pass. No Android build, device/provider execution or new GPU
qualification was performed. The first test-tool compile found a span/array
helper mismatch; the corrected test helper builds with zero warnings/errors.
Production source did not change after its freeze.

Local metadata-only receipts/logs are under `artifacts/sh-integration/`:
`trooper-material-owned.json`, `trooper-material-external-synthetic.log`,
`trooper-material-synthetic.log`, `trooper-material-echoes-regression.log`,
`trooper-material-inventory/` and `material-synthetic-baseline/receipt.json`.
They are ignored and contain no extracted payloads or captures. The probe
uses `external-import-check --probe-prime-rev2 <owned-rev2.rvz>` with
`PRIME_REV2_BASELINE_CORE` pointing at the separately retained pre-material
`ProjectPrime.dll` SHA256
`0599a74b5d56d8fe2aa04194107dcc38224e5bfe3d5db4590f0851dd812adfe7`.
That exact prior binary is required only for this actual migration comparison;
the synthetic recipe/dialect suites need no baseline binary or game media.

## Remaining character engineering

Read-only inventory shows that used UV1/UV2 equal UV0 bit-for-bit across all
23,640 corners, while UV3 is absent. That is evidence about these exact source
profiles, not generic multi-UV support. Every material1 needs packed post
selector3 (slot1), and Power additionally reads C1/register1. Those shader
semantics, the explicit external register value, exact generated-profile
admission and actual Windows source-program pictures still need implementation
and qualification. Imported Trooper poses, Hunters selection and match actors
then need the existing source-owner/lifecycle path; current native-fallback
practice mechanics do not establish any of those rendering capabilities.
