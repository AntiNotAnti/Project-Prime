# CAMP-07: Campaign acceptance and release gates

**Branch scope:** independently verify the CAMP-01..06 migration on the exact
Project Prime commit under test. All work is stacked on CAMP-06; do not merge a
later phase independently of an unaccepted earlier phase.

## What the automation actually establishes

| Gate | Command | Evidence type |
| --- | --- | --- |
| Original save serializer and lease | `--post-match-save` | Byte-for-byte frozen legacy reference, retries and release |
| File select copy/archive | `--campaign-save-management` | Three slots, immutable source bytes and backups |
| Unlock rules | `--campaign-navigation` | Original room/planet metadata and areas |
| Ship-only travel | `--campaign-ship-flow` | Authentic post-commit ticket, wrong slot, locked planet, ordinary quit |
| Film IDs / startup | `--campaign-movies` | Metadata and no-device timeout/failure policy, **not film playback** |
| Ending save handoff | `--campaign-endings` | Both value-only Gorea variants, save and no-save policy |
| UI value projections | `--campaign-presentation` | Explored-only map, scanned-only logbook, viewport math |
| Sound routing | `--campaign-audio` | Original sequence IDs, audio lease preemption, synthesized WAV bytes |
| Complete simulated journey | `--campaign-journey` | Fresh state, first scan, both ships, unlocked travel, real JSON roundtrip, three slots, bad/good ending variants |
| Scene cleanup | `--scene-lifetime`, `--foreground-binding` | Device-free owner and immutable state fencing |
| Multiplayer/replay guardrails | `--pickup-protocol`, `--compact-intents`, `--pickup-replay` | Device-free protocol/recorder contract regression |

The journey test **simulates** scripted pickups, scan completion and verified
Gorea exits by setting their same save/exit values. It does not load combat,
play movies or operate any real controls. Every step reuses the production
`StorySave`, `MatchStart`, `AdventureSave` and campaign APIs; it never
serializes SecondHunt's incompatible save format.

### Cross-platform device-free run

Build the test harness once; each command executes in a separate process,
avoiding false passes from residual static menus or game state:

```bash
dotnet build tools/nettest/nettest.csproj -c Release
python tools/campaign-acceptance/run.py \
  --output artifacts/camp07-local-001 --include-guardrails
```

This writes `campaign-acceptance.json` and one log file per case, including
SHA-256 checksums, the built nettest assembly hash, current commit, OS, and
explicit `NOT_RUN` fields for native gameplay/graphics/audio/replay compatibility.
Choose a **new** output folder for every run. The PR workflow
`.github/workflows/campaign-acceptance.yml` runs the same suite on Linux,
Windows and macOS and uploads only test receipts, never ROM files.

### Optional native room and world-ownership smoke

With your **own locally extracted AMHE1 filesystem** (never pass a ROM/archive
or upload extracted files to GitHub Actions), an operator may also run:

```bash
python tools/campaign-acceptance/run.py \
  --output artifacts/camp07-cartridge-run-001 \
  --include-guardrails \
  --assets-dir "/path/to/extracted-AMHE1-filesystem" --asset-loads 10
```

The optional `--campaign-native-scene` command starts the **actual native
single-player Celestial Archives** room with a real Samus, native objects and
90 fixed simulation steps, then checks the original save serializer and
file-select roundtrip. The boarding reason is injected for testing and is
**not** evidence of walking to a hatch. The optional existing
`--asset-lifecycle` loads ten actual multiplayer maps and checks historical
scene ownership and resource release; it is a separate test, not proof of
campaign map correctness. Both operate in temporary writable directories.
Pass `--replay /path/to/an-owned-replay.ppdemo` to also exercise the existing
asset-backed replica/ownership path; this does not verify rendered playback.

Audio/music/headset, GPU output, input latencies, movie/audio sync, controller
focus, long play sessions and the real story ending can only be confirmed
with device testing.

## Native acceptance: do not silently promote a green CI result

Generate a locked-to-commit checklist outside the repository:

```bash
python tools/campaign-acceptance/native_evidence.py \
  --template "/private/qa/camp07-native.json" --commit "$(git rev-parse HEAD)"
```

Test **each** Windows, Linux, macOS and Android release build using owned
cartridge files. Each of the following categories must have its own
platform-specific artifact (a sanitized screenshot/video/log with no raw
Nintendo asset files). Fill all template rows with a real tester, hardware,
build configuration, observed results, file path and SHA-256.

| Native test | Expected observation |
| --- | --- |
| Cold and warm boot | Launcher -> game -> launcher -> restart without hang; Android surface recreation and foreground resume |
| New file, first scan and visor | Start Celestial Archives, collect actual scan, check dialog/logbook and persistence |
| Dock and star map | Walk to hatch and confirm, dock page/planet music, star-map music and valid/invalid selection |
| Unlock, travel, save/reload | Real boss/Octolith unlock, locked target refusal, cross-planet landing, resume from correct file, three save slots preserved |
| Bad and good Gorea endings | Actual final encounters; two-part bad and good movies, credits and distinct results, native save confirmed |
| Movies and skip/fallback | Opening/land/takeoff/Gorea movies, skip input, missing/corrupt VX on a disposable game-file copy and no audio/mute hang |
| Music/SFX and pause | Original room/ship/credits themes, volume/mute, 30-second idle, repeated reopen, minimize/resume with no duplicated or orphan playback |
| Map and controls | Native 3D map on 4:3, 16:9, ultrawide/phone; visited-only markers, Scan Visor, controller/keyboard/touch and Back |
| Memory, pacing and stability | Sample 60/120/240 Hz where supported, frametime percentiles, fixed 60-Hz simulation, memory growth across 10+ reopen/planet trips |

Additionally provide independent *shared* evidence for multiplayer protocol
interoperability against an earlier compatible client, historical replay
playback, recovery/rollback and original save byte parity, and no proprietary
assets in any published package. These are not inferred from `nettest`.

Validate **after** filling actual results:

```bash
python tools/campaign-acceptance/native_evidence.py \
  --validate "/private/qa/camp07-native.json" --commit "$(git rev-parse HEAD)"
```

The validator fails on `NOT_RUN`, missing gates, wrong commits, duplicate
records, absent proof, bad proof digests and raw-asset evidence formats.
Passing its synthetic self-test confirms only the validator, **not** native
acceptance. A human reviewer must still inspect the actual proof.

## Regression and release policy

- Save files remain `Savedata/save001.json` through `save003.json`; the
  new UI must never silently overwrite existing progress, copy saves via
  new envelopes or change historical replay/network formats.
- Do not remove the original campaign simulation, Scan Visor, pause map,
  scene audio/movies or fallback until device parity is established.
- No Nintendo ROM, decrypted filesystem, `.vx`, `.minincsf` or original
  map bundles may be checked in, uploaded to CI or attached as a build asset.
- Fix all required **exact-head** build, campaign matrix, lifecycle, Android
  APK and source/asset exclusion checks before considering merge.
- CAMP-07 can land only after earlier stacked PRs merge in order and a
  reviewer signs off on both automated receipts and physical native tests.
- On any failed acceptance item, retain the previous release as the rollback
  point, keep production saves/backups unchanged, and attach the failing
  test's repro instructions to the corresponding PR.
