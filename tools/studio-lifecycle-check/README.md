# Studio lifecycle check

```sh
dotnet run --project tools/studio-lifecycle-check -c Release
```

Checks exact CLI parsing, bounded and atomic settings/session persistence, recents, missing/malformed state, generic dirty-document save/cancel/discard/Save As ownership, read-only source inspection, job cancellation and awaited shutdown.

Child processes run the production authenticated `StudioInstanceGuard` with temporary installation/user-data paths. They exercise second-launch forwarding, independent instance scopes, abrupt process disappearance, fresh endpoint replacement/reconnect and clean descriptor cleanup. The peer processes are test hosts, not the game. Dirty-document tests use a fake lifecycle document, not a second map/replay implementation.

`--native` additionally runs the production Avalonia `App` and `StudioWindow` with the classic desktop lifetime in child processes, then launches the actual `ProjectPrimeStudio` executable to forward a source document. This needs a desktop display (or a configured virtual display on Linux). It tests normal window shutdown, isolated profile coexistence, crash survival, explicit restart recovery and endpoint cleanup. The independent peer is another Studio profile; this does not substitute for the separate game/playtest broker gate.

The native gate also rejects one surface admission after the actual native surface and capabilities have been created. Created/released counters must each increase once while live and registered counts stay unchanged. The canonical Map must remain editable through visible CPU geometry and an availability warning, retain exact Undo, and close without leaking or double-releasing the failed handle. Its diagnostic CPU capture is preserved under the temporary `project-prime-native-admission-evidence` directory.

Additional gates are opt-in so a successful base run does not imply native game, Replay or export coverage:

```sh
dotnet run --project tools/studio-lifecycle-check -c Release -- --replay /absolute/paths.txt
dotnet run --project tools/studio-lifecycle-check -c Release -- --native-game /absolute/paths.txt
dotnet run --project tools/studio-lifecycle-check -c Release -- --native-startup /absolute/startup.json
dotnet run --project tools/studio-lifecycle-check -c Release -- --native-loaded-startup /absolute/paths.txt /absolute/recording.ppdemo /absolute/loaded-startup.json
dotnet run --project tools/studio-lifecycle-check -c Release -- --native-dense /absolute/dense.json
dotnet run --project tools/studio-lifecycle-check -c Release -- --native-map-modes /absolute/captures
dotnet run --project tools/studio-lifecycle-check -c Release -- --native-modals /absolute/captures
dotnet run --project tools/studio-lifecycle-check -c Release -- --native-replay /absolute/paths.txt /absolute/recording.ppdemo /absolute/captures
```

`--replay` uses a separate owner-thread process running the canonical passive Replay player with extracted game assets. It compares linear playback against forward/reverse seeks using gameplay, presentation and complete semantic graph hashes; verifies rates, camera sidecars, marked clip Save As, overlapping clip cache leases, viewport recreation, evidence validation and detached analytics. Two generated recordings refer to distinct historical versions of the same map identity; both private scenes coexist without game-library installation or a game Shell/NetSession. The two stock recording fixtures are read from the repository's baseline artifacts, so this gate needs those files in addition to the explicit asset configuration.

The same owner process checks fractional camera crops across every interpolation/ease and constant-speed mode, reloads, nested crops, transformed keys and checksum validation. It checks bounded asynchronous camera writes, canceled waiters, failed publication/retry and deliberate discard. Frozen source binding refuses changed recordings without altering the private world or existing targets. Cache tests hold source/package pins through owner closure, delayed analysis and aggressive pruning, then verify release after completion/cancellation. A focused repair case restores the identical source with a changed timestamp and requires dirty camera edits to reach the durable sidecar.

`--native-game` runs the real game and Studio in both launch orders with isolated profiles and runtime roots. It tests duplicate-game rejection, exact playtest package identities, immutable generated runtime files while a scene is active, actual dirty-close Cancel/Discard, stop/rebuild/restart and independent crash recovery. A dirty canonical Map's state, history, selection and layout are compared throughout. It never targets an unrelated game process.

`--native-startup` measures five owned Home processes through usable native layout and records startup time, working set, render scale and actual physical dimensions. `--native-dense` opens a generated 1,024-object canonical Map and forces 20 distinct native redraws. Its JSON distinguishes the requested 2560×1440 viewport from the dimensions actually allocated by the display/window manager. It records CPU submission metrics and retained upload/readback counters, then takes an explicit diagnostic GPU capture; nullable GPU timing remains unavailable when the driver does not provide timestamps.

