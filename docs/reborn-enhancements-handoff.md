# Reborn enhancements 1–7: implementation and acceptance record

Prepared 2026-10-01 against `e80098f4` (Project Prime protocol 34).

The seven implementations are committed on separate, focused branches. The combined
checkout is deliberately detached; it is a validation assembly, not a proposed
single feature PR. Live generated additions share protocol **35**. Existing
historical decoders remain available. OpenGL remains the default.

Implementation is delivered, but full hardware/release acceptance is **not complete**.
The renderer observations and unavailable-device gates below must not be represented
as passing results. No branches were pushed and no PRs were published.

## Where to review

The original workspace is on `codex/settings-backup`. Other implementations are in
the following worktrees. `/tmp` resolves to `/private/tmp` on this machine; branches
preserve committed work even if temporary checkouts are later removed.

| Work | Branch / tip | Worktree |
|---|---|---|
| Settings | `codex/settings-backup`, `6bf9384a` | Original workspace; also `/tmp/prime-settings-archive-completion` |
| Packet generator | `codex/protocol-generator`, `afede6c9` | `/tmp/prime-protocol-generator` |
| Fidelity Oracle | `codex/fidelity-oracle`, `3b41b0bc` | `/tmp/prime-fidelity-oracle` |
| Semantic foundation | `codex/semantic-match-events`, `6b9cbdc3` | `/tmp/prime-semantic-events` |
| Semantic wire | `codex/semantic-event-consumers`, `ae57673a` | `/tmp/prime-semantic-consumers` |
| Semantic delivery and consumers | `codex/semantic-delivery`, `a91e94a6` | `/tmp/prime-semantic-delivery` |
| Renderer capture/lifetime | `codex/render-lifetime-evidence`, `b0eddd61` | `/tmp/prime-render-final-composite` |
| Renderer performance/Vulkan | `codex/render-performance-investigation`, `853e0472` | `/tmp/prime-render-performance-investigation` |
| Renderer scene transitions | `codex/render-scene-lifecycle` | `/tmp/prime-render-scene-lifecycle` |
| Material authoring | `codex/material-authoring`, `c0e608cb` | `/tmp/prime-material-authoring` |
| Material backend checks | `codex/material-backend-evidence`, `d87417bb` | `/tmp/prime-material-backend-evidence` |
| Waitlist core | `codex/lobby-waitlist`, `b03e8692` | `/tmp/prime-lobby-waitlist` |
| Waitlist admission | `codex/lobby-waitlist-admission`, `63f38eb2` | `/tmp/prime-lobby-waitlist-admission` |
| Waitlist UI | `codex/lobby-waitlist-ui`, `c2714bf6` | `/tmp/prime-lobby-waitlist-ui` |
| Waitlist hardening | `codex/lobby-waitlist-hardening`, `96498772` | `/tmp/prime-lobby-waitlist-hardening` |
| Existing test harness namespace repair | `codex/test-harness-build-fix`, `63017a7e` | `/tmp/prime-test-harness-build-fix` |
| This record | `codex/enhancements-handoff` | `/tmp/prime-enhancements-handoff` |

Combined validation: `/tmp/prime-enhancements-validation`. Its ordered Git history
records the actual integration, including small union resolutions to CLI dispatch,
friend assemblies and protocol comments. Branches are stacked where dependencies
require it; do not merge every full branch diff independently. The partial
cross-field generator hook `cbe3d937` is in the semantic stack, after the generator
foundation. Semantic delivery also includes production admission dependencies.

## Delivered behavior

- **Settings:** bounded whitelisted ZIP, strict store validation, transactional
  rollback, reset, restart choice, stale-writer fencing, desktop UI and Android SAF
  integration. Auth, game assets and caches are excluded. Embedded preferences use
  their canonical stores. See `docs/settings/settings-archive.md`.
- **Generator:** analyzer-only Roslyn generation, strict primitive/bounded enum and
  UTF-8 codecs, compile diagnostics, cross-field validation and golden malformed
  wire tests. Fixed arrays/custom structs remain compile-time unsupported because
  these production packets need neither. No Roslyn runtime dependency.
- **Oracle:** 47 normalized, versioned scenarios with first-divergence output and
  explicit developer-only baseline recording. Eight F1 scenarios and 39 actual
  content-backed F2 scenarios cover movement, all main weapons, damage/headshots/
  splash, identity, respawn, modes, and all seven hunter alternate forms. Public
  baselines contain derived values only. See `tools/fidelity-oracle/README.md`.
- **Semantic facts:** authority-owned IDs with match/epoch/slot/generation/life
  fences, deterministic awards, passive parity checks, retained reliable delivery,
  ordered client presentation, replay checkpoint schema 6, telemetry, kill feed,
  announcer/medals, post-match totals and director interest. Previous checkpoint
  schemas remain readable. **Assist rule:** every distinct non-killer who dealt
  positive accepted damage within the last 300 simulation ticks (five seconds),
  including accepted friendly fire; self-damage and stale occupants are excluded.
- **Renderer:** final-composite readback, correct actual framebuffer dimensions,
  resource/loss tests, real backend assertions, bounded pixel comparisons and
  separated submission/presentation timing. Default/fallback policy is unchanged.
- **Materials:** stable keys and authored GUID provenance, bounded manifest and
  image validation, observed inventory/starter tools, legacy fallback, shared
  material resolution, Map Studio channel previews and usage focus. Texture-only
  refresh retains geometry; community package identity is unchanged.
- **Queue:** bounded server-owned FIFO, queue-only connections, connection-fenced
  reserved offers, normal admission validation, browser/UI position and countdown,
  leave/accept/decline, retry coalescing and real UDP coverage. Bots retain their
  occupied slots. See `docs/network/lobby-waitlist.md`.

