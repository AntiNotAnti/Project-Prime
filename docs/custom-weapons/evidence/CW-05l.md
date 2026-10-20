# CW-05l evidence — source-fenced pickup funding

Base: `6f98b1312c2a534308a24f5d5cb63a0658f33c20` (latest main after PR845).
[Contract and caller obligations](../05l-pickup-funding.md).

Empty life journals can accept trusted, next-source pickup ownership while
preserving native release history and the life clock. Repeated reserve grants
remain ordered around delayed controls, reload completion and interruption.
World identity, owner life and per-spawn incarnation fences precede mutation;
funding buffers and high-water marks have explicit hard capacities.

Desktop Release PASS 74.907s, zero warnings/errors; macOS arm64, .NET10.0.401,
DOTNET_PROCESSOR_COUNT=2. Final main SHA256:
`002cf277f82ec9ac5b8f0cff4b23fb7eeeff3b3832649dfd8f399c8a16fe271b`.
Source was frozen through desktop/Android builds. Tool builds use
BuildProjectReferences=false; copied main hashes match before/after all checks.
Android Debug experiment PASS 75.735s (26 warnings); default PASS 68.321s
(25 warnings), both zero errors. These are compile checks, not new device runs.

| Acceptance | Result |
| --- | --- |
| Focused pickup funding | 143 assertions PASS |
| Full pure custom contracts | 14,950 checks PASS |
| All custom commands including tool build | 17/17 PASS |
| Existing actual offline availability/window launch | 18/17 PASS; transitional public spawn grants remain |
| Scope/local/developer offline/public client | 37/28/46/24 PASS |
| Selection/projection/replica | 180/145/173 PASS |
| Readiness/admission/source/authority/rewind | 127/249/318/1,014/156 PASS |
| Synthetic controller mapping | 75 PASS; physical device not exercised |
| Native registry and tuning | 78 names preserved; exact MP/1P tables PASS |
| Fidelity | Established 46/47, raw exit1 |

Pickup fixtures supply trusted contact and source oracles. They prove empty
life/native release, source+1 acquisition including uint wrap, pre-acquisition
queued Fire/Equip refusal, ordered refill receipts, interrupted reloads,
pending/rejected admission, reentrant refusal, resource-preserving emitter
capacity failure, foreign context refusal, exact 64-spawn/128-addition bounds
and retry without consuming refused incarnations. Repeated full-reserve funding
caps reserve without resetting the magazine. 100,000 warmed previews over 32
pending additions allocate zero bytes and leave all committed state unchanged.
Value projections match the established state preview across exact cooldown and
reload deadlines, empty/canceled/invalid states and wraparound.

Fidelity retains the known weapon.missile.splash tick177 divergence:
victim.position4096.z expected52814/actual52815. Imperialist headshot F2 is
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Raw commands, timing, exits and hashes: `artifacts/custom-weapons/CW-05l-pickup-funding/`.

This foundation does **not** activate world pickups, remove public automatic
spawn grants, implement global item arbitration/respawn or replicate pickup
state. Public engine wiring, placements/rendering, ordinary selection,
reconnect/empty-life publication and final device/release acceptance remain
required. No protocol or packet changes are introduced. CI/review are neither
awaited nor reported PASS. CW-05 and CW-16 remain incomplete.
