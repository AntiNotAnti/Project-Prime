using System.Numerics;
namespace ProjectPrime.PayloadEscort;

/// <summary>Immutable sampled authoring curve, evaluated by arc length, never point index.</summary>
public sealed class PayloadRoute {
    private readonly Vector3[] _points;
    private readonly double[] _cumulative;
    public long Length { get; }
    public Guid MapId { get; }
    public IReadOnlyList<Vector3> Points { get; }
    public IReadOnlyList<Checkpoint> Checkpoints { get; }
    public PayloadRoute(Guid mapId, IEnumerable<Vector3> points, IEnumerable<Checkpoint> checkpoints) {
        MapId=mapId;_points=points.ToArray();
        if(mapId==Guid.Empty || _points.Length<2 || _points.Length>65535)throw new ArgumentException("Invalid route identity or count.");
        _cumulative=new double[_points.Length];
        for(int i=0;i<_points.Length;i++){
            Vector3 p=_points[i];if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))throw new ArgumentException("Non-finite route point.");
            if(i==0)continue;
            double length=Vector3.Distance(_points[i-1],p);
            if(length<0.000001)throw new ArgumentException("Coincident route samples.");
            _cumulative[i]=_cumulative[i-1]+length;
        }
        Length=checked((long)Math.Round(_cumulative[^1]*EscortRules.DistanceScale));
        if(Length<=0 || Length>10_000*EscortRules.DistanceScale)throw new ArgumentException("Route outside bounded world budget.");
        var cps=checkpoints.ToArray();
        if(cps.Length!=2 || cps[0].Distance<=0 || cps[1].Distance<=cps[0].Distance || cps[1].Distance>=Length
            || cps.Any(c=>c.AddedTicks<0||c.AddedTicks>3600*60||c.HoldTicks<0||c.HoldTicks>36000))throw new ArgumentException("Escort requires two ordered intermediate checkpoints.");
        Points=Array.AsReadOnly(_points);Checkpoints=Array.AsReadOnly(cps);
    }
    // Navigation helper only. Never use client projection as authoritative payload progress.
    public long Project(Vector3 point) {
        if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z))throw new ArgumentException("Non-finite projection.");
        double best=double.PositiveInfinity,along=0;
        for(int i=0;i<_points.Length-1;i++){
            Vector3 segment=_points[i+1]-_points[i];
            float t=Math.Clamp(Vector3.Dot(point-_points[i],segment)/segment.LengthSquared(),0,1);
            double distance=Vector3.DistanceSquared(point,_points[i]+segment*t);
            if(distance<best){best=distance;along=_cumulative[i]+t*(_cumulative[i+1]-_cumulative[i]);}
        }
        return Math.Clamp((long)Math.Round(along*EscortRules.DistanceScale),0,Length);
    }
    public (Vector3 Position,Vector3 Forward) Pose(long distance) {
        double d=Math.Clamp(distance,0,Length)/(double)EscortRules.DistanceScale;
        int index=Array.BinarySearch(_cumulative,d);
        if(index<0)index=~index-1;
        index=Math.Clamp(index,0,_points.Length-2);
        double span=_cumulative[index+1]-_cumulative[index];
        float t=(float)Math.Clamp((d-_cumulative[index])/span,0,1);
        return(Vector3.Lerp(_points[index],_points[index+1],t),Vector3.Normalize(_points[index+1]-_points[index]));
    }
}
