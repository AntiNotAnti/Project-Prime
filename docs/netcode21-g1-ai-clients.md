# G1 native AI network clients — acceptance blocked

The opt-in G1 harness is implemented and has passing 2/4-client evidence. G1 acceptance remains blocked by two failed 8-client impaired campaigns and an unresolved owner-offer versus authority-native turret-emission mismatch. Failed artifacts are retained; no 8-client acceptance or release readiness is claimed.

## Configuration

`tools/nettest/run-net2.py --ai-net-client [0..3]` launches ordinary UDP clients whose local inputs and decisions use the existing native tactical AI. An omitted level selects 2. The default remains ordinary scripted campaign behavior when this option is absent. Each AI client retains a normal player identity: it must have `IsBot=false` and `RosterBot=false`, and the server roster must not identify its slot as a bot.

`--ai-seed` defaults to 1729. It is a nonzero unsigned 32-bit gameplay seed, incremented by client launch index; the base seed must leave room for every client. `--seed` remains the independent signed 32-bit network fault seed. Recorded settings and fault profiles identify both streams. AI RNG isolation preserves a client stream across authority snapshot RNG synchronization; UDP scheduling remains nondeterministic.

Supported client counts are 2, 3, 4, and 8. `--server-bot` adds one normal authority bot and requires room within eight total slots. It requires its own server bot roster slot and actual accepted/committed shot activity. AI mode disallows explicit HitRig scenarios, the scripted soak tour, delayed spectators, and the bomb triangle fixture. The runner's `tour` scenario default selects the native AI branch when AI mode is enabled.

An example configuration to execute and assess, with actual build paths and exact source identity supplied:

```sh
python3 tools/nettest/run-net2.py \
  --build /absolute/path/to/Release/net10.0 \
  --paths /absolute/path/to/paths.txt \
  --source EXACT_SOURCE_SHA \
  --out /absolute/path/to/campaign \
  --protocol 51 --players 2 --seconds 90 --drain-seconds 20 \
  --profile lan --seed 8128 --ai-net-client 2 --ai-seed 1729 --record
```

This command is a proposed run, not evidence that it has executed. Confirm the built protocol before using the example's explicit protocol value.

## Activity evidence and gates

`net_ai_evidence.py` supplements the existing campaign parity/drain audit. Every independent client must show at least 60 input frames, 60 decision frames, travel of at least 2 game units, and a real offered shot from its local slot. Campaign totals must show declared client claims, authority claim receipts, committed component payments, and owner native pickup consumption. Zero required activity yields `INCOMPLETE`; absent or malformed required counters, forged roles, invalid identities, or legacy parity/drain failure yield `FAIL`.

Shot identities use explicit epoch/match/slot/generation/life/shot fields, independent of JSON object order. Claim receipts include retries. Component payments count the shot component health/turret path and exclude separate bomb/continuous payment paths. Owner native pickup consumption does not establish an admitted authority grant or the later A1/A2 ownership contract.

Authority receipt/application denominators require process-lifetime `CampaignClaims.Received` and `CampaignClaims.Applied`. There is no fallback to `HitClaims.Received` or `HitClaims.AppliedHere`: those snapshots reset when the last peer leaves and the server suspends. The initial `8ddfc4b8` LAN attempt exported a zero resettable receipt snapshot and remains incomplete; positive operator observations cannot repair a missing lifetime denominator. Fresh `7b932686` campaigns export the required lifetime counters.

Each client report includes descriptive `lifecycleEvidence`. `SpawnedLives` counts AI decisions that observe a changed life ID, including the initial life; `AltFrames` counts AI decision frames spent in an alternative form. Positive counts are `OBSERVED`; zero counts are `NOT_RUN`. These counters do not prove lifecycle parity.

When authority `Facts` is present, exact 48-byte semantic packets supply canonical death events (`PlayerKilled` and `PlayerSuicide`) and spawn events (`PlayerSpawned`). The audit validates packet headers against outer evidence fields, validates actor/target identities, excludes baselines and the separate award lane, deduplicates by epoch/match/event ID, and rejects conflicting payloads. A respawn is counted only when a spawn follows a recorded death for the same epoch/match/slot/generation with a newer life. Initial or unjoined spawns remain spawn events, rather than assumed respawns. Missing facts leave these observations null with `NOT_RUN`; measured zero is also `NOT_RUN`. Alternative form entry/exit transitions remain null: sampled form frames do not provide an exact event ledger. These descriptive fields add no activity gates.

