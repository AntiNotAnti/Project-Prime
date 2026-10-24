> R22 follow-up (2026-10-22): full color/alpha implementation is available as an explicit opt-in, preserving Original. See [implementation and Windows evidence](r22-implementation.md). Earlier NOT_IMPLEMENTED text below describes the preceding native qualification campaign.

# Renderer qualification resumed — 2026-10-22

Base: `46299080ba441970a6f32ce3acf74a8a65f1b7a6`.
Branch: `codex/renderer-qualification`, isolated managed worktree.
Original audit: `07f23c014a408d134d1cc67e2f1d7074fc7f7942`.
The [supplied plan](audit-plan.md) is preserved. The October21 implementation is
now tracked in current main following repository-history replacement. This
resume preserves newer network, map, HUD, launcher and movement work. Earlier
native observations belong to their own MVIDs; they are not final acceptance.

All execution in this campaign uses Windows. Android cross-builds are hosted on
Windows; no Android, Linux or macOS runtime test is claimed. No release/preset
promotion or publication is performed. Full plan acceptance remains pending:
AGX is UNRESOLVED_P0 and program reuse NOT_ELIGIBLE. R22 is now implemented in
the explicit, default-off LinearColor mode; its separate Windows correctness
evidence is linked above. Visual candidates retain Original/native fallbacks.

## Resume integration

- Initial current Release client/native harness build PASS, two existing
  warnings (CS0618 in HudStudioView; CS0649 in NetLobbyTest). This initial
  binary predates the resumed Parkour shadow and legacy-policy fixes.
- Linked Android pacing/source suite PASS 70; comparator self-test PASS.
- Expanded detached shadow fixture PASS 195 on the initial Release binary;
  the corrected Parkour parity witness reproduces a real failure on that DLL.
  The corrected Release passes 215, preserving source poses and effect state.
- Spatial AA and desktop BC7 now use the common strongest forced-legacy policy,
  including the command-line -gllegacy override. Native policy checks pass.
  The AA fixture needed its second current Scene owner collection initialized;
  its earlier missing-owner failure is preserved as a fixture failure.
- Windows native-pose test discovery, full source/build inventories and strict
  fresh-directory duration/condition/immutability gates pass focused regressions
  and independent review. The full Python suite passes 133, with one Windows
  symlink-privilege skip; four formerly skipped compiled cases now execute.
- Before additional correctness fixes, the frozen Release (MVID
  `5d14b5fe-80be-41a1-838a-b3c59a02d46f`) passes 474 visual pixel checks,
  14,822 preparation checks,1,680 shader failure scenarios and three map/Studio
  resource cycles per arm. These are intermediate results, not the final soak.
- Final gap review identified additional shared readback-state, cross-scene
  decode-ownership and opaque character KTX alpha repairs. These are integrated
  and requalified below; earlier receipts remain unchanged.

## Final Windows build and completed gates

The final client MVID is `53589a2c-2a2a-423c-94cc-24d50b7f6355`;
SHA256 `26906006a69c2275eb385e58ad1c1007167c137ec43b3eb541c74c2b73cbe729`.
Harness SHA256 is
`3eaf1d516939e49568d70af8cfb10a9d14555a2f883004960f5703e9e45eb1ba`.
The clean v5 Release rebuild reproduces all 489 files from the v4 bundle used
for native functional checks. Before/after inventories match for every
non-documentation input, including copied maps/assets. Explicit build metadata
and inventories remain separate from independently observed loaded DLL facts.

- Windows client/native harness and server builds PASS. Client has two existing
  upstream warnings; server has one. Test-tool builds have zero warnings/errors.
- Actual Scene decode retirement/shared reservations, thumbnail/readback pack
  restoration, OPAQUE KTX fallback and native material/state checks PASS.
- Native preparation 14,822, detached shadows 215, shader-failure scenarios 1,680,
  visual pixel assertions 474 and the final compositor fixtures PASS.
- Windows native Python suite PASS 134, with one symlink-privilege skip. The
  later duplicate-Python executable-resolution regression also passes in the
  focused eight-test tooling suite; its earlier preflight failure is retained.
- All 12 scale/shadow cells CAPTURE_PASS at 50/100/150/200% and Off/Low/High.
  Capture runs are excluded from timing acceptance.
