# Quake III menu and multiplayer integration results

Quake III is selectable through normal practice and multiplayer Match Rules.
Classic remains the default, and Momentum retains its immutable v1 behavior.
The new shared Quake III profile is fixed, separate from private JSON tuning,
and recordable. See the [user/profile guide](quake3-movement.md).

## Latest-main compatibility and source identity

The final normal Windows build and eight focused checks pass on latest main
`835a5ba5ad8b5112930f86ee2b7e04521103a827` plus the feature overlay, with
**zero warnings/errors**. The source manifest independently matches all 55
current C# overlay files. [Sanitized evidence](evidence/quake3-menu-selection.json)
retains exact source, executable and recording hashes.

During validation, #773 (Imperialist presentation sizing) and #774 (performance
baseline documentation) merged. Their seven files have no movement, network or
menu overlap. The final focused checks cover the new Quake III rules, all-seven
Hunter scenes, replay-width compatibility, Momentum replay rules/metadata,
protocol-56–60 archives, architecture and the updated Imperialist fixture.

The complete acceptance cohort remains attributed to genuine preceding main
`6a434a4ac4ff47fa243b7ef33b578db9fb5661a9`, with identical movement/network/menu
source hashes. Its binary results are not relabeled as tests of the final build.

| Build/lease | Production assembly SHA-256 | Harness SHA-256 |
| --- | --- | --- |
| Latest-main compatibility | `101f8a96eeae2901395a4d7dd2337d2e98a33d14e5a483dfb874bdcd17f7e99e` | `8190670d158793ec8868e97da1791a750f6ff1a45c1851939a91c724f168c8d6` |
| Full live acceptance | `da9f67e55b1f301fd9859bd94622628b90ea62f168b9fbc31dd7156dd2991b68` | `dae8a05ebda8ab3fa2bb9eb6fb6fbb0eecb58bcc731214aaba1a1f552322588d` |

## Behavior and acceptance

A normal Release build uses the ordinary UI default and disables the narrow
replay-preview failure fixture:

```text
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadReplayPreviewFailureFixture=false
```

The full acceptance matrix passes 17 core routes and 10 additional packet,
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
| Passive replay and seeking | 2,400 recorded frames; 73,413 replay assertions, nine checkpoint captures and nine seek targets pass |

The live owners pass 24,296 and 26,530 assertions, respectively. Their canonical
solver counts are 1,653 and 2,377; neither reports an owner anomaly. Both observe
the two-human/one-bot roster. The actual archive SHA-256 is
`9506a0cb2671aa94ec5d4e8187716a6d1e3c83422e6a4f3a1c57739d5e4952b5`.

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
`artifacts/quake3-selection/windows-final835-transfer/results/`.

**Hosted CI is NOT VERIFIED at publication.** The three-platform math workflow
includes the new authoritative rules and historical compatibility tests, but
local Windows results do not establish its final outcome. Publication follows
the requested immediate commit/merge policy.

Menu checks are controller/editor CPU fixtures, not rendered GL or physical
input/focus tests. No Mac build, simulation or performance run was performed.
Human feel, high-speed map qualification and broader platform/device acceptance
remain open. The operator's accepted performance tradeoff and deferred
optimization remain in [PR-09h](pr09h-results.md); no new timing-budget pass is
claimed by this integration.
