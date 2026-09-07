# G1-G5 stabilization worktree inventory

Captured at the S0 checkpoint on 2026-09-07 from `main` at `141386f`.

The supplied implementation plan is the source for the S0 inventory, generated-artifact, and logical-commit rules. The user request is the authority for execution scope: G1-G5 work may be integrated, G6 is not started here, and the combined bots/observers/replay/telemetry/Backend-outage soak plus physical Android and high-refresh acceptance are intentionally skipped and must not be represented as passed by this document.

The initial checkout contained 127 tracked changed/deleted paths and 208 untracked files (253 compact `git status --short` records because Git collapses untracked directories). Every path was inspected and assigned below. Cross-cutting files are listed once under their primary integration owner; the later feature commits depend on those shared seams. Tests and documentation are intentionally committed in their own integration units.

## G1 carry-forward stabilization

These uncommitted paths complete the already-started G1 timing/render work. They are kept separate from G6 and are committed before the G2-G5 subsystem units.

    src/Client/Rendering/EffectPresentation.cs
    src/Client/Rendering/Entities/EntityPresentation.cs
    src/Client/Rendering/ModelPoseHistory.cs
    src/Client/Rendering/NodePoseSampler.cs
    src/Client/Rendering/ParticleInterpolation.cs
    src/Client/Rendering/RenderInterpolation.cs
    src/Client/Rendering/Renderer.cs
    src/Game/Simulation/LegacyTickProjection.cs
    src/Game/World/Entities/BeamProjectileEntity.cs
    src/Game/World/Entities/NodeDefenseEntity.cs

## G2 combat/HUD/radar

    src/Client/Combat/FeedbackAudio.cs
    src/Client/HUD/PostMatchPresentation.cs
    src/Client/HUD/Radar/RadarContact.cs
    src/Client/HUD/Radar/RadarFrame.cs
    src/Client/HUD/Radar/RadarSettings.cs
    src/Client/HUD/Radar/RadarWidget.cs
    src/Client/Launcher/Gui/SettingsView.cs
    src/Client/Presentation/Players/ClientPlayerBindings.cs
    src/Client/Presentation/Players/PlayerPresentationCombatFeedback.cs
    src/Client/Presentation/Players/PlayerPresentationPostMatch.cs
    src/Client/Presentation/Players/PlayerPresentationRadar.cs
    src/Client/Presentation/Players/PresentationPlayerEntityCombat.cs
    src/Client/Presentation/Players/PresentationPlayerHud.cs
    src/Client/Presentation/Players/PresentationPlayerSound.cs
    src/Client/Runtime/GameSettings.cs
    src/Client/Runtime/MenuSettings.cs
    src/Game/Combat/DamageContributionLedger.cs
    src/Game/Gameplay/Hunters/PlayerEntity.cs
    src/Game/Gameplay/Hunters/PlayerEntityClient.cs
    src/Game/Gameplay/Hunters/PlayerEntityCombat.cs
    src/Game/Gameplay/Hunters/PlayerProcess.cs
    src/Game/Match/MatchResult.cs
    src/Game/Match/PlayerMatchStats.cs
    src/Game/Protocol/CombatEvent.cs
    src/Game/Protocol/KillEvent.cs
    src/Game/Runtime/CombatShot.cs
    src/Server/Simulation/ServerCombat.cs
    src/Shared.Replay/Feedback/CombatFeedback.cs
    src/Shared.Replay/Feedback/CombatFeedbackState.cs
    src/Shared.Replay/Feedback/DamageHistory.cs
    src/Shared.Replay/Feedback/KillFeedEntry.cs
    src/Shared.Replay/Feedback/RecapArchive.cs
    src/Shared.Replay/Feedback/ReplayFeedbackState.cs
    src/Shared.Replay/Feedback/WorldFeedback.cs

## G3 match/network UX

