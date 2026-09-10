# Authoritative map telemetry

Enable optional telemetry through the Node's Worker configuration: the Node
passes an absolute Worker `ArtifactDirectory`, and a frozen `MatchSpec` requests
`TelemetryPolicy.Record`. Each Node-owned Worker collects only its own resolved
match gameplay. It samples live positions once per second and records
spawn, damage, kill/death and world transitions. Collection starts with Playing
and stops after the terminal tick. Waiting/countdown/intermission are not route
samples or match duration. Initial spawn facts created during countdown are retained
in eight fixed slots and timestamped at the start of Playing for spawn-safety
intervals; their original spawn positions are preserved.

A fixed array holds at most 131,072 records. Full or nonfinite input increments
`droppedEvents`; it does not expand storage or stall simulation. At match end or
Worker shutdown, the owning Worker transfers one array copy to a two-match
background writer queue. JSON
serialization, gzip compression and file operations run on that worker. Exports
use a fresh `.partial` file and atomic rename to `.telemetry.json.gz`. Write
failure retains a partial file and logs a failure. Queue overflow is explicitly
logged as dropped telemetry. These optional exports do not replace the durable
match report outbox. Operators own directory retention.

Only file-local slot/life identifiers are exported, with no account IDs, network
connection IDs, IP addresses or player names. When a canonical report exists,
the telemetry file ID equals its match UUID; otherwise it is a random match UUID.
Coordinates are original map units, retaining X, Y and Z. Kill/death heatmaps use
the victim's location. Spawn LOS is a collision query at the observed spawn, and
average enemy distance considers active living opponents only. Missing distance
(no opponents) is null, not zero.

## Analysis commands

Use the Tools executable:

```sh
ProjectPrimeTools telemetry heatmap INPUT.telemetry.json.gz OUTPUT_PREFIX
ProjectPrimeTools telemetry spawn-safety INPUT.telemetry.json.gz OUTPUT_PREFIX
ProjectPrimeTools telemetry routes INPUT.telemetry.json.gz OUTPUT_PREFIX
ProjectPrimeTools telemetry weapon-control INPUT.telemetry.json.gz OUTPUT_PREFIX
```

Each command writes an SVG, JSON summary and event CSV. SVGs use four-unit X/Z
cells. Routes show sampled occupancy; heatmaps show kills; spawn-safety shows
deaths alongside the JSON's spawn danger table. Weapon-control includes pickup
counts, weapon damage share, kills after pickup and average pickup-to-kill time.
The spawn table reports repeated location counts and deaths within 3/5/10 seconds,
visible-enemy spawns and average enemy distance. Objective summaries retain
transition counts, sampled flag carry distance and node contested duration.

The parser limits decompressed input to 64 MiB and validates event count, finite
coordinates, bounded values and match-local tick ranges before output. It accepts
tick wrap and stably orders independently drained journals by their relative tick.
No game content extraction is required to analyze an existing file.

## Interpretation

Sampling approximates route length and activity duration. Unseen space is not
proof of walkable unused space without map geometry. A dropped or incomplete
file is not a complete balance baseline. Attacker Hunter identity is unknown if
the original combat identity is no longer sampled; it is not guessed from a new
occupant. Hunter win/pick rates and team bias require validated match reports;
telemetry alone cannot prove outcomes. No Competitive balance tuning is applied.

Focused tests and actual runtime export evidence are recorded in the program
progress log as they complete. A build alone does not prove map quality or balance.
