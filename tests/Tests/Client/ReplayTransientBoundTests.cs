using System;
using System.Buffers.Binary;
using System.Linq;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class ReplayTransientBoundTests
{
    [Fact]
    [Trait("RequiresGameContent", "true")]
    public void ReplayRoomRebuildWithPresentationReplacesAllPreparedSlotsWithoutStaleEntities()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1");
        Read.ServerMode = false;
        ServerContent.Open(data, "AMHE1");
        using var session = new ReplayPlaybackSession();
        using var scene = new Scene { Services = session.SceneServices };
        ScenePresentation? presentation = null;

        try
        {
            FeedCheckpoint(session);
            Assert.Equal(1, session.Modern.PlayerCount);

            SnapshotPlayer snapshot = default;
            foreach (SnapshotPlayer player in session.Modern.Players)
                if (player.Slot == 7) snapshot = player;
            Assert.Equal(7, snapshot.Slot);
            Assert.True((snapshot.Flags & SnapshotPlayerFlags.Active) != 0);
            Assert.True((snapshot.Flags & SnapshotPlayerFlags.Spawned) != 0);

            session.SetPerspective(5);
            presentation = new ScenePresentation(scene, new Vector2i(640, 480),
                (KeyboardState)Activator.CreateInstance(typeof(KeyboardState), nonPublic: true)!,
                (MouseState)Activator.CreateInstance(typeof(MouseState), nonPublic: true)!,
                static _ => { }, static () => { },
                new FrameTiming(), session.SceneServices, session);
            session.BuildPlayers(scene);
            presentation.AddRoom("MP1 SANCTORUS", GameMode.Battle,
                playerCount: session.Modern.Roster.Length);
            presentation.OnLoad();
            Assert.True(scene.LocalPlayer!.GetPresentation().HudReady);
            PlayerEntity[] initial = scene.Players.ToArray();

            session.Modern.RequestSceneReload();
            Exception? error = Record.Exception(() => session.Modern.BeforeSimulation(scene));
            Assert.Null(error);
            session.Modern.AfterSimulation(scene);
            AssertRebuilt(scene, initial, snapshot, expectedLocalSlot: 5);

            PlayerEntity[] firstRebuild = scene.Players.ToArray();
            session.Modern.Reset(NetHeader.Version);
            FeedCheckpoint(session);
            session.Modern.RequestSceneReload();
            error = Record.Exception(() => session.Modern.BeforeSimulation(scene));
            Assert.Null(error);
            session.Modern.AfterSimulation(scene);
            AssertRebuilt(scene, firstRebuild, snapshot, expectedLocalSlot: 5);
        }
        finally
        {
            presentation?.DoCleanup(preserveSharedAudio: true);
        }
    }

    [Trait("RequiresGameContent", "true")]
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualSingleAndLinkedLockjawBombsCannotOutliveReplayWarmup(bool linked)
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(data, "AMHE1");
        using var simulation = new ServerSimulation(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"));
        Scene scene = simulation.Scene;
        scene.Match.Phase = MatchPhase.Playing;
        scene.StepHeadlessFrame(advanceMatch: false);
        PlayerEntity owner = scene.Players[0]; owner.ServerActivate(100, Hunter.Sylux, 0);
        owner.Position = new Vector3(0, 20000, 0);
        var first = Assert.IsType<BombEntity>(BombEntity.Spawn(owner, Matrix4.CreateTranslation(0, 10000, 0), scene));
        if (!first.Initialized) first.Initialize();
        Assert.True(owner.TryRegisterLockjawBomb(first));
        BombEntity? second = null;
        if (linked)
        {
            second = Assert.IsType<BombEntity>(BombEntity.Spawn(owner, Matrix4.CreateTranslation(1, 10000, 0), scene));
            if (!second.Initialized) second.Initialize();
            Assert.True(owner.TryRegisterLockjawBomb(second));
        }
        Assert.Equal(1800, first.Countdown);
        if (second != null) Assert.Equal(1800, second.Countdown);
        bool firstAlive = true, secondAlive = second != null;
        int steps = 0;
        while (firstAlive || secondAlive)
        {
            Assert.True(++steps <= ReplayPlayback.TransientWarmupTicks);
            if (firstAlive) firstAlive = first.Process();
            if (secondAlive) secondAlive = second!.Process();
        }
        Assert.Equal(1801, steps);
    }

    private static void FeedCheckpoint(ReplayPlaybackSession session)
    {
        Assert.True(session.Modern.Receive(ReplayPlaybackTests.Match(1)));
        Assert.True(session.Modern.Receive(Roster(1)));
        Assert.True(session.Modern.Receive(ReplayPlaybackTests.Snapshot(1, 12, 9)));
        foreach (byte[] world in ReplayPlaybackTests.LiveWorld(1))
            Assert.True(session.Modern.Receive(world));
    }

    private static byte[] Roster(uint matchId)
    {
        byte[] body = new byte[4 + SessionRosterPacket.MaxSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body, matchId);
        NetRosterEntry[] entries = { new(5, 99, Hunter.Weavel, 0, "Replay") };
        int length = SessionRosterPacket.Write(body.AsSpan(4), 1, entries);
        byte[] record = new byte[1 + 4 + length];
        record[0] = (byte)ReplayRecordKind.Roster;
        body.AsSpan(0, 4 + length).CopyTo(record.AsSpan(1));
        return record;
    }

    private static void AssertRebuilt(Scene scene, PlayerEntity[] prior,
        SnapshotPlayer snapshot, int expectedLocalSlot)
    {
        Assert.Equal(PlayerEntity.SlotCapacity, scene.Players.CreatedCount);
        Assert.Equal(expectedLocalSlot, scene.LocalPlayerSlot);
        Assert.Same(scene.Players[expectedLocalSlot], scene.LocalPlayer);
        Assert.Equal(PlayerEntity.SlotCapacity, scene.Players.Distinct().Count());
        Assert.All(scene.Players, player =>
        {
            Assert.True(player.LoadFlags.TestFlag(LoadFlags.SlotActive));
            Assert.True(player.LoadFlags.TestFlag(LoadFlags.Initial));
            Assert.False(player.IsBot);
            Assert.Equal(0, player.BotLevel);
            Assert.NotNull(player.Halfturret);
            Assert.NotNull(player.GetPresentation());
            Assert.NotEmpty(player.GetModels());
            Assert.NotNull(player.BipedModel2);
            Assert.DoesNotContain(player, prior);
        });

        var entityList = new System.Collections.Generic.List<PlayerEntity>();
        foreach (PlayerEntity entity in scene.GetPlayerEntities()) entityList.Add(entity);
        PlayerEntity[] entities = entityList.ToArray();
        Assert.Equal(PlayerEntity.SlotCapacity, entities.Length);
        Assert.Equal(PlayerEntity.SlotCapacity, entities.Distinct().Count());
        Assert.All(scene.Players, player => Assert.Contains(player, entities));
        PlayerEntity slot7 = scene.Players[snapshot.Slot];
        Assert.True(slot7.LoadFlags.TestFlag(LoadFlags.Active));
        Assert.True(slot7.LoadFlags.TestFlag(LoadFlags.Spawned));
        Assert.Equal(snapshot.Hunter, slot7.Hunter);
        Assert.NotNull(slot7.GetPresentation());
        Assert.NotEmpty(slot7.GetModels());
        Assert.NotNull(slot7.BipedModel2);
        if (scene.LocalPlayer?.IsMainPlayer == true)
            Assert.True(scene.LocalPlayer.GetPresentation().HudReady);
        Assert.Equal(Hunter.Weavel, scene.Players[5].Hunter);
    }
}
