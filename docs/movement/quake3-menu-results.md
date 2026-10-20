# Quake III menu and multiplayer integration results

Quake III is selectable through normal practice and multiplayer Match Rules.
Classic remains the default, and Momentum retains its immutable v1 behavior.
The new shared Quake III profile is fixed, separate from private JSON tuning,
and recordable. See the [user/profile guide](quake3-movement.md).

## Latest-main compatibility and source identity

The clean committed combined branch
`03e07a00513d453febdecfdc768c1382a7df97b3`, including main
`47d446e22c8a95e694cee241405e449780220da6`, passes the final normal Windows
build with **zero warnings/errors**, all 15 focused checks and a fresh real
multiplayer recording/replay cohort. All 55 feature C# hashes independently
match the worktree. [Sanitized evidence](evidence/quake3-menu-selection.json)
retains exact source, executable and recording hashes.

This includes #773 (Imperialist sizing), #774 (performance documentation),
#775 (opt-in render profiling) and #776 (custom authority contacts). The final
checks cover Quake III rules, all-seven Hunter scenes, genuine authority bots,
replay-width compatibility, Momentum replay rules/metadata, historical archives,
architecture, Imperialist behavior, NCX and the asset-backed paid-receipt scene.

Earlier complete acceptance remains attributed to main
`6a434a4ac4ff47fa243b7ef33b578db9fb5661a9`; intermediate compatibility remains
attributed to `835a5ba5ad8b5112930f86ee2b7e04521103a827`. Their results are not
relabeled as tests of the combined binary. Later render pacing/timing changes
through main `3b42fb5dc` have no movement/network/menu source overlap and are
not claimed as runtime-tested by this cohort.

| Build/lease | Production assembly SHA-256 | Harness SHA-256 |
| --- | --- | --- |
| Combined committed acceptance | `bab438dcfb8f6a7b17435bdef2f1e20e820eee975b727d921447e4479fbc3887` | `15241eadc8ed31424f329049f9eafbc4ac4661fa6cbdf7db79cc1825f60b7d08` |
| Intermediate compatibility | `101f8a96eeae2901395a4d7dd2337d2e98a33d14e5a483dfb874bdcd17f7e99e` | `8190670d158793ec8868e97da1791a750f6ff1a45c1851939a91c724f168c8d6` |
| Original full acceptance | `da9f67e55b1f301fd9859bd94622628b90ea62f168b9fbc31dd7156dd2991b68` | `dae8a05ebda8ab3fa2bb9eb6fb6fbb0eecb58bcc731214aaba1a1f552322588d` |

## Behavior and acceptance

A normal Release build uses the ordinary UI default and disables the narrow
replay-preview failure fixture:

```text
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadReplayPreviewFailureFixture=false
```

The original full acceptance matrix passes 17 core routes and 10 additional packet,
name, pickup, Enhanced-retirement, lobby, queue and bot checks. New routes are
`--quake3-rules`, `--quake3-scene <assets> [json]`,
`--quake3-replay-compatibility` and
`--quake3-bot-authority <assets> [json]`. Asset paths point at the operator's
own extracted data. Existing math, v1 profiles, offline rules, native goldens,
NCX fixtures, advisory validation and replay rules/metadata remain covered.

| Focused evidence | Result |
| --- | --- |
| Three-model authoritative rules | 175 assertions; uint bit-16 roundtrip, host-only/stale/frozen rules and actual UDP refusal of protocol 58/59 peers |
| Normal offline rule across all seven Hunters | 24,167 assertions; no developer opt-in; native jump/landing parity and exact adapter Y bits |
| Same offered W+A input, every Hunter | Quake III peak 8.817804 world units/second vs Momentum v1 7.1997075 |
| Immutable profile | Frozen coefficients, positive gain beyond the v1 total-speed ceiling, and zero managed allocation across 100,000 warmed calls |
| Genuine authority AI | 14 scenes, all seven Hunters × difficulties 0/3, 900 ticks each; 257,110 assertions and 7,156 canonical solver samples with actual intents/snapshots |
| Historical replay conversion | 266 new compatibility checks; real 103/105/107-byte checkpoint handling and historical new-bit refusal; v1/Native archive checks retained |
| Normal menu/controller fixtures | Offline rule editor and live-menu/controller checks pass; three-way cycle, Classic default, Cancel/apply, save/reopen and unsupported values covered |
| Browser/party integration | Actual-DLL fixture and static registry check pass; historic queue versions explicitly refused and all packet IDs retained |
| Real multiplayer and recording | Dedicated authority, two independent scripted human-owner processes and one real authority bot; all three processes exit 0 |
| Original passive replay and seeking | 2,400 recorded frames; 73,413 replay assertions, nine checkpoint captures and nine seek targets pass |
| Combined passive replay and seeking | 2,399 recorded frames; 72,699 replay assertions, eight checkpoint captures and nine seek targets pass |

