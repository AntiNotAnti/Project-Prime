#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

public enum TheatreSpatialKind { Movement, Deaths, Kills, Damage, Weapons, Spawns, Objectives, Pickups, Engagements, SpawnPressure, ObjectivePresence, ImperialistSightlines, PickupRoutes }
public sealed record TheatreSpatialDirection(float X,float Y,float Z);
public sealed record TheatreSpatialSample(uint Frame,int Slot,float X,float Y,float Z,TheatreSpatialKind Kind,float Weight,TheatreSpatialDirection? Direction=null);
public sealed record TheatreSpatialAnalysis(uint Duration,ImmutableArray<TheatreSpatialSample> Samples);
public sealed record TheatreSpatialGrid(ImmutableArray<int> Levels,int Samples,float Weight,float MinX,float MaxX,float MinZ,float MaxZ);

/// <summary>Pure top-down projection of recorded facts; the result never changes playback.</summary>
public static class TheatreSpatialProjection
{
    public const int Size=16;
    public static bool WithinWindow(uint frame,uint origin,uint duration)=>frame>=origin&&frame-origin<=duration;
    public static ImmutableArray<TheatreSpatialSample> Filter(TheatreSpatialAnalysis analysis,TheatreSpatialKind kind,int slot,uint start,uint end)
    {
        if(start>end||end>analysis.Duration)throw new ArgumentException("Choose a time range inside this recording.");
        return analysis.Samples.Where(p=>p.Kind==kind&&(slot<0||p.Slot==slot)&&p.Frame>=start&&p.Frame<=end
            &&float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)&&float.IsFinite(p.Weight)&&p.Weight>0).ToImmutableArray();
    }
    public static ImmutableArray<TheatreSpatialSample> WorldMarkers(ImmutableArray<TheatreSpatialSample> filtered)
        =>filtered.OrderByDescending(p=>p.Weight).Take(64).ToImmutableArray();
    public static TheatreSpatialGrid Project(TheatreSpatialAnalysis analysis,TheatreSpatialKind kind,int slot,uint start,uint end)
    {
        if(start>end||end>analysis.Duration)throw new ArgumentException("Choose a time range inside this recording.");
        var all=analysis.Samples.Where(p=>float.IsFinite(p.X)&&float.IsFinite(p.Z)&&float.IsFinite(p.Weight)&&p.Weight>0).ToArray();
        float minX=all.Length==0?0:all.Min(p=>p.X),maxX=all.Length==0?1:all.Max(p=>p.X);
        float minZ=all.Length==0?0:all.Min(p=>p.Z),maxZ=all.Length==0?1:all.Max(p=>p.Z);
        var weights=new float[Size*Size];int count=0;float total=0;
        // Bounds stay fixed across filters so switching players or event types does not move the map.
        float span=Math.Max(1,Math.Max(maxX-minX,maxZ-minZ));
        foreach(var p in all)
        {
            if(p.Kind!=kind||(slot>=0&&p.Slot!=slot)||p.Frame<start||p.Frame>end)continue;
            int x=Math.Clamp((int)((p.X-minX)/span*(Size-1)),0,Size-1);
            int z=Math.Clamp((int)((p.Z-minZ)/span*(Size-1)),0,Size-1);
            weights[z*Size+x]+=p.Weight;count++;total+=p.Weight;
        }
        float maximum=weights.Max();
        return new(weights.Select(w=>w<=0?0:Math.Clamp((int)Math.Ceiling(w/maximum*4),1,4)).ToImmutableArray(),count,total,minX,maxX,minZ,maxZ);
    }
}
public interface ITheatreSpatialBackend
{
    Task<TheatreSpatialAnalysis> AnalyzeSpatial(TheatreEditorContext context,CancellationToken cancellation);
    void ValidateSpatial(TheatreEditorContext context);
}
#endif
