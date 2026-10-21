#if !MPHREAD_SERVER
using System;
using MphRead.CorpsePhysics;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;
using NV=System.Numerics.Vector3;
using NP=System.Numerics.Vector4;

namespace MphRead.Mods.Render;

// Engine-side read-only source fence. Backend receives only the copied World.
// Stock native runtime tables are immutable after loading; editor/side/custom
// rooms and selection transforms are excluded rather than assuming a revision.
internal sealed class CorpseStaticSnapshot
{
    private readonly record struct Chunk(CollisionInstance Instance,CollisionInfo Info,bool Active,string? Connector,
        Vector3 Translation,object Points,object Planes,object Indices,object Faces,int PointCount,int PlaneCount,int IndexCount,int FaceCount);
    private readonly RoomEntity _room;
    private readonly string _name;
    private readonly int _roomId;
    private readonly Chunk[] _chunks;
    internal readonly NV Center;
    internal readonly float Extent;
    internal readonly CorpseStaticWorld World;
    private CorpseStaticSnapshot(RoomEntity room,Chunk[] chunks,NV center,float extent,CorpseStaticWorld world)
    {_room=room;_roomId=room.RoomId;_name=room.Meta.Name;_chunks=chunks;Center=center;Extent=extent;World=world;}
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private static bool Overlap(Vector3 center,float radius,NV target,float extent)
        =>!Finite(center)||!float.IsFinite(radius)||radius<0
            ||MathF.Abs(center.X-target.X)<=extent+radius&&MathF.Abs(center.Y-target.Y)<=extent+radius&&MathF.Abs(center.Z-target.Z)<=extent+radius;
    private static Matrix4 HeaderTransform(EntityDataHeader h)
    {
        var result=Matrix4.CreateScale(Vector3.One)*EntityBase.GetTransformMatrix(h.FacingVector.ToFloatVector(),h.UpVector.ToFloatVector());
        result.Row3.Xyz=h.Position.ToFloatVector();return result;
    }
    private static bool FieldUnchanged(ForceFieldEntity field)
    {
        var h=field.Data.Header;var expected=HeaderTransform(h);var target=new Vector3(field.Data.Width.FloatValue,field.Data.Height.FloatValue,1);
        if(expected.ExtractScale()!=target)
        {
            expected.ExtractRotation().ToEulerAngles(out var rotation);
            expected=Matrix4.CreateScale(target)*Matrix4.CreateRotationZ(rotation.Z)*Matrix4.CreateRotationY(rotation.Y)*Matrix4.CreateRotationX(rotation.X);
            expected.Row3.Xyz=h.Position.ToFloatVector();
        }
        var facing=h.FacingVector.ToFloatVector();
        return field.Transform==expected&&field.Position==h.Position.ToFloatVector()&&field.FieldFacingVector==facing
            &&field.FieldUpVector==h.UpVector.ToFloatVector()&&field.Plane==new Vector4(facing,Vector3.Dot(facing,h.Position.ToFloatVector()));
    }
    private static bool Unsupported(Scene scene,NV center,float extent)
    {
        // No interpolated/swept moving-collider history is qualified. Refuse a
        // room containing any active nonplayer entity mesh, even when distant;
        // endpoint-only proximity could miss its travel through this region.
        foreach(var entity in scene.Entities)
        {
            if(entity is PlayerEntity)continue; // Living gameplay never collides with this visual world.
            foreach(var col in entity.EntityCollision)
            {
                if(col?.Collision is {Active:true})return true;
            }
        }
        foreach(var door in scene.GetDoorEntities())
            if(!door.Flags.TestFlag(DoorFlags.Open)&&!door.ConnectorInactive
                &&(door.Transform!=HeaderTransform(door.Data.Header)||door.Position!=door.Data.Header.Position.ToFloatVector()
                    ||!float.IsFinite(door.Radius)||door.Radius<0||Overlap(door.LockPosition,door.Radius+1.25f,center,extent)))return true;
        foreach(var field in scene.GetForceFieldEntities())
        {
            if(!field.Active)continue; // Typed property intentionally hides EntityBase.Active.
            if(!FieldUnchanged(field)||!Finite(field.FieldRightVector)||!Finite(field.FieldUpVector)||!Finite(field.FieldFacingVector)
                ||field.FieldRightVector.LengthSquared is < .9f or > 1.1f||field.FieldUpVector.LengthSquared is < .9f or > 1.1f
                ||field.FieldFacingVector.LengthSquared is < .9f or > 1.1f
                ||!float.IsFinite(field.Width)||!float.IsFinite(field.Height)||field.Width<=0||field.Height<=0)return true;
            float radius=field.FieldRightVector.Length*field.Width+field.FieldUpVector.Length*field.Height
                +field.FieldFacingVector.Length;
            if(Overlap(field.Position,radius,center,extent))return true;
        }
        return false;
    }
    internal static bool SceneAllowed(Scene scene)=>!scene.Exiting&&!scene.SideScene&&!scene.IsReplayLab
        &&scene.Services.AllowsPresentationSideEffects&&!scene.Services.IsReplica&&!scene.GameState.InRoomTransition
        &&scene.Room is {TransitionWorkerPending:false}
        &&!scene.TransformRoomNodes&&Selection.Entity==null&&Selection.Instance==null&&Selection.Node==null&&Selection.Mesh==null;
    internal static bool TryCapture(Scene scene,NV center,float extent,out CorpseStaticSnapshot? result)
    {
        result=null;if(!SceneAllowed(scene)||!StockRiftlinePickups.Supports(scene.Room!.Meta.Name)||Unsupported(scene,center,extent))return false;
        var room=scene.Room!;var source=room.RoomCollision;
        if(source.Count is < 1 or > 64)return false;
        var chunks=new Chunk[source.Count];var builder=new CorpseStaticMeshBuilder(center,extent);
        Span<NV> points=stackalloc NV[CorpseStaticMeshBuilder.MaximumFaceVertices];
        for(int i=0;i<source.Count;i++)
        {
            var c=source[i];
            if(c.Info is not MphCollisionInfoBase m||c.IsEntity||!Finite(c.Translation)
                ||m.Points.Count>65536||m.Planes.Count>65536||m.RuntimePointIndices.Count>262144)return false;
            chunks[i]=new(c,m,c.Active,c.ConnectorName,c.Translation,m.Points,m.Planes,m.RuntimePointIndices,m.RuntimeData,
                m.Points.Count,m.Planes.Count,m.RuntimePointIndices.Count,m.RuntimeData.Count);
            if(!c.Active)continue;
            foreach(var face in m.RuntimeData)
            {
                if(face.IgnorePlayers)
                {if(builder.TryAddFace(default,default,true)==CorpseSurfaceResult.Rejected)return false;continue;}
                long end=(long)face.PointStartIndex+face.PointIndexCount;
                if(face.PointIndexCount is < 3 or > CorpseStaticMeshBuilder.MaximumFaceVertices||face.PointStartIndex<0
                    ||end>m.RuntimePointIndices.Count||face.PlaneIndex<0||face.PlaneIndex>=m.Planes.Count)return false;
                for(int p=0;p<face.PointIndexCount;p++)
                {
                    int index=m.RuntimePointIndices[face.PointStartIndex+p];if((uint)index>=(uint)m.Points.Count)return false;
                    points[p]=CorpseMatrixConversion.Copy(m.Points[index]+c.Translation);
                }
                var plane=m.Planes[face.PlaneIndex];float distance=plane.W+Vector3.Dot(plane.Xyz,c.Translation);
                if(builder.TryAddFace(points[..face.PointIndexCount],new NP(plane.X,plane.Y,plane.Z,distance))==CorpseSurfaceResult.Rejected)return false;
            }
        }
        if(!builder.TryBuild(out var world))return false;
        var snapshot=new CorpseStaticSnapshot(room,chunks,center,extent,world!);
        if(!snapshot.Matches(scene))return false;result=snapshot;return true;
    }
    private static bool Exact(Vector3 a,Vector3 b)=>BitConverter.SingleToInt32Bits(a.X)==BitConverter.SingleToInt32Bits(b.X)
        &&BitConverter.SingleToInt32Bits(a.Y)==BitConverter.SingleToInt32Bits(b.Y)&&BitConverter.SingleToInt32Bits(a.Z)==BitConverter.SingleToInt32Bits(b.Z);
    internal bool Matches(Scene scene)
    {
        if(!SceneAllowed(scene)||!ReferenceEquals(scene.Room,_room)||_room.RoomId!=_roomId||_room.Meta.Name!=_name
            ||_room.RoomCollision.Count!=_chunks.Length||Unsupported(scene,Center,Extent))return false;
        for(int i=0;i<_chunks.Length;i++)
        {
            var c=_room.RoomCollision[i];var s=_chunks[i];
            if(!ReferenceEquals(c,s.Instance)||!ReferenceEquals(c.Info,s.Info)||c.Active!=s.Active||c.ConnectorName!=s.Connector
                ||!Exact(c.Translation,s.Translation)||c.Info is not MphCollisionInfoBase m
                ||!ReferenceEquals(m.Points,s.Points)||!ReferenceEquals(m.Planes,s.Planes)||!ReferenceEquals(m.RuntimePointIndices,s.Indices)
                ||!ReferenceEquals(m.RuntimeData,s.Faces)||m.Points.Count!=s.PointCount||m.Planes.Count!=s.PlaneCount
                ||m.RuntimePointIndices.Count!=s.IndexCount||m.RuntimeData.Count!=s.FaceCount)return false;
        }
        return true;
    }
    internal bool Contains(ReadOnlySpan<CorpsePose> bodies,CorpseRig rig)
    {
        if(bodies.Length!=15)return false;
        for(int i=0;i<15;i++)
        {
            var p=bodies[i];float radius=rig.Parts[i].Size.Length()*.5f+.02f;var d=NV.Abs(p.Position-Center);
            if(!p.IsValid||d.X+radius>Extent||d.Y+radius>Extent||d.Z+radius>Extent)return false;
        }
        return true;
    }
}
#endif
