using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapGen;
using ProjectPrime.PayloadEscort;
using OpenTK.Mathematics;
namespace MphRead.Mods.HeroModes;

/// <summary>The immutable map package owns the route, staged rooms, and cargo geometry together.</summary>
internal sealed class PayloadMap
{
    internal PayloadRoute Route { get; }
    internal MapDefinition Cargo { get; }
    internal MapDefinition CargoCollision { get; }
    internal record Room(string Id, float[] Min, float[] Max);
    internal record Stage(string Attacker, string Defender);
    internal Room[] Rooms { get; }
    internal Stage[] Stages { get; }
    internal FlankPath[] Flanks { get; }
    private static readonly ConditionalWeakTable<MapDefinition, PayloadMap> Cache = new();
    private PayloadMap(MapDefinition definition)
    {
        var json=definition.PayloadEscort ?? throw new InvalidDataException("Push the Payload requires an authored payload route.");
        byte[] bytes=Encoding.UTF8.GetBytes(json.GetRawText());
        Route=RouteAsset.Read(bytes);
        if(Route.MapId!=definition.MapId) throw new InvalidDataException("Payload route does not belong to this map.");
        Flanks=json.TryGetProperty("flanks",out var flanks)?flanks.EnumerateArray().Select(f=>new FlankPath(
            f.GetProperty("name").GetString()!,f.GetProperty("points").EnumerateArray().Select(p=>{var a=p.EnumerateArray().Select(x=>x.GetSingle()).ToArray();if(a.Length!=3)throw new InvalidDataException("Invalid flank point.");return new System.Numerics.Vector3(a[0],a[1],a[2]);}).ToArray())).ToArray():Array.Empty<FlankPath>();
        _=new BotFlankPlanner(Route,Flanks);
        Rooms=json.GetProperty("rooms").EnumerateArray().Select(v=>new Room(v.GetProperty("id").GetString()!,Vec(v,"boundsMin"),Vec(v,"boundsMax"))).ToArray();
        if(Rooms.Length is <2 or >16 || Rooms.Any(v=>string.IsNullOrWhiteSpace(v.Id) || !new CtzSpawnRoomDefinition{Min=v.Min,Max=v.Max,Side=0}.Valid)
            || Rooms.Select(v=>v.Id).Distinct(StringComparer.Ordinal).Count()!=Rooms.Length) throw new InvalidDataException("Invalid payload spawn rooms.");
        Stages=json.GetProperty("stages").EnumerateArray().Select(v=>new Stage(v.GetProperty("attackerRoom").GetString()!,v.GetProperty("defenderRoom").GetString()!)).ToArray();
        if(Stages.Length!=3 || Stages.Any(v=>v.Attacker==v.Defender || !Rooms.Any(x=>x.Id==v.Attacker) || !Rooms.Any(x=>x.Id==v.Defender)))
            throw new InvalidDataException("Payload stages must name separate, existing team rooms.");
        foreach(var room in Rooms)
            if(definition.Spawns.Count(s=>s.Position?.Length==3 && Contains(room,new(s.Position[0],s.Position[1],s.Position[2])))<4)
                throw new InvalidDataException("Every payload stage room needs four valid spawn candidates.");
        if(!json.TryGetProperty("cargo",out var cargo)) throw new InvalidDataException("Payload cargo geometry is missing.");
        Cargo=JsonSerializer.Deserialize<MapDefinition>(cargo.GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ?? throw new InvalidDataException("Invalid payload cargo.");
        if(Cargo.Geometry.Count is <1 or >64 || Cargo.Materials.Count is <1 or >16) throw new InvalidDataException("Cargo geometry exceeds its budget.");
        if(!json.TryGetProperty("cargoCollision",out var collision))throw new InvalidDataException("Payload compound collision is missing.");
        var boxes=collision.GetProperty("boxes").EnumerateArray().Select(b=>new MapBrush {Label=b.GetProperty("label").GetString(),Min=Vec(b,"min"),Max=Vec(b,"max"),Solid=true}).ToList();
        if(boxes.Count is <1 or >16 || boxes.Any(b=>Enumerable.Range(0,3).Any(i=>b.Min[i]>=b.Max[i] || Math.Abs(b.Min[i])>8 || Math.Abs(b.Max[i])>8)))throw new InvalidDataException("Invalid payload collision boxes.");
        CargoCollision=new MapDefinition {Brushes=boxes,Materials=Cargo.Materials};
        Cargo.BaseDirectory=definition.BaseDirectory;Cargo.BundlePath=definition.BundlePath;Cargo.SourcePath=definition.SourcePath;
    }
    private static float[] Vec(JsonElement value,string name)
    {
        float[] result=value.GetProperty(name).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        if(result.Length!=3 || result.Any(x=>!float.IsFinite(x))) throw new InvalidDataException("Invalid payload room bounds.");
        return result;
    }
    internal static bool Contains(Room r,Vector3 p)=>p.X>=r.Min[0]&&p.X<=r.Max[0]&&p.Y>=r.Min[1]&&p.Y<=r.Max[1]&&p.Z>=r.Min[2]&&p.Z<=r.Max[2];
    internal static PayloadMap Get(MapDefinition definition)=>Cache.GetValue(definition,d=>new PayloadMap(d));
    internal static string? Error(MapDefinition? definition)
    {
        if(definition==null)return "Push the Payload requires a payload map.";
        try { _=Get(definition);return null; }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException or IOException or OverflowException)
        {return e.Message;}
    }
    internal Room ForTeam(int stage,int attackingTeam,int team)
    {
        var s=Stages[Math.Clamp(stage,0,2)];string id=team==attackingTeam?s.Attacker:s.Defender;
        return Rooms.First(r=>r.Id==id);
    }
}
