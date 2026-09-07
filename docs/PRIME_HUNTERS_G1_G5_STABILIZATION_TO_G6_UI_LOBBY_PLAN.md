# Prime Hunters
# G1-G5 Stabilization -> G6 Lobby + UI/UX Overhaul
## Full Implementation Plan

**Target codebase:** latest uploaded Prime Hunters snapshot (`Fruity-Prime(2).zip`)  
**Current runtime:** .NET 10 / protocol 8 unreleased  
**Purpose:** finish and harden the already-implemented G1-G5 program, freeze a trustworthy multiplayer baseline, then build a persistent server-owned lobby and a complete cross-platform UI/UX shell around Prime Hunters.

---

# 0. Executive direction

Prime Hunters has reached a different stage from the earlier refactor.

The major multiplayer architecture already exists:

- 60 Hz authoritative server
- client prediction and remote interpolation
- G1 render interpolation and late-latched local look
- G2 combat feedback, assists, radar, network HUD, post-match presentation
- G3 overtime, late joining, team allocation, reliable world events, server browser quality work
- G4 Backend, accounts, PlayerId, Hunter License foundation, PostgreSQL ledger, game tickets, verified server reporting
- G5 observers, replay format 3, bots, telemetry, voting and tournament controls

The next work **must not be another architecture rewrite**.

The correct sequence is:

```text
CURRENT G1-G5 WORKING TREE
        |
        v
S0-S8: G1-G5 STABILIZATION
        |
        |-- integration cleanup
        |-- physical/render/device acceptance
        |-- G4 policy completion
        |-- Backend/security hardening
        |-- long combined soak
        |-- release baseline freeze
        |
        v
G6: LOBBY + UI/UX OVERHAUL
        |
        |-- persistent session shell
        |-- server-owned LobbyRuntime
        |-- redesigned Home / Play / Browser
        |-- lobby roster / Hunter / team / ready / chat
        |-- Hunter License UI
        |-- post-match -> lobby loop
        |-- replay library
        |-- settings/accessibility
        |-- Android responsive UI
        |
        v
POST-G6 STABILIZATION
        |
        v
AMHE1 GAMEPLAY FIDELITY AUDIT
```

Do not begin the AMHE1 gameplay-fidelity work during this plan.

AMHE1 may still be used as **test content** for current multiplayer validation, but not as a new gameplay-fidelity/remediation workstream until G6 is accepted.

---

# 1. Fresh codebase observations that drive this plan

## 1.1 Current project structure

Active projects now include:

```text
src/
  Game/
  Client/
  Server/
  Backend/
  Android/
  Audio.Ncsf/
  Shared.Replay/
  Tools/

tests/
  Tests/
  Backend.Tests/
  Imaging/
```

Retired build-artifact directories such as `src/MphRead`, `src/MphRead.Tests`, `src/MphRead.Android`, and `src/NcsfPlay` are still present in the archive but are not the active architecture.

Do not reintroduce dependencies on those retired directories.

## 1.2 Current automated evidence

The repository currently records:

```text
Main tests:       954 / 954
Backend tests:     43 / 43
Imaging tests:     18 / 18
Python tests:      58 / 58
Project boundary violations: 0
```

It also records:

- successful solution build
- successful dedicated-server publish
- successful Android managed Release build
- 300-second 8-player + 16-observer replay/telemetry soak
- 300-second impaired UDP combat runs at:
  - 100 ms RTT
  - +/-20 ms jitter
  - 2% loss
  - 8 peers
  - zero dropped ticks
  - zero reliable overflow
  - zero transport drops

These are strong foundations.

They are not yet full release acceptance.

## 1.3 Current open acceptance boundaries

The repository itself correctly identifies these remaining gaps:

- real 60 / 120 / 144 / 165 / 240 Hz rendered acceptance
- visual HUD/radar acceptance
- real audio-mix acceptance
- physical Android touch/HUD/radar/spectator/replay acceptance
- public TLS/SMTP Backend deployment
- external WAN validation
- combined long-duration soak with:
  - observers
  - bots
  - replay
  - telemetry
  - Backend outage/recovery
- G4 Ranking Points/star policy approval and implementation
- forfeit/disconnect policy completion
- production hostile-network protection for Ranked
- existing dependency advisory resolution/review

These become the stabilization program.

## 1.4 Current UI architecture

The GUI launcher has become too centralized.

Approximate current sizes:

```text
Client/Launcher/Gui/HomeView.cs        ~2,147 LOC
Client/Launcher/Gui/SettingsView.cs      ~956 LOC
Client/Runtime/Menu.cs                  ~1,852 LOC
GUI launcher folder total              ~7,300 LOC
```

`HomeView` currently orchestrates many unrelated product areas:

- account entry
- join by address
- server browser
- Quick Join
- match hosting
- map selection
- mode selection
- demos/replays
- settings
- launch handoff
- assorted status/error presentation

G6 must decompose this.

Do not implement a lobby as another 500-line section inside `HomeView`.

## 1.5 Current desktop flow is not session-persistent

Today the desktop flow is effectively:

```text
show HomeWindow
    ->
close HomeWindow
    ->
run RenderWindow match
    ->
finally:
    NetSession.Stop()
    NetHostSession.Stop()
    ->
create/show launcher again
```

`GuiLauncher.Run()` intentionally stops live networking after every match.

That conflicts with a real persistent lobby.

G6 therefore requires a deliberate **session-lifetime refactor**, but not a gameplay rewrite.

## 1.6 Current Android flow already shares Avalonia HomeView

Android uses Avalonia and directly builds `HomeView`.

This is valuable.

G6 should preserve one shared information architecture and one shared screen implementation wherever practical.

Do not create an unrelated Android lobby UI.

---

# 2. Program rules

These rules apply to all stabilization and G6 work.

## 2.1 Preserve authoritative gameplay

Lobby/UI code never determines gameplay truth.

```text
UI request
   ->
server validation
   ->
authoritative state
   ->
UI presentation
```

The client may request:

- Hunter choice
- team
- ready state
- map
- rule change
- vote
- start

The client never declares those actions successful.

## 2.2 Do not reintroduce offline authority

Practice remains:

```text
Client
  ->
localhost authoritative server
  ->
server bots
```

No local-only gameplay simulation path.

## 2.3 Keep `MatchRuntime` and lobby state separate

Do not put lobby fields into `MatchRuntime`.

Introduce a dedicated lobby/session domain.

Bad:

```csharp
MatchRuntime.IsInLobby
MatchRuntime.ReadyPlayers
MatchRuntime.LobbyHost
```

Good:

```text
LobbyRuntime
MatchRuntime
```

with explicit transition between them.

## 2.4 Do not upgrade unrelated frameworks during G6

Keep the current:

```text
.NET 10
Avalonia 11.3.11
current OpenTK/audio dependencies
```

unless a blocker is proven.

Do not combine:

- Avalonia 12 migration
- namespace rename
- NativeAOT conversion
- rendering backend rewrite
- dependency cleanup unrelated to G6

with this plan.

## 2.5 No full MVVM framework dependency

Do not add ReactiveUI or another framework solely to build G6.

Use a small internal UI state/navigation model.

Avalonia bindings are fine.

The architecture should remain understandable without a framework-specific mental maze.

## 2.6 Controller and touch are first-class inputs

Every screen introduced in G6 must be:

- mouse usable
- keyboard usable
- controller usable
- touch usable where Android exposes it

Do not build mouse-first screens and retrofit focus later.

## 2.7 Keep the text launcher

The portable text launcher remains valuable for:

- headless environments
- SSH
- broken desktop toolkit environments
- diagnostics
- recovery

Do not delete it as part of the visual overhaul.

It does not need feature parity with every decorative UI element, but core join/host/settings operations must remain functional.

## 2.8 No AMHE1 fidelity fixes during this program

If a G1-G5 or G6 test exposes a clear Prime Hunters regression, fix it.

Do not start broad:

