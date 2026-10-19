# CW-00 baseline — 2026-10-19

## Source and environment

- Repository: `AntiNotAnti/Project-Prime`.
- Latest fetched main at kickoff: `d726c766a10affaa0d8f95bc25bce9d178528215`,
  “Merge pull request #713 from AntiNotAnti/movement/pr04-collision-specials”.
- Isolated branch: `codex/custom-weapons-cw00`. The primary checkout contains unrelated
  uncommitted gameplay/UI/network changes and was not reset, stashed, committed or included.
- Host: macOS 27.0.1 (26A434), Darwin arm64; SDK `/Users/jarrett/.dotnet/dotnet`
  10.0.401 as pinned by `global.json`. The default PATH dotnet is a runtime-only installation.
- Android workload installed: 36.1.69/10.0.100; this is tool discovery, **not** Android acceptance.
- Extracted AMHE1 content exists locally; no extracted assets are committed.

## Concurrent work and CI observation

At kickoff, PR [#714](https://github.com/AntiNotAnti/Project-Prime/pull/714)
adds beam arsenal presentation, touching `BeamProjectileEntity.cs`, cosmetic definitions,
network cosmetics and `InstagibIonFracture.cs`. Recheck its merge state before CW-03/06/07.

Main CI observed after kickoff: scene ownership PASS; build IN_PROGRESS; Replay protocol
compatibility and Momentum math QUEUED; campaign acceptance CANCELLED.
[Build](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39307720070),
[replay](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39307719965),
[scene ownership](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39307719602).
These are point-in-time observations; queued/running/cancelled are not passing evidence.
The user explicitly requests immediate merge after completed local acceptance without waiting
for review or CI. Preserve that instruction while reporting actual statuses.

## Architecture and decisions

Read `ARCHITECTURE-INVARIANTS.md`, relevant `CLAUDE.md` workflows and actual source.
[Integration inventory](01-integration-inventory.md) references identity, inventory, native and
accepted firing, source/payment proof, damage/headshots, claims, replay, HUD, death/model replacement,
bots, pickups, Gun Game, saves, analytics and Community lifecycle.

Current live protocol is **58**, not the stale 57 in architecture prose. The current optional
unscoped Imperialist penalty halves both body and headshot channels. Preserve these actual
semantics. The CW-00 source audit does not change either behavior.

Accepted foundation decisions:

1. [Identity and native isolation](adr/001-identity.md).
2. [Separate versioned custom input](adr/002-custom-transport.md).
3. [Confirmed headshot presentation facts](adr/003-headshot-facts.md).
4. [Experimental corpse backend and fallback](adr/004-corpse-backend.md).
5. [Mechanics versus archive digests](adr/005-package-digests.md).

No custom packet number, production physics backend or package capability is enabled by this audit.
CW-04 must approve actual wire bytes with fixtures before transport changes.

## Invariant ledger

| Invariant | Baseline boundary / future proof |
| --- | --- |
| Native values/arrays/affinity/save bytes unchanged | No production source edits in CW-00; source SHA-256 guard in evidence |
| One authority and one payment per accepted component | AcceptedShotLedger, NetShotProof, source journal; native tests recorded separately |
| Fixed 60 Hz; display independent | FrameTimingCheck is arithmetic coverage, not measured render performance |
| Old demos/checkpoints/killcam readable | Native archive/replay fixtures; custom recording requires explicit schema and manifest |
| Exact custom identity before Ready | No custom sessions exist at baseline; CW-04/CW-12 must add fail-closed admission |
| Packages contain declarative data/assets only | Existing typed validator refuses gameplay content; CW-11/CW-13 need dedicated validation |
| Reactions/corpses never affect gameplay transforms | Existing cosmetic lifecycle remains; new overlays/physics disabled until validated |
| Existing Community identity/auth preserved | Typed catalog and map projections already exist; no new catalog/auth subsystem |
| No unlicensed committed assets | This slice adds text/tooling only |
| Default-off and reversible | No runtime entry points or flags introduced in this slice |

## Reproduction and evidence limits

Run `python3 tools/custom-weapons-baseline/run.py --dotnet /path/to/dotnet`.
The runner retains exact commands, durations, exits and local logs under
`artifacts/custom-weapons/CW-00/`; it isolates writable user data.
Individual test results and explicit release blockers are in [CW-00 evidence](evidence/CW-00.md).

The first shared-compiler build attempt was cancelled after the shared service changed while
other worktrees were building. Retry uses `-p:UseSharedCompilation=false` and does not kill
other worktrees' compiler/server processes. Compiler wait time is not gameplay timing.

No headless/synthetic check is physical Android, rendered seven-Hunter, real two-client/WAN
headshot, or frame-time percentile evidence. Those rows require their actual environment.
