# Lobby controller contract check

Run `dotnet run --project tools/lobby-controller-check -c Release` from the repository root. The check uses the production controller with a fake engine-service boundary and requires no game data, graphics device or network transport.

It verifies clock ownership, monotonic tick tokens, reentrant callbacks, owner-thread access, immutable snapshot versions, late-join loading and server-aborted starts, explicit gameplay clock handoff, rule confirmation and rejection, permission/revision/lifetime checks, retained admin slot-generation witnesses, team capacities, bot configuration, server-confirmed identity and spectator changes, protocol chat limits, and 50 create/leave cycles.

The Hunter selection checks use the real cosmetic catalog through a fake persistence/account boundary. They verify every cosmetic choice, per-Hunter drafts, explicit unavailable/locked states, preview controls, live identity confirmation, local equip before account synchronization, retry after remote failure, immutable presentation, and late completion after disposal. Real online lobbies, rendering/resource checks and physical device checks remain separate acceptance gates.
