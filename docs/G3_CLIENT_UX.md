# G3 client input, connection health, and browser

This implementation changes presentation and input intent, not simulation weapon timings or network authority. Native desktop/Android interaction and layout acceptance remain open.

## Weapon selection (G3.5)

`WeaponSelectionIntent` retains the newest valid weapon request while the existing gun-animation/morph gates prevent switching. It observes successful equips to track the previous weapon, and cancels pending intent on death, connection/life change, blocked input, or lost weapon/ammo availability. Q defaults to previous weapon. Existing direct binds and the original cycle ordering remain intact. DesiredWeapon on the network remains the actually equipped weapon; server admission and duplicate-input rules are unchanged.

The controller wheel uses right-stick angle across nine authored weapon-order sectors, a deadzone, highlighted text preview, release commit, and B cancel. It does not move the cursor. Holding the wheel suppresses pad aim and the conflicting B morph action. Disconnect, death, spectator/pause blocking, and completed-match results cancel the wheel. A released preview commits at most once. Nine flat annular sectors now provide the requested selected-wedge highlight behind the labels; controller readability still needs device acceptance.

## Connection health (G3.6)

The normal HUD appears only for degraded states. Existing RTT, jitter, snapshot-gap, silence, and queue-age measurements select Interrupted (1000ms silence/gap), HighLatency (150ms RTT), or Unstable (30ms jitter, 250ms snapshot gap, or 100ms queue age), in that priority. These are display thresholds only. Advanced Network settings show existing diagnostic samples and interpolation/hold counters at four updates per second. Unmeasured RTT and snapshot interval display `--`. No adaptive interpolation policy was introduced.

## Browser (G3.8)

The browser supports persisted bounded favorites (32) and recents (16), mode/full/incompatible/max-ping filters, ping/population sorting, and a fresh-probed scored Quick Join. The score favors compatible, reachable, nonfull populated servers with lower ping and the selected mode. It is not skill matchmaking. Saved endpoints are probed even when the directory does not list them.

Details show source-backed mode/map/count/ping/time and actual advertised friendly-fire, radar, spawn, overtime, and late-join rules. The optional discovery tail is capability-negotiated using the query byte's high bit. Legacy/base response lengths and offsets are unchanged; the eight-byte version-1 tail validates enums, flags, and reserved bytes. Missing rules are explicitly unknown. Server identity is unverified and current servers are public; an administrator's self-description cannot establish ranked verification.

## Post-match results (G2.10)

Three prepared pages read the immutable authority `Match.Result`: standings/KDA, actual accepted hostile-health damage plus headshot kills/streak/best beam, and mode-specific objective statistics. They never recompute a result from live state. Existing next/previous weapon controls page the results, with automatic paging for touch-only viewing. Damage uses `DamageDealt`, not the older capped beam-efficiency numerator. Best beam is derived only from the nine replicated beam kill counts, with stable first-weapon ties and None for no beam kills.

## Evidence and remaining gates

The focused G3 pass passed 45 tests including existing combat feedback: queue legality/identity/death/availability, radial deadzone/release/cancel/reset, diagnostic classification, browser eligibility/order/bounds, and legacy/base/extended discovery round trips plus malformed tails, and world-event phase/identity fencing. Log: `/tmp/codex-re-prime-g1/g3-focused.log`. A second focused run passed two result-model tests proving frozen actual damage (distinct from the efficiency numerator) and stable best-beam selection; log `/tmp/codex-re-prime-g1/g3-results.log`. Shared Client code compiled as part of those runs; existing Tmds.DBus.Protocol NU1903 remained.

Automated model tests do not establish rendered layout, controller ergonomics, touchscreen result paging, live two-player event/audio timing, or Android device behavior. Those remain explicit acceptance gates. All new portable files have explicit Android compile links.

## Touch and retained-recap follow-up

Android reuses existing touch controls contextually: post-match shows NEXT/PREV/MENU; modern replay exposes RECAP, and an open archive shows NEXT/PREV/CLOSE/MENU. Rising-edge input goes directly to presentation navigation, while aim/jump/boost accumulation is drained during browsing. It does not inject gameplay weapon or fire commands. Hidden gameplay buttons do not take touches. The configurable keyboard RecapHistory binding defaults to F6.

The annular wedge renderer uses bounded triangle strips with no per-frame geometry array allocation. Three focused model cases cover actual TouchControls visibility/labels/hit-and-release plus all nine wedge centre selections and shared boundaries. Together with queue/recap cases this pass is9/9 (`/tmp/codex-re-prime-g1/g3-touch-radial.log`). These execute real portable touch-control code, not Android MotionEvent delivery or GL rendering.

Final combined navigation/recap/weapon/feedback/result run passed43/43 (`/tmp/codex-re-prime-g1/g3-navigation-final.log`). Fresh Android Release managed/APK build (AOT disabled) succeeded with zero warnings/errors using separate artifacts `/private/tmp/codex-re-prime-g1/g3-client-android`; log `/tmp/codex-re-prime-g1/g3-client-android.log`. The explicit manifest includes both new node-pose dependencies added during integration. Compilation/package success does not establish rendered device acceptance.
