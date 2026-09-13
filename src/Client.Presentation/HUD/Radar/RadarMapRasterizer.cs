using System;
using System.Collections.Generic;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public enum RadarMapRasterLayers { Fill, Outline, Both }

public static class RadarMapRasterizer
{
    public static RadarMap Rasterize(RadarMapGeometry geometry, int resolution = 256,
        RadarMapRasterLayers layers = RadarMapRasterLayers.Both)
    {
        if (geometry == null) throw new ArgumentNullException(nameof(geometry));
        if (resolution < 8 || resolution > 2048) throw new ArgumentOutOfRangeException(nameof(resolution));
        var floors = new RadarMapFloor[geometry.Floors.Count];
        for (int i = 0; i < floors.Length; i++)
        {
            RadarFloorBand band = geometry.Floors[i];
            var pixels = new ColorRgba[checked(resolution * resolution)];
            Array.Fill(pixels, RadarPalette.Transparent);
            for (int j = 0; j < band.Polygons.Count; j++)
            {
                RadarPolygon polygon = band.Polygons[j];
                if (layers is RadarMapRasterLayers.Fill or RadarMapRasterLayers.Both)
                    Fill(pixels, resolution, geometry, polygon,
                        polygon.Surface == RadarSurfaceKind.Ramp
                            ? new ColorRgba(255, 255, 255, 235) : new ColorRgba(255, 255, 255, 225));
                if (layers is RadarMapRasterLayers.Outline or RadarMapRasterLayers.Both)
                    Outline(pixels, resolution, geometry, polygon, new ColorRgba(255, 255, 255, 255));
            }
            // The empty outer texel prevents sampling a visible edge when a
            // rotated cropped window reaches the full-room boundary.
            for (int x = 0; x < resolution; x++)
            {
                pixels[x] = RadarPalette.Transparent;
                pixels[(resolution - 1) * resolution + x] = RadarPalette.Transparent;
            }
            for (int y = 0; y < resolution; y++)
            {
                pixels[y * resolution] = RadarPalette.Transparent;
                pixels[y * resolution + resolution - 1] = RadarPalette.Transparent;
            }
            floors[i] = new RadarMapFloor(band, pixels);
        }
        return new RadarMap(resolution, resolution, geometry, floors);
    }

    private static Vector2 Pixel(RadarMapGeometry geometry, Vector2 world, int resolution)
    {
        Vector2 size = geometry.WorldSize;
        float u = size.X > .0001f ? (world.X - geometry.Min.X) / size.X : .5f;
        float v = size.Y > .0001f ? (world.Y - geometry.Min.Y) / size.Y : .5f;
        return new Vector2(u * (resolution - 1), v * (resolution - 1));
    }

    private static void Fill(ColorRgba[] pixels, int resolution, RadarMapGeometry geometry,
        RadarPolygon polygon, ColorRgba color)
    {
        var points = new Vector2[polygon.Points.Count];
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = Pixel(geometry, polygon.Points[i], resolution);
            minY = MathF.Min(minY, points[i].Y);
            maxY = MathF.Max(maxY, points[i].Y);
        }
        int firstY = Math.Clamp((int)MathF.Floor(minY), 1, resolution - 2);
        int lastY = Math.Clamp((int)MathF.Ceiling(maxY), 1, resolution - 2);
        var intersections = new float[points.Length];
        for (int y = firstY; y <= lastY; y++)
        {
            float sample = y + .5f;
            int count = 0;
            for (int i = 0, previous = points.Length - 1; i < points.Length; previous = i++)
            {
                Vector2 a = points[previous], b = points[i];
                if ((a.Y > sample) == (b.Y > sample)) continue;
                intersections[count++] = a.X + (sample - a.Y) * (b.X - a.X) / (b.Y - a.Y);
            }
            Array.Sort(intersections, 0, count);
            for (int i = 0; i + 1 < count; i += 2)
            {
                int left = Math.Clamp((int)MathF.Ceiling(intersections[i]), 1, resolution - 2);
                int right = Math.Clamp((int)MathF.Floor(intersections[i + 1]), 1, resolution - 2);
                for (int x = left; x <= right; x++) pixels[y * resolution + x] = color;
            }
        }
    }

    private static void Outline(ColorRgba[] pixels, int resolution, RadarMapGeometry geometry,
        RadarPolygon polygon, ColorRgba color)
    {
        for (int i = 0; i < polygon.Points.Count; i++)
        {
            Vector2 a = Pixel(geometry, polygon.Points[i], resolution);
            Vector2 b = Pixel(geometry, polygon.Points[(i + 1) % polygon.Points.Count], resolution);
            int x0 = (int)MathF.Round(a.X), y0 = (int)MathF.Round(a.Y);
            int x1 = (int)MathF.Round(b.X), y1 = (int)MathF.Round(b.Y);
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                if (x0 > 0 && x0 < resolution - 1 && y0 > 0 && y0 < resolution - 1)
                    pixels[y0 * resolution + x0] = color;
                if (x0 == x1 && y0 == y1) break;
                int twice = error * 2;
                if (twice >= dy) { error += dy; x0 += sx; }
                if (twice <= dx) { error += dx; y0 += sy; }
            }
        }
    }
}