There is no existing overtime rule to instrument; no new overtime gameplay was
invented. There is no independent spectator capacity pool in the current server,
so a full eight-slot server cannot offer an additional Spectate + Queue slot.
Queue-only waiting is supported. Resume uses the existing connection incarnation;
an endpoint change does not gain identity merely by presenting a display name.

## Validation evidence

F0 = schema/unit; F1 = content-free deterministic simulation; F2 = extracted-content
simulation; F3 = rendered local; F4 = real process/network; F5 = physical device/WAN.
Counts below describe assertions in their suites, not distinct end-to-end scenarios.

| Check | Result |
|---|---|
| Combined Release solution | Pass, 0 errors (72 warnings) |
| Combined Android managed Compile | Pass, 0 errors (88 warnings); no physical-device claim |
| Combined dedicated server | Pass, 0 errors (13 warnings) |
| Oracle framework | 26 F0/F1 checks pass |
| Oracle scenario baselines | All 47 match in combined checkout |
| Presentation-rate invariance | All 47 match at 30/60/120/144/240/997 Hz on Oracle branch |
| Semantic domain | 47 F0 checks pass |
| Actual semantic damage/parity | 35 F2 checks pass |
| Combat arbitration | 153 assertions pass |
| Actual game modes | 891 authority checks pass |
| Replay file/checkpoint formats | 2,886 checks pass |
| Full lobby/network suite | 7,724 assertions pass in combined checkout |
| Waitlist real UDP | 308 assertions pass |
| Reliability and queue budgets | Impairment, reserve, rate and pump checks pass |
| Protocol 17/18/19 and continuous targeting | All pass; v18 warmed decode allocates zero bytes |
| Netcode performance | Sampling, ledger comparison, decode equivalence and telemetry pass |
| Generator | 35 generator checks, 77 wire assertions, 5,000 malformed datagrams pass |
| Settings archive | 26 combined checks; 28 including focused UI checks pass |
| Material schema/resolution | 58 assertions pass |
| Map Studio | 388 checks pass |
| Community-map multiplayer | Download, exact identity, historical versions and restart pass |

Local Apple M4 Pro GPU evidence includes actual OpenGL, Metal and Vulkan/MoltenVK.
Twenty-two synthetic material fixtures have identical material-interior pixels;
the ten gameplay sampler/blend fixtures match over the whole frame. The original
editor fixtures differ at 52 background grid pixels (maximum 28/255), outside the
tested material interior. This does not prove arbitrary scene transparency sorting.

Renderer evidence files reside in `tools/render-parity/evidence/`, including
`macos-m4pro-20261001.json` and `macos-m4pro-vulkan-profile-20261001.json`.
Actual Vulkan/Metal tests include 120 native resize/resource cycles, forced device
loss/reconstruction and fallback, 24 scene parity captures and 31 full-shell captures.
The managed resource measurements do not prove native driver memory reclamation.

Performance measurements are diagnostic: approximately 8.3–10.6 ms OpenGL,
16.1–22.6 ms Metal and 16.4–18.6 ms MoltenVK in the sampled configurations.
All reported Immediate mode; acquisition/present calls were small. Submission and
buffer-write costs were measured separately. These are host/completion timings,
not GPU timestamp measurements, and are not representative-hardware release gates.

## Remaining acceptance gates

- Windows DX12/Vulkan, physical Android Vulkan/GLES, Android document picker and
  process restart, and real WAN/device acceptance were not available on this Mac.
- One Vulkan respawn run produced six black frames during a combined-effect phase.
  Subsequent runs did not reproduce them. Preserve this unresolved observation;
  passing reruns do not establish its cause or close the renderer release gate.
- Renderer scene transitions and replay camera initialization have a separate
  focused follow-up record on `codex/render-scene-lifecycle`.
- Two supplied older clips were rejected on the renderer-only baseline before
  semantic changes. Generated historical-format regression fixtures pass, but
  these particular clips are not proven compatible and need separate diagnosis.
- Native MoltenVK teardown reported residual allocations. Stable managed handle
  counts are insufficient evidence of native reclamation.
- Hardware audio/presentation, broad material scenes, long-session and controlled
  performance acceptance remain distinct from the bounded local tests.

Keep OpenGL as default until the plan's activation gates pass. Do not silently
regenerate Oracle baselines to accommodate a failure. Review any intentional
gameplay change separately and retain its first-divergence evidence.

## Reproducing the main local checks

Use .NET 10 (`/Users/jarrett/.dotnet/dotnet` on this machine). From the combined
checkout, build `src/MphRead.sln -c Release`, then run the tools under
`tools/fidelity-oracle`, `tools/protocol-generator-check`,
`tools/settings-archive-check`, `tools/material-pack`, `tools/map-editor-check`,
`tools/map-multiplayer-check` and `tools/match-events-check`.

Build `tools/nettest/nettest.csproj -c Release`; its flags include `--lobby`,
`--waitlist`, `--architecture`, `--reliable`, `--queue-budget`,
`--netcode-performance`, and `--semantic-scene`, `--combat-scene`,
`--gamemodecheck-scene` followed by a user-data directory containing valid paths.
Content-backed checks require legally supplied extracted files and are optional
for public CI. Local test data here is `/tmp/prime-composite-user-data`.

The game CLI accepts `-fidelityoracle verify all -allow-content -baselines
tools/fidelity-oracle/baselines` and `-replayformatcheck`. Set
`PROJECT_PRIME_USER_DATA` for an isolated local configuration. Run builds serially
within one checkout; server builds share output paths with desktop builds.

No proprietary assets, tokens, or local settings were added to these branches.
The original workspace's existing `.DS_Store` changes were preserved.
