# PR-00 baseline inventory

Base: `2d18ac05721366597afc1afdb57f061e73ee4423`, latest fetched main at task start. The parent preflight found no open PRs at start. Recheck upstream overlap before publishing each slice. This record inventories the base architecture; it does not count later candidate test results as baseline measurements. Machine-readable companion: `tools/upstream-improvements/baseline.json`.

## Environment and evidence status

macOS fallback uses `/Users/jarrett/.dotnet/dotnet`, SDK **10.0.401**, matching `global.json` exactly. The system dotnet host had no installed SDK. Windows SSH alias `jarrett-remote` rejected authentication, so no Windows command or physical Windows test was executed in this session. CI configuration includes Linux, Windows x64, macOS arm64/x64 and Android arm64/x64 paths; those are configured coverage, not fresh platform passes. Renderer direction remains OpenGL.

**Source audit completed; physical/gameplay controlled-run baseline not executed.** A constructed production-recorder fixture now supplies packet/event/shot SHA256 values, as described below; real recording/score hashes and frame timing remain unmeasured. The required real loaded multi-life recording remains an acceptance gap. Existing constructed fixtures are useful but are not an authentic recorded LAN match. Candidate builds and tests are recorded separately by the implementation slices/root verification.

## Compatibility and schemas

`NetConfig.ProtocolVersion` in `src/MphRead/Mods/Network/NetProtocol.cs` is **61**. Do not change it for these presentation/regression slices. `DemoReader` in `DemoFile.cs` recognizes container versions **1–6**: legacy v1 sequential records, v2 deflate, v3+ metadata/chunk/index reader. `ReplayFormatV3.cs` explicitly accepts metadata schemas 3, 4, 5 and 6. Container support is distinct from protocol/content compatibility. `ReplayIdentityCompatibility` accepts recorded protocols 4–61, treats older protocols as best-effort with explicit historical defaults/omissions, and requires exact protocol matching for live peers.

`ReplayCapture.Capture()` already chooses **v6 when timed-shot certificates exist, otherwise v3**. Range/archive paths retain v4 world-checkpoint and v5 indexed-fire-continuation semantics, or v6 when certificates are present. Preserve this choice and old bytes. `ReplayProtocol58Tests` contains explicit archival byte fixtures for protocols 56–61 and rejects unknown future protocol 62; this is a bounded historical-layout check, not a claim that all old gameplay recordings render correctly.

`FireEvent` has legacy 20, pose 67 and current 74-byte layouts. Optional pose/reticle fields preserve authored shot origin, aim, direction, view and quantized reticle, along with source/ack clocks and source life. `PlayerFormState` is 28 bytes including authoritative frame/start phase and body/turret state. `ReplayShotFactPacket` is version 1, 61 bytes: accepted damage/lethal/headshot/health facts keyed by identities and shot. Timed-shot certificate facts are optional **file-only** records (tags 251/252), not live packet authority. `ReplayFactKind` already covers Match, Roster, Snapshot, Intent, World, Event, Presentation and AuthorityWorld; marker kinds cover kills/deaths/spawns/damage, objectives and mode-specific events.

Network baselines, private replica checkpoints and saved-world checkpoints are different contracts. Existing strict replay-from-start fallback must remain when packet decoder parity is not proven. A world-only checkpoint must never silently authorize accelerated exact export.

## Available facts and provenance

