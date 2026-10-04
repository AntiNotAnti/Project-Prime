using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Input;
using MphRead.Mods.MapGen;
using MphRead.Utility;
using OpenTK.Mathematics;

static class CustomCollisionChecks
{
    public static void Run(Action<bool, string> check)
    {
        CollisionDetection.Init();
        var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(),
            SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
        var room = new RoomEntity(scene);
        // Install only collision in this fixture, without loading models or GL.
        typeof(Scene).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, room);
        var collisions = (List<CollisionInstance>)room.RoomCollision;
        var faces = new List<CollisionDataEditor>();
        foreach (int x in new[] { 0, 8 })
        {
            var face = new CollisionDataEditor { Plane = new Vector4(Vector3.UnitY, 0), LayerMask = 5 };
            face.Points.AddRange(new[] { new Vector3(x, 0, 0), new Vector3(x, 0, 4),
                new Vector3(x + 4, 0, 4), new Vector3(x + 4, 0, 0) });
            faces.Add(face);
        }
        byte[] bytes = MapCollisionPacker.Pack(faces);
        var header = Read.ReadStruct<CollisionHeader>(bytes);
        var compact = Collision.ReadMphCollision(header, bytes, -1);
        // Legacy files append the first vertex after every face. Both layouts
        // must give the same contacts, including the final face in the file.
        var indices = new List<ushort>();
        var data = new List<CollisionData>();
        foreach (var face in compact.Data)
        {
            byte[] record = new byte[16];
            System.Runtime.InteropServices.MemoryMarshal.Write(record, in face);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(14), (ushort)indices.Count);
            data.Add(Read.ReadStruct<CollisionData>(record));
            indices.AddRange(compact.PointIndices.Skip(face.PointStartIndex).Take(face.PointIndexCount));
            indices.Add(compact.PointIndices[face.PointStartIndex]);
        }
        var legacy = new MphCollisionInfo(header,
            Read.DoOffsets<Vector3Fx>(bytes, header.PointOffset, header.PointCount),
            Read.DoOffsets<Vector4Fx>(bytes, header.PlaneOffset, header.PlaneCount),
            indices, data, compact.DataIndices, compact.Entries, compact.Portals);
        foreach (var info in new[] { compact, legacy })
        foreach (var translation in new[] { Vector3.Zero, new Vector3(12, 3, -8) })
        {
            var instance = new CollisionInstance("fixture", info, false) { Translation = translation };
            collisions.Clear(); collisions.Add(instance);
            var candidates = info.RuntimeEntries.Where(e => e.DataCount > 0)
                .Select(e => new CollisionCandidate(instance, e)).ToArray();
            var results = new CollisionResult[8];
            foreach (int x in new[] { 0, 8 })
            foreach (float z in new[] { 2f, -0.1f, -1f })
            {
                var point = new Vector3(x + 2, 0, z) + translation;
                int swept = CollisionDetection.CheckSphereBetweenPoints(candidates,
                    point + Vector3.UnitY, point - Vector3.UnitY, 0.5f, 8, true,
                    TestFlags.Players, scene, results);
                bool expected = z >= -0.1f;
                check((swept > 0) == expected, $"swept floor/closing-edge contact x={x}, z={z}");
                if (z == -0.1f && swept > 0)
                    check(results[0].EdgePoint2 == new Vector3(x, 0, 0) + translation,
                        "closing edge uses its own first vertex");
                int overlap = CollisionDetection.CheckInRadius(point + Vector3.UnitY * 0.2f,
                    0.5f, 8, false, TestFlags.Players, scene, results);
                check((overlap > 0) == expected, $"radius floor/closing-edge contact x={x}, z={z}");
            }
        }

        // Adjacent coplanar faces share an internal edge. A sphere just
        // inside the second face must not receive a fake edge contact from the
        // first face; only the supporting plane is a real obstruction.
        var seamFaces = new List<CollisionDataEditor>();
        foreach (int x in new[] { 0, 4 })
        {
            var face = new CollisionDataEditor { Plane = new Vector4(Vector3.UnitY, 0), LayerMask = 5 };
            face.Points.AddRange(new[] { new Vector3(x, 0, 0), new Vector3(x, 0, 4),
                new Vector3(x + 4, 0, 4), new Vector3(x + 4, 0, 0) });
            seamFaces.Add(face);
        }
        byte[] seamBytes = MapCollisionPacker.Pack(seamFaces);
        var seamHeader = Read.ReadStruct<CollisionHeader>(seamBytes);
        var seamInfo = Collision.ReadMphCollision(seamHeader, seamBytes, -1);
        var seamInstance = new CollisionInstance("internal-seam", seamInfo, false);
        collisions.Clear(); collisions.Add(seamInstance);
        var seamCandidates = seamInfo.RuntimeEntries.Where(e => e.DataCount > 0)
            .Select(e => new CollisionCandidate(seamInstance, e)).ToArray();
        var seamResults = new CollisionResult[16];
        Vector3 seamPoint = new(4.1f, 0, 2);
        int seamCount = CollisionDetection.CheckSphereBetweenPointsRobust(seamCandidates,
            seamPoint + Vector3.UnitY, seamPoint - Vector3.UnitY, 0.5f, seamResults.Length,
            includeOffset: true, TestFlags.Players, scene, seamResults);
        check(seamCount > 0 && seamResults.Take(seamCount).All(result => result.Field0 == 0),
            "coplanar shared edge is suppressed as an internal player seam");

        Array.Clear(seamResults);
        Vector3 facePoint = new(2, 0, 2);
        int faceCount = CollisionDetection.CheckSphereBetweenPointsRobust(seamCandidates,
            facePoint + Vector3.UnitY, facePoint - Vector3.UnitY, 0.5f, seamResults.Length,
            includeOffset: true, TestFlags.Players, scene, seamResults);
        check(faceCount > 0 && seamResults.Take(faceCount).Any(result =>
                result.Field0 == 0 && MathF.Abs(result.Distance - .25f) < .001f),
            "robust face sweep reports sphere-surface time of impact before center-plane crossing");

        Array.Clear(seamResults);
        Vector3 cornerPoint = new(-0.4f, 0, -0.4f);
        int cornerCount = CollisionDetection.CheckSphereBetweenPointsRobust(seamCandidates,
            cornerPoint + Vector3.UnitY, cornerPoint - Vector3.UnitY, 0.5f, seamResults.Length,
            includeOffset: true, TestFlags.Players, scene, seamResults);
        check(cornerCount == 0,
            "robust edge query rejects a corner that is outside the true sphere radius");

        Array.Clear(seamResults);
        Vector3 outerEdgePoint = new(-0.3f, 0, 2);
        int outerEdgeCount = CollisionDetection.CheckSphereBetweenPointsRobust(seamCandidates,
            outerEdgePoint + Vector3.UnitY, outerEdgePoint - Vector3.UnitY, 0.5f, seamResults.Length,
            includeOffset: true, TestFlags.Players, scene, seamResults);
        check(outerEdgeCount > 0 && seamResults.Take(outerEdgeCount).Any(result =>
                result.Field0 == 1 && result.Plane.X < -0.9f && MathF.Abs(result.Plane.Y) < 0.1f),
            "robust outer-edge contact exposes a radial slide normal");
        float outerEdgeImpact = seamResults.Take(outerEdgeCount)
            .Where(result => result.Field0 == 1).Min(result => result.Distance);
        check(outerEdgeImpact > .27f && outerEdgeImpact < .33f,
            "outer-edge sweep reports first sphere contact instead of center-plane crossing");

        Array.Clear(seamResults);
        int tangentCount = CollisionDetection.CheckSphereBetweenPointsRobust(seamCandidates,
            new Vector3(2,.5f,2),new Vector3(3,.5f,2),.5f,seamResults.Length,
            includeOffset:true,TestFlags.Players,scene,seamResults);
        check(tangentCount==0,
            "resting horizontal movement does not treat an existing floor contact as a fresh obstruction");

        // Recovery is a distinct overlap query: unlike a forward sweep it must
        // detect a shallow start behind a face so the controller can push the
        // player back to valid space instead of silently accepting penetration.
        Array.Clear(seamResults);
        int penetrationCount = CollisionDetection.CheckSpherePenetration(
            new Vector3(2, -0.1f, 2), 0.5f, seamResults.Length,
            TestFlags.Players, scene, seamResults);
        check(penetrationCount > 0
            && seamResults.Take(penetrationCount).Any(result =>
                result.Plane.Y > 0.9f && result.Field14 > 0.39f),
            "bounded overlap query recovers shallow starts behind a collision plane");

        Array.Clear(seamResults);
        int seamPenetrationCount = CollisionDetection.CheckSpherePenetration(
            new Vector3(4.1f,.2f,2),.5f,seamResults.Length,
            TestFlags.Players,scene,seamResults);
        check(seamPenetrationCount > 0
            && seamResults.Take(seamPenetrationCount).All(result => result.Plane.Y > .9f),
            "depenetration suppresses lateral contacts on shared coplanar floor edges");

        var denseFaces = new List<CollisionDataEditor>();
        for (int i = 0; i < 48; i++)
        {
            float y = -0.2f + i * 0.008f;
            var face = new CollisionDataEditor { Plane = new Vector4(Vector3.UnitY, y), LayerMask = 5 };
            face.Points.AddRange(new[] { new Vector3(-2, y, -2), new Vector3(-2, y, 2),
                new Vector3(2, y, 2), new Vector3(2, y, -2) });
            denseFaces.Add(face);
        }
        byte[] denseBytes = MapCollisionPacker.Pack(denseFaces);
        var denseHeader = Read.ReadStruct<CollisionHeader>(denseBytes);
        var denseInfo = Collision.ReadMphCollision(denseHeader, denseBytes, -1);
        collisions.Clear(); collisions.Add(new CollisionInstance("dense-overlap", denseInfo, false));
        var boundedResults = new CollisionResult[40];
        int boundedCount = CollisionDetection.CheckSpherePenetration(Vector3.Zero, 0.5f,
            boundedResults.Length, TestFlags.Players, scene, boundedResults);
        check(boundedCount == boundedResults.Length,
            "penetration query stops at the runtime contact budget for conservative overflow fallback");
        var denseCandidates = denseInfo.RuntimeEntries.Where(e => e.DataCount > 0)
            .Select(e => new CollisionCandidate(collisions[0], e)).ToArray();
        Array.Clear(boundedResults);
        int denseSweepCount = CollisionDetection.CheckSphereBetweenPointsRobust(
            denseCandidates,new Vector3(0,1,0),new Vector3(0,-1,0),.5f,
            boundedResults.Length,includeOffset:true,TestFlags.Players,scene,boundedResults);
        check(denseSweepCount == boundedResults.Length,
            "continuous player sweep exposes a full contact buffer so movement can fail conservatively");

        // Grounded morphing keeps the retail center-preserving form
        // switch, but a larger alt sphere must not be born below the supporting
        // floor. Samus has equal radii and needs no correction; Sylux/Trace do.
        MethodInfo groundedAltLift = typeof(PlayerEntity).GetMethod(
            "ModGroundedFormLift", BindingFlags.Static | BindingFlags.NonPublic)!;
        float samusLift = (float)groundedAltLift.Invoke(null, new object[]
        {
            PlayerEntity.PlayerVolumes[(int)Hunter.Samus, 0],
            PlayerEntity.PlayerVolumes[(int)Hunter.Samus, 2]
        })!;
        float syluxLift = (float)groundedAltLift.Invoke(null, new object[]
        {
            PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 0],
            PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 2]
        })!;
        float traceLift = (float)groundedAltLift.Invoke(null, new object[]
        {
            PlayerEntity.PlayerVolumes[(int)Hunter.Trace, 0],
            PlayerEntity.PlayerVolumes[(int)Hunter.Trace, 2]
        })!;
        float weavelUnmorphLift = (float)groundedAltLift.Invoke(null, new object[]
        {
            PlayerEntity.PlayerVolumes[(int)Hunter.Weavel, 2],
            PlayerEntity.PlayerVolumes[(int)Hunter.Weavel, 0]
        })!;
        float noxusUnmorphLift = (float)groundedAltLift.Invoke(null, new object[]
        {
            PlayerEntity.PlayerVolumes[(int)Hunter.Noxus, 2],
            PlayerEntity.PlayerVolumes[(int)Hunter.Noxus, 0]
        })!;
        check(MathF.Abs(samusLift) < .0001f,
            "grounded Samus morph keeps the retail collision height");
        check(syluxLift > .12f && syluxLift < .14f,
            "grounded Sylux morph preserves the old collision bottom");
        check(traceLift > .12f && traceLift < .14f,
            "grounded Trace morph preserves the old collision bottom");
        check(weavelUnmorphLift > .09f && weavelUnmorphLift < .11f,
            "grounded Weavel unmorph preserves the old collision bottom");
        check(MathF.Abs(noxusUnmorphLift) < .0001f,
            "grounded Noxus unmorph needs no floor correction");

        var wideFaces = new List<CollisionDataEditor>(22000);
        for (int i = 0; i < 22000; i++)
        {
            float y = i / 4096f;
            var face = new CollisionDataEditor { Plane = new Vector4(Vector3.UnitY, y), LayerMask = 5 };
            face.Points.AddRange(new[]
            {
                new Vector3(-1, y, -1), new Vector3(0, y, 1), new Vector3(1, y, -1)
            });
            wideFaces.Add(face);
        }
        byte[] wideBytes = MapCollisionPacker.Pack(wideFaces);
        var wideHeader = Read.ReadStruct<CollisionHeader>(wideBytes);
        check(wideHeader.Type.MarshalString() == "wc02", "large custom collision selects wc02");
        PrimeCollisionInfo wide = Collision.ReadPrimeCollision(wideHeader, wideBytes, -1);
        check(wide.RuntimeData.Count == wideFaces.Count && wide.RuntimePointIndices.Count > ushort.MaxValue,
            "wc02 preserves more than 65,535 collision point indices");
        CollisionFace last = wide.RuntimeData[^1];
        check(last.PointStartIndex > ushort.MaxValue
            && wide.RuntimePointIndices[last.PointStartIndex] > ushort.MaxValue,
            "wc02 preserves 32-bit face starts and point indices");
    }
}