The following cross-cutting protocol and authoritative-runtime files are owned by this unit because they establish the shared match-flow and network contracts used by the later G4/G5 additions.

    src/Android/Android.csproj
    src/Client/HUD/Network/NetworkHealth.cs
    src/Client/Input/GamepadInput.cs
    src/Client/Input/PadBindings.cs
    src/Client/Input/WeaponSelectionIntent.cs
    src/Client/Launcher/Gui/HomeView.cs
    src/Client/Launcher/Gui/ServerRow.cs
    src/Client/Launcher/Portable/ServerBrowser.cs
    src/Client/Networking/AuthoritativeCheck.cs
    src/Client/Networking/AuthoritativePlay.Diagnostics.cs
    src/Client/Networking/AuthoritativePlay.cs
    src/Client/Networking/ClientWorldState.cs
    src/Client/Networking/DesktopNetHostSession.cs
    src/Client/Networking/NetClient.cs
    src/Client/Networking/NetHostSession.cs
    src/Client/Networking/NetLaunch.cs
    src/Client/Networking/NetPlayerBridge.cs
    src/Client/Networking/NetStatus.cs
    src/Client/Networking/ServerProcess.cs
    src/Client/Presentation/Players/PlayerPresentationNetworkHealth.cs
    src/Client/Presentation/Players/PlayerPresentationWeaponSelection.cs
    src/Game/Match/LateJoinPolicy.cs
    src/Game/Match/MatchFlow.cs
    src/Game/Match/MatchLifecycle.cs
    src/Game/Match/MatchLogic.cs
    src/Game/Match/MatchOvertime.cs
    src/Game/Match/MatchPeriod.cs
    src/Game/Match/MatchRules.cs
    src/Game/Match/MatchRuntime.cs
    src/Game/Match/NetScoreboard.cs
    src/Game/Match/RulesetPreset.cs
    src/Game/Protocol/JoinPacket.cs
    src/Game/Protocol/MapRotation.cs
    src/Game/Protocol/MatchRulesWire.cs
    src/Game/Protocol/MatchTransitionPacket.cs
    src/Game/Protocol/NetHeader.cs
    src/Game/Protocol/NetProtocol.cs
    src/Game/Protocol/ReliableChannel.cs
    src/Game/Protocol/ReliableEventWindow.cs
    src/Game/Protocol/RosterChatPackets.cs
    src/Game/Protocol/SnapshotPacket.cs
    src/Game/Protocol/WorldEvent.cs
    src/Game/Protocol/WorldPacket.cs
    src/Game/Runtime/AuthoritativeContent.cs
    src/Game/Runtime/ISceneServices.cs
    src/Game/Runtime/WorldSignal.cs
    src/Game/World/Entities/ItemInstanceEntity.cs
    src/Game/World/Entities/ItemSpawnEntity.cs
    src/Game/World/Entities/OctolithFlagEntity.cs
    src/Server/Networking/AuthoritativeServer.cs
    src/Server/Networking/RulesetResolver.cs
    src/Server/Networking/ServerLateJoinOptions.cs
    src/Server/Networking/ServerNetwork.cs
    src/Server/Networking/ServerOvertimeOptions.cs
    src/Server/Networking/ServerSceneServices.cs
    src/Server/Program.cs
    src/Server/Replication/ServerWorldEvents.cs
    src/Server/Replication/WorldStateCapture.cs
    src/Server/Simulation/ServerSimulation.cs
    tools/check-dedicated-server.sh
    tools/nettest/InterpolationExperimentCheck.cs
    tools/nettest/OvertimeCheck.cs
    tools/nettest/WorldCheck.cs

