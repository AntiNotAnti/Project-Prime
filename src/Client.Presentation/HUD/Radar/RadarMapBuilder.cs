using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public static class RadarMapBuilder
{
    public const float FloorNormalThreshold = .55f;
    public const float FloorBandThreshold = 3f;
    private const float MinimumArea = .01f;

    public static RadarMapGeometry Build(RoomEntity room)
        => Build(room?.RoomCollision ?? throw new ArgumentNullException(nameof(room)));

    public static RadarMapGeometry Build(IReadOnlyList<CollisionInstance> collisions)
    {
        if (collisions == null) throw new ArgumentNullException(nameof(collisions));
        var polygons = new List<RadarPolygon>();
        for (int i = 0; i < collisions.Count; i++)
        {
            CollisionInstance instance = collisions[i];
            if (instance == null || !instance.Active || !Finite(instance.Translation)) continue;
            if (instance.Info is MphCollisionInfo mph) ExtractMph(mph, instance.Translation, polygons);
            else if (instance.Info is FhCollisionInfo fh) ExtractFh(fh, instance.Translation, polygons);
        }
        return BuildGeometry(polygons);
    }

    /// <summary>
    /// Computes an in-process content fingerprint for the CPU map cache. This
    /// is only called after a room revision changes (never on the steady-state
    /// per-frame path), and includes the collision payload rather than relying
    /// on a room object or name as identity.
    /// </summary>
    public static int ComputeCollisionFingerprint(IReadOnlyList<CollisionInstance> collisions)
    {
        ArgumentNullException.ThrowIfNull(collisions);
        var hash = new HashCode();
        hash.Add(collisions.Count);
        for (int i = 0; i < collisions.Count; i++)
        {
            CollisionInstance instance = collisions[i];
            if (instance == null)
            {
                hash.Add(0);
                continue;
            }
            hash.Add(instance.Name, StringComparer.Ordinal);
            hash.Add(instance.Active);
            hash.Add(instance.Translation);
            CollisionInfo info = instance.Info;
            hash.Add(info.GetType());
            hash.Add(info.FirstHunt);
            AddList(ref hash, info.Points);
            AddList(ref hash, info.Planes);
            hash.Add(info.Portals.Count);
            if (info is MphCollisionInfo mph)
            {
                AddList(ref hash, mph.PointIndices);
                AddList(ref hash, mph.Data);
                AddList(ref hash, mph.DataIndices);
                AddList(ref hash, mph.Entries);
            }
            else if (info is FhCollisionInfo fh)
            {
                AddList(ref hash, fh.Data);
                AddList(ref hash, fh.Vectors);
                AddList(ref hash, fh.DataIndices);
                AddList(ref hash, fh.Entries);
                AddList(ref hash, fh.TreeNodeIndices);
                AddList(ref hash, fh.TreeNodes);
            }
        }
        return hash.ToHashCode();
    }

    public static RadarMapGeometry BuildGeometry(IReadOnlyList<RadarPolygon> source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var sorted = new List<RadarPolygon>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            RadarPolygon polygon = source[i];
            if (polygon != null && float.IsFinite(polygon.Elevation) && polygon.Points.Count >= 3)
                sorted.Add(polygon);
        }
        sorted.Sort(static (left, right) => left.Elevation.CompareTo(right.Elevation));

        var floors = new List<RadarFloorBand>();
        var cluster = new List<RadarPolygon>();
        float sum = 0, minY = 0, maxY = 0;
        void Flush()
        {
            if (cluster.Count == 0) return;
            floors.Add(new RadarFloorBand(minY, maxY, sum / cluster.Count, cluster));
            cluster = new List<RadarPolygon>();
            sum = 0;
        }
        for (int i = 0; i < sorted.Count; i++)
        {
            RadarPolygon polygon = sorted[i];
            float center = cluster.Count == 0 ? polygon.Elevation : sum / cluster.Count;
            if (cluster.Count > 0 && polygon.Elevation - center > FloorBandThreshold) Flush();
            if (cluster.Count == 0) minY = maxY = polygon.Elevation;
            else { minY = MathF.Min(minY, polygon.Elevation); maxY = MathF.Max(maxY, polygon.Elevation); }
            cluster.Add(polygon);
            sum += polygon.Elevation;
        }
        Flush();

        if (sorted.Count == 0) return new RadarMapGeometry(Vector2.Zero, Vector2.One, floors);
        Vector2 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < sorted.Count; i++)
        {
            foreach (Vector2 point in sorted[i].Points)
            {
                min = Vector2.ComponentMin(min, point);
                max = Vector2.ComponentMax(max, point);
            }
        }
        if (max.X - min.X < .0001f) { min.X -= .5f; max.X += .5f; }
        if (max.Y - min.Y < .0001f) { min.Y -= .5f; max.Y += .5f; }
        return new RadarMapGeometry(min, max, floors);
    }

    private static void ExtractMph(MphCollisionInfo info, Vector3 translation, List<RadarPolygon> output)
    {
        for (int i = 0; i < info.Data.Count; i++)
        {
            CollisionData data = info.Data[i];
            if ((uint)data.PlaneIndex >= (uint)info.Planes.Count || data.PointIndexCount < 3
                || data.PointIndexCount > 64 || data.PointStartIndex > info.PointIndices.Count - data.PointIndexCount)
                continue;
            Vector4 plane = info.Planes[data.PlaneIndex];
            if (!Finite(plane) || plane.Y < FloorNormalThreshold) continue;
            var points = new Vector3[data.PointIndexCount];
            bool valid = true;
            for (int j = 0; j < points.Length; j++)
            {
                int pointIndex = info.PointIndices[data.PointStartIndex + j];
                if ((uint)pointIndex >= (uint)info.Points.Count || !Finite(info.Points[pointIndex])) { valid = false; break; }
                points[j] = info.Points[pointIndex] + translation;
            }
            if (valid) AddPolygon(points, plane.Y, output);
        }
    }

    private static void ExtractFh(FhCollisionInfo info, Vector3 translation, List<RadarPolygon> output)
    {
        for (int i = info.Portals.Count; i < info.Data.Count; i++)
        {
            FhCollisionData data = info.Data[i];
            if ((uint)data.PlaneIndex >= (uint)info.Planes.Count || data.VectorCount < 3
                || data.VectorCount > 64 || data.VectorStartIndex > info.Vectors.Count - data.VectorCount)
                continue;
            Vector4 plane = info.Planes[data.PlaneIndex];
            if (!Finite(plane) || plane.Y < FloorNormalThreshold) continue;
            var points = new Vector3[data.VectorCount];
            bool valid = true;
            for (int j = 0; j < points.Length; j++)
            {
                FhCollisionVector vector = info.Vectors[data.VectorStartIndex + j];
                int pointIndex = vector.Point2Index;
                if ((uint)pointIndex >= (uint)info.Points.Count || !Finite(info.Points[pointIndex])) { valid = false; break; }
                points[j] = info.Points[pointIndex] + translation;
            }
            if (valid) AddPolygon(points, plane.Y, output);
        }
    }

    private static void AddPolygon(Vector3[] source, float normalY, List<RadarPolygon> output)
    {
        var points = new Vector2[source.Length];
        float elevation = 0, twiceArea = 0;
        for (int i = 0; i < source.Length; i++)
        {
            points[i] = new Vector2(source[i].X, source[i].Z);
            elevation += source[i].Y;
            Vector3 next = source[(i + 1) % source.Length];
            twiceArea += source[i].X * next.Z - next.X * source[i].Z;
        }
        if (MathF.Abs(twiceArea) * .5f < MinimumArea) return;
        RadarSurfaceKind surface = normalY < .9f ? RadarSurfaceKind.Ramp : RadarSurfaceKind.Floor;
        output.Add(new RadarPolygon(points, elevation / source.Length, surface));
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static void AddList<T>(ref HashCode hash, IReadOnlyList<T> values)
    {
        hash.Add(values.Count);
        for (int i = 0; i < values.Count; i++) hash.Add(values[i]);
    }
}
