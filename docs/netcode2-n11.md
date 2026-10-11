# N11: movement presentation and latency diagnostics

N11 is **verified within the Mac scope below and ready for PR review and merge**. The tested production source is `dc65318098d67e92024f7672ca9f98d70f8a486a`; immutable Mac engine SHA-256 is `59eb3fcc8bdc87b3f2c6ea3fb51c01463be6b67333a5ff0677741645f2c1e290`. Protocol 50 is unchanged. Immediate native local movement/camera/aim and the existing no-extrapolation presentation policy remain; N11 adds no hard movement correction or movement enforcement.

Authority frame-wrap handling now uses modular wire frames and an internal unwrapped presentation clock. Zero/stale pre-wrap samples cannot move presented history or ACK backward, and reordered velocity reports cannot corrupt native velocity history. Receiver-local input-age timestamps support actual arrival at local frame zero across wrap; wire snapshot frame zero remains reserved and rejected. Per-incarnation diagnostics distinguish interpolated/held/unavailable poses, read-point starvation, correction distance and local simulation stall attribution. A stationary held pose is not itself a network failure. No interpolation tuning is inferred from these short tests.

## Focused Mac evidence

The final engine passed 3,735 lifecycle assertions and asset-backed turret checks with evidence disabled and enabled, 120 assertions each. Lifecycle checks cover full-life fences, valid origin positions, frame wrap, per-slot starvation and deterministic timing boundaries. Repeated presentation at 60/120/144/240/360 Hz preserves fixed simulation steps and the presented world/ACK identity; this is a deterministic API test, not physical display pacing.

The compact native discontinuity fixture passed 278 checks in Combat Hall (`MP3 PROVING GROUND`). It exercises fixture-placed production teleporter and jump-pad entities, a directed native damage endpoint and a real Weavel form transition. Actual post-event native positions/speeds are serialized by `BroadcastSnapshot` and checked against the same-frame hit-validation history before observer smoothing. Teleport/form boundaries hold the lower complete pose and switch exactly at the upper frame; continuous pad/knockback motion interpolates real endpoints. This proves native endpoint ordering and blend behavior, not map traversal, physical projectile collision or rendered camera pixels.

That fixture uses test-only correction commit `eb87fd82` and separately compiled `native-tool-06`; tool SHA-256 is `d2478c6156d7dc55f11d377d10cf22195e79cd26ea67e950c5f4cfd3a8666392`. The immutable production engine hash remains unchanged. The strict Python harness passed 303 tests, including exact retirement/payment evidence and Form28 clock wrap. Original campaign summaries remain preserved; the corrected auditor has separate hashed re-audits.

## Real UDP evidence and measurement scope

Final short headless three-client LAN and extreme runs both passed the strict base, form, spectator and bounded-drain audits, with all processes exiting zero and no incomplete findings. They use the native `alt-morph` workload, including Samus firing, Weavel movement/form changes and a spectator, with 20 seconds of action and a bounded 20-second drain. Ordinary biped claim rescue also passed 94 asset-backed assertions afterward. All commands, raw manifests and per-lifecycle statistics are hashed in the ledger. Neither campaign had simulation exceptions; LAN reported six dropped steps/one local stall and extreme five/one, so no clean-runtime or performance threshold is claimed.

Observer pose ages compare the newest received authority frame with the sampled lower frame; they are not one-way latency. Aggregate the largest cumulative row for each full epoch/match/slot/generation/life instead of summing repeated windows. These sampled lifecycle totals are lower bounds. InputAges have actual authority and client consumption coverage: the metric is receiver-local time from the latest accepted intent arrival to consumption, not owner-source pose age or transit latency. Disconnect resets counters, so retain the highest cumulative sampled per-slot row before reset. Owner-source-to-authority accepted pose age remains unavailable.


Final sampled age data (frames; histogram and full-life rows are retained in the ledger):

| Profile | Measurement | Age samples | Mean frames | Maximum frames |
| --- | --- | ---: | ---: | ---: |
| lan | authority slot 0 input consumption | 1421 | 1.003 | 3 |
| lan | authority slot 1 input consumption | 1421 | 1.003 | 3 |
| lan | peer-0 observer pose | 1066 | 5.651 | 10 |
| lan | peer-1 observer pose | 1427 | 5.573 | 10 |
| lan | peer-2 observer pose | 2494 | 5.605 | 10 |
| extreme | authority slot 0 input consumption | 1343 | 1.792 | 5 |
| extreme | authority slot 1 input consumption | 1342 | 1.804 | 6 |
| extreme | peer-0 observer pose | 987 | 8.915 | 11 |
| extreme | peer-1 observer pose | 1339 | 8.920 | 11 |
| extreme | peer-2 observer pose | 2237 | 9.058 | 12 |

As retained attempt-03 diagnostics, authority LAN slots 0/1 had 1,481/1,503 samples, mean ages 1.003/1.009 frames and maxima 3/4; extreme had 1,461/1,373 samples, means 1.784/1.830 and maxima 5/6. The original final-window-only summary is retained, superseded by a separately hashed corrected input-age summary. These are diagnostic data from an earlier engine, not final performance thresholds.

## Failures retained and corrected

Attempt-02 LAN exposed release before a native respawn. Drain now waits for active participant pending respawns and a full quiescence grace; the gate was not relaxed. Attempt-02 extreme generic turret recovery expiries remain failures in their original evidence. Explicit authority retirement/unavailable-source facts are counted separately from successful native emissions.

Attempt-03 extreme exposed generic Shadow-mode claim payment for a turret source retired before emission. The authority now fences exact retired pre-emission source keys, refuses those claims independently of proof mode, and waits for known pending turret admission to become native emission or explicit rejection. Pending known-source settlement is bounded at 144 authority frames. Exact terminal identities survive 624 authority frames in a bounded 256-entry per-slot registry; a still-live terminal cannot be evicted to permit payment. Already-emitted turret projectiles remain valid after unmorphing, and unrelated unknown-source Shadow policy is unchanged. Focused native tests prove evidence-on/off rejection and previously emitted flight behavior.

Initial fixture failures are preserved: missing roster after match setup, observer role without authority-policy change, frame-zero arrival fixture ordering, and authored turret ray/lane selection with the projectile still inside its owner before unmorphing. Repairs change those fixtures only. Build manifests, commands and all retained evidence hashes are recorded in the [ledger](../tools/nettest/baselines/net2-n11-20261011.json).

## Scope limits

Coverage includes native teleport/pad/directed knockback/form endpoints, lifecycle/death-respawn fences, real UDP spectator exposure and deterministic high-refresh interpolation. Moving-platform rider presentation and grapple/boost presentation remain unexercised; geometry-rewind/contact tests cover narrower behavior. Rendered death/spectator camera smoothness, timed owner input-to-camera response, physical projectile knockback, all-hunter/map coverage, Windows and soak tests are not claimed. This scope is ready for review and merge; it is not yet merged.
