# G1 native AI network clients — bounded harness accepted

The opt-in G1 native AI network harness is implemented and closed for its declared bounded acceptance. Immutable source `3a0d5c834840372c3e55965047be0d9e86ef34e5`, protocol 52, and engine SHA-256 `2b61866932e60796f3d02a163018c6d6a22c3374c816d783b336c1afe58159b9` passed all five current campaign parity/drain and AI activity audits, 23 focused contract routes, the immutable Python suite, all 27 CI checks, and both replay modes for six independent recorded demos. The final raw ledger reports PASS with no failures or incomplete inputs. The committed acceptance manifest is [`net21-g1-20261012.json`](../tools/nettest/baselines/net21-g1-20261012.json), referencing `artifacts/net21-g1/ledger-3a0d5c83-final.json` and its raw artifact hashes. Earlier failed attempts remain retained. A1 and later gameplay slices are not yet implemented; R0 release readiness remains incomplete.

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
  --protocol 52 --players 2 --seconds 90 --drain-seconds 20 \
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

All five runs bind the same source and unchanged engine bytes. They used native AI level 2, AI seed base 1729, fault seed 8128, room `MP1 SANCTORUS`, and a 35-second bounded drain. The LAN and rendered bot runs were recorded; the eight-client retry2 was also recorded. Two- and four-client impaired runs used 150 ms RTT, 20 ms jitter, 5% loss, 2% duplication and 5% reordering; eight-client retry2 used 250 ms RTT, 40 ms jitter and the same loss/duplication/reordering percentages. The first four runs requested 60 seconds; eight-client retry2 requested 180 seconds.

| Current campaign | Campaign / AI audit | Declared claims | Authority receipts including retries | Applied claims | Component payments | Paid AI source keys | Owner native pickups | Canonical deaths / spawns / respawns |
|---|---|---:|---:|---:|---:|---:|---:|---|
| `udp-2-bot-lan-3a0d5c83` | PASS / PASS | 13 | 13 | 0 | 25 | 14 | 9 | 2 / 6 / 2 |
| `udp-2-bot-rendered-3a0d5c83` | PASS / PASS | 32 | 36 | 1 | 48 | 28 | 15 | 6 / 10 / 6 |
| `udp-2-impaired-3a0d5c83` | PASS / PASS | 17 | 27 | 4 | 22 | 22 | 8 | 4 / 7 / 4 |
| `udp-4-impaired-3a0d5c83` | PASS / PASS | 105 | 123 | 2 | 73 | 73 | 16 | 9 / 16 / 9 |
| `udp-8-impaired-180s-3a0d5c83-retry2` | PASS / PASS | 882 | 1068 | 20 | 552 | 541 | 108 | 81 / 95 / 81 |

Component payments can include multiple victim/components per source key; these columns are separate denominators. Owner native pickup consumption is not authority grant ownership. Canonical lifecycle events are descriptive observations, rather than proof of death/respawn presentation parity. The two bot campaigns observed 333 and 794 server-bot shot keys, respectively, in authority bot slot 2. Every eight-client retry2 client reported zero invalid position and distance samples.

The 23 focused contract routes passed with stable before/after engine and fixture hashes in `contracts-3a0d5c83/results.json`. The immutable Python suite ran 414 tests, reporting OK with one explicit skip. `ci-3a0d5c83-final.json` binds this exact source and contains all 27 checks COMPLETED/SUCCESS. Required recorded acceptance consists of two independent demos per prefix, each passing determinism and replica continuation: `acceptance-lan-3a0d5c83`, `acceptance-rendered-3a0d5c83`, and `acceptance-eight-3a0d5c83` (recorded slots 6 and 7). All six recorded demos passed both modes with unchanged engine bytes; the final ledger binds their source, binary, demo and raw log hashes.

The v7 replay capsule appends authority-form history while preserving the legacy graph contract. Older optional capsules fall back to the recording baseline. Older mandatory clips remain readable with an explicit warning that transition animation continuity is unverified. Restored ammunition delegates rebind to the correct existing reserve without calling `TryEquipWeapon` or resetting animation. Original failed replay logs and the initial incomplete campaign remain retained.