| Field | Source (under Network unless noted) | Authority/local | Frame clock | Serialized? | Replay accessible? |
|---|---|---|---|---|---|
| Match/epoch/generation/life | NetProtocol.cs; NetLifecycleTracker.cs; NetShotDiagnostics.cs | authority identity | match/authority frame | yes, packets and checkpoints | yes; reject stale identity |
| Shot ID / authored muzzle / direction / aim / view / reticle / charge | NetFireEvents.cs; PlayerInput.cs; NetHooks.cs | owner-authored input, authority validation separate | SourceFrame + AckFrame/AckSubFrame | yes, optional FireEvent pose/reticle; canonical replay intent | yes when optional flags present; legacy may omit |
| Accepted damage / lethal / headshot / victim / health | ReplayShotFact.cs; NetHitClaims.cs | authority accepted result | ResolveTick + LaunchFrame | yes, 61-byte version-1 fact | yes; damage identity and lifecycle joins |
| Timed-shot proof / funding / source knowledge | ReplayTimedShotFact.cs; ReplayTimedShotFacts.cs | recorded authority observations, never replica damage authorization | AuthorityFrame + authored fire source/ack | yes, optional format6 file-only tags 251/252 | yes where certificates captured |
| Biped/alt/transition start / body/turret health / anchor | NetPlayerFormState.cs; PlayerReplicationBridge.cs | authority form, local prediction kept distinct | Frame + StartedFrame | yes, 28-byte PlayerFormState | yes; older recordings may have Unknown phase |
| Body position / speed / facing / health / score | NetProtocol.cs PlayerState; ReplayRecorder.cs | authority snapshot; movement owner semantics preserved | snapshot server tick and recording frame | yes, snapshots and accepted timeline facts | yes; reconstruction is not exact owner render |
| Pose history / collision rewind | NetUnlagged.cs; NetShotInputHistory.cs | authority validation history | simulation/consumed input and ack clocks | not a general standalone exact owner visual track | derive only from facts actually recorded |
| Local camera transform / FOV / cannon animation at draw | PlayerHud.cs; FrameTimingCheck.cs; SceneReplayMultiView.cs | local presentation only | fractional render presentation and generation fences | shot view/reticle partial; continuous final draw stream absent | partial reconstruction; do not claim exact displayed frames |
| Recording frame / events / markers / restoration kind | ReplayTimeline/ReplayTimeline.cs; ReplayCapture.cs | accepted facts; markers derived | RecordingFrame + ServerTick | yes, archive/timeline; bounded rolling clips | yes; NetworkBaseline differs from ReplicaCheckpoint |
| World checkpoint / RNG / entities | ReplayWorldCheckpoint.cs; ReplayReplicaCheckpointLayout.cs | private replay reconstruction | canonical replay frame | yes, versioned world/replica checkpoints | yes; world-only does not prove packet decoder parity |

Only join shot records by match, authority epoch, shooter slot/generation/life and shot ID; retain victim identity and damage/event identity when comparing outcomes. Slot plus time is insufficient. Local camera/pose must remain presentation-only and must never settle damage, payment, movement, scoring or collision.

## Overlap decisions

- **Already implemented:** `wc02` wide collision. `tools/map-editor-check/CustomCollisionChecks.cs` checks over 65,535 point indices and 32-bit face starts/indices. Reuse it; no `wc32` port.
- **Already implemented:** HUD Studio's 14 components, anchors, profile migrations and rich crosshair controls. `docs/architecture/hud-customization-status.md` also records remaining preview/render acceptance gaps. No alternative editor.
- **Already implemented:** authority receipts, snapshot scores, life/generation fences, owner-death reconciliation and form/animation protections. Audit and add bounded regression coverage before changing production behavior.
- **Already implemented:** rolling clip recorder, Studio highlight categories, `.reel.json` non-destructive segments, OpenGL Four Views scopes and camera tracks. Extend their existing owners.
- **Already implemented:** separate viewmodel projection and responsive scope-FOV sampling. Read `docs/engineering-next/cpp04-responsive-scope-fov.md` and its linked completion record before adding an independent response setting; do not duplicate existing projection paths.
- **Already implemented:** normalized touch layout and editor. Add only the missing multi-instance/action behavior in the established models.
- **Not currently captured as a continuous exact track:** final local owner camera/cannon animation for every displayed draw. Shot-time view/reticle data is partial. Diagnose missing facts before proposing an opt-in bounded presentation track.

## Reproducible commands discovered in source

These are **available commands, not reported passes**. Use a single shared app/nettest build owner to avoid output races. From the repository root:

```sh
/Users/jarrett/.dotnet/dotnet build tools/nettest/nettest.csproj -c Release
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --replay-shot-payload
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --protocol58-replay
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --replay-form-history
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --lifecycle
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --combat-fast-receipts
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --claim-receipts
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
/Users/jarrett/.dotnet/dotnet run --project tools/map-editor-check -c Release -- --collision-only
/Users/jarrett/.dotnet/dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -replayformatcheck
/Users/jarrett/.dotnet/dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -replaycontrolcheck
/Users/jarrett/.dotnet/dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -frametimingcheck
```

`--replay-shot-payload` exercises the production recorder with constructed owner/remote shots, optional rays and quantized reticles, not UDP. `--protocol58-replay` is archival-byte-only. `--lifecycle` includes real UDP loopback admission and coalescing tests alongside constructed prediction and reordered packet injection; do not describe the entire suite as a two-player latency campaign. Existing real socket lobby fixtures include `--lobby`, `--waitlist`, `--custom-readiness-udp`; inspect their setup before running in an environment with ports already in use.