`paidAiSourceShotKeys` and each client's `paidSourceShotKeys` count unique source shot identities present in the authority payment evidence, with overflow rejected. These distinguish AI-origin paid shots from server bot payments. They are separate from the component payment denominator: one source shot may pay multiple victim/components. At least one paid AI source shot is required across the clients; there is no per-client paid-shot minimum. Missing key instrumentation remains null and yields `INCOMPLETE`; bot-only payments also yield `INCOMPLETE`. Malformed evidence yields `FAIL`.

Canonical respawn joins use serial-number comparisons for unsigned 16-bit life IDs and unsigned 32-bit ticks. Synthetic decoder tests cover wrap boundaries; they do not claim a campaign exercised a rollover.

## Verified checks and live results

Immutable source `7b93268638c35a70a0fdaeb3817c0c392b8c4215` built successfully. Twelve C# check groups passed; Python completed 388 tests with one explicit skip; all 27 CI checks passed. These checks do not replace live acceptance.

The following campaign parity, drain and AI activity audits passed under `artifacts/net21-g1/`:

| Campaign | Result |
|---|---|
| `udp-2-bot-lan-7b932686` | PASS |
| `udp-2-bot-rendered-7b932686` | PASS |
| `udp-2-impaired-7b932686` | PASS |
| `udp-4-impaired-7b932686` | PASS |

All four fresh demos from the two recorded LAN/rendered campaigns passed determinism and replica continuation checks on that immutable build. Engine SHA256 was `a3411e32b46c95dfaf7c82ded987defb6fe16f7b78cea8d814b560a3704273d4` and remained unchanged. Results are retained in `acceptance-lan-7b932686-replays.json` and `acceptance-rendered-7b932686-replays.json`.

The v7 replay capsule appends authority-form history while preserving the legacy graph contract. Older optional capsules fall back to the recording baseline. Older mandatory clips remain readable with an explicit warning that transition animation continuity is unverified. Restored ammunition delegates rebind to the correct existing reserve without calling `TryEquipWeapon` or resetting animation. Original failed replay logs and the initial incomplete campaign remain retained.

## Retained 8-client failures and prerequisite repair

`udp-8-impaired-7b932686` is FAIL: peer 3 could not serialize non-finite evidence; two offered turret shots lacked authority-native commits; and three shots had an observer playback gap near initial world admission. The missing peer report also invalidated the drain proof.

`udp-8-impaired-7bbdf4c8` is FAIL: finite activity instrumentation reported zero invalid samples and produced complete evidence, but the same class of two turret emission gaps remained. Removing the serialization failure did not establish 8-client acceptance. Both attempts are retained.

The current failure-counter contract requires each AI client to export nonnegative `InvalidPositionSamples` and `InvalidDistanceSamples`. Missing/malformed counters fail validation; either nonzero counter fails activity acceptance. Invalid samples must not inflate travel or silently become a passing report.

The unresolved turret defect separates an owner-authored offer from actual authority-native emission. Raw owner turret events can reach observers before the authority's native turret fires. Diagnostics identify a missing native target in one case and native cooldown followed by turret death in another. A prerequisite repair must give observers actual authoritative emission/refusal outcomes while preserving the original shot identity, source clock, native firing gates and one-time payment. This behavior is not fixed, and audit exceptions cannot substitute for that repair.

The failed acceptance manifest is [`net21-g1-20261011.json`](../tools/nettest/baselines/net21-g1-20261011.json), including source/binary identities and raw artifact hashes. Later audit fixtures require exact authority receipt, bootstrap revision and baseline frame before excluding pre-admission observer offers; missing historical evidence supplies no exemption. The expanded Python suite has 396 tests with one existing skip.

Server timing, allocation, queue and traffic measurements in campaign reports are descriptive observations for those runs. Matched before/after gameplay and performance comparisons are NOT_RUN; no performance improvement is claimed.

## Current limitations and required results

One bounded run covers one activity phase. Seeded AI input does not establish repeatable packet timing, all hunters/maps, alternative form fidelity, touch parity, death/respawn parity, reliable replay seek/presentation parity, sustained stress, or honest-player rejection rates. Existing G0 evidence gaps remain separately visible. A passing AI activity audit alone cannot establish the full plan's acceptance criteria.

Before reporting G1 acceptance, attach exact source and binary hashes, configuration and seeds, effective network profile, raw artifact hashes, existing parity/drain results, AI activity denominators, replay validation, CI results, measured performance, exclusions, and rollback instructions. Feature rollback is to omit `--ai-net-client`, `--ai-seed`, and `--server-bot`; compare against the same source/build and explicitly documented baseline configuration.
