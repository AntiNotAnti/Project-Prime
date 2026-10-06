# Studio foundation lifecycle check

```sh
dotnet run --project tools/studio-lifecycle-check -c Release
```

Checks exact CLI parsing, bounded and atomic settings/session persistence, recents, missing/malformed state, generic dirty-document save/cancel/discard/Save As ownership, read-only source inspection, job cancellation and awaited shutdown.

Child processes run the production authenticated `StudioInstanceGuard` with temporary installation/user-data paths. They exercise second-launch forwarding, independent instance scopes, abrupt process disappearance, fresh endpoint replacement/reconnect and clean descriptor cleanup. The peer processes are test hosts, not the game. Dirty-document tests use a fake lifecycle document, not a second map/replay implementation.

`--native` additionally runs the production Avalonia `App` and `StudioWindow` with the classic desktop lifetime in child processes, then launches the actual `ProjectPrimeStudio` executable to forward a source document. This needs a desktop display (or a configured virtual display on Linux). It tests normal window shutdown, isolated profile coexistence, crash survival, explicit restart recovery and endpoint cleanup. The independent peer is another Studio profile; this does not substitute for the separate game/playtest broker gate.

This is a Phase 1–2 foundation check. It does not claim the game/Studio playtest lifecycle, extracted map/replay editing, viewport device loss or full editor parity.
