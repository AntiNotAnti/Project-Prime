# Known gaps and bounded claims

Last reviewed: **2026-09-12**.

This is the current evidence ledger for behavior that is open, only partially
verified, fixed but awaiting broader validation, superseded by a narrower fact,
not reproducible in the available environment, or dependent on a real device.
The only status values used here are `OPEN`, `PARTIALLY VERIFIED`, `FIXED`,
`SUPERSEDED`, `CANNOT REPRODUCE`, and `NEEDS DEVICE VALIDATION`.

An evidence date of `unknown` means the earlier record did not retain a date. It
does not mean the evidence was rerun on the review date. Automated, emulator,
synthetic, local-native, physical-device, deployed, and WAN evidence are not
interchangeable.

## Index

| ID | Gap | Status |
|---|---|---|
| CLIENT-001 | Phone scoreboard crash | CANNOT REPRODUCE |
| PLATFORM-001 | Launcher on Windows and macOS | NEEDS DEVICE VALIDATION |
| PLATFORM-002 | Launcher-to-live-match desktop flow | PARTIALLY VERIFIED |
| PLATFORM-003 | macOS was wholly unrun | SUPERSEDED |
| ANDROID-001 | Emulator visual fidelity | NEEDS DEVICE VALIDATION |
| ANDROID-002 | Portrait freeze fix on a real phone | PARTIALLY VERIFIED |
| RENDER-001 | Cel shading on a real phone | NEEDS DEVICE VALIDATION |
| ANDROID-003 | Physical ARM64/driver coverage | NEEDS DEVICE VALIDATION |
| UPDATE-001 | Update against a Project Prime release | OPEN |
| UPDATE-002 | Real browser launch | OPEN |
| DEPLOY-001 | Pi rename migration | OPEN |
| DEPLOY-002 | Linux ARM64 package startup | OPEN |
| DEPLOY-003 | Long-running Windows server behind a firewall | PARTIALLY VERIFIED |
| CAPACITY-001 | Pi real-game capacity and traffic ceiling | PARTIALLY VERIFIED |
| COMPAT-001 | Old client against current server | OPEN |
| ROTATION-001 | Rotation fix across additional map pairs | PARTIALLY VERIFIED |
| METRICS-001 | Late-join and bursty-tour skew | PARTIALLY VERIFIED |
| NETWORK-001 | Alt-attack edge coalescing | PARTIALLY VERIFIED |
| FIDELITY-001 | Kanden and Spire fidelity | OPEN |
| UI-001 | Scoreboard beyond eight players | OPEN |
| CONTENT-001 | First Hunt biodefense rooms | SUPERSEDED |
| FIDELITY-002 | Zoom and double-damage coverage | PARTIALLY VERIFIED |

## CLIENT-001 — Phone scoreboard crash

- **Status:** `CANNOT REPRODUCE`
- **Evidence date/reference:** 2026-09-06 phone report; desktop sweep date unknown.
- **Evidence:** A phone report said bot matches sometimes crash when opening the
  scoreboard. Desktop `-maptest` now forces the scoreboard twice per run and
  fails if it never draws. All twelve modes, two through eight players, bots,
  Pro HUD on/off, and forced `MatchState.Ending` completed without the crash.
  Two real defects on that path were fixed: `GameState.Reset` had retained
  networked `Nicknames`/`Stars`, and all four `DrawText2D` alignment branches
  indexed font tables without validating `ch - MinCharacter`. Either could
  have caused the report, but neither is confirmed.
- **Remaining work:** Capture the existing full render-thread exception and
  debug log from a phone where the crash occurs. Do not claim the reported
  phone defect fixed without that evidence.

## PLATFORM-001 — Launcher on Windows and macOS

- **Status:** `NEEDS DEVICE VALIDATION`
- **Evidence date/reference:** WSL/X11 launcher evidence date unknown; local
  macOS runtime smoke 2026-09-12.
- **Evidence:** The Avalonia front screen, settings, map grid, and pause menu
  were driven and captured under WSL/X11. The content-free SDL GPU runtime
  smoke now passes locally on Apple Silicon macOS, but it does not open or
  exercise the full launcher. Windows remains a GUI binary without a console.
- **Remaining work:** Run the actual launcher and its UI flows on Windows and
  macOS, including shared Avalonia/SDL event ownership and shutdown.

## PLATFORM-002 — Launcher-to-live-match desktop flow

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** source/menu-side checks date unknown; local
  content-free SDL runtime smoke 2026-09-12.
- **Evidence:** The launcher starts a match request and hides its window; pause
  flags, overlay windows, and event pumping have focused coverage. The current
  SDL GPU host can create a native Metal device and submit a content-free frame
  locally. No recorded run has played a content-backed match from the launcher
  through Escape/pause and return.
- **Remaining work:** Complete that end-to-end flow on a real desktop with game
  content and record the result.

## PLATFORM-003 — macOS was wholly unrun

