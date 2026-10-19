# PORT-25: offline local character-port acceptance matrix

PORT-25 adds a reproducible local evidence script for the source-owned, opt-in biped visual test path from PORT-24. It invokes Project Prime's **existing** model acceptance command, checks staged receipts and SHA256s, and records an audit result for every supplied Dark Samus or Space Pirate Trooper candidate.

This is **not a complete playable port**. The script never installs a model, changes a setting, enables special Trooper gameplay or modifies network/replay formats. Synthetic content-free tests require no Nintendo assets or Blender.

## Run against private assets and the actual compiled game

After producing individual source-model GLBs (PORT-23), staging their private test packs (PORT-24), and using your own verified game media:

```bash
python3 tools/retro-character-preview/acceptance_matrix.py \
  --pack /private/darksamus-visual-test-pack \
  --pack /private/trooper-power-visual-test-pack \
  --pack /private/trooper-ice-visual-test-pack \
  --pack /private/trooper-wave-visual-test-pack \
  --pack /private/trooper-plasma-visual-test-pack \
  --project-prime /path/to/ProjectPrime \
  --report /private/port-characters-visual-validation.json
```

The report is a local JSON record of source identity, fallback Hunter, asset SHA, bounded GLB skin/material checks, native renderer validator process exit status and diagnostics. It keeps the physical-device/manual acceptance tasks visible. There is no automatic game launch, no upload and no automatic install.

CPP-08 staging also binds canonical `characterId` and `skinSource` to the native
parent and the exact contained provenance receipt. Each result includes
`bindingKind`: `private-catalog-appearance` for that binding, or
`legacy-native-hunter` for older packs. The latter retain their previous behavior
and do not prove independent native/extra seat selection. Partial bindings,
parent/skin/source mismatches, altered receipts and unperformed acceptance claims
fail preflight. Receipt hashes witness local consistency, not source ownership.

The command returns nonzero when any supplied pack fails its structural or game-model validation, or the native validator was not provided. The report always declares `fullPortReady=false` because successful native biped model validation does not prove completeness of first-person, alternate form, new gameplay, network compatibility, replays or Echoes maps.

## Required manual acceptance before enabling new playable IDs

- Run a private offline session and examine actual Dark Samus and all four Trooper variants through idle, movement, turning, zoom, shooting, hit/death/respawn and LOD transitions.
- Record first-person muzzle origin and visibility, native/source clip mapping, alternate form transitions, real source materials/VFX, Android and desktop frametimes, and asset reload behavior.
- Implement and authoritatively test independent extra-character selection, Trooper wall-cling, Scout Drone and Pirate Blaster, plus 1v1/8-player multiplayer and protocol rollback.
- Test each of the six Echoes arenas with owned source media, including collision/visual parity, one-way routes, jump pads, moving interactions, bots, live multiplayer and replay seek.
- Run supported platform CI and dedicated-server matrix without changed vanilla Hunter IDs, game assets in Git history, or altered legacy replay behavior.

The PORT-25 runner validates the existing private body candidate contract. The
CPP-08 identity input has no public selector or scene activation path; ordinary
Hunter-only lookups cannot activate an identity-keyed pack. Actual visual
inspection requires an explicit private consumer in a later slice. Real source
media, a Blender export and a compiled Project Prime client are required to
produce native-rig validation evidence. Those results still do not constitute
the complete playable Dark Samus/Trooper port or final arena acceptance.
