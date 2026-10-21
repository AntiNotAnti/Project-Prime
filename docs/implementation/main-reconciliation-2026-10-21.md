# Main reconciliation, October 21, 2026

The local settings, HUD, input, presentation and renderer work was committed as
`76096e4a98b1367f5e569d5718060405223869e7`. Its original parent was `9885b151c`.
The user authorized committing all pending source changes and merging with
`origin/main`, while retaining both sets of fixes.

## Preservation

- Backup branch: `codex/backup-main-before-reconcile-20261021`.
- All 551 paths changed by the local commit remain present.
- 494 paths retain their exact local Git blob; 57 overlapping paths contain
  upstream integration or test adjustments. Every newly added local file remains
  unchanged.
- The initial 55 overlaps received a bidirectional added-line review. The extra
  overlaps are the native offline test (upstream preference persistence) and
  the font allowlist (a licensed face above the generic source-size limit).
- The accompanying `main-reconciliation-preservation-2026-10-21.json` lists the
  affected paths and the final merged upstream identity.
- Both histories are merged rather than replaced. No reset, rebase or force push
  is used. Temporary acceptance output and personal editor files remain local.

## Resolutions

Nine files initially conflicted. The combined implementation retains:

- Previous/next menu choices together with upstream remembered-map preferences.
- HUD/fonts resources together with the map compiler source-version stamp.
- Independent replacement surfaces, authored tint and neutral albedo together
  with upstream explicit diffuse overrides.
- Renderer qualification scale controls together with upstream pose schedules
  and FOV controls; both sets of native UI test entry points remain available.
- Kill-camera identity diagnostics and the local master enable gates.
- The updated authoritative map submission assertion rather than the retired
  staged-map behavior.

Independent review found a missing identity diagnostic and a relative asset-path
issue in the isolated GL test. Both were corrected and rechecked. The six-source
Prime import request and the current native fallback notice required updating
older test expectations; product behavior was retained. The native offline test
now requires explicit private preferences because choosing a map persists it.

While checks ran, upstream PRs 1006 and 1008 arrived. Both were merged cleanly,
retaining typed replay capture phases and opt-in dedicated custom replay capture.

The exact licensed Linux Libertine `font17.ttf` is explicitly allowed by the
asset guard because it exceeds the generic 2 MiB source-size limit. Its adjacent
notice and bundled license text remain included.

## Validation

- Desktop and dedicated server builds pass. The native bridge was rebuilt.
- Native renderer, custom weapon and UI test projects compile against the merged
  desktop assembly.
- Isolated settings: 360 passing checks, including automatic saving, transaction
  rollback, retained HUD drafts, stacked layout and live preview restoration.
- Native settings UI: 323 passing checks each at 1280x800 and 640x360; the final
  upstream integration was additionally checked at 1280x800.
- Native offline UI: 590 checks, including hidden deferred mechanics and actual
  translated input, using a private preference directory. Relative asset paths
  were exercised after the isolation correction.
- HUD controller: 86 checks; dual-screen geometry and hit regions: 572 checks.
- Managed RmlUi runtime checks pass; Android input queue: 63 checks; native range
  queue source: 20 checks.
- Renderer resources: zero failures, including 120 shader ownership cycles and
  1,680 scenarios.
- Offline/map menu CPU checks pass. Typed custom replay capture: 238 checks across
  recipient and authority origins, rerun after the final upstream integration.

Preference-writing checks used private temporary roots. The normal repository
`Savedata/settings.json` was not created. These checks do not establish physical
input latency or Android APK/device behavior; the Android workload was unavailable
in the existing environment.
