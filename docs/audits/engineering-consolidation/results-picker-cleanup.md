# Post-match picker cleanup

The results screen still owns map voting, offline rematch and persistent-lobby
return. This slice removes the Hunter/Suit picker that current main already
disables with `EndScreen.CharacterChangeEnabled = false`. Hunter and suit choices
remain in the lobby/settings and `RespawnChoice` applies them at spawn.

## Reachability and persisted state

At baseline `91b1c0c6c44e8c90effd250729709f791a84765e`, the eight unreachable
branches in `EndScreen.cs` and three in `EndPanelView.cs` generate CS0162 warnings.
Keyboard and gamepad Left/Right cannot pick a Hunter; Up/Down/Accept and pointer
clicks retain their existing `MapPick` behavior. `EndScreen.Pick` has one source
caller, the disabled `EndPanelView.Commit` path. `ShowHunter` only appears in a
diagnostic capture whose selector is already a no-op.

Remove those picker branches, their private selection helpers, hidden tabs,
hidden HunterStand/choice rows and the duplicate `end-panel-hunter` capture.
Retain `end-panel`, the actual ballot/search/footer, the persistent-lobby view,
and renderer-facing `EndScreen.Hit`, layout/pointer adapters and Hunter/Suit
getters. `PlayerEntityEndScreen.cs` remains unchanged, including its existing
disabled-picker guard. This is not a renderer or input redesign.

`LauncherPrefs.LastHunter`, `LastColor`, the `prefs_schema=1` contract and
`RespawnChoice` are unchanged. No saved preference aliases or keys are removed.
All packet, checkpoint and replay sources are unchanged; this branch makes no
wire/schema or historical-reader change. Related retirement is separately
reviewed in [#498](https://github.com/AntiNotAnti/Project-Prime/pull/498).

## Reproducible device-free check

`tools/results-ui-check` enters `Headless` before toolkit/feedback initialization,
creates a `Scene` with `initializeRuntime:false`, and checks the actual results
input and Avalonia panel. It never creates a game GL context or native audio
engine. Persistent-lobby metadata uses only UDP loopback port 9; no real account,
social service or external server is contacted. Use isolated preferences:

```sh
PROJECT_PRIME_USER_DATA=/tmp/results-ui-check-user-data \
PROJECT_PRIME_MAP_COMMUNITY=https://maps.example.test/ \
dotnet run --project tools/results-ui-check/results-ui-check.csproj -c Release -- \
  artifacts/results-ui-check
```

The check preserves saved Hunter/Suit choices, exercises ballot keyboard and
pointer semantics, and captures offline-ballot, empty-ballot and
persistent-return panels. It records the exact product MVID/SHA256, 11 assertion
results, PNG hashes and the process-specific tile gradient hashes. The existing
Windows/macOS controller CI matrix runs the check and uploads its witnesses.

Original temporary baseline/candidate witnesses are preserved under
`artifacts/results-picker-characterization`. Baseline producer MVID is
`bb5922d1-051d-4a89-940b-d14096c550fe`; its unchanged product SHA256 is
`22C7EB0FB0E59C7E40FA656C4BDA1C20A58C42B5628E3FDD5F47FB79FB6FF0B2`.
The first candidate passed the same 11 assertions and compiled with zero errors,
reducing desktop warnings from 30 to 19 by removing all 11 CS0162 sites.

Empty-ballot and persistent-return PNGs match baseline byte for byte. The
offline-ballot PNGs differ: unchanged `DeckTile.Drift` derives its gradient phase
from `RoomKey.GetHashCode()`, which is randomized between processes. Preserve
that first mismatch; it is not a valid cross-process exact-image oracle for
arena interiors. A separate comparison must verify layout and pixels outside
those interiors. No production gradient change belongs in this cleanup.

Final tool and baseline both pass all 11 assertions. The final desktop/tool build
passes with 19 warnings and zero errors; Android shared-source Release compile
with verified Temurin 17 passes with 39 warnings and zero errors. Existing
GamepadChecks pass 1006 assertions, including all 165 Prime UI assertions, against
the exact candidate MVID `337269fc-714b-4d42-aa21-a6ee5187557c` and SHA256
`0CE213543BC4E294268B120576BC238594EFCCA11091A53877121EB902288FBB`.

The original offline mismatch has 15,637 changed pixels, maximum channel delta 4,
all within tile interiors. Re-running the same final fixture source against both
products gives 8,145 changed pixels, maximum channel delta 3, again exclusively
within those interiors. **Zero pixels outside the two tile interiors differ**.
`compare_png_boundary.py` preserves the exact border/layout/header/footer oracle;
a negative probe changing one channel of pixel (0,0) is rejected with exit 1.
`DeckTile.cs` and `MapPick.cs` are byte-identical to baseline, and the ledger records
the differing string hash phases rather than changing production gradients.

The final evidence ledger preserves every original and final witness hash. Physical pointer,
gamepad, touchscreen, IME, screen-reader, native scoreboard composition and
Windows/Android device acceptance remain **NOT_RUN** locally. CI platform
checks are independent; a device-free pass does not establish physical parity.

## Rollback

Restore the three changed results source files to recover the former disabled
picker. No preference migration, protocol change or replay conversion is needed.