- All six cadence cells PASS: numeric 60/120/144/240, Display and Unlimited,
  complete storage/identity/duration/state checks, zero simulation drops/stalls.
  Details and tails are in [Windows qualification](windows-native.md).
- The final 30-minute combat run PASS: 1,800.0138169 measured seconds, 431,761
  intervals, 239.865 FPS mean, 4.8192 ms p99, zero simulation drops/stalls, complete
  storage and matching duration/identity/state. Native/raw coverage agrees within
  2.512 ms. This passes the declared application gate; it is not an all-frame
  deadline, photon/VRR or thermal guarantee. Native maximum interval 32.3563 ms.
- Windows-hosted Android optional UI compile and normal enrolled ARM64 full
  AOT/APK build PASS; 123 AOT libraries are packaged. This is build evidence.

The [R00–R30 ledger](package-ledger.md) distinguishes implemented repairs,
partial scope, native gates and rejected/deferred experiments. New correctness
details: [decode ownership](texture-decode-lifetime.md),
[capture regions](capture-regions.md), [OPAQUE KTX fallback](opaque-ktx-fallback.md).
The later [R22 implementation](r22-implementation.md) has separate source/binary
identity and correctness evidence; the following native timing campaign predates it.
The [Android evidence](android.md) retains source identity and restore-only lock
normalization receipts; no dependency version was changed.

The compact [final receipt](final-receipt.json) binds the observed client,
harness, 3,543 extracted fixture files, build metadata and campaign gates.
The full identity manifest remains outside Git at
`C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-qualification-20261022\final-integrated-manifest.json`
(SHA256 `d719a700d2f9a9238496f1e7452316f0c1245d9542a17086d4b08c284264741c`).
The identity writer deliberately leaves its own native-acceptance field
UNVERIFIED; acceptance is assessed separately by the retained strict runner
receipts. All rejected, failed and inconclusive attempts remain preserved.

The [tail receipt](windows-timing-tails.json) and
[attribution receipt](windows-timing-attribution.json) retain rare misses:
the recurring short-run tick314 hitch lies inside measured HUD/simulation work;
the soak's worst interval is mostly an uninstrumented inter-callback gap.
Its cause is unknown. These observations remain in the distributions and do
not justify a speculative scheduler/GPU fix or all-frame deadline claim.

## Timing gate (declared before the resumed campaign)

One immutable integrated Release build, fixed seed/frozen OS keyboard+mouse
callbacks, eight bots, MP3 PROVING GROUND, 1280x720, Original, shadows Off, scale 100.
Exclude 240 simulation-step warmup. Numeric caps 60/120/144/240: completed callback
mean within2% of cap, interval p99<=twice target period, zero simulation drops or
stalls, complete chronological storage and matching immutable binary/conditions.
Display/Unlimited have functional/cadence-storage gates without guessing panel
scanout. Steady tests also require no unexpected focus/visibility/window-state
transitions or launcher startup. Preserve every failing run. No periodic profiler stream/GPU query mode,
source compilation or other test workload may run during the final matrix/soak.
The 30-minute combat run separately requires complete storage and no unexplained
drops/stalls. Capture-only scale runs are excluded from FPS acceptance.
These are application timing gates, not photon/VRR/network or universal FPS
acceptance. Raw capture overhead remains a separate unqualified cost.

## Evidence handling

Fresh output only; never replace a failed receipt. Small current logs/reports
live here. Large raw captures, source inventories, frozen binaries and invented
images are outside Git under the local ProjectPrime renderer evidence root.
The operator's extracted AMHE1 fixture remains outside Git. Build/source hashes
must describe the exact current binaries; digest matching alone never invents
compiler attestation, hardware tests or rollout approval.

All 489 frozen files and every non-documentation input remain byte-identical
through the final campaign. All 12 scale/shadow cells have correct PNG/world
sizes, nonflat color/depth coverage and identical repeated RGB/depth captures.
The [capture QA](windows-capture-qa.json) and [depth QA](windows-capture-depth-qa.json)
are evidence checks, not broad visual acceptance.

The required other-device, real-scene motion/visual/cost and complete
launcher/replay/four-view Studio lifecycle cells remain separately declared.
Windows RTX5080 results cannot close macOS AGX, physical Android or Linux gates.
There is no release/default/preset promotion, global program reuse, full linear
color pipeline, bloom/AO/governor or backend replacement in this campaign.