- weapon comparison
- movement reverse engineering
- retail camera parity
- ARM9 behavior audits

until this plan completes.

---

# 3. Branch / commit safety

The latest snapshot currently contains a very large working tree:

```text
~127 tracked modified/deleted paths
~125 untracked paths
```

This is the first risk to address.

## Mandatory rules

The implementation agent must never:

```text
git reset --hard
git clean -fd
git checkout -- .
```

or otherwise discard unrelated work.

Known unrelated work includes at least:

- existing LICENSE deletion
- maps/Parallax changes

Those must remain outside stabilization/G6 commits unless explicitly requested.

---

# PART I
# G1-G5 STABILIZATION

---

# S0 - Integration checkpoint and worktree normalization

## Goal

Turn the current large working tree into reviewable, recoverable units before adding more features.

## S0.1 Inventory all changes

Generate:

```text
docs/STABILIZATION_WORKTREE_INVENTORY.md
```

Group every changed/untracked path into:

```text
G2 combat/HUD/radar
G3 match/network UX
G4 backend/account/persistence
G5 spectator/replay
G5 bots/telemetry
G5 voting/tournament
tests
docs
unrelated/pre-existing
generated/build artifacts
unknown
```

Any unknown file must be inspected before staging.

## S0.2 Remove generated artifacts from source packaging

Do not delete user source.

Remove or ignore obsolete generated folders from future source archives:

```text
src/MphRead/bin
src/MphRead/obj
src/MphRead.Tests/bin
src/MphRead.Tests/obj
src/MphRead.Android/obj
src/NcsfPlay/bin
src/NcsfPlay/obj
src/*/bin
src/*/obj
```

Ensure `.gitignore` covers them.

If old retired project directories contain no source needed by the active build, document that and remove them in a separate cleanup commit only after verification.

## S0.3 Commit by subsystem

Recommended integration commits:

```text
1. G2 combat feedback / HUD / radar
2. G3 match policy / reliable events / browser UX
3. G4 identity / backend / persistence
4. G5 observer / replay
5. G5 bots / telemetry
6. G5 voting / tournament
7. integrated tests
8. integrated docs
```

Do not create one giant "G1-G5 done" commit.

## S0 acceptance

- no valuable G1-G5 code remains untracked
- unrelated work remains untouched
- generated artifacts are not committed
- each commit can be reviewed independently
- complete test suite still passes after the final integration commit

---

# S1 - Freeze the G1-G5 technical baseline

## Goal

Create the baseline that every remaining stabilization change must preserve.

Create:

```text
docs/G1_G5_STABILIZATION_BASELINE.md
```

Record:

- commit hash
- .NET SDK
- protocol version
- AMHE1 test content hash/reference
- package versions
- test counts
- server publish output
- Android managed build result
- current Backend schema/migration version
- replay format version
- current network soak results

## Required commands

At minimum:

```bash
dotnet build Game.sln -c Release

GAME_DATA_DIRECTORY=<AMHE1> \
dotnet test tests/Tests/Tests.csproj -c Release

PRIME_TEST_POSTGRES_FILE=<private-connection-file> \
dotnet test tests/Backend.Tests/Backend.Tests.csproj -c Release

dotnet test tests/Imaging/Imaging.Tests.csproj -c Release

python3 -m unittest discover -s tools/tests

python3 tools/check-project-boundaries.py

dotnet publish src/Server/Server.csproj -c Release

dotnet build src/Android/Android.csproj \
  -c Release \
  -p:RuntimeIdentifier=android-arm64 \
  -p:RunAOTCompilation=false
```

## Acceptance

All expected results are documented before further stabilization edits.

---

# S2 - G1 physical render and input acceptance

## Goal

Close the biggest remaining G1 release gap: actual high-refresh visual behavior.

Automated tests are not sufficient for this pass.

## S2.1 High-refresh matrix

Run real rendered gameplay at:

```text
60 Hz
120 Hz
144 Hz
165 Hz
240 Hz
```

where hardware supports each value.

Test at least:

```text
Battle
Capture
Nodes
Prime Hunter
```

with:

- local movement
- jumping
- rapid view rotation
- alt-form transitions
- projectiles
- moving platforms
- death/spawn
- teleport transitions
- spectator target switching

## S2.2 Record metrics

Capture:

- simulation rate
- render rate
- frame time p50/p95/p99
- hard prediction corrections
- interpolation discontinuity resets
- camera visual offset
- GC allocations/frame if practical

## S2.3 Visual acceptance

Verify manually:

- no double-position rendering
- no first-person weapon wobble caused by bad interpolation
- no pose retained after render restoration
- no spawn/teleport interpolation streak
- no morph transition smear
- no camera input applied twice
- no refresh-rate-dependent aim outcome

## S2.4 Input acceptance

Test:

- high-DPI mouse
- low/high sensitivity
- controller right stick
- focus loss/refocus
- opening pause menu while moving mouse
- rapid alt-attack
- weapon change
- morph/jump press-release-press sequences

## Acceptance

G1 is physically accepted when gameplay outcome remains 60 Hz-authoritative while high-refresh presentation is visibly smoother and lower-latency.

---

# S3 - G2 visual/audio/touch acceptance

## Goal

Validate the real presentation systems added by G2.

## S3.1 HUD layouts

Test:

```text
16:9
16:10
21:9
small window
4K/high-DPI
Android portrait-prevented landscape sizes
Android small phone
Android tablet
```

Validate:

- Pro HUD
- radar
- kill feed
- hit marker
- headshot/kill marker
- death recap
- post-match presentation
- network health warning
- objective messages
- eight-way damage direction

## S3.2 HUD load

Create stress scenarios:

- rapid multi-kills
- multiple world events
- eight-player firefight
- repeated assist events
- chat + kill feed + objective message simultaneously
- degraded-network warning while scoreboard is open

No text overlap or unbounded queue growth.

## S3.3 Audio mix

Validate:

- hit confirm
- headshot
- kill
- incoming damage
- low health
- objective pickup/drop/capture
- overtime
- Prime Hunter transition
- pickup respawn cue

Rules:

- important game-world sounds remain audible
- hit feedback does not drown weapons
- repeated rapid hits do not produce unbearable stacking
- spectator and replay sound behavior is defined
- volume settings affect appropriate categories

## S3.4 Physical Android

Real device acceptance is mandatory.

Test:

- touch HUD
- radar
- pause
- scoreboard
- replay controls
- spectator controls
- post-match
- account screens
- soft keyboard interaction

## Acceptance

G2 is accepted only after visual/audio/touch evidence is recorded in:

```text
docs/G2_RELEASE_ACCEPTANCE.md
```

---

# S4 - G3 external-network and match-flow acceptance

## Goal

Close the gap between loopback impairment testing and realistic Internet play.

## S4.1 External WAN test

Use at least two physical Internet endpoints if available.

Test:

```text
40-80 ms normal
100-150 ms normal
high jitter
2-5% loss
temporary 1-3 second stall
```

Validate:

- connection/join
- countdown
- combat
- world events
- overtime
- late join
- reconnect
- spectator transition
- map transition
- intermission vote
- chat

## S4.2 Match-flow edge scenarios

Validate:

- player leaves during countdown
- player joins during overtime
- reconnect during grace
- team imbalance before start
- team disconnect mid-match
- lobby-choice vote selected
- rematch selected
- next-map selected
- server admin blocks rotation
- report outbox temporarily blocked at match end

## S4.3 Fixed interpolation remains default

The adaptive interpolation experiment failed its acceptance criteria.

Do not reopen it during stabilization.

Keep:

```text
RemotePresentationDelay = 6 ticks / 100 ms
```

unless a new design is separately approved.

## S4 acceptance

Create:

```text
docs/G3_WAN_ACCEPTANCE.md
```

and retain raw logs.

---

# S5 - Complete G4 rating / forfeit semantics

## Goal

Remove `policyPending` from the official Hunter License path.

This is a product-policy pass plus implementation.

## S5.1 Required decision

