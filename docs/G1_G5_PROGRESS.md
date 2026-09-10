# Project Prime G1–G5 implementation progress

Current status: the G6 UI/lobby rollout was reverted following reported launcher
and game-entry regressions. The historical evidence below is not acceptance of
that rollout. Backend security and ranking storage remain implemented; restored
servers produce rating-ineligible legacy reports. See [G6 rollback](G6_ROLLBACK.md).

The [supplied implementation plan](PROJECT_PRIME_G1_G5_IMPLEMENTATION_PLAN.md)
is the scope. Classic balance, 60 Hz authority, existing package/namespace
identities and unrelated working-tree changes are preserved. Protocol 8 remains
unreleased; the historical replay adapters remain isolated from live admission.

| Epic | Implementation | Validation |
|---|---|---|
| G1 | Timing projections, bounded render interpolation/late latch, afflictions, spawn policies and input paths implemented | Fresh integrated main suite: 954/954. Real-content ordering, catch-up/homing and all twelve world modes pass. Render/device acceptance remains open. |
| G2 | Authoritative feedback, assists, radar/audio, results, retained recaps and touch navigation implemented | Integrated suite passes. Real rendered layout, audio mix and physical touch acceptance remain open. |
| G3 | Team allocation, overtime, late join/reconnect, world events, weapon queue, browser and network HUD implemented | Actual impaired-UDP weapon blocking/equip/previous-weapon checks pass. Fixed six-tick interpolation retained after the adaptive candidate failed its declared gates. |
| G4 | Account/license UI, signed endpoint-pinned admission, durable reports, PostgreSQL career/history and queries implemented | Backend 43/43 against disposable PostgreSQL, including actual HTTP/UDP admission and outbox-to-commit receipt recovery. RP, star progression and the proposed rating/forfeit extension await approval. |
| G5 | True delayed observers/cameras, indexed replay/touch controls, bots/Practice, telemetry, presets/Duel, voting and tournament controls implemented | Full main suite passes. Actual UDP bot retirement and mixed human/bot teams pass. Automatic recording gates and replay failure handling pass. Both 300-second impaired combat runs and the 300-second 8-player/16-observer replay run pass; see the integration evidence document. |

## Acceptance boundaries

[Integration evidence](G1_G5_VALIDATION.md) separates source, automated tests,
loopback runtime checks, long-run measurements and unavailable release gates.
The solution and dedicated server publish build; Android managed Release builds
with zero warnings/errors. Imaging passes 18/18, Python 58/58, and the project
boundary guard reports zero violations.

The program is **not fully accepted**: [G4_RANKING_SPEC.md](G4_RANKING_SPEC.md)
still requires approval before RP/rating/forfeit implementation, and the plan's
physical-device/high-refresh/visual/audio gates remain open. Local PostgreSQL and
UDP impairment tests do not establish a deployed Backend or external WAN result.
The existing Tmds.DBus.Protocol NU1903 advisory remains unsuppressed.

The pre-existing LICENSE deletion and maps/Parallax edits are outside this task.