`--native-loaded-startup` measures five fresh Map and five fresh Replay processes through their first usable native viewport. Its JSON records the source hash/size, authored object count or initial Replay hashes, actual viewport pixels, render scaling and process working set. These are fixture-specific fresh-process measurements with a warm operating-system cache; run them without competing builds or performance probes.

`--native-map-modes` captures eight actual GPU presentation modes while checking retained mesh uploads and zero normal-frame readback. It also opens and captures the owned native performance HUD window with inspector/hierarchy hidden, verifies unchanged viewport bounds, measured source values and timer/provider cleanup. Scoped provider checks require unavailable metrics for an unrendered second Map, a released Map and a lost device, then require current-generation metrics after presentation resumes.

`--native-modals` opens the real owned primitive and model-import dialogs. It checks native ownership, initial field focus, invalid/valid Enter, Escape/titlebar/button cancellation, one history edit with exact Undo, preview staging and resource release on document/window close. Separate explicit GPU captures prove the model preview target; Avalonia control bitmaps prove dialog text/actions and short-window scrolling. Control bitmaps do not capture the native child surface.

`--native-replay` exercises actual native single/four-view captures, positive default HUD pixel differences, a second player POV, nonzero-frame retry, a continuing viewport after clip Save As, independent PNG and audio/encoder workers, explicit cancellation, document closure and whole-window closure. It verifies retained source snapshots after the original path is renamed and installation ownership until the worker finishes. The PCM file must contain measured audio energy, and real FFprobe validates encoded video/audio geometry and duration. This gate requires a native graphics display, a recording with at least two active players and real FFmpeg/FFprobe executables supplied through `PROJECT_PRIME_TEST_FFMPEG` and `PROJECT_PRIME_TEST_FFPROBE` (the local development defaults are under `/tmp/project-prime-export-ffmpeg`). A source-level implementation of this gate does not establish a passing native result; retain the run log and captures as evidence. Diagnostic flags `--native-replay-no-hud` and `--native-replay-workers-only` print their reduced scope explicitly and do not certify the omitted default HUD/four-view/clip checks.

The four-view clock gate measures the child's accepted elapsed interval at 1× and 2× and requires each individual viewport timer to remain disabled. A separate controlled owner-tick fixture observes backward preparation in central Jobs, cancels it, and requires the published world and checkpoint accounting to remain exact; old-request cancellation must leave a newer seek intact. Its returned-frame complete graph comparison explicitly presents the same native view before both snapshots. Diagnostic capsules distinguish the ready simulation replica from its first GPU presentation; the full graph includes mutable presentation assets and draw caches. `--native-replay-stale-view` adds a failed fourth view across device loss, current-generation recovery and final stale-view teardown; it requires scene renderbuffers and native surfaces to release while allowing the renderer's legitimate retained texture cache. Export checks freeze the cropped camera state before later editor edits, compare the first worker PNG with an exact native owner reference and require terminal scratch removal with durable status/log retention.

`--native-replay-audio /absolute/paths.txt /absolute/recording.ppdemo /absolute/evidence` creates a separate canonical recorder copy with explicit event metadata and records its source identity. It drives the actual native Add Cue and Export Selection controls at the product's 1280×720 resolution, validates persisted immutable tickets, and requires audible event-cue PCM on the Replay bus versus exact silence at Replay volume zero with other buses disabled. Each independent native worker must complete three inclusive samples and clean its private scratch. Native hosted control bitmaps at two scroll positions show selected cue actions and volume controls; these captures are distinct from native GPU viewport pixels and an operating-system composite screenshot.

To rebuild only this harness against a coherent, complete Studio output snapshot while other build targets run:

```sh
dotnet build tools/studio-lifecycle-check -c Release -p:StudioHostSnapshotPath=/absolute/studio-output
dotnet tools/studio-lifecycle-check/bin/Release/net10.0/studio-lifecycle-check.dll --native
```

The snapshot option references its managed assemblies and copies its native dependencies, application executables and runtime files. Normal builds retain the production project reference. Screenshot inspection and separate domain/renderer checks remain necessary; this suite does not claim full migrated editor parity from a base or headless run.