The existing `G4_RANKING_SPEC.md` has recovered the actual AMHE Rev 1 data:

### Star thresholds

```text
0-39       *
40-139     **
140-389    ***
390-749    ****
750-850    *****
```

850 is the legal maximum.

### Retail AMHE1 gain/loss matrix

| Self / Opponent | 1 star | 2 star | 3 star | 4 star | 5 star |
|---|---:|---:|---:|---:|---:|
| 1 star | +2/-1 | +4/-1 | +7/-1 | +10/0 | +15/0 |
| 2 star | +1/-2 | +3/-3 | +6/-1 | +10/0 | +15/0 |
| 3 star | +1/-4 | +2/-3 | +4/-4 | +8/-3 | +12/-2 |
| 4 star | +1/-8 | +1/-8 | +4/-6 | +5/-5 | +8/-4 |
| 5 star | +1/-15 | +1/-12 | +2/-10 | +4/-8 | +6/-6 |

The static values are verified against AMHE Rev 1.

The remaining choice is how Prime Hunters extends that four-player-era behavior to up to eight players.

### Recommended policy

Use the documented:

```text
PairwiseNormalizedV1
```

unless the project owner explicitly selects another named policy.

Do not let the implementation agent silently choose.

## S5.2 Forfeit policy

The current proposal is:

- fixed official roster when Playing begins
- explicit Leave => immediate forfeit
- transport loss => 30-second / 1,800-tick grace
- valid same-PlayerId reconnect before deadline resumes participant
- deadline expiry => forfeit
- match completion while disconnected => forfeit
- forfeiter ranks below all finishers
- forfeiter remains in opponent calculations
- bots/guests do not qualify for official RP
- official late joins spectate until next match

Approve or modify these rules before implementation.

## S5.3 Implement versioned RatingService

Backend:

```text
Backend/Rating/
  RatingPolicy.cs
  RatingPolicyVersion.cs
  RetailPointMatrix.cs
  PairwiseNormalizedV1.cs
  RatingTransaction.cs
```

Requirements:

- pure calculation
- all participant input ratings frozen inside one DB transaction
- stable PlayerId lock ordering
- single final clamp to 0..850 if using proposed modern policy
- store:
  - points before
  - tier before
  - pair contributions
  - raw delta
  - normalized delta
  - applied delta
  - points after
  - tier after
  - policy version

## S5.4 Outcome projection cleanup

Current career projection must distinguish:

```text
FinishedWin
FinishedLoss
Tie
Forfeit
DepartedGraceExpired
NoContest / Invalid if introduced
```

Do not let `!won` implicitly define every non-win outcome.

## S5.5 Weapon aggregate semantics

Current weapon aggregate `Matches` increments even when the weapon was not used.

Before exposing that value, choose a real definition.

Recommended:

```text
MatchesUsed
```

increments only when authoritative data proves at least one of:

- fired
- damaged
- killed
- possessed/equipped if that is explicitly tracked

If current MatchResult cannot prove usage, do not fabricate `MatchesUsed`.

Either:

1. omit the metric for now, or
2. add bounded authoritative weapon usage counters.

## S5.6 Rating tests

Required:

- all 25 matrix cells gain
- all 25 matrix cells loss
- boundaries:
  - 0
  - 39
  - 40
  - 139
  - 140
  - 389
  - 390
  - 749
  - 750
  - 850
- saturation at 0
- saturation at 850
- tie
- mixed signed pair results
- eight-player normalization
- team opposing-pair logic
- bots excluded
- guest excluded
- community/private excluded
- immediate leave
- reconnect within grace
- reconnect after grace
- match ends during grace
- duplicate result idempotency
- concurrent report serialization
- rating rebuild from persisted transaction ledger

## S5 acceptance

Backend no longer returns `policyPending` for eligible verified matches.

Hunter License displays authoritative:

```text
Ranking Points
star tier
rank title
delta from last official match
```

---

# S6 - G4 production Backend/security hardening

## Goal

Make the Backend safe enough to support G6 account-first UI and eventual public deployment.

## S6.1 Fix global rate limiter

Current global fixed-window limiter uses one literal partition for all callers.

That can allow one noisy source to exhaust the shared request allowance.

Replace it with:

```text
per-IP / per-account endpoint limiting
+
global concurrency/resource protection
```

Do not maintain a small shared requests-per-minute bucket as the primary global control.

## S6.2 TLS and public endpoint configuration

Production Backend must require:

```text
HTTPS
```

except explicit loopback developer mode.

Add startup validation:

- production + HTTP => fail startup
- missing signing keys => fail startup
- default/dev server credentials => fail startup

## S6.3 Email/SMTP

If email verification is required for official play:

- production SMTP/provider configuration must be validated
- resend rate limits
- confirmation code expiry
- no email content containing secrets beyond required verification token
- graceful provider outage

## S6.4 Ranked transport security gate

Current signed bearer game tickets provide authenticity but not complete on-path theft resistance over the plain UDP handshake.

Before hostile public Ranked, choose one:

### Path A: implement session proof-of-possession / authenticated session establishment

or:

### Path B: keep Ranked disabled publicly

G6 may still proceed under Path B.

The UI must not advertise Ranked as available if this gate is not satisfied.

## S6.5 Refresh-token persistence

For polished G6 automatic sign-in, introduce:

```text
ISecureSessionStore
```

Store only refresh/session material that is appropriate for persistence.

Platform implementations may use:

```text
Windows Credential Manager / DPAPI
macOS Keychain
Android Keystore
Linux Secret Service
```

If a platform does not provide an approved secure store, fall back to memory-only sign-in.

Do not store passwords.

## S6.6 Secrets / operations

Validate:

- secrets absent from repo
- report credentials rotated
- server identities revocable
- ticket signing key rotation documented
- DB migrations back up safely
- server outbox status observable
- failed/rejected reports inspectable

## S6 acceptance

Create:

```text
docs/G4_PRODUCTION_READINESS.md
```

with explicit status:

```text
Account system: ready / not ready
Verified Casual: ready / not ready
Ranked: ready / intentionally disabled
```

---

# S7 - G5 combined release soak

## Goal

Exercise the systems together rather than only in focused runs.

## S7.1 Minimum one-hour combined soak

Use real AMHE1 multiplayer content.

Recommended phases:

### Phase 1

```text
4 humans
4 server bots
8 observers
replay enabled
telemetry enabled
Backend online
```

### Phase 2

Humans join until bots retire.

### Phase 3

Humans disconnect/reconnect.

### Phase 4

Temporarily disable Backend/report endpoint for 10 minutes.

Confirm:

- gameplay continues
- reports spool durably
- no simulation block
- reports submit after recovery

### Phase 5

Rotate several maps/modes.

### Phase 6

Exercise:

- Duel
- team mode
- objective mode
- overtime
- voting
- tournament controls
- trusted observer
- delayed observers

## S7.2 Maximum spectator delay

Current supported delay is:

```text
0..30 seconds
```

Run at 30 seconds with:

```text
16 observers
```

on a heavy map.

Verify:

- timeline memory stays bounded
- baseline remains available
- no live fallback
- observer age meets configured policy within expected snapshot cadence
- no send queue growth

## S7.3 Replay durability

During soak:

- simulate abrupt server stop after replay writes
- verify recoverable/reported tail behavior
- index all completed replays
- seek repeatedly
- change camera targets
- use 0.25x / 0.5x / 1x / 2x / 4x

## S7.4 Metrics

Record:

- tick p50/p95/p99/max
- working set over time
- GC collections
- send queue depth
- reliable pending high-water
- observer timeline memory
- replay write throughput
- telemetry queue depth
- outbox depth
- report retry count
- connection drops
- dropped simulation ticks

## Acceptance

No unbounded growth trend over the run.

Any monotonic memory growth must be explained or fixed before G6.

---

# S8 - Stabilization freeze / G6 gate

## Goal

Declare a trustworthy pre-G6 baseline.

Create:

