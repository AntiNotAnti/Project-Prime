# CW-16 — release acceptance and remaining qualification

Status: implementation review, **not release-qualified**. This document records source-level implementation and unresolved acceptance work. The user waived remaining tests to continue implementation through CW-16. Every new runtime, security, multiplayer, UI and performance acceptance item below is **NOT RUN** under that waiver. Android is **DEFERRED**. A successful compilation establishes API/build consistency only; it does not establish gameplay correctness or release acceptance.

## Implementation versus qualification

| Area | Source implementation | New acceptance status / release gate |
| --- | --- | --- |
| Exact weapon packages | Bounded package parser, canonical mechanics/content/dependency/archive identities, immutable store and independently retained leases | NOT RUN: adversarial archives, replacement/uninstall races, cancellation, corrupt/unsupported assets, memory ceilings |
| Actual pickup gameplay | World placements and pickup incarnations fund ownership/ammo through the common source journal; no spawn grant or native weapon alias | NOT RUN: pickup collision, simultaneous claims, respawn/refill, map rotations, form/death transitions |
| Multiplayer | Exact package manifest preparation/readiness, source lifecycle fencing, ordinary transport and dedicated/local/remote launch integration | NOT RUN: missing bytes, wrong hash/definition, late join, disconnect/reconnect, packet loss/reorder, allocator lifecycle and mixed versions |
| Replay capture and seeking | Explicit custom descriptor/checkpoints/worlds, recipient/authority origins, format7/8 phase records, exact resources, private reconstruction, retained head history | NOT RUN: new integrated capture/reopen, linear/cold seek parity, stale/future facts, checkpoint boundaries, terminal deferred suffix, source clocks and quiet frames |
| Replay consumers | Theatre, phase-preserving clips/killcam, detached export, exact package portable member and PCM projection, optional captured owner lens | NOT RUN: interactive pause/seek/cleanup, camera controls, life/form audio cancellation, source policy preservation, export/portable round trips and missing exact bytes |
| Authority bots | Explicit opt-in actual authority bot sources, pickup navigation through existing motor, common action/payment/contact journals; zero-human startup and ordinary late join | NOT RUN: eight bots; seven bots plus human; pickups, hits, old-life projectile contacts, fairness, load and autonomous rotation |
| Community CW-13 | Typed weapon kind, full package validation, exact revision identities, creator ownership, quota/revision limits, authenticated transfer witnesses, explicit installation, report/moderation paths | NOT RUN: permission bypass, cross-account retry/install, owner/draft/unlisted visibility, publication races, malicious metadata/blobs, moderation and quota enforcement |
| Separate Studio CW-14 | Separate Studio application/process integration and source-level command/ownership bridge | NOT RUN: launch/focus/close/restart, malformed/stale IPC, process isolation, concurrent documents, no game-owned resource leakage |
| Creator UI CW-15 | Native RmlUi weapon editor and typed Community browsing/publication/transfer/install integration | NOT RUN: genuine rendered workflows, keyboard/controller/accessibility, async stale result retirement, account switching, recoverable errors and discoverability |
| Performance | Bounded record/byte/worker/job/identity capacities in source | NOT RUN: frame time, allocation, audio callback behavior, server catch-up, export duration, disk/memory pressure and cancellation latency |
| Compatibility | Native/default paths retained with explicit custom contracts and feature gates | NOT RUN: native replay byte/behavior regression, ordinary weapons/bots/maps, historical supported fixtures, packaged deployment |
| Android | No new Android acceptance work authorized in this sequence | DEFERRED |

The implementation column is a source inventory, not an assertion that every plan acceptance item is satisfied. Detailed slice documents and code remain authoritative for exact restrictions. Any restriction below remains a release blocker until resolved or explicitly excluded from a release's documented scope.

## Historical evidence is separate

