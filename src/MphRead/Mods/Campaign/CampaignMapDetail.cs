using System;
using System.Collections.Generic;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Campaign;

/// <summary>Detached map polygon; no collision table, Scene or mutable vertex array escapes.</summary>
public sealed class CampaignMapDetailPolygon
{
    public int SourceFace { get; }
    public Vector3 Normal { get; }
    public IReadOnlyList<Vector3> Vertices { get; }
    internal CampaignMapDetailPolygon(int sourceFace, Vector3 normal, Vector3[] vertices)
    { SourceFace=sourceFace;Normal=normal;Vertices=Array.AsReadOnly(vertices); }
}

public sealed class CampaignMapDetailResult
{
    public bool Ready { get; }
    public string Reason { get; }
    public IReadOnlyList<CampaignMapDetailPolygon> Polygons { get; }
    public int SkippedDegenerateTriangles { get; }
    internal CampaignMapDetailResult(bool ready,string reason,CampaignMapDetailPolygon[] polygons,int skippedDegenerateTriangles=0)
    { Ready=ready;Reason=reason;Polygons=Array.AsReadOnly(polygons);SkippedDegenerateTriangles=skippedDegenerateTriangles; }
}

/// <summary>
/// Bounded, synchronous conversion only. Callers provide stable collision tables;
/// this helper performs no IO, caching, visibility decisions, scene mutation or GL work.
/// Failed conversion publishes no partial geometry. Native rendering remains unchanged.
/// </summary>
public static class CampaignMapDetail
{
    public const int MaxPoints=65_536, MaxPlanes=65_536, MaxFaces=32_768;
    public const int MaxIndices=262_144, MaxOutputVertices=262_144, MaxFaceVertices=10;
    public const float MaxCoordinate=65_536f;

    public static float RoomHeightScale(string? roomName) =>
        String.Equals(roomName,"UNIT2_C2",StringComparison.OrdinalIgnoreCase) ? 2.05f :
        String.Equals(roomName,"UNIT2_C3",StringComparison.OrdinalIgnoreCase) ? 1.305f : 1f;

    /// <summary>
    /// The native marker's exact room-to-map order: scale local Y, add NAV origin
    /// (already including its area-half offset), subtract connector translation,
    /// then add the independently requested marker lift. Geometry normally uses zero lift.
    /// </summary>
    public static Vector3 RoomToMap(string? roomName,Vector3 point,Vector3 navOrigin,
        Vector3 connectorTranslation=default,float markerLift=0)
    {
        if(!Finite(point)||!Finite(navOrigin)||!Finite(connectorTranslation)||!Single.IsFinite(markerLift))
            throw new ArgumentException("Map transform contains a nonfinite value.");
        Vector3 result=new(point.X+navOrigin.X-connectorTranslation.X,
            point.Y*RoomHeightScale(roomName)+navOrigin.Y-connectorTranslation.Y+markerLift,
            point.Z+navOrigin.Z-connectorTranslation.Z);
        if(!Bounded(result))throw new ArgumentException("Map transform exceeds the coordinate budget.");
        return result;
    }

    public static CampaignMapDetailResult Build(MphCollisionInfoBase? source,string? roomName,
        Vector3 navOrigin,Vector3 connectorTranslation=default,int maximumOutputVertices=MaxOutputVertices)
    {
        CampaignMapDetailResult Refuse(string reason)=>new(false,reason,Array.Empty<CampaignMapDetailPolygon>());
        if(source==null)return Refuse("Collision source is missing.");
        if(maximumOutputVertices<1||maximumOutputVertices>MaxOutputVertices)return Refuse("Invalid output vertex budget.");
        if(source.Points.Count>MaxPoints||source.Planes.Count>MaxPlanes||source.RuntimeData.Count>MaxFaces
            ||source.RuntimePointIndices.Count>MaxIndices)return Refuse("Collision input exceeds the map-detail budget.");
        if(!Bounded(navOrigin)||!Bounded(connectorTranslation))return Refuse("Invalid map placement.");
        for(int i=0;i<source.Points.Count;i++)if(!Bounded(source.Points[i]))return Refuse("Collision point is nonfinite or unbounded.");
        for(int i=0;i<source.Planes.Count;i++)
        {
            Vector4 plane=source.Planes[i];
            if(!Finite(plane.Xyz)||!Single.IsFinite(plane.W)||MathF.Abs(plane.W)>MaxCoordinate
                ||plane.Xyz.LengthSquared>1e12f)
                return Refuse("Collision plane is invalid.");
        }
        for(int i=0;i<source.RuntimePointIndices.Count;i++)
            if((uint)source.RuntimePointIndices[i]>=(uint)source.Points.Count)return Refuse("Collision point index is out of range.");
        var result=new List<CampaignMapDetailPolygon>();
        long outputVertices=0;
        int skipped=0;
        for(int ordinal=0;ordinal<source.RuntimeData.Count;ordinal++)
        {
            CollisionFace face=source.RuntimeData[ordinal];
            if(face.PointIndexCount<0||face.PointIndexCount>MaxFaceVertices||face.PointStartIndex<0
                ||(long)face.PointStartIndex+face.PointIndexCount>source.RuntimePointIndices.Count
                ||(uint)face.PlaneIndex>=(uint)source.Planes.Count)return Refuse("Collision face span, count or plane index is invalid.");
            if(face.IgnorePlayers||face.PointIndexCount<3)continue;
            var points=new Vector3[face.PointIndexCount];
            for(int j=0;j<points.Length;j++)points[j]=source.Points[source.RuntimePointIndices[face.PointStartIndex+j]];
            try
            {
                for(int j=0;j<points.Length;j++)points[j]=RoomToMap(roomName,points[j],navOrigin,connectorTranslation);
            }
            catch(ArgumentException){return Refuse("Transformed map polygon exceeds the coordinate budget.");}
            // Match Scene.RenderNgon's existing native triangle fan exactly.
            // Retail collision includes warped/concave polygons and planes
            // which do not describe their render vertices. Never project,
            // reorder or repair those vertices against the physics planes.
            for(int j=1;j<points.Length-1;j++)
            {
                Vector3 normal=Vector3.Cross(points[j]-points[0],points[j+1]-points[0]);
                if(normal.LengthSquared==0) { skipped++;continue; }
                outputVertices+=3;
                if(outputVertices>maximumOutputVertices)return Refuse("Map-detail output exceeds the vertex budget.");
                normal.Normalize();
                result.Add(new(ordinal,normal,new[]{points[0],points[j],points[j+1]}));
            }
        }
        return result.Count==0 ? new(false,"No nondegenerate player collision triangles are available.",
            Array.Empty<CampaignMapDetailPolygon>(),skipped) : new(true,"",result.ToArray(),skipped);
    }

    private static bool Finite(Vector3 value)=>Single.IsFinite(value.X)&&Single.IsFinite(value.Y)&&Single.IsFinite(value.Z);
    private static bool Bounded(Vector3 value)=>Finite(value)&&MathF.Abs(value.X)<=MaxCoordinate
        &&MathF.Abs(value.Y)<=MaxCoordinate&&MathF.Abs(value.Z)<=MaxCoordinate;
}
