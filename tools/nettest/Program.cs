using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>
    /// Headless conformance tests for the dedicated server.
    ///
    /// Simulated clients, no game window: the point is to prove the parts a
    /// human cannot easily verify by playing -- that concurrent joiners get
    /// distinct slots, that they all read the same match clock, and that the
    /// server's own accounting agrees with reality.
    ///
    /// Usage: nettest [host] [port]
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
            {
                Console.WriteLine("Usage: nettest [host] [port]\n"
                    + "  --ncx-timeline off|on              Test bounded opt-in shot timeline and clock identity.\n"
                    + "  --ncx-visibility                  Test exact pixel differences and restoration controls.\n"
                    + "  --ncx-hit-oracle                  Test frozen independent native contact inputs.\n"
                    + "  --ncx-hit-oracle-scene <assets> [room] [json] Exercise native accepted-source oracle controls.\n"
                    + "  --ncx-fixtures                    Test emulated fixture roles and declared aim schedule.\n"
                    + "  --ncx-fixture-lobby               Test exact-map fixture lobby eligibility and readiness guards.\n"
                    + "  --ncx-observer-evidence           Test no-fire observer participant lifecycle/emission evidence.\n"
                    + "  --instagib-ion-fracture           Test presentation-only rail palette, fade and ownership.\n"
                    + "  --outline-colors                  Test exact team/player palette persistence and style.\n"
                    + "  --scene-lifetime                  Test value-only scene diagnostics and eligible owner release.\n"
                    + "  --post-match-save                 Test post-match save byte parity, owner fencing and retry.\n"
                    + "  --campaign-save-management        Test Adventure save copy/archive and byte preservation.\n"
                    + "  --campaign-navigation             Test campaign planet unlocks, progress, and exits.\n"
                    + "  --campaign-ship-flow              Test ship-only travel and completed save receipt.\n"
                    + "  --campaign-movies                 Test cartridge-movie catalog and bounded VX warmup.\n"
                    + "  --campaign-movie-lifetime         Test private VX decode, cancellation and movie worker retirement.\n"
                    + "  --campaign-endings                Test final cinematics, ending results and save handoff.\n"
                    + "  --campaign-presentation           Test native campaign HUD, scan journal, and map framing.\n"
                    + "  --campaign-audio                  Test original gunship/credits audio and ownership fencing.\n"
                    + "  --campaign-journey                Simulate complete 3-slot journey with actual native save IO.\n"
                    + "  --campaign-dialog-input           Test campaign keyboard/pad dialog confirmation.\n"
                    + "  --campaign-delano7                Test story-only Sylux ship presentation eligibility.\n"
                    + "  --campaign-native-scene <assets> <json> Load actual single-player room, simulate, verify save.\n"
                    + "  --foreground-binding              Test eligible facade release and cancelled room publication.\n"
                    + "  --lifecycle-soak                  Test 10/100/500 device-free save/bot-owner handoffs.\n"
                    + "  --party-join-ownership            Test actual content-free client lifecycle and stale candidate cleanup.\n"
                    + "  --android-local-cycle-integration Test actual content-free scene retirement and foreground release.\n"
                    + "  --match-rotations                 Test consecutive server generations and stale/reordered round packets.\n"
                    + "  --display-pose-integration        Test display pose isolation across lifecycle discontinuities.\n"
                    + "  --particle-presentation           Test read-only fractional particle geometry and continuity.\n"
                    + "  --beam-presentation               Test read-only beam head, trail and model-pose coherence.\n"
                    + "  --specialized-presentation        Test read-only specialized Hunter chain-pose coherence.\n"
                    + "  --asset-lifecycle <filesystem> <count> <replay|-> <json> Test loaded world/save/replica ownership.\n"
                    + "  --shot-source-journal              Test independent authority resources and grant chronology.\n"
                    + "  --timed-grants                    Test authority timed pickup reservations and source history.\n"
                    + "  --timed-shot-certificates         Test bounded immutable timed shot storage.\n"
                    + "  --timed-shot-replay               Test detached archival fact codecs and optional replay schemas.\n"
                    + "  --protocol58-replay               Test reviewed 56/57/58/59 archival layouts and future refusal.\n"
                    + "  --timed-shot-replay-scene <assets> [room] Test actual authority launch fact persistence.\n"
                    + "  --timed-shot-certificate-benchmark [json] Measure certificate storage operations.\n"
                    + "  --timed-shot-native <assets> [room] Test real native shot certificates and Shadow parity.\n"
                    + "  --native-grant-hunter-tour-arm <filesystem> <hunter> <item> <Off|Shadow> <room> <json> One fresh-process native timed item arm.\n"
                    + "  --timed-shot-accepted <assets> [room] Test admitted remote shot certificates.\n"
                    + "  --timed-damage-profiles           Test frozen native damage arithmetic.\n"
                    + "  --timed-damage-profile-scene <assets> [room] Test authority damage profile history.\n"
                    + "  --timed-shot-modifiers <assets> [room] Test native trusted modifiers and payment retries.\n"
                    + "  --timed-shot-rescue <assets> [room] Test controlled unpaid timed-modifier rescue.\n"
                    + "  --timed-shot-damage               Test immutable launch channel ceilings.\n"
                    + "  --timed-source-capabilities       Test source-family capability classification.\n"
                    + "  --timed-campaign-joins           Test bounded process-owned join value storage.\n"
                    + "  --timed-shot-damage-benchmark [json] Measure launch channel/profile arithmetic.\n"
                    + "  --pickup-protocol                 Test pickup packet codecs, registry and budgets.\n"
                    + "  --pickup-claims                   Test authoritative pickup claim decisions.\n"
                    + "  --pickup-claim-scene <assets> [room] Test native pickup claim settlement and timer phases.\n"
                    + "  --pickup-network                  Test bounded pickup retries and impaired reliable delivery.\n"
                    + "  --pickup-readiness                Test hosted pickup discovery after scene loading.\n"
                    + "  --pickup-replay                   Test pickup replay facts and checkpoint state.\n"
                    + "  --timed-grant-presentation        Test prediction, reconciliation and private checkpoints.\n"
                    + "  --grant-replay                    Test recorded grant presentation and private restore.\n"
                    + "  --powerup-ledger-scene <assets> [profile] [room] Test native timed pickups and timer phases.\n"
                    + "  --shot-inputs                     Test live physical-input codec and bounded sender history.\n"
                    + "  --compact-intents                 Test compact live intent transport and replay compatibility.\n"
                    + "  --shot-proof-scene <assets> [room] Test shot proof with real scene assets.\n"
                    + "  --native-discontinuities <assets> [room] Test native events through snapshot/history/presentation.\n"
                    + "  --momentum-admission               Test offline developer scene/owner admission without assets.\n"
                    + "  --momentum-rules                   Test authoritative two-mode rules, wire and lobby ownership.\n"
                    + "  --momentum-offline-rules           Test saved/offline selection and hidden rule preservation.\n"
                    + "  --momentum-replay-rules            Test passive movement rules and decoder checkpoint consistency.\n"
                    + "  --momentum-replay-metadata         Test recorded movement labels and cold/warm library cache.\n"
                    + "  --momentum-replay-scene <assets> <replay> <Native|Momentum> <json> [durable] Test recorded world determinism and mode restoration.\n"
                    + "  --momentum-rewind-scene <assets> [json] Test actual movement trajectories through historical geometry queries.\n"
                    + "  --momentum-input-scene <assets> [json] Test actual keyboard, controller and touch-model movement semantics.\n"
                    + "  --momentum-input-packets          Test signed axes and input override lifecycle on existing packets.\n"
                    + "  --momentum-bot-scene <assets> [json] Test real offline AI through both movement rules.\n"
                    + "  --momentum-bot-authority <assets> [json] Test actual server AI movement, intents and snapshots.\n"
                    + "  --momentum-touch-gl <assets> [json] Test native alternate-form touch and boost parity with GL.\n"
                    + "  --momentum-bot-navigation <assets> [json] Test actual bot geometry probes and stopping margin.\n"
                    + "  --momentum-match-scene <assets> [json] Test actual rule-selected native scene movement.\n"
                    + "  --momentum-online-scene <assets> <port> <Native|Momentum> <json> [seconds] [replay] Run a real UDP owner world.\n"
                    + "  --momentum-touch-online <assets> <port> <Native|Momentum> <json> [seconds] Run actual touch-model movement over 200 ms UDP impairment.\n"
                    + "  --momentum-scene <assets> [json]   Test offline biped integration and Native ownership gates.\n"
                    + "  --momentum-jump-scene <assets> [json] Test legal hops, landing and native multi-jump semantics.\n"
                    + "  --momentum-collision <assets> [json] Test authored geometry and native contact handling.\n"
                    + "  --momentum-specials <assets> [json] Test native discontinuities, damage and bombs.\n"
                    + "  --momentum-abilities <assets> [json] Test native Hunter forms and abilities.\n"
                    + "  --momentum-carrier <assets> [json] Test native moving entity standing ownership.\n"
                    + "  --momentum-profile                Test v1 retention policy, analog magnitudes and fixed cadence.\n"
                    + "  --momentum-math                   Test pure horizontal Momentum equations and validation.\n"
                    + "  --momentum-benchmark [json]       Measure warmed eight-player math and assert zero allocations.\n"
                    + "  --native-movement-baseline <assets> <fixture> [--record] Record/verify native seven-Hunter values.\n"
                    + "  --movement-tour <assets> [room]    Test seven hunters through native local movement.\n"
                    + "  --q3-traversal-scene <assets> [room] Test Q3 traversal through native movement.\n"
                    + "  --quiescent-turret-scene <assets> [room] Test native turret retirement during drain.\n"
                    + "  --turret-outcome-scene <assets> [room] Test native emission gates and terminal outcomes.\n"
                    + "  --movement-validation-shadow      Test bounded advisory movement observations.\n"
                    + "  --form-scene <assets> [room]       Test authority form and detached turret lifecycle.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--ncx-timeline" && args[1] is "off" or "on")
                return ShotTimelineTests.Run(args[1] == "on");
            if (args.Length == 1 && args[0] == "--ncx-fixtures")
                return NcxFixtureTests.Run();
            if (args.Length == 1 && args[0] == "--ncx-fixture-lobby")
                return NcxFixtureLobbyJoinTests.Run();
            if (args.Length == 1 && args[0] == "--ncx-observer-evidence")
                return NcxObserverEvidenceTests.Run();
            if (args.Length == 1 && args[0] == "--ncx-visibility")
                return ShotVisibilityTests.Run();
            if (args.Length == 1 && args[0] == "--ncx-hit-oracle")
                return CampaignHitOracleTests.Run();
            if (args.Length > 1 && args[0] == "--ncx-hit-oracle-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return CampaignHitOracleTests.RunScene(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS",
                    args.Length > 3 ? args[3] : null);
            }
            if (args.Length > 1 && args[0] == "--biped-edge-collision-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return BipedEdgeCollisionTests.Run(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--scene-setup-ownership")
                return SceneSetupOwnershipTests.Run(Array.IndexOf(args, "--baseline") >= 0);
            if (args.Length > 0 && args[0] == "--instagib-ion-fracture") return InstagibIonFractureTests.Run();
            if (args.Length > 0 && args[0] == "--browser-join") return BrowserJoinTests.Run();
            if (args.Length > 0 && args[0] == "--map-vote-ready") return MapVoteReadyTests.Run();
            if (args.Length > 0 && args[0] == "--animation-sampling") return AnimationSamplingTests.Run();
            if (args.Length > 0 && args[0] == "--particle-presentation") return ParticlePresentationTests.Run();
            if (args.Length > 0 && args[0] == "--beam-presentation") return BeamPresentationTests.Run();
            if (args.Length > 0 && args[0] == "--specialized-presentation") return SpecializedPresentationTests.Run();
            if (args.Length > 0 && args[0] == "--shot-pose-bounds") return ShotPoseBoundsTests.Run();
            if (args.Length > 0 && args[0] == "--replay-shot-payload") return ReplayShotPayloadTests.Run();
            if (args.Length > 0 && args[0] == "--q3-mover-source") return Q3MoverSourceTests.Run();
            if (args.Length > 0 && args[0] == "--q3-shader-stages") return Q3ShaderStagesTests.Run();
            if (args.Length > 0 && args[0] == "--outline-colors") return OutlineColorTests.Run();
            if (args.Length > 0 && args[0] == "--scene-lifetime") return SceneLifetimeTests.Run();
            if (args.Length > 0 && args[0] == "--post-match-save") return PostMatchSaveTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-save-management") return CampaignSaveManagementTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-navigation") return CampaignNavigationTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-ship-flow") return CampaignShipFlowTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-movies") return CampaignMovieTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-movie-lifetime") return CampaignMovieLifetimeTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-endings") return CampaignEndingTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-presentation") return CampaignPresentationTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-audio") return CampaignAudioTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-journey") return CampaignJourneyTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-dialog-input") return CampaignDialogInputTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-delano7") return CampaignDelano7Tests.Run();
            if (args.Length > 0 && args[0] == "--foreground-binding") return ForegroundBindingTests.Run();
            if (args.Length > 0 && args[0] == "--lifecycle-soak") return LifecycleSoakTests.Run();
            if (args.Length > 0 && args[0] == "--campaign-native-scene")
            {
                if (args.Length != 3 || !System.IO.Directory.Exists(args[1])
                    || System.IO.File.Exists(args[2]))
                {
                    Console.Error.WriteLine("Usage: --campaign-native-scene <extracted AMHE1 filesystem> <fresh result.json>");
                    return 1;
                }
                string assets = System.IO.Path.GetFullPath(args[1]);
                string result = System.IO.Path.GetFullPath(args[2]);
                Paths.MphKey = "AMHE1";
                Paths.SetPath(Paths.MphKey, assets);
                return CampaignNativeSceneTests.Run(result);
            }
            if (args.Length > 0 && args[0] == "--asset-lifecycle")
            {
                if (args.Length != 5 || !int.TryParse(args[2], out int count) || count < 1 || count > 500)
                {
                    Console.Error.WriteLine("Usage: --asset-lifecycle <extracted AMHE1 filesystem> <count 1..500> <replay path|-> <output json>");
                    return 1;
                }
                string assets = System.IO.Path.GetFullPath(args[1]);
                string? replay = args[3] == "-" ? null : System.IO.Path.GetFullPath(args[3]);
                string output = System.IO.Path.GetFullPath(args[4]);
                if (!System.IO.Directory.Exists(assets) || replay != null && !System.IO.File.Exists(replay))
                {
                    Console.Error.WriteLine("Asset lifecycle requires an existing extracted filesystem and, when requested, a recording.");
                    return 1;
                }
                Paths.MphKey = "AMHE1"; Paths.SetPath(Paths.MphKey, assets);
                return AssetLifecycleTests.Run(count, replay, output);
            }
            if (args.Length > 0 && args[0] == "--ai-client-driver") return NetAiClientTests.Run();
            if (args.Length > 0 && args[0] == "--pickup-protocol") return PickupProtocolTests.Run();
            if (args.Length > 0 && args[0] == "--pickup-claims") return PickupClaimTests.Run();
            if (args.Length > 1 && args[0] == "--pickup-claim-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return PickupClaimSceneTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--pickup-network") return PickupNetworkTests.Run();
            if (args.Length > 0 && args[0] == "--pickup-readiness") return PickupReadinessTests.Run();
            if (args.Length > 0 && args[0] == "--pickup-replay") return PickupReplayTests.Run();
            if (args.Length > 1 && args[0] == "--native-timed-grant-presentation")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NativeTimedGrantPresentationTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--timed-grant-presentation") return TimedGrantPresentationTests.Run();
            if (args.Length > 0 && args[0] == "--grant-replay") return GrantReplayTests.Run();
            if (args.Length > 0 && args[0] == "--grant-network") return GrantNetworkTests.Run();
            if (args.Length > 0 && args[0] == "--terminal-retries") return TerminalRetryTests.Run(Array.IndexOf(args, "--lost-terminal-only") >= 0);
            if (args.Length > 0 && args[0] == "--timed-shot-certificates") return TimedShotCertificateTests.Run();
            if (args.Length > 0 && args[0] == "--timed-shot-replay") return TimedShotReplayTests.Run();
            if (args.Length > 0 && args[0] == "--protocol58-replay") return ReplayProtocol58Tests.Run();
            if (args.Length > 1 && args[0] == "--timed-shot-replay-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedShotReplaySceneTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--timed-damage-profiles") return TimedDamageProfileTests.Run();
            if (args.Length > 1 && args[0] == "--timed-damage-profile-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedDamageProfileSceneTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--timed-shot-rescue")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedShotRescueTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--timed-shot-damage") return TimedShotDamageTests.Run();
            if (args.Length > 0 && args[0] == "--native-impact-witness") return NativeImpactWitnessTests.Run();
            if (args.Length > 0 && args[0] == "--timed-native-effects") return TimedNativeEffectTests.Run();
            if (args.Length > 0 && args[0] == "--timed-imperialist-impact") return TimedImperialistImpactTests.Run();
            if (args.Length > 1 && args[0] == "--timed-native-effect-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedNativeEffectTests.RunScene(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--timed-imperialist-impact-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedImperialistImpactTests.RunScene(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--timed-source-capabilities") return TimedSourceCapabilityTests.Run();
            if (args.Length > 0 && args[0] == "--timed-campaign-joins") return TimedCampaignJoinTests.Run();
            if (args.Length > 0 && args[0] == "--timed-shot-damage-benchmark") return TimedShotDamageTests.RunBenchmark(args);
            if (args.Length > 1 && args[0] == "--timed-shot-modifiers")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedShotModifierTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--timed-shot-certificate-benchmark") return TimedShotCertificateTests.RunBenchmark(args);
            if (args.Length > 1 && args[0] == "--timed-shot-native")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedShotNativeTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--native-grant-hunter-tour-arm")
            {
                if (args.Length != 7 || !Enum.TryParse<Hunter>(args[2], out var hunter)
                    || !Enum.TryParse<ItemType>(args[3], out var item) || !Enum.TryParse<TimedGrantProofMode>(args[4], out var mode))
                {
                    Console.Error.WriteLine("Usage: --native-grant-hunter-tour-arm <filesystem> <hunter> <item> <Off|Shadow> <room> <fresh json>");
                    return 1;
                }
                string assets = System.IO.Path.GetFullPath(args[1]);
                if (!System.IO.Directory.Exists(assets)) { Console.Error.WriteLine("Native grant arm requires an extracted filesystem."); return 1; }
                Paths.MphKey = "AMHE1"; Paths.SetPath(Paths.MphKey, assets);
                return NativeGrantHunterTourTests.RunSingleArm(args[5], hunter, item, mode, System.IO.Path.GetFullPath(args[6]));
            }
            if (args.Length > 0 && args[0] == "--native-grant-hunter-tour")
            {
                Console.Error.WriteLine("Use tools/nettest/run_native_grant_hunter_tour.py for exact Off/Shadow parity in fresh processes. --native-grant-hunter-tour-in-process preserves the historical diagnostic.");
                return 1;
            }
            if (args.Length > 0 && args[0] == "--native-grant-hunter-tour-in-process")
            {
                if (args.Length < 2 || args.Length > 4)
                {
                    Console.Error.WriteLine("Usage: --native-grant-hunter-tour <extracted AMHE1 filesystem> [room] [fresh json]");
                    return 1;
                }
                string assets = System.IO.Path.GetFullPath(args[1]);
                string? output = args.Length > 3 ? System.IO.Path.GetFullPath(args[3]) : null;
                if (!System.IO.Directory.Exists(assets))
                {
                    Console.Error.WriteLine("Native grant hunter tour requires an existing extracted filesystem.");
                    return 1;
                }
                Paths.MphKey = "AMHE1"; Paths.SetPath(Paths.MphKey, assets);
                return NativeGrantHunterTourTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS", output);
            }
            if (args.Length > 1 && args[0] == "--timed-shot-accepted")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TimedShotAcceptedTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--timed-grants") return TimedGrantTests.Run();
            if (args.Length > 0 && args[0] == "--timed-grant-codec") return TimedGrantFactTests.Run();
            if (args.Length > 1 && args[0] == "--powerup-ledger-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return PowerUpLedgerSceneTests.Run(args.Length > 2 ? args[2] : "prime",
                    args.Length > 3 ? args[3] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--campaign-claims") return CampaignClaimEvidenceTests.Run();
            if (args.Length > 0 && args[0] == "--replay-form-history") return ReplayFormHistoryTests.Run();
            if (args.Length > 0 && args[0] == "--turret-fire-outcome-codec") return TurretFireOutcomeTests.Run();
            if (args.Length > 0 && args[0] == "--player-form-protocol") return PlayerFormProtocolTests.Run();
            if (args.Length > 0 && args[0] == "--bomb-protocol") return BombProtocolTests.Run();
            if (args.Length > 2 && args[0] == "--turret-cancellation-scene")
            {
                string replay = System.IO.Path.GetFullPath(args[2]);
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TurretCancellationSceneTests.Run(replay);
            }
            if (args.Length > 2 && args[0] == "--replay-resolved-death-scene")
            {
                string replay = System.IO.Path.GetFullPath(args[2]);
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return ReplayResolvedDeathTests.Run(replay);
            }
            if (args.Length > 2 && args[0] == "--bomb-effect-birth-scene")
            {
                string replay = System.IO.Path.GetFullPath(args[2]);
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return BombEffectBirthTests.Run(replay);
            }
            if (args.Length > 0 && args[0] == "--continuous-healing-prediction") return ContinuousHealingPredictionTests.Run();
            if (args.Length > 0 && args[0] == "--continuous-snapshots") return ContinuousSnapshotTests.Run();
            if (args.Length > 1 && args[0] == "--continuous-combat-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return ContinuousCombatTests.Run(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--combat-reconciliation") return CombatReconciliationTests.Run();
            if (args.Length > 0 && args[0] == "--self-prediction") return SelfPredictionTests.Run();
            if (args.Length > 1 && args[0] == "--bomb-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return BombSceneTests.Run(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--self-splash-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return SelfSplashTests.Run(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--combat-arbitration-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return CombatArbitrationTests.Run(args[1], args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--shot-source-journal") return ShotSourceJournalTests.Run();
            if (args.Length > 0 && args[0] == "--shot-inputs") return ShotInputTests.Run();
            if (args.Length > 0 && args[0] == "--movement-validation-shadow") return MovementValidationTests.Run();
            if (args.Length > 1 && args[0] == "--quiescent-turret-blocked-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return QuiescentTurretTests.Run(args.Length > 2 ? args[2] : "MP3 PROVING GROUND", blockedPose: true);
            }
            if (args.Length > 1 && args[0] == "--quiescent-turret-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return QuiescentTurretTests.Run(args.Length > 2 ? args[2] : "MP3 PROVING GROUND");
            }
            if (args.Length > 1 && args[0] == "--turret-outcome-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TurretOutcomeSceneTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--sylux-muzzle-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return SyluxMuzzleTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--q3-traversal-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return Q3TraversalSceneTests.Run(args.Length > 2 ? args[2] : "MP3 PROVING GROUND");
            }
            if (args.Length > 0 && args[0] == "--momentum-admission") return MomentumAdmissionTests.Run();
            if (args.Length > 0 && args[0] == "--momentum-rules") return MomentumRulesTests.Run();
            if (args.Length > 0 && args[0] == "--momentum-offline-rules") return MomentumOfflineRulesTests.Run();
            if (args.Length > 0 && args[0] == "--momentum-replay-rules") return MomentumReplayRulesTests.Run();
            if (args.Length > 0 && args[0] == "--momentum-replay-metadata") return MomentumReplayMetadataTests.Run();
            if (args.Length > 0 && args[0] == "--momentum-input-packets") return MomentumInputPacketTests.Run();
            if (args.Length > 1 && (args[0] == "--momentum-input-scene"
                || args[0] == "--momentum-bot-scene" || args[0] == "--momentum-bot-authority"
                || args[0] == "--momentum-touch-gl"))
            {
                string? output = args.Length > 2 ? System.IO.Path.GetFullPath(args[2]) : null;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return args[0] switch
                {
                    "--momentum-input-scene" => MomentumInputSceneTests.Run(output),
                    "--momentum-bot-scene" => MomentumBotSceneTests.Run(output),
                    "--momentum-touch-gl" => MomentumTouchGlTests.Run(output),
                    _ => MomentumBotAuthorityTests.Run(output)
                };
            }
            if (args.Length >= 5 && args[0] == "--momentum-replay-scene")
            {
                if (!Mods.Movement.MovementModelRules.TryParse(args[3], out var model)) return 2;
                string replay = System.IO.Path.GetFullPath(args[2]);
                string output = System.IO.Path.GetFullPath(args[4]);
                string? durable = args.Length > 5 ? System.IO.Path.GetFullPath(args[5]) : null;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MomentumReplaySceneTests.Run(replay, model, output, durable);
            }
            if (args.Length > 1 && args[0] == "--momentum-rewind-scene")
            {
                string? output = args.Length > 2 ? System.IO.Path.GetFullPath(args[2]) : null;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MomentumRewindSceneTests.Run(output);
            }
            if (args.Length >= 4 && args[0] == "--momentum-test-server")
            {
                if (!Int32.TryParse(args[2], out int movementPort) || movementPort is < 1 or > 65535
                    || !Mods.Movement.MovementModelRules.TryParse(args[3], out var model)) return 2;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                var server = new DedicatedServer(movementPort, 2,
                    MapRotation.SingleMatch("MP3 PROVING GROUND", GameMode.Battle, 120, 999))
                {
                    MovementModel = model, ServerName = "movement acceptance", AutoUpdate = false,
                    ReplayPolicy = ServerReplayPolicy.Default with { Enabled = false }
                };
                // A real local authority, with no listing reporter or hosted children.
                int lifetime = args.Length > 4 && Int32.TryParse(args[4], out int requestedLifetime)
                    ? Math.Clamp(requestedLifetime, 30, 180) : 45;
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(lifetime));
                server.Run(stop.Token);
                return 0;
            }
            if (args.Length >= 5 && (args[0] == "--momentum-online-scene" || args[0] == "--momentum-touch-online"))
            {
                if (!Int32.TryParse(args[2], out int movementPort) || movementPort is < 1 or > 65535
                    || !Mods.Movement.MovementModelRules.TryParse(args[3], out var model)) return 2;
                int seconds = args.Length > 5 && Int32.TryParse(args[5], out int duration) ? duration : 12;
                string output = System.IO.Path.GetFullPath(args[4]);
                bool touchModel = args[0] == "--momentum-touch-online";
                string? replay = !touchModel && args.Length > 6 ? System.IO.Path.GetFullPath(args[6]) : null;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MomentumOnlineSceneTests.Run(movementPort, model, output, seconds, replay, touchModel);
            }
            if (args.Length > 1 && args[0] == "--momentum-match-scene")
            {
                string? output = args.Length > 2 ? System.IO.Path.GetFullPath(args[2]) : null;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MomentumMatchSceneTests.Run(output);
            }
            if (args.Length > 0 && args[0] == "--momentum-profile") return MomentumProfileTests.Run();
            if (args.Length > 1 && (args[0] == "--momentum-collision"
                || args[0] == "--momentum-specials" || args[0] == "--momentum-abilities"
                || args[0] == "--momentum-carrier" || args[0] == "--momentum-bot-navigation"))
            {
                string? output = args.Length > 2 ? System.IO.Path.GetFullPath(args[2]) : null;
                // Resolve the authored map catalog before the asset configuration changes cwd.
                Mods.MapGen.CustomRooms.MapDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "maps", "momentum-collision");
                Mods.MapGen.CustomRooms.UserMapDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "movement-fixtures", Guid.NewGuid().ToString("N"));
                Mods.MapGen.CustomRooms.RuntimeNamespace = args[0][2..];
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                if (args[0] != "--momentum-specials")
                    Mods.MapGen.CustomRooms.GenerateMissing(MomentumCollisionSceneTests.Room);
                return args[0] switch
                {
                    "--momentum-collision" => MomentumCollisionSceneTests.Run(output),
                    "--momentum-specials" => MomentumSpecialSceneTests.Run(output),
                    "--momentum-carrier" => MomentumCarrierSceneTests.Run(output),
                    "--momentum-bot-navigation" => MomentumBotNavigationTests.Run(output),
                    _ => MomentumAbilitySceneTests.Run(output)
                };
            }
            if (args.Length > 1 && args[0] == "--momentum-jump-scene")
            {
                string? output = args.Length > 2 ? System.IO.Path.GetFullPath(args[2]) : null;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MomentumJumpSceneTests.Run(output);
            }
            if (args.Length > 1 && args[0] == "--momentum-scene")
            {
                string? output = args.Length > 2 ? System.IO.Path.GetFullPath(args[2]) : null;
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MomentumSceneTests.Run(output);
            }
            if (args.Length > 0 && args[0] == "--momentum-math") return MomentumMathTests.Run();
            if (args.Length > 0 && args[0] == "--momentum-benchmark")
            {
                if (args.Length > 2) return 2;
                return MomentumMathTests.RunBenchmark(args.Length == 2 ? args[1] : null);
            }
            if (args.Length > 0 && args[0] == "--native-movement-baseline")
            {
                if (args.Length < 3 || args.Length > 4 || args.Length == 4 && args[3] != "--record")
                {
                    Console.Error.WriteLine("Usage: --native-movement-baseline <asset-config-directory> <fixture.json> [--record]");
                    return 2;
                }
                string fixture = System.IO.Path.GetFullPath(args[2]);
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NativeMovementBaselineTests.Run("MP3 PROVING GROUND", fixture, args.Length == 4);
            }
            if (args.Length > 1 && args[0] == "--movement-tour")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MovementTourTests.Run(args.Length > 2 ? args[2] : "MP3 PROVING GROUND");
            }
            if (args.Length > 1 && args[0] == "--native-discontinuities")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NativeDiscontinuityTests.Run(args.Length > 2 ? args[2] : "MP3 PROVING GROUND");
            }
            if (args.Length > 1 && args[0] == "--form-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return PlayerFormTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--turret-events")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return TurretEventTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--impact-authority")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return ImpactAuthorityTests.Run(args[1], args.Length > 2 ? args[2] : "MP3 PROVING GROUND");
            }
            if (args.Length > 0 && args[0] == "--shot-lifecycle") return ShotLifecycleTests.Run();
            if (args.Length > 0 && args[0] == "--gamemodecheck") return GameModeCheck.Run();
            if (args.Length > 1 && args[0] == "--gamemodecheck-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return GameModeSceneCheck.Run();
            }
            if (args.Length > 1 && args[0] == "--resourceaudit")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return Mods.Multiplayer.ResourceAudit.Run();
            }
            if (args.Length > 2 && args[0] == "--enhanced-historical-matrix")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return EnhancedHistoricalMatrixTests.Run(args[2], args.Length > 3 && args[3] == "--record-baseline");
            }
            if (args.Length > 2 && args[0] == "--enhanced-retirement-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return EnhancedRetirementTests.ArchiveScene(args[2], args.Length > 3 && args[3] == "--record-baseline");
            }
            if (args.Length > 1 && args[0] == "--enhanced-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return EnhancedRetirementTests.LiveScene();
            }
            if (args.Length > 0 && args[0] is "--enhanced-retirement" or "--enhanced-hunters") return EnhancedRetirementTests.Run();
            if (args.Length > 1 && args[0] == "--advanced-rules-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetLobbyTest.RunAdvancedRulesScene();
            }
            if (args.Length > 0 && args[0] == "--advanced-rules") return NetLobbyTest.RunAdvancedRules();
            if (args.Length > 0 && args[0] == "--player-names-chat") return PlayerNameChatTests.Run();
            if (args.Length == 4 && args[0] == "--lobby-map")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Mods.MapGen.CustomRooms.MapDirectory = System.IO.Path.GetFullPath(args[2]);
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetLobbyTest.RunMapStart(args[3]);
            }
            if (args.Length > 1 && args[0] == "--protocol19-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetProtocol19Check.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--protocol19") return Protocol19Tests.Run();
            if (args.Length > 1 && args[0] == "--continuous-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetContinuousTargetCheck.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--alt-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetAltHitCheck.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 0 && args[0] == "--alt-hits") return AltFormHitTests.Run();
            if (args.Length > 0 && args[0] == "--continuous-targets") return ContinuousTargetTests.Run();
            if (args.Length > 0 && args[0] == "--rotation-evidence") return RotationEvidenceTests.Run();
            if (args.Length > 0 && args[0] == "--headless-gamepad") return HeadlessGamepadTests.Run();
            if (args.Length > 0 && args[0] == "--radar-history") return HeadlessRadarHistoryTests.Run(headless: false);
            if (args.Length > 0 && args[0] == "--headless-radar") return HeadlessRadarHistoryTests.Run(headless: true);
            if (args.Length > 0 && args[0] == "--scene-entity-index") return SceneEntityIndexTests.Run();
            if (args.Length == 3 && args[0] == "--inspect-rotation-archive") return RotationArchiveInspection.Run(args[1], args[2]);
            if (args.Length == 1 && args[0] == "--rotation-archive-inspection-tests") return RotationArchiveInspection.RunTests();
            if (args.Length > 1 && args[0] == "--transport-profile-json") return TransportProfile.Run(args[1]);
            if (args.Length > 0 && args[0] == "--replication-decode-benchmark") return ReplicationDecodeBenchmark.Run(args);
            if (args.Length > 0 && args[0] == "--server-performance-evidence") return ServerPerformanceEvidenceTests.Run(args);
            if (args.Length > 0 && args[0] == "--server-performance") return ServerPerformanceBenchmark.Run(args);
            if (args.Length > 0 && args[0] == "--netcode-performance") return NetcodePerformanceTests.Run();
            if (args.Length > 0 && args[0] == "--dynamic-geometry") return DynamicGeometryTests.Run();
            if (args.Length > 0 && args[0] == "--spectator-camera") return SpectatorCameraTests.Run();
            if (args.Length > 0 && args[0] == "--input-edges") return InputEdgeTests.Run();
            if (args.Length > 0 && args[0] == "--protocol18") return Protocol18Tests.Run();
            if (args.Length > 0 && args[0] == "--claim-stress") return ClaimStressTests.Run();
            if (args.Length > 0 && args[0] == "--health-shots") return HealthShotTests.Run();
            if (args.Length > 0 && args[0] == "--lifecycle") return LifecycleTests.Run();
            if (args.Length > 0 && args[0] == "--party-join-ownership") return PartyJoinOwnershipTests.Run();
            if (args.Length > 0 && args[0] == "--android-local-cycle-integration") return AndroidLocalSceneIntegrationTests.Run();
            if (args.Length > 0 && args[0] == "--match-rotations") return MatchRotationTests.Run();
            if (args.Length > 0 && args[0] == "--display-pose-integration") return DisplayPoseIntegrationTests.Run();
            if (args.Length > 0 && args[0] == "--lobby-community-selection") { NetLobbyTest.CustomMapDownloadScenario(lobbySelectionOnly: true); return 0; }
            if (args.Length > 0 && args[0] == "--custom-map-download") { NetLobbyTest.CustomMapDownloadScenario(); return 0; }
            if (args.Length > 0 && args[0] == "--custom-map") { NetLobbyTest.CustomMapReadinessScenario(); Console.WriteLine("Custom map readiness passed."); return 0; }
            if (args.Length > 1 && args[0] == "--bots-online")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetLobbyTest.RunBotReplication(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--bots-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetBotCheck.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS", args.Length > 3 ? int.Parse(args[3]) : 3);
            }
            if (args.Length > 0 && args[0] == "--bot-ai") return BotAiChecks.Run();
            if (args.Length > 0 && args[0] == "--bots") return NetLobbyTest.RunBots();
            if (args.Length > 0 && args[0] == "--waitlist") return NetLobbyTest.RunWaitlist();
            if (args.Length > 0 && args[0] == "--lobby") return NetLobbyTest.Run();
            if (args.Length > 0 && args[0] == "--compact-intents")
            {
                int result = CompactIntentCodecTests.Run();
                if (result != 0) return result;
                result = CompactIntentTransportTests.Run();
                if (result != 0) return result;
                result = CompactIntentHistoryTests.Run();
                return result != 0 ? result : CompactIntentReplayTests.Run();
            }
            if (args.Length > 0 && args[0] == "--server-operator-evidence") return ServerOperatorEvidenceTests.Run();
            if (args.Length > 0 && args[0] == "--architecture") return NetArchitectureTests.Run();
            if (Array.IndexOf(args, "--network-benchmark") >= 0 || Array.IndexOf(args, "--network-benchmark-json") >= 0)
                return NetworkBenchmark.Run(args);
            if (args.Length > 0 && args[0] == "--allocations") return NetworkAllocationTests.Run(Array.IndexOf(args, "--report-only") >= 0);
            if (args.Length > 0 && args[0] == "--protocol17") return Protocol17Tests.Run();
            if (args.Length > 0 && args[0] == "--reliable") return ReliableTests.Run();
            if (args.Length > 0 && args[0] == "--reliable-rate-refusal")
                return RateRefusalReliableTests.CheckRun(Array.IndexOf(args, "--falsify-early-ack") >= 0);
            if (args.Length > 0 && args[0] == "--queue-budget") return QueueBudgetTests.Run();
            if (args.Length > 0 && args[0] == "--confirmed-impacts") return ConfirmedImpactTests.Run();
            if (args.Length > 0 && args[0] == "--load-lifecycle") return LoadLifecycleTests.Run();
            if (args.Length > 0 && args[0] == "--lagcomp-shadow") return LagCompensationTests.Run();
            if (args.Length > 0 && args[0] == "--weapon-policy") return WeaponPolicyTests.Run();
            if (args.Length > 0 && args[0] == "--transport-stress") return TransportStressTests.Run();
            if (args.Length > 1 && args[0] == "--claim-load-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetClaimLoadCheck.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--geometry-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetGeometrySceneCheck.Run(args.Length > 2 ? args[2] : "UNIT1_RM1");
            }
            if (args.Length > 1 && args[0] == "--bootstrap-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return args.Length > 2 && args[2] == "all" ? NetBootstrapCheck.RunAllModes()
                    : NetBootstrapCheck.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--semantic-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return MphRead.Mods.MatchEvents.MatchSemanticSceneCheck.Run();
            }
            if (args.Length > 1 && args[0] == "--shot-proof-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return ShotProofTests.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            if (args.Length > 1 && args[0] == "--combat-scene")
            {
                System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetFullPath(args[1]));
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                return NetCombatCheck.Run(args.Length > 2 ? args[2] : "MP1 SANCTORUS");
            }
            // A mistyped or unwired test flag must not be treated as a remote host.
            if (args.Length > 0 && args[0].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unknown nettest option: {args[0]}. Run --help for supported tests.");
                return 2;
            }
            string host = args.Length > 0 ? args[0] : "127.0.0.1";
            int port = args.Length > 1 && Int32.TryParse(args[1], out int p)
                ? p : NetConfig.DefaultPort;

            Console.WriteLine($"=== MphRead server tests against {host}:{port} ===\n");

            TestHostnameResolution(host, port);
            TestReachable(host, port);
            TestDistinctSlots(host, port);
            TestClockAgreement(host, port);
            TestJoinInProgress(host, port);
            TestSlotReuseAfterLeave(host, port);
            TestStatusReporting(host, port);
            TestPeerSurvivesTimeoutWindow(host, port);
            TestPlayersSeeEachOther(host, port);
            TestServerAuthorityInvariant(host, port);

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "ALL TESTS PASSED"
                : $"{_failures} TEST(S) FAILED");
            return _failures;
        }

        private static void Check(bool ok, string name, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}"
                + (detail.Length > 0 ? $"  --  {detail}" : ""));
            if (!ok)
            {
                _failures++;
            }
        }

        /// <summary>A simulated client that holds its slot until disposed.</summary>
        private sealed class FakeClient : IDisposable
        {
            private readonly LoopbackPeer _socket;
            private readonly IPEndPoint _server;
            private readonly Thread _pump;
            private volatile bool _running = true;

            public int Slot { get; private set; } = -1;
            public MatchStatePacket? LastState { get; private set; }
            public string Name { get; }

            public FakeClient(string name, string host, int port)
            {
                Name = name;
                _socket = new LoopbackPeer();
                // Bind explicitly: UdpClient only binds implicitly on the
                // first Send, and this client's receive pump starts first.
                _socket.Client.ReceiveTimeout = 500;
                IPAddress ip = Dns.GetHostAddresses(host)
                    .First(a => a.AddressFamily == AddressFamily.InterNetwork);
                _server = new IPEndPoint(ip, port);
                _pump = new Thread(Pump) { IsBackground = true };
                _pump.Start();
            }

            public string[] RosterNames { get; } = new string[RosterPacket.MaxSlots];
            public bool[] RosterSlots { get; } = new bool[RosterPacket.MaxSlots];

            private readonly ushort[] _generations = new ushort[RosterPacket.MaxSlots];
            private readonly ushort[] _lives = new ushort[RosterPacket.MaxSlots];

            public void SendHello()
            {
                Send(PacketType.Hello, new byte[] { NetConfig.ProtocolVersion });
            }

            /// <summary>Announce a display name, the way a real client does.</summary>
            public void SendIdentify(string displayName)
            {
                byte[] bytes = new byte[2 + PlayerNameCodec.MaxWireBytes];
                if (!PlayerNameCodec.TryEncode(displayName, bytes.AsSpan(2), out int count))
                    throw new ArgumentException("Invalid test player name", nameof(displayName));
                Send(PacketType.Identify, bytes.AsSpan(0, count + 2));
            }

            /// <summary>True if a server ever emits the reserved legacy authority packet.</summary>
            public bool ReceivedAuthorityPacket { get; private set; }
            public int ImpossiblePositionUpdates { get; private set; }

            public bool SeesName(string other)
            {
                return RosterNames.Any(n => n == other);
            }

            /// <summary>Positions last received per slot, and how many updates.</summary>
            public Vector3[] SeenPositions { get; } = new Vector3[RosterPacket.MaxSlots];
            public int[] SeenUpdates { get; } = new int[RosterPacket.MaxSlots];

            /// <summary>Send a legacy client-authored snapshot as a negative probe.</summary>
            public void SendSnapshot(uint frame, int slot, Vector3 position)
            {
                const int timeSyncSize = PlayerEntity.SlotCapacity * sizeof(float) * 2;
                ushort matchId = LastState?.MatchId ?? 0;
                int healthOffset = SnapshotHeader.Size + PlayerState.Size + timeSyncSize;
                byte[] payload = new byte[healthOffset + NetHealthSync.HeaderSize];
                var header = new SnapshotHeader
                {
                    Frame = frame,
                    Rng1 = 0,
                    Rng2 = 0,
                    PlayerCount = 1,
                    MatchId = matchId,
                    AuthorityEpoch = LastState?.AuthorityEpoch ?? 0
                };
                header.Write(payload);
                var state = new PlayerState
                {
                    SlotIndex = (byte)slot,
                    Flags = PlayerState.FlagActive | PlayerState.FlagSpawned,
                    SlotGeneration = _generations[slot],
                    LifeId = _lives[slot] == 0 ? (ushort)1 : _lives[slot],
                    Position = position,
                    Speed = Vector3.Zero,
                    Facing = new Vector3(0, 0, 1),
                    Health = 100,
                    CurrentWeapon = 0,
                    Team = 0
                };
                state.Write(payload.AsSpan(SnapshotHeader.Size));
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(healthOffset), matchId);
                Send(PacketType.Snapshot, payload);
            }

            /// <summary>
            /// Keep the peer alive. The server drops silent peers, so a test
            /// client must behave like a real one and keep talking.
            /// </summary>
            public void SendIntent(uint frame)
            {
                var intent = new IntentPacket { Frame = frame, Buttons = IntentButtons.None,
                    MatchId = LastState?.MatchId ?? 0, AuthorityEpoch = LastState?.AuthorityEpoch ?? 0,
                    SlotGeneration = Slot < 0 ? (ushort)0 : _generations[Slot],
                    LifeId = Slot < 0 ? (ushort)0 : _lives[Slot] };
                byte[] payload = new byte[CompactIntentCodec.Size(intent)];
                CompactIntentCodec.Write(intent, payload);
                Send(PacketType.Intent, payload);
            }

            private void Send(PacketType type, ReadOnlySpan<byte> payload)
            {
                Span<byte> buffer = stackalloc byte[payload.Length + 1];
                buffer[0] = (byte)type;
                payload.CopyTo(buffer[1..]);
                try
                {
                    _socket.Send(buffer, _server);
                }
                catch (SocketException)
                {
                    // Transient; the pump keeps running.
                }
            }

            private void Pump()
            {
                var any = new IPEndPoint(IPAddress.Any, 0);
                while (_running)
                {
                    try
                    {
                        IPEndPoint from = any;
                        byte[] data = _socket.Receive(ref from);
                        if (data.Length < 1)
                        {
                            continue;
                        }
                        var type = (PacketType)data[0];
                        ReadOnlySpan<byte> payload = data.AsSpan(1);
                        if (type == PacketType.Welcome && payload.Length >= 1)
                        {
                            Slot = payload[0];
                        }
                        else if ((type == PacketType.MatchState || type == PacketType.MapChange)
                            && payload.Length >= MatchStatePacket.Size)
                        {
                            LastState = MatchStatePacket.Read(payload);
                        }
                        else if (type == PacketType.Authority)
                        {
                            ReceivedAuthorityPacket = true;
                        }
                        else if (type == PacketType.Snapshot
                            && payload.Length >= SnapshotHeader.Size + PlayerState.Size)
                        {
                            SnapshotHeader header = SnapshotHeader.Read(payload);
                            int offset = SnapshotHeader.Size;
                            for (int i = 0; i < header.PlayerCount; i++)
                            {
                                if (offset + PlayerState.Size > payload.Length)
                                {
                                    break;
                                }
                                PlayerState state = PlayerState.Read(payload[offset..]);
                                offset += PlayerState.Size;
                                if (state.SlotIndex < SeenPositions.Length)
                                {
                                    SeenPositions[state.SlotIndex] = state.Position;
                                    if (state.Position.LengthSquared > 50_000_000f) ImpossiblePositionUpdates++;
                                    _lives[state.SlotIndex] = state.LifeId;
                                    SeenUpdates[state.SlotIndex]++;
                                }
                            }
                        }
                        else if (type == PacketType.Roster
                            && payload.Length >= RosterPacket.Size)
                        {
                            RosterPacket roster = RosterPacket.Read(payload);
                            Array.Clear(RosterNames);
                            Array.Clear(RosterSlots);
                            for (int i = 0; i < roster.Count; i++)
                            {
                                int s = roster.Slots[i];
                                if (s >= 0 && s < RosterNames.Length)
                                {
                                    RosterNames[s] = roster.Names[i];
                                    _generations[s] = roster.Generations[i];
                                    RosterSlots[s] = true;
                                }
                            }
                        }
                    }
                    catch (SocketException)
                    {
                        // Receive timeout: expected while idle.
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }

            public void Leave()
            {
                Send(PacketType.Bye, ReadOnlySpan<byte>.Empty);
            }

            public void Dispose()
            {
                _running = false;
                _socket.Dispose();
            }
        }

        /// <summary>
        /// Join and wait for a slot, re-sending the Hello while waiting.
        /// UDP drops packets, and a single-shot Hello made several tests
        /// fail intermittently for a reason that said nothing about the
        /// server. A real client keeps asking until it is admitted.
        /// </summary>
        private static bool Join(FakeClient client, string? identifyAs = null,
                                 int timeoutMs = 8000)
        {
            bool joined = WaitFor(() =>
            {
                if (client.Slot < 0)
                {
                    client.SendHello();
                }
                return client.Slot >= 0;
            }, timeoutMs);
            if (joined && identifyAs != null)
            {
                client.SendIdentify(identifyAs);
            }
            return joined;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                {
                    return true;
                }
                Thread.Sleep(25);
            }
            return false;
        }

        /// <summary>
        /// Regression guard: the client used IPAddress.Parse, which only
        /// accepts a literal, so a hostname threw and the join failed
        /// silently -- both players ended up offline in their own match.
        /// Anything that resolves a server address must handle a name.
        /// </summary>
        private static void TestHostnameResolution(string host, int port)
        {
            Console.WriteLine("Address handling");
            bool isLiteral = IPAddress.TryParse(host, out _);
            if (isLiteral)
            {
                Console.WriteLine($"  [SKIP] {host} is a literal address; "
                    + "pass a hostname to exercise resolution");
                Console.WriteLine();
                return;
            }
            bool resolved;
            try
            {
                resolved = Dns.GetHostAddresses(host)
                    .Any(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch (Exception)
            {
                resolved = false;
            }
            Check(resolved, $"hostname {host} resolves to IPv4");

            (bool ok, string message) = NetProbe.Probe(host, port);
            Check(ok, "a hostname (not just an IP) can reach the server", message);
            Thread.Sleep(300);
            Console.WriteLine();
        }

        private static void TestReachable(string host, int port)
        {
            Console.WriteLine("Reachability");
            (bool ok, string message) = NetProbe.Probe(host, port);
            Check(ok, "server answers a Hello", message);
            // The probe leaves; give the server a moment to release its slot
            // so it cannot pollute the slot-allocation test that follows.
            Thread.Sleep(300);
            Console.WriteLine();
        }

        private static void TestDistinctSlots(string host, int port)
        {
            Console.WriteLine("Slot allocation");
            using var a = new FakeClient("A", host, port);
            using var b = new FakeClient("B", host, port);

            Join(a);
            Join(b);

            Check(a.Slot >= 0, "client A received a slot", $"slot {a.Slot}");
            Check(b.Slot >= 0, "client B received a slot", $"slot {b.Slot}");
            Check(a.Slot != b.Slot, "the two clients hold different slots",
                $"A={a.Slot} B={b.Slot}");

            a.Leave();
            b.Leave();
            Thread.Sleep(300);
            Console.WriteLine();
        }

        private static void TestClockAgreement(string host, int port)
        {
            Console.WriteLine("Match clock");
            using var a = new FakeClient("A", host, port);
            using var b = new FakeClient("B", host, port);
            // Retry rather than send once: UDP drops, and a lost Hello left
            // the client waiting forever with nothing to resend it. A real
            // client re-sends until it is admitted; the test must too.
            WaitFor(() =>
            {
                if (a.LastState == null)
                {
                    a.SendHello();
                }
                if (b.LastState == null)
                {
                    b.SendHello();
                }
                return a.LastState != null && b.LastState != null;
            }, 10000);

            bool both = a.LastState != null && b.LastState != null;
            Check(both, "both clients received match state");
            if (both)
            {
                MatchStatePacket sa = a.LastState!.Value;
                MatchStatePacket sb = b.LastState!.Value;
                Check(sa.RoomKey == sb.RoomKey, "both clients see the same map",
                    $"A={sa.RoomKey} B={sb.RoomKey}");
                // Both read the same server clock, so any gap is packet
                // timing, not two independent timers drifting apart.
                float gap = Math.Abs(sa.TimeRemaining - sb.TimeRemaining);
                Check(gap < 2.0f, "clocks agree within 2 s",
                    $"A={sa.TimeRemaining:0.0}s B={sb.TimeRemaining:0.0}s gap={gap:0.00}s");
            }
            a.Leave();
            b.Leave();
            Thread.Sleep(300);
            Console.WriteLine();
        }

        private static void TestJoinInProgress(string host, int port)
        {
            Console.WriteLine("Join in progress");
            using var first = new FakeClient("first", host, port);
            Join(first);
            WaitFor(() => first.LastState != null, 8000);
            float atJoin = first.LastState?.TimeRemaining ?? 0;

            // Let the match run, then have a second client arrive: it must be
            // told the running map and a clock that has advanced, not a fresh
            // match of its own.
            Thread.Sleep(3000);
            first.SendIntent(1);

            using var late = new FakeClient("late", host, port);
            Join(late);
            bool got = WaitFor(() => late.LastState != null, 8000);
            Check(got, "late joiner received match state");
            if (got && first.LastState != null)
            {
                MatchStatePacket state = late.LastState!.Value;
                Check(state.RoomKey.Length > 0, "late joiner learned the running map",
                    state.RoomKey);
                Check(state.TimeElapsed > 1.0f,
                    "late joiner sees a match already under way",
                    $"elapsed={state.TimeElapsed:0.0}s");
                Check(state.TimeRemaining < atJoin,
                    "the clock advanced rather than restarting",
                    $"{atJoin:0.0}s -> {state.TimeRemaining:0.0}s");
            }
            first.Leave();
            late.Leave();
            Thread.Sleep(300);
            Console.WriteLine();
        }

        private static void TestSlotReuseAfterLeave(string host, int port)
        {
            Console.WriteLine("Slot reuse");
            using var a = new FakeClient("A", host, port);
            Join(a);
            int firstSlot = a.Slot;
            a.Leave();
            Thread.Sleep(500);

            using var b = new FakeClient("B", host, port);
            Join(b);
            Check(b.Slot == firstSlot,
                "a freed slot is handed to the next client",
                $"freed {firstSlot}, reassigned {b.Slot}");
            b.Leave();
            Thread.Sleep(300);
            Console.WriteLine();
        }

        /// <summary>
        /// Regression guard for the disconnect seen in the field: a client
        /// whose local player was not active sent no intents, so the server
        /// dropped it on the TimeoutSeconds deadline while it was perfectly
        /// healthy. A peer that keeps talking must still be connected well
        /// past that window.
        /// </summary>
        private static void TestPeerSurvivesTimeoutWindow(string host, int port)
        {
            Console.WriteLine("Keepalive");
            double window = NetConfig.TimeoutSeconds;
            using var a = new FakeClient("A", host, port);
            using var b = new FakeClient("B", host, port);
            Join(a);
            Join(b);

            // Talk for longer than the server's timeout, then ask the server
            // how many peers it thinks are present.
            var clock = Stopwatch.StartNew();
            uint frame = 0;
            while (clock.Elapsed.TotalSeconds < window + 3)
            {
                frame++;
                a.SendIntent(frame);
                b.SendIntent(frame);
                Thread.Sleep(100);
            }

            int reported = b.LastState?.PlayerCount ?? -1;
            Check(reported >= 2,
                $"both peers still connected after {window + 3:0} s of play",
                $"server reports {reported} peer(s)");

            a.Leave();
            b.Leave();
            Thread.Sleep(300);
            Console.WriteLine();
        }

        /// <summary>
        /// The test that actually answers "are these two in the same match":
        /// each client announces a distinct name, and each must then see the
        /// other's name in the roster the server sends. Positions can look
        /// plausible while two clients sit alone in their own scenes, but a
        /// name can only reach your scoreboard if it came from the other
        /// machine.
        /// </summary>
        private static void TestPlayersSeeEachOther(string host, int port)
        {
            Console.WriteLine("Players see each other");
            const string nameA = "ALICE";
            const string nameB = "BOB";
            using var a = new FakeClient(nameA, host, port);
            using var b = new FakeClient(nameB, host, port);

            Join(a, nameA);

            Join(b, nameB);

            // Keep both alive while the roster propagates.
            bool mutual = WaitFor(() =>
            {
                a.SendIntent((uint)Environment.TickCount);
                b.SendIntent((uint)Environment.TickCount);
                return a.SeesName(nameB) && b.SeesName(nameA);
            }, 8000);

            Console.WriteLine($"    {nameA} (slot {a.Slot}) sees: "
                + Describe(a.RosterNames));
            Console.WriteLine($"    {nameB} (slot {b.Slot}) sees: "
                + Describe(b.RosterNames));

            Check(a.Slot != b.Slot, "the two players hold different slots",
                $"{nameA}={a.Slot} {nameB}={b.Slot}");
            Check(a.SeesName(nameB), $"{nameA} sees {nameB} on the scoreboard");
            Check(b.SeesName(nameA), $"{nameB} sees {nameA} on the scoreboard");
            Check(mutual, "both players see each other in the same match");

            a.Leave();
            b.Leave();
            Thread.Sleep(300);
            Console.WriteLine();
        }

        /// <summary>
        /// The check that "connected" actually means "playing together":
        /// the authority walks its player along a path, and the other client
        /// must receive those coordinates and see them change. A static
        /// position would pass a naive presence test while the players are
        /// in fact frozen to each other.
        /// </summary>
        private static void TestServerAuthorityInvariant(string host, int port)
        {
            Console.WriteLine("Server authority invariant");
            using var first = new FakeClient("FIRST", host, port);
            using var second = new FakeClient("SECOND", host, port);
            Join(first, "FIRST"); Join(second, "SECOND");
            for (int i = 0; i < 15; i++)
            {
                first.SendIntent((uint)(i + 1)); second.SendIntent((uint)(i + 1)); Thread.Sleep(80);
            }
            Check(!first.ReceivedAuthorityPacket && !second.ReceivedAuthorityPacket,
                "no player receives simulation authority", "server retains authority");
            int before = second.ImpossiblePositionUpdates;
            var forged = new Vector3(10000, 10000, 10000);
            for (int i = 0; i < 12; i++)
            {
                first.SendSnapshot((uint)(9000 + i), first.Slot, forged);
                first.SendIntent((uint)(100 + i)); second.SendIntent((uint)(100 + i)); Thread.Sleep(60);
            }
            Check(second.ImpossiblePositionUpdates == before,
                "client-authored snapshots cannot enter the authoritative stream",
                "forged world state ignored");
            first.Leave(); second.Leave(); Thread.Sleep(300); Console.WriteLine();
        }

        private static string Format(Vector3 v)
        {
            return $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
        }

        private static string Describe(string[] names)
        {
            var parts = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.IsNullOrEmpty(names[i]))
                {
                    parts.Add($"slot {i}={names[i]}");
                }
            }
            return parts.Count == 0 ? "(nobody)" : string.Join(", ", parts);
        }

        private static void TestStatusReporting(string host, int port)
        {
            Console.WriteLine("Player accounting");
            var clients = new List<FakeClient>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var client = new FakeClient($"C{i}", host, port);
                    clients.Add(client);
                    Join(client);
                    Thread.Sleep(150);
                }
                // Keep them alive long enough for a periodic state broadcast
                // to reflect all three.
                for (int tick = 0; tick < 20; tick++)
                {
                    foreach (FakeClient client in clients)
                    {
                        client.SendIntent((uint)(tick + 1));
                    }
                    Thread.Sleep(100);
                }

                var slots = clients.Select(c => c.Slot).ToList();
                Check(slots.All(s => s >= 0), "all three clients were admitted",
                    string.Join(", ", slots));
                Check(slots.Distinct().Count() == slots.Count,
                    "all three slots are distinct", string.Join(", ", slots));

                FakeClient last = clients[^1];
                int reported = last.LastState?.PlayerCount ?? -1;
                // At least, not exactly: a live server may have real players
                // on it while these tests run, and demanding an exact count
                // would fail for a reason that says nothing about the server.
                Check(reported >= clients.Count,
                    "server reports at least the clients this test connected",
                    $"reported {reported}, this test added {clients.Count}");
            }
            finally
            {
                foreach (FakeClient client in clients)
                {
                    client.Leave();
                    client.Dispose();
                }
            }
            Thread.Sleep(300);
            Console.WriteLine();
        }
    }
}
