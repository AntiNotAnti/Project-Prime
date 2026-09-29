# Shared biped scenarios

Run a fresh process per fixture, with absolute input/output paths (the app changes its working directory):

```sh
dotnet run --project src/MphRead -c Release -- \
  -fpsscenario /absolute/scenario.json -fpsoutput /absolute/new-trace.jsonl
```

Normal Prime captures run at 60 Hz. Directions are held for two ticks; jump edges occur on the first substep only. The runner asserts consumed input and complete frame coverage. `spawn` is the exact physics position, compensating for the respawn helper's +1 vertical offset. Seeds are reset after construction/spawn, before settling. Each output has a scenario-hash manifest. Horizontal facing allows one native fixed-point unit of length quantization, since original spawn vectors are not perfectly unit length.

All 17 fixtures are executable. Data Shrine covers idle, forward, release, strafe, diagonal, reversal, jumps, fall, walls, ceiling, slopes, ledge fall and knockback response. Jump pad uses Combat Hall and requires its own native starting state. Alt forms remain rejected. `placementRequired` remains a fail-closed escape hatch for future unverified scenarios.

`initialImpulse` supplies velocity, acceleration and its duration in native ticks **after settling**. The knockback fixture uses this to verify the movement response and timer, not weapon damage or hit-generated impulse magnitude. Every vector component must be exactly representable in native fixed point. Neither runner rewrites that impulse during recording.

The original ROM consumes the same JSON through [native-recorder](../native-recorder/README.md). Both starts reset the input-idle timer and begin Standing without Grounded; settling establishes floor contact. This avoids saved idle sway contaminating the starting movement basis. Real native idle sway that begins later remains visible in comparison results.

## Native-cadence experiment

Add `-fpsnativekernel` to run the offline 30 Hz operation reconstructed from ROM stages. The manifest explicitly records `movementOperationHz: 30`, `sceneClockHz: 60`, and `diagnosticNativeKernel: true`; trace frames still identify elapsed 60 Hz boundaries (2, 4, ...). The harness invokes the scene once per native operation and does not convert its unrelated world timers. This does **not** enable a live gameplay mode or prove 60 Hz intermediate-state behavior. The full comparator passes 15/17 fixtures; idle and knockback fail heading only because of the delayed-sway policy. All 17 agree on movement state and measured stages/contacts. See [native findings](../../docs/physics/NATIVE-MOVEMENT-PARITY.md).

### Actual 60 Hz scene scheduling

Use `-fpsnativecadence60` instead of `-fpsnativekernel` to run two real scene frames per native movement operation. The native operator executes on even boundaries; authoritative position is held on odd boundaries. One-frame jump edges are buffered until the boundary. `-fpsinputphase 1` supplies commands on the second half instead of the first, exercising the other edge timing. Both phases match all 17 native movement/stage/contact trajectories; idle and knockback retain their heading-only failures.

The `.cadence.jsonl` companion records **every** scene frame and distinguishes held frames from native boundaries. The manifest reports `sceneFramesPerNativeTick: 2`, `diagnosticCadence60: true`, the input phase, and held intermediate authority. The draw-only presentation candidate shows the native midpoint on even frames and its endpoint on odd frames, adding one 60 Hz frame of translation delay. The companion includes `presentationPosition`; the manifest records this delay. The runner checks body/camera positions, five first-person render fractions per scene frame, and draw noninterference. It ends the room intro camera before capture. All four movement modes retain their previous traces. The native mode is also exercised by the synthetic network workers below; shipping policy negotiation and visual playtesting remain unverified. Cadence policy, buffered input and existing render histories restore through the versioned world capsule.

## Reproduce the entire paired corpus

Prepare Data Shrine and Combat Hall starting states using the recorder README, then run:

```sh
python3 tools/fps-scenarios/run_corpus.py \
  --dotnet /absolute/dotnet --prime /absolute/ProjectPrime.dll \
  --bridge /absolute/prime-native --rom /absolute/AMHE1.nds \
  --state /absolute/data-shrine/native-biped.state \
  --state /absolute/combat-hall/native-biped.state \
  --output /absolute/new-corpus-directory --native-kernel
```

Replace `--native-kernel` with `--native-cadence60` for the actual 60 Hz scene experiment; add `--input-phase 1` to test late commands. Omit both mode flags for the normal 60 Hz baseline. Each case captures both implementations, verifies geometry/impulse coverage where relevant, runs the strict comparator, and writes logs plus `summary.json`. Exit 0 requires every parity comparison to pass. The present experiment correctly returns 1 for its two heading mismatches; the shipping 60 Hz baseline also fails. Capture completion and geometry coverage are reported separately from parity.

