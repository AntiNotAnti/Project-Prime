# G6 lobby and UI acceptance

The final tree contains the requested G6 foundation and server-owned lobby slice
at `1c8df59`. This record describes implemented source behavior, completed
automated evidence, and the remaining owner-assumed gates. Physical Android,
high-refresh, audio/touch, and long-device tests remain administrative assumptions
under the stabilization disposition.

## Implemented client shell

`AppShellView` and `UiRouter` provide a shared Avalonia shell implementation for
desktop and Android with route history, modal history, focus restoration,
keyboard/controller directional focus, compact/medium/wide breakpoints, safe-area
and accessibility preferences, visible loading/empty/error states, and a
persistent session badge. `AndroidApp` now creates the shared `ClientUiRuntime`
and roots `MainView` at `Runtime.Shell`, so Android and desktop use the same shell
and screen factory. The managed Android build proves compilation/adoption only;
it is not physical-device evidence.
The top-level routes are Home, Play, Hunter License, Replays, and Settings. Play
opens Quick Play, Server Browser, Private Match, Practice, or Ranked. Ranked is
shown with an explicit unavailable reason when its prerequisites are not met and
never silently falls back to unranked play.

The screen factory currently covers Account, Home, Play, Lobby, Server Browser,
Private Match, Hunter License, Replays, Settings, and Post-match Results. Screen
models keep request intent separate from authoritative state; they do not claim a
server-side mutation succeeded before the returned state changes.

## Persistent session behavior

`ClientSessionCoordinator` owns the client session through:

```text
Disconnected -> Connecting -> Lobby -> LoadingMatch -> InMatch
             -> PostMatch -> Lobby
```

`Leaving` and `Failed` are explicit terminal/error paths. `SessionPump` gives
polling to the shell while the lobby is visible and to the match loop while a
scene is attached; teardown waits for an in-flight poll before disposing the
session. Returning to a persistent lobby therefore keeps the admitted connection
and hosted server alive until the player leaves, the server disconnects, or the
application exits.

## Server-owned lobby and wire contract

`LobbyRuntime` owns only session ID, revision, policy, draft rules, players,
observers, host/admin permissions, completed match ID, and vote revision. It does
not own gameplay entities, score, health, damage, or the simulation clock.
`ServerLobby` validates admission, reconnect reservations, bounded map/mode/rule
changes, ready/start/return/rematch requests, team/lobby chat, bot fill, and
post-match summary publication on the single server owner thread.

Reliable protocol-8 records are fixed and revision-addressed:

- `LobbySnapshot` projects at most 8 players and 16 observers, with explicit
  guest/registered/bot identity and ready/observer/host/admin flags.
- `LobbyRequest` carries session ID, expected revision, request ID, and a bounded
  operation (`SetReady`, Hunter/team/rule/bot selection, start, return, or
  rematch).
- `LobbyFeedback` distinguishes stale session/revision, duplicate, invalid,
  permission, capacity, phase, conflict, unsupported, and rate-limited results.
- `LobbyChat` is scoped to lobby or team and remains bounded and server-routed.
- `MatchSummary` carries the completed match rows and rating state before the
  client returns to the lobby.

Stale revisions, duplicate request IDs, invalid phase transitions, and missing
permissions are rejected. Rules are frozen before the match transition. Private
host policy is persistent lobby + ready required + one-player minimum + host force
start; bots are ready automatically and are never lobby hosts.

## Private hosting, Practice, and host capability

Directory-hosted private sessions carry validated `MatchRules`, lobby policy,
ready/minimum-player settings, bot population/skill, and observer settings. The
directory returns a nonce-matched 128-bit owner capability. The launcher passes it
in the first gameplay join; the server checks the source IPv4 address and consumes
the capability once. If no authenticated owner is present, host migration prefers
authenticated humans in join order, then guests; bots and observers never become
host.

Practice starts an unlisted loopback-only authoritative child with bots and strips
Backend/ticket/report environment. Practice rules are unranked. The capability is
a private lobby host permission only; it is not a Ranked session proof-of-possession
mechanism and does not change the public Ranked security decision.

## Backend and rating presentation

The Hunter License and post-match contracts expose server-authoritative points,
tier, title, next threshold, and last official delta. `PairwiseNormalizedV1`
freezes eligible registered-player balances in one transaction, stores pair
contributions and before/after values, normalizes at most three opposing pairs,
truncates once toward zero, and clamps final points to 0–850. Retail star thresholds
remain 0/40/140/390/750 with 850 as the maximum. Community, private, practice,
tournament-disabled, bot, guest, invalid, and incomplete reports are explicitly
ineligible with a reason. Report schema 2 stores explicit participant outcome
reasons, and `LastOfficialMatchId` is durable and exposed through the rating,
license, and career contracts.

Public Ranked remains intentionally disabled under Path B until authenticated UDP
session proof-of-possession is implemented. The UI must keep the explicit disabled
reason and must not route the action to Quick Play or Practice.

## Evidence and acceptance boundary

The current tree includes focused tests for UI routing/focus, responsive models,
private-match validation, lobby authority/admission, lobby packet round trips,
status v4, persistent session ownership, rating calculation/persistence/rebuild,
and Backend security. The final validation includes 14/14 focused UI acceptance
tests and 68/68 PNG captures at 1280x720, 1920x1080, 2560x1440, 3440x1440,
360x640, and 768x1024. The captures cover Home, Play, populated lobby states,
Server Browser, Private Match, Hunter License, Post-match, Replays, Settings,
and the controller/focus path through Home → Play → Private Match → Lobby →
Hunter Select → Ready. The broader Release record is 1122/1122 main tests with
extracted AMHE1, 198/198 Backend tests against isolated PostgreSQL with no skips,
Imaging 18/18, Python 58/58, zero boundary violations, a successful Server
publish, a zero warning/error Android managed arm64 Release build, and no
vulnerable packages.

Bot fill remains server-owned: bots are ready automatically, never become lobby
hosts, and never receive or contribute official rating. Lobby chat mute is a local
client projection keyed by sender identity; server administrator mute is the live
chat gate. Live movement, combat, score, result, and lobby state remain owned by
the server, and delayed observers never fall back to live state. Public Ranked
remains disabled under Path B until authenticated UDP session proof-of-possession
is implemented.

The owner-assumed physical Android/high-refresh/audio/touch and long-device gates,
the 30-second delay with 16 observers, and the combined bots + observers + replay
+ telemetry + Backend-outage endurance run are administrative acceptance only. No
device log, high-refresh trace, combined endurance metric, or 30-second-delay
measurement is claimed by this document.
