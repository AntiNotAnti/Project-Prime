# G1 native AI network clients — evidence draft

This draft describes the opt-in G1 harness and its evidence contract. It records no passing G1 acceptance result, gameplay comparisons, or performance improvements. Run artifacts and actual checks must supply those results; the initial incomplete LAN attempt is retained as a limitation below.

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

Authority receipt/application denominators require process-lifetime `CampaignClaims.Received` and `CampaignClaims.Applied`. There is no fallback to `HitClaims.Received` or `HitClaims.AppliedHere`: those snapshots reset when the last peer leaves and the server suspends. The initial LAN attempt's exported receipt counter was zero and its activity result remains incomplete; positive operator observations cannot repair a missing lifetime denominator. A later run must export the required counters before claiming acceptance.

Each client report includes descriptive `lifecycleEvidence`. `SpawnedLives` counts AI decisions that observe a changed life ID, including the initial life; `AltFrames` counts AI decision frames spent in an alternative form. Positive counts are `OBSERVED`; zero counts are `NOT_RUN`. These counters do not prove lifecycle parity.

When authority `Facts` is present, exact 48-byte semantic packets supply canonical death events (`PlayerKilled` and `PlayerSuicide`) and spawn events (`PlayerSpawned`). The audit validates packet headers against outer evidence fields, validates actor/target identities, excludes baselines and the separate award lane, deduplicates by epoch/match/event ID, and rejects conflicting payloads. A respawn is counted only when a spawn follows a recorded death for the same epoch/match/slot/generation with a newer life. Initial or unjoined spawns remain spawn events, rather than assumed respawns. Missing facts leave these observations null with `NOT_RUN`; measured zero is also `NOT_RUN`. Alternative form entry/exit transitions remain null: sampled form frames do not provide an exact event ledger. These descriptive fields add no activity gates.

`paidAiSourceShotKeys` and each client's `paidSourceShotKeys` count unique source shot identities present in the authority payment evidence, with overflow rejected. These distinguish AI-origin paid shots from server bot payments. They are separate from the component payment denominator: one source shot may pay multiple victim/components. At least one paid AI source shot is required across the clients; there is no per-client paid-shot minimum. Missing key instrumentation remains null and yields `INCOMPLETE`; bot-only payments also yield `INCOMPLETE`. Malformed evidence yields `FAIL`.

Canonical respawn joins use serial-number comparisons for unsigned 16-bit life IDs and unsigned 32-bit ticks. Synthetic decoder tests cover wrap boundaries; they do not claim a campaign exercised a rollover.

## Replay repair — development verification only

Two replay repairs have been verified against both original AI demos using a development build whose source is not yet the final committed campaign source. The v7 replay capsule appends the authority-form cache while preserving the legacy graph contract. Older optional capsules fall back to the replay baseline; older mandatory clips retain an explicit warning that their transition is unverified. Restored ammunition delegates rebind to the existing player reserve without calling `TryEquipWeapon` or resetting animation.

After these repairs, the original demos passed determinism verification with 3,789 and 3,792 compared hashes, and replica verification with 3,788 and 3,791 compared hashes, respectively. These are development verification results, not immutable G1 campaign acceptance. Retain the original failed replay logs alongside the repaired-build results. The first LAN attempt also remains incomplete because its authority claim receipt snapshot reset; replay repair does not replace that missing lifetime denominator. Final G1 PASS requires new immutable campaigns with the final source/binary identities and all required checks.

## Current limitations and required results

One bounded run covers one activity phase. Seeded AI input does not establish repeatable packet timing, all hunters/maps, alternative form fidelity, touch parity, death/respawn parity, reliable replay seek/presentation parity, sustained stress, or honest-player rejection rates. Existing G0 evidence gaps remain separately visible. A passing AI activity audit alone cannot establish the full plan's acceptance criteria.

Before reporting G1 acceptance, attach exact source and binary hashes, configuration and seeds, effective network profile, raw artifact hashes, existing parity/drain results, AI activity denominators, replay validation, CI results, measured performance, exclusions, and rollback instructions. Feature rollback is to omit `--ai-net-client`, `--ai-seed`, and `--server-bot`; compare against the same source/build and explicitly documented baseline configuration.