- **Status:** `SUPERSEDED`
- **Evidence date/reference:** local native runtime smoke 2026-09-12.
- **Evidence:** The older blanket statement that macOS was only cross-compiled
  is no longer accurate. On Apple Silicon macOS, `--runtime-smoke` initialized
  SDL GPU/Metal, created the hidden swapchain, submitted one frame, reported a
  32x32 drawable surface, and disposed successfully. macOS packages also pass
  static package validation.
- **Remaining work:** This bounded smoke is not launcher, content-backed
  gameplay, input, audio, high-refresh, or long-session acceptance. Those remain
  under PLATFORM-001, PLATFORM-002, and the release gates.

## ANDROID-001 — Emulator visual fidelity

- **Status:** `NEEDS DEVICE VALIDATION`
- **Evidence date/reference:** emulator run date unknown; `.claude/android/ANDROID-PORT.md`.
- **Evidence:** An API 30 x86_64 emulator using software CPU and SwiftShader was
  driven from cold portrait start through the front screen into an offline
  first-person match with HUD. It proves that the Android lifecycle and guarded
  GLES path can load a room. SwiftShader produced vertical streaks with cel
  shading both on and off, so its images are not visual-fidelity evidence.
- **Remaining work:** Judge appearance on physical Android hardware with a real
  GPU driver.

## ANDROID-002 — Portrait freeze fix on a real phone

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** measurement and emulator rerun date unknown.
- **Evidence:** A synthetic 12-second room load with an injected resize held the
  old `GLSurfaceView` UI thread for 16,921 ms. After `GameView` took ownership of
  its EGL context/thread, the measured maximum was 1,092 ms and none occurred
  during load. The emulator subsequently started from portrait, survived
  home-and-back, exited, and started another match.
- **Remaining work:** Reproduce the load and lifecycle sequence on a real phone.

## RENDER-001 — Cel shading on a real phone

- **Status:** `NEEDS DEVICE VALIDATION`
- **Evidence date/reference:** desktop/emulator measurement date unknown;
  `.claude/render/CEL-SHADING.md`.
- **Evidence:** Desktop captures covered five rooms and a live two-client match
  at 1600x900; cel-off was pixel-identical to the prior path. Emulator depth
  noise measured 235–256 under SwiftShader versus 0.004–0.009 under llvmpipe,
  against a 1.1 threshold. `Renderer.CalibrateInk` measured 1998 there versus
  0.0159 locally and raises the ink floor so the emulator degrades to no outline
  instead of a black screen. Earlier wording that claimed visible ES cel
  rendering was wrong because those runs had cel shading off.
- **Remaining work:** Measure the calibration floor and appearance on a phone
  whose depth precision is between desktop and SwiftShader behavior.

## ANDROID-003 — Physical ARM64 and driver coverage

- **Status:** `NEEDS DEVICE VALIDATION`
- **Evidence date/reference:** unknown.
- **Evidence:** Current Android runtime evidence is x86_64/SwiftShader emulator
  evidence. A phone changes both ABI to ARM64 and GL implementation to a real
  vendor driver.
- **Remaining work:** Run protected and unprotected ARM64 builds on representative
  physical devices, including lifecycle, touch/controller, audio, and rendering.

## UPDATE-001 — Update against a Project Prime release

- **Status:** `OPEN`
- **Evidence date/reference:** upstream compatibility test date unknown.
- **Evidence:** Update discovery, version comparison, the update-available line,
  and release-page URL were exercised against upstream NoneGiven/MphRead. The
  no-matching-asset path was exercised at runtime; a matching Project Prime
  asset name is covered only by unit tests.
- **Remaining work:** Exercise update discovery and artifact selection against
  an actual Project Prime release.

## UPDATE-002 — Real browser launch

- **Status:** `OPEN`
- **Evidence date/reference:** headless test date unknown.
- **Evidence:** `OpenPage` correctly declined in a headless environment without
  `DISPLAY`. No browser was launched.
- **Remaining work:** Exercise `xdg-open` on a real Linux desktop and
  `UseShellExecute` on Windows; retain the URL safety boundary.

## DEPLOY-001 — Pi rename migration

- **Status:** `OPEN`
- **Evidence date/reference:** unknown.
- **Evidence:** `deploy-server.sh` contains migration logic for a systemd
  `ExecStart` still naming `MphRead` and removes the old binary, but that path
  has not run on the real Pi.
- **Remaining work:** After the first renamed deployment, inspect
  `systemctl cat mphread-server`, process identity, and the installed binaries.

## DEPLOY-002 — Linux ARM64 package startup

- **Status:** `OPEN`
- **Evidence date/reference:** current CI workflow reviewed 2026-09-12.
- **Evidence:** Linux ARM64 is cross-published on x64. CI can statically validate
  it but cannot execute its native apphost there. Linux x64 exercises the same
  build configuration on a runnable processor; that is not ARM64 execution.
- **Remaining work:** Start the extracted Linux ARM64 package on the Pi and run
  the package smoke there.

