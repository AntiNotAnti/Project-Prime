# RML-83 lobby restoration and original menu trial

Date: October 18, 2026.

User scope: RmlUi exclusively. The latest request prioritizes lobby rail clearance,
missing map imagery, empty-slot Add Bot controls, and an original MPH Samus/menu-color
trial. Separate networking, world presentation/performance, character and arena
acceptance remain paused. This does not mark the full RmlUi migration complete.

## Implementation

- Connected Hunters and chat use the left rail's existing outer widths and margins
  at the same breakpoints, including border and padding.
- Eligible empty display rows expose Add Bot. The controller rereads lobby/roster
  ownership, starting/pending state and capacity and submits the existing revisioned
  AddBot request. The server chooses the actual network slot. No bot is invented
  locally. A now-occupied row resolves its current occupant through existing admin.
- Missing room previews request only the selected room through ThumbnailHost and
  the existing serialized renderer worker. Worker setup/cwd identify the configured
  user-data root. The existing PNG-to-TGA cache retains source-change/lifetime fences.
- Actual renderer output exposed a second defect: ReFuel requests RGBA but its
  Format/AsSpan length retain the source PNG's RGB channel count. The cache now reads
  the bounded requested RGBA surface. Original failure and an RGB regression remain
  recorded; RGBA alpha/orientation checks remain required.
- The backdrop decodes the user's `sourceimages/bg/main1.bin` and `main2.bin` into
  one 256x384 texture. It uses the original RGB555 keys (0,31,0), (4,16,16),
  (19,9,6), 170 original menu ticks per key, and a 21/32 tint overlay. The existing
  presentation clock owns the cycle; Reduce Motion freezes it. Original assets are
  read at runtime and are excluded from packages. The procedural backdrop remains
  the missing-art fallback. Resource release uses the existing compositor owner.
- The lobby uses warm yellow/orange controls over the rotating backdrop, retaining
  team, ready and destructive-action colors.
- Shared HUNTERS/Studio header routes and Theatre import-path action are restored,
  preserving dirty-draft guards and existing replay ownership.

## Qualification

Bounded lobby trial qualified; full migration remains open. Artifacts:
`artifacts/rmlui-lobby-restoration-rml83-20261018/`.
The earlier RML-82 frozen five CPU tools and actual Hunter GL suite pass; the
transfers GL fixture's fixed-footer scroll assumption was corrected after all
three original failures were preserved. Final scoped acceptance and package
receipts are recorded below.


## Qualified result

The delivered Mac ARM trial is
`/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml83-20261018.zip`
(85,163,721 bytes; SHA-256
`edfe7b4c8c3258b95e28708137efce0243d7e96a78bbf7c9b6fd94d96fdd615c`).
Main assembly: `9bfeace48b7c17f6ace27f3e27c1417413cd1547314868a035a577ffd58b22cc`.
Native bridge: `23a47a54b767341370d753ebde4b1e7e05aea5a9d9ab0f50939aa3c3de820d82`.

- Client, server and acceptance tools build with zero warnings/errors. All five CPU
  tools plus 37-line bitmap/tint acceptance pass. Add Bot has 51 explicit CPU
  assertions; authorities are synthetic, with no live server bot-creation claim.
- Actual existing RenderWindow, native input/admission/controller, real owned
  Hunter models/Samus art and real AD1 thumbnail pass at client 1024x720, 1280x720
  and 1512x820 (density 2). All six eligible Add Bot controls remain inside the
  roster, whose outer widths equal the left rail: 380, 432 and 476 physical pixels.
  Three runs provide 483 PASS lines, true exit 0 and unchanged frozen-runtime guards.
- Actual selected-room renderer produces the AD1 PNG. The final cache consumes its
  RGB pixels successfully. Native RGB/RGBA, alpha and orientation acceptance passes,
  retaining all bounded/cancelled/stale/cache-rejection checks.
- Community Transfers passes 339 checks at each of three native GL test sizes.
  A raw semantic snapshot proved that the unmarked detail paragraph disappeared
  from accessibility at larger sizes. The final asset-only closeout adds a status
  role and polite live semantics. Each final snapshot contains the exact detail
  node and complete install/recheck guidance. This one RML attribute change is the
  only production delta between build2 and build3; managed/native bytes are identical.
- Original owned assets were verified unchanged: 3,858 files, 840,039,391 bytes,
  matching hashes and original filesystem witness fields. Rendering/import-cache
  writes use independent private APFS clones; no game assets are packaged.
- Bundle version 83, bundled ARM64 .NET 10.0.12, all 26 expected ARM-capable Mach-O
  files and signatures pass (23 thin ARM64, three verified universal libraries).
  Independent ZIP extraction, exact signed inventory, startup, actual rendered
  launcher/resize and a real AD1 capture all pass using the extracted app. The
  trial is locally ad hoc signed, not notarized.

The broader actual replay workflow was also attempted. Its Home -> Hunters -> dirty
Studio refusal -> Discard -> License -> Studio -> Home route journey passed, followed
by real replay camera/seek/tool steps. That run closed unexpectedly during the
120-paused-swap timeline hold, after 87 completed steps; its remaining 62 steps are
INCOMPLETE, with the close trigger unestablished. No complete new replay-workflow
qualification is claimed. The original failure, observer/frame-handler cleanup, source-byte checks and
all prior RML-81 replay evidence remain preserved. This does not block
these bounded lobby fixes or mark the full migration/release complete.

Evidence: `qualification-summary/actual-lobby-qualification-v2.json`, final
`build-attempt2/acceptance/cpu-20261018T183226Z-e0c70fcf/`, actual map and native
pixel checks in build2, and `build-attempt3/acceptance/gl-transfers-closeout-20261018T184236Z-826bff32/`.
Delivery/signature/extraction receipts are in
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml83-20261018/`.


## Superseded visual experiment

The user subsequently requested removal of the Samus background/color trial.
[RML-84](rmlui-platform-layout-rml84-2026-10-18.md) restores the original chamber
and blue/cyan styling while preserving this trial's functional map/bot repairs.
Its Home follow-up adds three party platforms and a four-seat Current Session card.
