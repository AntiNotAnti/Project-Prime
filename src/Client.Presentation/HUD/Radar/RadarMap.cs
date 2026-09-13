using System;
using System.Collections.Generic;
using MphRead.Formats;

namespace MphRead.Hud.Radar;

public sealed class RadarMapFloor
{
    public RadarMapFloor(RadarFloorBand band, IReadOnlyList<ColorRgba> pixels)
    {
        Band = band;
        var copy = new ColorRgba[pixels.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = pixels[i];
        Pixels = Array.AsReadOnly(copy);
    }
    public RadarFloorBand Band { get; }
    public IReadOnlyList<ColorRgba> Pixels { get; }
}

public sealed class RadarMap
{
    public RadarMap(int width, int height, RadarMapGeometry geometry, IReadOnlyList<RadarMapFloor> floors)
    {
        Width = width;
        Height = height;
        Geometry = geometry;
        var copy = new RadarMapFloor[floors.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = floors[i];
        Floors = Array.AsReadOnly(copy);
    }
    public int Width { get; }
    public int Height { get; }
    public RadarMapGeometry Geometry { get; }
    public IReadOnlyList<RadarMapFloor> Floors { get; }
}
