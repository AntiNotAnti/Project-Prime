# Enhanced Hunters live retirement and historical compatibility

Baseline: `91b1c0c6c44e8c90effd250729709f791a84765e` (`origin/main`, 2026-10-12).
SDK: .NET 10.0.401; macOS arm64. This is Phase 2A's live eligibility boundary;
it is not a protocol or replay-schema migration.

## Behavior and ownership

The live option is removed from launcher lobby/offline screens, console settings
and hosted-server argument forwarding and dedicated-server option properties. An explicit `-enhancedhunters` request fails
before game assets or native startup. Online lobby definitions, session control,
match control and direct server simulation reject the retired modifier with an
explanation. Settings schema 11 normalizes its retained JSON key to `off` at every
load, including current-schema imports; selected mode, Balanced Mode, affinity and
other choices survive.

Only a scene owned by `ReplaySceneServices` can consume Enhanced reconstruction.
Live frame/lifecycle/HUD/world hooks are excluded, enhanced fire context is no
longer authored, and live snapshots emit the existing neutral 26-byte suffix
(target slot `FF`). A forced old scene flag cannot enable the feature. No new
telemetry events or aggregate counters are produced by reconstruction.

Compatibility fields retain their exact identity. `EnhancedDirectHit` is shared
ordinary combat evidence and is unchanged. Balanced weapon/alt-form behavior and
native Hunter bot decisions remain independent.

## Frozen contracts and retained readers

- Protocol 53; modifier 1024 remains reserved for Enhanced, 8192 remains Balanced.
- Recorded protocols 29/30 map old Enhanced bit 8192 to 1024. Earlier player suffix
  expansion still uses target `FF` and neutral state.
- Canonical player suffix: 26 bytes, containing two 8-byte movement events.
- World zones: 16 maximum, 24 bytes each, with existing bounded validation.
- World checkpoint version 9, replica checkpoint version 10; no field/type rename.
- World contract: `A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A`.
- Existing fire bit `1 << 28`, enhanced-child replay fact `1 << 6` and telemetry
  event ordinal remain reserved.

Archive consumers call `SessionStatePacket.TryReadArchived(...)`. It retains all
original match-definition and wire validation, including room/mode validity and
modifier conflicts. Only the support policy for Enhanced differs from live
admission. Archived Balanced + Enhanced remains invalid as before. Live control
uses full current definition validation and reports unsupported rules clearly.

The `Mods/EnhancedHunters` namespace retains state types, codecs and historical
reconstruction. Some former authority helper code remains behind an authority
gate that no live scene can satisfy. Deleting or renaming the namespace/state is
not this change's scope: it changes checkpoint fingerprints and object IDs, and
requires a separately tested archive migration. This slice makes no FPS or memory
improvement claim.

## Validation commands

Public, asset-free checks:

```sh
dotnet run --project tools/nettest -c Release -- --enhanced-retirement
dotnet run --project tools/nettest -c Release -- --gamemodecheck
dotnet run --project tools/nettest -c Release -- --advanced-rules
dotnet run --project tools/nettest -c Release -- --protocol18
dotnet run --project src/MphRead/MphRead.csproj -c Release -- -replayformatcheck
```

Headless content-backed checks use a legally supplied `paths.txt` directory:

```sh
dotnet run --project tools/nettest -c Release -- --enhanced-scene "$DATA"
dotnet run --project tools/nettest -c Release -- --advanced-rules-scene "$DATA"
dotnet run --project tools/nettest -c Release -- --combat-scene "$DATA"
dotnet run --project tools/mechanicscheck -c Release -- "$DATA"
```

The old `--enhanced-hunters` and `--enhanced-scene` test aliases now run retirement
assertions. The renamed CI retirement job retains generator, waitlist, lane,
transport and replay-format checks.

Before the runtime change, a synthetic Enhanced v3 packet recording and v9 world
capsule were generated locally, then restored and stepped for 12 frames. Baseline
PASS: 46 assertions; forward/restored gameplay and presentation hashes matched.
The capsule and per-frame hashes remain local because world graphs include
content-derived state. Replaying those same files after the change tests historical
reading rather than regenerating a candidate baseline:

```sh
# Run --record-baseline only on the pinned baseline with the characterization harness.
dotnet run --project tools/nettest -c Release -- --enhanced-retirement-scene "$DATA" "$FIXTURE"
```

