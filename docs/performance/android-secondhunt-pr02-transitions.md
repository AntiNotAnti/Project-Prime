# PR-02: Android transition GC policy

SecondHunt's room-loader experiments identified expensive forced full collections as a source of
mobile stalls. Project Prime still called `TransitionGcDiagnostics.Collect(SceneLoad)` from
`Scene.OnLoad` and performed a **blocking compacting Gen2** collection unconditionally.

This change targets Android **only**, without altering the desktop/server collection policy
or the existing observer-driven diagnostic experiments.

On Android, `SceneLoad` does not invoke an unconditional forced Gen2 collection.
If `GC.GetGCMemoryInfo()` reports at least 90% of its high-memory threshold in use,
it requests an **optimized nonblocking noncompacting** Gen2 collection; the GC can
still choose its own scheduling. An unavailable threshold does not trigger this
proactive request. This is a GC request, not a guarantee of nonblocking behavior.

The scene loading, resource ownership, graph, replay, multiplayer, and cleanup
paths remain untouched. RoomPrewarm and asset residency already exist; this
change does not add a second preparation system.

## Validation
- `dotnet run --project tools/nettest -c Release -- --scene-lifetime-check`
  (or run the existing transition GC/retained-graph checks in their CI jobs).
- Android: measure multiple load/unload cycles in campaign, 1v1 and 8-player
  bot sessions, background/resume, repeated maps, and low-memory conditions.
- Compare loading tail latency, Android GC pause, native RSS, managed heap,
  and any new OutOfMemoryException. Do not declare speedups from this patch
  without physical-device A/B evidence.
- Retain the default desktop collector policy and the observer's original calls.

Rollback is confined to `TransitionGcDiagnostics.Collect`'s Android branch.
