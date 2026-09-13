using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public enum RadarSurfaceKind { Floor, Ramp }

public sealed class RadarPolygon
{
    public RadarPolygon(IReadOnlyList<Vector2> points, float elevation, RadarSurfaceKind surface)
    {
        Points = Array.AsReadOnly(Copy(points));
        Elevation = elevation;
        Surface = surface;
    }

    public IReadOnlyList<Vector2> Points { get; }
    public float Elevation { get; }
    public RadarSurfaceKind Surface { get; }

    private static Vector2[] Copy(IReadOnlyList<Vector2> source)
    {
        var result = new Vector2[source.Count];
        for (int i = 0; i < result.Length; i++) result[i] = source[i];
        return result;
    }
}

public sealed class RadarFloorBand
{
    public RadarFloorBand(float minY, float maxY, float centerY, IReadOnlyList<RadarPolygon> polygons)
    {
        MinY = minY;
        MaxY = maxY;
        CenterY = centerY;
        var copy = new RadarPolygon[polygons.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = polygons[i];
        Polygons = Array.AsReadOnly(copy);
    }

    public float MinY { get; }
    public float MaxY { get; }
    public float CenterY { get; }
    public IReadOnlyList<RadarPolygon> Polygons { get; }
}

public sealed class RadarMapGeometry
{
    public RadarMapGeometry(Vector2 min, Vector2 max, IReadOnlyList<RadarFloorBand> floors)
    {
        Min = min;
        Max = max;
        var copy = new RadarFloorBand[floors.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = floors[i];
        Floors = Array.AsReadOnly(copy);
    }

    public Vector2 Min { get; }
    public Vector2 Max { get; }
    public Vector2 WorldSize => Max - Min;
    public IReadOnlyList<RadarFloorBand> Floors { get; }

    public Vector2 WorldToMap(Vector3 worldPosition)
    {
        float width = Max.X - Min.X;
        float height = Max.Y - Min.Y;
        float u = float.IsFinite(worldPosition.X) && width > .0001f ? (worldPosition.X - Min.X) / width : .5f;
        float v = float.IsFinite(worldPosition.Z) && height > .0001f ? (worldPosition.Z - Min.Y) / height : .5f;
        return new Vector2(u, v);
    }

    public int FindCurrentFloor(float playerY)
    {
        if (Floors.Count == 0) return -1;
        if (!float.IsFinite(playerY)) return 0;
        int nearest = 0;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < Floors.Count; i++)
        {
            RadarFloorBand floor = Floors[i];
            if (playerY >= floor.MinY && playerY <= floor.MaxY) return i;
            float distance = MathF.Abs(playerY - floor.CenterY);
            if (distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }
        return nearest;
    }
}
