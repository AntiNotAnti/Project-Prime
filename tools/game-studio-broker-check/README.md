# Game and Studio broker gate

Run `dotnet run --project tools/game-studio-broker-check -c Release` with .NET 10.

This asset-free check builds actual immutable `.ppmap` versions, starts an
authenticated current-user Game endpoint, and executes the production game
broker and package installer on a dedicated owner dispatcher. It verifies exact
MapId/ContentHash/PackageHash publication, canonical model decoding, active
preparation rejection with byte-identical runtime/package files, successful
release/retry, substituted-version rejection, private audit output isolation,
application-version mismatch before mutation, duplicate launch IDs, status and
stale stop handling, cancellation, and narrow `ppm1` ticket filtering.

The owner launch callback is observed here without creating a graphics window.
The production adapter queues the existing offline `LaunchPlan` and watches the
real game scene for Started/Ended/Rejected state. Native gameplay and independent
application survival are covered by the desktop lifecycle gate.

`tools/studio-ipc-check` separately verifies wire framing, authentication,
malformed peers, disconnect/cancellation, and crash/reconnect. The publication
tool adds kernel lease regression checks with independent processes.