Asset-backed native fixtures: `--form-scene <asset-root> [room]`, `--combat-arbitration-scene <asset-root> [room]`, `--self-splash-scene <asset-root> [room]`, `--bots-scene <asset-root> [room]`. Real UDP owner fixtures include `--momentum-online-scene <assets> <port> <Native|Momentum> <json> [seconds] [replay]` and `--momentum-touch-online <assets> <port> <Native|Momentum> <json> [seconds]`. These require legal extracted content and appropriate server/port setup. Do not commit cartridge/player data.

Existing replay-backed app commands such as `-replayexportcheck <source>`, `-replaylivecheck <source>` and `-replaydurablecheck <source>` need a real compatible recording and appropriate loaded assets. `tools/rmlui-editor-check` and `tools/rmlui-opengl-check` provide editor/GL fixtures; their names alone do not prove GPU coverage.

## Required remaining baseline campaigns

Offline four-bot match; two-player LAN; 150–200 ms RTT with jitter/loss/reorder; Insta-Gib and standard weapons; third-person; rapid morph/unmorph; custom-weapon isolation; repeated respawn and all seven Hunters. For each, retain anonymous fixture identity and reproducible settings, replay/shot/event/score hashes, first/last life identities, decoder digest and frame timing. Run the same small multi-life fixture twice independently and compare exact outputs; hashes are currently **unmeasured**.

Seek/reverse/preview/Four Views and exact export need separate logical packet/scene comparisons and actual OpenGL validation. Also pending: Windows access, physical Android touch/device checks, camera-pop visual checks, platform builds/CI and measured overhead. No unconditional per-frame I/O, unbounded queues or live-thread synchronous analytics are authorized. Preserve vanilla 1v1, custom weapon isolation, Classic/Momentum and existing timed-shot rules throughout.

## Sanitized constructed multi-life fixture

`tools/nettest/UpstreamBaselineTests.cs` uses the actual `ReplayRecorder`, intent and accepted-shot codecs, plus the actual `NetLifecycleTracker`. A synthetic roster identifies all seven Hunter IDs; two lives each contain a posed nonlethal and lethal result. Reused shot IDs remain distinct under complete match/epoch/generation/life identity. It asserts four accepted shot facts, four cross-match and four cross-epoch recorder refusals, and one old-life rejection by the lifecycle guard before forwarding. The recorder deliberately assumes prior lifecycle admission; the test does not misattribute this guard to the recorder. No headshot is fabricated. Four event markers (two spawns, two kills) are grounded in the constructed lifecycle/result sequence.

`tools/upstream-improvements/expected-synthetic.json` is the sanitized generated expected output. The test checks its committed digests and compares two independent recorder instances. It uses an explicit little-endian length-delimited record/marker envelope, excluding runtime hash codes, wall clocks, process identifiers and JSON formatting. The packet digest is an accepted-record envelope digest, not a full demo-file hash.

**Tested:** standalone net10.0 Release test harness referencing the existing built production assembly and compiling this test file with assembly name `nettest`; build passed with zero warnings/errors. Two separate process invocations passed; each captures two independent recorder instances. Centralized full candidate build/run is pending. The temporary harness is `/tmp/prime-pr00-recorder/check.csproj` and does not rebuild or mutate shared application outputs.

```sh
/Users/jarrett/.dotnet/dotnet build /tmp/prime-pr00-recorder/check.csproj -c Release
/Users/jarrett/.dotnet/dotnet /tmp/prime-pr00-recorder/bin/Release/net10.0/nettest.dll
# Normal command after parent registration/build:
/Users/jarrett/.dotnet/dotnet tools/nettest/bin/Release/net10.0/nettest.dll --upstream-baseline
```

Expected: 20 records, four events, four accepted shots, two lives, seven roster Hunter IDs.

- Packet SHA256: `db1a6ea3b5dd4c1199f4fb04946a999da9f7998172c4f3d07586b6012f5055ed`
- Event SHA256: `e73817c8a2bfc4f0d33e6c9bafd2b22f69ecbde422cdd69cb7d27110fa867e92`
- Shot SHA256: `3c12bae80dd14d27e385dd0a505129c2e041a66e819a223629dfa98540bec5b2`

This proves deterministic constructed serialization in this environment. It does not prove hit validation, seven-Hunter gameplay, animation, authority score parity, native simulation, real UDP, packet-decoder seek equality, GPU export or a real loaded replay campaign.
