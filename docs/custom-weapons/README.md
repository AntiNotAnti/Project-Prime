# Custom weapons implementation ledger

Source specification: `PROJECT_PRIME_CUSTOM_WEAPONS_SNIPER_HEADSHOT_RAGDOLL_IMPLEMENTATION_PLAN.md`
supplied by the user on 2026-10-19 and preserved as [implementation-plan.md](implementation-plan.md). The user's request authorizes sequential CW-00 through CW-16,
one independently reviewable PR and completed-slice commit at a time, immediate merge after
local acceptance, and **no wait for review or CI**. CI queued/running is never reported as PASS.

Initial source: `d726c766a10affaa0d8f95bc25bce9d178528215` (latest fetched main at kickoff).
Implementation uses an isolated managed worktree; unrelated working-copy edits are excluded.

| Slice | Deliverable | Status |
| --- | --- | --- |
| CW-00 | Source/behavior baseline and five ADRs | Complete; documented baseline failure/platform gaps |
| CW-01 | Immutable identity and registry | Complete; 69 checks and native parity passed |
| CW-02 | Mechanics schema and deterministic per-life state | Complete; 1,179 checks, zero-allocation routine ticks, native parity |
| CW-03 | Offline M7 through canonical combat hooks | Complete; 1,181 pure and 45 real-scene checks; native parity |
| CW-04 | Custom action authority/network state | Authority implementation complete through04q: live private results, public recipient replicas, exact source/resource/contact admission, impaired ordinary-client owner symmetry, spectator reconnect/old ACK refusal and native client HP/death projection validated. Developer flags remain off; public controls/device cadence are CW-05 and custom bots/packages/replay are CW-12. Physical/rendered/WAN acceptance remains a release gate. |
| CW-05 | Loadout, input, HUD and scope | In progress:05a canonical client request producer,05b bounded source-time selection history,05c authority native-fire isolation and05d public device bindings/local input isolation and05e optics/sensitivity/hold settings, canonical HUD and reversible feedback;05f reserved frozen availability contract and shared eligibility policy;05g protocol70 frozen online activation and public readiness;05h launcher choice and captured-human offline availability; Android async settings, accessible in-game selection and final device acceptance pending |
| CW-06 | Original assets and presentation | Pending CW-05 |
| CW-07 | Accepted nonlethal head reaction | Pending CW-06 |
| CW-08 | Lethal head collapse/cosmetics | Pending CW-07 |
| CW-09 | Experimental physics pilot | Pending CW-08 |
| CW-10 | Hybrid corpse rollout/fallback | Pending CW-09 |
| CW-11 | Safe local packages and installation | Pending CW-10 |
| CW-12 | Match readiness and replay manifest | Pending CW-11 |
| CW-13 | Typed Community weapon publication | Pending CW-12 |
| CW-14 | Studio creator | Pending CW-13 |
| CW-15 | Community creator/install UI | Pending CW-14 |
| CW-16 | Full release/security/platform acceptance | Pending CW-15 |

No pending row denotes delivered runtime functionality. Device/visual/network gaps remain explicit
release blockers. Foundation code alone cannot satisfy the final create/publish/download/play/replay flow.

Evidence lives under [evidence](evidence/); local complete logs under
`artifacts/custom-weapons/CW-NN/`. Production game files and private credentials never enter Git.