## G4 backend/account/persistence

    Game.sln
    src/Backend/Backend.csproj
    src/Backend/Program.cs
    src/Backend/README.md
    src/Backend/Data/BackendDbContext.cs
    src/Backend/Data/BackendDesignTimeFactory.cs
    src/Backend/Data/Migrations/20260907102416_InitialAccounts.Designer.cs
    src/Backend/Data/Migrations/20260907102416_InitialAccounts.cs
    src/Backend/Data/Migrations/20260907104309_MatchLedger.Designer.cs
    src/Backend/Data/Migrations/20260907104309_MatchLedger.cs
    src/Backend/Data/Migrations/20260907104836_CareerStatistics.Designer.cs
    src/Backend/Data/Migrations/20260907104836_CareerStatistics.cs
    src/Backend/Data/Migrations/BackendDbContextModelSnapshot.cs
    src/Backend/Identity/AccountEndpoints.cs
    src/Backend/Identity/AccountOptions.cs
    src/Backend/Identity/ConfirmationEmail.cs
    src/Backend/Matches/CareerProjection.cs
    src/Backend/Matches/CareerQueries.cs
    src/Backend/Matches/CareerRebuild.cs
    src/Backend/Matches/MatchData.cs
    src/Backend/Matches/MatchEndpoints.cs
    src/Backend/Matches/MatchExport.cs
    src/Backend/Matches/MatchIngestion.cs
    src/Backend/Matches/ReportValidation.cs
    src/Backend/Profiles/ProfileEndpoints.cs
    src/Backend/Tickets/GameServerRegistry.cs
    src/Backend/Tickets/GameTicketIssuer.cs
    src/Backend/Tickets/TicketEndpoints.cs
    src/Client/Accounts/AccountSession.cs
    src/Client/Accounts/CareerQueries.cs
    src/Client/Launcher/Gui/AccountView.cs
    src/Client/Launcher/Portable/LauncherPrefs.cs
    src/Game/Identity/MatchReport.cs
    src/Game/Identity/PlayerId.cs
    src/Server/Identity/ServerTicketAuthority.cs
    src/Server/Identity/ServerTicketConfiguration.cs
    src/Server/Reporting/DurableSpool.cs
    src/Server/Reporting/MatchParticipantLedger.cs
    src/Server/Reporting/MatchReportOutbox.cs
    src/Server/Reporting/ReportTransport.cs
    src/Server/Reporting/ServerReportingOptions.cs
    src/Server/Server.csproj
    tools/check-project-boundaries.py
    tools/nettest/HistoryBoundaryCheck.cs

## G5 spectator/replay

    src/Android/GameView.cs
    src/Android/TouchControls.cs
    src/Client/Audio/Music.cs
    src/Client/Audio/Sfx.cs
    src/Client/Client.csproj
    src/Client/HUD/ChatBox.cs
    src/Client/Networking/DemoFile.cs
    src/Client/Networking/DemoPlayback.cs
    src/Client/Networking/DemoRecorder.cs
    src/Client/Networking/LegacyDemoState.cs
    src/Client/Networking/Protocol7DemoWorld.cs
    src/Client/Networking/ReplayControls.cs
    src/Client/Networking/ReplayTransport.cs
    src/Client/Presentation/Players/PlayerPresentationNetworkAfflictions.cs
    src/Client/Rendering/SpectatorCameraController.cs
    src/Client/Runtime/SpectatorMode.cs
    src/Game/World/Scene/Scene.World.cs
    src/Server/Replay/ServerReplayFeedback.cs
    src/Server/Replay/ServerReplayPolicy.cs
    src/Server/Replay/ServerReplaySession.cs
    src/Server/Spectator/ObserverTimeline.cs
    src/Server/Spectator/ServerNetworkObservers.cs
    src/Server/Spectator/ServerObserverConfiguration.cs
    src/Shared/Transport/UdpTransport.cs
    src/Shared.Replay/DemoFile.cs
    src/Shared.Replay/DemoRecordKind.cs
    src/Shared.Replay/Properties/AssemblyInfo.cs
    src/Shared.Replay/ReplayArchive.cs
    src/Shared.Replay/ReplayEventIndexer.cs
    src/Shared.Replay/Shared.Replay.csproj
    tools/nettest/ObserverSoakCheck.cs

## G5 bots/telemetry

    src/Game/Content/Formats/AiPersonality.cs
    src/Game/Telemetry/MatchTelemetry.cs
    src/Server/Admin/ServerNetworkAdmin.cs
    src/Server/Bots/BotFillPolicy.cs
    src/Server/Bots/BotParticipant.cs
    src/Server/Bots/ServerBotManager.cs
    src/Server/Bots/ServerNetworkBotAdmissions.cs
    src/Server/Networking/ReliableDiagnostics.cs
    src/Server/Telemetry/TelemetryCollector.cs
    src/Server/Telemetry/TelemetryWriter.cs
    src/Shared/Hosting/ServerProcessHost.cs
    src/Tools/Balance/BalanceCommand.cs
    src/Tools/Telemetry/TelemetryCommand.cs
    src/Tools/Program.cs
    tools/nettest/MixedCombatBackpressureCheck.cs
    tools/nettest/MixedCombatClients.cs
    tools/nettest/MixedCombatSoak.cs
    tools/run-mixed-combat-soak.py

