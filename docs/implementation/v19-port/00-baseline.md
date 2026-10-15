# V19 customization port: PR-00 source/compatibility baseline

> Read-only baseline captured against main `f17a5de74d82f6b201b90ef7017414108e1a6629` on 2026-10-15. No renderer/gameplay changes in this PR. This document is not a pass report.

## Source truth and scope

* User-supplied `v19 (1).txt` defines custom improvements **1–53** relative to official v0.1.53 `91b1c0c6c44e8c90effd250729709f791a84765e`.
* Source `v19.zip` is not a trustworthy distributable ZIP: its central directory was missing when inspected. Sequentially recovered files are usable for _review_, but font/audio binaries **must not be imported** until independently verified.
* `main` still uses the **Avalonia 12.1.2** launcher/settings/HUD Studio, OpenGL/GLES world rendering, fixed 60 Hz simulation, protocol 56 and versioned historical replay readers. The RmlUi candidate is an unmerged draft, not an accepted requirement.
* Current `HudProfile` schema is **1**; it already supports layout anchors, scale, opacity, contexts, layers, independent health/ammo gauges and crosshair overrides. `HudProfiles` publishes snapshots. Port **deltas only**, not the v19 renderer/network/engine.
* Existing `PlayerOutlines` uses a depth-tested world mask; custom colors must use that mask, not an x-ray overlay. Known solo-vs-bot depth regressions require separate checks.
* Current Insta-Gib rules already control Imperialist-only loadouts and authoritative hits. PR-14 is **visual-only**. Do not port Fruity C++ damage, pickups, headless or networking rules.
* Use the same `SettingsDraft`, UI overlay and Android activity instead of constructing a parallel settings subsystem.

## Frozen compatibility invariants

| Contract | Baseline | Prohibited changes in presentation PRs |
| --- | --- | --- |
| Live wire | Protocol **56** | Packet IDs, layouts, admission, authoritative damage |
| Replay | Readers for file formats **1–5**, historical playback support | Codec IDs, fields and snapshot layout |
| Simulation | **60 Hz** fixed step, independent presentation FPS | Additional simulation work or different damage timing |
| Renderer | **OpenGL/GLES** | Introducing Vulkan/DX12/Metal or render bypass |
| HUD storage | `HudProfile` schema **1** | Breaking old profiles or silently dropping fields |
| Settings | Versioned `SettingsMigration` and `SettingsPersistence` atomic writes | Bypassing draft/apply/archive |
| Android | Avalonia activity + GLES compositing | Assuming desktop-only shader/UI APIs work on Android |
| Studio | Existing Map Studio, standalone Studio/NetRadiant work, replay editor | Replacing those modules with v19 copies |
| Assets | Licensed source only | Bundled ROM/extracted Nintendo content or unverified warning audio |

## Execution check list

1. Establish an actual _before_ run of HUD, outline and GL GPU draw stats with current commands and hardware; record logs and screenshots in review evidence. **NOT RUN in this branch.**
2. Run `dotnet build src/MphRead.sln -c Release`, `tools/check-opengl-only.sh`, `tools/hud-check`, `tools/hud-ui-check`, `tools/nettest`, pointer/touch tests and the latest release workflow, confirming actual harness entry points. **NOT RUN in this branch.**
3. Confirm Windows/Linux/macOS desktop publish, Linux/Windows dedicated server, Android Release APK compilation/package, and post-install native behavior. **NOT RUN in this branch.**
4. Test standard and legacy profiles/import/export, crosshair native and custom, battle/instagib/balanced, 1v1/8p, fresh/legacy replay and solo world occlusion. **NOT RUN in this branch.**
5. Produce complete attribution inventory for any of the 34 proposed fonts or warning audio before merging PR-07/04. v19's credited Halo-like audio is **not** license proof.

Acceptance ledger: [feature-coverage.md](./feature-coverage.md).

## Independent PRs and order

PR-01 (single HUD/profile storage) follows PR-00. PR-12 (Balanced Mode fallback) and PR-14 (Ion Fracture Insta-Gib Imperialist) can branch from main independently after the PR-00 baseline is accepted. PR-02/03/04 then cover stacked HUD and editor, PR-05/06 reticles, PR-07/08 fonts, PR-09/10 settings, PR-11 input and PR-13 outlines. PR-15 is product-wide acceptance.

### PR-14 design deviation from Fruity Prime

The desired Project Prime **Ion Fracture** signature is _not_ a helix. A narrow white-blue/slot-color core creates a clean aim-readable axis, with a short staggered procession of razor-angle energy shards/pulses that fan outward for an instant, plus a clipped impact afterglow. Shards are deterministic from shot identity and age, not random per render frame. The beam stays world-depth-tested and ends at the actual accepted collision point. Reduced Motion removes secondary shard animation but retains the core. Do not contaminate globally shared materials or create network messages.

## Evidence status

* Source path reconnaissance: **completed** at the above main revision through repository read-only calls.
* v19 recovered source: **partial and unverified**; no new binaries approved for shipment.
* Build, runtime, screenshot, FPS, GC, GPU, APK, and online tests: **NOT RUN**.
* Expected difference for PR-00: **documentation only**, no runtime changes.