Earlier qualified slices retain their original receipts in `docs/custom-weapons/evidence/` and `artifacts/custom-weapons/`. Examples include prior real two-client pickup/contact capture, strict private world continuation, resource lifetime and rendered presentation work. Those results apply to their recorded commits, binaries, fixtures and settings only. They do not qualify the later combined Community/Studio/bot/audio/consumer tree. No historical counts are promoted into a new acceptance result here.

Compilation receipts, when present, must identify source revision, build configuration and binary hash. They must remain labelled compilation rather than runtime/security qualification. No new tests were run for this document.

## Delivered slices and observed compilation

CW-12 implementation ends at [12k](12k-export-audio-and-owner-lens.md), following
the authority recorder, ordinary playback, clips/killcam, portable/audio and bot slices.
CW-13 [PR #1017](https://github.com/AntiNotAnti/Project-Prime/pull/1017), CW-14
[PR #1018](https://github.com/AntiNotAnti/Project-Prime/pull/1018) and CW-15
[PR #1020](https://github.com/AntiNotAnti/Project-Prime/pull/1020) are merged. This
CW-16 slice records the assessment and current architecture contracts; it runs no
acceptance tests and does not authorize a production deployment.

| Compilation | Observed result | Local receipts |
| --- | --- | --- |
| CW-13 Release desktop / dedicated, combined draft worktree | Exit 0; 0 errors, 2 / 1 existing warnings; 98.17 / 69.92 seconds | `artifacts/custom-weapons/CW-13/compile-final/` |
| CW-14 paired Studio/game, Release win-x64, explicit global SelfContained=true | Exit 0; 0 errors, 2 existing warnings; 101.89 seconds | `artifacts/custom-weapons/CW-14/compile-fixed/`; initial NETSDK1150 configuration failure retained in `compile/` |
| CW-15 Release desktop including Starting/late-join review fix | Exit 0; 0 errors, 2 existing warnings; 96.90 seconds | `artifacts/custom-weapons/CW-15/compile/` |
| CW-16 final integrated dedicated, separate output directory | Exit 0; 0 errors, 1 existing warning; 61.09 seconds | `artifacts/custom-weapons/CW-16/compile/` |

The initial CW-16 dedicated source is `f1d56ba879108ba8f33b88bb97f6edb966233979` with only
this slice's documentation uncommitted. Binary SHA-256:
`1002b7a97598dc3cf85953611bbdacae88648a4b0d9bbc972a2556f13469c46f`;
log SHA-256 `cd464a27cf93fa1ff21adb9c3a9a091532ff5b3ff3f0ea2cae1992d8af1dba6d`.
The later integration between the CW-15 build and final source added upstream CI/tool
fixes, not production C# changes. The initial CW-16 assessment changed documentation only. Source comparison
also leaves protocol85, native weapon enum/metadata and canonical pose exclusions
unchanged; this is a source observation, not a native runtime regression pass.

### Final integration follow-up

After that recorded build, main added cinematic final-kill presentation. Compiling
merged `7c42846cef64e2072d63034fc53fe04917a9bb74` exposed CS0136: the new cinematic
FOV local conflicted with the first-person FOV local. The first-person local is now
named `ownerFov`, preserving its existing finite checks, clamping and camera values.
The failed receipt remains in `CW-16/compile-integrated/`.

Final source review also found that valid catalog mutations could exceed the 32 MiB
restart reader limit. All catalog writes now serialize through the same bounded stream.
New revisions preflight it before publishing their immutable object; visibility,
archive and report/moderation updates reject over-budget metadata before persistence
or in-memory adoption. This closes the source-level oversized-catalog/repeated-budget-
refusal object accumulation path. Disk failure may still leave an unreferenced immutable
object under the existing retention policy; this change does not delete shared bytes.
The corrected compilation receipts are in `CW-16/compile-integrated-fixed/`.
Catalog boundary/restart/fault and cinematic runtime acceptance remain **NOT RUN**.

The corrected Release builds on that merged base plus this follow-up both exited 0:
desktop 0 errors / 2 existing warnings in 76.27 seconds; dedicated 0 errors / 1 existing
warning in 56.88 seconds. Receipt JSON retains source/configuration, log and binary
SHA-256 values. Independent read-only review found no further concrete issue in these
two changes. No tests, gameplay, rendered captures or Android checks were run.

## Threat model and required negative cases

- **Untrusted package bytes:** archive traversal/links/duplicates, expansion and member limits, malformed model/PCM/project metadata, unsupported mechanics, changed archive bytes and digest mismatch. Refuse before activation; retain no executable/plugin/script capability.
- **Untrusted network peers:** forged owner/life/start/source, missing exact manifest readiness, stale/duplicate actions, fake resource knowledge and pickup claims. No peer-supplied filesystem paths, fabricated bot peers/ACKs, native aliasing or bypass of launch payment/contact journals.
- **Community creators/accounts:** server derives ownership from verified credentials; a client OwnerId is not authority. Publication must not overwrite another owner, mutate exact prior revisions, bypass expected-parent checks or creator/global quotas. Account changes must not resume or install private jobs for another subject. Persist witnesses, never credentials.
- **Visibility and moderation:** browse, exact project/revision reads, raw object hashes, previews/media and dependencies must agree. Hash knowledge alone must never expose Draft or moderated/archived private content. Historical public availability and lifecycle retention need explicit semantics.
- **Replay and portable input:** validate metadata, all consumed chunks/checkpoints, exact package identity, lengths/CRC, phase order and combined budgets. Preserve original clocks and producer presentation policy. Deferred final records are inert. Never fall back to native interpretation, current mechanics or replacement assets after custom admission fails.
- **Workers/processes:** retain exact accepted inputs and independent child leases; cancel stale work and dispose late successes. Keep CPU preparation off the packet/simulation loop and Scene/device ownership on its correct thread. Studio IPC and detached exports must not accept arbitrary file destinations or commands.
- **Audio/rendering:** private projection never pays ammo, applies damage, advances gameplay RNG or changes global owner/spectator state. Offline audio uses exact package PCM and CPU facts, not native sound substitution. Seek/baseline reconstruction must not replay historical cues as new events.

## Source-discovered unresolved gates

1. **Weapon Unlisted exact-link semantics are implemented but NOT RUN.** Weapon-specific exact Get/Revisions and exact raw blobs admit Published/Unlisted while browse remains Published-only. Draft, archived and moderation-hidden content remains unavailable to nonowners. Unlisted-only direct projects derive public name/author from the validated visible package and suppress draft-inherited description/tags/presentation metadata. Generic other kinds are unchanged. This resolves the source gap found during this audit; its security/runtime acceptance remains NOT RUN.
2. **No release claim from waived acceptance.** Source review cannot establish transport timing, UI ownership, audio fidelity, bot combat or strict world parity. New integrated acceptance remains NOT RUN even when code compiles.
3. **Historical provenance restrictions remain deliberate.** Unsupported or absent owner-lens/audio/history coverage must retain truthful documented fallback/refusal behavior. Do not invent authority World13, recipient bootstrap, recording facts or source knowledge to satisfy a consumer.
4. **Private transfer isolation includes UI.** Async admission/network/completion/install are bound to the original verified account; transfer lists and install controls must also filter account-bound private downloads. Public synchronous installation must continue to refuse private receipts.
5. **Portable import never silently installs.** A validated retained package can be installed only through explicit user action. Missing installation is a safe refusal with the retained exact package path; native v1 import must remain compatible.

These items are intentionally visible rather than represented as completed acceptance. The source-discovered visibility issues were repaired in CW-13; retain the NOT RUN labels until real qualification is authorized and performed.

## Unresolved release blockers

| ID | Required before a qualified release | Current disposition |
| --- | --- | --- |
| CW16-B01 | Integrated Community authorization, draft/unlisted privacy, moderation, quotas, malicious packages, restart integrity and original-account transfer/install isolation | NOT RUN under user waiver |
| CW16-B02 | Actual dedicated recording/reopen, custom bots, late join/reconnect/loss, pickup-only acquisition and authoritative combat on the final tree | NOT RUN under user waiver |
| CW16-B03 | Rendered Studio and player create/publish/download/install/join flow, cancellation/crash/lease ownership, keyboard/controller/accessibility | NOT RUN under user waiver |
| CW16-B04 | Replay seek/clip/killcam/portable/video/audio round trips and presentation fidelity for the exact historical package and owner coverage | NOT RUN under user waiver |
| CW16-B05 | Disabled/enabled p50/p95/p99 frame times, allocations, network bytes, install latency/failure rates, eight-player/bot/server/export load and disk pressure | NOT RUN; no new measurements are claimed |
| CW16-B06 | Cross-platform packaging, deployment/updater compatibility and safe rollback rehearsal for the exact source/binary/schema set | NOT RUN; Android builds/emulators/devices explicitly DEFERRED |
| CW16-B07 | Native gameplay, weapons, maps, bots and historical replay compatibility on the final combined tree | NOT RUN; earlier receipts remain historical |
| CW16-B08 | A documented creator release scope | MVP is original rigid-box/precision JSON editing plus static preview and a separate pickup-test game. Arbitrary converters, animated editor preview, Hunter attachment editing and collection/ratings editing are excluded. A wider creator promise requires implementing and qualifying them. |

No known source-level vulnerability is declared fixed merely by compiling. The waiver
permits implementation delivery; it does not convert these blockers into passes.

## Rollback and operational controls

- Keep custom Community publication disabled unless `PROJECT_PRIME_WEAPON_COMMUNITY_PUBLISH=1` is explicitly approved by the operator. Disabling publication must not relabel or erase retained immutable revisions.
- Keep Weapon Studio disabled unless `PROJECT_PRIME_WEAPON_CREATOR=1` is explicitly selected. Removing that flag closes new creator admission; save/close active workspaces and stop owned pickup tests before restarting with it disabled.
- Disable experimental authority bots by removing `PROJECT_PRIME_CUSTOM_WEAPON_BOTS=1`; omit the autonomous `-weaponbots` launch option. Native bot behavior remains the rollback path.
- Remove `PROJECT_PRIME_CUSTOM_WEAPON_ROLLING=1`, `PROJECT_PRIME_CUSTOM_WEAPON_RECORDING=1` and `PROJECT_PRIME_CUSTOM_WEAPON_SERVER_RECORDING=1` to disable their corresponding opt-in capture paths. Retire current owners and writers cleanly before changing running configuration.
- Keep custom playback/export admission behind its intended supported metadata/resource checks. Missing exact bytes or unsupported contracts must fail closed; do not add native fallback as an operational workaround.
- Remove `PROJECT_PRIME_CUSTOM_WEAPON_PLAYBACK=1` to close ordinary experimental custom playback. Keep `PROJECT_PRIME_WEAPON_STORE` pinned to an operator-approved absolute store; retain historical archives and active shared leases. Uninstall is separate from explicit prune and cannot revoke a running owner's retained bytes.
- Stop new job admission before shutdown, cancel workers, await owned writer/process completion, and release Scene/audio/package leases on their owner thread. Preserve recoverable `.part` files and immutable accepted inputs; never overwrite active DLLs or mutate package stores to repair a running match.
- A release rollback should identify the prior binary/source revision and compatible data/schema contracts. Preserve original recording/package bytes and source policy; do not rewrite historical assets to make a downgraded reader accept them.

## Release decision

Release requires an explicit scope decision, resolution of documented correctness/security blockers, and recorded acceptance evidence for that exact source/binary configuration. Under the present waiver, implementation may be delivered for review while release qualification remains incomplete. Android remains deferred.
