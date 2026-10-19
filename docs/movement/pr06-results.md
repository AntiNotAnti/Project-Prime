# PR-06: Momentum replay and historical geometry compatibility

Native remains the default. This slice restores the recorded movement rule in
private replay worlds, checks coherent playback/seek behavior, and exercises the
existing historical pose/hitbox queries with earned Momentum trajectories.

## Rule restoration and ownership

Replay services previously defaulted a match-only recording to a new Native
definition whenever SessionState was absent. Both initial application and world
restore now derive the model from the recorded MatchState modifier. A frozen
same-identity SessionState/MatchState contradiction is refused before decoder
publication, or on a detached checkpoint decoder before restore commits. Lobby
drafts remain mutable and new match identities can select a different model.

Decoder configuration and MatchState already carry the rule. Momentum v1 has no
new persistent jump queue, so no packet, event, hash or checkpoint schema changes
are needed. `ReplayStateHash` remains schema 4 and does not include MovementModel;
the new tests explicitly check the model before gameplay, every frame, after
restore and throughout memory/file seeks and paused transport operations.

Private replicas continue applying accepted snapshots before and after their
native simulation step. They never run the owning player's Momentum solver.
Recorded poses drive collision bodies and existing indexed presentation; no
replay prediction, live ownership or transport is introduced. Replay Lab practice
forks remain outside Momentum admission and are not movement-parity acceptance.

## Actual recordings and exercised paths

Two real UDP clients joined a dedicated server for each model. Client one enabled
the production accepted-fact recorder after actual world readiness, moved with
keyboard controls, jumped, morphed in flight, reached stable alt form and returned
to biped movement. Client two joined later and moved independently. Both the
completed DemoRecorder FullMatch and a production-frozen Clip were retained.
No fixture assigns a fake role, readiness witness or remote snapshot.

Native recorded 2,982 visible frames; Momentum recorded 2,979. The recording
worker itself advanced 2,983/2,980 scene ticks, with zero/1,583 solver steps;
Momentum suspends the solver through native form transitions and alt movement.
Native/Momentum each spent 400 measured ticks in stable alt form and captured
124/100 airborne morph ticks. These are CPU keyboard fixtures, not physical
device or network hit-result evidence.

Both private-world tests compare all gameplay and presentation hashes between
interleaved worlds in this environment. Ten real world checkpoints include the
first morph and 300-frame boundaries; restored worlds continue for 2,762/2,759
frames. Nine memory seeks and six durable-file seeks compare with the original
linear reference. File seeks use actual file capsules with 0–199 continuation
steps, zero rejected capsules and the existing 120-step per-update limit.
Pause, one-frame step, resume, EOF, canonical collision positions and recorded
movement mode are checked separately from the hashes.

The warmed scalar movement solver and native collision are unchanged. Existing
`NetSession.BroadcastSnapshot` still calls `NetUnlagged.Record(header.Frame)`
immediately before publishing that snapshot. The new historical-geometry test
samples actual offline 60 Hz movement through `Record` explicitly; it does not
claim to prove live snapshot/ACK binding. All seven Hunters earn airborne
direction changes and run native mid-air form transitions. Exact/fractional
poses, existing biped cylinders, body/head/miss rays, form-boundary interpolation,
native teleport endpoint discontinuity and explicit lifecycle fences pass.
The latter identities are synthetic production-API fixtures, not earned deaths.
The history remains 128 frames and the configured rewind limit remains 45;
neither is enlarged.

## Executed checks

Latest integrated base: main `bff37487c`, including PR-05, beam presentation PRs
#714/#716, custom-weapon foundation #720 and CI fixture repair #718.
Host: macOS 27 ARM64, Apple M4 Pro, SDK 10.0.401/runtime 10.0.12.
Managed production and harness builds pass with zero warnings/errors.
The complete rendered/recording matrix ran on `244b24d8c`. After #720 merged,
the harness was rebuilt and both all-frame CPU replay/durable-mode checks,
Native goldens, schema/admission and archival model checks ran again. Their
counts and aggregate replay digests remained unchanged. The new optional weapon
registry remains absent in these Native-weapon scenes.

