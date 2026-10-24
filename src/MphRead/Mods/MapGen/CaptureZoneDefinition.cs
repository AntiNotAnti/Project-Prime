using System;
using System.Linq;
using OpenTK.Mathematics;
using MphRead.Mods.HeroModes;
namespace MphRead.Mods.MapGen;
public sealed class CaptureZoneDefinition
{
    public System.Collections.Generic.List<CtzSpawnRoomDefinition> SpawnRooms { get; set; } = new();
    public float[] Min { get; set; } = [-8, -.1f, -8];
    public float[] Max { get; set; } = [8, 5, 8];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Valid => SpawnRooms != null && SpawnRooms.Count <= 2 && SpawnRooms.All(r => r.Valid)
        && SpawnRooms.Select(r => r.Side).Distinct().Count() == SpawnRooms.Count && Min?.Length == 3 && Max?.Length == 3
        && Enumerable.Range(0,3).All(i => float.IsFinite(Min[i]) && float.IsFinite(Max[i]) && Min[i] < Max[i]);
    public bool Contains(Vector3 p) => Valid && CtzRules.Finite(p) && p.X >= Min[0] && p.Y >= Min[1] && p.Z >= Min[2]
        && p.X <= Max[0] && p.Y <= Max[1] && p.Z <= Max[2];
}
