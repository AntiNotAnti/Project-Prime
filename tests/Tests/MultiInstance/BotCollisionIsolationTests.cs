using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class BotCollisionIsolationTests
{
    [Fact]
    public void RetainedCandidatesSurviveOtherSceneAndSameSceneQueriesBeyondOldPoolCapacity()
    {
        using Scene first = MakeScene(2050, 2);
        using Scene second = MakeScene(3, 1);
        var retained = Candidates(first, 2050);
        Assert.Equal(2050, retained.Count);
        var collision = retained[0].Collision;
        Parallel.For(0, 100, i =>
        {
            var current = Candidates(i % 2 == 0 ? first : second, i % 2 == 0 ? 2050 : 3);
            Assert.Equal(i % 2 == 0 ? 2050 : 3, current.Count);
            Assert.NotSame(retained, current);
            Assert.NotSame(retained[0], current[0]);
        });
        Assert.Equal(2050, retained.Count);
        Assert.All(retained, item => Assert.Same(collision, item.Collision));
    }

    [Fact]
    public void ParallelCollisionResultsMatchSerialQueriesWithDifferentGeometry()
    {
        using Scene first = MakeScene(1, 2);
        using Scene second = MakeScene(1, 1);
        float Query(Scene scene)
        {
            CollisionResult result = default;
            Assert.True(CollisionDetection.CheckBetweenPoints(new(2, 3, 2), new(2, 0, 2), TestFlags.None, scene, ref result));
            return result.Position.Y;
        }
        float firstSerial = Query(first);
        float secondSerial = Query(second);
        Assert.Equal(2, firstSerial);
        Assert.Equal(1, secondSerial);
        Parallel.For(0, 1000, i => Assert.Equal(i % 2 == 0 ? firstSerial : secondSerial, Query(i % 2 == 0 ? first : second)));
    }

    [Fact]
    public void InterleavedBotVisibilityAndDestinationUpdatesMatchIsolatedRunAcrossOtherReset()
    {
        using Scene isolated = MakeScene(1, 2);
        using Scene first = MakeScene(1, 2);
        using Scene second = MakeScene(1, 1);
        foreach (Scene scene in new[] { isolated, first, second })
        {
            scene.Players.MaxPlayers = 2;
            for (int slot = 0; slot < 2; slot++)
            {
                scene.Players[slot].Health = 100;
                scene.Players[slot].LoadFlags = LoadFlags.Active;
                scene.Players[slot].IsBot = true;
                scene.Players[slot].CameraInfo.Position = new Vector3(2, slot == 0 ? 1.5f : 3, 2);
            }
            scene.BotRuntimeState.GlobalField2 = 1;
            var destination = scene.BotRuntimeState.GlobalObjects[0];
            destination.Player = scene.Players[1];
            destination.Field4 = 1;
            destination.NodeData = new[] { new NodeData3(new Vector3Fx(8192, 6144, 8192)) };
        }
        for (int i = 0; i < 50; i++)
        {
            PlayerEntity.PlayerAiData.UpdateVisibilityAndGlobals(isolated);
            PlayerEntity.PlayerAiData.UpdateVisibilityAndGlobals(first);
            PlayerEntity.PlayerAiData.UpdateVisibilityAndGlobals(second);
            Assert.False(first.BotRuntimeState.PlayerVisibility[0, 1]);
            Assert.True(second.BotRuntimeState.PlayerVisibility[0, 1]);
            Assert.Equal(isolated.BotRuntimeState.GlobalField2, first.BotRuntimeState.GlobalField2);
            Assert.Equal(isolated.BotRuntimeState.VisibilityIndex1, first.BotRuntimeState.VisibilityIndex1);
            Assert.Equal(isolated.BotRuntimeState.VisibilityIndex2, first.BotRuntimeState.VisibilityIndex2);
            PlayerEntity.PlayerAiData.InitializeGlobals(second);
            Assert.Same(first.Players[1], first.BotRuntimeState.GlobalObjects[0].Player);
            var targetField = typeof(PlayerEntity.PlayerAiData).GetField("_node40", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Same(first.BotRuntimeState.GlobalObjects[0].NodeData[0], targetField.GetValue(first.Players[1].AiData));
            Assert.NotSame(targetField.GetValue(second.Players[1].AiData), targetField.GetValue(first.Players[1].AiData));
        }
    }

    private static System.Collections.Generic.IReadOnlyList<CollisionCandidate> Candidates(Scene scene, int count) =>
        CollisionDetection.GetCandidatesForLimits(null, default, 0, Vector3.Zero, new Vector3(count * 4 - 1, 3, 3), false, scene);

    private static Scene MakeScene(int cells, int planeHeight)
    {
        var scene = new Scene(headless: true);
        var header = Struct<CollisionHeader>((nameof(CollisionHeader.PartsX), cells), (nameof(CollisionHeader.PartsY), 1), (nameof(CollisionHeader.PartsZ), 1));
        var data = Struct<CollisionData>((nameof(CollisionData.LayerMask), (ushort)1), (nameof(CollisionData.PointIndexCount), (ushort)4));
        var info = new MphCollisionInfo(header,
            new[] { new Vector3Fx(0, planeHeight * 4096, 0), new Vector3Fx(16384, planeHeight * 4096, 0), new Vector3Fx(16384, planeHeight * 4096, 16384), new Vector3Fx(0, planeHeight * 4096, 16384) },
            new[] { Struct<Vector4Fx>((nameof(Vector4Fx.Y), new Fixed(4096)), (nameof(Vector4Fx.W), new Fixed(planeHeight * 4096))) }, new ushort[] { 0, 1, 2, 3, 0 }, new[] { data }, new ushort[] { 0 },
            Enumerable.Repeat(new CollisionEntry(1, 0), cells).ToArray(), Array.Empty<Portal>());
        var room = new RoomEntity(scene);
        room._roomCollision.Add(new CollisionInstance("synthetic", info, false));
        typeof(Scene).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, room);
        return scene;
    }

    private static T Struct<T>(params (string Name, object Value)[] fields) where T : struct
    {
        object value = default(T);
        foreach (var field in fields) typeof(T).GetField(field.Name)!.SetValue(value, field.Value);
        return (T)value;
    }
}
