# RmlUi reintegration: RML-03 Home / Play / Lobby services

**Status:** desktop developer build only. The current Avalonia client remains
the default, and Android is not switched. Historical page artwork and CSS
remain intact.

Source: latest main after RML-02 (#657); runtime gate:
`-p:MphReadRmlUiGlSmoke=true` plus `PROJECT_PRIME_RMLUI_GL_SMOKE=1`.

## Current slice

- Consume real `HubState` for player name, selected hunter and game-file readiness.
- Bind native Home / multiplayer browser / authoritative lobby RML data through
  revisioned `RmlUiBindingSnapshot`; only changed snapshots cross the native bridge.
- Reuse `ServerBrowserService.DiscoverAsync`, `FindBestAsync`, and `JoinAsync`,
  with cancellation/epoch fencing, UI-thread completion, incompatible-server
  filtering and bounded eight-row display.
- Hand admitted connections to the existing `StartScreen.ConnectedOrFinished`
  path. Its `LobbySessionCoordinator` remains the only UI-owned lobby pump.
- Lobby roster, ready flags, owner, arena, format, map preparation and start
  requests use `NetSession` authoritative state. No optimistic ready acknowledgements.
- Native actions: Ready, Start, Leave and legacy handoff for detailed lobby
  administration, Hunter selection, and rules editing.
- Existing create lobby, Settings, Hunter License, Community, Offline and Studio
  screens stay reachable through guarded legacy navigation.
- Suppress native presentation/input over startup PRESS START, setup, confirmation
  modals, legacy workspaces and gameplay; resume on Home/Lobby.

## Checks

`dotnet run --project tools/rmlui-live-menu-check -c Release`
covers snapshot stability, incompatible directory filtering, admission handoff,
authoritative ready/start, disconnect routing, cancelled results, invalid actions
and owner-thread requirements with a deterministic fake service.

CI also compiles the opt-in client and runs existing netcode/renderer gates.
This is not yet live matchmaking, 2/4/8-client rendered parity, Android
lifecycle, IME/accessibility, or a shipping-default cutover.

## Next slice

RML-04: native create-lobby and rules, full Hunter selection, ready validation,
2/4/8-client integration and map-preparation progress; then Community, License,
Settings and Studio native client navigation. Keep Avalonia until parity.
