using System;
using System.Buffers;
using MphRead.Mods.Network;
using MphRead.Runtime.HistoricalCollision;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// World-space presentation of the bounded server diagnostic packet. It uses
/// the existing volume submission primitives so both the legacy and SDL
/// renderers see the same facts. The packet is never consulted by gameplay.
/// </summary>
internal static class HistoricalCollisionDebugPresentation
{
    public static void Draw(ScenePresentation scene, HistoricalCollisionDebugPacket packet)
    {
        if (packet.Mode == HistoricalCollisionDebugMode.History)
        {
            foreach (HistoricalCollisionDebugPlayer player in packet.Players)
                DrawPlayer(scene, player);
        }
        else
        {
            foreach (HistoricalCollisionDebugCollider collider in packet.Colliders)
                DrawCollider(scene, collider);
        }

        if (packet.HasProjectilePath)
            DrawPath(scene, packet.ProjectileStart, packet.ProjectileEnd);
    }

    private static void DrawPlayer(ScenePresentation scene, HistoricalCollisionDebugPlayer player)
    {
        float radius = MathF.Max(0.05f, MathF.Min(player.SphereRadius, 32));
        float minY = player.Position.Y + MathF.Max(-64, MathF.Min(64, player.MinPickupHeight));
        float maxY = player.Position.Y + MathF.Max(-64, MathF.Min(64, player.MaxPickupHeight));
        if (maxY < minY) (minY, maxY) = (maxY, minY);
        Vector3 center = player.SpherePosition;
        Vector3 min = new(center.X - radius, minY, center.Z - radius);
        Vector3 max = new(center.X + radius, MathF.Max(minY + 0.05f, maxY), center.Z + radius);
        scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(),
            player.CanBeHit ? new Vector4(0.2f, 1f, 0.2f, 0.24f) : new Vector4(0.5f, 0.5f, 0.5f, 0.14f),
            RenderPrimitive.Box, Box(min, max), 8);
    }

    private static void DrawCollider(ScenePresentation scene, HistoricalCollisionDebugCollider collider)
    {
        Vector4 color = collider.Kind switch
        {
            HistoricalColliderKind.Door => new Vector4(1f, 0.55f, 0.1f, collider.Blocking ? 0.6f : 0.18f),
            HistoricalColliderKind.ForceField => new Vector4(0.1f, 0.9f, 1f, collider.Active ? 0.5f : 0.14f),
            HistoricalColliderKind.Platform => new Vector4(0.1f, 0.75f, 0.9f, collider.Active ? 0.35f : 0.12f),
            _ => new Vector4(1f, 0.15f, 0.8f, collider.Active ? 0.35f : 0.12f)
        };
        switch (collider.Kind)
        {
            case HistoricalColliderKind.Door:
                float radius = MathF.Sqrt(MathF.Max(0, collider.RadiusSquared));
                if (radius > 0 && TryDoorRectangle(collider.Position, collider.Facing, radius, out Vector3[] door))
                    AddPolygon(scene, door, 4, color);
                break;
            case HistoricalColliderKind.ForceField:
                if (collider.Width > 0 && collider.Height > 0)
                {
                    Vector3 up = collider.Up * collider.Height;
                    Vector3 right = collider.Right * collider.Width;
                    AddPolygon(scene, RentQuad(
                        collider.Position - up - right,
                        collider.Position - up + right,
                        collider.Position + up + right,
                        collider.Position + up - right), 4, color);
                }
                break;
            case HistoricalColliderKind.Object:
            case HistoricalColliderKind.Platform:
                AddBox(scene, collider.BoundsMin, collider.BoundsMax, color);
                break;
        }
    }

    private static void DrawPath(ScenePresentation scene, Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        if (direction.LengthSquared < 0.000001f) return;
        Vector3 toCamera = scene.ViewPosition - start;
        Vector3 side = Vector3.Cross(direction, toCamera);
        if (side.LengthSquared < 0.000001f) side = Vector3.Cross(direction, Vector3.UnitY);
        if (side.LengthSquared < 0.000001f) side = Vector3.Cross(direction, Vector3.UnitX);
        side = side.Normalized() * 0.035f;
        AddPolygon(scene, RentQuad(start - side, start + side, end + side, end - side), 4,
            new Vector4(1f, 1f, 0.1f, 0.9f));
    }

    private static void AddBox(ScenePresentation scene, Vector3 first, Vector3 second, Vector4 color)
    {
        Vector3 min = Vector3.ComponentMin(first, second);
        Vector3 max = Vector3.ComponentMax(first, second);
        if (max.X - min.X < 0.05f) max.X = min.X + 0.05f;
        if (max.Y - min.Y < 0.05f) max.Y = min.Y + 0.05f;
        if (max.Z - min.Z < 0.05f) max.Z = min.Z + 0.05f;
        scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(), color,
            RenderPrimitive.Box, Box(min, max), 8);
    }

    private static void AddPolygon(ScenePresentation scene, Vector3[] points, int pointCount, Vector4 color)
        => scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(), color,
            RenderPrimitive.Ngon, points, pointCount);

    internal static Vector3[] Box(Vector3 min, Vector3 max)
    {
        Vector3[] points = ArrayPool<Vector3>.Shared.Rent(8);
        points[0] = new Vector3(min.X, min.Y, min.Z);
        points[1] = new Vector3(min.X, min.Y, max.Z);
        points[2] = new Vector3(max.X, min.Y, min.Z);
        points[3] = new Vector3(max.X, min.Y, max.Z);
        points[4] = new Vector3(min.X, max.Y, min.Z);
        points[5] = new Vector3(min.X, max.Y, max.Z);
        points[6] = new Vector3(max.X, max.Y, min.Z);
        points[7] = new Vector3(max.X, max.Y, max.Z);
        return points;
    }

    internal static Vector3[] RentQuad(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth)
    {
        Vector3[] points = ArrayPool<Vector3>.Shared.Rent(4);
        points[0] = first;
        points[1] = second;
        points[2] = third;
        points[3] = fourth;
        return points;
    }

    private static bool TryDoorRectangle(Vector3 center, Vector3 facing, float radius, out Vector3[] points)
    {
        points = Array.Empty<Vector3>();
        if (facing.LengthSquared < 0.000001f) return false;
        Vector3 normal = facing.Normalized();
        Vector3 reference = MathF.Abs(Vector3.Dot(normal, Vector3.UnitY)) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 horizontal = Vector3.Cross(normal, reference);
        if (horizontal.LengthSquared < 0.000001f) return false;
        horizontal = horizontal.Normalized() * radius;
        Vector3 vertical = Vector3.Cross(horizontal.Normalized(), normal).Normalized() * radius;
        points = RentQuad(center - horizontal - vertical, center + horizontal - vertical,
            center + horizontal + vertical, center - horizontal + vertical);
        return true;
    }
}
