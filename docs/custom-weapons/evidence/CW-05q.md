# CW-05q evidence — private acquisition knowledge

Base SHA: `df3df17abf35447f5f1e173a8832af835a8802f8` (CW-05p merge).
Head SHA: the completed-slice commit containing this file.
Scope: new `CustomPickupKnowledge.cs`, new pure checks and one check-runner call.
[Behavior and next integration gate](../05q-pickup-knowledge.md).

Desktop Release PASS86.447s, zero warnings/errors. .NET10.0.401/macOS arm64,
DOTNET_PROCESSOR_COUNT=2, UseSharedCompilation=false. Final main SHA256:
`7c76ca77403d674faa5840be2b6d841b1a8f321d95b31c3f8ca15a2b99b7fd83`.
Android experimental Debug PASS89.230s,26 warnings/zero errors; default Debug
PASS163.048s,25 warnings/zero errors. Isolated headless-server compile
PASS129.619s, zero warnings/errors, preserving the main DLL hash. These are
compilation results, not device/server gameplay acceptance.

All18 custom build/test commands PASS on that exact main/tool-copy hash. Pure
contracts:15,403 PASS, including114 new knowledge assertions. The new cases fund
through a real `CustomPickupLedger` and initially empty `CustomSourceActionJournal`;
accepted native ingress metadata is explicitly synthetic. They establish model
admission behavior, not live network acquisition or authoritative damage.

The delayed-input counterexample captures empty source102 before publication of
source101/world120 funding. An echoed current nonce cannot upgrade102. Fresh103
with that nonce still refuses Fire ACK100; ACK120 admits. Source-first and
control-first echo order produce the same boundary. Duplicate ingress cannot
replace its original stamp/world. First/repeat funding, exact duplicate outcomes,
nonce rotation, bounded128 history, uint wrap, full owner/world/layout/bootstrap/
connection identities, malformed funding/codec values, retirement and reconnect
are checked. Rebind preserves funded resources while requiring new knowledge and
fresh source history; same-context retries remain idempotent after later input.
100,000 warmed capture/echo/inspection interactions allocate0B; nonce generation
is setup/transition work and leaves both gameplay RNG words unchanged.

Native registry and native MP/1P tables PASS. Native fidelity remains the known
46/47 with raw exit1: weapon.missile.splash tick177, victim.position4096.z
expected52814/actual52815. Imperialist headshot F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
No native enum, live packet, replay payload or existing gameplay mutation.

Raw commands, timing, hashes and logs: `artifacts/custom-weapons/CW-05q-pickup-knowledge/`.
Main source stayed frozen during compilation. Tool builds use
BuildProjectReferences=false. Earlier pre-idempotency build evidence is retained
separately; final tests use only the hash above. A read-only secondary audit found
no remaining concrete model bypass; its ingress/publication/privacy caveats are
recorded in the behavior document.

CI/review: NOT_WAITED per user instruction, never reported PASS.
No new physical-device, rendered, impaired live pickup transport, display-Hz or
complete platform-profile claim. No UI change; prior UI/GL evidence is historical.
Live protocol remains71. Custom replay stays blocked. Online pickup knowledge
wiring, explicit empty private state and reliable item baseline/delivery remain
release blockers. CW-05 and CW-16 are incomplete.

Rollback: revert this inert foundation/check/documentation commit; no match flag,
assets, resources or migration changed. Next gate: preserve these immutable stamps
through live controls/actions/input/results before enabling online world pickups.