## G5 voting/tournament

    src/Client/Networking/IntermissionVoteControls.cs
    src/Game/Protocol/IntermissionVotePacket.cs
    src/Server/Admin/AdminCommands.cs
    src/Server/Admin/AdminHttpServer.cs
    src/Server/Admin/TournamentAdmin.cs
    src/Server/Voting/ServerVoteOptions.cs
    src/Server/Voting/ServerVoteSession.cs
    src/Server/Voting/ServerVoting.cs

## Tests

All test changes are held for one integrated test commit so the implementation commits remain focused by subsystem.

    tests/Backend.Tests/AccountTests.cs
    tests/Backend.Tests/Backend.Tests.csproj
    tests/Backend.Tests/BackendFactory.cs
    tests/Backend.Tests/MatchLedgerTests.cs
    tests/Backend.Tests/PersistenceBoundaryTests.cs
    tests/Backend.Tests/ServerTicketAuthorityIntegrationTests.cs
    tests/Backend.Tests/TicketTests.cs
    tests/Tests/Admin/AdminCommandTests.cs
    tests/Tests/Admin/ReplayPolicyTests.cs
    tests/Tests/Admin/TournamentIntegrationTests.cs
    tests/Tests/Balance/BalanceReportTests.cs
    tests/Tests/Bots/MixedTeamBalanceTests.cs
    tests/Tests/Bots/ServerBotTests.cs
    tests/Tests/Client/AccountSessionTests.cs
    tests/Tests/Client/CareerQueryTests.cs
    tests/Tests/Client/DemoPlaybackTests.cs
    tests/Tests/Client/FeedbackAudioTests.cs
    tests/Tests/Client/IntermissionVoteControlsTests.cs
    tests/Tests/Client/NetworkHealthTests.cs
    tests/Tests/Client/PostMatchPresentationTests.cs
    tests/Tests/Client/PublicNetworkEntryTests.cs
    tests/Tests/Client/RadarTests.cs
    tests/Tests/Client/RecapArchiveTests.cs
    tests/Tests/Client/ReplayArchiveBoundsTests.cs
    tests/Tests/Client/ReplaySeekTests.cs
    tests/Tests/Client/ReplayStateTests.cs
    tests/Tests/Client/ReplayTouchControlsTests.cs
    tests/Tests/Client/ReplayTransientBoundTests.cs
    tests/Tests/Client/ServerBrowserTests.cs
    tests/Tests/Client/SpectatorTouchControlsTests.cs
    tests/Tests/Client/TouchResultNavigationTests.cs
    tests/Tests/Client/WeaponSelectionTests.cs
    tests/Tests/Client/WorldFeedbackAnnouncementTests.cs
    tests/Tests/Client/WorldFeedbackTests.cs
    tests/Tests/CombatFeedbackTests.cs
    tests/Tests/Game/AuthoritativeTimerTests.cs
    tests/Tests/Game/PlayerIdTests.cs
    tests/Tests/Identity/GameTicketTests.cs
    tests/Tests/Identity/GuestAdmissionCompatibilityTests.cs
    tests/Tests/Identity/ObserverPresentationTests.cs
    tests/Tests/Identity/ObserverTimelineTests.cs
    tests/Tests/Identity/TicketAdmissionTests.cs
    tests/Tests/Integration/KillDeliveryTests.cs
    tests/Tests/Integration/LateJoinTests.cs
    tests/Tests/Integration/LifecycleTests.cs
    tests/Tests/Integration/NetworkActionIntegrationTests.cs
    tests/Tests/Integration/VoteDeliveryTests.cs
    tests/Tests/Match/AssistResultTests.cs
    tests/Tests/Match/CombatAttributionTests.cs
    tests/Tests/Match/OvertimePolicyTests.cs
    tests/Tests/Match/RichWorldResultTests.cs
    tests/Tests/Match/WorldSignalTransitionTests.cs
    tests/Tests/NodePoseInterpolationTests.cs
    tests/Tests/ParticleInterpolationTests.cs
    tests/Tests/Protocol/AssistAttributionTests.cs
    tests/Tests/Protocol/ConnectionTests.cs
    tests/Tests/Protocol/MatchRulesProtocolTests.cs
    tests/Tests/Protocol/Protocol8AfflictionTests.cs
    tests/Tests/Protocol/ReliableDiagnosticsTests.cs
    tests/Tests/Protocol/ReliableEventWindowTests.cs
    tests/Tests/Protocol/TransportBoundsTests.cs
    tests/Tests/Protocol/WorldEventTests.cs
    tests/Tests/Protocol/WorldTests.cs
    tests/Tests/Reporting/MatchParticipantLedgerTests.cs
    tests/Tests/Reporting/MatchReportOutboxTests.cs
    tests/Tests/Server/RulesetResolverTests.cs
    tests/Tests/Server/ServerVoteSessionTests.cs
    tests/Tests/Telemetry/TelemetryCollectorTests.cs
    tests/Tests/Tests.csproj
    tools/tests/test_mixed_combat_soak.py
    tools/tests/test_project_boundaries.py
    tools/tests/test_server_update_package.py
    tools/tests/test_telemetry.py

