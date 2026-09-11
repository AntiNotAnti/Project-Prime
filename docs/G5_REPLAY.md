# Replay 2.0 (G5.2)

The playable replay path records accepted authoritative facts, never client commands. Format 3 adds independently compressed checkpoints and a recoverable event index. The original format 2 writer remains the default for frozen fixtures; shipping client recording opts into format 3. Protocol 4, 5, 6 and 7 decoding remains isolated from the current live protocol.

`Shared.Replay` owns bounded file IO, record framing, and the shared neutral feedback reducers/checkpoint codec, with a Game protocol reference. Client and Server reference that single assembly; Game does not depend on replay IO. Client recording is optional local recording, not server audit evidence. The separately owned server recorder uses this same format for mandatory server recordings.

## Format

The six-byte file header is `FPDM`, format byte (2 or 3), protocol byte. Format 2 retains its original single Deflate body, byte/extended-u32 frame deltas and u16 payload lengths.

Every format 3 chunk has this little-endian header, followed by a complete independent Deflate stream:

| Offset | Type | Meaning |
| --- | --- | --- |
| 0 | u32 | `0x33435046` (`FPC3`) |
| 4 | u8 | 1 = ordinary record; 2 = keyframe |
| 5 | u8 | Reserved, zero |
| 6 | u16 | Event marker flags |
| 8 | i32 | Compressed payload length |
| 12 | i32 | Exact decompressed payload length |
| 16 | u32 | Monotonic recording frame, 60 Hz |
| 20 | u32 | CRC32 over header bytes 0–19 followed by compressed payload |

The CRC detects accidental corruption; it does not authenticate recordings. Complete chunks are flushed individually. Opening scans and verifies the prefix, rebuilding a bounded index of keyframe and event offsets. A torn or corrupt trailing chunk terminates the recovered prefix, preserving prior records. No footer is required for recovery.

Ordinary payloads retain one record-kind byte plus the existing protocol data: Match=1, Snapshot=2, World=3, Roster=4, Event=5. Roster bodies have match ID followed by the version-specific roster. Event bodies have match ID, reliable event type, and accepted semantic payload. Kill and world events preserve identities, phase, weapon/source classification, assists, position and objective facts rather than reducing them to text.

Keyframes contain i32 record count, then u16 length and bytes for each complete record. Optional checkpoint records are Presentation=6, Clock=7, ChatState=8 and Perspective=9. Clock is 32 bytes: scene frame u64, live frames u64, elapsed float, global elapsed float, RNG1 u32, RNG2 u32. Perspective is one byte: slot 0–7, or 255 for an observer. Presentation/chat use ordered fragments (part u16, part count u16, total byte count i32, bytes).

Client checkpoints contain current rules/match, complete roster, latest player snapshot, all complete world batches, clock/RNG, bounded feedback and visible chat. Feedback serializes independent event-ID windows, identity/name order, kill feed, current damage history and the sixteen-life recap archive. Restore validates isolated temporary objects before replacing live feedback. It does not invoke event handlers or audio. The first checkpoint's metadata also appears in the ordinary stream so a recording started mid-match has the same sequential baseline.

Limits: 12 hours, 2 GiB file size, four million chunks, 8 GiB aggregate decoded bytes, 131,072 indexed entries, 2 MiB raw checkpoint, 2,048 checkpoint records, ordinary payload at most `NetConfig.MaxPacketSize`, 256 KiB feedback, 256 ordered fragments. Invalid lengths are rejected before the corresponding large allocation. A file that exceeds the total file limit is rejected; exhausted scanning/index limits expose only the verified prefix. A writer stops with `LastError` on IO or format bounds.

## Seek and transport

`ReplayPlayback.Seek(frame)` queues a seek; `Transport.Paused`, `Transport.Step()` and `Transport.Rate` provide camera-independent control. Rates are exactly 0.25, 0.5, 1, 2 and 4. Integer quarter-tick scheduling preserves fixed 60 Hz simulation delta time. Pause accumulates no catch-up debt; an explicit step advances exactly one tick. Seeking preserves the selected pause/rate state. Format 2 remains sequentially playable with transport controls; indexed seeking is a format 3 capability.

