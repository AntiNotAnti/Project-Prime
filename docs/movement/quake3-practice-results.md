# Quake III practice tuning results

The private offline tuning slice passes a fresh normal Windows build and all
13 focused correctness routes. Classic remains the default. Existing Momentum
v1, protocol 59 and replay formats are unchanged. The draft now builds speed
under airborne forward+strafe input; it is available through `-momentumq3dev`.
See the [launch/tuning guide](quake3-practice-tuning.md).

## Source and build

Testing used genuine latest main `a471fd3d58fbca817070aadccad4950301f5f6dd`
(including merged NCX05), plus the exact 14-file source overlay retained in
[sanitized evidence](evidence/quake3-practice.json). Every current file matched
its manifest hash before publication, and source/binary hashes were unchanged
after the checks. No open PRs were present at the initial compatibility review.

```text
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadReplayPreviewFailureFixture=false
```

Windows x64, .NET SDK 10.0.401, ordinary broad UI default enabled; the narrow
replay-failure fixture was disabled. **Build PASS, zero warnings/errors.**
Production assembly SHA-256:
`a5c47e09eb2dd6ecde51af90260839976f4b3628c044e1156f114d6dfc657b04`.
Harness SHA-256:
`4bf9df93fc631b5146b0a3c2bc5be79807e54249b917ef644bf8a3576819dfc2`.

## Correctness results

All routes below exited 0. Invoke them with `dotnet nettest.dll` from the built
output; `<assets>` is a paths-only configuration directory pointing at the
operator's own extracted data. Optional scene JSON paths preserve raw results.

| Route | Passing checks |
| --- | ---: |
| `--momentum-practice-tuning` | 230 equation/configuration checks; zero managed allocation in warmed Step loop |
| `--momentum-admission` | 44 owner, scene and recording-admission checks |
| `--momentum-practice-scene <assets> [json]` | 24,032 assertions across all seven Hunters |
| `--momentum-math` | 104 shared equation/input checks |
| `--momentum-profile` | 20,048 unchanged v1 profile checks |
| `--momentum-scene <assets> [json]` | 57,346 existing offline checks |
| `--momentum-jump-scene <assets> [json]` | 187,405 existing native jump/collision checks |
| `--momentum-rules` | 146 authoritative rules/wire/ownership checks |
| `--momentum-replay-rules` | 82 replay-rule checks |
| `--momentum-replay-metadata` | 54 recording-label/cache checks |
| `--protocol58-replay` | 204 historical archive checks |
| `--combat-family-policies` | 325 latest-main NCX05 policy checks |
| `--native-movement-baseline <assets> <fixture>` | 56 original Native golden trajectories |

The native golden fixture is
`tools/nettest/fixtures/native-movement-dc7f097.json`. Three actual production
CLI smoke checks also passed with expected exit 2: missing room, missing
`-seconds` value, and JSON with a negative ground acceleration. Captured native
logs confirm refusal before creating a window.

In each of the seven actual moving CPU scenes, ordinary W runs through tick 95,
A runs ticks 49–95, and a Space edge requests the native jump at tick 48. Controls
release through tick 179. No transform, facing or velocity is written by the
controller. Each arm executes 180 fixed simulation steps.

| Same offered input, every Hunter | Existing v1 | Quake III draft |
| --- | ---: | ---: |
| Peak planar speed, world units/second | 7.1997075 | 8.817804 |
| Airborne samples with speed gain | 0 | 18 |
| Forward+strafe samples | 47 | 47 |
| Exact adapter Y-bit comparisons | 180 | 180 |
| Actual jump and native collision landing | pass | pass |

Separate jump-only Native/draft pairs preserve vertical trajectories and jump/
landing flags. Independent math fixtures verify mirrored optimal turning can
continue past the former 1.5× retention ceiling; that is an equation fixture,
not a claim that the fixed-facing scene schedule exceeds it.

Admission tests cover all three live-recording boundaries, pending client/server
recorders, owner loss and permanent retirement. A spawned-scene synthetic policy
seam confirms retirement falls back to supported v1 without consuming retained
draft rates. A separately labeled synthetic bot identity checks the v1 adapter
boundary; it does not establish real mixed-roster AI lifecycle acceptance.

## CI, limits and remaining work

**Hosted CI is NOT VERIFIED at publication.** The math workflow now includes
private draft tests on Windows, macOS and Linux, but those jobs are not local
passes and publication does not wait for them. No Mac build/simulation was run.

This is the first playable tuning stage. Human feel, physical input/focus,
rendering, and high-speed map qualification remain open. Native Hunter jump,
gravity, collision and ability ownership mean it is not exact Quake Live physics.
The draft is deliberately unavailable to multiplayer and live recording; a
settled multiplayer/recorded revision needs a separately identified profile and
its compatibility validation. No fresh online campaign or replay-playback pass
is claimed by these focused tests. The earlier v1 closeout and qualification
limits remain in force.

No new performance measurements or budget pass are claimed. The operator's
accepted performance tradeoff and deferred optimization remain documented in
[PR-09h](pr09h-results.md). Raw logs are retained in ignored
`artifacts/quake3-practice/windows-evidence-01.zip`, with the redirected CLI logs
separately retained. The initial incremental bundle was refused for a missing
shallow-history prerequisite before build; a genuine direct fetch supplied the
base. This setup failure is retained and is not reclassified as a product failure
or hidden by the successful build.
