# N14 release integration and operator guide

Status: final replay/contracts verified; two local8 client canaries PENDING. This guide does not authorize or establish public deployment. Protocol remains 50, with no N14 protocol bump. Matching client/server artifacts are required; feature flags cannot bypass mixed-protocol refusal.

## Candidate and contracts

Final source `01292af8597a2bb1b88438390c4c9f87d953f2ef`, SDK10.0.401, engine SHA256 `e0fc25aadcad558b8f9d977cd6fd7e1eb043c5ce7da33eec9bc3518c2a258bca`. The build completed with33 warnings/0 errors. Exact commands, runner identity and outputs are bound in the ledger.

The initial contract set passed23/32, including339 Python checks, operator evidence, protocol generation/all-packet and custom contracts and native bot/game-mode checks. Corrected startup guarding, checkpoint/legacy fixtures and native custom readiness were subsequently validated. Contracts-06 passed all seven remaining lobby/native/cadence routes, including actual mixed-Hello UDP refusal in its lobby route at sourcea188d1be. Final01292af8 changes imported-clip capability/fixture scope; unchanged contracts carry forward under their actual tested source.

## Replay and visual scope

Final attempt07 passed all four replay routes: both historical replica checks, replay controls, and3013 format checks. Recording 1 verified2039 gameplay/presentation hashes,15786 animation restores, seven detached worlds/1996 continuation frames, and62 file/clip comparisons including frozen EOF. Recording 0 verified1143 hashes,8976 animation restores, four detached worlds/1032 continuation frames, and nine seek comparisons.

The replay fix resolves future lookahead facts at their first recorded snapshot whose server tick reaches the impact resolve tick, with delivery fallback, rather than freezing against a distant prior clock. 14 previously omitted projectile fields are checkpointed. Imported clips declare their source capabilities; current capability remains the default. Clip fixtures use absolute source clocks, canonical wire capabilities and bounded retained lookahead with visible EOF.

Both contracts-06 cadence checks used actual GL with600 identical gameplay frames each at virtual 60/120/144/240/360/540 Hz. Caffeinate and unchanged binary/profile hashes are recorded. This proves cadence/state invariance for those fixtures. Rendered Studio open/layout/close/reopen, HUD/seek UX and live RmlUi multiplayer join remain NOT_RUN.

## Operator evidence and response

`PRIME_SERVER_OPERATOR_OUTPUT` is default-off. `run-net2.py --server-operator` writes `server.operator.json` at authority shutdown; direct operators supply a fresh path. CreateNew preserves existing reports. The closed schema contains Schema, Protocol, Phase, Queue, Traffic, ClaimVerdictResponses and VerdictCountingUnit. It excludes endpoint, peer identity and raw evidence objects.

Queue reports Current, HighWater and Drops. BufferExhaustions is null and BufferExhaustionStatus=`UNAVAILABLE`; missing measurement is not zero exhaustion. Traffic contains packet/byte totals. Verdict responses count authority Answer decisions, including retries, rather than unique rejected shots or delivered acknowledgements.

On gameplay/drain failure, preserve private evidence/source/hashes/settings, stop widening rollout and diagnose the exact gate. Queue growth/drop aggregates warrant load/traffic investigation; they do not independently identify an endpoint or prove a DoS diagnosis. Buffer exhaustion needs explicit measurement. Protocol refusal requires matching artifacts, not disabling admission checks.

## Rollout and rollback

1. Freeze matching server/client artifacts, source, protocol, feature settings and hashes.
2. Validate isolated local canaries and exact gameplay/readiness/drain/operator evidence. Local success does not establish external staging or capacity.
3. When staging hardware and deployment authorization are supplied, update dedicated servers before distributing matching clients; validate admission and required release gates before expansion.
4. For rollback, stop new admissions and restore a previously validated matching server/client set together. Preserve candidate/failed evidence. Do not use feature flags to bypass protocol refusal.

Leave `PRIME_CONFIRMED_IMPACTS`, `PRIME_MOVEMENT_VALIDATION_SHADOW`, `PRIME_SERVER_PERF_OUTPUT` and `PRIME_SERVER_OPERATOR_OUTPUT` unset to disable optional diagnostics/presentation. Omit their runner flags. Movement shadow does not enable enforcement. Client `-nointerp` disables interpolation while retaining snapshot-owned puppets; `-relayedpuppets` also changes the pose source and is a historical diagnostic, not a narrow interpolation rollback.

## Retained failures

| Evidence | Classification |
| --- | --- |
| Contracts-01 nine failures | Startup absent-world bomb accessor; legacy/current checkpoint and custom-readiness fixtures; old replay continuation/seek issues; unavailable display cadence. Original outputs retained. |
| Attempts 02–05 replay diagnostics | Actual future-lookahead/clock resolution defect, including frame 423 effect/RNG divergence. Temporary tracing removed; final historical continuation now independently passes. |
| Attempt-06 frozen clip | sourceOrigin 31 ignored, protocol 41 capability assumed current, and finite boundary discarded file 960 lookahead. Original failure retained; final source and62 file/clip comparisons are separate positive evidence. |
| Initial cadence without active monitor | Environment failure, retained separately from display-awake actual-GL positives. |

Format-valid recordings alone were never treated as successful playback. Diagnostic causal checks and intermediate builds do not replace final acceptance.

## Release limits

N13 local timing remains INCONCLUSIVE with original p99 flags retained; target-host validation is NOT_RUN. Live Settlement telemetry remains INCOMPLETE despite valid default-Shadow UDP gameplay. No optimization, default change, performance gain or full capacity certification is claimed.

N12 movement shadow is advisory/default-off; its all seven hunter native tour does not establish the incomplete native-equivalent authority kernel. Stable alt forms remain unsupported; movement authority stays disabled/deferred. Headless delivery is not rendered same-shot fidelity. Prior milestone ledgers retain their exact combat/pixel/replay scopes.

External staging/public deployment and rendered Studio/RmlUi are NOT_RUN. Linux, Android, CI, long soaks and unnecessary Windows checks were excluded from this execution scope. No staging target is supplied. Remaining deployment/visual/performance gates must be completed when their environment is available; local implementation readiness does not certify them.

## Final local canaries

Two serial 8-client moderate-fault canaries used 20 seconds of action and the same 20-second maximum drain on the frozen final engine. Control and experimental arms passed strict base adjudication, bounded drain, operator/performance schema validation and process exit0. Experimental movement and confirmed-impact audits passed without failures or incomplete findings. The engine hash remained unchanged.

These are matched local settings, not identical timing or staging. All eight peer byte totals and measured intervals, server segments and performance operation counts are recorded in `canary-summary.json`. Spectator, map rotation, rendered projectile agreement, bot fill and long-duration soak were not exercised by these canaries; their prior milestone evidence remains separate. Server dropped steps were 6 control/2 experimental, with one stall each; queue drops and final queue were zero in both arms. No clean-performance or capacity claim is made. Movement shadow measured Sylux biped only: 7,991 evaluated observations and zero would-refuse. This is not all-hunter UDP coverage. Impact evidence published 214 facts; peers missed 0–5 cosmetic facts and recorded no draw submissions. Its audit PASS permits lossy presentation and proves neither perfect delivery nor visible pixels. Settlement telemetry had only one control rejection (result9) and zero experimental calls; live settlement coverage remains INCOMPLETE. Optional features remain opt-in.
