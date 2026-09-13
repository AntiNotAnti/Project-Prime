using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public static class RadarPalette
{
    public static readonly ColorRgba Transparent = new(0, 0, 0, 0);
    public static readonly ColorRgba Floor = new(64, 105, 132, 225);
    public static readonly ColorRgba Ramp = new(82, 132, 158, 235);
    public static readonly ColorRgba Outline = new(132, 190, 214, 255);
    public static readonly Vector4 Background = new(.015f, .035f, .055f, .62f);
    public static readonly Vector4 Border = new(.42f, .72f, .82f, .9f);
    public static readonly Vector4 Ring = new(.25f, .48f, .58f, .55f);
    public static readonly Vector4 Player = new(.88f, 1f, 1f, 1f);
    public static readonly Vector4 Enemy = new(1f, .25f, .22f, 1f);
    public static readonly Vector4 Teammate = new(.25f, .8f, 1f, 1f);
    public static readonly Vector4 Objective = new(1f, .78f, .2f, 1f);
    public static readonly Vector4 PrimeHunter = new(1f, .55f, .1f, 1f);
}