```text
docs/G1_G5_STABILIZED.md
```

It must contain:

```text
commit
protocol
replay format
DB migration version
rating policy version
test counts
physical render acceptance
Android acceptance
WAN acceptance
Backend readiness status
combined soak evidence
known limitations
```

## Required G6 entry conditions

G6 starts only when:

- G1-G5 changes are committed
- full automated suite passes
- physical high-refresh acceptance passes
- physical Android acceptance passes
- rating/forfeit semantics are frozen
- Backend contracts needed by the UI are stable
- no known G1-G5 P0/P1 correctness issue remains
- combined soak passes
- lobby wire protocol policy is decided

---

# PART II
# G6 - LOBBY + UI/UX OVERHAUL

---

# 4. G6 product goals

G6 turns the collection of multiplayer features into one coherent player experience.

Target loop:

```text
Launch
  ->
automatic session restore / sign in / guest
  ->
HOME
  ->
PLAY
  ->
Quick Play / Ranked / Browse / Private / Practice
  ->
LOBBY
  ->
Hunter / team / ready / chat
  ->
MATCH
  ->
POST-MATCH RESULTS
  ->
RP / stats update
  ->
LOBBY
  ->
rematch / vote / next game
```

A player should not repeatedly return to a disconnected launcher state between matches on the same server.

---

# 5. G6 information architecture

Top-level navigation:

```text
HOME
PLAY
HUNTER LICENSE
REPLAYS
SETTINGS
```

`PLAY` contains:

```text
Quick Play
Ranked
Server Browser
Private Match
Practice
```

Ranked must be hidden/disabled with an explicit reason if the S6 hostile-public security gate is not satisfied.

Do not display a button that silently falls back to unranked behavior.

---

# G6.0 - Design language and UI foundation specification

## Goal

Define one shared UI system before migrating screens.

Create:

```text
docs/G6_UI_DESIGN_SYSTEM.md
```

## Visual principles

Prime Hunters UI should be:

- high contrast
- readable during motion
- sci-fi without sacrificing usability
- information-dense only where useful
- consistent with the in-game HUD
- responsive across desktop and Android
- animation restrained
- controller focus obvious

## Token system

Add:

```text
src/Client/UI/Theme/
  UiColors.cs
  UiSpacing.cs
  UiTypography.cs
  UiMetrics.cs
  UiMotion.cs
  UiBreakpoints.cs
```

Avoid random per-screen constants.

Example tokens:

```text
Space1
Space2
Space3
Space4
RadiusSmall
RadiusMedium
TextSmall
TextBody
TextHeading
TextDisplay
FocusThickness
SafeArea
```

## Accessibility

Define from day one:

- UI scale
- minimum text size
- colorblind-friendly team palettes
- reduced motion
- no status encoded only by color
- visible focus state
- optional large text
- safe area
- screen reader/automation labels where Avalonia supports them
- hold/toggle accessibility
- independent HUD/radar scaling remains separate

---

# G6.1 - Introduce a real UI navigation layer

## Current problem

`HomeView` is approximately 2,147 lines and contains multiple unrelated workflows.

## Target structure

Add:

```text
src/Client/UI/
  AppShell/
  Navigation/
  State/
  Components/
  Screens/
  Dialogs/
  Theme/
```

Suggested:

```text
UI/
  AppShell/
    AppShellView.cs
    AppShellState.cs

  Navigation/
    UiRoute.cs
    UiRouter.cs
    UiNavigationState.cs
    UiModalHost.cs

  State/
    AppState.cs
    PlayState.cs
    SessionViewState.cs

  Components/
    PrimaryButton.cs
    SecondaryButton.cs
    FocusCard.cs
    PlayerRow.cs
    HunterCard.cs
    MapCard.cs
    ServerCard.cs
    Tabs.cs
    StatusBadge.cs
    EmptyState.cs
    ErrorBanner.cs
    LoadingIndicator.cs
```

Use Avalonia XAML for layout/styles where it genuinely reduces code.

Custom-drawn controls may remain for branded/high-performance elements.

Do not rewrite everything into XAML solely for ideology.

## Navigation model

Example:

```csharp
public enum UiRoute
{
    Home,
    Play,
    Lobby,
    ServerBrowser,
    PrivateMatch,
    HunterLicense,
    Replays,
    Settings,
    PostMatch,
    Account
}
```

`UiRouter` owns:

- current route
- back stack
- modal stack
- route transition
- focus restoration

Screens do not open random windows directly.

## Acceptance

A test shell can navigate between placeholder screens on desktop and Android before existing features are migrated.

---

# G6.2 - Persistent client session coordinator

## This is the key architectural pass

Current `GuiLauncher` disposes networking after every match.

A persistent lobby requires the socket/session to outlive the RenderWindow.

## Add

```text
src/Client/Networking/ClientSessionCoordinator.cs
src/Client/Networking/ClientSessionState.cs
src/Client/Networking/SessionPump.cs
```

Suggested states:

```text
Disconnected
Connecting
Lobby
LoadingMatch
InMatch
PostMatch
Leaving
Failed
```

## Preserve current networking implementation

Do not rewrite `NetClient`.

Do not create a second network stack.

Refactor `AuthoritativePlay` minimally so it can live while no `Scene` is attached.

### Add scene lifecycle

Conceptually:

```csharp
AttachScene(Scene scene)
DetachScene(Scene scene)
PollSession()
```

`BeforeSimulation(scene)` becomes:

```text
PollSession()
+
scene-specific apply/presentation
```

When the lobby UI is active:

```text
Avalonia DispatcherTimer
  ->
ClientSessionCoordinator.Poll()
  ->
AuthoritativePlay.PollSession()
```

When the match is active:

```text
60 Hz game simulation
  ->
AuthoritativePlay.BeforeSimulation()
```

Never poll from both owners simultaneously.

## Persistent desktop shell

Change `GuiLauncher` behavior.

Instead of repeatedly constructing/destroying HomeWindow:

```text
create AppShellWindow once
show
hide during match
show after match
```

The same Avalonia application remains alive.

Do not close the live server/client session when merely returning to lobby.

## Session shutdown

Networking is disposed only when:

- user explicitly leaves server/session
- server disconnects
- connection fails unrecoverably
- app exits
- user switches to unrelated server

## Host process lifetime

`NetHostSession` must remain alive across:

```text
Lobby -> Match -> Results -> Lobby -> Match
```

for a private hosted session.

Do not restart the dedicated server for every rematch.

## Acceptance

A client can:

```text
connect
enter lobby
play match
return to lobby
play another match
leave server
```

with one network connection/session lifecycle where server policy permits it.

---

# G6.3 - Add shared Lobby domain

## Goal

Represent lobby truth separately from match truth.

Add:

```text
src/Game/Lobby/
  LobbyRuntime.cs
  LobbyPhase.cs
  LobbyPolicy.cs
  LobbyPlayer.cs
  LobbyPermissions.cs
  LobbyRulesDraft.cs
  LobbySelection.cs
```

## Suggested LobbyPhase

```text
Open
Starting
Locked
```

Do not mirror match phases.

## LobbyRuntime owns

```text
SessionId
Revision
Policy
Players
Observers
Host/owner if applicable
selected map
draft match rules
ready states
Hunter selections
team requests/assignments
last completed match summary reference
vote state if active
```

## It does not own

- movement
- health
- damage
- gameplay entities
- active MatchRuntime score
- hit detection
- simulation clock

## Lobby player state

Example:

```text
PlayerId / guest session identity
connection identity
display name
role
Hunter
team
ready
ping summary
star rank if authenticated and available
host/admin permissions
```

## Rules draft

Lobby edits a mutable:

```text
LobbyRulesDraft
```

When a match starts:

```text
validate
  ->
freeze
  ->
MatchRules
```

`MatchRules` remains immutable once play begins.

---

# G6.4 - Lobby protocol

## Protocol version strategy

Current protocol 8 is still unreleased.

Recommended:

```text
Keep protocol 8 unreleased through G6.
Finalize lobby messages inside protocol 8.
Release protocol 8 only after G6 wire contracts freeze.
```

If protocol 8 is shipped publicly before G6 begins, then G6 must use protocol 9.

Never silently change a released protocol.

## Use reliable lobby state

Given the small maximum lobby size:

```text
8 players
+
up to 16 spectators
```

prefer simplicity over delta complexity.

### Server -> client

Add:

```text
ReliableEventType.LobbySnapshot
ReliableEventType.LobbyFeedback
ReliableEventType.MatchSummary
```

`LobbySnapshot` is a complete bounded authoritative lobby state.

Every accepted lobby mutation increments:

```text
LobbyRevision
```

and broadcasts a new snapshot.

This is intentionally simpler than a large delta-state engine.

## Client -> server

Add a bounded:

```text
LobbyRequest
```

with request type:

```text
SetReady
SelectHunter
RequestTeam
SetMap
SetMode
SetRule
StartMatch
ReturnToLobby
Rematch
```

Only include host/admin commands where permissions allow them.

## Server validation

Validate:

- request length
- request enum
- Hunter range
- team range
- map allowlist
- mode support
- rules bounds
- sender permissions
- current lobby phase
- match/session identity
- duplicate/replay semantics

## Chat

Existing session chat can remain for active matches.

Add a lobby-aware chat scope rather than forging a fake MatchId.

Suggested:

```text
ChatScope.Match
ChatScope.Lobby
ChatScope.Team
```

Server remains authoritative about recipient routing.

## Snapshot size

Hard-cap names, rows and optional strings.

No unbounded arbitrary JSON on the UDP gameplay transport.

---

# G6.5 - Server Lobby service

Add:

```text
src/Server/Lobby/
  ServerLobby.cs
  LobbyAdmission.cs
  LobbyAuthority.cs
  LobbyStartPolicy.cs
  LobbyHostPolicy.cs
```

## ServerLobby responsibilities

- maintain LobbyRuntime
- validate lobby requests
- broadcast lobby snapshots
- determine readiness
- freeze draft MatchRules
- initiate next MatchId/map transition
- return completed sessions to lobby
- preserve connections across match
- coordinate bot policy
- coordinate observers
- coordinate voting
- coordinate tournament locks

## Reuse current systems

Do not create parallel implementations of:

```text
TeamAllocator
RulesetResolver
ServerVoteSession
ServerBotManager
TournamentAdmin
```

Lobby calls those services.

## Integrate current `VoteLobbyHold`

Today the server uses:

```text
VoteLobbyHold
```

as a waiting-state seam after a lobby intermission choice.

G6 should migrate that behavior into real LobbyRuntime.

After migration:

- `VoteLobbyHold` becomes unnecessary or narrowly transitional
- `IntermissionChoice.Lobby` enters LobbyRuntime
- auto-rotation servers may still skip manual lobby

## Public dedicated-server policies

Support at least:

```text
NoLobby
    traditional auto-rotate behavior

IntermissionLobby
    lobby only between matches

PersistentLobby
    session uses lobby before every match
```

Private hosted games default:

```text
PersistentLobby
```

Practice may:

```text
PersistentLobby or auto-start based on user preference
```

---

# G6.6 - Ready system

## Policy

Lobby has:

```text
ReadyRequired
MinimumPlayers
HostMayForceStart
```

Bots count as ready automatically.

Observers never count toward player readiness.

## Start behavior

Do not implement two different gameplay countdowns.

Lobby start:

```text
all conditions satisfied
    ->
freeze MatchRules
    ->
send MatchTransition
    ->
client loads map
    ->
existing authoritative MatchLifecycle.Countdown
    ->
Playing
```

The existing match countdown remains the only competitive countdown.

## Cancel behavior

If a critical participant leaves while map loading/countdown policy requires quorum:

- existing match lifecycle cancels/resets
- server returns or remains in appropriate state
- lobby snapshot reflects the change

---

# G6.7 - Hunter selection

## Lobby UI

Add:

```text
Hunter grid
selected Hunter preview
affinity weapon
personal Hunter stats if authenticated
```

Example data:

```text
Noxus
142 matches
58.4% win rate
1.91 K/D
```

Stats are presentation only.

## Rules

Server validates Hunter selection.

Do not assume unique-Hunter locking unless a ruleset explicitly requires it.

If current gameplay allows duplicate Hunters, preserve that behavior.

## Timing

Hunter selection locks when:

```text
MatchRules freeze / transition begins
```

Mid-match Hunter switching remains whatever existing gameplay policy defines.

Do not add it through lobby code.

---

# G6.8 - Team lobby

For team modes, visually present:

```text
ORANGE
  Jarrett
  Player3

GREEN
  Player2
  Player4
```

## Casual/private

Player may submit:

```text
RequestTeam
```

Server decides.

## Competitive

Use:

```text
TeamAllocator
```

and make manual requests unavailable or advisory only according to policy.

## Tournament

Admin roster/team locks take precedence.

Lobby UI must explain why a control is unavailable.

Never present a dead button with no explanation.

---

# G6.9 - Private match creation

Replace the current direct host/start flow with:

```text
Private Match
   ->
Create Lobby
   ->
configure
   ->
invite/join
   ->
ready
   ->
start
```

## Host configuration

Expose supported MatchRules through grouped UI.

### Match

- map
- mode
- score goal
- time limit
- lives/objective goal where relevant

### Gameplay

- Friendly Fire
- affinity weapons
- radar policy
- spawn policy
- damage level
- overtime policy
- late join

### Participants

- max players
- spectators
- bots
- bot skill
- ready required

### Preset

- Classic
- Competitive
- Custom
- Duel
- Practice

Do not expose invalid combinations.

Use current rules validators to drive enabled/disabled UI.

---

# G6.10 - Home screen

Replace the feature-heavy HomeView root with a focused Home screen.

Suggested content:

```text
PRIME HUNTERS

PLAY

HUNTER LICENSE

REPLAYS

SETTINGS
```

Account identity appears as a compact status area:

```text
Jarrett
**** Master Hunter
524 RP
```

Guest:

```text
Guest
Create Hunter License
```

Do not place server configuration directly on Home.

---

# G6.11 - Play screen

Provide:

```text
Quick Play
Ranked
Server Browser
Private Match
Practice
```

Each has one-sentence purpose text.

## Quick Play

Reuse current:

```text
ServerBrowser.QuickJoin
favorites/recent/filter primitives
```

Do not invent a new directory stack.

## Ranked

Initially:

- discover only verified Ranked-eligible servers
- require authenticated verified account
- require S6 security gate
- require rating policy active

If any prerequisite is missing, show an explicit unavailable state.

Do not silently route Ranked to casual.

## Practice

Flow:

```text
Practice
  ->
private localhost lobby
  ->
bot configuration
  ->
start
```

Uses authoritative server bot fill.

---

# G6.12 - Server browser overhaul

Keep current portable selection/filter logic.

Replace presentation.

## Layout

Wide:

```text
server list      server details
```

Compact:

```text
server list
  ->
details screen
```

## Show

- server
- map
- mode
- players/max
- bots
- spectators if relevant
- ping
- current phase:
  - Lobby
  - Playing
  - Intermission
- time remaining
- ruleset
- verified/ranked state
- friendly fire
- radar
- spawn policy
- late join
- password/private if introduced

## Filters

Keep/enhance:

- mode
- favorites
- recent
- max ping
- hide full
- hide incompatible
- sort ping/population

## Actions

- Join
- Spectate
- Favorite
- Copy endpoint if appropriate

---

# G6.13 - Lobby screen

This is the center of G6.

## Wide layout