`check_coverage.py --scenario ... --native ... --prime ...` checks penetrating contact normals for the two walls, ceiling and slopes; standing/falling/landing transitions for the ledge; an active pad plus upward launch for the pad; and prescribed velocity plus exact acceleration duration/rate for knockback. A file named after a geometry feature is not sufficient evidence of coverage.

## Cadence checkpoint continuation

```sh
dotnet run --project tools/nettest -c Release -- --fps-cadence-replay \
  /absolute/directory-containing-paths.txt /absolute/v134-fixture-directory
```

The fixture directory contains `v134-source.ppdemo` and `v134-world.ppwc` from the historical exporter. The check creates current native-mode checkpoints at both scene phases, restores into fresh worlds, verifies the pending edge, gameplay/presentation state and 20 continuation frames, and checks policy isolation, malformed-policy rejection and historical/current-v3 compatibility. Native modes use world-capsule v4; ordinary worlds continue writing v3. Replay file formats and protocol 33 are unchanged. This is not a network rollback test.

## Synthetic network regression

```sh
dotnet run --project tools/nettest -c Release -- --fps-client-parity \
  /absolute/directory-containing-paths.txt /absolute/biped-release.json /absolute/new-results
```

This creates a dedicated authority and two real clients for each 0/50/100/200 ms RTT, 100 ms + up-to-40 ms jitter, and 100 ms RTT + 150 ms pump-stall profile. Client A uses the local keyboard path; B observes. It verifies consumed input, whole local position/velocity trajectories against 0 ms, final authority snapshot/observer convergence, and rejected updates; correction counts are reported. Movement remains owner-authored in the shipping protocol. This is a replication regression, not combat fairness certification. Injected-impulse and combat scenarios fail closed here until their network setup is implemented.


To require native movement parity in those real client sessions:

```sh
dotnet run --project tools/nettest -c Release -- --fps-client-parity-native \
  /absolute/directory-containing-paths.txt /absolute/scenario.json \
  /absolute/new-results /absolute/original-ROM-native-reference.jsonl
```

The authority establishes a new life at the fixture spawn; A waits for that life and resets through the replication spawn scope before recording. The script checks actual consumed input and normalizes elapsed frames for the strict ROM comparison. After capture, A holds its endpoint while publishing through the real transport to drain queues. This also supports continually bouncing pads, which cannot settle. Protocol 33 transmits owner position and derives remote velocity from consecutive reports: the endpoint check requires that derived velocity to settle to zero and the observer to match it, while retaining the difference from the owner's integration velocity separately. It does not claim identical velocity semantics across those lanes. Native-mode remote animation/contact processing preserves the accepted reported velocity instead of publishing a second gravity/traction update. Native spawning clears the predecessor's airborne timer.

Earlier `native-client-release1` evidence exposed that calling client `Spawn` outside the replication scope had silently failed. Earlier default-network results therefore tested transport at the default map spawn, not the requested fixture placement. Corrected runs retain strict native comparison as a required gate.

## Combat timing

The `combat/` fixtures add continuous `fire` input through the ordinary keyboard path and the native DS L button. Power Beam single press, repeated taps, and full-charge release are measured in the actual 60 Hz scene. Use the corpus command with `--scenarios /absolute/tools/fps-scenarios/combat --native-cadence60`; the one-call 30 Hz movement experiment rejects combat because unrelated world timers are not converted there.

Prime writes `.combat.jsonl` every scene frame; the recorder writes `native-combat.jsonl` every native tick. `check_combat.py scenario native prime` requires complete ordered timelines, actual shots, consumed input, exact charge conversion and shot-age progression. A Prime shot at frame F maps to native boundary `2*ceil(F/2)`; raw Prime event frames remain in the result. Thus early-half firing stays visible instead of being described as simultaneous. Power Beam charge is exactly twice the native counter. The first three fixtures pass both this timing check and the strict movement comparison. This proves these Power Beam cases, not freeze, damage, other weapons or combat network fairness.

Combat captures also retain projectile pool slots, position, spawn position, velocity, flags, age and remaining lifetime. Prime velocity is recorded in units per native tick (twice its per-scene-frame vector); ages/lifetimes use seconds in both traces. These observations do not themselves assert projectile parity. Native pool type, count, memory bounds and owner are checked before recording. Power Beam's raw ammo backing value differs between implementations and is not an ammo-cost test.