The original live owners pass 24,296 and 26,530 assertions, respectively. Their canonical
solver counts are 1,653 and 2,377; neither reports an owner anomaly. Both observe
the two-human/one-bot roster. The actual archive SHA-256 is
`9506a0cb2671aa94ec5d4e8187716a6d1e3c83422e6a4f3a1c57739d5e4952b5`.

The combined live owners pass 23,232 and 25,698 assertions with 1,408 and
2,222 canonical solver steps. Both observe the two-human/one-bot roster; the
server and both clients exit 0. The fresh archive SHA-256 is
`74b65d6d4938026d8940a2e5a7e08fa18eb8d34cfec04667829e2d6049d09414`.

The combined cohort retains 8/30 displacement diagnostic triggers, with no
sample overflow. This test-only trigger records post-step body displacement or
change greater than 0.25 units; it is not an acceptance threshold. The largest
rows show native health 0→99 respawns with zero solver velocity; another records
damage 51→3 with native knockback. All 36 available solver boundaries in flagged
rows preserve Y exactly. Native collision/contact flags also occur. These
observations do not establish an adapter regression; uncontrolled bot combat
precludes causal cross-run comparisons or a blanket seamless-traversal claim.

The normal menu selection is server-admitted and fixed for the match. Live
protocol 60 refuses older peers; it does not silently substitute Classic or
Momentum. Historical records preserve their Native/Momentum semantics, and
current Quake III records retain their model through construction, library
cache, replay checkpoints and seeking. The private JSON experiment remains
unrecordable and cannot override the shared profile.

## Retained failures and limits

Earlier failing logs remain preserved: a migrated enum-sentinel compile typo,
two-choice UI assumptions, historical checkpoint-width fixtures, a QueueHello
version literal, an owned-worker DLL copy lock and a pre-existing Windows
synthetic-path fixture. Corrections retain explicit old-version refusal and
true historical byte widths; no physics tolerance or product safeguard was
weakened. The fixture-only Studio path helper now supplies platform-qualified
logical paths without creating files. Raw logs and archives remain ignored in
`artifacts/quake3-selection/windows-transfer-01/results/` and
`artifacts/quake3-selection/windows-final835-transfer/results/` and
`artifacts/quake3-selection/windows-final03e-transfer/results/`.

**Hosted CI is partially observed, not fully verified.** Linux movement
math/allocation and scene-ownership jobs passed. The semantic-event content-free
job failed compilation because upstream `SceneMatchEvents.CustomDamage` added
`WeaponOwnerContext` without updating the linked test-project closure. The narrow follow-up
adds an identity-only substitute within the existing content-free fixture boundary.
On Windows main `3b42fb5dc` plus that one-file overlay, the baseline failure was
reproduced, the fixed build passed with zero warnings/errors, and all 47 semantic
event checks passed. No production code changed. Exact source/binary hashes are
in the sanitized evidence. Other hosted jobs remain pending/unverified;
publication follows the requested immediate commit/merge policy.

Menu checks are controller/editor CPU fixtures, not rendered GL or physical
input/focus tests. No Mac build, simulation or performance run was performed.
Human feel, high-speed map qualification and broader platform/device acceptance
remain open. The operator's accepted performance tradeoff and deferred
optimization remain in [PR-09h](pr09h-results.md); no new timing-budget pass is
claimed by this integration.