```text
+-----------------------------------------------------------+
| PRIME HUNTERS                    MAP / MODE / RULESET      |
+------------------------------+----------------------------+
| PLAYERS                      | MATCH                      |
|                              |                            |
| **** Jarrett  NOXUS   READY  | Sanctorus                  |
| ***  Player2  TRACE   READY  | Battle                     |
| **   Player3  SYLUX   ---    | Competitive                |
|                              | 7 points / 7:00            |
| SPECTATORS                   | Radar: Enhanced            |
| Player4                      | FF: Off                    |
|                              | Spawn: Enhanced            |
+------------------------------+----------------------------+
| CHAT                                                       |
| ...                                                        |
+-----------------------------------------------------------+
| CHANGE HUNTER | TEAM | READY | LEAVE                      |
+-----------------------------------------------------------+
```

## Compact/mobile layout

Use tabs or stacked sections:

```text
Players
Match
Chat
```

Do not shrink the desktop layout until text becomes microscopic.

## State indicators

Must distinguish:

```text
Ready
Not Ready
Loading
Spectator
Disconnected grace
Bot
Host
Admin
Ranked eligible
```

without relying only on color.

---

# G6.14 - Map selection redesign

Replace text-only picker for modern UI with map cards.

Each card may show:

- preview
- name
- supported mode
- recommended players
- retail/custom label
- custom map author if metadata supports it

Use existing map thumbnail/preparation infrastructure.

## Performance

Do not synchronously decode every full-resolution map preview when opening the screen.

Use:

- cached thumbnail
- bounded async loading
- placeholder
- cancellation when screen closes

---

# G6.15 - Lobby chat

Reuse current bounded server chat infrastructure.

Add lobby scope.

## Features

Initial:

- lobby chat
- team chat in applicable contexts
- mute
- compact history
- controller-friendly text-entry handoff
- Android soft keyboard

Do not add voice chat in G6.

## Moderation hooks

Keep room for:

- mute
- report
- server admin silence

but do not build an enormous moderation platform in this pass.

---

# G6.16 - Hunter License UI overhaul

Current G4 Backend supplies the domain foundation.

Create:

```text
UI/Screens/HunterLicense/
```

Tabs:

```text
Overview
Hunters
Weapons
Maps
Matches
```

Achievements may be added only if their backend domain actually exists.

Do not create fake placeholder achievement persistence.

## Overview

Show:

- display name
- star rank/title
- Ranking Points
- next threshold
- selected favorite Hunter
- W/L
- win ratio
- K/D
- play time
- favorite map
- favorite weapon
- favorite mode
- streaks
- win emblem

## Hunters

Per-Hunter only display stats the backend can truthfully attribute.

Do not invent per-Hunter kills where mixed-Hunter matches prevent attribution.

## Matches

Cursor-paginated match history.

No huge eager history fetch.

---

# G6.17 - Post-match results -> lobby loop

## Current target

After match:

```text
Playing
  ->
Ending
  ->
PostMatch
  ->
Lobby
```

## Server

At completion produce a bounded immediate:

```text
MatchSummary
```

for connected participants.

Do not wait for Backend persistence.

## MatchSummary should contain

At minimum:

```text
MatchId
map/mode
duration
placement
player rows:
  name
  Hunter
  team
  points
  kills
  deaths
  assists
  damage
  headshots
  objective stats supported by mode
```

Keep Backend-only/private data off the gameplay packet.

## Rating delta

For official verified matches:

```text
MatchSummary immediate
+
Backend rating asynchronously
```

UI initially shows:

```text
Rating update pending...
```

then refreshes when the Backend commit becomes available.

Do not delay return to lobby waiting for PostgreSQL.

## Actions

Depending on server policy:

```text
Continue
Rematch vote
Next map vote
Return to lobby
Leave server
View Hunter License
```

## Server transition

Connections remain alive.

Private persistent session:

```text
PostMatch -> Lobby
```

Traditional auto-rotation server may:

```text
PostMatch -> next MatchTransition
```

according to lobby policy.

---

# G6.18 - Replay library overhaul

Replace the current demo picker with a real replay screen.

Use existing Replay 2.0 / format 3.

## Library

Show:

- date
- map
- mode
- duration
- players
- result if available
- file compatibility
- recovered-tail warning if relevant

## Actions

- Play
- Delete local replay with confirmation
- Import
- Export/share file through platform mechanism where supported

## Viewer controls

Reuse existing:

- seek
- speed
- event markers
- spectator cameras

Build polished controls around them.

Do not rewrite replay storage.

---

# G6.19 - Settings overhaul

Current `SettingsView.cs` is nearly 1,000 lines.

Split by domain.

Suggested categories:

```text
Gameplay
Controls
Video
Audio
HUD
Radar
Network
Accessibility
Account
```

## Rules

- search is optional, not required
- changes clearly indicate immediate vs restart-required
- reset category
- restore defaults
- key conflicts clearly shown
- controller focus predictable
- Android hides irrelevant desktop-only controls
- desktop hides Android-only controls

## Keep settings domain separate

The UI writes through existing settings services.

Do not make UI controls the storage model.

---

# G6.20 - In-match pause / overlay integration

Do not replace gameplay HUD with Avalonia.

The actual rendered match remains OpenGL/game presentation.

G6 should unify the *look and navigation language* of:

- pause
- settings
- scoreboard interactions
- leave session
- player actions

but maintain the correct rendering architecture.

## Pause actions

Suggested:

```text
Resume
Settings
Hunter License / player card where safe
Mute players
Leave Match / Leave Server
Quit
```

Opening a menu never pauses authoritative server gameplay.

---

# G6.21 - Android responsive architecture

Android already shares HomeView through Avalonia.

Replace that with the same AppShell.

## Responsive breakpoints

Define explicit composition classes:

```text
Compact
Medium
Wide
```

based on logical width rather than device model.

## Compact behavior

Prefer:

- stacked cards
- full-width actions
- tabs
- bottom navigation if useful

Avoid:

- tiny desktop tables
- hover-only affordances
- precise small click targets

## Physical-device matrix

At minimum:

- small phone
- large phone
- tablet

Test:

- account login
- soft keyboard
- Home
- Play
- Server Browser
- Lobby
- Hunter selection
- Ready
- Lobby chat
- Hunter License
- Replay library
- Settings
- return from match

---

# G6.22 - Controller navigation

Every focusable component must support explicit directional navigation.

Add a central:

```text
UiFocusManager
```

or use Avalonia focus navigation with small project-specific policy helpers.

Test:

- D-pad
- stick navigation
- A/confirm
- B/back
- shoulder tab changes
- modal focus trap
- returning focus to previous item after closing modal

## Critical rule

Every G6 screen must be usable from launch to match start without a mouse.

---

# G6.23 - Loading, failure, reconnect and empty states

Modern UX is not just happy-path screens.

Define reusable states:

```text
Loading
Empty
Offline
Backend unavailable
Server unavailable
Version mismatch
Authentication expired
Ticket expired
Reconnect grace
Host left
Map missing
Replay incompatible
No servers found
```

Every asynchronous screen must have:

- loading state
- success state
- failure state
- retry or clear next action

Do not surface raw exception messages as the primary user experience.

Log detailed exceptions separately.

---

# G6.24 - Session reconnect UX

The current netcode supports reconnection mechanics.

Expose them coherently.

When connection drops:

```text
CONNECTION INTERRUPTED
Attempting to reconnect...
```

If within valid grace:

- request fresh authenticated ticket where necessary
- reconnect same PlayerId
- preserve lobby/match participant semantics

If grace expires:

- server determines forfeit policy
- UI reports result honestly
- user may rejoin as spectator/lobby participant if allowed

Do not let UI "cancel" an already authoritative forfeit.

---

# G6.25 - Host migration policy

Private lobby needs an explicit host-owner policy.

Recommended v1:

```text
server process owns authority
player host owns edit permissions
```

If host player leaves:

1. if other authenticated players remain:
   - transfer lobby-owner permission deterministically
2. if none remain:
   - server may shut down after configured idle timeout

Suggested transfer order:

```text
oldest eligible connected authenticated participant
then oldest eligible guest if guests may host
```

Tournament/dedicated servers have no player owner; server/admin owns configuration.

