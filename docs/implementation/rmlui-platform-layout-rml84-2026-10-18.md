# RML-84 platform layout and four-seat Home party

Date: October 18, 2026.

The user explicitly reversed the RML-83 Samus artwork/color experiment and supplied
Home/Lobby screenshots of the original platform chamber. The follow-up asks for
three additional Home party platforms and four Current Session slots. RmlUi remains
the exclusive workstream; full Settings stays last.

## Changes

- Restored the original procedural deployment chamber and its foreground Hunter
  platform; removed the cartridge Samus backdrop helper and warm-color overrides.
- Restored blue/cyan Play navigation and lobby controls. Empty capacity uses the
  reference OPEN SLOT headline and smaller + ADD BOT text. Existing fresh-authority
  checks, actual selected-room preview and narrower aligned side rails remain.
- Home adds three party pedestals through the existing chamber shader/context and
  authored formation geometry. The original local Hunter/platform remains.
- Current Session has four seats: the local Hub identity and three invite/member
  controls. Verified Party membership provides names and presence; friendship alone
  never occupies a seat. Larger parties remain intact with an overflow notice.
- Home and the Party page share one account/revision-fenced Party controller. The
  menu refreshes membership periodically while the Party page is closed. Closing
  the page preserves the service-owned controller; launcher teardown disposes it.
- The Party service has no remote Hunter/suit appearance contract. Extra platforms
  do not display guessed models. Slots open the existing Social/Party flow.
- Session/Social share a bounded right-hand rail; Social scrolls below the four
  seats and compact breakpoints preserve navigation access.

## Qualification

Delivered: `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml84-20261018.zip`
(85,162,276 bytes; SHA-256
`5d9d394de5cee0a9541bb7dd84b260afe955d3f9baea28c66ee78e146196182f`).
Main assembly: `1b30053f64d3796acfac78df88ec5ad913ae8313c72ca2b970e3f997ad3b622e`.
Native bridge: `23a47a54b767341370d753ebde4b1e7e05aea5a9d9ab0f50939aa3c3de820d82`.

- Client, server and six acceptance tools compile with zero warnings/errors.
  The five CPU suites pass. Twelve new Party assertions cover membership versus
  friendship, bounded four-seat projection/overflow, private presence, quiet
  refresh preservation, failed refresh clearing, account retirement and stale results.
- The actual native Home/Lobby fixture passes 699 assertions at client 1512x820,
  1280x720 and 1024x720, measured density 2. Real Hunter assets and the real AD1 PNG
  render in the existing RenderWindow/GL context. All four Home seats and their
  three native Social-route actions fit; map imagery and eligible Add Bot controls
  fit. Pointer/keyboard inputs preserve fresh revision, pending/owner/capacity gates,
  no optimistic bot insertion, and stale-row current-occupant resolution.
- Home, four-member party and lobby screenshots are captured for each size.
  Party/roster authorities in this fixture are explicitly synthetic. No live
  multiplayer or two-account backend acceptance is claimed. Remote Hunter/suit
  data remains absent from the service, so models are not inferred from names.
- Bundle version 84 uses the verified bundled ARM64 .NET 10.0.12 runtime and 26
  expected ARM-capable Mach-O components (23 thin ARM64; three verified universal
  libraries). Ad hoc signatures, exact signed inventory, independent ZIP extraction,
  bundled/extracted startup, extracted native render/resize and actual AD1 capture
  all pass. No game assets or player data are included.
- Sources and runtime are frozen under
  `artifacts/rmlui-platform-layout-rml84-20261018/build-attempt4/`.
  Only the restored Home text target and lobby preview-caption spacing differ from
  build3 production inputs; managed/native bytes are identical. Source/manifest guards
  pass before and after each run. Packaging/delivery receipts are under
  `artifacts/ProjectPrime-RmlUi-macOS-arm64-rml84-20261018/`.

The initial render failure (removed `home_social_summary` target) is preserved in
build3. The target now lives in the Social rail and every required Home binding is
present. The first new CPU fixture checked state before the existing 125ms poll;
its corrected fixture waits for projection and all final suites pass. The initial
source-only restoration in build1 predates the user's four-seat follow-up and is
not the delivered snapshot. Final membership refreshes preserve current-account
labels while busy; explicit page refresh and failed/account-changed states retain
clearing and authorization behavior.

Historical RML-83 replay acceptance remains incomplete at its unexpected paused
timeline close. This bounded layout work does not claim full migration completion.
