using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Fidelity;

/// <summary>Selects actual room geometry deterministically; never substitutes toy collision.</summary>
internal static class FidelityGeometryFixture
{
    internal static string PlatformRoom()
    {
        foreach (string room in Metadata.RoomList.Where(m => !m.Multiplayer && !m.FirstHunt && m.EntityPath != null).Select(m => m.Name))
        {
            var (metadata, _) = Metadata.GetRoomByName(room);
            if (metadata?.EntityPath == null || metadata.FirstHunt) continue;
            foreach (var entity in Read.GetEntities(metadata.EntityPath, 0, false))
                if (entity is Entity<PlatformEntityData> platform && platform.Data.PositionCount > 1 && platform.Data.Active != 0
                    && Metadata.GetPlatformById((int)platform.Data.ModelId) is { } model
                    && Metadata.ModelMetadata[model.Name].CollisionPath != null) return room;
        }
        throw new InvalidOperationException("No native moving platform fixture available.");
    }
    internal static PlatformEntity PlaceOnPlatform(Scene scene, PlayerEntity actor)
    {
        foreach (var entity in scene.Entities)
        {
            if (entity is not PlatformEntity platform || platform.Data.PositionCount <= 1 || platform.Data.Active == 0
                || platform.EntityCollision[0] is not { Collision.Info: MphCollisionInfoBase collision } instance) continue;
            var face = collision.RuntimeData.Where(f => !f.IgnorePlayers && collision.Planes[f.PlaneIndex].Y > 0.95f)
                .OrderByDescending(f => collision.Planes[f.PlaneIndex].W).First();
            var points = Enumerable.Range(0, face.PointIndexCount)
                .Select(i => collision.Points[collision.RuntimePointIndices[face.PointStartIndex + i]]).ToArray();
            var center = points.Aggregate(Vector3.Zero, (sum, value) => sum + value) / points.Length;
            actor.ModPlaceAt(Vector3.TransformPosition(center, instance.Transform) + Vector3.UnitY * 0.5f);
            return platform;
        }
        throw new InvalidOperationException("Selected room did not instantiate a moving collision platform.");
    }
    internal static void Place(Scene scene, PlayerEntity actor, bool slope)
    {
        var instance = scene.Room!.RoomCollision[0];
        if (instance.Info is not MphCollisionInfoBase collision) throw new InvalidOperationException("Expected native collision.");
        Vector3[] Points(CollisionFace face) => Enumerable.Range(0, face.PointIndexCount)
            .Select(i => collision.Points[collision.RuntimePointIndices[face.PointStartIndex + i]] + instance.Translation).ToArray();
        Vector3? position = null; Vector3 direction = default;
        float best = float.PositiveInfinity;
        for (int i = 0; i < collision.RuntimeData.Count; i++)
        {
            var first = collision.RuntimeData[i]; if (first.IgnorePlayers) continue;
            var normal = collision.Planes[first.PlaneIndex].Xyz;
            if (slope)
            {
                if (normal.Y is < 0.9f or > 0.999f) continue;
                var points = Points(first);
                var center = points.Aggregate(Vector3.Zero, (sum, value) => sum + value) / points.Length;
                float span = points.Max(p => (p - center).LengthSquared);
                if (span < 4) continue;
                float distance = (center - actor.Position).LengthSquared;
                if (distance >= best) continue;
                best = distance; position = center + Vector3.UnitY * 0.7f;
                direction = new Vector3(-normal.X, 0, -normal.Z).Normalized();
            }
            else
            {
                if (MathF.Abs(normal.Y) > 0.001f) continue;
                var points = Points(first);
                for (int j = i + 1; j < collision.RuntimeData.Count; j++)
                {
                    var second = collision.RuntimeData[j]; if (second.IgnorePlayers) continue;
                    var other = collision.Planes[second.PlaneIndex].Xyz;
                    if (MathF.Abs(other.Y) > 0.001f || MathF.Abs(Vector3.Dot(normal, other)) > 0.1f) continue;
                    var shared = Points(second).Where(p => points.Any(q => (p - q).LengthSquared < 0.000001f)).Distinct().ToArray();
                    if (shared.Length < 2 || shared.Max(p => p.Y) - shared.Min(p => p.Y) < 2) continue;
                    // Concave room corner: each wall extends into the other wall's
                    // positive half-space. Convex pillar edges allow sliding around them.
                    if (points.Max(p => Vector3.Dot(other, p - shared[0])) < 0.5f
                        || Points(second).Max(p => Vector3.Dot(normal, p - shared[0])) < 0.5f) continue;
                    var bottom = shared.OrderBy(p => p.Y).First();
                    // Use a wall edge meeting the current floor, not a ceiling junction.
                    if (MathF.Abs(bottom.Y - (actor.Position.Y - 0.5f)) > 0.1f) continue;
                    var inward = normal + other;
                    var candidate = bottom + inward * 0.8f + Vector3.UnitY * 0.5f;
                    float distance = (candidate - actor.Position).LengthSquared;
                    if (distance >= best) continue;
                    best = distance; position = candidate; direction = -inward.Normalized();
                }
            }
        }
        if (position == null) throw new InvalidOperationException("No qualifying native " + (slope ? "slope" : "corner") + " fixture.");
        Console.Error.WriteLine($"Geometry fixture slope={slope} position={position} direction={direction}");
        actor.ModPlaceAt(position.Value);
        actor.Spawn(position.Value, direction, Vector3.UnitY, actor.NodeRef, respawn: true);
        for (int settle = 0; settle < 20; settle++) { Network.NetTestScript.Rest(actor, true); scene.OnSimulationFrame(); }
    }
}
