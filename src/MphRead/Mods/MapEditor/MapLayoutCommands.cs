using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public static class MapLayoutCommands
{
    private static MapObject[] Selected(MapDefinition definition, ISet<Guid> ids)
        => MapObjects.All(definition).Where(o => ids.Contains(o.Id) && o.Value is not MapGeometry { Locked: true }).ToArray();
    private static (float Min, float Max) Bounds(MapObject item, int axis)
    {
        if (item.Value is MapBrush box) return (box.Min[axis], box.Max[axis]);
        if (item.Value is MapGeometry geometry)
        {
            var points = GeometryCompiler.Compile(geometry, 1).SelectMany(f => f.Points).ToArray();
            if (points.Length > 0) return (points.Min(p => p[axis]), points.Max(p => p[axis]));
        }
        return (item.Position[axis], item.Position[axis]);
    }
    public static void Align(MapDefinition definition, ISet<Guid> ids, int axis, int edge)
    {
        if (axis is < 0 or > 2 || edge is < -1 or > 1) throw new ArgumentOutOfRangeException();
        var items = Selected(definition, ids);
        if (items.Length < 2) return;
        var bounds = items.Select(o => Bounds(o, axis)).ToArray();
        float target = edge < 0 ? bounds.Min(b => b.Min) : edge > 0 ? bounds.Max(b => b.Max)
            : (bounds.Min(b => b.Min) + bounds.Max(b => b.Max)) / 2;
        for (int i = 0; i < items.Length; i++)
        {
            var delta = new float[3];
            delta[axis] = target - (edge < 0 ? bounds[i].Min : edge > 0 ? bounds[i].Max : (bounds[i].Min + bounds[i].Max) / 2);
            items[i].Move(delta);
        }
    }
    public static void Distribute(MapDefinition definition, ISet<Guid> ids, int axis)
    {
        if (axis is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(axis));
        var items = Selected(definition, ids).OrderBy(o => o.Position[axis]).ToArray();
        if (items.Length < 3) return;
        float first = items[0].Position[axis], step = (items[^1].Position[axis] - first) / (items.Length - 1);
        for (int i = 1; i < items.Length - 1; i++)
        { var delta = new float[3]; delta[axis] = first + step * i - items[i].Position[axis]; items[i].Move(delta); }
    }
    public static void Snap(MapDefinition definition, ISet<Guid> ids, float grid)
    {
        if (!float.IsFinite(grid) || grid <= 0) throw new ArgumentOutOfRangeException(nameof(grid));
        foreach (var item in Selected(definition, ids))
            item.Move(item.Position.Select(p => MathF.Round(p / grid) * grid - p).ToArray());
    }
    public static void SnapToFloor(MapDefinition definition, ISet<Guid> ids, IReadOnlyList<MapViewportFace> faces)
        => SnapToFloor(definition, ids, _ => faces);

    public static void SnapToFloor(MapDefinition definition, ISet<Guid> ids,
        Func<Vector3,IReadOnlyList<MapViewportFace>> candidates)
    {
        foreach (var item in Selected(definition, ids))
        {
            float bottom = Bounds(item, 1).Min;
            var point = new Vector3(item.Position[0], bottom + .05f, item.Position[2]);
            float? floor = FloorBelow(point, candidates(point), ids);
            if (floor.HasValue) item.Move(new[] { 0f, floor.Value - bottom, 0f });
        }
    }
    public readonly record struct SurfaceContact(Vector3 Point,Vector3 Normal,Guid ObjectId,float Distance);

    public static SurfaceContact? NearestSurface(Vector3 point,IReadOnlyList<MapViewportFace> faces,
        ISet<Guid>? excluded=null)
    {
        SurfaceContact? best=null;
        foreach(var face in faces)
        {
            if(!face.Solid||excluded?.Contains(face.ObjectId)==true||face.Points.Length<3)continue;
            Vector3 a=face.Points[0];
            for(int i=1;i<face.Points.Length-1;i++)
            {
                Vector3 b=face.Points[i],cc=face.Points[i+1];
                Vector3 nearest=ClosestPointOnTriangle(point,a,b,cc);
                float distance=Vector3.DistanceSquared(point,nearest);
                if(best!=null&&distance>=best.Value.Distance*best.Value.Distance)continue;
                Vector3 normal=Vector3.Cross(b-a,cc-a);
                if(normal.LengthSquared()<1e-8f)continue;
                normal=Vector3.Normalize(normal);
                best=new(nearest,normal,face.ObjectId,MathF.Sqrt(distance));
            }
        }
        return best;
    }

    public static void SnapToSurface(MapDefinition definition,ISet<Guid> ids,
        Func<Vector3,IReadOnlyList<MapViewportFace>> candidates,bool align=false)
    {
        foreach(var item in Selected(definition,ids))
        {
            float bottom=Bounds(item,1).Min;
            Vector3 anchor=new(item.Position[0],bottom,item.Position[2]);
            SurfaceContact? contact=NearestSurface(anchor,candidates(anchor),ids);
            if(contact==null)continue;
            Vector3 delta=contact.Value.Point-anchor;
            item.Move(new[]{delta.X,delta.Y,delta.Z});
            if(align&&item.Value is MapGeometry geometry)
            {
                Vector3 normal=contact.Value.Normal;
                if(normal.Y<0)normal=-normal;
                geometry.Transform.Rotation=QuaternionBetween(Vector3.UnitY,normal);
            }
        }
    }

    private static float[] QuaternionBetween(Vector3 from,Vector3 to)
    {
        from=Vector3.Normalize(from);to=Vector3.Normalize(to);
        float dot=Math.Clamp(Vector3.Dot(from,to),-1,1);
        Quaternion q;
        if(dot>0.9999f)q=Quaternion.Identity;
        else if(dot<-0.9999f)q=Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI);
        else
        {
            Vector3 axis=Vector3.Normalize(Vector3.Cross(from,to));
            q=Quaternion.CreateFromAxisAngle(axis,MathF.Acos(dot));
        }
        return new[]{q.X,q.Y,q.Z,q.W};
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        Vector3 ab=b-a,ac=c-a,ap=p-a;
        float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0&&d2<=0)return a;
        Vector3 bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
        if(d3>=0&&d4<=d3)return b;
        float vc=d1*d4-d3*d2;
        if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);return a+v*ab;}
        Vector3 cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
        if(d6>=0&&d5<=d6)return c;
        float vb=d5*d2-d1*d6;
        if(vb<=0&&d2>=0&&d6<=0){float w=d2/(d2-d6);return a+w*ac;}
        float va=d3*d6-d5*d4;
        if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0)
        {float w=(d4-d3)/((d4-d3)+(d5-d6));return b+w*(c-b);}
        float denom=1/(va+vb+vc),v2=vb*denom,w2=vc*denom;
        return a+ab*v2+ac*w2;
    }

    public static float? FloorBelow(Vector3 point, IReadOnlyList<MapViewportFace> faces, ISet<Guid>? excluded = null)
    {
        float? height = null;
        foreach (var face in faces)
        {
            if (!face.Solid || excluded?.Contains(face.ObjectId) == true || face.Points.Length < 3) continue;
            var a = face.Points[0];
            for (int i = 1; i < face.Points.Length - 1; i++)
            {
                var b = face.Points[i]; var c = face.Points[i + 1];
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() < .00001f || Vector3.Normalize(normal).Y < .7f) continue;
                float y = a.Y - (normal.X * (point.X - a.X) + normal.Z * (point.Z - a.Z)) / normal.Y;
                if (y > point.Y || height.HasValue && y <= height.Value) continue;
                var p = new Vector3(point.X, y, point.Z);
                if (Vector3.Dot(Vector3.Cross(b-a, p-a), normal) >= -.0001f
                    && Vector3.Dot(Vector3.Cross(c-b, p-b), normal) >= -.0001f
                    && Vector3.Dot(Vector3.Cross(a-c, p-c), normal) >= -.0001f) height = y;
            }
        }
        return height;
    }
    public static void Array(MapDefinition definition, ISet<Guid> ids, int count, Vector3 spacing, bool radial = false)
    {
        if (count is < 1 or > 256 || !float.IsFinite(spacing.X) || !float.IsFinite(spacing.Y) || !float.IsFinite(spacing.Z))
            throw new ArgumentOutOfRangeException(nameof(count));
        var originals = Selected(definition, ids);
        var center = originals.Length == 0 ? Vector3.Zero : originals.Select(o => new Vector3(o.Position[0], o.Position[1], o.Position[2])).Aggregate(Vector3.Zero, (a,b) => a+b) / originals.Length;
        for (int i = 1; i <= count; i++)
        {
            var existing = MapObjects.All(definition).Select(o => o.Id).ToHashSet();
            MapObjects.Duplicate(definition, ids);
            foreach (var item in MapObjects.All(definition).Where(o => !existing.Contains(o.Id)))
            {
                var delta = spacing * i - new Vector3(1, 0, 1);
                if (radial)
                {
                    float angle = MathF.Tau * i / (count + 1);
                    var original = new Vector3(item.Position[0] - 1, item.Position[1], item.Position[2] - 1);
                    var desired = center + Vector3.Transform(original - center + spacing, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
                    delta = desired - new Vector3(item.Position[0], item.Position[1], item.Position[2]);
                }
                item.Move(new[] { delta.X, delta.Y, delta.Z });
            }
        }
    }
}
