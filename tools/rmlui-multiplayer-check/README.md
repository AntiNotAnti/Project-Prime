# Native multiplayer contracts

Run with the native client build flags:

```
dotnet run --project tools/rmlui-multiplayer-check -p:MphReadRmlUi=true
```

The checks inject the matchmaking authority boundary and use actual transferable
admission handles with playback transports. They cover party-capacity search,
reserve-before-join ordering, row/manual/spectator joins, party membership changes,
request-scoped cancellation, Social authority epochs, late connection completions,
presenter retirement, safe errors, and community/Studio map-form state. They do not
authenticate, send messages, access Supabase, or connect to a production server.

Run the local, real UDP waitlist checks separately:

```
dotnet run --project tools/rmlui-multiplayer-check -p:MphReadRmlUi=true -- --live-queue
```

These fill a disposable local dedicated server, join and cancel its queue, accept
an offered seat, and transfer the existing queued transport into `NetSession`.
They also cancel after the server has promoted the seat but before the client has
observed its Welcome, checking immediate seat cleanup. The fixture suppresses
identity ticket loading and never writes launcher preferences or connects to
account services. It uses the existing explicit control-plane bootstrap fixture;
it does not load or render a gameplay scene.
