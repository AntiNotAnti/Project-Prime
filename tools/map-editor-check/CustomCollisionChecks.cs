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
            var candidates = info.Entries.Where(e => e.DataCount > 0)
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
    }
}