No actual recorded seven-Hunter Enhanced gameplay fixture was available. Synthetic
archive continuation covers canonical state, recorded rules, world restore and
seek continuation, not an end-to-end seven-Hunter visual/audio matrix. Native UI
windows and physical Windows/Linux/Android tests are not covered by headless runs.
## Candidate results

All tests below used .NET 10.0.401. The final asset-backed checks used isolated
`/tmp/consolidation-retirement-user-data` containing only a copied `paths.txt`,
plus `PROJECT_PRIME_MAP_COMMUNITY=https://maps.example.test/` to avoid unrelated
local Community preferences. No user settings or game assets were changed.

| Check | Result | Scope / evidence |
|---|---|---|
| Desktop/nettest Release build | PASS | 0 errors, 31 warnings |
| Headless server Release build (`MphReadServer=true`) | PASS | Separate `/tmp/consolidation-retirement-server-build` output |
| Retirement rules/settings/archive malformed input | PASS | 80 assertions; includes prior room/mode/conflict validation and NaN time rejection |
| Frozen baseline v9 capsule and continuation | PASS | 46 assertions; identical pre-change gameplay/presentation hashes, 12 frames |
| Eight-player live eligibility and Balanced scene | PASS | 20 assertions; concrete replay owner required |
| Replay formats | PASS | 3,034 assertions |
| Game mode lifecycle | PASS | 2,198 assertions |
| Advanced rules | PASS | 130 assertions |
| Full lobby | PASS | 7,746 assertions |
| Actual waitlist transport | PASS | 309 assertions |
| Synthetic waitlist policy/wire | PASS | 203 assertions |
| Protocol 18 replication lanes | PASS | Zero warmed decode allocations; existing 1,172-byte ceiling |
| Eight mixed-quality UDP peers | PASS | 1,720 intents, 128 exactly-once controls, 400 ms pump stall |
| Packet generator | PASS | 35 generator / 77 wire assertions; 5,000 malformed datagrams |
| Network Python contracts | PASS | 414 tests, one existing skip |
| Settings archive | PASS | 26 assertions, including transactional rollback and writer fencing |
| Native mechanics | PASS | 280 Vanilla/Balanced assertions; retired live matrix removed |
| Full combat scene | FAIL (pre-existing) | `DeadHeldFireDoesNotSpawnGhostShot/Magmaul`, HP 0; exact same failure on pristine baseline |
| Full Balanced advanced scene | FAIL (pre-existing) | Kanden charged-disrupt duration; exact same failure on pristine baseline |
| Native UI, rendered seven-Hunter archive matrix, physical Windows/Linux/Android | NOT_RUN | Headless scope; hardware/UI acceptance remains separate |

The two gameplay failures were compared against the pristine baseline client at
`engineering-baseline/tools/native-render-check/bin/Release/net10.0/ProjectPrime.dll`
(MVID `bb5922d1-051d-4a89-940b-d14096c550fe`). A temporary `nettest` harness referenced
that already-built assembly and its dependencies; it did not rebuild or modify the
baseline product. Both fail at the same assertion and values as the candidate.
The retirement-specific native old-life Missile/no-barrage assertions pass before
the later combat failure. No unrelated gameplay fix was introduced.

Initial broader tests against the real user-data folder also failed on an invalid
local Community URI. Fresh isolated preferences removed that environmental issue;
the passing isolated results above supersede those attempts without deleting their
local logs.

Local logs: `/tmp/consolidation-retirement-baseline-*.log` and
`/tmp/consolidation-retirement-candidate-*.log`. Original frozen fixture SHA-256:

| Local fixture | Bytes | SHA-256 |
|---|---:|---|
| `enhanced-synthetic.ppdemo` | 1694 | `3c3b539b8922ec9374cbb672754fae10507070ae1b8b3518799aa07dd74afdef` |
| `enhanced-world-v9.bin` | 646308 | `a842a51563ee32307b209423e888a89ed167d5b37bf333c015f789a4434149c0` |
| `enhanced-world-v9-hashes.json` | 1585 | `a29bb0647836e589aaf5934a4965b0c068b821d3a87bf33ec9a200b8e9d99b93` |

They remain under `/tmp/consolidation-enhanced-fixture`; neither the binary world
graph nor retail data is committed.

## Rollback

Revert the retirement commit to restore live exposure/eligibility. Reserved packet
and replay layouts are unchanged in either direction. Settings already migrated to
`off` remain off after rollback; the user may explicitly select the old option in
the restored build. No automatic switch to Balanced was made.