| Check | Result |
| --- | --- |
| Asset-free replay model/decoder rules | 82 assertions |
| Actual Native/Momentum replay worlds, mode and seeks | 76,861 / 76,783 assertions; all 2,982 / 2,979 frames |
| Completed FullMatch mode/seek verification | Same assertion/frame counts; same aggregate gameplay/presentation digest as each session's frozen Clip |
| Durable diagnostics | Four passes: both models in Clip and FullMatch; nested ranges, cold/backward seeks, corruption fallback and recovery |
| Replica diagnostics | Four passes: both models/formats, nine detached world restores and 62 file/clip seek comparisons per run |
| Live capture diagnostics | Both pass; 11 world checkpoints and 251 frozen frame comparisons per model, backward seek and match-reset isolation |
| Historical Momentum pose/geometry | 18,489 assertions; seven Hunters, 14 actual scene trajectories |
| Native golden trajectories | All 56 unchanged against pinned `dc7f097`, tolerance `1e-5` |
| Archive format | 3,041 checks including original v1/protocol-4 timing fixture |
| Pre-Momentum protocol-57 qualified recording | 1,980 frames, 14 cold/cached seeks, five rates, frozen EOF and divergence detection pass on the current player |
| Reviewed protocol / timed-shot / pickup / grant archives | 204 / 4,668 / 83 / 51 assertions |
| Other focused checks | Payload/form history, lifecycle, architecture, compact intents, advanced rules and complete lobby lifecycle pass |
| Native/Momentum real GL virtual cadence | Both pass: 600 identical gameplay frames at each of 60/120/144/240/360/540 Hz |
| Native/Momentum theatre with GL | Both pass: 620 isolated frames, camera/player changes, four seeks, pause, HUD scrub and foreground/transport sentinels |
| Native/Momentum killcam lifecycle | Both pass: 2,355 checks, 24 skip/respawn/disconnect cycles, 314 historical frames, final freeze, resize and versioned audio handoff |
| Native/Momentum export with GL/FFmpeg | Both pass: repeated identical 24/30/48/60/90/120/144 FPS samples, native 720p/4K targets, queue/reel and cancellation |

The three-OS archival workflow now also runs the new asset-free replay rules.
It retains the existing explicit protocol allowlists and historical fixtures.
Current malformed/future protocol refusals remain strict. Test-local user-data
directories copy only the extraction's `paths.txt`; no account data is copied.
Dense recordings, rendered frames and encoded movies remain ignored local
artifacts. Committed evidence contains only numeric results, counts and hashes.

## Diagnostic compatibility corrections

Actual frozen live Clips deliberately continue their producer's held-input
firing world. FullMatch consumers remain indexed. Existing replica and durable
diagnostics assumed every input was an indexed FullMatch, causing the same
failure on actual Native and Momentum Clips. Durable rewriting now preserves
the input replay type; capsule checks compare with the source policy while
retaining explicit indexed FullMatch and unindexed live-world requirements.
Both policies and both models pass afterward. This changes diagnostics, not
the recording format, consumer policy or production firing reconstruction.

Two further diagnostic assumptions surfaced on these actual recordings. A short
killcam now checks its clamp against the first genuinely retained frame rather
than assuming history begins at zero. Export cancellation releases its render
lease immediately while the requested backward seek may remain pending; the
next independent test job now waits for that bounded restore and verifies ready
ownership of the same replay. All original cancellation, camera, lifecycle and
identity assertions remain. Killcam lifecycle markers/contexts in this diagnostic
are synthetic identities applied to actual recorded trajectories, not evidence
of an earned lethal hit or network damage outcome.

## Reproduction and limits

Build `tools/nettest/nettest.csproj -c Release`, then run:

```sh
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-replay-rules
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-rewind-scene "$ASSET_CONFIG" rewind.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-replay-scene "$ASSET_CONFIG" "$REPLAY" Momentum replay.json "$DURABLE_REPLAY"
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -noupdate -replaydurablecheck "$REPLAY" -output "$OUTPUT"
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -noupdate -replayreplicacheck "$REPLAY"
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -noupdate -replaylivecheck "$REPLAY"
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -noupdate -replaycadencecheck "$REPLAY"
```

To regenerate actual recordings, use the PR-05 real server/client recipe with
server lifetime `120`, client duration `50`, and an additional replay output path
on client one. That enables actual recording and mid-air keyboard form changes;
the shorter no-recording PR-05 script remains available unchanged.

Gameplay/presentation equality is proven within this tested environment and
fixture, not asserted as universal cross-platform bit identity. Virtual cadences
use actual GL draws but do not measure physical displays. Historical archive
tests retain Native defaults and exact decoder bytes; valid older world layouts
continue to use their existing compatibility/reconstruction paths.
The independent protocol-57 recording had no stored expected hashes from its old
build. Its result proves current playback/seek consistency, not visual or frame
hash parity with that historical executable. Older firing-contract capsules used
their expected safe reconstruction fallback.

Commit, publication and merge do not wait for CI/review. No all-green CI claim is
made. PR-07 input/bot parity, PR-08 lobby controls and PR-09 multiplayer/device,
hit-registration, performance and release acceptance remain mandatory. This
feature is not complete.