Document and test this.

---

# G6.26 - Lobby bots

Integrate existing `ServerBotManager`.

Lobby shows bots distinctly.

Host may configure:

```text
Bot fill on/off
Minimum participants
Skill
```

Rules:

- bot changes are server-owned
- bot roster changes update LobbySnapshot
- bots automatically ready
- entering match uses existing bot handoff
- official RP eligibility remains governed by G4 policy

If current official policy excludes any match containing a bot, lobby must clearly show:

```text
Ranking disabled: bots present
```

before match start.

---

# G6.27 - Lobby spectators

Observers appear in a separate group.

They can:

- chat according to policy
- view lobby
- select spectator preferences
- enter delayed/trusted spectator role according to server policy

They cannot:

- count toward ready
- choose gameplay team
- affect match rules unless admin/host permission explicitly allows it

---

# G6.28 - Ranked lobby behavior

Only after G4/S6 gates are complete.

Ranked lobby should be more constrained than private lobby.

Recommended:

```text
rules locked
map from server/match pool
teams server-assigned
manual team switching disabled
ready optional/automatic depending matchmaking flow
late join disabled
bots disabled
verified identity required
rating eligibility shown
```

Do not let Ranked reuse private-host controls with some buttons merely greyed out everywhere.

Give it a clean constrained view.

---

# G6.29 - Tournament lobby behavior

Reuse existing TournamentAdmin.

UI role:

```text
players see locked roster/team/map state
trusted operators receive admin controls
```

Do not duplicate tournament authority in client code.

Possible controls:

- ready check
- roster lock
- team assignment
- map selection
- ruleset
- start
- force spectator

Admin requests remain authenticated server commands.

---

# G6.30 - Server-browser / lobby phase reporting

Extend server status to report session state:

```text
Lobby
Countdown
Playing
Ending
Intermission
```

Where protocol space permits, expose:

```text
lobby player count
spectator count
joinability
ready/start state
```

Server browser can then answer:

```text
Join now
Join lobby
Spectate
Wait for next match
Full
```

rather than forcing players to guess.

---

# 6. G6 internal data contracts

Recommended conceptual contracts.

## LobbyPlayer

```csharp
public readonly record struct LobbyPlayer(
    PlayerId PlayerId,
    ulong ConnectionId,
    string DisplayName,
    Hunter Hunter,
    int Team,
    bool Ready,
    bool Observer,
    bool Bot,
    bool Host,
    bool Admin,
    int Ping,
    int StarTier);
```

Do not literally send Backend-only fields if unavailable or unnecessary.

Guest PlayerId handling must be explicit.

## LobbyRuntime

```csharp
public sealed class LobbyRuntime
{
    public uint SessionId { get; }
    public uint Revision { get; private set; }
    public LobbyPhase Phase { get; private set; }
    public LobbyPolicy Policy { get; }
    public LobbyRulesDraft Draft { get; }
    public IReadOnlyList<LobbyPlayer> Players { get; }
}
```

## Session coordinator

```csharp
public enum ClientSessionState
{
    Disconnected,
    Connecting,
    Lobby,
    LoadingMatch,
    InMatch,
    PostMatch,
    Leaving,
    Failed
}
```

Keep those contracts small.

Do not turn them into all-purpose application state bags.

---

# 7. Existing-file migration map

## `Client/Launcher/Gui/HomeView.cs`

Target:

```text
temporary compatibility shell
  ->
features extracted screen-by-screen
  ->
delete or reduce to thin adapter
```

Move responsibilities to:

```text
UI/Screens/Home
UI/Screens/Play
UI/Screens/ServerBrowser
UI/Screens/PrivateMatch
UI/Screens/Replays
UI/Screens/Account
```

## `Client/Launcher/Gui/AccountView.cs`

Migrate to:

```text
UI/Screens/Account
UI/Screens/HunterLicense
```

Reuse `AccountSession`.

## `Client/Launcher/Gui/MapPickerView.cs`

Migrate to:

```text
UI/Screens/Lobby/MapPicker
UI/Components/MapCard
```

## `Client/Launcher/Gui/ServerRow.cs`

Migrate/reuse logic as:

```text
UI/Components/ServerCard
UI/Screens/ServerBrowser
```

Keep portable filtering logic in `Launcher/Portable/ServerBrowser.cs`.

## `Client/Launcher/Gui/DemoPickerView.cs`

Migrate to:

```text
UI/Screens/Replays
```

## `Client/Launcher/Gui/SettingsView.cs`

Split by category into:

```text
UI/Screens/Settings/*
```

Do not rewrite setting semantics.

## `Client/Launcher/Gui/GuiLauncher.cs`

Retain as platform/application host but remove feature orchestration.

It should become primarily:

```text
Avalonia setup
AppShell lifetime
hide/show around RenderWindow
dispatcher pumping
```

## `Client/Launcher/Portable/MatchStart.cs`

Keep map/renderer launch mechanics.

Adapt to:

- session persists
- scene attach/detach
- return reason/result
- no automatic network disposal

## `Client/Runtime/Menu.cs`

Do not rewrite wholesale in G6.

Touch only where required to align pause/session UX.

Large gameplay menu refactoring can be a later isolated cleanup.

---

# 8. G6 client/server lifecycle

## Private session

```text
Create Private Match
    ->
start server process in lobby mode
    ->
client obtains/uses local admission
    ->
LobbyRuntime.Open
    ->
players join
    ->
ready
    ->
freeze MatchRules
    ->
MatchTransition
    ->
existing match Countdown
    ->
Playing
    ->
Ending
    ->
MatchSummary
    ->
LobbyRuntime.Open
    ->
rematch / next map
```

## Dedicated public server

```text
Lobby or Playing
    ->
join policy decides:
      player
      spectator
      wait-next-match
    ->
match
    ->
results
    ->
intermission lobby or auto rotate
```

## Practice

```text
Practice setup
    ->
localhost authoritative server
    ->
LobbyRuntime
    ->
bot fill
    ->
start
    ->
results
    ->
lobby
```

---

# 9. Protocol migration details

## Reliable events

Likely additions:

```text
LobbySnapshot
LobbyFeedback
MatchSummary
```

Client requests may use:

```text
LobbyRequest
```

as a reliable event type or a dedicated bounded request message.

Prefer existing reliable-channel infrastructure.

Do not create a second reliability protocol.

## Stale state

Every LobbySnapshot must include:

```text
SessionId
Revision
```

Client accepts only newer revision for active session.

Reconnect gets a full authoritative snapshot.

## Match boundary

Lobby state does not use fake MatchIds.

Match transition creates/announces the actual MatchId.

## Demo/replay

Lobby events do not need to be part of gameplay replay unless product requirements explicitly ask to replay pre-match lobby behavior.

Do not bloat `.fpdemo` with lobby chat/account information.

---

# 10. UI performance requirements

## No large per-frame allocations

Lobby UI is event-driven.

Do not rebuild the complete player list 60 times per second.

Update when:

- LobbyRevision changes
- local timer/ping display requires refresh
- animation needs render frame

## Thumbnail caching

Bound map/replay preview cache.

## Backend calls

Hunter License/history:

- async
- cancellable
- paginated
- cached with reasonable lifetime
- never block UI thread

## Server browser

Ping/query asynchronously.

Never freeze the AppShell while querying many endpoints.

---

# 11. Testing plan

# 11.1 Unit tests

Add tests for:

```text
UiRouter
back-stack behavior
LobbyRulesDraft validation
Lobby permissions
Lobby ready calculation
host migration
LobbyRevision ordering
Lobby snapshot codec
Lobby request codec
ranked control restrictions
team request validation
Hunter selection validation
server phase reporting
post-match summary
session state transitions
```

# 11.2 Integration tests

Real client/server tests:

```text
join lobby
ready
start
match
results
return lobby
rematch
```

Additional:

