using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ForegroundBindingTests
{
    internal static int Run()
    {
        Headless.Enter();
        var previousState = GameState.Current;
        var previousPlayers = PlayerEntity.LegacyRegistry;
        var previousRandom = Rng.Current;
        byte previousSlot = Menu.SaveSlot;
        SaveWhen previousRequest = Menu.NeededSave;
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("BINDING FAIL " + label);
            checks++; Console.WriteLine("BINDING PASS " + label);
        }
        try
        {
            Check(!RuntimeHelpers.IsReferenceOrContainsReferences<ForegroundSceneLease>(),
                "foreground lease is a value token with no prior-world restore chain");
            using var observation = SceneLifetimeDiagnostics.Begin();
            (PostMatchCommitContext oldCommit, WeakReference<Scene> oldWorld, bool heldAfterSave) = EndEligibleWorld();
            Check(heldAfterSave, "save completion retains facades until the exit coordinator finishes its remaining consumers");
            Check(oldCommit.State == PostMatchCommitState.Released && GameState.Current.Owner == null
                && PlayerEntity.LegacyRegistry.PlayersCreated == 0 && GameState.Kills[0] == 0,
                "successful handoff and completed visual cleanup release only the eligible foreground owner");
            Check(GameState.Mode == GameMode.Battle && GameState.PointGoal == 13 && PlayerEntity.MaxPlayers == 8
                && GameState.Nicknames[0] == "retained value" && Rng.Rng1 == 123 && Rng.Rng2 == 456,
                "configuration, nickname values, slot preference and RNG seeds survive without actors/progress");
            Collect();
            Check(!IsAlive(oldWorld), "actual eligible bot scene is collectible while the released lease/context/snapshots remain alive");
            Check(observation.Snapshots.Last().Stage == SceneLifetimeStage.CompatibilityReleased
                && observation.Snapshots.Last().Facades == SceneFacadeRoots.None,
                "value diagnostic observes successful conditional facade release");

            var replacement = NewScene();
            Check(!ForegroundSceneBinding.TryRelease(oldCommit) && GameState.Current == replacement.GameState
                && PlayerEntity.LegacyRegistry == replacement.Players && Rng.Current == replacement.Random,
                "old completed owner cannot clear any replacement scene facade");
            var beforeCleanup = PostMatchCommitContext.Capture(replacement.GameState, 0, SaveWhen.Never, 0);
            beforeCleanup.Commit(); beforeCleanup.Release();
            Check(!ForegroundSceneBinding.TryRelease(beforeCleanup), "save completion alone cannot release a running scene");
            replacement.DoCleanup();
            Check(!ForegroundSceneBinding.TryRelease(beforeCleanup), "cleanup alone cannot release an owner awaiting visual teardown");
            replacement.UnloadGl();
            bool workerReleased = true;
            var otherThread = new Thread(() => workerReleased = ForegroundSceneBinding.TryRelease(beforeCleanup));
            otherThread.Start(); otherThread.Join();
            Check(!workerReleased && GameState.Current == replacement.GameState,
                "a foreign thread cannot release the owning foreground binding");
            Check(ForegroundSceneBinding.TryRelease(beforeCleanup), "owning thread can release after both required cleanup boundaries");

            var aiOwned = NewScene();
            var aiCommit = PostMatchCommitContext.Capture(aiOwned.GameState, 0, SaveWhen.Never, 0);
            var aiGlobals = (PlayerEntity.PlayerAiData.AiGlobals[])typeof(PlayerEntity.PlayerAiData)
                .GetField("_globalObjs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var savedAi = aiGlobals.Take(2).Select(g => (g.Player, g.Field4, g.NodeDataIndex, g.NodeData)).ToArray();
            var globalField = typeof(PlayerEntity.PlayerAiData).GetField("_globalField2", BindingFlags.NonPublic | BindingFlags.Static)!;
            int previousGlobalField = (int)globalField.GetValue(null)!;
            try
            {
                var newerAi = NewScene();
                Check(ForegroundSceneBinding.TryResetLegacyAiGlobals(newerAi),
                    "current foreground still performs the existing global AI adapter reset");
                aiGlobals[0].Player = aiOwned.Players.Items[0];
                aiGlobals[0].Field4 = 41; aiGlobals[0].NodeDataIndex = 17;
                aiGlobals[0].NodeData = new NodeData3[1];
                aiGlobals[1].Player = newerAi.Players.Items[0];
                aiGlobals[1].Field4 = 99; aiGlobals[1].NodeDataIndex = 23;
                var newerNodes = new NodeData3[1]; aiGlobals[1].NodeData = newerNodes;
                globalField.SetValue(null, 73);
                Check(!aiOwned.Exiting && !ForegroundSceneBinding.TryResetLegacyAiGlobals(aiOwned)
                    && ReferenceEquals(aiGlobals[1].Player, newerAi.Players.Items[0])
                    && aiGlobals[1].Field4 == 99 && (int)globalField.GetValue(null)! == 73,
                    "old still-live room adapter cannot clear newer foreground AI global sentinels");
                aiOwned.DoCleanup(); aiOwned.UnloadGl(); aiCommit.Commit(); aiCommit.Release();
                Check(!ForegroundSceneBinding.TryRelease(aiCommit) && aiGlobals[0].Player != null && aiGlobals[1].Player != null,
                    "old handoff cannot perform AI cleanup after a newer scene binds");
                Check(PlayerEntity.PlayerAiData.ReleaseScene(aiOwned) == 1
                    && aiGlobals[0].Player == null && aiGlobals[0].NodeData == null
                    && aiGlobals[0].Field4 == 0 && aiGlobals[0].NodeDataIndex == 0
                    && ReferenceEquals(aiGlobals[1].Player, newerAi.Players.Items[0])
                    && ReferenceEquals(aiGlobals[1].NodeData, newerNodes)
                    && aiGlobals[1].Field4 == 99 && aiGlobals[1].NodeDataIndex == 23
                    && (int)globalField.GetValue(null)! == 73,
                    "owned-entry AI release drops only old actor/node references and preserves newer/global state");
                var newerCommit = PostMatchCommitContext.Capture(newerAi.GameState, 0, SaveWhen.Never, 0);
                newerAi.DoCleanup(); newerAi.UnloadGl(); newerCommit.Commit(); newerCommit.Release();
                Check(ForegroundSceneBinding.TryRelease(newerCommit) && aiGlobals[1].Player == null
                    && !PlayerEntity.PlayerAiData.HasSceneReferences(newerAi),
                    "actual eligible handoff releases its ended bot entries without a global table reset");
            }
            finally
            {
                for (int i = 0; i < savedAi.Length; i++)
                {
                    aiGlobals[i].Player = savedAi[i].Player; aiGlobals[i].Field4 = savedAi[i].Field4;
                    aiGlobals[i].NodeDataIndex = savedAi[i].NodeDataIndex; aiGlobals[i].NodeData = savedAi[i].NodeData;
                }
                globalField.SetValue(null, previousGlobalField);
            }

            var foreground = NewScene();
            var host = new PassiveReplaySessionHost();
            using var session = new ReplayPlaybackSession(host);
            var replica = NewScene(new ReplaySceneServices(session, host.State));
            bool replicaBindRejected = false;
            try { ForegroundSceneBinding.Bind(replica); } catch (InvalidOperationException) { replicaBindRejected = true; }
            replica.DoCleanup(); replica.UnloadGl();
            Check(replicaBindRejected && replica.ForegroundLease.Generation == 0
                && GameState.Current == foreground.GameState && PlayerEntity.LegacyRegistry == foreground.Players
                && Rng.Current == foreground.Random, "private replica construction/cleanup cannot publish foreground facades");

            // Synthetic room owns the real production CTS/publication helper;
            // no SetUpRoom, assets, native context or simulation loop is used.
            var room = new RoomEntity(foreground);
            foreground.GameState.TransitionState = TransitionState.Process;
            Check(!room.TryCompleteTransition(foreground.LifetimeGeneration + 1, CancellationToken.None)
                && foreground.GameState.TransitionState == TransitionState.Process,
                "generation-mismatched completion cannot publish into this room owner");
            Check(room.TryCompleteTransition(foreground.LifetimeGeneration, CancellationToken.None)
                && foreground.GameState.TransitionState == TransitionState.End,
                "valid synchronous owner preserves the existing transition completion value");
            foreground.GameState.TransitionState = TransitionState.Process;
            using var started = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
            Task<bool> late = Task.Run(() => { started.Set(); finish.Wait(); return room.TryCompleteTransition(foreground.LifetimeGeneration, CancellationToken.None); });
            started.Wait(); room.CancelTransition(); finish.Set();
            Check(!late.GetAwaiter().GetResult() && foreground.GameState.TransitionState == TransitionState.Process,
                "cancelled owner atomically rejects completion after the former final-check/publication gap");
            var process = typeof(RoomEntity).GetMethod("ProcessTransition", BindingFlags.Instance | BindingFlags.NonPublic)!;
            process.Invoke(room, new object[] { CancellationToken.None });
            Check(foreground.GameState.TransitionState == TransitionState.Process,
                "real cancelled ProcessTransition returns before metadata/setup even with CancellationToken.None");

            var unsupported = PostMatchCommitContext.Capture(foreground.GameState, 0, SaveWhen.Never, 0);
            unsupported.Commit(); unsupported.Release();
            foreground.GameState.TransitionState = TransitionState.None;
            foreground.DoCleanup(); foreground.UnloadGl();
            Check(unsupported.LegacyTransitionTiming && !ForegroundSceneBinding.TryRelease(unsupported),
                "active-transition capture never gains release eligibility from a later state change");
            var exited = new RoomEntity(foreground);
            Check(!exited.TryCompleteTransition(foreground.LifetimeGeneration, CancellationToken.None),
                "exited scene cannot publish terminal room state even when no caller token was cancelled");
            Type shell = typeof(Scene).Assembly.GetType("MphRead.Mods.Launcher.Gui.Shell")!;
            var lobbyCalls = PostMatchSaveTests.Calls(shell, "EndNetworkMatchToLobby").ToList();
            var endCalls = PostMatchSaveTests.Calls(shell, "EndMatch").ToList();
            Check(lobbyCalls.Last() == "TryRelease" && lobbyCalls.IndexOf("ResetMatchState") < lobbyCalls.IndexOf("TryRelease")
                && endCalls.Last() == "TryRelease" && endCalls.IndexOf("ShowFrontScreen") < endCalls.IndexOf("TryRelease"),
                "compiled coordinators release only after all existing reset/UI-return consumers");
            Console.WriteLine($"BINDING summary checks={checks} scope=device-free foreground/terminal publication contracts; native transition flows NOT_RUN");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            GameState.Current = previousState; PlayerEntity.LegacyRegistry = previousPlayers; Rng.Current = previousRandom;
            Menu.SaveSlot = previousSlot; Menu.NeededSave = previousRequest;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (PostMatchCommitContext, WeakReference<Scene>, bool) EndEligibleWorld()
    {
        var scene = NewScene();
        scene.GameState.Mode = GameMode.Battle; scene.GameState.PointGoal = 13;
        scene.GameState.Nicknames[0] = "retained value"; scene.GameState.Kills[0] = 27;
        scene.Players.MaxPlayers = 8; scene.Random.SetRng1(123); scene.Random.SetRng2(456);
        // Keep the real bot-to-scene root in the production AI destination table
        // through cleanup. Only the eligible handoff may drop it before the
        // caller attempts weak collection; no test-only root clear is used.
        scene.Players.Items[0].IsBot = true;
        var globals = (PlayerEntity.PlayerAiData.AiGlobals[])typeof(PlayerEntity.PlayerAiData)
            .GetField("_globalObjs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var prior = (globals[0].Player, globals[0].Field4, globals[0].NodeDataIndex, globals[0].NodeData);
        globals[0].Player = scene.Players.Items[0];
        globals[0].NodeData = new NodeData3[1];
        Menu.SaveSlot = 0; Menu.NeededSave = SaveWhen.Never;
        try
        {
            var commit = MatchStart.PrepareBeforeTeardown(scene.GameState)!;
            scene.DoCleanup(); scene.UnloadGl();
            MatchStart.AfterMatch(commit);
            bool heldAfterSave = ReferenceEquals(GameState.Current, scene.GameState)
                && PlayerEntity.PlayerAiData.HasSceneReferences(scene);
            if (!ForegroundSceneBinding.TryRelease(commit) || PlayerEntity.PlayerAiData.HasSceneReferences(scene))
                throw new InvalidOperationException("Eligible bot owner did not release its real AI table root.");
            return (commit, new(scene), heldAfterSave);
        }
        finally
        {
            globals[0].Player = prior.Player; globals[0].Field4 = prior.Field4;
            globals[0].NodeDataIndex = prior.NodeDataIndex; globals[0].NodeData = prior.NodeData;
        }
    }
    private static Scene NewScene(ISceneServices? services = null) => new(new Vector2i(32, 32),
        SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, services, initializeRuntime: false);
    [MethodImpl(MethodImplOptions.NoInlining)] private static bool IsAlive(WeakReference<Scene> world) => world.TryGetTarget(out _);
    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
