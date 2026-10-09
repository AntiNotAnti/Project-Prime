# Rendered reconciliation and native validation

This follow-up implements bounded live trail endpoints and authored per-weapon
impact sprites. It does not enable either production gate. Correction checks full
ShotKey/component, current lifecycle, the smoothed body offset, distance, angle,
and line of sight from both beam and trail origin on every draw. The endpoint is
read only by drawing; no simulation position/history/velocity is edited. The
existing per-draw particle pool bounds fallback effects without advancing match
RNG, creating simulation effects, or playing additional audio.

`-impactdistance` (0..8, default 4), `-impactangle` (0..60, default 60), and
`-impacthold` (0..8 simulation frames, default 3) expose conservative limits.
`-liveimpacts` remains opt-in; `-noliveimpacts` rolls it back. Primary ordinal
matching remains intentionally narrower than all child/continuous effects.

## Results

- 21/21 local contract groups pass, including 30 presentation assertions, 178
  native early-claim assertions, historical protocols and unchanged authority.
- Nine weapons pass native Metal shooter/victim/spectator runs at 250 ms RTT,
  ±40 ms jitter, 2% loss, 2% reorder and 1% duplicates. All 644 classified
  impact events and 587 draw submissions have exact authoritative backing.
  37,941 projectile draw-state comparisons report zero simulation mutations.
- Charged native emissions are counted in the actual server spawn path; intended
  weapon facts and ordinary ammo/cadence/proof gates are required. Original failed
  pilots are retained. Counter fixes are rechecked on four-player duel, eight-player
  continuous damage, and authority-only Omega/Judicator hits.
- The native eight-player inbox now drains in the headless presentation step. It
  never records a successful draw. This avoids a harness-only 128-event cap.
- Real replay theatre/export/killcam and virtual 60..540 Hz draw checks pass in
  `replay/`. The protocol-44 synthetic export has the same pre-existing one/two-pixel
  30 FPS repeat failure in baseline and current builds; all corresponding baseline
  and current output pixels are identical. See `historical-replay/pixel-comparison.json`.
- Thirteen predicted-kill tickets across three impaired runs all confirm, with
  zero rejection or expiry. The short 5% loss arm fails shot-replication coverage;
  the 60-second rerun passes. This remains too small a sample for default enablement.
- Real Metal 120/144/240/360 target runs pass; actual throughput is approximately
  117 FPS on this host. These are not proof of a physical 240/360 Hz display.
- OpenGL and MoltenVK views render. Android emulator and builds are documented
  in final acceptance; Windows/Linux/physical Android remain unavailable.

This is functional evidence, not release acceptance. Victim/spectator exact
projectile matches remain uncommon; truthful authoritative fallbacks dominate.
The proposed 90% same-projectile target is not met. Counts of classifications or
particle submissions are not pixel-visibility percentages. Spot-inspected local
captures show the arena, actors and weapon cues; this is not exhaustive human QA.

## Evidence and reproduction

Each campaign's `manifest.json` records runtime hashes, source fences, population,
impairments and limits. `summary.json` includes exact commands and original pass/fail
results. Raw logs/diagnostics are gzip-compressed **text only**, without alteration;
`raw-index.json` stores original SHA-256 hashes. No game images, replay binaries,
map bundles or extracted assets are committed. `local-images.json` inventories
local screenshots for inspection; their `/tmp` paths are not durable shared assets.

Regenerate a report, including directly from compressed exports:

```sh
python3 tools/live-impact/report-native.py \
  docs/network/validation/live-impact/rendered/metal-nine --output /tmp/impact-report.json
```

`tools/hitrig/run-networking-slices.py --help` documents native, charged, rendered,
spectator, backend, high-refresh and pause options. `tools/live-impact/benchmark-native.py`
alternates same-binary live-delivery off/on arms with identical diagnostics and
retains whole-match sim p50/p95/p99/p99.9/allocation and loop metrics. Spawns and
actual workload vary; this is not an identical-event deterministic benchmark.

Recorded local builds and tests are distinct from CI. CI review was skipped at
user request. No merge or release is authorized.
