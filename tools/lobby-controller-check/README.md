# Lobby controller contract check

Run `dotnet run --project tools/lobby-controller-check -c Release` from the repository root. The check uses the production controller with a fake engine-service boundary and requires no game data, graphics device or network transport.

It verifies clock ownership, monotonic tick tokens, reentrant callbacks, owner-thread access, immutable snapshot versions, late-join loading and server-aborted starts, explicit gameplay clock handoff, rule confirmation and rejection, permission/revision/lifetime checks, and 50 create/leave cycles. Real online lobbies and physical device checks remain separate acceptance gates.