```text
host leaves lobby
player disconnects lobby
player reconnects
observer joins
bot added/removed
map changed
rule changed
invalid client rule request rejected
tournament lock prevents request
```

# 11.3 Network impairment

Lobby mutations under:

```text
loss
jitter
reorder
duplicate
```

Because LobbySnapshot is reliable/full-state:

- client converges
- no duplicate UX action
- revision order correct

# 11.4 UI automated/imaging tests

Add screenshot/layout tests for:

```text
Home
Play
Lobby FFA
Lobby teams
Lobby with 8 players + observers
Server browser
Hunter License
Post-match
Replay library
Settings
```

At:

```text
1280x720
1920x1080
2560x1440
3440x1440
compact phone-like logical viewport
tablet logical viewport
```

Use imaging tests for layout regressions.

# 11.5 Controller automation

Simulate focus path from:

```text
Home
  ->
Play
  ->
Private Match
  ->
Lobby
  ->
Hunter Select
  ->
Ready
```

without mouse.

# 11.6 Android physical acceptance

Required.

# 11.7 End-to-end soak

After G6:

```text
1+ hour
persistent session
several matches
map changes
lobby returns
bots
observers
replays
telemetry
Backend brief outage
Hunter License refresh
```

Verify no socket/session leak from repeated lobby/match transitions.

---

# 12. G6 implementation passes

Do not build G6 in one branch.

Recommended order:

```text
G6.0   design-system specification
G6.1   UI navigation/AppShell foundation
G6.2   persistent ClientSessionCoordinator
G6.3   Lobby domain + wire codec
G6.4   ServerLobby integration
G6.5   client lobby state + basic lobby screen
G6.6   ready/Hunter/team/chat
G6.7   private match + Practice lobby flows
G6.8   Home + Play overhaul
G6.9   Server Browser overhaul
G6.10  Hunter License overhaul
G6.11  post-match -> lobby loop
G6.12  replay library/viewer shell
G6.13  settings/accessibility overhaul
G6.14  pause/session overlay integration
G6.15  Android responsive/touch acceptance
G6.16  migrate/delete legacy HomeView feature sections
G6.17  full integration / soak / docs
```

Each pass should be independently buildable.

---

# 13. Commit plan for G6

Suggested commit boundaries:

```text
ui: add app shell and navigation primitives

net: add persistent client session coordinator

game: add lobby domain contracts

protocol: add bounded lobby wire messages

server: add authoritative lobby runtime

client: add lobby state and basic lobby screen

lobby: add ready hunter team and chat flows

launcher: route private/practice through persistent lobby

ui: replace home and play screens

ui: overhaul server browser

ui: overhaul hunter license

ui: add post-match lobby flow

ui: overhaul replay library

ui: split settings and accessibility screens

android: adopt shared responsive app shell

cleanup: remove migrated HomeView feature code

test: add G6 integration and imaging coverage

docs: record G6 acceptance
```

Avoid combining protocol/server/client/UI in one enormous commit where possible.

For a vertical feature requiring all layers, use a small sequence of commits that leaves the branch buildable.

---

# 14. Explicit non-goals

Do not add during stabilization/G6 unless separately approved:

- AMHE1 gameplay-fidelity remediation
- weapon rebalance
- Hunter rebalance
- 120 Hz server simulation
- rollback netcode
- peer-to-peer authority
- voice chat
- friends/party platform
- clan/guild system
- marketplace
- battle pass
- seasonal progression
- achievements without an approved domain
- full matchmaking service if only server discovery currently exists
- microservices
- Redis by default
- message broker by default
- web frontend
- namespace-wide `MphRead` -> `PrimeHunters` rename
- Avalonia major-version upgrade
- rendering engine rewrite

Parties/friends may be a later G7-style social epic after the lobby proves stable.

---

# 15. Definition of done: G1-G5 Stabilization

G1-G5 is stabilized when:

## Source

- all G1-G5 implementation is committed
- unrelated changes preserved separately
- generated build artifacts excluded

## Automated

- all main tests pass
- all Backend tests pass
- all imaging tests pass
- Python tests pass
- project-boundary guard passes
- solution builds
- Server publishes
- Android managed Release builds

## G1

- high-refresh real render acceptance passes

## G2

- visual/audio/physical Android acceptance passes

## G3

- external WAN acceptance passes
- no open P0/P1 match-flow defect

## G4

- RP/star policy approved and implemented
- forfeit semantics explicit
- career projection semantics explicit
- Backend rate limiting fixed
- production readiness documented
- Ranked either secure enough to enable or explicitly disabled

## G5

- one-hour combined soak passes
- 30-second / 16-observer delay test passes
- replay/telemetry/outbox recovery works

---

# 16. Definition of done: G6

G6 is complete when:

- one persistent AppShell drives desktop and Android launcher UI
- HomeView no longer owns unrelated product workflows
- player can remain connected across:
  - lobby
  - match
  - results
  - lobby
- server owns LobbyRuntime
- lobby requests are validated authoritatively
- private match uses lobby
- Practice uses lobby + authoritative bots
- team/Hunter/ready/chat flows work
- server browser reports lobby/match state
- Hunter License is integrated into the shell
- post-match results return to lobby without tearing down session
- replay library/viewer UX is integrated
- Settings is decomposed and responsive
- all G6 screens are keyboard/controller usable
- all applicable screens are Android touch usable
- accessibility gates pass
- one-hour persistent lobby/multi-match soak passes
- no network/session/thread/resource leak appears across repeated match transitions
- protocol 8 is frozen/released only after all final wire contracts are stable, or protocol 9 is used if 8 was already released

---

# 17. Final post-G6 baseline

Create:

```text
docs/G6_ACCEPTANCE.md
```

Record:

- final commit
- protocol
- replay format
- Backend migration
- rating policy
- desktop screenshots
- Android screenshots
- controller acceptance
- WAN acceptance
- persistent-session soak
- known limitations

Then freeze that baseline.

Only after this point begin:

```text
AMHE1 Gameplay Fidelity Audit
```

against the stable Prime Hunters gameplay/runtime.

---

# 18. Instructions for the AI implementation agent

1. Read the existing implementation before editing.
2. Do not recreate systems that already exist.
3. Keep Server authoritative.
4. Keep LobbyRuntime separate from MatchRuntime.
5. Keep UI presentation separate from gameplay truth.
6. Preserve unrelated working-tree modifications.
7. Never use destructive git cleanup commands.
8. Keep every protocol field bounded and validated.
9. Do not add unbounded strings/collections to UDP messages.
10. Use the existing reliable channel for lobby state where practical.
11. Prefer full bounded LobbySnapshot updates over complex premature delta replication.
12. Do not block the 60 Hz simulation thread on Backend, file, UI, or HTTP operations.
13. Do not block the Avalonia UI thread on network or Backend calls.
14. Keep one client network session across lobby/match transitions.
15. Do not run two concurrent poll owners for the same NetClient.
16. Keep existing MatchLifecycle countdown as the actual match countdown.
17. Reuse TeamAllocator, ServerBotManager, ServerVoteSession, TournamentAdmin and current rules validators.
18. Keep fixed-six-tick remote interpolation during this program.
19. Do not change Classic weapon/Hunter balance.
20. Do not begin AMHE1 fidelity remediation.
21. Add tests with every domain/protocol behavior change.
22. Add visual/physical acceptance evidence for UI work.
23. Keep commits narrow and reviewable.
24. Update docs in the same pass as behavior.
25. If a policy is unresolved, stop that narrow feature at an explicit gate instead of inventing product behavior.

---

# 19. Recommended immediate next action

Do **not** start coding LobbyRuntime first.

The next implementation action should be:

```text
S0 - checkpoint and normalize the current G1-G5 working tree
```

followed immediately by:

```text
S1 - freeze the stabilization baseline
```

Once the current implementation is safely committed and reproducible, execute S2-S8.

Only then begin G6.0/G6.1.

That sequence minimizes the chance that a UI/lobby overhaul hides or destabilizes a G1-G5 integration defect.
