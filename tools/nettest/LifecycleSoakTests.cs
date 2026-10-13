using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// This is a controlled ownership/save soak, not a simulated gameplay or native
// transition campaign. Player/hunter values need no model/room asset load.
internal static class LifecycleSoakTests
{
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Trace,
        Hunter.Sylux, Hunter.Noxus, Hunter.Spire, Hunter.Weavel };
    private sealed record EndedOwner(PostMatchCommitContext Commit, WeakReference<Scene> World,
        WeakReference<Scene>? Replica, string SaveHash, bool Retried);

    internal static int Run()
    {
        Headless.Enter();
        string previousDirectory = Directory.GetCurrentDirectory();
        string root = Path.Combine(Path.GetTempPath(), "prime-life-soak-" + Guid.NewGuid().ToString("N"));
        var previousState = GameState.Current;
        var previousPlayers = PlayerEntity.LegacyRegistry;
        var previousRandom = Rng.Current;
        byte previousSlot = Menu.SaveSlot;
        SaveWhen previousRequest = Menu.NeededSave;
        var globals = AiGlobals();
        var priorAi = globals.Select(g => (g.Player, g.Field4, g.NodeDataIndex, g.NodeData)).ToArray();
        bool enteredTemporaryDirectory = false;
        try
        {
            Directory.CreateDirectory(root); Directory.SetCurrentDirectory(root);
            enteredTemporaryDirectory = true;
            foreach (int count in new[] { 10, 100, 500 })
            {
                string group = Path.Combine(root, count.ToString());
                Directory.CreateDirectory(group); Directory.SetCurrentDirectory(group);
                WeakReference<Scene>[] simultaneous = EndSimultaneousWorlds();
                using var observation = SceneLifetimeDiagnostics.Begin(maximumRecords: 32);
                var owners = new List<EndedOwner>(count);
                var timer = Stopwatch.StartNew();
                int[] gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                long allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
                for (int i = 0; i < count; i++)
                    owners.Add(EndOne(i, owners.LastOrDefault()?.Commit));
                timer.Stop();
                int[] transitionCollections = Enumerable.Range(0, 3)
                    .Select(g => GC.CollectionCount(g) - gcBefore[g]).ToArray();
                long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocationBefore;
                // No diagnostic collection inside the transition loop. Keep all
                // released contexts and bounded snapshots alive across the only
                // liveness check so the observer/lease cannot mask retention.
                Collect();
                int live = owners.Count(o => IsAlive(o.World));
                int liveReplicas = owners.Count(o => o.Replica != null && IsAlive(o.Replica));
                int liveSimultaneous = simultaneous.Count(IsAlive);
                Require(live == 0 && liveReplicas == 0 && liveSimultaneous == 0,
                    $"{count}: live worlds={live} replicas={liveReplicas} simultaneous={liveSimultaneous}");
                Require(owners.All(o => o.Commit.State == PostMatchCommitState.Released && !o.Commit.HasPayload),
                    "every retained commit is released and contains no save bytes");
                Require(GameState.Current.Owner == null && PlayerEntity.LegacyRegistry.PlayersCreated == 0
                    && globals.All(g => g.Player == null && g.NodeData == null),
                    "bootstrap facades and bot destination table contain no ended owner");
                Require(observation.Snapshots.Count == 32 && observation.DroppedRecords > 0,
                    "value observations remain bounded while the soak continues");
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var owner in owners) digest.AppendData(Convert.FromHexString(owner.SaveHash));
                Console.WriteLine($"SOAK PASS transitions={count} hunters=7 botSlots=7 released={owners.Count}"
                    + $" retries={owners.Count(o => o.Retried)} privateReplicas={owners.Count(o => o.Replica != null)}"
                    + $" liveWorlds={live} liveReplicas={liveReplicas} liveSimultaneous={liveSimultaneous} records={observation.Snapshots.Count}"
                    + $" droppedRecords={observation.DroppedRecords} transitionGc=[{string.Join(',', transitionCollections)}]"
                    + $" allocatedBytes={allocated} managedBytesAfterDiagnosticGc={GC.GetTotalMemory(false)}"
                    + $" elapsedMs={timer.ElapsedMilliseconds} saveDigest={Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant()}");
                GC.KeepAlive(owners); GC.KeepAlive(observation);
            }
            Console.WriteLine("SOAK summary transitions=610 realSaveByteComparisons=610 scope=device-free ownership/save contracts; gameplay/render/audio/worker-asset/native flows NOT_RUN");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            // The fixture owns the temporary filesystem. Restore any obstructed
            // writer before retrying; never add a production abandon-save path.
            if (enteredTemporaryDirectory)
            {
                if (File.Exists("Savedata")) File.Delete("Savedata");
                MatchStart.RetryPendingCommit();
            }
            for (int i = 0; i < priorAi.Length; i++)
            {
                globals[i].Player = priorAi[i].Player; globals[i].Field4 = priorAi[i].Field4;
                globals[i].NodeDataIndex = priorAi[i].NodeDataIndex; globals[i].NodeData = priorAi[i].NodeData;
            }
            GameState.Current = previousState; PlayerEntity.LegacyRegistry = previousPlayers; Rng.Current = previousRandom;
            Menu.SaveSlot = previousSlot; Menu.NeededSave = previousRequest;
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EndedOwner EndOne(int iteration, PostMatchCommitContext? previous)
    {
        var scene = NewScene();
        Require(previous == null || !ForegroundSceneBinding.TryRelease(previous),
            "previous released generation cannot clear the next live world");
        scene.GameState.Mode = GameMode.Battle;
        scene.Players.MaxPlayers = 8;
        scene.GameState.Nicknames[0] = "soak-" + iteration;
        scene.Random.SetRng1((uint)(iteration + 123)); scene.Random.SetRng2((uint)(iteration + 456));
        var globals = AiGlobals();
        for (int slot = 1; slot <= Hunters.Length; slot++)
        {
            var player = scene.Players.Items[slot];
            player.IsBot = true; player.Hunter = Hunters[slot - 1];
            globals[slot - 1].Player = player; globals[slot - 1].NodeData = new NodeData3[1];
            globals[slot - 1].Field4 = iteration + slot; globals[slot - 1].NodeDataIndex = slot;
        }
        Require(scene.Players.Items.Skip(1).Take(7).Select(p => p.Hunter).SequenceEqual(Hunters)
            && PlayerEntity.PlayerAiData.HasSceneReferences(scene), "seven hunter bot owners enter the real destination table");

        WeakReference<Scene>? replica = iteration % 10 == 0 ? EndPrivateReplica(scene) : null;
        var save = SaveFixture(iteration);
        typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave))!.SetValue(scene.GameState, save);
        byte[] expected = PostMatchSaveTests.LegacySave(SaveFixture(iteration));
        Menu.SaveSlot = (byte)(iteration % 3 + 1); Menu.NeededSave = SaveWhen.Always;
        var commit = MatchStart.PrepareBeforeTeardown(scene.GameState)!;
        string destination = Path.Combine("Savedata", $"save{commit.Slot:000}.json");
        scene.DoCleanup(); scene.UnloadGl();
        bool retry = iteration % 17 == 0;
        if (retry)
        {
            if (Directory.Exists("Savedata")) Directory.Delete("Savedata", recursive: true);
            File.WriteAllText("Savedata", "real write failure");
            try { MatchStart.AfterMatch(commit); } catch (IOException) { }
            Require(commit.State == PostMatchCommitState.Failed && commit.HasPayload
                && !ForegroundSceneBinding.TryRelease(commit) && ReferenceEquals(GameState.Current, scene.GameState),
                "failed writer keeps payload and facade owner until retry succeeds");
            save.Health = 1; save.Stats.EnemyKills = 1;
            File.Delete("Savedata");
            Menu.NeededSave = SaveWhen.Always; // replacement request, same value
            MatchStart.RetryPendingCommit();
            Require(Menu.NeededSave == SaveWhen.Always, "old retry cannot clear a newer same-value request");
        }
        else MatchStart.AfterMatch(commit);
        Require(File.ReadAllBytes(destination).SequenceEqual(expected), "real committed bytes equal independent baseline including Stats");
        int writes = commit.WriteAttempts;
        commit.Commit(); commit.Release();
        Require(commit.WriteAttempts == writes && writes == (retry ? 2 : 1), "terminal commit never duplicates successful writes");
        Require(ReferenceEquals(GameState.Current, scene.GameState) && PlayerEntity.PlayerAiData.HasSceneReferences(scene),
            "save completion keeps real owner through remaining coordinator consumers");
        Require(ForegroundSceneBinding.TryRelease(commit) && !PlayerEntity.PlayerAiData.HasSceneReferences(scene)
            && GameState.Current.Owner == null && GameState.Nicknames[0] == "soak-" + iteration
            && Rng.Rng1 == (uint)(iteration + 123) && Rng.Rng2 == (uint)(iteration + 456),
            "eligible handoff preserves values and drops only ended scene/bot roots");
        Menu.NeededSave = SaveWhen.Never;
        return new(commit, new(scene), replica, Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), retry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Scene>[] EndSimultaneousWorlds()
    {
        var old = NewScene(); var current = NewScene();
        var host = new PassiveReplaySessionHost();
        using var session = new ReplayPlaybackSession(host);
        var replica = NewScene(new ReplaySceneServices(session, host.State));
        var oldRoom = new RoomEntity(old);
        old.GameState.TransitionState = TransitionState.Process;
        Require(oldRoom.TryCompleteTransition(old.LifetimeGeneration, default)
            && old.GameState.TransitionState == TransitionState.End
            && current.GameState.TransitionState == TransitionState.None
            && !ForegroundSceneBinding.TryResetLegacyAiGlobals(old),
            "two still-live owners publish only owned state and old owner cannot reset current AI globals");
        // These are genuine production query leases, held simultaneously across
        // three worlds. Empty-room coverage tests disposal/thread scratch scope;
        // the existing collision-query-check covers nonempty candidate lifetime.
        using (var outer = CollisionDetection.GetCandidatesForPoints(Vector3.Zero, Vector3.One, 0, false, old))
        {
            using (var inner = CollisionDetection.GetCandidatesForPoints(Vector3.Zero, Vector3.One, 0, false, current))
            using (var detached = CollisionDetection.GetCandidatesForPoints(Vector3.Zero, Vector3.One, 0, false, replica))
                Require(!ReferenceEquals(outer, inner) && !ReferenceEquals(outer, detached)
                    && !ReferenceEquals(inner, detached) && outer.Count == 0 && inner.Count == 0 && detached.Count == 0,
                    "nested production query leases have independent storage across live and replica worlds");
            Require(outer.Count == 0, "nested lease disposal preserves the outer production lease");
        }
        var oldCommit = PostMatchCommitContext.Capture(old.GameState, 0, SaveWhen.Never, 0);
        old.DoCleanup(); old.UnloadGl(); oldCommit.Commit(); oldCommit.Release();
        Require(!ForegroundSceneBinding.TryRelease(oldCommit) && !oldRoom.TryCompleteTransition(old.LifetimeGeneration, default)
            && GameState.Current == current.GameState, "ended old owner cannot publish or release a replacement binding");
        replica.DoCleanup(); replica.UnloadGl();
        var currentCommit = PostMatchCommitContext.Capture(current.GameState, 0, SaveWhen.Never, 0);
        current.DoCleanup(); current.UnloadGl(); currentCommit.Commit(); currentCommit.Release();
        Require(ForegroundSceneBinding.TryRelease(currentCommit), "current simultaneous owner releases after genuine cleanup");
        return new[] { new WeakReference<Scene>(old), new(current), new(replica) };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Scene> EndPrivateReplica(Scene foreground)
    {
        var host = new PassiveReplaySessionHost();
        using var session = new ReplayPlaybackSession(host);
        var replica = NewScene(new ReplaySceneServices(session, host.State));
        replica.DoCleanup(); replica.UnloadGl();
        Require(GameState.Current == foreground.GameState && PlayerEntity.LegacyRegistry == foreground.Players
            && Rng.Current == foreground.Random, "private replica teardown leaves foreground identities intact");
        return new(replica);
    }

    private static StorySave SaveFixture(int iteration)
    {
        var save = PostMatchSaveTests.Fixture();
        save.Health = 200 + iteration; save.Stats.EnemyKills = 0xF1234567u + (uint)iteration;
        save.Stats.Deaths = (uint)iteration; save.RoomState[0][0] = (byte)iteration;
        return save;
    }
    private static PlayerEntity.PlayerAiData.AiGlobals[] AiGlobals()
        => (PlayerEntity.PlayerAiData.AiGlobals[])typeof(PlayerEntity.PlayerAiData)
            .GetField("_globalObjs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private static Scene NewScene(ISceneServices? services = null) => new(new Vector2i(32, 32),
        SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, services, initializeRuntime: false);
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("SOAK FAIL " + message); }
    [MethodImpl(MethodImplOptions.NoInlining)] private static bool IsAlive(WeakReference<Scene> scene) => scene.TryGetTarget(out _);
    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
