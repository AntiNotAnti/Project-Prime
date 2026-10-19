using System.Numerics;
using MphRead.Mods.MapEditor;

internal static class StudioModelingOverlayChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var source=new[]{Vector3.Zero,Vector3.One};var batch=new MapViewportOverlayBatch(MapViewportOverlayPrimitive.Lines,source,Vector4.One,3,false);
        source[0]=new(99,99,99);check(batch.Vertices[0]==Vector3.Zero,"overlay vertices detach from caller mutable arrays");
        var batches=new[]{batch};var overlay=new MapViewportOverlay(batches,true);batches[0]=new(MapViewportOverlayPrimitive.Points,new[]{Vector3.Zero},Vector4.One,1,true);
        check(ReferenceEquals(overlay.Batches[0],batch)&&overlay.Truncated&&overlay.Status.Length>0,"overlay batches detach and truncated rendering is disclosed");
        check(MapViewportOverlay.Empty.Batches.Count==0&&!MapViewportOverlay.Empty.Truncated,"empty overlay is explicit and reusable");
        foreach(var invalid in new Action[]{()=>new MapViewportOverlayBatch(MapViewportOverlayPrimitive.Lines,new[]{Vector3.Zero},Vector4.One,1,false),
            ()=>new MapViewportOverlayBatch((MapViewportOverlayPrimitive)8,Array.Empty<Vector3>(),Vector4.One,1,false),
            ()=>new MapViewportOverlayBatch(MapViewportOverlayPrimitive.Points,new[]{new Vector3(float.NaN,0,0)},Vector4.One,1,false),
            ()=>new MapViewportOverlayBatch(MapViewportOverlayPrimitive.Points,new[]{Vector3.Zero},new(float.PositiveInfinity,0,0,1),1,false),
            ()=>new MapViewportOverlayBatch(MapViewportOverlayPrimitive.Points,new[]{Vector3.Zero},Vector4.One,float.NaN,false),
            ()=>new MapViewportOverlayBatch(MapViewportOverlayPrimitive.Points,new[]{Vector3.Zero},Vector4.One,0,false),
            ()=>new MapViewportOverlay(new MapViewportOverlayBatch[]{null!})})
        {
            bool refused=false;try{invalid();}catch(ArgumentException){refused=true;}
            check(refused,"overlay rejects malformed or nonfinite presentation data before renderer submission");
        }
    }
}
