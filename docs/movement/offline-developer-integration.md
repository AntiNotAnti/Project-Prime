# Offline developer integration (PR-02)

This records the PR-02 seam and initial profile. PR-03 replaces its temporary v0
coefficients with the immutable [v1 private candidate](tuning-v1.md), adding an
explicit native jump/contact label and a steering-growth ceiling.

Classic remains the default. The desktop developer command is:

```sh
ProjectPrime -momentumdev "MP3 PROVING GROUND" -hunter Samus
# Optional bounded run: -seconds 1..600
```

This is a private offline experiment, selected once after room loading and before
the first simulation tick. It has no saved preference, lobby control, network bit
or replay mode. A scene must own the foreground state and player registry on its
simulation thread. Online roles, active replication, playback, replicas, side
scenes, campaign, aim training, Balanced and Enhanced rules are refused. Losing
that eligibility permanently retires the selection for that scene. Input follows
the existing suppression decision for camera-only mode, chat, pause, UI and
spectating. A presentation camera is not the identity of the local body: the
adapter checks the scene's main player and local slot directly.

`PlayerEntity.Speed` remains the single velocity. Existing integration advances
position by `Speed / 2` at 60 Hz, so the adapter converts X/Z to units/second with
`Speed * 30`, calls the pure solver with `FrameTiming.StepSeconds`, and converts
X/Z back. It preserves the original Y bits. It reads the same signed movement
controls and analog scales as native movement. Right is the negative native
`_field78/_field7C` basis; the pure solver normalizes the planar basis and radially
limits the input without discarding partial magnitude.

The seam replaces native X/Z damping inside `ProcessMovement`, before the
unchanged gravity, position and collision code. When it succeeds,
`ProcessBiped` skips the later native horizontal impulse and cap. Classic executes
the original statements in their original order. An eligibility check both before
and after triggers/external acceleration prevents a newly launched body from
receiving two movement owners. A trigger teleport bypasses Momentum for that tick.

Only living, active, ordinary local bipeds are eligible. Morph transitions, alt
forms, bots, Prime Hunter, scripted cameras, frozen/stuck/locked bodies, climbing,
boosting, jump pads, Q3 launches, external acceleration, attached enemies, moving
platforms and slippery/hazard surfaces retain native movement. The private Trooper
wall-cling variant from merged #702 is excluded. These conservative exclusions
are development boundaries; they are not substitutes for PR-03/04/07 acceptance.
Native-comparison movement-shadow observations are suppressed in experimental
scenes. Optional scene diagnostics instead retain one value sample and a
saturating count; they are off by default and never drive physics.

## Temporary profile v0

This slice deliberately does not freeze competitive tuning. For the current
Hunter's walking or strafing table row, `cap` and `traction` are native fixed-point
values converted to floats:

| Parameter | Formula |
| --- | --- |
| Desired speed | `cap * 30` |
| Ground and air acceleration rate | `traction * 60 / cap` |
| Ground friction | `(1 - clamp(StandSpeedFactor, 0, 1)) * 30` |
| Stop-speed floor | `0` |
| Air projection cap | `desiredSpeed * (1 - clamp(AirSpeedFactor, 0, 1))` |

Grounded means native `Standing` with nonpositive Y velocity; an immediate native
jump therefore selects air acceleration. Jump eligibility, impulse, gravity and
landing are still entirely native. There is no auto-hop, jump buffer, retention
ceiling or approved v1 profile yet. PR-03 must supply and measure those policies.

## Reproducing CPU scene acceptance

```sh
dotnet build tools/nettest/nettest.csproj -c Release
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-admission
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-scene "$ASSET_CONFIG" artifacts/momentum-pr02/scene.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --native-movement-baseline "$ASSET_CONFIG" tools/nettest/fixtures/native-movement-dc7f097.json
```

`ASSET_CONFIG` is an operator directory containing `paths.txt` for extracted game
files; no assets are distributed. The scene harness uses genuine offline input,
spawn, player processing and native collision, without impersonating an online
role. Headless mode retains its Roam presentation camera. The harness enables
body input and ends the native intro through `CameraSequence.End()`, since that
camera does not advance the intro. It does not construct HUD textures or certify
rendering, mouse aim, physical input devices, server authority or replay playback.

Each of seven Hunters runs six identical input scripts in Classic and Momentum:
forward/release, reversal, diagonal, jump-only, air input and idle. Each fresh scene
settles for 120 ticks after spawning and measures 180 ticks. Assertions require
actual controls, displacement, jump/landing and a difference between modes; they
reject a vacuous inactive solver. Per tick they check one invocation at most,
sample ownership, solver output, exact Y preservation, and unchanged final X/Z on
clear ticks. Jump-only trajectories compare native Y and flags. Separate gate
scenes exercise late selection, locks, morph, alt form, input suppression and
permanent retirement. Field-level gate probes are synthetic and do not certify
the corresponding full gameplay event.
