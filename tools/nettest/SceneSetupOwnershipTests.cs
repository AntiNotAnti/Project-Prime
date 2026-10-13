using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Exercise production setup entry points with a foreground and a private scene.
// The generated navigation data is content-free. Precreated turret/spawner
// objects exclude retail models so this tests owner selection, not model loading.
internal static class SceneSetupOwnershipTests
{
    internal static int Run(bool baseline)
    {
        Headless.Enter();
        string root = Path.Combine(Path.GetTempPath(), "prime-scene-owner-" + Guid.NewGuid().ToString("N"));
        string previousPath = Paths.FileSystem;
        Directory.CreateDirectory(root);
        int failures = 0;
        void Check(bool value, string label)
        {
            Console.WriteLine($"SCENE OWNER {(value ? "PASS" : "FAIL")} {label}");
            if (!value) { failures++; if (!baseline) throw new InvalidOperationException(label); }
        }
        try
        {
            var foreground = NewScene();
            foreground.GameState.Mode = GameMode.SinglePlayer;
            foreground.Players.PlayersCreated = 3;
            foreground.Players.PlayerCount = 3;
            foreach (var p in foreground.Players.Items) PrecreateTurret(p, foreground);
            using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            var host = (PassiveReplaySessionHost)session.Host;
            var replica = NewScene(new ReplaySceneServices(session, host.State));
            replica.GameState.Mode = GameMode.SinglePlayer;
            replica.AreaId = 0; replica.RoomId = 28;
            var save = (StorySave)RuntimeHelpers.GetUninitializedObject(typeof(StorySave));
            save.LostOctoliths = uint.MaxValue; // no artifact/model creation
            typeof(StorySave).GetProperty(nameof(StorySave.AreaHunters))!.SetValue(save, new byte[4]);
            typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave))!.SetValue(replica.GameState, save);
            foreach (var p in replica.Players.Items) PrecreateTurret(p, replica);
            string before = Fingerprint(foreground);
            var spawner = Spawner(replica, Hunter.Kanden, color: 2);
            SceneSetup.InitHunterSpawns(replica, new EntityBase[] { spawner }, initialize: false);
            Check(Fingerprint(foreground) == before, "private hunter setup leaves foreground registry/state/RNG untouched");
            Check(replica.Players.PlayersCreated == 2 && replica.Players.PlayerCount == 2
                && replica.Players.Items[1].Hunter == Hunter.Kanden && replica.Players.Items[1].Recolor == 2
                && replica.Players.Items[1].IsBot && replica.Players.Items[1].LoadFlags.HasFlag(LoadFlags.SlotActive),
                "hunter is created in the requested scene registry");
            Check(PlayerEntity.Players == foreground.Players.Items && GameState.Current == foreground.GameState,
                "private setup never rebinds foreground compatibility facades");

            // Actual node-file loading observes the scene's encounter state.
            // Version-zero node files can carry a single generated marker point.
            Paths.SetPath(Paths.MphKey, root);
            string fallback = @"levels\nodeData\owner-fallback.bin";
            string encounter = Metadata.EncounterNodeDataOverrides[28];
            WriteNode(Paths.Combine(root, fallback), 1);
            WriteNode(Paths.Combine(root, encounter), 2);
            foreground.Players.Items[1].IsBot = false;
            foreground.GameState.EncounterState[1] = 0;
            replica.Players.Items[1].IsBot = true;
            replica.GameState.EncounterState[1] = 1;
            Check(NodeMarker(replica, fallback, 28) == 2, "private encounter selects its own navigation override");
            foreground.Players.Items[1].IsBot = true;
            foreground.GameState.EncounterState[1] = 1;
            replica.Players.Items[1].IsBot = false;
            replica.GameState.EncounterState[1] = 0;
            Check(NodeMarker(replica, fallback, 28) == 1, "foreground encounter cannot override private navigation");
            Check(NodeMarker(foreground, fallback, 28) == 2, "foreground legacy behavior remains equivalent");
            Console.WriteLine($"SCENE OWNER summary failures={failures}");
            return !baseline && failures != 0 ? 1 : 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Paths.SetPath(Paths.MphKey, previousPath); Directory.Delete(root, recursive: true); }
    }

    private static Scene NewScene(ISceneServices? services = null) => new(new Vector2i(256, 192),
        SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, services, initializeRuntime: false);
    private static void PrecreateTurret(PlayerEntity player, Scene scene)
        => typeof(PlayerEntity).GetField("_halfturret", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(player, new HalfturretEntity(player, scene));
    private static string Fingerprint(Scene scene) => string.Join("|", scene.Players.Items.Select(p =>
        $"{p.SlotIndex}:{p.Hunter}:{p.Recolor}:{p.LoadFlags}:{p.IsBot}:{p.BotLevel}"))
        + $"/{scene.Players.PlayersCreated}/{scene.Players.PlayerCount}/{scene.Random.Rng1}/{scene.Random.Rng2}"
        + "/" + string.Join(",", scene.GameState.EncounterState);
    private static EnemySpawnEntity Spawner(Scene scene, Hunter hunter, byte color)
    {
        byte[] bytes = new byte[Marshal.SizeOf<EnemySpawnEntityData>()];
        bytes[(int)Marshal.OffsetOf<EnemySpawnEntityData>(nameof(EnemySpawnEntityData.EnemyType))] = (byte)EnemyType.Hunter;
        int fields = (int)Marshal.OffsetOf<EnemySpawnEntityData>(nameof(EnemySpawnEntityData.Fields));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(fields), (uint)hunter);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(fields + 4), 1); // encounter
        bytes[fields + (int)Marshal.OffsetOf<EnemySpawnFields09>(nameof(EnemySpawnFields09.HunterColor))] = color;
        bytes[fields + (int)Marshal.OffsetOf<EnemySpawnFields09>(nameof(EnemySpawnFields09.HunterChance))] = 100;
        var entity = (EnemySpawnEntity)RuntimeHelpers.GetUninitializedObject(typeof(EnemySpawnEntity));
        typeof(EntityBase).GetField("<Type>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, EntityType.EnemySpawn);
        typeof(EnemySpawnEntity).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, Read.ReadStruct<EnemySpawnEntityData>(bytes));
        return entity;
    }
    private static void WriteNode(string path, int marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = new byte[4 + Marshal.SizeOf<FhNodeData>()];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
        int at = 4 + (int)Marshal.OffsetOf<FhNodeData>(nameof(FhNodeData.Position));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), marker * 4096);
        File.WriteAllBytes(path, bytes);
    }
    private static float NodeMarker(Scene scene, string fallback, int room)
    {
        var method = typeof(SceneSetup).GetMethod(nameof(SceneSetup.LoadNodeData))!;
        object?[] args = method.GetParameters().Length == 6
            ? new object?[] { fallback, room, GameMode.SinglePlayer, Array.Empty<EntityBase>(), false, scene }
            : new object?[] { fallback, room, GameMode.SinglePlayer, Array.Empty<EntityBase>(), false };
        var result = (NodeData)method.Invoke(null, args)!;
        return result.Data[0][0][0].Position.X;
    }
}
