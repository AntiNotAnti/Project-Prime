# Online bots

Persistent online lobbies support owner-managed bots before and during a round. The lobby's **Manage Bots** panel and the owner's pause-menu panel select hunter (including server-resolved Random), suit, team and Easy/Normal/Hard/Insane difficulty. Updating a bot replaces that slot's lifecycle, so its former combat state is not retained.

Bots occupy ordinary server slots without creating network peers. Human and bot occupancy shares the eight-slot/server limit and team capacities. Readiness, ownership, voting, connection health and directory human counts remain human-only. Joining humans use free slots; automatic eviction of a bot is intentionally deferred.

The authority activates existing entities through the roster and loads each bot's existing offline personality independently. Input-edge history and charge-release capture are isolated per bot. Bot inputs use the normal SlotIntent payload and cadence, with a current authority firing pose for continuous weapons and no shooter rewind. Snapshots and target history include bots normally. Clients and replay replicas never run AI.

## Tactical AI layer

The native MPH personality tree remains the navigation/objective backbone, but authority bots now run a deterministic tactical layer after each legacy decision. The layer scores visible/known targets, gives extra weight to objective carriers and recent attackers, applies target stickiness and teammate focus spreading, chooses weapons by range/safety/affinity rather than random availability, and drives range-aware advance/retreat/strafe movement. Movement probes reject walls, missing floor and damaging terrain, with stuck recovery before the legacy random-strafe fallback.

Perception keeps firing fair: geometric LOS and cloak/radar rules gate precision target acquisition and aim. Recent damage records an attacker location, and nearby unseen gunfire creates a low-confidence investigation cue; neither path permits through-wall firing. Difficulty changes tactical reaction cadence, memory and decision quality in addition to the existing aim/prediction profile, so Insane is not only a faster Hard aim profile.

Enhanced-hunter choices still run after the shared tactical layer, preserving hunter-specific combat behavior for Samus, Kanden, Trace, Sylux, Noxus, Weavel and Spire.

Add, remove and update advance slot generations. Ordinary lifecycle cleanup removes cached input, weapon state, history and scores. The per-bot capture state also resets at spawn. Custom maps must pass generated navigation validation before bots can be added.

A server-side practice latch is initialized from the bot roster when a new round starts, and is set by any active-round bot insertion. Removing bots cannot clear it. Rosters carry this flag to late joiners; reports and the lobby display practice status. Career reporting and the report outbox reject bot-assisted reports. The Edge Function and the accompanying SQL migration independently reject such reports before accepted-match/history/stat writes. The migration and Edge Function require deployment through the normal release process.

The combined workspace uses protocol 27 (custom-map identity, bot roster fields, and the separate player-name wire update). Clients with older protocols are refused. Replay metadata, replica rosters, checkpoints and library caches preserve bot identity and difficulty; playback consumes recorded facts.

## Verification

Run with the repository's .NET 10 SDK:

```sh
dotnet run --project tools/nettest -- --bots
dotnet run --project tools/nettest -- --bot-ai
dotnet run --project tools/nettest -- --lobby
dotnet run --project tools/nettest -- --architecture
dotnet run --project tools/nettest -- --bots-scene /path/to/game-config 'MP1 SANCTORUS' 7
dotnet run --project tools/nettest -- --bots-online /path/to/game-config 'MP1 SANCTORUS'
dotnet run --project tools/nettest -- --continuous-scene /path/to/game-config 'MP1 SANCTORUS'
```

The config directory must provide working game-data paths. The bot scene check verifies spawning, movement, firing, damage, removal, slot reuse, insertion and client AI suppression, and prints step timing/allocation measurements. The online test uses a real dedicated simulation and two UDP clients, then adds a third client after removing a bot. The control-plane test covers permissions, capacity, readiness and practice-latch reset between rounds. Replay-format checks additionally cover metadata and checkpoint persistence.

`supabase/tests/bot_assisted_career.sql` exercises the database ingestion guard without creating career records. Rendered multiplayer acceptance (animation/POV fidelity across two visible clients), every map/mode combination and production database deployment remain release checks; headless tests do not establish those results.