## DEPLOY-003 — Long-running Windows server behind a firewall

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** CI workflow reviewed 2026-09-12.
- **Evidence:** The Windows dedicated server starts in CI. There is no recorded
  long session on a real Windows machine behind a real firewall comparable to
  the Linux server deployment.
- **Remaining work:** Run an extended real-Windows session through its intended
  firewall/NAT path and retain logs and shutdown evidence.

## CAPACITY-001 — Pi real-game capacity and traffic ceiling

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** synthetic ramp date unknown.
- **Evidence:** Twenty hosted games and 160 synthetic players held with 98.8%
  delivery and no UDP errors. At 24 and 32 games, admission remained capped at
  160. The ramp measured relay/admission behavior, not whether a real match at
  that load remained playable. Above four games, the WSL NAT sender cost about
  3.9 ms per `sendto`, so the higher steps held aggregate traffic constant
  instead of offering every client a full 60 Hz.
- **Remaining work:** Measure real gameplay quality and the true traffic ceiling
  on the Pi with native clients and representative match behavior.

## COMPAT-001 — Old client against current server

- **Status:** `OPEN`
- **Evidence date/reference:** server running since 2026-09-01; wire refusal
  checks date unknown.
- **Evidence:** A full server answered a ninth `Hello` with `Refused` reason 1
  in 11 ms, and answered a protocol-3 `Hello` with reason 2. A client built
  before `RefusedPacket` has not been run against it. The intended fallback is
  to ignore the unknown packet and reach the pre-existing eight-second timeout.
- **Remaining work:** Run an actual pre-`RefusedPacket` client and verify that
  bounded compatibility behavior.

## ROTATION-001 — Rotation fix across additional map pairs

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** public-server reproduction date unknown.
- **Evidence:** The pooled-player `NodeRef` crash was found and fixed on the
  public rotation. MP1 SANCTORUS to MP3 PROVING GROUND was exercised with three
  clients. The mechanism is not map-specific, but no other pair is recorded.
- **Remaining work:** Exercise additional map-family transitions, joins/leaves,
  and pooled-player reuse.

## METRICS-001 — Late-join and bursty-tour skew

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** tour date unknown.
- **Evidence:** Tour clients begin roughly three seconds apart. A late observer
  can miss a burst such as bombing or unmorphing; time normalization cannot
  recreate a burst that happened before admission. This explains why raw
  subject/observer totals are not directly comparable and is not packet-loss
  evidence by itself.
- **Remaining work:** Compare only clients present for the same interval, or add
  an explicitly aligned measurement window before using the tour quantitatively.

## NETWORK-001 — Alt-attack edge coalescing

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** tour date unknown.
- **Evidence:** Every observer records about 60% of alt-attack presses. Agreement
  between observers argues against packet loss. Two presses within one intent
  window are ORed into one edge mask per packet. The bombs resulting from those
  presses still arrive at 79–99%.
- **Remaining work:** Decide whether the metric should count intent windows or
  whether protocol/input history needs a separately designed multiplicity
  representation. Do not call the current count packet loss.

## FIDELITY-001 — Kanden and Spire fidelity

- **Status:** `OPEN`
- **Evidence date/reference:** tour date unknown.
- **Evidence:** Kanden and Spire show lower observer fidelity than other Hunters
  for `unmorph` and projectile lifetime. No cause is established.
- **Remaining work:** Trace version-correct input, simulation, replication, and
  presentation evidence for those Hunters before changing behavior.

## UI-001 — Scoreboard beyond eight players

- **Status:** `OPEN`
- **Evidence date/reference:** layout review date unknown.
- **Evidence:** Rows tighten beyond four players to 19 px. A roster beyond eight
  would require another layout, probably a second column.
- **Remaining work:** Define and test the greater-than-eight-player layout,
  focus/navigation, text bounds, and narrow-screen behavior.

## CONTENT-001 — First Hunt biodefense rooms

- **Status:** `SUPERSEDED`
- **Evidence date/reference:** content inspection date unknown.
- **Evidence:** The First Hunt biodefense-chamber rooms are marked multiplayer
  but contain no player spawn points. They are survival rooms and are
  intentionally excluded from the launcher map list and Battle rotations.
- **Remaining work:** No defect fix is claimed or required. Revisit only if a
  separately authorized content-design task adds verified multiplayer spawns.

## FIDELITY-002 — Zoom and double-damage coverage

- **Status:** `PARTIALLY VERIFIED`
- **Evidence date/reference:** 90-second three-client match date unknown.
- **Evidence:** Tours do not reliably collect zoom or double damage, so they are
  usually untested. In one 90-second match, three clients agreed on replicated
  double-damage pickup counts (`12`, `12`, `12`). That makes the path plausible,
  not broadly measured.
- **Remaining work:** Add deterministic pickup scenarios and compare effect
  activation, duration, damage, expiry, reconnect, and observer presentation.
