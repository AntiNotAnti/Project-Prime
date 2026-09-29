# Replay implementation progress

Updated 2026-09-28. The supplied replay plan is still in progress; this document distinguishes implemented code from unverified acceptance criteria.

## Implemented and reviewed

- Final-kill eligibility compares the kill's server tick with the authoritative match-end tick. Recording frames are used for the frozen playback interval. The authority wait uses a separate local timer.
- Replay semantic marker v2 carries killer life while retaining v1 decoding. Authority capture retains the damage/launch life instead of looking up a potentially respawned attacker. Packets without this evidence retain life zero and the documented legacy generation-only behavior.
- Killcams resolve the attacker's generation and available life, hold a recent attacker camera briefly, then use a cinematic fallback. First-person camera position and FOV use presentation interpolation.
- Presentation time is fractional, with player-pose sampling in the recording clock and camera history reset on seeks or subject changes. Director scoring refreshes the current subject, applies cooldown before candidate selection, and resets across seeks.
- Camera tracks bind to logical replay identity, content hash, and clip range; v1/v2 tracks remain readable. Cache-owned tracks migrate before cache removal or rebuilding. Camera edits roll back in memory if durable saving fails.
- Camera controls support selecting/dragging keys, copying, pasting, duplicating the preceding key, snapping to events/kills/bookmarks, and Hold interpolation.
- Central artifact cleanup covers the library and virtual clips. Move collisions are checked before mutation, and failed moves roll back. Active/favorite virtual clips are protected during storage cleanup. Same-named clips in different directories have separate caches.
- Path comparison preserves case on Linux and macOS, conservatively avoiding collisions on case-sensitive macOS volumes. Windows uses case-insensitive comparison.
- A rational export sampler supports 24/30/48/60/90/120/144 FPS, sharing one render path. Existing inclusive 30/60/120 sample counts are retained. The UI, presets and export validation use the same rate list. Renderer advancement queues at most one step per observed simulation frame.
- Timeline shot/spawn lanes, event selection, and the Combat Inspector expose recorded evidence. The inspector navigates all shots and labels nearby events as unlinked; it does not infer per-shot CombatAck or damage settlement.

## Review fixes

The review corrected mixed server/recording clocks in final-kill eligibility, an authority timeout that mixed clocks, a recording-time clamp against visible duration, false success after failed camera saves, partial artifact moves, unmigrated camera loss during cache cleanup, virtual-cache collisions across directories, active/favorite clip deletion, stale FPS UI mappings, director cooldown ordering, and queued single steps surviving a seek.

## Validation

A temporary .NET 10 SDK is available at `/tmp/prime-replay-dotnet/dotnet`; the repository's `global.json` was not changed.

Run the desktop build and asset-free checks with:

```sh
/tmp/prime-replay-dotnet/dotnet build src/MphRead/MphRead.csproj --no-restore
/tmp/prime-replay-dotnet/dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -replaycontrolcheck
/tmp/prime-replay-dotnet/dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -replayformatcheck
/tmp/prime-replay-dotnet/dotnet run --project tools/replay-timeline-check
/tmp/prime-replay-dotnet/dotnet run --project tools/nettest -- --protocol19
```

These passed during this review: desktop compilation, replay controls/camera/hardening/review checks, 2,716 format checks, 43 timeline checks, and 143 protocol combat/telemetry checks. The desktop build reports 21 warnings outside the replay files changed here.

No recorded `.ppdemo` fixture or extracted game assets were found in the checkout. Actual rendered-frame hashes, camera appearance, Android lifecycle, and the full cadence matrix remain unverified. Mathematical export sampling checks do not substitute for rendered-frame checks.

## Remaining work

- Persist exact shot geometry, claim/ack identity, accepted rewind history and settlement timing, then build combat overlays and exact per-shot inspection. Client kill markers cannot supply strict killer-life evidence omitted by their network packets.
- Finish multi-frame presentation history for projectiles, viewmodels and other visual consumers at accelerated playback, and run gameplay-hash checks across every requested playback rate and display cadence.
- Editable Bezier tangents, a curve graph, and viewport visualization of authored versus collision-adjusted camera paths.
- Synchronized multiview rendering and offline audio mixing/export.
- Replay package export/import with exact custom-map ownership.
- Build-to-build replay regression analysis and diagnostic bundle export.
- Full replay-error recovery actions and the final cross-platform acceptance suite.

## Theatre usability follow-up

- Camera keys keep an explicit selection independent of the playhead. Clicking no longer retimes a key to the click position; dragging requires actual pointer movement. Previous/next, update selected, copy, delete and selection highlighting support precise editing.
- The embedded preview accepts focused keyboard movement and drag-to-look. Typing in the options does not move the camera, and playback buttons keep the embedded editor open.
- Camera shortcuts are shown near the top of the editor. Free camera applies live FOV and roll, with a fullscreen readout. B adds a key; N selects the next; minus/equal change FOV; semicolon/apostrophe change roll. Brackets retain playback-speed bindings.
- Save controls display progress, range errors and the saved path locally, honor the entered name, and offer a full-replay save without range marks. Recording completion feedback waits for the asynchronous writer and reports failures.
- Desktop compilation completed with no errors and the same 21 warnings. These UI changes have not been exercised with a rendered replay or real input; the earlier automated-check results above predate this follow-up.
