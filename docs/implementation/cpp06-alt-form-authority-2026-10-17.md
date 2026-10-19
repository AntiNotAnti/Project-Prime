# CPP-06: bounded form reconciliation at 750 ms RTT

Status: **narrow source fix implemented; broader CPP-06 acceptance remains open**.
This slice fixes an independently reproduced reconciliation-timing failure in
the existing authority owner. It does not establish seven-hunter rendered,
real-process impairment, replay appearance or physical-device acceptance.

## Reproduced problem and correction

`PlayerReplicationBridge.ApplyForm` already uses the existing
`FormReconciliation` state machine. Its ping comes from
`PlayerReplicationHost.Ping` → `NetSession.SlotPing`, populated from the dedicated
server's transport RTT in the roster. A `Start` correction calls the native
`ModStartFormSwitch` / `TrySwitchForms(force: true)`; a premature correction can
reverse an otherwise completed form transition.

The old post-transition grace expression clamped RTT-derived grace to 32
simulation frames. Under the existing fixture's delayed-report model, a 750 ms
RTT spans 45 frames at unchanged 60 Hz simulation. A morph settling at frame 20
does not receive its fresh report until frame 65, but the old grace plus mismatch
delay requested another switch at frame 60. The corresponding unmorph timeline
requests a switch at frame 54 before its fresh report at frame 59.

The existing standalone `tools/formtest` directly compiles the production
`FormReconciliation.cs`; it does not reference or build MphRead. Extending its
matrix and horizon reproduced this actual old-source failure:

```text
morph at 750 ms frame 60: expected None, got Start
```

Only the production grace calculation changes: lower bound remains 8 frames,
upper bound increases from 32 to 60 frames (one second), and ping arithmetic
uses `long` before clamping. At 750 ms the effective grace is 45 + 8 = 53 frames.
The upper bound handles extreme readings without indefinite waiting; the
independent **90-frame absolute mismatch ceiling and 90-frame transition
ceiling remain unchanged**. No new lifecycle owner, protocol change, movement
rule, hunter mechanic or UI is introduced.

## Source-linked verification

The existing tests now exercise 45/150/250/300/350/750 ms in both directions.
Their horizon follows delayed report arrival and continues another 20 frames,
instead of stopping at frame 50 and missing both 750 ms failures.

Missing-report cases leave requested corrections unapplied and verify finite
`Start`/`Force` recovery. The tests check both 750 ms directions, transport's
9999 ms maximum roster value, integer overflow/negative inputs, and an unmorph
where the independent 90-frame mismatch ceiling must win over later fallback.
Existing lost-press recovery, stalled animations, canceled/restarted transitions,
long valid animations, stale reports after agreement and reset checks remain.

Commands used (SDK `/Users/jarrett/.dotnet/dotnet` 10.0.401; standalone target
`net9.0`; Release; macOS host):

```sh
/Users/jarrett/.dotnet/dotnet build tools/formtest/formtest.csproj -c Release \
  -p:OutputPath=/tmp/prime-cpp06-before/bin/ --nologo
/Users/jarrett/.dotnet/dotnet /tmp/prime-cpp06-before/bin/formtest.dll
# Apply only the reviewed production grace change, retaining the same fixture.
/Users/jarrett/.dotnet/dotnet build tools/formtest/formtest.csproj -c Release \
  -p:OutputPath=/tmp/prime-cpp06-after/bin/ --nologo
/Users/jarrett/.dotnet/dotnet /tmp/prime-cpp06-after/bin/formtest.dll
```

Before: build succeeded with 0 warnings/errors; execution failed at the exact
750 ms morph boundary above. After: build succeeded with 0 warnings/errors;
**1,731 reconciliation checks passed**. The same fixture source was used on
both sides. Initial attempts to relocate `BaseIntermediateOutputPath` picked up
old generated assembly attributes and failed compilation; those commands produced
no before/after test evidence. Retaining the standalone tool's own `obj` and
isolating only its output resolved that test-invocation issue without changing
the project or production build.

Actual locally measured SHA-256 receipts:

| File | Before | After |
| --- | --- | --- |
| Production `FormReconciliation.cs` | `89b19d5e0c51421cbe6b38ad2e544b7031efbe99f947a4a9d74851358dcbf9a5` | `d8a99b2e9308a45a57e9548de868c6e6c80a25d85ee51c8f4d42636eddfe38b8` |
| Existing formtest `Program.cs` | `60362d02302702450fbab9e17fb01214e02f8c54ba2acdc7fcd96ad1ca9d5d6d` | same |
| Standalone `formtest.dll` | `6897cc9c3db671a5e3c1ee881812f270e71c691027a06c27586bce87117f2bd7` | `0cc3520d6b376cf1eab275ff9d50e5dc3cbe9a5f344ae2661a9d922d7e024296` |

Private logs are `/tmp/prime-cpp06-before-build.log`,
`/tmp/prime-cpp06-before-run.log`, `/tmp/prime-cpp06-after-build.log` and
`/tmp/prime-cpp06-after-run.log`. Source copies, binary outputs and SHA receipts
are in `/tmp/prime-cpp06-before` and `/tmp/prime-cpp06-after`. These are local
working-tree fixture identities, not a clean exact-head application build or a
performance benchmark. No GPU, asset-map, input-latency or physical Android
result was measured by this slice. Root centrally owns subsequent full client
and server builds and fresh compiled engine checks.

## Existing coverage reused and remaining acceptance

Source review found existing relevant coverage that should be extended instead
of duplicated:

- `AltFormHitTests`, `HistoricalPoseTests`, `AltContactLagTests` and
  `NetAltHitCheck` cover all seven historical form hit volumes, Kanden segment
  geometry, lifecycle/form sweep boundaries and actual native Spire rock
  attachment/contact damage.
- `PlayerFormProtocolTests` covers immediate slow-lane phase publication,
  future-form deferral, life/generation rejection and bounded eight-player wire
  lanes. `PlayerFormTests` uses native Weavel models to test authority animation
  age, detached anchors, split/merged health, duplicates, stale/ordered episodes,
  death/respawn, local prediction and turret destruction.
- `ReplayFormHistoryTests` and `ReplayFormCompatibilityChecks` cover form-history
  persistence and historical formats. `NetContinuousTargetCheck` checks
  historical biped/alt target selection across all seven hunters, target
  eligibility and lifecycle rejection; continuous-target impairment fixtures
  already extend through 400 ms RTT.
- `tools/nettest/form-audit.py` already joins full authority/source identities,
  observer phases, turret anchors and health revisions. Its existing Python
  suite was rerun during this investigation: **30 tests passed**. Those tests
  validate evidence contracts, not a new network or hardware run.

The existing real-process alt runner's documented profiles are LAN, 100, 250
and 320 ms; the requested complete 45/150/250/350/750 ms rendered seven-hunter
transition/attack matrix was not run here. Trace visual cloak/attack consistency,
Shock Coil carry through form changes, Kanden appearance, Spire visible attachment,
replay appearance/seek, five-plus rotations under impairment and device
acceptance still need their actual applicable fixtures and independent captures.
Trooper Scout Drone remains part of the later imported-character mechanics
track. This small timing fix does not implement or certify those mechanics.
