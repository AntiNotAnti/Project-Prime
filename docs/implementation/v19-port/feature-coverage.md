# v19 feature-coverage / merge acceptance ledger

**Source of truth:** supplied `v19 (1).txt`, features 1–53. **Scope:** add one Project Prime original `Ion Fracture` Imperialist visual effect. This table is a _work queue_, not a claim of implementation.

Legend: `PENDING` means not delivered by this port, even if an older partial feature exists in current main. Mark `IMPLEMENTED` only with linked code; `TESTED` only with commands and logs; `MERGED` only after merged commit verification.

| Feature | Workstream | PR | Port status | Acceptance/evidence |
|---:|---|---|---|---|
| 1 | HUD/profile ownership, existing element controls, migration | PR-01 | PENDING | Not run |
| 2 | Equal stacked aspect geometry, controls and touch mapping | PR-02 | PENDING | Not run |
| 3 | Equal stacked aspect geometry, controls and touch mapping | PR-02 | PENDING | Not run |
| 4 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 5 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 6 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 7 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 8 | Equal stacked aspect geometry, controls and touch mapping | PR-02 | PENDING | Not run |
| 9 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 10 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 11 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 12 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 13 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 14 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 15 | Stacked HUD visuals/radar/combat feedback | PR-03 | PENDING | Not run |
| 16 | HUD editor, presets, palette and alert | PR-04 | PENDING | Not run |
| 17 | HUD/profile ownership, existing element controls, migration | PR-01 | PENDING | Not run |
| 18 | HUD editor, presets, palette and alert | PR-04 | PENDING | Not run |
| 19 | HUD editor, presets, palette and alert | PR-04 | PENDING | Not run |
| 20 | HUD editor, presets, palette and alert | PR-04 | PENDING | Not run |
| 21 | HUD editor, presets, palette and alert | PR-04 | PENDING | Not run |
| 22 | HUD editor, presets, palette and alert | PR-04 | PENDING | Not run |
| 23 | HUD/profile ownership, existing element controls, migration | PR-01 | PENDING | Not run |
| 24 | Native reticles, scalable contours, min visibility | PR-05 | PENDING | Not run |
| 25 | Native reticles, scalable contours, min visibility | PR-05 | PENDING | Not run |
| 26 | Native reticles, scalable contours, min visibility | PR-05 | PENDING | Not run |
| 27 | Crosshair shapes, property inheritance and preview parity | PR-06 | PENDING | Not run |
| 28 | Crosshair shapes, property inheritance and preview parity | PR-06 | PENDING | Not run |
| 29 | Crosshair shapes, property inheritance and preview parity | PR-06 | PENDING | Not run |
| 30 | Crosshair shapes, property inheritance and preview parity | PR-06 | PENDING | Not run |
| 31 | Crosshair shapes, property inheritance and preview parity | PR-06 | PENDING | Not run |
| 32 | Crosshair shapes, property inheritance and preview parity | PR-06 | PENDING | Not run |
| 33 | Licensed font choices, picker and texture cache | PR-07 | PENDING | Not run |
| 34 | Licensed font choices, picker and texture cache | PR-07 | PENDING | Not run |
| 35 | Independent HUD/UI typography | PR-08 | PENDING | Not run |
| 36 | Independent HUD/UI typography | PR-08 | PENDING | Not run |
| 37 | Independent HUD/UI typography | PR-08 | PENDING | Not run |
| 38 | Independent HUD/UI typography | PR-08 | PENDING | Not run |
| 39 | Licensed font choices, picker and texture cache | PR-07 | PENDING | Not run |
| 40 | Reversible 60-second settings preview | PR-09 | PENDING | Not run |
| 41 | Reversible 60-second settings preview | PR-09 | PENDING | Not run |
| 42 | Decimal UI, exact persistence and navigation | PR-10 | PENDING | Not run |
| 43 | Decimal UI, exact persistence and navigation | PR-10 | PENDING | Not run |
| 44 | Decimal UI, exact persistence and navigation | PR-10 | PENDING | Not run |
| 45 | X/Y aim and contact-driven stylus | PR-11 | PENDING | Not run |
| 46 | X/Y aim and contact-driven stylus | PR-11 | PENDING | Not run |
| 47 | X/Y aim and contact-driven stylus | PR-11 | PENDING | Not run |
| 48 | Equal stacked aspect geometry, controls and touch mapping | PR-02 | PENDING | Not run |
| 49 | Decimal UI, exact persistence and navigation | PR-10 | PENDING | Not run |
| 50 | Balanced Mode empty ammo weapon fallback | PR-12 | PENDING | Not run |
| 51 | Team/player outlines and editor | PR-13 | PENDING | Not run |
| 52 | Team/player outlines and editor | PR-13 | PENDING | Not run |
| 53 | Team/player outlines and editor | PR-13 | PENDING | Not run |
| Bonus | **Ion Fracture** Insta-Gib Imperialist rail, deterministic slot coloring, world occlusion, impact and accessibility path | PR-14 | PENDING | Not run |

## Release gates

- All 54 rows backed by code, assertions, rendered evidence, source/license metadata and merged commit hashes.
- No network packet, damage, replay or fixed-step changes from cosmetics.
- HUD/profile v1 plus the v19 v2 migration must retain all existing settings; corrupt profiles fall back.
- Asset provenance must be checked before bundling fonts or the optional low-health audio.
- Depth testing verified on an empty solo world and a world with at least one bot; no x-ray pickups/outlines/effects.
- Windows/Linux/macOS publish and Android Release package plus supported native input/device checks are independently recorded.
- Wave PRs are merged only once their dependency and CI gates are complete. Open/draft does not mean accepted.

## Evidence slots

| Check | Baseline log | After log | Notes |
|---|---|---|---|
| Desktop Release build | NOT RUN | NOT RUN | |
| Windows/Linux/macOS publish | NOT RUN | NOT RUN | |
| Android build and on-device | NOT RUN | NOT RUN | |
| Dedicated server smoke | NOT RUN | NOT RUN | |
| HUD/UI/golden screenshots | NOT RUN | NOT RUN | |
| Render depth and occlusion | NOT RUN | NOT RUN | |
| 1v1/8p, bots and network | NOT RUN | NOT RUN | |
| Historical replay and Studio | NOT RUN | NOT RUN | |
| Frame time CPU/GPU / allocations | NOT RUN | NOT RUN | |
| License and binary integrity | NOT RUN | NOT RUN | |