## Docs and integration guidance

    SERVER.md
    docs/G1_G3_ACCEPTANCE.md
    docs/G1_G5_PROGRESS.md
    docs/G1_G5_VALIDATION.md
    docs/G1_RENDERING.md
    docs/G1_TIMING.md
    docs/G2_ATTRIBUTION.md
    docs/G2_AUDIO.md
    docs/G2_FEEDBACK.md
    docs/G2_RADAR.md
    docs/G3_CLIENT_UX.md
    docs/G3_INTERPOLATION_EXPERIMENT.md
    docs/G3_LATE_JOIN.md
    docs/G3_OVERTIME.md
    docs/G3_WORLD_EVENTS.md
    docs/G4_IMPLEMENTATION_DESIGN.md
    docs/G4_RANKING_SPEC.md
    docs/G4_REPORTING.md
    docs/G5_BALANCE_CHANGE_TEMPLATE.md
    docs/G5_BALANCE_REPORTS.md
    docs/G5_BOTS.md
    docs/G5_REPLAY.md
    docs/G5_TELEMETRY.md
    docs/G5_TOURNAMENT.md
    docs/G5_VOTING.md
    docs/STABILIZATION_WORKTREE_INVENTORY.md

The inventory itself and the narrow root-artifact ignore are part of the S0 documentation/normalization commit. The retired directories `src/MphRead`, `src/MphRead.Tests`, `src/MphRead.Android`, and `src/NcsfPlay` contain no non-build source files in this checkout; their `bin/`/`obj/` contents are already ignored. They are left untouched in S0 and deferred to any separate cleanup decision.

## Unrelated/pre-existing

The following paths remain unstaged and outside stabilization/G6 commits by explicit policy:

    LICENSE (deleted before S0)
    maps/README.md
    maps/parallax/parallax.pk3
    maps/parallax/source/README.md
    maps/parallax/source/art/README.md
    maps/parallax/source/art/alimbic-stone.png
    maps/parallax/source/art/build-materials.py
    maps/parallax/source/art/geometry-baseline.json
    maps/parallax/source/art/materials.png
    maps/parallax/source/art/package-materials.py
    maps/parallax/source/art/preview.png
    maps/parallax/source/art/verification.txt
    maps/parallax/source/build.py
    maps/parallax/source/scripts/parallax.shader
    maps/parallax/source/textures/parallax/accent.tga
    maps/parallax/source/textures/parallax/boost.tga
    maps/parallax/source/textures/parallax/climb.tga
    maps/parallax/source/textures/parallax/floor.tga
    maps/parallax/source/textures/parallax/lower.tga
    maps/parallax/source/textures/parallax/pad.tga
    maps/parallax/source/textures/parallax/spawn.tga
    maps/parallax/source/textures/parallax/trim.tga
    maps/parallax/source/textures/parallax/upper.tga
    maps/parallax/source/textures/parallax/wall.tga

## Generated/build artifacts

    FruityPrime

`FruityPrime` is a 96 MiB arm64 Mach-O executable at the repository root, produced by the local build and not source. It is retained on disk but ignored by the S0 normalization change. Existing `[Bb]in/`, `[Oo]bj/`, `artifacts/`, and related Visual Studio/.NET patterns cover the generated project outputs observed under `src/*`, `tests/*`, and `tools/*`.

## Unknown

None. Every non-ignored changed or untracked path was inspected and assigned above. No G6 lobby/UI implementation path is present in the S0 staging set.