The world stream intentionally contains no active beam/bomb object graph. Seek therefore restores the closest checkpoint at or before `target - 1801`, then runs the actual simulation forward to target. This is a bounded transient warmup, not an approximation of projectile positions:

* `BombEntity.Initialize` gives Lockjaw bombs 900 original 30 Hz frames = 1800 simulation ticks. `Process` decrements countdown every call, marks explosion at zero, and removes the already exploded bomb on the next step. Link/target/owner-death branches only shorten countdown. Actual single and linked Lockjaw tests cover the 1801-step lifetime.
* Authored player projectile lifespans are below that horizon; collision tails last four original 30 Hz frames. Lifespan/age characterization is also covered by `AuthoritativeTimerTests`.
* Five-second checkpoint spacing adds at most 300 ticks. The maximum accepted seek distance is 2101 frames, or 2102 inclusive simulation calls (about 35 seconds of recorded time). A missing checkpoint outside that bound produces an explicit error rather than an unbounded simulation run.

Seek work is split into batches of at most 120 simulation steps per host update. Actual scene clocks/RNG are restored before the world step. Room/rules changes still pass through the normal replica room reload. Intermediate drawing and device input are suppressed; effects and fades advance without submitting frames. Central sample/script/music playback is muted during warmup. On completion, pose history is reset and feedback sound sequences synchronize without replaying historical confirmations. Existing cosmetic ambient particles may restart at the restore checkpoint; their complete historical particle graph is deliberately not serialized.

Chat ages by recording time during playback, so pause and speed changes also pause/scale its visibility. The timeline is camera independent. F6 toggles pause, F7 steps, F8 cycles speed, F9/F10 seek previous/next indexed event. The bottom HUD bar exposes the same clickable controls and scrub timeline. Android touch forwards bottom-bar taps through a single bounded pending coordinate to the simulation thread.

Index markers include kill, headshot, authoritative award-based multi-kill, flag capture, node capture, Prime change, match point, overtime and match end. Protocol 9 derives objective/lifecycle markers, including match end, from normalized `MatchSemantic` facts and multi-kill only from the server's `DoubleKill`/`TripleKill` award; the capture award carries only the general `Award` marker. Protocol 8 retains its low-level `WorldEvent` mappings and terminal-world match-end inference for compatibility. Markers contain offsets/ticks, not gameplay commands. Seeking and playback never create a live network connection.

## Replay and observation architecture (F3-F5)

`ReplayPlaybackSession` is the instance owner for a replay source, transport,
seek state, perspective, decoded world state and scene services. The static
`ReplayPlayback` entry point remains a compatibility facade for full-file
Theatre playback. `IReplayTimeline` is the renderer- and network-neutral source
boundary; `ReplayFileTimeline` reads immutable `.fpreplay` files and
`RollingReplayTimeline` retains accepted facts in memory without compression or
disk IO. The rolling source targets 45 seconds, has a hard 64 MiB payload cap,
and evicts only complete restore segments.

The live killcam freezes a five-second clip only after an accepted `KillEvent`
matches the current local slot, connection and life identity. It constructs a
passive replay session with a separate `Scene` and `ScenePresentation`; the live
scene continues receiving and applying authoritative state and is never rewound.
Immediate playback ends when the local life, match or connection changes, so it
cannot delay respawn. Post-round playback waits for a non-playing phase. The
server policy is `Disabled`, `Immediate` or `PostRound`, and the local setting
can only disable an allowed killcam. Hidden seek warmup submits no frames or
audio. Once visible, presentation audio/input ownership moves to the replay scene
and returns to the live scene on skip, completion or context change.

`HighlightAnalyzer` consumes only recorded Kill, MatchAward and MatchSemantic
facts. It applies deterministic scores and context bonuses, merges linked or
overlapping action sequences, and returns at most eight immutable clip windows.
`ReplayHighlightMetadataService` stores a versioned SHA-256-keyed cache under
`cache/replay-highlights`; it never mutates the replay. Theatre shows the full
replay and generated highlights, and launches either one range or a chronological
reel through the existing seek/transport path.

