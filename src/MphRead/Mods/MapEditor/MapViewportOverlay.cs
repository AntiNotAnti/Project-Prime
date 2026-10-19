using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MphRead.Mods.MapEditor;

public enum MapViewportOverlayPrimitive { Lines, Points }

/// <summary>Immutable world-space authoring geometry consumed by the retained scene resource owner.</summary>
public sealed class MapViewportOverlayBatch
{
    public MapViewportOverlayPrimitive Primitive { get; }
    public IReadOnlyList<Vector3> Vertices { get; }
    public Vector4 Color { get; }
    public float Size { get; }
    public bool DepthTest { get; }
    public MapViewportOverlayBatch(MapViewportOverlayPrimitive primitive,Vector3[] vertices,Vector4 color,float size,bool depthTest)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if(!Enum.IsDefined(primitive) || primitive==MapViewportOverlayPrimitive.Lines && vertices.Length%2!=0
            || !float.IsFinite(size)||size<=0 || !Finite(color)
            || vertices.Any(v=>!float.IsFinite(v.X)||!float.IsFinite(v.Y)||!float.IsFinite(v.Z)))
            throw new ArgumentException("Overlay geometry must contain finite points, paired lines and a positive size.");
        Primitive=primitive;Vertices=Array.AsReadOnly((Vector3[])vertices.Clone());Color=color;Size=size;DepthTest=depthTest;
    }
    private static bool Finite(Vector4 c)=>float.IsFinite(c.X)&&float.IsFinite(c.Y)&&float.IsFinite(c.Z)&&float.IsFinite(c.W);
}

public sealed class MapViewportOverlay
{
    public static MapViewportOverlay Empty { get; }=new(Array.Empty<MapViewportOverlayBatch>());
    public IReadOnlyList<MapViewportOverlayBatch> Batches { get; }
    public bool Truncated { get; }
    public string Status => Truncated?"NATIVE OVERLAY LIMIT // SELECTION REMAINS COMPLETE":"";
    public MapViewportOverlay(IReadOnlyList<MapViewportOverlayBatch> batches,bool truncated=false)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if(batches.Any(batch=>batch==null))throw new ArgumentException("Overlay batches cannot contain null.",nameof(batches));
        Batches=Array.AsReadOnly(batches.ToArray());Truncated=truncated;
    }
}
