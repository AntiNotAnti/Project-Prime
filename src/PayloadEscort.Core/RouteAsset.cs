using System.Numerics;
using System.Text.Json;
namespace ProjectPrime.PayloadEscort;

/// <summary>Reads only the authored Escort sidecar. This is not a Map Studio schema extension.</summary>
public static class RouteAsset {
    public const int MaximumBytes=2*1024*1024;
    public static IReadOnlyList<FlankPath> ReadFlanks(ReadOnlyMemory<byte> bytes){
        _=Read(bytes); // Reuse bounds and identity validation before opening optional planning metadata.
        try{
            using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=24});
            if(!document.RootElement.TryGetProperty("flanks",out var items))return Array.Empty<FlankPath>();
            var result=new List<FlankPath>();
            foreach(var f in items.EnumerateArray()){
                var points=new List<Vector3>();
                foreach(var p in f.GetProperty("points").EnumerateArray()){
                    if(p.GetArrayLength()!=3)throw new ArgumentException("Flank point must be XYZ.");
                    points.Add(new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle()));
                }
                result.Add(new(f.GetProperty("name").GetString()??"",Array.AsReadOnly(points.ToArray())));
            }
            return result.AsReadOnly();
        }catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){throw new ArgumentException("Malformed flank metadata.",e);}
    }
    public static PayloadRoute Read(ReadOnlyMemory<byte> bytes){
        if(bytes.Length==0 || bytes.Length>MaximumBytes)throw new ArgumentException("Sidecar outside size budget.");
        try {
            using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=24});
            var root=document.RootElement;
            if(root.GetProperty("schema").GetInt32()!=1 || root.GetProperty("distanceScale").GetInt64()!=EscortRules.DistanceScale)
                throw new ArgumentException("Unsupported route schema or distance scale.");
            Guid mapId=root.GetProperty("mapId").GetGuid();
            var pointData=root.GetProperty("points");
            if(pointData.GetArrayLength()<2 || pointData.GetArrayLength()>65535)throw new ArgumentException("Invalid route sample count.");
            var points=new List<Vector3>();
            foreach(var p in pointData.EnumerateArray()){
                if(p.GetArrayLength()!=3)throw new ArgumentException("Route point must be XYZ.");
                points.Add(new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle()));
            }
            var checkpoints=new List<Checkpoint>();
            foreach(var c in root.GetProperty("checkpoints").EnumerateArray())
                checkpoints.Add(new(c.GetProperty("distance").GetInt64(),c.GetProperty("addedTicks").GetInt32(),c.GetProperty("holdTicks").GetInt32()));
            return new PayloadRoute(mapId,points,checkpoints);
        } catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){
            throw new ArgumentException("Malformed Escort route sidecar.",e);
        }
    }
}
