using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

internal static class EchoesFloorPlacementFixtureTests
{
    public static void Run()
    {
        // Up-facing floor Y=0 with nontrivial XZ barycentrics.
        var floor = new MapMesh
        {
            CollisionOnly = true, Solid = true,
            Vertices = new List<float[]>
            {
                new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 5f },
                new[] { 5f, 0f, 0f }
            },
            Faces = new List<int[]> { new[] { 0, 1, 2 } }
        };
        Check(EchoesFloorPlacement.Snap(new[] { floor }, new[] { 1f, 1f, 1f },
            3f, out float[] grounded) && MathF.Abs(grounded[1]) < 1e-5f,
            "walkable floor beneath raised spawn");
        Check(!EchoesFloorPlacement.Snap(new[] { floor }, new[] { 1f, 5f, 1f },
            3f, out _), "reject floor beneath max drop");
        Check(!EchoesFloorPlacement.Snap(new[] { floor }, new[] { 50f, 1f, 50f },
            3f, out _), "reject unsupported horizontal coordinates");
        floor.CollisionOnly = false;
        Check(!EchoesFloorPlacement.Snap(new[] { floor }, new[] { 1f, 1f, 1f },
            3f, out _), "visual-only geometry must never change spawn placement");
        floor.CollisionOnly = true;

        var backwards = new MapMesh
        {
            CollisionOnly = true, Solid = true,
            Vertices = floor.Vertices,
            Faces = new List<int[]> { new[] { 0, 2, 1 } }
        };
        Check(!EchoesFloorPlacement.Snap(new[] { backwards }, new[] { 1f, 1f, 1f },
            3f, out _), "down-facing geometry is not a floor");

        var map = new MapDefinition { Name = "CANNON ECHOES SPIRES",
            LocalSourceOnly = true, Geometry = new List<MapGeometry> { floor },
            Spawns = new List<MapSpawn> {
                new(){Id=Guid.NewGuid(),Position=new[]{1f,1f,1f}},
                new(){Id=Guid.NewGuid(),Position=new[]{20f,1f,20f}} },
            Items = new List<MapItem> {
                new(){Id=Guid.NewGuid(),Position=new[]{1.5f,1f,1f},Type="HealthSmall"} } };
        int moved = EchoesFloorPlacement.SnapSpawnAndPickupFeet(map, 3f);
        Check(moved == 2 && MathF.Abs(map.Spawns[0].Position[1]) < 1e-5f &&
            MathF.Abs(map.Items[0].Position[1]) < 1e-5f &&
            map.Spawns[1].Position[1] == 1f,
            "spawn and pickup align only where verified source floor exists");
        Check(EchoesFloorPlacement.SnapSpawnAndPickupFeet(map, 3f) == 2,
            "source floor placement is idempotent on already grounded points");
        map.LocalSourceOnly = false;
        Expect<InvalidDataException>(() =>
            EchoesFloorPlacement.SnapSpawnAndPickupFeet(map, 3f),
            "cannot mutate ordinary/shareable map geometry");
    }
    private static void Check(bool yes, string why)
    {
        if (!yes) throw new InvalidOperationException("Echoes floor alignment: " + why);
    }
    private static void Expect<T>(Action call, string why) where T:Exception
    {
        try { call(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why);
    }
}
