using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
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

// Asset-dependent production world ownership check. Native GL/audio, UI routes,
// network reconnect and media encoding need their separate rendered campaigns.
internal static class AssetLifecycleTests
{
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Trace,
        Hunter.Sylux, Hunter.Noxus, Hunter.Spire, Hunter.Weavel };
    private static readonly string[] Rooms = { "MP3 PROVING GROUND", "MP9 CRYOCHASM" };
    private sealed record Receipt(WeakReference<Scene> World, PostMatchCommitContext Commit,
        long Generation, long SaveRequest, byte Slot, int RoomId, int Players,
        int Entities, int Steps, int SaveBytes, string SaveHash, bool Retried);
    private static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("ASSET LIFE FAIL " + label); }

    internal static int Run(int count, string? replayPath, string output)
    {
        if (count < 1 || count > 500) throw new ArgumentOutOfRangeException(nameof(count));
        if (File.Exists(output))
        {
            Console.Error.WriteLine("Asset lifecycle output must be fresh; preserve previous evidence.");
            return 1;
        }
        Headless.Enter();
        string previousDirectory = Directory.GetCurrentDirectory();
        string temporary = Path.Combine(Path.GetTempPath(), "prime-asset-life-" + Guid.NewGuid().ToString("N"));
        var previousState = GameState.Current;
        var previousPlayers = PlayerEntity.LegacyRegistry;
        var previousRandom = Rng.Current;
        byte previousSlot = Menu.SaveSlot;
        SaveWhen previousRequest = Menu.NeededSave;
        Directory.CreateDirectory(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var receipts = new List<Receipt>();
        WeakReference<Scene>[] simultaneous = Array.Empty<WeakReference<Scene>>();
        using var observation = SceneLifetimeDiagnostics.Begin(4096);
        try
        {
            Directory.SetCurrentDirectory(temporary);
            if (replayPath != null) simultaneous = EndSimultaneous(replayPath);
            for (int i = 0; i < count; i++)
            {
                receipts.Add(EndOne(i, receipts.LastOrDefault()?.Commit));
                if ((i + 1) % 10 == 0 || i + 1 == count)
                    Console.WriteLine($"ASSET LIFE loads={i + 1}/{count} steps={receipts.Sum(r => r.Steps)} realSaveFiles={receipts.Count}");
            }
            Read.ClearCache();
            // One post-eligible diagnostic observation, outside all construction
            // methods; retained receipts/snapshots contain only values/weak roots.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            int live = receipts.Count(r => Alive(r.World)), liveSimultaneous = simultaneous.Count(Alive);
            Require(live == 0 && liveSimultaneous == 0,
                $"eligible loaded scene retention: sequential={live}, simultaneous={liveSimultaneous}");
            Require(receipts.All(r => r.Commit.State == PostMatchCommitState.Released && !r.Commit.HasPayload),
                "all immutable save leases released");
            int loaded = observation.Snapshots.Count(s => s.Stage == SceneLifetimeStage.Loaded);
            Require(loaded >= count && receipts.All(r => r.Players == 8 && r.Entities > 8 && r.Steps == 60),
                "positive production load/player/entity/step denominators");
            WriteResult("PASS", null, loaded, live, liveSimultaneous);
            Console.WriteLine($"ASSET LIFE PASS loads={count} rooms={receipts.Select(r => r.RoomId).Distinct().Count()} hunters=7 steps={count * 60} saves={count} liveWorlds={live} simultaneousWorlds={simultaneous.Length}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            WriteResult("FAIL", error.GetType().Name + ": " + error.Message, observation.Snapshots.Count(s => s.Stage == SceneLifetimeStage.Loaded), null, null);
            return 1;
        }
        finally
        {
            if (File.Exists("Savedata")) File.Delete("Savedata");
            MatchStart.RetryPendingCommit();
            Read.ClearCache();
            GameState.Current = previousState; PlayerEntity.LegacyRegistry = previousPlayers; Rng.Current = previousRandom;
            Menu.SaveSlot = previousSlot; Menu.NeededSave = previousRequest;
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(temporary, recursive: true);
        }

        void WriteResult(string status, string? error, int loaded, int? live, int? liveSimultaneous)
        {
            string assembly = typeof(Scene).Assembly.Location;
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                schema = 1, status, error, scope = "asset-backed headless production world/save ownership",
                assemblyMvid = typeof(Scene).Module.ModuleVersionId,
                assemblySha256 = Hash(File.ReadAllBytes(assembly)), requestedLoads = count,
                completedLoads = receipts.Count, observedLoaded = loaded, liveWorlds = live, liveSimultaneous,
                simultaneousWorlds = simultaneous.Length, detachedReplica = replayPath == null ? "NOT_RUN" : status,
                nativeGlAudio = "NOT_RUN",
                lobbyReconnectUiMedia = "NOT_RUN", observationDropped = observation.DroppedRecords,
                loads = receipts.Select(r => new { r.Generation, r.SaveRequest, r.Slot, r.RoomId,
                    r.Players, r.Entities, r.Steps, r.SaveBytes, r.SaveHash, r.Retried }),
                snapshots = observation.Snapshots
            }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Receipt EndOne(int iteration, PostMatchCommitContext? previous)
    {
        Scene scene = Load(Rooms[iteration % Rooms.Length]);
        Require(previous == null || !ForegroundSceneBinding.TryRelease(previous),
            "previous released generation cannot clear the next loaded foreground");
        int entities = EntityCount(scene), players = scene.Players.PlayerCount, roomId = scene.RoomId;
        ulong frameBefore = scene.FrameCount;
        for (int frame = 0; frame < 60; frame++) scene.OnSimulationFrame();
        Require(scene.FrameCount == frameBefore + 60, "all sixty native steps actually advance the loaded world");
        Require(scene.Players.Items.Skip(1).Take(7).Select(p => p.Hunter).SequenceEqual(Hunters), "seven hunters survive native world initialization and steps");
        // Real StorySave from the asset-dependent constructor and world setup.
        // Frozen baseline writer is independent of the production handoff helper.
        var save = scene.GameState.StorySave;
        save.Stats.EnemyKills = 0xF1234567u + (uint)iteration;
        byte[] expected = PostMatchSaveTests.LegacySave(save);
        Menu.SaveSlot = (byte)(iteration % 3 + 1); Menu.NeededSave = SaveWhen.Always;
        var commit = MatchStart.PrepareBeforeTeardown(scene.GameState)
            ?? throw new InvalidOperationException("Loaded fixture unexpectedly entered a room transition.");
        Require(commit.HasPayload, "loaded world creates an immutable save payload");
        Require(commit.SceneGeneration == scene.LifetimeGeneration && commit.RequestVersion == Menu.SaveRequestVersion,
            "save root stamp matches the loaded owner and exact request");
        // Mutate aliases after capture on every arm; neither teardown nor retry
        // may serialize a different save through a shared mutable reference.
        save.Health = 1; save.Stats.EnemyKills = 1; save.RoomState[0][0] ^= 0xFF;
        scene.DoCleanup(); scene.UnloadGl();
        bool retry = iteration % 17 == 0;
        if (retry)
        {
            if (Directory.Exists("Savedata")) Directory.Delete("Savedata", recursive: true);
            File.WriteAllText("Savedata", "owned failure fixture");
            try { MatchStart.AfterMatch(commit); } catch (IOException) { }
            Require(commit.State == PostMatchCommitState.Failed && commit.HasPayload && !ForegroundSceneBinding.TryRelease(commit), "real failed writer retains payload and owner");
            File.Delete("Savedata"); MatchStart.RetryPendingCommit();
        }
        else MatchStart.AfterMatch(commit);
        Require(File.ReadAllBytes(Path.Combine("Savedata", $"save{commit.Slot:000}.json")).SequenceEqual(expected), "loaded world save bytes equal independent original serializer");
        Require(ForegroundSceneBinding.TryRelease(commit), "loaded world reaches eligible owner release");
        int writes = commit.WriteAttempts; commit.Commit(); commit.Release();
        Require(commit.WriteAttempts == writes && writes == (retry ? 2 : 1), "completed loaded world never writes twice");
        return new(new(scene), commit, scene.LifetimeGeneration, commit.RequestVersion, commit.Slot,
            roomId, players, entities, 60, expected.Length, Hash(expected), retry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Scene>[] EndSimultaneous(string replayPath)
    {
        Scene old = Load(Rooms[0]); Scene current = Load(Rooms[1]);
        using var replica = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
        Require(GameState.Current == current.GameState && PlayerEntity.LegacyRegistry == current.Players && Rng.Current == current.Random,
            "asset-backed replica construction preserves newer foreground binding");
        Require(replica.Step(), "detached asset-backed replica really advances");
        Require(GameState.Current == current.GameState && PlayerEntity.LegacyRegistry == current.Players && Rng.Current == current.Random,
            "actual detached replica step preserves current foreground aliases");
        string currentHash = ReplayStateHash.Compute(current);
        RoomEntity oldRoom = old.Room ?? throw new InvalidOperationException("Old loaded scene has no native room owner.");
        Require(!Task.Run(() => oldRoom.TryCompleteTransition(old.LifetimeGeneration + 1, CancellationToken.None)).GetAwaiter().GetResult(),
            "foreign generation worker completion cannot publish into old owner");
        using (var outer = Candidates(old))
        {
            int outerCount = outer.Count; var collision = outer[0].Collision;
            using (var inner = Candidates(current))
            using (var detached = Candidates(replica.Scene))
                Require(inner.Count > 0 && detached.Count > 0 && !ReferenceEquals(outer, inner) && !ReferenceEquals(outer, detached), "three loaded worlds hold genuine nonempty independent collision leases");
            Require(outer.Count == outerCount && ReferenceEquals(outer[0].Collision, collision), "nested disposal preserves outer loaded-world collision candidates");
        }
        var oldCommit = PostMatchCommitContext.Capture(old.GameState, 0, SaveWhen.Never, 0);
        old.DoCleanup(); old.UnloadGl(); oldCommit.Commit(); oldCommit.Release();
        Require(!oldRoom.TryCompleteTransition(old.LifetimeGeneration, CancellationToken.None)
            && !ForegroundSceneBinding.TryRelease(oldCommit) && GameState.Current == current.GameState,
            "disposed old loaded owner cannot complete or clear the current foreground");
        Require(ReplayStateHash.Compute(current) == currentHash,
            "old producer, nested real queries and old teardown cannot mutate the newer loaded gameplay world");
        var weakReplica = new WeakReference<Scene>(replica.Scene); replica.Dispose();
        Require(GameState.Current == current.GameState && PlayerEntity.LegacyRegistry == current.Players && Rng.Current == current.Random,
            "detached replica disposal preserves current foreground aliases");
        var currentCommit = PostMatchCommitContext.Capture(current.GameState, 0, SaveWhen.Never, 0);
        current.DoCleanup(); current.UnloadGl(); currentCommit.Commit(); currentCommit.Release();
        Require(ForegroundSceneBinding.TryRelease(currentCommit), "simultaneous current owner releases after actual cleanup");
        return new[] { new WeakReference<Scene>(old), new(current), weakReplica };
    }

    private static CollisionQueryWorkspace Candidates(Scene scene)
    {
        // Cover the native room grid, independent of its current spawn position.
        var lease = CollisionDetection.GetCandidatesForPoints(new(-1000), new(1000), 0, true, scene);
        if (lease.Count > 0) return lease;
        lease.Dispose(); throw new InvalidOperationException("Loaded scene yielded no native collision candidates.");
    }
    private static Scene Load(string room)
    {
        var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
        try
        {
            scene.Players.MaxPlayers = 8; scene.AddPlayer(Hunter.Samus);
            foreach (Hunter hunter in Hunters) scene.AddPlayer(hunter);
            scene.AddRoom(room, GameMode.Battle, playerCount: 8); scene.OnLoad();
            scene.GameState.PointGoal = 0; scene.GameState.MatchTime = -1;
            Require(scene.RoomId >= 0 && scene.Players.PlayerCount == 8 && EntityCount(scene) > 8, "native room, actors and entities initialized");
            return scene;
        }
        catch { scene.DoCleanup(); scene.UnloadGl(); throw; }
    }
    private static int EntityCount(Scene scene)
    {
        int count = 0;
        foreach (EntityBase entity in scene.Entities) count++;
        return count;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    [MethodImpl(MethodImplOptions.NoInlining)] private static bool Alive(WeakReference<Scene> world) => world.TryGetTarget(out _);
}