`ObservationContext` is an immutable view of one scene at one delivered tick.
It contains only the players, objectives, scores, clock, phase and recent facts
already delivered to that live observer, replay or killcam source; it never falls
back to another process-wide session. `BroadcastDirector` consumes that context
at 10 Hz, with a 2.5-second minimum shot, eight-second normal maximum, switch
threshold, recent-target penalty, event overrides and manual lock. Camera motion
remains in `SpectatorCameraController`, including logical operator commands,
first-person/chase/orbit/free modes, collision checks and presentation-rate
smoothing. `BroadcastHud` owns Full, Minimal and Off/clean-feed views and does not
include network diagnostics. Existing Node/Worker authority continues to decide
ordinary delayed versus trusted zero-delay observation.

The file header identifier `FPDM` is retained solely as an on-disk compatibility
contract. User-facing names, paths, commands and UI use Replay and Theatre.

## Verification and limits

Focused tests cover frozen protocol fixtures, accepted live Kill/World recording and semantic roundtrip, fixed-tick rates/pause/step, byte-exact feedback restore and duplicate rejection, malformed checkpoint atomicity, every truncated byte of a final chunk, every corrupted header/payload byte of that chunk, file/payload/frame bounds, nearest checkpoint selection, and fact-stream seeks across match transitions. Real AMHE1 tests exercise single and linked Lockjaw lifetime bounds.

The fact-stream seek fixture does not exercise a GPU or audio device. The
`ReplayPlaybackCheck` now requests the SDL `IRenderToolHost` path, which owns a
hidden SDL GPU presentation rather than the retired hidden compatibility-OpenGL
window. No successful rendered run is recorded for the current host, so full
beam/bomb appearance and device-audio silence remain open acceptance gates.
The earlier NSGL compatibility-profile failure is retained as historical
evidence for the retired probe only; it is not evidence that the SDL GPU path
has passed. CPU/fact-stream timings are reported separately from rendered seek
latency.

Validated focused command (2026-09-07): `GAME_DATA_DIRECTORY=AMHE1 dotnet test tests/Tests/Tests.csproj -c Release --filter 'FullyQualifiedName~Replay|FullyQualifiedName~ReplayPlaybackTests|FullyQualifiedName~FeedbackAudioTests'`. The final run passed 35 tests. In the fact-only fixture, targets 0/2300/2500/4100 restored frames 0/300/600/2100 and ran 1/2001/1901/2001 steps in 0.008/2.128/2.045/2.076 ms respectively. These measurements deliberately exclude rendering, actual projectile presentation and audio devices; they are not rendered seek-latency claims.

### Touch camera and transport access

The existing Android VIEW button cycles Free, FirstPerson, Chase, Orbit, and AutoDirector; NEXT cycles the watched target through the same presentation controller. The existing top spectator strip also accepts taps: camera mode / target / objective on its first row, FOV minus/plus and camera speed minus/plus on its second. These commands are consumed by the outer presentation update, including while replay transport is paused. The Android spectator input branch still returns before gameplay input collection. Desktop F1–F4, minus/equals, and brackets remain supported. F5 toggles the director, F11 locks or resumes its target, F12 cycles Full/Minimal/Clean Feed, and Home returns to free camera. F6–F10 remain exclusively owned by replay transport. Raw keys are translated only at the input boundary; camera behavior consumes `SpectatorCommand` actions.

The bottom replay strip exposes pause, one frame, playback speed, previous/next event, and timeline scrubbing. Its single queued touch is bound to the current recording reader, discarded across recording changes, and rejected for non-finite coordinates or while chat/pause UI owns input. Opening a pause/chat UI after a tap was queued discards that tap during consumption. Spectator strip coordinates are finite and bounded, and camera preference changes are gated to spectating. These are source and focused-input checks, not rendered-device acceptance.
