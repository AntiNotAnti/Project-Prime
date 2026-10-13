# Scene setup ownership evidence (Phase 4B slice)

Base/source: `91b1c0c6c44e8c90effd250729709f791a84765e` (main). SDK 10.0.401/runtime 10.0.12; headless macOS arm64. Source hashes and measured assertion counts are in `scene-setup-ownership-validation.json`.

## Reproduced ownership violation

`SceneSetup.LoadNodeData` has exactly two production callers: `LoadGame`, and `RoomEntity` reload. It accepted no scene and selected single-player encounter navigation from the foreground `PlayerEntity.Players` and `GameState.EncounterState`. `InitHunterSpawns` already received a Scene and selected the private slot from that registry, then called foreground `PlayerEntity.Create`. Loading a private scene could therefore change foreground hunter/recolor/slot state and select another scene's encounter navigation.

The generated, asset-free `--scene-setup-ownership` fixture uses real foreground and private `ReplaySceneServices` scenes. It precreates turret objects and constructor-free spawner/save data to exclude retail model/logbook loading. It checks requested registry creation, foreground player/state/RNG sentinels, compatibility-facade binding, private navigation override in both directions, and foreground legacy behavior. Identical fixture against baseline assembly: four failed assertions. Candidate: all six pass. Generated one-node files are deleted and Paths restored in finally.

## Narrow implementation

Use existing `scene.Players.Create` and `scene.GameState.EncounterState`; require the owner Scene at both node-loading callers. No new services, static-to-instance rewrite, collision math, frame order, match rules, wire/replay layout, or authority behavior changes. Foreground creation delegates to the same ScenePlayerRegistry method as before. Other global AI/presentation seams remain outside this specific fix and are not claimed isolated by this PR.

Conceptual prerequisite: #497 scoped collision scratch removes shared active query scratch. This source change is independently main-based and does not contain that PR. #499 retirement/replay fixture should be run against the integrated branch by the root agent; this PR does not duplicate it. #472 adds a separate power-up initialization hook in SceneSetup: no pickup/grant/history changes are imported or duplicated, but integration review must retain both changes.

## Verification and limits

Release nettest build passes (existing baseline warnings retained). Asset-free ownership fixture is added to the existing network regression CI job. Existing headless retail-asset gates pass: 51 native biped assertions; seven-hunter movement tour (603 frames each); 43 replay-form assertions; 3034 v2/v3/v4 replay format/order/CRC/recovery assertions. Local retail assets are external and not committed. Movement fixture does not establish pad, moving-platform, teleport, knockback, grapple, Spire climb, HUD or camera/input parity. Native rendering, physical multiplayer and platform acceptance are NOT_RUN. No performance claim; this is a reproduced correctness/ownership seam.

Reproduce: `dotnet run --project tools/nettest -c Release -- --scene-setup-ownership`; baseline characterization can run the same helper with `--baseline` against the original main assembly (four failures are intentionally reported without aborting).
