# RML-06: Native Create Lobby with existing authoritative server allocation

## New native form

The existing RmlUi Play screen now handles CREATE LOBBY, arena cycling, mode
cycling, and hosted-versus-local-dedicated selection in its native controls.
Player/lobby-name inputs are passed to the current launcher service.

## Correctness invariants

- Only current built-in compatible arenas are permitted in this first slice.
- The current `LobbyCreationService` uses `NetMasterClient.FindHosts` to
  discover real authoritative HostPool servers. The host request carries
  the same room, rotation, time/goal and Lobby session policy as the legacy UI.
- Local hosting starts the existing `LocalServer` dedicated child process.
  Android cannot use that branch; no client-authority fallback is allowed.
- Server allocation and client admission are separate. Do not show a created
  lobby until NetLaunch.Connect succeeds. Admission then returns the plan
  to the existing StartScreen/LobbySessionCoordinator only once.
- Host discovery and preallocation are cancellable. After a remote process
  has been asked to allocate a game, a temporary Hide action must not make
  the authoritative server allocation disappear.
- Successful sessions alone update persistent launcher preferences.
- The legacy CreateServerScreen shares InitialMatchLimits through the
  portable service, reducing divergence in lobby defaults.

## Automated tests

`tools/rmlui-live-menu-check` exercises native creation flow, safe mode and
host choice, duplicate-submit rejection, immutable request transmission,
and one admitted handoff using a deterministic fake service. Existing lobby
netcode, headless server and OpenGL native frame acceptance still run.

## Remaining parity

Full Community package map picker/rotation, host-region selection, remote
host fallback and actual multi-client integration acceptance require physical
or networked runners. Legacy CreateServerScreen remains available as fallback
until these pass. Avalonia remains the shipping default.