## Retained failures and prerequisite repair

`udp-8-impaired-7b932686` is FAIL: peer 3 could not serialize non-finite evidence; two offered turret shots lacked authority-native commits; and three shots had an observer playback gap near initial world admission. The missing peer report also invalidated the drain proof.

`udp-8-impaired-7bbdf4c8` is FAIL: finite activity instrumentation reported zero invalid samples and produced complete evidence, but the same class of two turret emission gaps remained. Removing the serialization failure did not establish 8-client acceptance. Both attempts are retained.

The current failure-counter contract requires each AI client to export nonnegative `InvalidPositionSamples` and `InvalidDistanceSamples`. Missing/malformed counters fail validation; either nonzero counter fails activity acceptance. Invalid samples must not inflate travel or silently become a passing report.

The short `udp-8-impaired-a0a120b5` passed its campaign audit but observed zero turret workload, so turret coverage remains NOT_RUN. `udp-8-impaired-180s-a0a120b5` remains FAIL because peer 7 recorded four invalid position samples despite receiving its 17 turret outcomes. The first current-source 180-second run failed disk exhaustion and retained truncated server evidence; retry1 failed staging-adapter import before gameplay. Retry2 supplies fresh acceptance evidence and does not rewrite these failures. Their references and explicit statuses are retained separately from the current gates.

The current eight-client retry2 observed 14 native turret emissions, seven refusals (four OwnerRetired and three RecoveryExpired), 168 matching peer receipts against 168 required, and 56 refusal applications against 56 required. All live cancellation counts were zero. These turret workload labels are OBSERVED; live active-flight cancellation is NOT_RUN. The passing synthetic production-path cancellation fixture establishes only its declared fixture scope. See [Authority turret outcomes](netcode21-turret-outcomes.md) for the full contract and retained history.

The implemented protocol-52 outcome path separates owner offers from authoritative native emission/refusal while preserving the original shot identity, source clock, native firing gates and payment rules. The initial-admission proof remains exact and required. The APFS copy-on-write staging adapter is an artifact wrapper with its own recorded SHA-256; it dereferences symlinks and preserves independent copies of unchanged game bytes. The ledger binds its hash independently from source and engine identity.

The historical failed acceptance manifest is [`net21-g1-20261011.json`](../tools/nettest/baselines/net21-g1-20261011.json), including source/binary identities and raw artifact hashes. Later audit fixtures require exact authority receipt, bootstrap revision and baseline frame before excluding pre-admission observer offers; missing historical evidence supplies no exemption. That historical expanded Python suite had 396 tests with one existing skip; the current immutable suite has 414 tests with one explicit skip.

Server timing, allocation, queue and traffic measurements in campaign reports are descriptive observations for those runs. Matched before/after gameplay and performance comparisons are NOT_RUN; no performance improvement is claimed.

## Current limitations and rollback

Coverage of all hunters/maps, alternative form fidelity, touch parity, death/respawn presentation parity, broad replay presentation parity, honest-player rejection rates, spectator coverage, map rotation, sustained 20-minute stress, live active-flight cancellation, rendered projectile agreement, exact alt entry/exit event parity, native Weavel physics parity, matched before/after performance improvement, and R0 readiness remain unestablished. The explicit campaign coverage fields for spectator, mapRotation, sustained20Minutes, turretCancelledFlights and renderedProjectileAgreement are NOT_RUN in the current runs. Server timing, allocations, queue and traffic are descriptive run measurements; no matched performance improvement is claimed. A1 onward remains unimplemented. Closing G1's declared harness does not close these later slices or the release gate.

Feature rollback omits `--ai-net-client`, `--ai-seed` and `--server-bot`. Protocol 52 requires matching server/client deployment; reverting the turret prerequisite requires reverting its protocol and replay producers together. Compare rollback behavior with explicitly matched source/build and baseline configuration.
