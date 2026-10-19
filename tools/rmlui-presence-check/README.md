# Native Presence controller acceptance

Run from the repository root with the pinned .NET SDK:

```sh
dotnet run --project tools/rmlui-presence-check -c Release
```

This small project links the actual portable Presence and Social identity
validation sources. It builds no game project, starts no renderer, and uses no
account, settings storage, network service or real session. A fake TimeProvider
advances heartbeat and expiry deadlines without waiting in real time. The fake
service deliberately allows late completions after cancellation so stale-result
fences are exercised.

Coverage includes owner-thread access, immediate initial advertisement followed
by 15-second heartbeat cadence, early activity transitions, single-flight work,
refresh cancellation and private-state clearing, directory and individual-row
45-second expiry, malformed service payloads, privacy expected/next/account
witnesses, error redaction, copied source arrays, and disposal during idle,
refresh, privacy changes and heartbeats. Leave cleanup must retain only the
previously advertised account/session witness. Hidden/expired users are not
invented as offline rows.

`tools/rmlui-opengl-check/PresenceAdapterAcceptance.cs` separately exercises the
production REST parser and serialized privacy witnesses without sending HTTPS.
