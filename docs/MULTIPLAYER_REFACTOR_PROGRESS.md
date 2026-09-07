# Multiplayer-only refactor implementation

The continuation begins at `f405497`, after the R0/R1 characterization and .NET
10 migration. R2 is included as an explicitly approved prerequisite to R3–R12.
Each pass remains a separate reviewable commit. Existing R1 release limitations
are recorded in [the baseline report](MULTIPLAYER_REFACTOR_BASELINE.md).

## R2 — Supported launch paths

Removed Adventure/save-slot and offline actions from the graphical and text
launchers, the legacy playable console menu, and desktop/Android match startup.
Persisted launch-kind numbers remain stable: Online=1, Host=3, Demo=5; removed
values and unknown values fail validation. Online/host startup requires the
authoritative client session and uses its admitted room and mode, without a
local-player fallback. Demo spectator playback is preserved.

Practice is not retained as a separate launcher mode. Existing hosting still
starts or requests an authoritative server and joins through the network path.
CLI local gameplay options are rejected; model and multiplayer-room inspection
remain non-playable asset tools. Campaign implementation, save data and parsers
remain temporarily present for the later deletion passes.

Validation: 347 C# tests (11 new launch-boundary cases), 34 Python tests, all 12
real-content mode scoring checks, and the data-enabled dedicated-server/content
regression suite passed. The server/nettest build had zero warnings or errors.
Desktop Release built successfully with the existing NU1903 dependency warning.
The Android Release APK published with both default ABIs, target API 36/minimum
24, verified signatures and ZIP alignment. Its seven XML documentation warnings
remain recorded in the build log; no emulator run was repeated for R2. Both asset
guards passed; available-room audit results exactly match R1, including the
explicit missing-content completeness limits.

## Planned remaining passes

R3 introduces scene-owned match state and immutable rules; R4 migrates scoring
and results; R5 owns lifecycle and rule replication on the server. R6–R9 remove
campaign behavior with content-aware guards. R10–R12 split the projects, converge
Android and enforce dependency boundaries. No later pass is marked complete
before its implementation and checks finish.

The content deletion gate remains conservative: the available audit covers 27
retail and three custom rooms, not the six missing First Hunt data sets. Shared
Door/ForceField/Platform/FH implementations cannot be deleted merely because
they were absent from that subset. In particular, ForceField dynamically uses
the existing Enemy49 lock implementation, and EnemySpawnEntity.cs also contains
FhEnemySpawnEntity; these dependencies must be separated before enemy deletion.
